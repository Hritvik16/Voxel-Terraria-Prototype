# Phase 2 Completion Record — First Light: A Rendered Generated Island

**Project:** Voxel Terraria 1 Byte BrickMap
**Spec:** ARCHITECTURE_v8.6.md §13 Phase 2
**Amendments in force:** 8.7 (air-mip), 8.8 (occupancy bitmask), 8.9 (LOD plan / hard rules), 8.10 (LOD cascade built + measured)
**Date closed:** August 24, 2026
**Hardware:** Apple M1 Air (fanless), 8GB unified memory
**Engine:** Unity 6000.3.10f1, URP, IL2CPP, Apple Silicon only

---

## 1. Scope (per §13 Phase 2)

Phase 2's stated goal: the flat GPU clipmap + the DDA raymarcher, drawing a *generated* island — merging v8's old Phase 2 (raymarcher) and Phase 3b (view generation) so there is something on screen in week ~4.

Its six literal acceptance assertions, and their honest status:

| # | §13 Phase 2 acceptance assertion | Status |
|---|---|---|
| 1 | Island renders as hard 0.1m cubes; normals correct from every angle **including from inside a dug-out pocket** | ⚠️ **Partial** — see §4.1 |
| 2 | Step-count heatmap: sky deep blue, **no red/stalled rays** | ⚠️ **Partial** — see §4.2 |
| 3 | Hand-authored dense brick renders pixel-identically to a uniform brick of the same material at comparable heatmap cost | ⚠️ **Not specifically tested** — see §4.3 |
| 4 | **ClipmapValidator green the entire session** | ✅ Green (coverage gap found and fixed — §4.4) |
| 5 | **Xcode capture, Performance State checked: raymarch ≤8.0ms at 1080p**, camera 1m from a fully-detailed wall | ❌ **Cannot be run as written** — see §5 |
| 6 | Hex-inspect a clipmap region: matches `CoordMath` addressing and A.8 layout | ✅ Satisfied by a stronger check — §4.5 |

Plus the build-step requirement: *"LOD: v1 uses 3 tiers… build the downsampler for those three; measure the cascade pool ceiling (§11.3)."* — downsampler and 3 tiers ✅ delivered and correctness-proven; **cascade pool ceiling ❌ never measured** (§6.1).

**This document does not claim Phase 2 passed cleanly.** It claims Phase 2's *substance* is built, proven where proof exists, and that the remaining gaps are known, written down, and non-blocking. See §7 for the actual sign-off wording.

---

## 2. Code delivered

| File | Purpose |
|---|---|
| `CoreEngine/Mirror/TerrainClipmap.cs` | Flat `GraphicsBuffer<uint>` window grid + GPU brick data; `MarkDirty`/`UploadDirty`; owns the air-mip pyramid + packed mips |
| `CoreEngine/Mirror/ClipmapValidator.cs` | Debug-only readback + byte-compare vs CPU pool (§10.4) |
| `CoreEngine/Mirror/AirMip.cs` (+ `.FromStore`, `.Packed`) | Amendment 8.7 air-skip pyramid, build + bit-packed/merged form |
| `CoreEngine/Mirror/AirMipValidator.cs` | Full-buffer GPU-vs-CPU pyramid compare |
| `CoreEngine/Mirror/OccupancyMask.cs` | Amendment 8.8 per-cell octant occupancy bitmask |
| `CoreEngine/Mirror/LODDownsampler.cs` | Majority-vote downsampling, tier-0 → coarser tiers |
| `CoreEngine/Mirror/CascadeTierPool.cs` | One GPU cascade pool per non-zero tier |
| `CoreEngine/Mirror/LODCascadeManager.cs` | Owns the per-tier pools; `MarkDirty`/`UploadDirty` |
| `ContentModules/Config/LODConfig.cs` | Parameterized tier count / voxel sizes / boundaries |
| `CoreEngine/WorldGen/GenerateChunk.cs` | Simple 2D heightfield generator, per-voxel height (v2) |
| `CoreEngine/Rendering/Raymarch.compute` | The DDA: 5 traversal modes, air-mip skip, LOD cascade branch, 7 debug views |
| `CoreEngine/Rendering/RaymarchFeature.cs` | `ScriptableRendererFeature`; dispatch + blit; all runtime toggles |
| `CoreEngine/Rendering/RaymarchReference.cs` | **The CPU DDA oracle** — 7 tracer variants, all fuzz-diffed against the per-voxel walk |
| `CoreEngine/Rendering/RaymarchDenseSkip.cs` | Mode-4 CPU sibling + counted variant |
| `CoreEngine/Rendering/RaymarchOccupancyReference.cs` | Mode-2 CPU sibling |
| `CoreEngine/Rendering/RaymarchStripped.compute` | Diagnostic: mode-1-only kernel (register-pressure probe) |
| `CoreEngine/Rendering/RaymarchMemoryProbe.compute` | Diagnostic: dependent-read latency isolation probe |
| `Game/Phase2Bootstrapper.cs` | Scene wiring, world generation, validator invocation |
| `Game/RaymarchAutoBenchmark.cs` | Automated multi-config, two-pose, driftchecked benchmark harness |
| `Game/RaymarchCaptureRig.cs` | Automated 7-pose × 7-variant visual sweep + pixel diagnostic scan |
| `Game/RaymarchDebugControls.cs`, `RaymarchGpuDebugReadback.cs` | Runtime toggles + per-pixel GPU counter readback |
| `CoreEngine/Tests/*` | 13 test classes (see §3) |

---

## 3. Test results — correctness

**Evidence:** Unity Test Runner, EditMode, **144+/144+ green, 0 failed**, verified repeatedly through the session.

The load-bearing ones for this phase:

- **`RaymarchMacroSkipTests`** (21) — O(1) brick leap vs. the per-voxel oracle, including a 3,000-ray randomized differential fuzz.
- **`RaymarchMipTests`** (12) — air-mip tracer vs. oracle, 3,000-ray fuzz, plus an explicit *work-saving* assertion (mip steps < walk steps < O1 steps) so a silently-disengaged pyramid can't pass.
- **`RaymarchMipReseedTests`** (23) — reseed + closed-form variants, 5,000-ray fuzz each, half deliberately in the near-axis-aligned pathological regime.
- **`RaymarchDenseSkipTests`** (7) — mode 4 (the certified default) vs. oracle, 4,000-ray fuzz over genuinely mixed/tunneled bricks, plus a work-saving proof.
- **`AirMipTests` / `AirMipPackedTests` / `OccupancyMaskTests`** (22) — pyramid construction, bit-pack round-trip, octant math, region-rebuild vs. from-scratch agreement.
- **`LODConfigTests` / `LODDownsamplerTests` / `CascadeTierPoolTests` / `LODCascadeManagerTests`** (34) — tier config invariants, majority-vote tie-breaks, bulk brick extraction against hand-computed values, cascade pool stale-slot freeing.

**Runtime validators, both GREEN in every session:**
- `ClipmapValidator` — GPU clipmap grid + brick data byte-identical to CPU truth.
- `AirMipValidator` — all 4 GPU air-mip levels byte-identical to an independent CPU rebuild from the store.

**LOD tier correctness (Amendment 8.10 §2):** all three tiers independently confirmed engaging, via per-pixel tier-coverage histograms computed against the exact on-screen colors (not eyeballed). Tier-0 behaviour proven unchanged when the cascade is enabled but not engaged (`denseMicroSteps` matched exactly, 116 = 116).

---

## 4. Acceptance assertions — the honest detail

### 4.1 Normals from inside a dug-out pocket — PARTIAL
Normals are correct from every exterior angle (verified via the `Normals` debug view across 7 poses). **The specific "carve a pocket with `SetVoxel`, confirm interior faces shade correctly" test was not run this session** — the pocket-carving code exists in `Phase2Bootstrapper` but is commented out. Interior-face shading is therefore *unverified*, not *failed*. Cheap to close: uncomment, run, look.

### 4.2 "No red/stalled rays" — PARTIAL, and this one is a real caveat
Sky is deep blue everywhere (confirmed; it became *solid* blue after the no-hit early-exit fix in Amendment 8.10 §3b). **But a genuine red band (>96 steps) exists at the horizon in the `GroundHorizon` pose** — rays crossing dense terrain at grazing angles, peaking at 122 steps. These are not *stalled* rays (they resolve, and the iteration cap is never hit), but they are red. Whether that violates the assertion depends on reading "no red" as "no runaway rays" (satisfied) or literally (not satisfied). Recorded as-is rather than argued either way.

### 4.3 Dense brick vs. uniform twin — NOT SPECIFICALLY TESTED
The `UniformDense` debug view exists and was used extensively. The specific hand-authored *A/B* — one dense brick, one uniform brick of the same material, compared pixel-for-pixel and by heatmap cost — was never constructed. The underlying property is indirectly supported (dense and uniform paths are both fuzz-proven against the same oracle), but the literal assertion is untested.

### 4.4 ClipmapValidator — GREEN, with a coverage gap found and closed
Green in every session. **However:** its loop bounds were hardcoded to the 8×8 chunk region the bootstrapper generated when it was written. The world was bumped to 22×22 during this session's LOD work, so from that point it was silently validating **64 of 484 chunks (~13%)**. Fixed on closing this phase (bounds now derive from the clipmap's own window dims; ungenerated chunks were already skipped). **Any "validator was green" claim made between the world bump and this fix covered only 13% of chunks** — stated so nobody over-reads the earlier green.

### 4.5 Hex-inspect a clipmap region — SATISFIED BY A STRONGER CHECK
Manual hex inspection was not performed. `ClipmapValidator` performs an automated byte-for-byte comparison of the entire clipmap grid and brick pool against CPU truth, using `CoordMath`'s own addressing — which is strictly stronger than spot-checking a region by eye. Treated as satisfied.

---

## 5. The performance gate — the §13 assertion that cannot be run

**Spec text:** *"Xcode capture, Performance State checked: raymarch ≤8.0ms at 1080p, camera 1m from a fully-detailed wall."*

This cannot be executed as written, for two independent reasons:
1. **Amendment 8.9 Rule 1** forbids Xcode for any performance measurement in this project — a deliberate workflow decision.
2. **`FrameTimingManager` cannot report Performance State** at all (Amendment 8.9 Rule 2), so the "Performance State checked" qualifier has no equivalent.

**What was measured instead** (standalone build, `RaymarchAutoBenchmark`, 240 samples/config, driftcheck-validated):

| Pose | Config | Wall-clock ms/frame @ 960×540 |
|---|---|---|
| `GroundHorizon` (real gameplay camera) | mode 4, cascade ON | **11.95** (driftcheck 11.89, 1.2% spread) |
| `GroundHorizon` | mode 4, cascade OFF | 11.81 |
| `TopDownAerial` | mode 4, cascade ON | 8.74 |

**Against the adopted target — 960×540 at 60fps (16.67ms), upscaled to 1080p — this passes with ~4.7ms headroom.**
**Against §11.2's own ≤9.0ms raymarch line, it does not** — raymarch alone is ~12ms hot, and that 9.0ms line is supposed to include Phase 7's shadow rays.

**Measurement-validity note (Amendment 8.10 §7), important enough to repeat here:** `FrameTimingManager.gpuFrameTime` is inflated by a near-constant ~2.7× on this setup and **must not** be read as an absolute figure; wall-clock frame time is the trustworthy measure. An earlier draft of Amendment 8.10 made exactly this mistake and reported a non-existent 3× budget overrun.

**The §4 Xcode/spec-gate conflict from Amendment 8.9 remains formally unresolved.** This document does not resolve it; it records what was measured, by what method, and against which target.

---

## 6. Known gaps carried forward (tracked, not blocking)

1. **Cascade pool ceiling never measured (§11.3 / §13 build step).** `LODCascadeManager.DefaultTierPoolCapacity` is `BRICK_POOL_CAP / 4` — a guess, twice revised by reasoning, never by measurement. `BrickDataPool`'s exhaustion exception is the intended signal; it has never fired. **This is an unmet Phase 2 build-step requirement**, carried forward deliberately rather than fudged.
2. **No CPU oracle for the cascade traversal path.** Every tier-0 mode has a fuzz-proven `RaymarchReference` sibling; the tier-1/2 branch in `Raymarch.compute` has none. Real testing debt.
3. **`cap1 ≈ cap400` anomaly unexplained** (Amendment 8.10 §5.2). Capping rays at one iteration doesn't measurably beat 400. The frequency-scaling hypothesis was tested and disproved. Not blocking; the most interesting loose thread in the project.
4. **~Half the frame is dependent-read memory latency** (Amendment 8.10 §5.1) — `MEMORYPROBE` costs 4.80ms of mode 4's 9.87ms. Any future optimization should start here, not at step-count reduction, which has now measured as the wrong lever twice.
5. **Upscaler cost unmeasured.** 960×540 → 1080p is the adopted target; no upscaler has been selected or benchmarked. It draws against the same budget.
6. **Two pre-existing render artifacts**, confirmed present with the cascade both on and off: small sky-colored gaps along the horizon at certain angles, and a grazing-angle terracing artifact.
7. **Acceptance assertions 4.1 and 4.3 untested** (above) — both cheap to close.
8. **Multi-pose ms coverage is thin** — only two poses have rigorous driftchecked numbers; the other five have visual/relative data only.
9. **The six original benchmark pose transforms are permanently lost** (deleted in a prior session's cleanup, never recorded outside deleted scripts). Current poses are reconstructions derived from generation geometry, not the originals — historical Amendment 8.9 numbers are therefore *not* strictly comparable to current ones.

---

## 7. Sign-off

Phase 2's substance is delivered: the flat GPU clipmap, the DDA raymarcher with a fuzz-proven CPU oracle, generated terrain rendering as hard 0.1m cubes, the air-mip acceleration pyramid (Amendment 8.7), the occupancy bitmask (8.8), and the 3-tier LOD cascade (8.9/8.10) — all correctness-proven by 144+ green tests and two byte-exact runtime validators.

- ✅ Clipmap + validator green; GPU state byte-identical to CPU truth.
- ✅ DDA raymarcher correct — every traversal mode differentially fuzz-tested against the per-voxel oracle.
- ✅ Generated island renders; normals correct from all exterior angles.
- ✅ LOD cascade built, all three tiers confirmed engaging, overhead measured at 1.2%.
- ✅ Certified defaults set so the shipped path is the fast path (Amendment 8.10 §7).
- ⚠️ Three acceptance assertions partial/untested (§4.1, §4.2, §4.3).
- ❌ The literal §13 performance gate is unrunnable by this project's own rules (§5); a substitute measurement is recorded and the conflict remains formally open.
- ❌ Cascade pool ceiling (a §13 build step) never measured (§6.1).

**Phase 2 is closed as substantially complete, with §6's nine gaps carried forward explicitly.** Proceeding to Phase 3 (§13): full world generation — biomes, features, static water.

**The one thing Phase 3 must do before it does anything else:** re-run `RaymarchAutoBenchmark` once real generation exists. Every performance number in this document describes a world made of a single noise function. Phase 3 changes terrain density and variety — the property raymarch cost is most sensitive to — so these numbers should be treated as a baseline to *re-establish*, not one to build on.
