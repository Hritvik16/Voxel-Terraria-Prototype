# Design plan — contiguous dense-brick allocation

**Status: PLAN ONLY, awaiting §0.3 human review. Nothing implemented.**
Branch `fix/brick-upload-contiguity`, off `phase4-acceptance` @ 93945bc.
Written 2026-08-31.

**Why this needs review at all:** it changes `BrickDataPool`'s allocation
contract, which §0.3 lists under "chunk/brick/clipmap memory layout" — the
class of change that "can make old saves unloadable or silently desync state".

---

## 1. The problem, in one line

`TransferToSharedPool` allocates a chunk's dense bodies **one slot at a time**
(`StreamManager.cs:685`), so after eviction churn a chunk's ~460 bodies land on
scattered indices. `TerrainClipmap.UploadDirtyBrickBodies` can only merge
**consecutive** slots into one `SetData`, so scattered slots become many small
driver calls.

Measured (2026-08-31, release standalone):

| | p99 | max |
|---|---|---|
| dense slots uploaded / frame | 3308 | 4832 |
| runs (`SetData` calls) / frame | 241 | 1084 |
| **runs/slots** | **0.224** | 0.224 |

Average run ~4.5–13.7 bricks (2.3–7 KB per call). `brick bodies` is the
dominant upload phase at 0.29–1.15 ms p99, against a 1.0 ms total budget.

---

## 2. Proposed change to the allocation contract

**Additive. `Alloc()` and `Free(int)` keep their exact current semantics.**

Two new methods:

```
bool TryAllocRange(int n, out int baseIndex)   // n contiguous slots, or false
void FreeRange(int baseIndex, int n)           // return a run wholesale
```

`TransferToSharedPool` gains a counting pass, then one `TryAllocRange` for the
chunk's dense-brick count, copying bodies into `base+0 .. base+n-1` in brick
order. **If `TryAllocRange` returns false it falls back to the existing
per-slot loop, unchanged.** Contiguity becomes an optimisation, never a
precondition.

Internally the free list changes from a flat LIFO stack of slots to a set of
free **runs** `[start, length)`, merged on insert:

- `Alloc()` — take one slot from the most-recently-freed run (preserves LIFO
  observably; see §7).
- `TryAllocRange(n)` — best-fit over the run set.
- `Free(i)` — insert `[i,1)`, merge with adjacent runs.
- `FreeRange(b,n)` — insert `[b,n)`, merge.

### Why not the thing that already failed

Free-list **defragmentation** failed because it sorted the free list and
streaming immediately re-scattered it: `Free` and `Alloc` interleave
continuously, so the ordering was destroyed before it was consumed.

This design has a structurally different dependency. **Contiguity is
established at allocation and persists for the chunk's lifetime.** It never
depends on the free list being in any particular order at a later moment. The
free list's job is only to *find* a run once, at admission.

It is also **self-reinforcing rather than self-defeating**: chunks are the unit
of both allocation and eviction. A chunk allocated as one run, when evicted,
returns that same run — so the dominant churn cycle regenerates the exact
structure it consumes, instead of eroding it. That is the specific property
defragmentation lacked, and it is the load-bearing claim of this design. **If
review doubts one thing, doubt this.** §9 lists the measurement that falsifies
it cheaply.

---

## 3. Everything that depends on the current contract

Every `Alloc`/`Free` call site in the tree:

| site | role | affected? |
|---|---|---|
| `StreamManager.cs:685` `_pool.Alloc()` | **streaming admission — the hot path** | **yes — becomes `TryAllocRange` + fallback** |
| `StreamManager.cs:698` `scratch.pool.Free` | returns scratch slot | no |
| `ChunkStore.cs:381` `_brickPool.Alloc()` | `SetVoxel` densifies one brick | no (single-slot path unchanged) |
| `ChunkStore.cs:254` `_brickPool.Free()` | **evict frees per brick in a loop** | **yes — should free as runs (§4)** |
| `Coalescer.cs:44` `brickPool.Free()` | brick collapsed to uniform | no, but punches holes (§5) |
| `DeltaCodec.cs:471/463` | delta decode alloc/free | no |
| `GenerateChunk.cs:109/580` | generation into **scratch** pools | no (per-worker pools, never uploaded) |
| `CascadeTierPool.cs:315/304/351` | cascade tiers' **own** pools | no (separate instances; tier bodies upload through a different path) |
| `Phase4AcceptanceRig.cs:1075` | test teardown | no |

Nothing outside `BrickDataPool` reads the free list. No caller assumes
anything about *which* index it receives.

---

## 4. Eviction must free runs, or the structure shreds

`ChunkStore.EvictChunk` (`ChunkStore.cs:245-258`) frees each dense brick
individually. Left alone, a chunk allocated as one run would be returned as
~460 single-slot inserts and only re-merge if the merge logic is perfect.

Two options, and I recommend the second:

1. **Store `(base,count)` on `Chunk`** and free the run directly. Fastest, but
   `Chunk` is A.2 — a §0.3-listed layout. Adding a field is additive and CPU-only
   (A.2 is not GPU-mirrored), but it is still a layout change and would need to
   be called out as such in review.
2. **Detect runs at evict time.** Collect the chunk's dense indices, sort,
   emit maximal consecutive spans as `FreeRange` calls. `O(n log n)` on ~460
   entries per eviction, allocation-free if the scratch buffer is preallocated.
   **No layout change, no §0.3 surface beyond the allocator itself.**

Recommending (2): it keeps the blast radius inside `BrickDataPool` +
`ChunkStore.EvictChunk`, and eviction is not the phase under budget pressure.

---

## 5. Fragmentation pressure, and §3.6

**When no run of size `n` exists:** `TryAllocRange` returns false and the
caller uses the existing per-slot loop. That chunk uploads as scattered runs —
exactly today's behaviour, no worse. **Allocation never fails while free slots
exist**, so §3.6's guarantee ("the triggering edit always succeeds — you push
the eviction radius inward, never fail") is preserved unchanged. The LRU
pool-pressure valve is untouched and still fires on total occupancy, which this
change does not affect.

**No compaction.** Moving a live brick would require rewriting its handle *and*
its GPU mirror region, which is exactly the CPU/GPU desync risk §0.3 guards.
Explicitly out of scope.

**Expected fragmentation behaviour:** the steady state is ~729 resident chunks
cycling in and out at the window edge. Each admission takes one run; each
eviction returns one. Sizes vary (dense-brick count per chunk varies with
terrain), so external fragmentation will accumulate — bounded, because peak
occupancy is 336,731 of a 500,000 cap (67%), leaving 163,269 slots of slack for
runs to be found in. The fallback makes the worst case "today's performance",
not a failure.

---

## 6. Interaction with coalescing (§4.5)

`Coalescer` frees a single brick when its 512 bytes become uniform. Inside a
chunk's run that punches a one-slot hole.

- **Correctness:** unaffected. The hole is a free slot like any other.
- **Upload:** the chunk's remaining dirty bodies now form two runs instead of
  one. Degradation is proportional to how many bricks coalesce, and coalescing
  only ever *reduces* the number of dense bodies to upload — fewer slots and
  slightly more runs, which is a net win on bytes.
- **Full collapse:** when a chunk goes fully uniform, every brick frees and the
  whole run returns and re-merges. The §4.5 background scan therefore *helps*
  this design rather than fighting it.

---

## 7. The CPU/GPU sync contract (§3.9) — unchanged, and this matters

The GPU mirror is indexed **positionally** from the pool slot:
`BrickDataBuffer` region for slot `i` is uints `[i*128, i*128+128)`
(`TerrainClipmap.WriteBrickRun`, `ClipmapValidator.cs:216`). The handle stores
the slot in `[29:0]` (A.3) and 500,000 fits in 30 bits with room to spare.

**This change alters only *which* slot a brick occupies, never the rule mapping
slot → GPU region.** No addressing math changes, `GpuIndexOf` is untouched,
`CoordMath` is untouched, and no serialized format is touched — delta records
store brick *content* and `brickIndex`, never pool slots. So §3.9 is not
modified. ClipmapValidator is nonetheless the check that this held, and it is
the gate that reverts the change (§9).

---

## 8. Tests that must still pass unchanged

`BrickDataPool` is referenced by 12 EditMode test files. The contract-sensitive
one:

- **`MemoryModelTests.Pool_DoesNotLeak_AfterAllocAndFree`** asserts
  `Assert.AreEqual(poolIndex, newIndex)` — free one slot, next `Alloc()` returns
  **that same slot**. This is a hard LIFO assertion on single-slot behaviour.
  The design must keep `Alloc()` preferring the most-recently-freed run, or this
  test fails and correctly so.
- `MemoryModelTests.Coalescer_ReclaimsMemory_And_ReturnsToUniform` — free-count
  accounting.
- `StreamingTests`, `GenerationTests`, `LODDownsamplerTests`,
  `CascadeTierPoolTests`, and the five `Raymarch*Tests` families build synthetic
  pools; they must be unaffected because they use single-slot `Alloc`.

**New tests to add:** `TryAllocRange` returns genuinely consecutive indices;
returns false rather than overlapping when no run fits; `FreeRange` round-trips
to the same free count; run merging joins adjacent frees; a fragmentation fuzz
(random alloc/free cycles) ending with free-count back at capacity.

---

## 9. Expected gain, with the arithmetic

`MAX_CLIPMAP_CHUNKS_PER_FRAME = 16`, so at p99 the 3308 slots and 241 runs are
~207 slots and ~15 runs per chunk.

With one run per chunk: **16 runs/frame instead of 241 — a ~15x reduction.**

Per-call overhead implied by the current numbers is ~1 µs (241 calls ≈ 0.24 ms;
1084 calls ≈ 1.08 ms — both match the measured 0.29–1.15 ms `brick bodies`
range). At 16 calls the phase should fall to ~0.02 ms.

Predicted upload p99: `0.98 − 0.9×0.29 ≈ 0.72 ms` (Gate C) and
`1.63 − 0.9×1.15 ≈ 0.60 ms` (Gate E worst). **Both under the 1.0 ms budget**,
which would close the criterion.

**The cheap falsification, to run first:** the fragmentation counter added last
session already reports runs/slots per frame. If after implementation
runs/slots does **not** drop toward ~0.005 (1 run per ~207 slots), the
self-reinforcing claim in §2 is wrong and the change should be abandoned
before any timing argument is made.

---

## 10. Rollback

Single commit, revertable in isolation:
- `BrickDataPool`: two added methods + internal free-list change.
- `StreamManager.TransferToSharedPool`: counting pass + range request + fallback.
- `ChunkStore.EvictChunk`: run-detecting free.

`git revert` restores the flat stack; nothing persists on disk that encodes
allocation order (deltas store content and `brickIndex`, never slots), so a
revert needs no data migration and worlds saved under either version load
identically.

**Immediate revert triggers, no performance number consulted:**
ClipmapValidator `mismatchesInCleanChunks` ≠ 0, any LOST UPDATE, Gate D delta
round-trip failure, or EditMode below 181.

---

## 11. Risks I would want review to weigh

1. **The free-list rewrite is the real risk**, not the call-site changes. A
   merge bug leaks slots or double-issues one; the latter would desync CPU and
   GPU silently — the §0.3 failure class. Mitigation: the fuzz test in §8 and
   the validator gate in §10.
2. **External fragmentation is unbounded in theory.** Mitigated by the fallback
   and 33% headroom, but not eliminated. A long soak is the only real evidence,
   and §9's runs/slots counter is the early-warning signal.
3. **§0.1 invariant 9** ("the dumbest implementation that works, wins") argues
   against a run allocator on principle. The counter-argument is that the
   measured cause is per-call overhead and no simpler mechanism addresses it —
   defragmentation was the simpler one and it failed for a reason now
   understood. If review disagrees, the honest alternative is to accept
   0.98–1.63 ms and record §4.3 as unmet.
