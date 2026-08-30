# Design: Job-System-compatible chunk generation

**Status:** Stages 0-4 and D1/D2 implemented on branch `job-system-chunk-storage`.
Stage 5 NOT started. See §8 for the Stage 5 brief and its pass/fail criteria.
**Date:** 2026-08-29
**Decision owner:** you. This document exists so that decision is made with the
frozen surfaces named explicitly, per §0.3.

---

## 0. Why this is on the table at all

The stutter investigation established that **occupancy, not per-thread work, starves
the main thread**. Seven raw `Thread` workers compete with Unity's main and render
threads for four performance cores, and nothing that changes *what* those threads do
has moved p99:

| lever | result |
|---|---|
| screenshots, leg boundaries, GC, upload path | no effect |
| `ThreadPriority.BelowNormal` | no effect (Mono does not map it on macOS) |
| `pthread` QoS `UTILITY` | no effect (accepted, scheduled the same) |
| Burst: generation 5.58 → 2.44 ms/chunk | **no effect on p99** |
| worker count 7 → 4 → 2 | 961 → 846 → 326 ms, deficit 0 → 19 → 202 |

Only removing threads moves it, and that trades stutter for terrain popping.

**The one argument left for this conversion is scheduling.** Unity's job workers are
co-scheduled with the main thread rather than competing with it as opaque OS threads.
That is a different mechanism from anything tested so far. It is *not* a throughput
argument — Burst already captured the throughput win, and it did not help the stutter.

This document deliberately does **not** argue the conversion is worth doing. It
establishes what it would cost and what it would risk.

---

## 1. What this touches, and what it does not

### 1.1 Chunk / brick memory layout — **CHANGES** (§0.3 review required)

Today (`StructHeaders.cs`):

```csharp
public class Chunk                 // managed CLASS
{
    public int3 coord;
    public bool isUniform;
    public byte uniformMaterial;
    public BrickHandle[] bricks;   // managed array, 4096 entries, 16 KB
    public bool dirty, deltaDirty;
}
```

A Burst job cannot touch either the class or the managed array. **This is the only
genuinely frozen thing the conversion must disturb**, and §0.3 names it directly:
*"the chunk/brick/clipmap memory layout"*.

**Blast radius, measured:** 84 `.bricks` access sites across 19 files —
`ChunkStore` (16), `GenerateChunk` (14), `DeltaCodec` (8), `StreamManager` (7),
plus validators, coalescer, cascades, and tests.

Section 5 proposes an option that avoids changing this at all. Read that before
accepting the blast radius as necessary.

### 1.2 Delta serialization format (D.1) — **UNCHANGED**

Verified by reading `DeltaCodec.Encode`. The wire format is:

```
header:  chunkCoord.x, .y, .z (int)  seed (uint)  FORMAT_VERSION (ushort)  recordCount (ushort)
record:  brickIndex (ushort)  kind (byte)  then either  material (byte)
                                                 or     512 raw body bytes
```

**No pool index, no array reference, and no in-memory layout detail is ever
serialised.** The format describes bricks by *index, kind, and content*. Any storage
that can answer "for brick i: is it dense, what material, what are its 512 bytes"
produces byte-identical output.

`FORMAT_VERSION` does **not** need to be bumped.

### 1.3 Content-hash function — **UNCHANGED**

`ChunkContentHash.Hash` is already representation-independent *by explicit design*.
Its own comment records why: an earlier version mixed per-brick dense/uniform flags
and broke when the coalescer legally collapsed a brick.

It folds effective voxel bytes only — a uniform brick folds its material 512 times,
a dense brick folds its body. Pool slot indices never enter the hash. Storage changes
cannot move it.

### 1.4 Single-writer rule (§0.1.5, §3.2) — **PRESERVED, and structurally easier**

The current design already has the exact seam a job conversion needs. From
`StreamManager.cs:641`:

```csharp
// Transfer scratch -> shared pool. This is the only place
// shared pool allocation happens for generated chunks.
TransferToSharedPool(c.chunk, c.scratch);
```

Workers own a private `ScratchContext` (`BrickDataPool(BRICKS_PER_CHUNK)`,
`ChunkHandleAllocator(2)`, scratch body, downsample buffers) and never touch shared
state. The main thread drains a `ConcurrentQueue` and performs the single transfer.

A job conversion keeps this shape exactly: jobs write to per-job native scratch, the
main thread transfers. **No lock is introduced and none is needed.** This is the part
of the existing design that makes the conversion tractable at all.

### 1.5 `BrickDataPool` allocation contract — **CHANGES only under Option B**

```csharp
public int Alloc()
{
    if (_freeCount == 0)
        throw new InvalidOperationException("BrickDataPool exhausted...");
    return _freeStack[--_freeCount];
}
```

Two problems for jobs: it **throws** (Burst has no useful exception support), and it
is a stateful managed class mutated by a single owner.

Note the storage is *already* native (`NativeArray<byte> _brickData`,
`NativeArray<int> _freeStack`) — only the wrapper is managed. Under Option A the
pool is never touched from a job, so the contract stands unchanged.

### 1.6 Frozen public API — **UNCHANGED**

`IWorldQuery.GetVoxel` and `IEditService.SetVoxel` signatures are untouched under
both options. §0.1 invariant 6 forbids redesigning a passed system's public
interface; internals may change.

---

## 2. Do on-disk artifacts survive?

**Yes, under both options. This is not a breaking format change.**

| artifact | survives | why |
|---|---|---|
| `*.delta` files | **yes** | format describes bricks by index/kind/content; no layout detail on the wire (§1.2) |
| `world.meta` | **yes** | untouched — anchors, seeds, seed, sizeClass only |
| Recorded content hashes | **yes** | hash is content-canonical by construction (§1.3) |
| `contentVersionHash` | **yes** | derives from the material/biome tables, not from storage |

**The tripwire that proves it:** `GenerationTests`' determinism and per-voxel oracle
suites, plus Gate A's delta round-trip (`live 0x… == baseline+delta 0x…`) and its
200-single-bit corruption sweep. If any of these change behaviour, the claim above is
wrong and the stage must be reverted rather than reasoned around.

**One caveat worth stating plainly:** this holds only if the new storage preserves
*effective voxel content* per brick index. A change that also altered, say, how
uniform bricks are canonicalised would break hashes — not because of the storage, but
because it changed content. Keep those separate.

---

## 3. Rollback plan

Every stage is a separate commit on a branch, and no stage depends on an
irreversible data migration.

1. **Branch:** `job-system-chunk-storage`, off `main`. `main` is never the working
   branch for this.
2. **Per-stage revert:** each stage is one commit with EditMode green at that commit.
   `git revert <sha>` restores the previous stage. Because stages are ordered so that
   generated output is bit-identical throughout (§4), a revert cannot leave worlds
   half-migrated.
3. **Whole-feature abort:** `git reset --hard <sha of main before branch>`. There is
   no on-disk migration to undo — deltas and `world.meta` written before, during, and
   after are mutually compatible (§2). Worlds generated on the branch remain readable
   by `main`.
4. **The tripwire for "this isn't panning out":** any stage that cannot reach
   bit-identical generation output. That is the stop condition, not a puzzle to solve
   — it means the storage change altered content, which is the §0.3 failure mode this
   whole document exists to avoid.
5. **Known-good reference:** run `2026-08-29_163605` — full five gates, 44 PASS /
   2 FAIL, generation 2.21 ms/chunk. Any stage is compared against this.

---

## 4. Staged implementation order

Each stage must **compile, pass 181/181 EditMode, and produce bit-identical
generation output** before the next begins. Stages 1–3 are useful on their own even
if the project stops there.

### Stage 0 — Oracle harness (no production change)
Add a rig check that hashes N generated chunks and compares against hashes captured
from `main`. Makes "bit-identical" a measured gate rather than an assumption, before
anything moves.
*Verify:* hashes recorded; check passes trivially against itself.

### Stage 1 — `BrickDataPool.TryAlloc`
Add `bool TryAlloc(out int index)` alongside `Alloc()`. `Alloc()` keeps throwing and
keeps its callers. Nothing switches yet.
*Verify:* 181/181; no behaviour change possible — pure addition.

### Stage 2 — Native chunk-result struct
Define a blittable `GeneratedChunk` (a `NativeArray<uint>` of 4096 handles + a
`NativeArray<byte>` body region + `isUniform`/`uniformMaterial`), and a
main-thread converter `GeneratedChunk → Chunk` that allocates from the shared pool.
Not yet produced by a job.
*Verify:* 181/181; converter round-trips a managed `Chunk` through the native form
with identical content hash.

### Stage 3 — Generation writes the native form (still on existing threads)
`ChunkGeneratorFull` fills a `GeneratedChunk` instead of a managed `Chunk`; the
worker converts at the end. The existing `TransferToSharedPool` seam absorbs this.
*Verify:* 181/181, **bit-identical generation** vs Stage 0 hashes, full rig run.
**This is the highest-risk stage — it is where content can silently change.**

### Stage 4 — `[BurstCompile] IJob` producing `GeneratedChunk`
Generation becomes a job, executed with `.Run()` on the existing worker threads.
No scheduling change yet. Proves Burst compiles the whole path.
*Verify:* 181/181, bit-identical, rig run; generation cost measured.

### Stage 5 — Schedule on Unity's job workers (**the actual experiment**)
Replace the raw `Thread` pool with `Schedule()` + `JobHandle` completion drained on
the main thread. This is the only stage that tests the co-scheduling hypothesis, and
the only one that touches the worker/queue handoff.
*Verify:* 181/181, bit-identical, **and the p99 measurement that justifies the whole
exercise.** If p99 does not move here, stages 0–4 were still net-positive cleanups
and stage 5 reverts alone.

### Not in scope
`Chunk.bricks` itself is **not** converted in any stage above. See Option A below.

---

## 5. Two options — and the recommendation

### Option A — native *result*, managed `Chunk` (recommended)
Jobs produce a native `GeneratedChunk`; the existing main-thread transfer converts it
into today's managed `Chunk`.

- `Chunk.bricks` unchanged → **84 call sites untouched**
- `BrickDataPool` contract unchanged → §1.5 not disturbed
- Delta format, content hash, single-writer rule: all unchanged
- **Touches nothing on §0.3's list**
- Cost: one native→managed conversion per chunk, alongside the transfer that already
  happens

### Option B — convert `Chunk.bricks` to `NativeArray<BrickHandle>`
Everything jobs touch becomes native end to end.

- Touches the frozen memory layout (§0.3 review)
- 84 call sites, 19 files
- `Chunk` becoming a struct changes reference semantics across `ChunkStore`,
  `DeltaCodec`, the coalescer, and the validators — a much larger surface than the
  line count suggests
- Buys: no per-chunk conversion, and a path to jobs that mutate resident chunks
  directly (which the single-writer rule currently forbids anyway)

**Recommendation: Option A.** The co-scheduling hypothesis — the only argument left
standing — is fully testable under Option A at Stage 5. Option B's extra cost buys
capabilities (jobs mutating resident chunks) that §0.1.5 does not currently permit,
so it would be paying §0.3 risk for something the architecture forbids using.

If Stage 5 shows p99 unmoved, Option B would not have changed that outcome either —
the scheduling mechanism is identical; only the data plumbing differs.

---

## 6. Honest risks

1. **Stage 5 may show nothing.** Seven job workers can oversubscribe four
   performance cores just as seven raw threads do. Unity's scheduler is *aware* of
   the main thread in a way the OS is not, but "aware" is a mechanism, not a
   guarantee. Budget for the possibility that the answer is "job workers behave the
   same" — that is a real result, and cheaper to obtain via stages 0–4 than by
   Option B.
2. **`.Run()` vs `Schedule()` is the whole experiment.** Stages 1–4 deliver zero
   scheduling change. Anyone reading a green Stage 4 as "the conversion worked" has
   misread it.
3. **Determinism is the failure mode to fear**, not crashes. Bit-identical output is
   gated at every stage for that reason.
4. **The 7-short cave placement** (241 of 248) is rejection-sampling variance, not
   related to this work — don't let it confound a bit-identical comparison; compare
   against a fixed seed and fixed counts.

---

## 7. What I would do first

Stage 0 alone, then stop and re-read the p99 evidence. It costs one commit, changes
no production code, and makes every later claim of "bit-identical" measurable instead
of asserted. Everything after it is optional.


---

# 8. Stage 5 brief — read this before restarting

Written 2026-08-29 at the end of the implementation session, so the next person
does not re-derive it cold.

## Where the branch stands

`job-system-chunk-storage`, pushed, **not merged to main**.

    a3d6452  Stage 0  oracle gate: bit-identical is now measured, not asserted
    4307626  Stage 1  BrickDataPool.TryAlloc, additive
    acc63ff  Stage 2  blittable GeneratedChunk + main-thread converter
    221e710  Stage 3  generation writes the native form
    ebae29c  Stage 4  brick loop becomes a Burst job
    6dbdffe  D1       downsample buffers to native containers
    1f44fce  D2       halving chain Bursted + benchmark path fix

Every stage bit-identical against the Stage 0 oracle, 188/188 EditMode, rig
44 PASS / 2 FAIL (both the known upload-p99 budget). Nothing on §0.3's list has
moved: `Chunk.bricks`, the pool contract, the delta format and the content hash
are all untouched.

## Corrected worker cost — §0's original numbers were wrong

The rig's micro-benchmark was calling the ALLOCATING `DownsampleChunkToTier`,
which rebuilds a 2 MB tier-0 gather per tier. Workers call `PrepareTier0` +
`DownsampleTierFromScratch` against a reused scratch. Measured on the path
workers actually run:

    generation                  2.05 ms/chunk   (was 5.58 pre-Burst)
    downsample                  1.98 ms/chunk   (was reported as 5.02)
      tier-0 gather, managed    0.09
      halving chain, Burst      1.89
    -------------------------------------------
    total worker cost/chunk    ~4.0 ms          (not the ~7.2 previously believed)

Consequence: generation and downsample are ROUGHLY EQUAL, not 1:2. Both are now
Burst job code executed via `.Run()` on the existing raw threads.

**D3 is not worth doing.** The only remaining managed piece is the tier-0
gather, at 0.09 ms of 1.98. Extracting from `GeneratedChunk` natively would buy
at most that, and it was the last piece with any §0.3 adjacency.

## What Stage 5 actually does

Replace the raw `Thread` pool in `StreamManager` with Unity Job System
scheduling:

1. `ChunkFillJob` and `ColumnSampleJob` move from `.Run()` (immediate, calling
   thread) to `.Schedule()` (queued onto Unity's worker pool), chained by
   `JobHandle`.
2. `DownsampleStepJob` follows the same way — it is already a job, so this is
   trivial once the first is done, and it is half the occupancy.
3. The `ConcurrentQueue<LoadCompletion>` drain becomes `JobHandle.IsCompleted`
   polling on the main thread, still bounded by `MAX_CHUNK_LOADS_PER_FRAME`.
4. `TransferToSharedPool` / `TryToChunk` stay exactly where they are — main
   thread, single-writer, unchanged.

**This is the only stage that touches the worker/queue handoff**, which is why
it was held back for a separate decision.

## The hypothesis, stated so it can fail

Unity's job workers are **co-scheduled with the main thread**; raw OS threads
are not. Seven raw threads at normal priority oversubscribe the four
performance cores the main and render threads need, and the main thread gets
descheduled — `mt` reads 4-7 ms on frames that take 1100 ms.

Nothing else has moved p99: screenshots, leg boundaries, GC, upload path,
`ThreadPriority`, pthread QoS, and a 2.3x cut in per-chunk generation cost all
left it unchanged. Only removing threads moved it (961 → 846 → 326 ms for
7 → 4 → 2 workers), and that trades stutter for terrain popping.

## Success looks like

Measured against a 7-worker baseline, App Nap disabled, same world:

- **Gate C frame total p99 drops materially** — call it below ~500 ms, versus
  the ~860-960 ms that has been immovable all session.
- **`load deficit` p50 stays at 0.** A p99 win bought by generating less is the
  worker-count trade again, not co-scheduling.
- **`mt` on the worst frames stays low** while the frames themselves get
  shorter. That is the signature of the main thread being scheduled rather than
  doing less.
- 188/188 EditMode, oracle bit-identical, gates no worse than 44 PASS / 2 FAIL.

## No effect looks like

- **p99 stays in the 800-1000 ms band** with deficit still 0. Unity's workers
  oversubscribe the same four performance cores, and "co-scheduled" turns out
  to be a mechanism rather than a guarantee.
- This is a **real result, not a failed stage.** It closes the last standing
  argument for the conversion, and it is far cheaper to obtain here than by
  attempting Option B.
- Stages 0-4 and D1/D2 remain net-positive regardless: generation 5.58 → 2.05
  ms/chunk, ~7.6 GB/traversal of allocation removed, and a bit-identical gate
  that did not exist before.

## If it does not pan out

`git revert ebae29c..HEAD` is NOT needed — Stage 5 would be its own commit on
top, and reverting that single commit restores `.Run()` and the raw threads.
Nothing else unwinds. There is no on-disk migration: worlds written under any
stage are readable by every other, because the delta format and content hash
never changed.

## Do not re-derive these

- The stutter is **CPU occupancy**, not per-thread work. Seven proven-negative
  levers are recorded in `CpuTopology.cs` and this document's §0.
- `ThreadPriority` does nothing on Mono/macOS. pthread QoS `UTILITY` is
  accepted and changes nothing. Both measured.
- macOS App Nap must stay disabled or every frame-time number is noise; the
  build writes `NSAppSleepDisabled` into Info.plist (`f687293` on main).
- `ChunkContentHash` is content-canonical: it cannot see storage changes. That
  is why the oracle works, and why "bit-identical" is a meaningful gate.
