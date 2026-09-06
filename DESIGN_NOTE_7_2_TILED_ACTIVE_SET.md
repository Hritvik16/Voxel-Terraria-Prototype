# Design note — §7.2's active set, so §7.4's radius is reachable at scale

**Status: PROPOSAL. No code written against it yet.** Written 2026-09-05 as
Step 1 of a scoped brief; Step 2 implements whatever survives review.

---

## 0. The problem, measured

`FluidGpuSimulation` allocates **one dense per-cell buffer set** for its region:
`claim`, `slotAt`, `reacted`, `wakeMark`, four bytes each — **16 B/cell**,
confirmed identical at three sizes (`run-fluid-scale.sh` step 3):

| region | cells | per-cell buffers | half-diagonal |
|---|---|---|---|
| 64³ | 262,144 | 4.0 MB | 55 vox |
| 128³ | 2,097,152 | 32.0 MB | 110 vox |
| 256³ | 16,777,216 | 256.0 MB | 221 vox |

`CSClear`, `CSCommit` and `CSWakeScan` each dispatch over **every cell every
tick**, so per-tick cost also scales with region volume regardless of how much
fluid is live.

For §7.4's radius to gate anything, the region must be larger than the radius.
`FLUID_ACTIVE_RADIUS_VOXELS = 1280` (128 m) needs a region whose half-diagonal
reaches 1280 → the smallest power-of-two edge is **2048** → 8,589,934,592 cells
→ **137 GB** of per-cell buffers.

**This is not a tuning problem.** At any region size that can exist, the radius
is at least 5.8× the region's own half-diagonal, so `WithinActiveRadius` is
always true and `BeyondSleepRadius` never is: §7.4's gate is inert by
construction, and the "Noita-density chaos" goal has no substrate under it.

---

## 1. Two facts that change the shape of this problem

Both were established by reading the shipped code, and both are load-bearing
for everything below.

### 1.1 The op-list is ALREADY world-addressed

`EmitOp(int3 dst, …)` is called with world coordinates — `RegionVoxel(index)`
returns `local + _RegionOriginVoxels`. On the CPU side `FluidOpListReadback`
consumes them as world coordinates too:

```csharp
if (!_store.IsResident(CoordMath.VoxelToChunk(op.Dst)) || …)
if (_store.GetVoxel(op.Dst) != op.ExpectedAtDst) …
_store.SetVoxel(op.Dst, op.NewMaterial);
```

Nothing in the op-list, its header, `CSCommit`'s append, the readback, or any
rig that parses it encodes a region index. **The §7.2 op-list format does not
change under any option below**, which removes the single largest source of
risk the brief anticipated.

### 1.2 The cell↔index mapping is three functions

`InRegion`, `RegionIndex`, `RegionVoxel` (FluidCA.compute:247–268). Every other
kernel works in world coordinates and calls these. The addressing change is
therefore *local*, not a rewrite of the CA's logic.

---

## 2. Candidate (c) — many fixed dense regions. **Rejected, with numbers.**

Partially built already, and it works: `run-fluid-scale.sh` step 2 measured 2, 4
and 8 simultaneous regions with flat 26.6% per-region slot utilisation and no
cross-region interference. Every region returned its slots.

**It does not solve the scaling problem, because it does not change the unit
cost.** Covering a 1280-voxel radius means covering ~8.8×10⁹ voxels of sphere.
Whether that is one 2048³ region or 268,000 regions of 64³, the per-cell buffers
still cost 16 B for every cell covered: **the same 137 GB, now spread across
268,000 heavyweight objects**, each with its own slot pool, counter buffers,
op-list ring, and a CPU-side `Tick()` call per region per frame.

Multiple regions are genuinely useful for *disjoint* pools of fluid — which is
what step 2 measured and what the Playground uses. They are not a scaling
answer, because the thing that has to become sparse is **cells**, and (c) keeps
cells dense inside every unit it allocates.

**(c) is (a) at the wrong granularity and without a pool.**

---

## 3. Candidate (b) — GPU-side sparse hash keyed by tile coordinate. **Rejected on risk.**

Technically possible: Metal and HLSL both expose `InterlockedCompareExchange` on
`RWStructuredBuffer<uint>`, which is enough for open addressing with linear
probing.

Rejected for three reasons, in descending order of seriousness:

1. **It puts atomics where the design forbids them.** FluidCA.compute's header
   states the invariant explicitly: *"The claim is an ORDINARY STORE, not
   InterlockedMin. §7.3: a single aligned store."* There is exactly one
   `InterlockedAdd` in the allocation path and none in the claim path. A hash
   *lookup* is atomic-free, but hash *insertion* is not — and insertion happens
   during `CSWakeScan`/`CSPromote`, i.e. in the tick. Candidate (a) needs **no
   new atomics at all** (§5.4).
2. **Growth has no good answer on the GPU.** A load-factor overflow mid-tick
   means either failing the insert (fluid silently does not wake — the exact
   class of bug this project has now fixed twice) or a resize, which cannot
   happen inside a dispatch.
3. **It buys nothing over a dense ring.** The lookup a hash provides in O(1)
   expected, a toroidal ring provides in O(1) *worst case* with two shifts and a
   mask — and the project already enforces power-of-two ring dimensions
   (`§6.2 phantom terrain`, the aliasing bug that made the rule) and has a
   proven implementation of exactly this in `ChunkStore`.

A hash is the right structure when the key space is unbounded and unpredictable.
Here it is bounded by the active radius and perfectly predictable.

---

## 4. Candidate (a) — TILE THE ACTIVE SET. **Recommended.**

The same two-level shape the project already proves at scale: a **sparse
directory** over a bounded window (like `ChunkStore`'s toroidal ring) plus a
**hard-capped pool of fixed-size tiles** (like `BrickDataPool`), with tiles
freed the moment they hold no live fluid (§3.10's `Volatile` rule, immediate,
not the lazy §4.5 coalescer).

### 4.1 Where the saving actually comes from — stated precisely

**Not from tiling the radius.** A sphere of radius 1280 contains ~8.8×10⁹
voxels however it is sliced; instantiating a tile for every tile *within the
radius* costs the same 137 GB.

**The saving is sparsity: only tiles that contain fluid exist.** §2.5 puts peak
active fluid at ~500,000 voxels. Those occupy a tiny, spatially clustered
fraction of the radius volume, so the resident tile count is bounded by *how
much fluid there is*, not by how far the radius reaches.

The radius then does its real job — bounding **simulation cost** — by gating
which tiles stay resident, exactly as the chunk load/evict radius does. Memory
becomes O(fluid present), capped; it stops being O(r³) entirely.

### 4.2 Concrete sizing at the SHIPPED radius

Tile edge **T = 32 voxels** (32,768 cells → 512 KB/tile at 16 B/cell).
Directory: a toroidal ring of tile coordinates, **128³**, one `uint` slot index
per entry.

- Ring extent = 128 × 32 = 4,096 voxels = **±204.8 m**, covering the 128 m
  radius with margin.
- Directory cost: 2,097,152 × 4 B = **8.4 MB, fixed**.
- Tile pool: cap × 512 KB. At **cap = 512 tiles → 256 MB**, holding 16.8M cells
  of simultaneous fluid capacity — ~33× §2.5's 500,000 target.

**Total at the shipped radius: ~264 MB, hard-capped, independent of the radius.**
Against 137 GB. That is the headline number this design exists to produce, and
it is arithmetic over a measured 16 B/cell, not an estimate.

`T = 16` is the alternative (64 KB tiles waste less on sparsely-filled tiles,
but the ring must be 256³ = 67 MB). Both are tunable constants; T = 32 is
recommended as the smaller total fixed cost and the simpler thing to build.

**T must divide 128.** Both 16 and 32 do — see §5.2, where this stops being a
convenience and becomes a correctness property.

### 4.3 Addressing

Bitwise throughout, per §0's integer rule — no float division, no modulo:

```
tileCoord   = v >> TILE_SHIFT                       // 32 -> shift 5
ringIndex   = (tileCoord & RING_MASK) linearised    // power-of-two ring
tileSlot    = TileDirectory[ringIndex]              // or NO_TILE
cellIndex   = tileSlot * TILE_CELLS + localIndex(v & TILE_MASK)
```

`InRegion` becomes "the tile exists": `TileDirectory[ringIndex] != NO_TILE`,
plus the ring-identity check `ChunkStore.GetChunk` already models (an entry is
only yours if its stored tile coordinate matches — without that, a ring aliases
silently, which is precisely the §6.2 bug).

Only `InRegion`, `RegionIndex`, `RegionVoxel` and the four buffer accesses
change. The CA's logic — claim, intent hierarchy, commit, sweep, recycle — does
not.

### 4.4 Tile lifetime

- **Allocated CPU-side**, from wake requests and edits (see §5.4 for why this
  matters). The CPU already owns promotion via `RequestWake`/`FluidWakeQueue`.
- **Freed immediately** when a tile's live-slot count reaches zero — §3.10's
  `Volatile` rule verbatim: *"when its active-fluid count drops to zero, its
  body is returned to the free-list immediately — not on the background
  sweep."* The eviction-spiral that rule exists to prevent is the same one a
  lazily-freed tile pool would reintroduce.
- **Pool pressure** demotes the coldest tiles outside the wake radius first,
  mirroring §3.6's LRU valve — the mechanism this project drove to its cap and
  proved this week (peak 426,720 vs a 425,000 mark, 401–544 evictions, cap never
  exceeded).

### 4.5 Per-tick dispatch

`CSClear`/`CSCommit`/`CSWakeScan` dispatch over **resident tiles' cells**, not
the radius volume, via `DispatchIndirect` with an args buffer written from the
active-tile count. Cost becomes proportional to fluid present.

**NOT MEASURED and NOT CLAIMED:** the GPU-lane cost of this. §2.2 budgets the
fluid CA at ≤3.5 ms and this workflow cannot attribute GPU stages at all
(AMENDMENT_8_9 §0 Rules 1–2; AMENDMENT_8_10's 2.6–2.7× inflation). The design
makes the cost *proportional to the right thing*; whether the constant is
affordable is a separate, currently unanswerable question, and this note does
not pretend otherwise.

---

## 5. What the brief requires this design to address, explicitly

### 5.1 Op-list addressing (§7.2)

**Unchanged.** Per §1.1 the op-list already carries world coordinates and
contains no region index. `FluidWriteOp`, the header convention, `CSCommit`'s
append, `FluidOpListReadback.Apply`, and every rig that parses the op-list are
untouched. This is the single biggest de-risking fact in the proposal and it was
verified in the code, not assumed.

### 5.2 §9.4's residency guard — and a structural improvement

Today the region is a fixed box that **straddles chunk boundaries by design**.
That is exactly the shape that destroyed 7 water voxels of 52 in Phase 5d, with
`StaleOpsDropped` reading 0 because nothing looked stale.

**With `T` dividing 128 and tiles aligned to multiples of `T`, a tile can never
span a chunk boundary.** Chunk edges are at multiples of 128; a tile occupies
`[kT, (k+1)T)` which lies wholly inside one chunk. So residency becomes decidable
*per tile*: a tile belongs to exactly one chunk, and if that chunk is not
resident the tile is not admitted at all.

This does not replace §9.4's guard — `FluidOpListReadback` keeps checking
`IsResident` on both halves of every op, because an op is decided a frame or
more before it is applied and the chunk can be evicted in between. The tile
alignment removes the *class* of straddle; the guard still handles the *timing*.
Both, not either.

**Stated as a test, not an assertion:** Phase 5d's rig must be re-pointed at
this — a pour that crosses a chunk edge, one side evicted mid-flight, mass
conserved. §6 lists it.

### 5.3 §9.5's admission guard

A single-frame teleport larger than the window makes `ChunkStore` refuse the
insert. Under tiling the same event slides the **tile ring origin**: every tile
whose coordinate leaves the ring is freed to the pool, and nothing is admitted
until its chunk is resident (§5.2). This is the chunk ring's own proven
behaviour at a different scale, and it degrades to "no fluid simulates until
terrain arrives", which is what already happens when a whole region is evicted.

The failure mode to test is a teleport *during* op flight: ops in flight carry
world coordinates and are re-validated against residency on arrival, so they
drop rather than write into a re-based ring. That is the existing §9.4 guard
doing its job, and it is why §5.1 mattering is not incidental.

### 5.4 Single-writer, and no atomics in the claim path

**Preserved, and this constrains the design rather than following from it.**

- The **claim stays a plain aligned store** into the tile's own cell array. Tile
  indirection changes the *address*, not the write.
- **Tile allocation is CPU-side.** A GPU-side allocator would need an atomic in
  `CSWakeScan`/`CSPromote` — inside the tick, in the path the header forbids.
  The CPU already drives promotion, so tiles are created from wake requests and
  edits before the dispatch that uses them.
- **Consequence, stated honestly:** fluid flowing into a cell whose tile does not
  yet exist waits **one tick**. The op still emits (world-addressed, terrain byte
  authoritative), the CPU applies it and calls `RequestWakeNeighbourhood`, and
  the tile is created for the next tick. That is the *existing* §7.2
  bounded-latency model, not a new compromise — but it is a real one-tick
  boundary effect at tile edges and it needs a test (§6).
- `ChunkStore` remains the sole terrain writer. Nothing here adds a CPU writer.

---

## 6. What happens to every existing fluid proof

| Proof | Under tiling | Action |
|---|---|---|
| **Phase 5a** `FluidReferenceCPU` + conservation tests | Unaffected — separate CPU implementation, its own addressing, still the oracle | none |
| **Phase 5b** GPU-vs-CPU steady-state comparison | Compares **terrain bytes** at rest, not internal state | re-run as-is; must still pass |
| **Phase 5c** edit-stress (14 cases × 3 orderings) | Drives edits, checks terrain | re-run as-is |
| **Phase 5d** streaming × fluid | **Meaning changes.** It exists because the region straddles chunk edges; tiles cannot. Still valuable — it now tests tile-level admission and the in-flight timing case — but it is proving a *different claim* | **rewrite the claim, re-run** |
| **`run-fluid-activity.sh`** | Steps assert on `RegionOriginVoxels`/`InRegion` semantics | update addressing assertions; keep the rate check from Step 0 |
| **`run-fluid-scale.sh`** | Step 3 measures exactly the thing this design changes | rewrite as the acceptance measurement (§7) |
| **Phase 6 sandbox** (integrated) | Uses the arena through the public API | re-run as-is |
| **`FluidActiveRegionTests`** (§7.4 policy) | Pure radius policy, no addressing | unaffected |
| **`run-playtest-bugs.sh`** | §7.4 wake-on-approach, world-coordinate assertions | re-run as-is |

"Re-run as-is" is a claim to be *checked*, not assumed — anything that fails
gets reported as a finding, not quietly updated until green.

---

## 7. Acceptance bar for Step 2

1. **The shipped radius is reachable with a bounded, measured footprint.**
   `FLUID_ACTIVE_RADIUS_VOXELS = 1280`, memory reported from actual buffer
   allocations (not arithmetic), demonstrated constant as the radius grows.
2. **The radius demonstrably bites** — tiles outside it demote, tiles inside wake
   — which it cannot do today at any affordable size.
3. **§9.4 and §9.5 re-verified explicitly** under the new structure.
4. Every rig in §6 re-run, with each result classified: passed as-is, updated and
   why, or failed.

---

## 8. Risk, stated plainly

This is a change to the **shipped fluid substrate**, which currently carries
proofs from four phases. The addressing change is confined (§1.2) and the op-list
is untouched (§1.1), which is what makes it tractable at all — but "confined" is
not "small", and the failure modes here are the silent kind this project has been
bitten by three times (§6.2 aliasing, §9.4 residency, the scratch-pool leak).

Two things are deliberately **not** claimed by this note:
- that the tiled CA is **faster**, or meets §2.2's ≤3.5 ms GPU budget — that lane
  is unmeasurable here and no number is offered;
- that 512 tiles is the right cap. It is derived from §2.5's target with ~33×
  headroom, and like `BRICK_POOL_HIGH_WATER_FRACTION` before it, it is an
  assumption until a rig drives it to its limit.
