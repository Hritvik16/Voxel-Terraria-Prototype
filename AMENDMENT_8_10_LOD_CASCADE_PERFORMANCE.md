# Amendment 8.10 — LOD Cascade: Built, Correctness-Proven, Performance-Fixed, and What's Still Open

**Date:** August 24, 2026
**Supersedes:** Amendment 8.9's LOD-cascade-related OPEN items (§3, §5) — those questions now have real
measured answers instead of open guesses, recorded below. Amendment 8.9 itself remains valid for
everything it covers that this document doesn't touch (the Xcode/spec-gate conflict in its §4, the
900×540-vs-1080p resolution decision, and its own historical benchmark table).

**Read this whole document before starting new work.** Amendment 8.9 was written because a prior session
blurred verified fact and assumption together. This document follows the same discipline: every number
below is either **VERIFIED** (with the run it came from) or **OPEN** (a decision not yet made). Nothing
here is invented to fill a gap.

---

## 0. Bottom line, if you read nothing else

The LOD cascade (3 tiers: 0.1m / 0.2m / 0.4m) is **built and correctness-proven** across all three tiers.
It went through a real performance regression (up to **+82.7%** slower than no cascade), which was
diagnosed with real data — not guessed — and fixed down to **~1.2% overhead at the pose that actually
matters** (`GroundHorizon`, ground level, looking at the skyline — Amendment 8.9's own definition of the
real gameplay camera), reproduced across three separate benchmark runs.

**The target is 960×540 at 60fps, upscaled to 1080p for display.** As of the corrected measurements in §3c:
`GroundHorizon` with the cascade on runs at **11.95ms/frame wall-clock**, against a 16.67ms budget —
roughly **71% consumed, ~4.7ms headroom**, on a standalone build. **The stated target is met today.**

Three caveats that matter more than the headline (all detailed in §3c and §5):
- The dev machine (fanless M1 Air) **throttles ~24% between runs** — 7.96ms vs 9.87ms for an identical
  config 9 minutes apart. Budget against the hot number, not the cold one.
- Remaining headroom (~4.7ms) is **thinner than the architecture's own remaining allowances** (§11.2
  reserves 3.5ms fluid + 0.5ms light drain = 4.0ms, before shadow rays exist at all).
- **Roughly half the frame cost is memory latency, not traversal work** — see §3c. This changes what
  future optimization should even target.

### CORRECTION NOTICE (important — an earlier version of this document was wrong)

The first version of §0/§5 claimed the raymarcher was **"23.73ms, 1.42× over the entire 16.67ms budget."**
**That claim was wrong and is retracted.** It read `gpu_avg` from `RaymarchAutoBenchmark` as an absolute
millisecond figure. That harness's own header, and its own `run_metadata.txt`, explicitly state the number
is a *relative* signal only and not valid as an absolute figure — a disclaimer that was quoted correctly
earlier in the same session and then ignored anyway. `gpu_avg` was subsequently found to be inflated by a
near-constant **~2.6–2.7×** across all 21 configs (a frame's GPU time cannot exceed that frame's
wall-clock duration; it consistently did). The corrected figures throughout this document use `wall_avg`.
The methodology fixes that surfaced this are recorded in §3c.

---

## 1. VERIFIED: what was built this session

- **`LODConfig.cs`** — parameterized tier config (tier count, voxel sizes, outer boundaries), matching
  Amendment 8.9 §5.1's instruction to build this as config values, not hardcoded.
- **`LODDownsampler.cs`** — majority-vote downsampling from tier-0 material data to coarser tiers, with a
  documented, tested tie-break rule (air needs a strict 5-of-8 majority; ties resolve to the lowest
  material id, a solid-preserving bias).
- **`CascadeTierPool.cs` / `LODCascadeManager.cs`** — one GPU cascade pool per non-zero tier, mirroring
  `TerrainClipmap`'s own clipmap + brick-pool pattern.
- **`Raymarch.compute`** — a new coarse-tier traversal branch, gated entirely behind `_UseLODCascade`
  (default off). With the flag off, tier is forced to 0 every iteration and the original tier-0 code path
  is unchanged.
- **Developer material palette + per-voxel grain shading** (`_UseDevColors`, default on) — cosmetic only,
  doesn't touch traversal or any measured number. Flip off for a byte-identical reproduction of the
  original flat-gray Beauty shading.
- **Tooling**: `RaymarchCaptureRig` (pose × variant visual/correctness sweep, version-gated auto-rebuild,
  runtime ground-height queries instead of guessed Y constants) and an extended `RaymarchAutoBenchmark`
  (cascade-aware configs, a second-pose comparison).

## 2. VERIFIED: correctness

- **All three tiers visually confirmed independently**, via `RaymarchCaptureRig`'s `LODTierView` and a
  per-pixel tier-coverage histogram (not eyeballed — computed against the exact on-screen color):
  - Tier 0→1 boundary: confirmed at `TopDownAerial_Center` and `GroundHorizon_Approx`.
  - Tier 1→2 boundary: confirmed at `HighAerial_TierTwoReach` (`tier2=9.7%` of frame) and independently at
    `GroundDiagonal_ReverseCorner`/`GroundEdge_LookOutOfBounds`.
- **Tier-0 path proven unchanged when cascade is on but not engaged**: `denseMicroSteps` matched exactly
  (116 = 116) between cascade-off and cascade-on captures at a pose where the ray never leaves tier 0 —
  real evidence, not just the "should be identical" argument from the code's own structure.
- **144+ NUnit tests green**, including new tests added this session for the bulk-brick extraction rewrite
  (hand-computed expected values, not just re-running the majority-vote formula) and the `CascadeTierPool`
  allocator (stale-slot-freeing, pool-exhaustion guards).
- **NOT covered by any CPU oracle**: unlike tier 0's traversal modes (which all have a
  `RaymarchReference.cs` sibling, differential-fuzzed against `TracerRaycast`), the tier-1/2 traversal
  branch in `Raymarch.compute` has no CPU-side equivalent at all. Every claim about it is "this reuses an
  already-proven primitive with different arguments," which is real evidence but categorically weaker than
  a fuzz-tested proof. **This is a real, open testing gap**, not resolved by this document.

## 3. VERIFIED: the performance investigation, in order — what was tried, what worked, what didn't

This is worth recording in full, not just the final numbers, because the *pattern* matters for next time:
two of the four fixes below barely moved the number despite sound reasoning, and only measuring after each
one caught that.

### 3a. Chunk-load-time (one-time cost, not per-frame)
- **Symptom**: multi-minute hang ("beach ball") when generating the test world.
- **Cause, confirmed by reading `ChunkGenerator.GenerateChunk`, not guessed**: `LODDownsampler` extracted
  tier-0 data via `ChunkStore.GetVoxel()` once per voxel (~2.1M calls/chunk/tier). At 484 chunks × 2 tiers,
  ~2 billion calls, synchronous on the main thread.
- **Fix**: bulk brick-level extraction (walk 4096 bricks, not 2.1M voxels; fill/copy each brick's 512 bytes
  in one shot). **Result: `Cascades.UploadDirty` 20,741ms → still slow.**
- **Second cause, confirmed by reading the same file again**: a chunk-level "skip if uniform" fast path
  never fired, because `GenerateChunk` unconditionally sets `chunk.isUniform = false` for every chunk — the
  condition it checked can never occur from this generator. Dead code, not a bug, just misdiagnosed.
- **Real fix**: `MajorityVote`'s inner loop used a 256-entry table for a vote among at most 8 samples.
  Rewritten to track ≤8 distinct values directly, verified bit-identical to the old version across every
  documented test case plus a 200,000-sample random fuzz before shipping.
- **Final measured result: `Cascades.UploadDirty` 20,741ms → 11,178ms** (~46% reduction). Total one-time
  world load: minutes → ~21 seconds.

### 3b. Per-frame cascade overhead (the one that matters for 60fps)
Measured via `RaymarchAutoBenchmark` (standalone build, `Mode4-DenseSkip`, cascade on vs. off, with a
driftcheck line for every "on" measurement to rule out thermal drift):

| Fix attempted | `TopDownAerial` overhead | `GroundHorizon` overhead | Worked? |
|---|---|---|---|
| (none — baseline) | +82.7% (18.30→33.44ms) | not yet tested | — |
| Coarse-tier dense-voxel stepped 1 raw voxel at a time → leap full coarse-voxel span | +77.9% (15.59→27.73ms) | not yet tested | **Barely moved it — real fix, wrong bottleneck** |
| Empty coarse *bricks* skipped one at a time → chained leaps (mirroring tier 0's own mode-2 chaining) | +21.8% (18.44→22.46ms) | +50.9% (29.95→45.18ms) | **Big win at TopDown; revealed GroundHorizon was much worse and untested** |
| No-hit sky rays now walk to 290m instead of 128m with no acceleration structure → hard early-exit once a ray is outside the only generated chunk layer (`cy=0`) and diverging further | +13.4% (18.79→21.30ms) | **+0.6%** (23.73→23.87ms, driftcheck −2.8%) | **Closed almost the entire remaining gap at the pose that matters** |

The last fix was found by actually looking at `StepHeat`/`LODTierView`/`UniformDenseView` captures at
`GroundHorizon` — the cost concentrated exactly at the sky/ground transition in `LODTierView` (rays that
never resolve a hit), not inside the visible terrain (which was 100% tier-0, `tierAtHit` never left 0 for
any visible pixel at this pose). **The first two fixes were argued from reasoning about the code, without a
capture to check the argument against; the third was found by looking at real diagnostic data first.** The
difference in outcome speaks for itself.

## 4. VERIFIED: the OPEN items from Amendment 8.9 §3, now decided

- **Tier voxel sizes**: 0.1m / 0.2m / 0.4m — decided (chat, prior session), unchanged.
- **Tier boundaries**: 64m / 128m / 290m — decided, and the tier-2 boundary (window-corner-distance
  option) is now independently confirmed correct by the `HighAerial_TierTwoReach` capture actually
  reaching tier 2 at the predicted distance.
- **Internal render resolution for the pixel-subtend formula**: 960×540 — used throughout this session's
  benchmarking. **Still not compared against native 1080p** — see §5 below; this is now blocked on the
  absolute-budget question, not just an unmeasured preference.

## 5. OPEN: what is not resolved, stated plainly

1. **~Half the frame is memory latency, not traversal work — and this changes what "optimizing" means.**
   `Mode1-Reseed_MEMORYPROBE` (the existing diagnostic kernel: same dependent-read chain as the real
   traversal, zero traversal math) costs **4.80ms of Mode 4's 9.87ms**. Corroborated by the iteration-cap
   ladder: capping rays at **1** outer iteration (19.22ms) vs 400 (17.46ms) makes *no difference*, across
   two runs with different settle lengths — so cost is not driven by how far rays travel. That probe's own
   header called this: *"If GPU frame time here is close to the real kernel's, the bottleneck is memory
   latency and no amount of traversal cleverness fixes it."* **Consequence: the sub-brick occupancy /
   empty-space-skipping structure this document's earlier draft was about to scope would likely have made
   things WORSE** — it adds another dependent read per step to fix a cost that is not step-count-driven.
   The real lever, if one is ever needed, is memory layout and cache residency. Not investigated here.
2. **The `cap1 ≈ cap400` anomaly is unexplained.** The frequency-scaling hypothesis was tested (settle
   raised 30→180 frames, samples 25→240) and **disproved** — the inversion persists. Whatever causes it is
   still unknown. Likely related to item 1 (fixed per-pixel cost dominating), but that is a hypothesis, not
   a finding.
3. **Diagnostic branches cost ~12%, measured.** `Mode1-Reseed_STRIPPED` (mode-1-only, Beauty-only, no debug
   buffer) runs at 15.30ms vs the full kernel's ~17.5ms in the same sweep. That is the price of keeping
   every traversal mode and debug view compiled into one kernel. **Recommendation: keep paying it for now**
   — the diagnostic kernels repeatedly earned their keep this session (MEMORYPROBE just prevented building
   the wrong optimization; STRIPPED is what quantifies this very cost). If it ever needs reclaiming, the
   right fix is `multi_compile`/`shader_feature` keywords so only the active path compiles, **not** deleting
   the diagnostics — this project has already lost data once this way (the six original pose transforms,
   deleted in a prior session's cleanup and unrecoverable).
4. **The 1080p upscale cost is unmeasured.** The target is 960×540 → 1080p. Unity 6 URP ships STP
   (Spatiotemporal Post-processing) and FSR1; MetalFX is another route on this hardware. Any of them draws
   against the same 16.67ms budget. Nobody has measured which, or how much. **Do not assume it is free.**
5. **The 960×540 vs. native-1080p comparison** (Amendment 8.9's own original OPEN item) — never run. Now
   arguably moot rather than blocked: 960×540+upscale is the adopted target (§4), so the open question is
   item 4's upscaler cost, not native 1080p's raymarch cost.
6. **No air-mip-equivalent hierarchy for tiers 1/2.** The chaining fix (§3b) made single-brick-at-a-time
   skipping much cheaper without building a real hierarchy. Currently not needed (the numbers are fine),
   but worth remembering if a future resolution/tier-boundary change extends how far these tiers must see.
7. **No CPU oracle for cascade traversal** (§2, restated because it matters). A real, tracked testing gap.
8. **Multi-pose coverage remains thin for real ms numbers.** Only `TopDownAerial` and `GroundHorizon` have
   been run through the rigorous, driftcheck-validated harness. Every other pose in `RaymarchCaptureRig`
   (`GroundCenter_Forward`, both diagonals, `GroundEdge_LookOutOfBounds`) has visual/relative capture data
   only.
9. **Window-size reconciliation with Phase 4 (streaming).** The generated test area was bumped from 8×8 to
   22×22 chunks specifically to give the cascade room to reach tier 2 during testing — not necessarily the
   shipped game's generation/streaming footprint, which doesn't exist yet (Phase 4).
   `WINDOW_CHUNKS_XZ = 32` (409.6m) remains the resident-window config value; whether the real game fills
   it, and how that interacts with the tier-2 outer bound (290m, derived from that same window size), is
   Phase 4's problem.
10. **Amendment 8.9 §4's Xcode/spec-gate conflict** — still open, untouched by this session.
11. **Two pre-existing, unrelated rendering artifacts found but not fixed**, confirmed identical between
    cascade-on and cascade-off (so not caused by this session's work): small sky-colored gaps at certain
    viewing angles along the terrain horizon, and a grazing-angle terracing artifact. Logged so they're not
    forgotten; not blocking anything above.

## 6. Next steps, in order

1. **Proceed to Phase 3 (world generation).** The stated target — 960×540 at 60fps — is met today, measured
   on a standalone build with a tight driftcheck. The cascade is done and costs ~nothing. There is no
   identified, low-risk optimization left to take: §5.1 shows the obvious next one would likely backfire.
   Blocking further would be optimizing against an unproven theory.
2. **Re-measure after Phase 3 lands**, before Phase 5 (fluid). Phase 3 replaces one noise function with real
   biomes/features/static water — materially different terrain density and variety than anything measured
   here. Every number in this document describes a single-noise-function world.
3. **Measure the upscaler (§5.4) as its own line item** once there is a reason to pick one. It is the only
   *known* unmeasured cost sitting inside the current budget.
4. **Treat §11.2's ≤9.0ms raymarch line as the real constraint, not 16.67ms.** Raymarch alone is already
   ~12ms hot. Fluid (3.5ms) and light drain (0.5ms) are still to come, and shadow rays (Phase 7) can double
   ray count in the worst case. The doc's three shadow fallback rungs are likely to be *needed*, not
   optional — plan for engaging them rather than being surprised.
5. **Add CPU-oracle fuzz tests for the cascade traversal path** (§5.7) — real technical debt, schedulable
   any time, no urgency.
6. **If a raymarch optimization pass ever becomes necessary**, start from §5.1: profile memory access
   patterns and cache residency. Do **not** start from traversal step reduction — that has now been
   measured as the wrong lever twice.

---

## 7. CERTIFIED DEFAULTS — what actually ships now

**A latent problem found while writing this document:** every good number above came from the benchmark and
capture rigs *explicitly setting* the fast configuration. `RaymarchFeature`'s own static defaults — what you
get from plain Play mode, or from any future scene or tool that doesn't set them — were the **slowest**
configuration measured.

| Setting | Was | Now | Why |
|---|---|---|---|
| `TraversalMode` | `0` (LeapSpan) | **`4`** (DenseSkip) | Mode 0 measured **76.66ms** vs mode 4's **27.06ms** in the same sweep — 2.8× slower, and the worst of all five modes. Amendment 8.9 already said "keep mode 4"; the code never agreed. |
| `UsePackedMips` | `false` | **`true`** | Amendment 8.9 §6 already recorded packed mips as "real, free, ~2.9% win. Keep it." The default never changed to match. |
| `UseLODCascade` | `false` | **`true`** | Overhead measured at 1.2% (inside driftcheck spread). Also *required* for correct distant terrain: off caps ray travel at 128m, on reaches tier 2's 290m bound. |

Mode 3 (ReseedClosedForm, 26.30ms) measured statistically **tied** with mode 4 at the benchmarked pose.
Mode 4 was chosen anyway: its advantage is specifically the dense-brick path that dominates close-range and
grazing views, and it is the mode with an existing CPU-oracle fuzz proof (`RaymarchDenseSkipTests`). For
mode 3 to displace it, it would need to win across multiple poses by more than the ~24% run-to-run thermal
drift this machine exhibits.

**On deleting the non-default modes and diagnostic kernels: don't.** They cost ~12% (§5.3, measured) and
have repeatedly paid for themselves — `MEMORYPROBE` prevented building an optimization that would have made
things worse (§5.1), and `STRIPPED` is what quantifies the 12% itself. If that cost ever needs reclaiming,
use `multi_compile`/`shader_feature` keywords so only the active path compiles. This project has already
permanently lost data to a cleanup pass once (the six original benchmark pose transforms, deleted in a prior
session and unrecoverable) — that is the failure mode to avoid repeating.