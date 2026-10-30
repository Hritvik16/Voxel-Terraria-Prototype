# Unity version research, and a proposed upgrade point

**Status: RESEARCH + PROPOSAL, for the owner (decision 12).** Written 2026-09-24.
Nothing was upgraded. The Metal/MSL floor is left alone, as decided.

Every claim below is tagged. **SOURCED** means it comes from Unity's release notes
or third-party reporting on them (links in §6); none of it was measured on this
machine. **ESTABLISHED** means it was read from this repo. **PROPOSED** is my
recommendation.

---

## 1. Where we are

| | |
|---|---|
| Editor in use | **6000.3.10f1** (Unity 6.3 LTS). ESTABLISHED: every runner script hard-codes `/Applications/6000.3.10f1/…` |
| 6.3 LTS status | Unity's current LTS; reported as supported into late 2027. SOURCED |
| Newest 6.3 patch found | **6000.3.17f1 (4 June 2026)**. SOURCED. The search index may lag, so check Unity Hub for anything newer. |
| Newer streams | **6.4** shipped 19 March 2026. **6.5** shipped mid-June 2026 as a "Supported" (non-LTS) release. **6.6** is referenced by third parties. **6.7 LTS** is announced for "later in 2026", with no date found. SOURCED |

## 2. What changed after 6.3 that matters to *this* engine

This engine is a compute-shader raymarcher on URP, running on an M1 under Metal.
It has a known stutter tail, and it has measurement discipline built on
cross-build comparisons. Filtered for that:

| Change | Version | Relevance here |
|---|---|---|
| **`CAMetalDisplayLink` for the macOS player** (opt-in: Player Settings, "Use MetalDisplayLink (Mac Player only)", or env `UNITY_USE_METAL_DISPLAY_LINK=1`). Unity: "decreases stuttering, improves frame pacing, and makes `Time.deltaTime` more stable". A follow-up fixed stutter when using it (UUM-112011). | 6.4 (6000.4.0f1); also listed in 6.5 notes | **HIGH.** Two things hang on it: our pre-existing stutter tail, and FPS_INVESTIGATION §0.1's finding that windowed capture is unreliable on this machine. **Our rigs measure frame time with `Time.unscaledDeltaTime`, so a change to how deltaTime is paced changes what every rig reports, not just what the player sees.** That makes it an evaluation item and a re-baseline item at the same time. |
| STP on Metal: dynamic-resolution corruption fixed | 6000.4.4f1 | LOW for now. Decision 10 parked upscaling, and STP needs depth plus motion vectors we don't emit (handoff §1). |
| STP: final post-process pass removed | 6.4 | LOW, for the same reason. |
| Known issue: "Metal: game freezes after command-buffer timeout" | 6.4 notes | **RISK** to check before any move: our kernels are long single dispatches. |
| DXC: compute shaders error on "show compiled code" | 6.4 known issue | LOW (editor-only), but it's the same area as §10 flag 7 (`use_dxc`). |
| Shader compilation: "PSO caching and the DXE compiler" | 6.6, per third-party roadmap reporting | **HIGH as a confound.** A compiler change alters generated code for `Raymarch.compute`. Every historical frame-time comparison would then carry a codegen difference. That's the same reason CLAUDE.md forbids retrying `use_dxc` casually. |
| Built-in RP deprecated; dynamic batching deprecated | 6.5 | NONE. We're on URP and draw nothing batched. |
| CoreCLR runtime migration: lifecycle API (6.5), fast enter-play default (6.6), experimental CoreCLR player (6.7), Mono removed (6.8) | 6.5 onwards (roadmap) | MEDIUM, later. Burst jobs (`ColumnSampleJob`, strict float mode) and the worker-thread generation path need re-verifying for determinism. §0.3 puts generation determinism under review. |
| "Surface Caching GI" (SCGI) | 6.7 (roadmap) | NONE directly. Our lighting is our own compute path. |

**Not found in any release notes:** MetalFX in URP. MetalFX for Unity exists as
a third-party Asset Store package, not an engine feature (SOURCED). Since
decision 10 parked upscaling, it isn't a reason to move.

## 3. What an upgrade costs here, and why the timing matters more than the version

ESTABLISHED from this repo's history:

- **Every frame-time number in the project is comparative.** Gate C,
  `PHASE_4_COMPLETION.md`, the `FLUID_*_RESULTS.md` A/Bs, the roof-skip and lava
  A/Bs all depend on "same binary pipeline, one variable changed". A compiler,
  runtime or frame-pacing change moves every arm at once, so no comparison
  across the upgrade is valid.
- `ShaderCompileCheck` can report a shader green that fails on the Metal
  player (CLAUDE.md). An upgrade must re-run the full build-log `Shader error`
  grep for every scene.
- The wave-intrinsic workaround (`groupshared` plus barrier) and the MSL floor
  stay as they are (decision 12). An upgrade must not quietly raise the Metal
  target.

So **the cost of an upgrade is mostly a re-baseline**: EditMode, all rigs, and
the sustained arms, re-recorded as a new "zero" for later comparisons. That's a
few hours of machine time and one session of reading results. It's cheap at
a phase boundary and expensive in the middle of a measured optimisation.

## 4. PROPOSED upgrade point

1. **Now: nothing.** Phase 7 is mid-measurement (roof skip, lava A/B, the grade).
2. **At the Phase 7 close, before the terrain-overhaul phase starts**: move to
   the **newest 6000.3.x LTS patch**. That means bug fixes only on the same
   stream, with the lowest codegen risk. Re-baseline once, and record the old and
   new numbers side by side in a short `UNITY_UPGRADE_REBASELINE.md`.
3. **In the same window, as a throwaway branch: evaluate `CAMetalDisplayLink`**
   on 6.5 (or 6.4's latest patch). Use one sustained golden-hour orbit arm and
   one Phase 4 rig run, on vs off. It's opt-in, so the A/B is a single toggle on
   one binary. Adopt the stream only if it measurably shrinks the stutter tail;
   Unity's claim is a claim until then.
4. **Target 6.7 LTS as the long-lived base for the large-world phase**, once it
   ships and has had one or two patches. The large-world work (the companion
   `DESIGN_LARGE_WORLD_AND_TERRAIN_OVERHAUL.md`) invalidates the frame-time
   baseline anyway: new far-field pass, new window shape. **Paying for the
   compiler and runtime re-baseline at the same boundary** means paying for it
   once.

What would change this proposal: if the display-link A/B in step 3 shows a
large stutter-tail win, bring the stream move forward to the Phase 7 close.

## 5. Checklist for whichever upgrade happens

- [ ] Take the build lock (`tools/unity-lock.sh`); an upgrade reimport is a long Unity process.
- [ ] New editor path in every `run-*.sh` (currently hard-coded to 6000.3.10f1).
- [ ] EditMode green (`run-editmode-tests.sh`), with 0 `error CS` in the log.
- [ ] Every scene builds with 0 `Shader error` in its build log (not just `ShaderCompileCheck`).
- [ ] `run-phase7-rig.sh` green, since Gate 3's GPU-vs-CPU oracle catches codegen changes in the shadow ray.
- [ ] Burst: `ColumnSampleJob` still byte-identical to Mono (the existing benchmark asserts this).
- [ ] Re-baseline arms recorded before any new optimisation is measured.
- [ ] Metal target and MSL version unchanged (decision 12).

## 6. Sources

- [Unity 6000.4.0f1 release notes](https://unity.com/releases/editor/whats-new/6000.4.0f1): CAMetalDisplayLink, STP final-pass removal, macOS 12 minimum, known issues
- [Unity 6000.4.4f1 release notes](https://unity.com/releases/editor/whats-new/6000.4.4f1): STP + dynamic resolution on Metal fix
- [Unity 6000.5.1f1 release notes](https://unity.com/releases/editor/whats-new/6000.5.1f1) and [6000.5 alpha/beta notes](https://unity.com/releases/editor/alpha/6000.5): MetalDisplayLink opt-in wording
- [Unity 6000.3.17f1 release notes](https://unity.com/releases/editor/whats-new/6000.3.17f1): newest 6.3 LTS patch found
- [Unity 6 releases & support](https://unity.com/releases/unity-6/support) and [Unity 6.3 LTS announcement](https://unity.com/blog/unity-6-3-lts-is-now-available)
- [DEV Community: "Unity 6.5 is here"](https://dev.to/oceanviewgames/unity-65-is-here-should-your-studio-upgrade-1hef): 6.5 status, BIRP deprecation, CoreCLR staging (third party)
- [StraySpark: Unity 6.4 to 6.8 roadmap](https://www.strayspark.studio/blog/unity-6-4-to-6-8-roadmap-indie-developers-2026): 6.4 ship date, 6.6 compiler/PSO items, 6.7 SCGI (third party, roadmap)
- [Unity Discussions: MetalFX Upscaling for Unity](https://discussions.unity.com/t/released-metalfx-upscaling-for-unity-boost-your-framerate-on-apple-silicon/1723837): third-party asset, not engine
