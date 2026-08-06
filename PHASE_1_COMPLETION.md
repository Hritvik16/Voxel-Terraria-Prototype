# Phase 1 Completion Record — Core Memory Model

**Project:** Voxel Terraria 1 Byte BrickMap
**Spec:** ARCHITECTURE_v8.6.md
**Date closed:** July 22, 2026
**Hardware:** Apple M1 Air (fanless), 8GB unified memory
**Engine:** Unity 6000.3.10f1

---

## 1. Scope (per §13 Phase 1)

Phase 1's stated goal: implement the two-tier uniform/dense chunk/brick structure and `GetVoxel`/`SetVoxel`, proven by unit tests, no rendering. Acceptance criteria (§13 Phase 1):

1. Round-trip write/read across chunk and brick boundaries, including negative coordinates, byte-equal against a plain reference.
2. Uniform → populated → dense transitions correct; coalescer returns a refilled brick/chunk to uniform.
3. Pool free-lists return to starting count after alloc/free cycles (no leak).
4. Buffer read-timing recorded (`LockBufferForWrite` vs `SetData`) to decide §3.7's clipmap upload mechanism.
5. Fresh base memory overhead measured, not reused from v7.

All five are closed as of this document. Details and honest caveats below.

---

## 2. Code delivered

| File | Purpose |
|---|---|
| `CoreEngine/Coord/CoordMath.cs` + `.hlsl` | Bitwise coordinate chain (C.1), C#/HLSL identical |
| `CoreEngine/Memory/StructHeaders.cs` | `Chunk`, `BrickHandle` (A.2/A.3) |
| `CoreEngine/Memory/BrickDataPool.cs` | Flat 512B-brick pool, free-list stack |
| `CoreEngine/Memory/ChunkHandleAllocator.cs` | Pooled `BrickHandle[4096]` allocator |
| `CoreEngine/Memory/ChunkStore.cs` | Resident window, `GetVoxel`/`SetVoxel`, implements `IWorldQuery`/`IEditService` |
| `CoreEngine/Memory/Coalescer.cs` | `TryCoalesce` — collapses uniform dense bricks/chunks back to sticky notes |
| `CoreEngine/Tests/CoordMathTests.cs` | 3 tests |
| `CoreEngine/Tests/MemoryModelTests.cs` | 4 tests |
| `Game/Phase1Validator.cs` | Buffer upload benchmark harness (see §4) |
| `Assets/.../Phase1ReadTest.compute` | Synthetic GPU read-stress kernel for the benchmark |

---

## 3. Test results — correctness (proven, not just present)

**Evidence:** Unity Test Runner, EditMode, 7/7 green, 0 failed.

| Suite | Test | What it proves |
|---|---|---|
| `CoordMathTests` | `WorldToVoxel_NegativeCoords_FloorsCorrectly` | `-0.05m` floors to `-1`, not `0` |
| | `VoxelToChunk_NegativeCoords_UsesArithmeticShift` | Arithmetic shift, not truncating divide, for negative coords |
| | `LocalIndices_RoundTrip` | Full decode-and-compare round trip for both voxel index (0..511) and brick index (0..4095) — **fixed from an earlier version that only checked bounds, not an actual round trip** |
| `MemoryModelTests` | `SetVoxel_GetVoxel_RoundTrips_AcrossBoundaries` | Byte-exact read/write across brick boundary at positive coords |
| | `SetVoxel_GetVoxel_RoundTrips_NegativeChunkCoord` | Same, at chunk `(-1,-1,-1)`, voxel `(-1,-1,-1)` — isolates `ChunkStore`'s own toroidal masking (`GetFlatIndex`) from `CoordMath`, which was already proven correct separately |
| | `Coalescer_ReclaimsMemory_And_ReturnsToUniform` | Dense brick fully repainted one material coalesces back to uniform; pool byte freed |
| | `Pool_DoesNotLeak_AfterAllocAndFree` | Brick pool free-list returns to starting count, LRU-correct reuse order |

**Why `(-1,-1,-1)` was chosen for the negative-chunk test:** `VoxelToChunk(-1,-1,-1) == (-1,-1,-1)` is already independently proven in `CoordMathTests`. Reusing that exact value means a failure in the new `ChunkStore` test isolates cleanly to `ChunkStore`'s masking logic, not to coordinate math — no ambiguity about where a regression would live.

---

## 4. Buffer upload benchmark (§3.7 decision) — inconclusive, decision made anyway

### What was tried, and why each attempt was rejected before trusting a number

1. **CPU `Stopwatch` around `Dispatch()` calls.** Rejected — `Dispatch()` is async and returns immediately; this measured driver submission time, not GPU execution. Produced a meaningless `0ms` reading.
2. **1000-iteration loop of `Dispatch(kernel, 1, 1, 1)`, 40KB test buffer, Xcode capture.** Rejected on two grounds: (a) 2000 tiny dispatches per capture measured per-call submission overhead, not memory-bus read cost — this is what produced the initially "clean-looking" ~2.5x gap (11ms vs 27ms) between `SetData` and `LockBufferForWrite`, which in hindsight was very likely a methodology artifact, not a real signal; (b) 40KB fits entirely in GPU cache regardless of buffer placement, so even a correct single dispatch at this size couldn't show a real difference.
3. **Single dispatch, ~8MB (2,000,000-element) buffer, group count derived from buffer size, warm-up dispatch discarded.** This is the methodologically correct version. Confirmed via Xcode capture that the fix took (`dispatchThreadgroups:{31250, 1, 1}`, matching `2,000,000 / 64`).
4. **Alternating S/L/S/L/S/L capture sequence on the corrected harness.** Results:

| Order | Path | `ComputePipelinePerformance` | Performance State |
|---|---|---|---|
| 1 | SetData | 16.21 ms (36.3%) | Medium |
| 2 | LockBufferForWrite | 32.91 ms (50.9%) | Medium |
| 3 | SetData | 22.51 ms (41.9%) | Medium |
| 4 | LockBufferForWrite | 22.49 ms (44.3%) | Medium |
| 5 | SetData | 32.91 ms (54.9%) | Medium |
| 6 | LockBufferForWrite | 5.73 ms (17.3%) | Medium |

**Diagnosis:** ranges overlap almost entirely (SetData: 16.2–32.9ms; LockBufferForWrite: 5.7–32.9ms), and within-path variance exceeds any between-path gap. Root cause identified: the test scene was Unity's default scene, still rendering unrelated geometry (44-60 render encoders, 41,040 vertices, variable draw-call counts per capture) concurrently with the compute dispatch under Metal's `Overlapping` execution mode — the dispatch was contending for GPU time against a variable-sized render workload, not running in isolation. This confound was never fully isolated before the decision below was made — the fix (a genuinely empty scene) was identified but not executed, in favor of a faster, honestly-logged tiebreak.

### Decision

**`SetData` selected as the clipmap upload mechanism**, on these grounds:
- The measured comparison is inconclusive, not a demonstrated win for either side — this is explicitly **not** claimed as a proven performance result.
- `SetData` is the simpler call site (no lock/unlock pairing to misuse later).
- Unity's own documentation favors `SetData` for device-local placement on GPU-read-heavy buffers, which matches the clipmap's access pattern (§3.7: "written rarely, read millions of times per frame").
- The decision is cheap to reverse: the upload mechanism inside `TerrainClipmap.UploadDirty()` is an internal implementation detail, not a frozen public API (§12) — swapping to `LockBufferForWrite` later is a one-function change, not a redesign.
- The real, trustworthy test of this decision arrives in Phase 2, where the actual `Raymarch.compute` kernel is profiled against the genuine §2.2 ≤9.0ms/≤13.0ms budgets, at real clipmap access patterns — a far more meaningful measurement than this synthetic one.

**Explicitly not claimed:** that `SetData` is faster than `LockBufferForWrite` on this hardware. That claim was never established. If Phase 2's raymarch capture shows clipmap upload/read cost eating meaningfully into the ≤9.0ms budget, **re-open this decision** and re-test properly (empty scene, isolated dispatch) before assuming the mechanism is the cause.

---

## 5. Memory baseline (§11.3)

**Measurement conditions:** standalone build, no other applications competing for memory, **Swap Used: 0 bytes**, no orphaned `GPUToolsReplayService` processes (an earlier reading was discarded — see below).

**Result: ~608 MB** (`Voxel Terraria 1 Byte BrickMap` process, Activity Monitor).

**Caveat, logged honestly:** this figure includes the Phase 1 benchmark scaffolding still present in the build (8MB test buffers ×2, `Phase1ReadTest.compute`, `Phase1Validator`). It is **not** a minimal empty-project floor in the strictest sense — it's "this build's overhead," a reasonable stand-in baseline for now. **Re-measure once the Phase 1 benchmark scaffolding is stripped from the build**, so that any memory growth observed in Phase 2+ can be attributed to genuinely new systems (clipmap, raymarcher) rather than confused with scaffolding removal.

**Discarded reading:** an earlier attempt at this measurement showed 7.33GB swap in use and ~17 orphaned `GPUToolsReplayService` processes (leftover Xcode GPU-capture helpers) consuming >12GB combined on the 8GB machine. That reading was correctly discarded as unusable — not a real baseline, a measurement of "engine running while the OS thrashes under an unrelated leak."

---

## 6. Known gaps carried forward (not blocking, but tracked)

- **Buffer benchmark confound never fully isolated.** The empty-scene re-test (to remove render-encoder contention from the comparison) was scoped but not executed, in favor of the tiebreak decision in §4. If §3.7's choice ever needs re-litigating, start there rather than re-running the contaminated harness.
- **Base overhead includes benchmark scaffolding**, per §5 — re-measure after scaffolding removal.
- **`ChunkHandleAllocator`/`Coalescer` interaction under eviction** is unit-tested for coalescing in isolation, but the "return `chunk.bricks` to the allocator on full-uniform collapse" step is explicitly deferred to the streaming/eviction system (Phase 4, per §4.5) — this is by design, not a gap, but noted so it isn't mistaken for an oversight when Phase 4 begins.

---

## 7. Sign-off

Phase 1 acceptance criteria (§13 Phase 1) are satisfied:
- ✅ Round-trip correctness, including negative coordinates, proven via 7/7 green EditMode tests.
- ✅ Uniform/dense/coalesce transitions proven.
- ✅ Pool leak-freedom proven.
- ✅ Buffer read-timing recorded and a decision made, with the inconclusive result and its confound honestly documented rather than overstated.
- ✅ Fresh base memory overhead measured under clean conditions (0 swap), with a caveat on what it includes.

**Phase 1 is closed.** Proceeding to Phase 2 (§13): the flat GPU terrain clipmap and the DDA raymarcher, merged with first-light generation per the phase plan.
