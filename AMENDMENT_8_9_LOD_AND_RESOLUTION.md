# Amendment 8.9 — Phase 2 Status, Resolution, and LOD Cascade Plan

**Date:** August 20, 2026
**Supersedes:** `PHASE2_HANDOFF.md` and `PROJECT_HANDOFF_v2.md` — both are obsolete. Use only this document going forward. The reason both are obsolete: neither clearly separated "verified fact" from "assumption," which caused a later session to invent numbers (a 15ms result that doesn't exist, a "1080p is impossible" conclusion that was never tested) while trying to fill gaps I'd left ambiguous. This document exists specifically to close that gap.

**Read this whole document before starting new work.** It is short on purpose.

---

## 0. Hard rules for this project, going forward

1. **No Xcode, ever, for any performance measurement.** The project uses only the in-engine automated benchmark harness (`FrameTimingManager`, one-button build-and-run, folder dump, screenshots + `report.txt`). This is a deliberate workflow decision, not a limitation to work around. Any future session that suggests Xcode should be corrected immediately.
2. **`FrameTimingManager` cannot report Performance State** (thermal throttling status). This is a real, permanent limitation of the chosen workflow, not a bug to fix. Mitigation: every benchmark run includes a `REPEAT_driftcheck` line — a config repeated at the end of the sweep. If it closely matches its first measurement, that's decent (not perfect) evidence the machine wasn't mid-throttle. Treat a spread-out or drifted driftcheck as a signal to re-run, not as data to use.
3. **Every number in this document is either marked VERIFIED (with its source) or marked OPEN (a decision not yet made).** Do not treat an OPEN item as decided. Do not invent a number to fill a gap — ask, or run the benchmark.

---

## 1. VERIFIED: what has actually been measured

**Internal render resolution used in all benchmarking so far: 960×540.** This is `RaymarchFeature`'s `_gateWidth`/`_gateHeight` default — originally chosen because it made repeated benchmark runs fast, not because of a documented performance ceiling at 1080p. **1080p has never been benchmarked.** No session has produced a number showing 1080p is achievable or unachievable. Any claim otherwise (in either direction) is not backed by data in hand.

**Best real numbers so far, 960×540, mode 4 (current best traversal), `FrameTimingManager`, six poses, default vsync:**

| Config | Worst pose | Worst ms | Mean ms |
|---|---|---|---|
| Baseline (legacy mips) | GroundHorizon | 20.42 | 14.26 |
| + packed mips (shippable) | GroundHorizon | 19.68 | 13.85 |
| + packed mips + 64m distance trim (**not shippable** — breaks seeing the whole world) | GroundHorizon | 16.56 | 12.48 |

**No configuration measured so far — shippable or not — has hit 8.0ms on the worst-case pose.** The closest shippable number is 13.85ms mean / 19.68ms worst-case. There is no 15ms result anywhere in the actual data. If a 15ms number exists from a run not captured in this document, it needs to be re-sent as a real benchmark folder before it's treated as fact.

**`DeepTunnel`** — the pose the dense-skip fix (mode 4) specifically targets — is consistently the cheapest pose in every config. **`GroundHorizon`** — standing on the ground looking at the skyline, the real gameplay camera — is consistently the most expensive, and it's dominated by distant terrain, which is exactly the case LOD cascades exist to make cheap.

---

## 2. VERIFIED: what the architecture document actually says (cross-checked against source text, not paraphrase)

**§13, Phase 2 build steps:** *"LOD: v1 uses 3 tiers, not 5 (§6.4 — 0.1m / 0.4m / 1.6m). Build the downsampler for those three; measure the cascade pool ceiling (§11.3)."* — **LOD cascades are unfinished Phase 2 work, not deferred scope.**

**§13, Phase 2 acceptance test:** *"Xcode capture, Performance State checked: raymarch ≤8.0ms at 1080p, camera 1m from a fully-detailed wall."* — this is the literal spec text. Per Rule 1 above, this project does not use Xcode. **This means the spec's gate, as literally written, cannot be run as written.** This is an open conflict between the spec and the chosen workflow — see §4 below. It is not resolved by pretending the gate was met, or by silently substituting a different tool without saying so.

**§6.4, C.5 derivation:** the LOD tier-boundary formula is "a voxel of size V subtends ~1 pixel at distance D," derived from vertical FOV and screen height:
```
anglePerPixel = vFOV_radians / screenHeightPixels
D(V) = V / anglePerPixel
```
The spec's own 128m figure for the 0.1m→0.4m transition is this formula at 1080p (screenHeight=1080), with headroom rounding (103.1m raw → 128m, a ×1.24 safety margin, chosen because 128m is exactly 10 chunks at 12.8m/chunk — a clean number, not just a round one).

**`EngineConfig.WINDOW_CHUNKS_XZ = 32`**, chunks are 12.8m each → the resident window is 409.6m across → **~205m from center to the middle of an edge, ~290m from center to a far corner** (diagonal). This is VERIFIED from the config value itself, not derived from any performance test.

---

## 3. OPEN: decisions not yet made — do not assume these are settled

**OPEN — internal render resolution.** 960×540 has been *used for convenience*, not *chosen as the ship target*. Nobody has benchmarked 1080p. Until that happens, there is no basis to say either "960×540 is the target" or "1080p is impossible." **Recommended resolution to this open item:** benchmark both, once LOD cascades exist (see §5) — building LOD first means whichever resolution gets tested afterward is tested against the actual intended architecture, not a crippled placeholder.

**OPEN — LOD tier scheme.** The spec's 3-tier table is 0.1m / 0.4m / 1.6m. A finer scheme (0.1m / 0.2m / 0.4m) was discussed as preferred, for smoother/less noticeable transitions. This has **not been formally decided or written up as a deviation.** If the finer scheme is adopted, it needs to be documented the same way every other deviation in this codebase has been (a dated, numbered amendment — this document can serve as that amendment once §5 below is resolved).

**OPEN — LOD tier boundaries depend on the still-undecided resolution.** The pixel-subtend formula in §2 needs a screen height. At 1080p (height=1080) vs. 540p (height=540), every distance exactly halves:

| Transition | at 1080p height | at 540p height |
|---|---|---|
| 0.1m → 0.2m | 128m | 64m |
| 0.2m → 0.4m | 256m | 128m |

**These cannot be finalized until the resolution question (above) is resolved.**

**OPEN — the LOD tier boundaries don't fit the current window size.** The resident window reaches ~205–290m depending on direction (§2). At 540p-derived boundaries, tier 2 (0.4m) would end at 128m — well inside the window, meaning most of the window's own edge is never reached by any tier, a gap, not an overshoot. At 1080p-derived boundaries, tier 2 would end at 256m — past the ~205m edge-center distance but short of the ~290m corner distance. **Neither resolution's derived boundaries cleanly match the window's actual shape.** This needs an explicit decision, not a default. Three real options, stated plainly:

1. **Extend the outermost tier's coverage to the window's actual corner distance**, past where the strict pixel-math says it needs to — the overshoot is small and the formula already has ~24% headroom built in, so this is likely visually unnoticeable. Cheapest, no config changes.
2. **Grow the window** (`WINDOW_CHUNKS_XZ`) so it genuinely reaches past the derived tier-2 boundary — a real memory-cost increase (bigger clipmap, bigger air-mip pyramid, more resident chunks), and a deliberate `EngineConfig` change, not a free option.
3. **Add a 4th, coarser tier** to cover the gap — more downsampler/pool/validation work, the exact cost the 5-tier→3-tier simplification in the original spec was trying to avoid.

**No option has been chosen.** Recommendation, not a decision: option 1, because it's free and the formula's own headroom makes the overshoot low-risk — but this is the next session's call to make explicitly, not infer.

---

## 4. OPEN: the Xcode/spec conflict

The spec's Phase 2 gate is written against Xcode. This project will not use Xcode (Rule 1). This is a real, acknowledged conflict, not something to paper over. **Recommended resolution, not yet formally adopted:** treat the `FrameTimingManager` harness's own worst-case, driftcheck-validated number as the project's *substitute* gate, explicitly documented as a deviation from the literal spec text — the same way every other deviation in this codebase has been handled (a dated amendment, not a silent substitution). This document can be extended to formally make that substitution once the LOD system exists and a real benchmark run is in hand to set the substitute number against.

---

## 5. What to actually do next — unambiguous

1. **Build the LOD cascade system.** This is unfinished Phase 2 work regardless of how the resolution/tier-boundary questions above resolve — three tiers (whatever their exact boundaries end up being), a downsampler, cascade pools, one traversal algorithm parameterized by voxel size. This is not blocked by the open questions in §3 — build it parameterized (tier count, tier voxel sizes, and tier boundaries all as config values, not hardcoded), so the open questions can be resolved by changing numbers, not by rewriting the system.
2. **Once it exists, benchmark at both 960×540 and 1080p**, using the existing automated harness (no Xcode), against the six-pose sweep already established, with the driftcheck line included every time.
3. **Only then resolve the OPEN items in §3** — with real LOD-equipped numbers in hand instead of guesses, decide the resolution target, the tier scheme, and the window-size fit.
4. **Write the final decisions up as a proper dated amendment** (this document can be extended, or a new one written), so a future session has one unambiguous source instead of needing to reconstruct decisions from a conversation transcript.

---

## 6. Everything else from the prior handoff that remains true and doesn't need re-litigating

- Traversal mode 4 (dense-brick skip) is real, CPU-proven via fuzz testing, and consistently the fastest mode measured. Keep it.
- Packed/merged air-mip buffer is real, free, ~2.9% win. Keep it.
- The per-voxel worldgen height fix (no more brick-quantized terracing) is real and correct. Keep it.
- 110 automated tests are green and form a trustworthy foundation — no need to re-verify.
- Phase order is: 0 ✅ → 0.5 ✅ → 1 ✅ → **2 (current, LOD unfinished)** → 3 (full world generation — biomes, features, static water) → 4 (streaming) → 5a/5b (fluid) → 6 (physics/player/editing) → 7 (lighting/shadows) → 8 (soak). Phase 3 is world generation, not player movement.
- Eleven superseded diagnostic scripts were deleted this session (list preserved in `PROJECT_HANDOFF_v2.md` if needed for reference, though that file is otherwise obsolete per this document's header).
