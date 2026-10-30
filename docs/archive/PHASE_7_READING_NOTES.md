# Phase 7 Reading Notes — what each document constrains, one line each

**Date:** 2026-09-12
**Purpose:** the Stage 0 deliverable. One line per document saying what in it
constrains Phase 7 (Lighting & Day/Night). A document that constrains nothing
says so. This exists so the owner can check that the record was *read* rather
than pattern-matched, and it is where anything the scoping prompt got wrong
gets caught.

**EditMode baseline at phase start, run before a line of Phase 7 code:**
`PASS 499  FAIL 0  SKIP 0` (`./run-editmode-tests.sh`, 2026-09-12) — matches the
figure the scoping prompt states, so the starting point is confirmed rather than
assumed.

**Method:** read in the tiers the prompt specified, which is §0.3's
header-first rule — Chapter 0 and Appendix A/B first, then the phase spec, then
the measurement history, then the rest.

---

## TIER 1 — the constitution and the layouts

| Document / section | What it constrains for Phase 7 |
|---|---|
| **§0.1 Twelve Invariants** | inv 1 (CPU sole terrain writer) — a torch is a terrain byte written via `SetVoxel`, never a GPU-side entity. inv 2 (bitwise integer coords) — shadow-ray DDA reuses the existing integer DDA, no float division. inv 4 (prove on CPU before GPU) — `ShadowRayReference.cs` exists before one line of shadow HLSL. inv 5 (one new untrusted thing) — the shadow ray is that thing; point lights are a sampling loop over a buffer and are not a second traversal. inv 8 (limits defined once in `EngineConfig`) — `MAX_ACTIVE_LIGHTS` and the shadow quality level both live there. inv 9 (dumbest thing that works) and inv 10 (never trust untested code). inv 12 — a plausible cause is a hypothesis with a measurement attached. |
| **§0.2 Hard Limits Table** | `MAX_ACTIVE_LIGHTS = 32` is the v1 value and is the number to implement; its "raise only if" condition is *Phase 7 measurement*, which means this phase may propose a raise **with data** and must not raise it to fix a symptom. `MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME` is marked raise-**never**; the light buffer must not be funded by touching it. `LOD_TIERS` is 3, raise never for v1 — so no shadow fix may add a tier. |
| **§0.3 What AI may / may not touch** | The light buffer is inside contract: §3.9's sync-contract table already lists "light sources" as a CPU→GPU row, so **adding** it is additive, not a redesign. `CoordMath`, chunk/brick/clipmap layout and serialization, generation determinism and the §4.4 streaming state machine are off-limits and Phase 7 touches none of them. The **3-attempt debugging rule** applies per subsystem: after ~3 failed attempts, regenerate from spec+tests or stop and move to the next independent stage — no 4th cleverer patch. **Header-first prompting** is why this file's reading order is what it is. **The Sibling Pattern** is why `ShadowRayReference.cs` is a permanent fixture, not scaffolding. |
| **Appendix A.6 `LightSource`** | The exact GPU struct: `float x,y,z / r,g,b / range / k` — eight floats, 32 B, in that order. C# mirror is `[StructLayout(LayoutKind.Sequential)]` with identical field order. No invented fields (no intensity, no enabled flag, no padding of my own). |
| **Appendix A.7 `MaterialData`** | 32 B × 256: `albedoR/G/B, emissive, viscosityDrag, density, flags, tickInterval`. `emissive` is the field §6.6 and C.9 depend on. The struct exists in both `StructHeaders.cs` and `.hlsl` today. |
| **Appendix A.8 clipmap entry** | `[31]` uniform flag, uniform material in `[7:0]`, dense index in `[30:0]`. The shadow ray reads the *same* entries the primary ray reads — this identity is the whole reason §6.5 says acne is impossible by construction. |
| **Appendix B bitfields** | `MaterialData.flags` bit **[4] IsEmissive** is the spec-fixed bit. `MaterialRules.IsEmissive = 1u << 4` already matches. Do not reorder. |
| **Appendix C.5 / C.9 / C.10 / C.11** | C.5 is the LOD boundary derivation the shadow ray's tier selection reuses. **C.9** is the combine, verbatim: `albedo × (sunColor·sunVis·max(0,N·L) + pointLights + ambientSky) + emissive`, **then VAO**. **C.10** is the penumbra: track `m = min(clearance_i / t_i)` over near-miss cells, `sunVis = smoothstep(0, k_soft, m)` on miss, `0` on hit, `k_soft→0` = hard. **C.11** is the attenuation: `1/(1 + d²·k)` summed over lights within `range`, `k` an art constant. |
| **CLAUDE.md** | Standing project rules. **Three conflicts with this prompt are recorded in §CONFLICTS below rather than silently resolved.** Otherwise it constrains: read a file's on-disk content before editing it; write complete files or verbatim-anchored patches; `Assets/CoreEngine/Mirror/` is namespace `VoxelEngine.Memory` except `CascadeTierPool`/`LODCascadeManager`; `WINDOW_CHUNKS_Y=16` ≠ `MIRROR_CHUNKS_Y=4` deliberately; never attribute a coordinate bug to floating point. |

---

## TIER 2 — this phase's spec

| Section | What it constrains for Phase 7 |
|---|---|
| **§13 Phase 7** | The whole phase. Four files in order (`LightSourceBuffer.cs`, `SunCycle.cs`, the shadow ray in `Raymarch.compute`, `Diagnostics/`); the scene (`Phase7_Cycle` — cave with a large chamber, a mountain in view, placeable torches, a time scrubber); five acceptance bullets; the two budget gates (≤9.0 ms raymarch+shadows, ≤13.0 ms combined with fluid), **both Performance-State-checked and therefore both unrunnable here** (see Tier 3). Its "if stuck" note names the method: the shadow ray is the Phase-2 DDA with a new origin and an early-out — diff it against `RaymarchReference` with the shadow origin/direction substituted. Its solo-dev note says the real work is tuning by eye in real time, which is what `LightingConfig.json` is for. |
| **§6.5 Lighting — two systems, one combine** | The governing section. **Origin rule:** centre of the first voxel *adjacent* to the hit face, one cell off along the face normal — no bias term, ever; acne and Peter-Panning cease to be a possible bug category by construction. **LOD rule (8.2):** `shadowLOD = SelectLODTier(length(sampleWorldPos − cameraWorldPos))` — by **camera** distance to the sample point, not the shadow ray's own travel; the 8.1 travelled-distance rule was wrong and produced detached shadows. **Early exit:** occlusion-only, skip sky pixels, clamp at max distance (island diagonal). **Wave-uniform guard (8.4):** `WaveActiveAnyTrue(hasPrimaryHit)`, "worth building in from the start rather than retrofitting after a Phase 7 budget miss." **Four fallback rungs** (0 full penumbra / 1 half-res distant with neighbour reuse / 2 hard only / 3 off) — "the game ships either way." **Night:** below horizon, skip shadow rays, sun term 0, ambient carries the night. **Point lights:** pure attenuation, no occlusion ray — "a torch behind a wall does not light the far side" is stated as the *v1 limitation*, which is in tension with §13's acceptance wording (see §FLAGS). **Ambient:** `ambientSky` is a cheap hemisphere term, also the bounce stand-in for cave mouths, and "underground reads near-zero" — also in tension (see §FLAGS). |
| **§3.8 Lighting storage** | Flat `GraphicsBuffer<LightSource>`, no BFS, no per-brick clustering in v1. The light cap exists to stop the 2M-pixels × N-lights death spiral. **Degradation rule is a hard requirement:** over the cap, *furthest-from-player lights drop first*, and placing more than the cap must **not** silently ignore new torches. `MAX_LIGHTS_PER_BRICK` (8) is explicitly v1.5, not built in v1. |
| **§6.1 Aesthetic contract** | Flat-shaded axis-aligned cubes, normals from the DDA step mask, no smoothing. "The look is carried by albedo, the lighting of §6.5, and voxel ambient occlusion (VAO)." Beauty comes from lighting and atmosphere, never geometry. Palette/mood/post are explicitly deferred to *Phase 7* visual prototyping — i.e. this phase is allowed to change the look. |
| **§6.4 LOD cascades** | 3 tiers, "one traversal algorithm parameterized by voxel size — **and it serves shadow rays too**." So the shadow ray must reuse the tier machinery, not grow its own. |
| **§6.6 Materials** | "The 1-byte ID indexes the Material Registry for albedo/emissive; **emissives additionally enqueue into the light system when placed**." That enqueue is the §8.3 edit path, not a second edit path. |
| **§2.2 Frame budget** | Two lanes. GPU: raymarch+shadow ≤9.0 ms, fluid CA ≤3.5 ms, light drain ≤0.5 ms, steady total ≤13.0 ms. **The fallback order if the lane does not fit:** shrink fluid active radius → two-speed fluid tick → shadow fallback rungs. But the same section also ranks **shadows as degrading before fluid** ("does the game stop being fun without X" answers differently). Those two orderings disagree; §10 of the prompt says measure both and flag, do not pick. |
| **§11.1 CPU budget** | Terrain upload ≤1.0 ms; gameplay ≤1.5 ms. Nothing Phase 7 adds is a per-frame CPU cost of consequence except the light drain and the config poll. |
| **§11.2 GPU budget** | Restates ≤9.0 ms and ≤13.0 ms, and states explicitly that **the true combined gate can only be measured in Phase 7** — "an earlier draft mistakenly asked Phase 5b to measure this before shadows existed." |
| **§11.3 Memory** | "Light source buffer — small (sparse point lights) — ~1 MB." 32 lights × 32 B = 1,024 B, four orders of magnitude inside that line. Not a constraint in practice; recorded so nobody re-derives it. |
| **§3.9 Sync contract** | The CPU→GPU row already names **"light sources"**. This is the sentence that makes the light buffer in-contract under §0.3. The closed rule is unchanged: CPU never reads GPU terrain, GPU never writes terrain. |
| **§3.10 Authoritative-byte rule** | "Shadow rays read one authoritative source with zero knowledge of the fluid pool. Live pouring fluid renders and shadows correctly **for free**." This is why §13's "pour water off a ledge → moving shadow" is an acceptance *consequence*, not a feature to build. Same for "bomb a cave roof at noon: sunlight reaches the floor the same frame." |
| **§10.3 Debug passes & HUD** | Item **6** is mine: sun-visibility view + shadow-ray step-count heat. Item **7** adds sun angle / time of day to the HUD. Items 1–5 already exist. |
| **§10.4 Validation suite** | The per-subsystem ring-buffer dump: "if a single ray's step count exceeds a threshold, log its origin, direction, and last ~10 clipmap indices traversed." Phase 7 adds the shadow-ray form of that, same shape. |
| **§12 Module organization** | `LightSourceBuffer.cs` and `SunCycle.cs` are already named in the `Rendering/` listing. `CoreEngine` has no upward refs; `Game` sees only public interfaces. Every `.compute` includes `CoordMath.hlsl` + `StructHeaders.hlsl`. **The freeze commitment:** additive hooks to a passed system's internals are normal; redesign is not. |

---

## TIER 3 — the measurement history (where sessions go wrong)

| Document | What it constrains for Phase 7 |
|---|---|
| **AMENDMENT_8_9** | **Rule 1: no Xcode, ever, for any performance measurement** — so §13's "Xcode capture" gate cannot be run. **Rule 2: Performance State cannot be read at all** — so "Performance State checked" has no equivalent. **Rule 3: every number is VERIFIED (with its source) or OPEN; never invent one to fill a gap.** §4 records the Xcode/spec-gate conflict as *still open and never formally resolved*, and recommends formalising a substitute — which is exactly what `AMENDMENT_8_12_PHASE7_GATE.md` is for. Also: 960×540 internal is the benchmarking resolution and was never compared against native 1080p. |
| **AMENDMENT_8_10** | **CORRECTION NOTICE: `gpuFrameTime` is inflated ~2.6–2.7× and a result was retracted for quoting it** — so no Phase 7 number may come from it. §3b is the method that works: the two fixes argued from reading the code barely moved the number; the one found by *looking at StepHeat / LODTierView captures first* closed the gap. §5.1: ~half the frame is dependent-read memory latency, not traversal work, so step-count reduction is the wrong lever (measured wrong twice). §5.3: diagnostic branches cost ~12%, measured — **keep paying it**, and if it ever must be reclaimed use `multi_compile` keywords, not deletion. §5.7: the tier-1/2 traversal branch has **no CPU oracle** — shadow rays inherit that gap and this phase must say so rather than claim coverage. §6.4: "the doc's three shadow fallback rungs are likely to be *needed*, not optional — plan for engaging them rather than being surprised." §7: the certified defaults (mode 4, packed mips, cascade on). |
| **FPS_INVESTIGATION_RESULTS** | §0.1 **windowed capture is worthless on this machine** (26.9 vs 122.8 FPS back-to-back); fullscreen reproduces to 2.9% — fullscreen only. §0.2 **percentiles mislead here**: 120 Hz ProMotion, exactly 600 of 1800 frames over 16.6 ms in *every* isolation arm regardless of workload — use frames ÷ elapsed wall time, never p99 as a headline. §0.3 ordering confounds produce clean, convincing, wrong results — counterbalance everything. §0.4 is the method that survived. §2.1 the current baseline: **69.8 ± 0.4 FPS, mean frame 14.33 ms, ~2.34 ms of margin** — that is what shadow rays must fit inside, and the prompt correctly says re-establish it in-session rather than compare across sessions (§3.2's cross-session comparison produced a wrong number). §3 two measured-and-rejected shader fixes: air-mip reuse in the tier 1/2 path (67.0 → 44.9 FPS) and mirroring tier 0's dense inner loop (66.0 → 61.4). §5.1 the CA region passes are the largest single cost in the frame (87 FPS with the CA suspended vs 65.5 with it running) — relevant to the combined arm. |
| **PHASE_2_COMPLETION §5** | The same wall, hit before: §13 Phase 2's Xcode gate "cannot be executed as written", for the same two reasons. What was measured instead: `GroundHorizon` **11.95 ms** at 960×540, mode 4, cascade on. **Against §11.2's ≤9.0 ms raymarch line it does not pass** — and that 9.0 ms line is supposed to *include* Phase 7's shadow rays. Amendment 8.9 §4's conflict is recorded as still formally unresolved. |
| **PHASE_2_COMPLETION §6** | Nine carried-forward gaps. The ones that bind Phase 7: **#2 no CPU oracle for cascade traversal**; **#4 ~half the frame is memory latency**; **#5 upscaler cost unmeasured** (it draws against the same budget); **#6 two pre-existing render artifacts** (sky-coloured horizon gaps, grazing-angle terracing) present with the cascade both on and off — so if I see either at golden hour it is not mine. |
| **PHASE_3_COMPLETION §5** | **"Plan against 6 ms, not 16.67 ms."** Worst named pose `GroundHorizon` 10.69 ms at 960×540; native 1080p would be ~4× and blow the budget immediately. Also: **the LOD cascade is a correctness feature, not just a perf feature** — disabling it is *faster* (8.00 vs 9.64 ms) but produces 18× more false misses, so it must never be traded away for frame time. Air-mip earns its keep (disabling costs 44%). Iteration cap above 400 is essentially free. |
| **PHASE_3_COMPLETION §8** | The established rig workflow, kept verbatim so it survives context loss: scene setup, EditMode green *first*, standalone build, report ending in a **COMMIT READINESS block stating correctness / performance / visual separately**, `DEFECTS_<Pose>.png` annotated copies, and the interpretation rules (use `wall_avg`; check stddev; a frame-time figure without a stated internal resolution is meaningless). This is the shape `phase7_report.txt` must follow. |

---

## TIER 4 — the rest of the record

| Document | What it constrains for Phase 7 |
|---|---|
| **PHASE_1_COMPLETION** | **Nothing directly.** Establishes the memory model, `CoordMath`, and the `SetData` vs `LockBufferForWrite` decision the light buffer inherits by analogy (it is written rarely and read per-pixel, i.e. the same shape as the clipmap, so `SetData` is the matching choice). |
| **PHASE_4_COMPLETION** | **Almost nothing directly.** Its §6.5 note restates that tiers 1/2 have no CPU oracle. Its §3.1 records that the upload-budget p99 is the one red gate and that the write mechanism is settled and load-bearing — so Phase 7 must not add per-frame upload pressure. §11.3's re-derived pool caps are unaffected by a 1 KB light buffer. |
| **PHASE_5A_COMPLETION** | **Nothing.** The CPU fluid oracle. Its value here is precedent: the Sibling Pattern done properly, which is the model `ShadowRayReference.cs` follows. |
| **PHASE_5B_COMPLETION** | **Nothing directly.** Records that two gates (the Metal claim race, and any Xcode-verified timing) cannot be closed at all in this workflow — the same wall Phase 7's budget gate hits. |
| **PHASE_5C_COMPLETION** | **Nothing directly.** Precedent: the defect class was found *by a human clicking*, not by a rig. That is the argument for Stage 7's Playground work being a deliverable rather than a nicety. |
| **PHASE_6_COMPLETION** | **§2.3 is the important one:** §8.6 read `MaterialData.density`/`viscosityDrag` "from the Registry" and **nothing in the project had ever populated them** — the struct existed, the numbers did not. `emissive` is the same field class and §6.6 depends on it, so Stage 0(c) established its real state rather than assuming (answer: **no A.7 registry buffer is populated anywhere**; see §FINDINGS). **CARRY-FORWARD:** Amendment 8.11 undecided; feel-based items never chased; the sustained-load stutter not fixed and must not be reported as fixed; two ranked deferred optimizations (CA region passes scaling with resident tiles; the LOD cascade air-mip gap) — all four out of scope. Open item 4: §2.2's GPU-lane budget is a **permanent toolchain limitation, nothing to resolve, ever, in this workflow.** |
| **PHASE_6_QA_PASS** | **Nothing directly.** Per-system frame-time QA under natural heavy use; the shape of a "one mode per process launch" rig with 300 s cooldowns, which `run-phase7-lighting-ab.sh` copies. |
| **AMENDMENT_8_8_OCCUPANCY_BITMASK.md** | **THE FILE DOES NOT EXIST.** Neither does an 8.7 document. Both amendments are *in force* and *implemented* (`OccupancyMask.cs`, `AirMip*.cs`) and are cited by CLAUDE.md's ground-truth order and by PHASE_2/3_COMPLETION's "amendments in force" headers, but no `.md` for either is in the repo and `git log` shows none was ever deleted. **Flagged, not worked around:** I read `OccupancyMask.cs` and the citing sections instead. Neither constrains Phase 7 — the shadow ray reuses the same traversal these accelerate, so it inherits them without knowing about them. |
| **DESIGN_NOTE_7_2_TILED_ACTIVE_SET** | **Nothing directly.** The sparse fluid tile substrate. Relevant only in that the combined arm's fluid load runs on it, and that its §12.2 explains why `run-phase6-brushguard.sh` is red for rig-staleness reasons. |
| **DESIGN_NOTE_7_4_ACTIVE_RADIUS** | **Nothing directly.** §7.4's wake radius. Relevant only to Stage 6(e): if I ever touch `FLUID_ACTIVE_RADIUS_VOXELS`, the reduction must be written as a visible fraction of the constant (`/ 10`), never a literal — the 1280-voxel/128-voxel unit error already produced a retracted "fix". |
| **FLUID_PERFORMANCE_AB_RESULTS** | **§3 is a real constraint on my combined arm.** Under live fluid, `StreamManager.LastUploadMs` p50 goes 0.001 → **6.595 ms**, and **6.36 ms of that is the LOD cascade re-downsampling fluid-touched chunks** (6.35 ms of it `LODDownsampler` alone). This is a **CPU main-thread** cost, it is known, and it is *not* shadow/fluid GPU contention. If arm (ii) breaches, this is the first thing to rule out before concluding §13's "new information". The recommended fix (frame-budgeting cascade rebuilds) is explicitly *not* Phase 7's. |
| **FLUID_TILE_CAP_RESULTS** | **Nothing directly.** The cap arithmetic behind `FLUID_TILE_POOL_CAPACITY = 1024` and `MAX_ACTIVE_FLUID = 750,000`. §12.1/§12.2 explain the two rigs CLAUDE.md records as rebased or stale. |
| **FLUID_SCALE_ARCHITECTURE_RESULTS** | **Nothing.** |
| **OPTIMIZATION_CANDIDATES** | **Nothing implemented, and its preamble is the constraint:** "we have no trustworthy performance data", per-kernel GPU timing does not exist, nothing here may be treated as "this is the slow part". Also: no §0.2 hard limit is raised anywhere in it, and nothing touches §0.3's review list. Phase 7 holds the same line. |
| **MORNING_REPORT** | **§1.3/§1.4 set the standard for touching an assertion.** Tie-break variance was established *by experiment first* (a `TieBreakSalt` that moved the CPU's own layout), and only then was the assertion **rebuilt** — not loosened — with a comment saying why the old check was wrong. That is the bar if I ever believe a Phase 7 assertion is wrong. |
| **PLAYGROUND_GUIDE** | Its boxed rule is a deliverable: a phase is not done when its rigs go green, it is done when you can walk in and *feel* it — and its five steps (wire into `Playground.cs`; add to the **F2** overlay, "the copy that cannot go stale"; add a §8 row; update §3 and §4; state in §7 what is not reachable). §8's row 7 currently reads *"Lighting: (not started — do not begin without a scoped prompt)"*. §4's "Live tuning" section is the model `LightingConfig.json` copies. **Two stale claims found in it** — see §FINDINGS #7. |
| **AMENDMENT_8_11_RENDER_RANGE** | **DRAFT, NOT ADOPTED, out of scope, read only to recognise proximity.** The island is ~1,909 m across and the renderer draws 290 m. **Where Phase 7 comes near it:** §6.5 says the shadow ray "clamps at max distance (island diagonal)". Taken literally that is ~2,700 m — far outside both the 290 m render range and the 409.6 m resident window, so a literal reading would walk every shadow ray into the window-bounds guard for nothing. I clamp at the same `maxDist` the primary ray uses (tier 2's outer bound), which is the *effective* island diagonal in this build because no data exists past it. **That is a deliberate, flagged reading of §6.5, not an implementation of 8.11**, and it is recorded in `AMENDMENT_8_12_PHASE7_GATE.md` rather than left implicit. |

---

## CONFLICTS between CLAUDE.md and this prompt — flagged, not silently resolved

The prompt says: *"If anything in CLAUDE.md conflicts with this prompt, STOP and
flag the conflict; do not silently pick."* Three exist. None of them blocks
starting, so they are recorded here with the reading I am proceeding under, and
each is re-stated in `PHASE_7_COMPLETION.md` for the owner.

**C1 — "The ONLY trusted frame-time source" vs Stage 6's instrument. REAL, and
it matters.**
CLAUDE.md *Measurement discipline* says the only trusted frame-time source is
`./run-acceptance-rig.sh`'s output. The prompt's Stage 6 mandates fullscreen,
frames ÷ elapsed wall time, 300 s cooldowns, counterbalanced, same-session,
reusing `PlaygroundCapture`'s levers.
**These genuinely disagree, and the newer evidence is on the prompt's side.**
`run-acceptance-rig.sh` launches `Builds/Phase4Acceptance.app` with
`open -n -W ... --args -cleardeltas` and **neither the script nor
`Phase4AcceptanceRig.cs` nor `Phase4Bootstrapper.cs` ever calls
`Screen.SetResolution` or sets fullscreen** (verified by grep, 2026-09-12) — so
it runs windowed, which `FPS_INVESTIGATION_RESULTS.md` §0.1 (written later,
2026-09-11/12) demonstrates is worthless on this machine: 69.6 vs 26.9 FPS for
identical back-to-back runs, and "fluid ON" measuring *faster* than "fluid OFF".
**Proceeding under:** the prompt's method for Phase 7's own numbers, written up
as a substitute gate in `AMENDMENT_8_12_PHASE7_GATE.md` with this reasoning on
the record. **Not doing:** changing `run-acceptance-rig.sh`. Making it
fullscreen would silently invalidate every historical Gate C comparison in
`PHASE_4_COMPLETION.md`, and that is a decision for the owner, not a cleanup.
**For the owner:** CLAUDE.md's *Measurement discipline* section may want a
sentence distinguishing "the trusted source for the Phase 4 *gates*" from "the
trusted method for a *frame-rate* number". Flagged, not edited — the prompt
says do not rewrite standing rules.

**C2 — "Stop and report after every iteration / do not chain more than 3
iterations unsupervised" vs "work autonomously to completion." NOT a real
conflict, on inspection.**
CLAUDE.md's rule is scoped to *the build-run-review loop* — iterating toward a
stable build by making performance-affecting changes and re-running the rig.
The neighbouring rule is the operative one: *"After a change intended to affect
performance, run the full rig once, report the specific numbers that moved, and
STOP... Do not chain multiple unverified performance changes."* Stage 6 makes
**no** performance-affecting changes — it measures five arms of one binary with
a config number varied. **Proceeding under:** the prompt's autonomy, while
honouring the actual constraint — if Stage 6 produces a breach, I measure and
report it and do **not** chain speculative fixes on top.

**C3 — CLAUDE.md's "Current state" paragraph is stale, and says so itself.**
It describes Phase 4 as the current work ("Current work is throughput and
frame-time"). Phase 6 is closed and merged (`cbdc90f`, `4ee6b63`). The paragraph
carries its own instruction — *"Don't assume this paragraph is still accurate"*
— so this is a documentation-freshness item, not a conflict. Deliverable 8
updates it as a factual section.

---

## FINDINGS — Stage 0's questions, answered from the code

**(a) What `Raymarch.compute` does at the shading step.**
Traversal resolves `hit / voxel / mat / normal / currentDist / tierAtHit`, then
**seven** debug views return early (`_DebugModePacked.x`: 0 Beauty, 1 StepHeat,
2 UniformDense, 3 Normals, 4 NonExitHeat, 5 VoxelGrain, 6 LODTier), then Beauty
shades. Beauty has two paths behind `_UseDevColors` (default **on**): the
authored path — `MaterialAlbedo(mat)` (12 hand-authored colours mirroring
`MaterialPalette`, magenta fallback), per-voxel `VoxelHash` grain ±9%,
hemisphere tint `lerp(0.62, 1.12, upness)` behind `_ShadeHemi`, corner AO via
`FaceAO`/`AoSolidAt` behind `_ShadeAO`, a softened `lerp(0.80, 1.0, ndotl)`
directional term against a **hardcoded** light direction
`normalize(float3(0.5, 1.0, 0.2))`, a self-lit branch
(`MaterialIsSelfLit(mat)` — lava only, `mat == 11u`) that skips AO/hemi/N·L and
pushes `albedo × 1.35 + 0.06`, and distance fog toward `_SkyColor` behind
`_ShadeFog`; and the legacy flat-grey path for byte-identical A/B against
pre-palette screenshots. **Five traversal modes** live in one kernel
(`_TraversalMode`, certified default 4 = DenseSkip) and all seven debug views
are compiled in — Amendment 8.10 §5.3 measured that at ~12% and says keep
paying it. Two sibling kernels exist (`RaymarchStripped.compute`,
`RaymarchMemoryProbe.compute`). **All three presentation terms are already
flags**, and `RaymarchFeature.cs`'s own comment says they are flags precisely
"so Phase 7's real lighting (§6.5) can switch any of them off without unpicking
anything." That is the seam Phase 7 was left.

**(b) `MAX_ACTIVE_LIGHTS` in `EngineConfig`?** **No. It does not exist.** No
light-related constant of any kind is in `EngineConfig.cs`. Phase 7 adds it at
§0.2's value, 32.

**(c) Is the §3.9 Material Registry `GraphicsBuffer<MaterialData>` populated?**
**No — and it is worse than PHASE_6 §2.3's case.** There, the struct existed and
two *fields* were unpopulated. Here **no `GraphicsBuffer<MaterialData>` is
created anywhere in the project.** The A.7 struct exists in both
`StructHeaders.cs` and `StructHeaders.hlsl` and is referenced by nothing.
`FluidGpuSimulation.UploadMaterialRegistry()` uploads **four separate flat
`uint` arrays** (`_materialFlags`, `_materialTick`, `_reactSelf`, `_reactOther`)
— deliberately, per its own comment, "restricted to what the CA reads" — not the
A.7 struct. And `MaterialRules` has **no emissive table at all**: `IsEmissive`
exists as a *flag bit* (set on Lava only) but there is no per-material emissive
*strength*. `MaterialPalette.IsSelfLit` is a shading cheat whose own comment
says "NOT a lighting system: nothing here emits light onto anything else. Real
emission arrives with Phase 7 (§6.5)." **Consequence for Phase 7:** the emissive
data must be created, additively, in `MaterialRules.cs` beside the density and
drag tables that PHASE_6 §2.3 added for exactly this reason.

**(d) Any in-game clock for `SunCycle` to read?** **No.** No `TimeOfDay`,
`WorldClock`, `GameClock`, `dayLength`, `SunCycle`, `_SunDir` or `sunDirection`
anywhere in `Assets/`. `SunCycle.cs` owns the clock as well as the mapping.

**(e) Does VAO exist?** **Functionally yes, by another name — and its
provenance is not the Phase-2 build step.** The literal token "VAO" appears
nowhere in `Assets/`. But `FaceAO()` in `Raymarch.compute` is a textbook voxel
ambient-occlusion term: for each of the four corners of the hit face it samples
the two edge neighbours and the diagonal via `AoSolidAt`, and bilinearly
interpolates across the face using the ray's actual hit position. That *is* what
C.9 means by "then VAO". **The honest caveat:** it was added in a later
*presentation* pass (its own comments reference Playground captures), not as
§13 Phase 2's file-4 build step, and `PHASE_2_COMPLETION.md` never mentions it —
so the Phase-2 build step was, strictly, never ticked; an equivalent arrived
later by a different route. **I am therefore not inventing one** (which is what
the prompt guards against): C.9's trailing VAO term is `FaceAO`, kept exactly as
it is and applied in C.9's position. Recorded rather than quietly reused.

**(f) How scenes are created, and the `run-*.sh` contract.** Scenes are
*generated*, not hand-authored: `Assets/Editor/Phase5aSceneBuilder.cs` holds one
`[MenuItem]`-decorated `public static void Generate<Name>()` per scene, each
building an empty scene from code and `EditorSceneManager.SaveScene`-ing it to
`Assets/Scenes/<Name>.unity`. `Assets/Editor/CommandLineBuild.cs` holds one
`Build<Name>Standalone()` per scene with an **explicit** scene list (never Build
Settings' checkboxes), always `BuildOptions.None` — **release, never
Development** — plus `DisableAppNap`. A `run-*.sh` is then: generate the scene
headlessly (`-batchmode -quit -nographics -executeMethod ...SceneBuilder.X`),
build (`-batchmode -quit -executeMethod CommandLineBuild.Y`), grep the build log
for `Shader error`, assert the `.app` exists, `open -n -W "$APP" --args <flags>`,
find the newest timestamped folder under
`~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/<Rig>/`,
and `cat` its report. `-quit` **is** correct for `-executeMethod` and is never
used with `-runTests`. One config per process launch; cooldowns are the shell
script's job.
**A trap recorded in the builder's own comments, which Phase 7's builder must
avoid:** a scene builder stamps its values into the scene on every
regeneration, so raising a `[SerializeField]` default in the component does
**nothing** — the builder's value wins. The tell was an identical number across
two different builds. Phase 7's builder therefore sets every value explicitly
and references the `EngineConfig`/`LightingConfig` constants rather than
retyping them.

**(g) Not asked, but found and load-bearing:** there is **no `SelectLODTier`
function**. §6.5 writes the shadow-ray LOD rule as
`shadowLOD = SelectLODTier(...)` as if one existed; in fact tier selection is
four inline lines in `CSMain` (`Raymarch.compute:701-706`) comparing
`currentDist` against `_TierOuterRangeVoxels.x/.y`. For the primary ray
`currentDist` *is* camera distance, so the existing code is already correct by
accident of the primary ray's origin. A shadow ray's `currentDist` is **not**
camera distance — it is exactly the 8.1 travelled-distance rule §6.5 says was
wrong. Phase 7 extracts those four lines into a real `SelectLODTier(float
camDistVoxels)` and calls it from both sites, so the rule is defined once
(§0.1 inv 8) and the shadow call site is forced to pass camera distance
explicitly.

**(h) Not asked, but needed:** there is **no `Materials.Torch`**. Ids 0–13 are
taken; 14 is free. Adding it is additive and must stay out of
`ContentVersionHash()` — the same treatment `Lava`/`Honey`/`Obsidian` already
get, and for the same stated reason (world generation never places them).

---

## FLAGS raised by the reading itself — for the owner, not decided here

These are recorded now because the *reading* produced them; they are costed and
re-stated in `PHASE_7_COMPLETION.md`.

1. **Point lights through walls.** §6.5 and §3.8 specify pure attenuation with
   no per-light occlusion ray, and §3.8's entire cost model (2M pixels × N
   lights) assumes it. §13's acceptance bullet says a torch "does not bend
   around a wall". With no occlusion test a torch behind a wall **lights the far
   side** — that is *leaking*, not bending. The literal §6.5/§3.8 reading is
   what gets implemented; the consequence gets screenshotted; the ambiguity gets
   flagged with the cost of the alternative. No per-light shadow rays added.
2. **Near-zero ambient underground.** §6.5 requires "deep cave genuinely dark"
   *and* "a cheap hemisphere term" *and* ambient as the bounce stand-in for cave
   mouths. A normal-based hemisphere term lights caves fine — the three
   requirements are not simultaneously satisfiable by a plain hemisphere.
3. **§2.2's two fallback orderings disagree** (fluid-first in the numbered list,
   shadows-first in the prose). Measure both, flag, do not pick.
4. **The art constants are placeholders.** `k_soft`, C.11's `k`, torch range and
   colour, the ambient curve, the sun-colour ramp, day length. Shipped sane,
   exposed in `LightingConfig.json`, awaiting the owner's eye.
5. **VAO's provenance** (finding (e)) — an unticked Phase-2 build step that was
   satisfied later by a different route.
6. **`AMENDMENT_8_8_OCCUPANCY_BITMASK.md` and an 8.7 document do not exist**,
   though both amendments are in force and cited as ground truth by CLAUDE.md.
7. **`PLAYGROUND_GUIDE.md` carries two stale claims.** §7 says "Not a
   demonstration of the shipped active radius. The radius here is a demo value
   10× smaller than the engine constant", and §9 repeats "The active radius is a
   scene demo value (128 voxels), not the shipped 1280." Both were true and are
   no longer: `Playground.cs:159` now reads
   `_activeRadiusVoxels = EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS`, which
   `FPS_INVESTIGATION_RESULTS.md` §2.3 records as the deliberate fix for the
   unit-error incident. Corrected in the same pass that adds the Phase 7 rows,
   and called out here so the correction is visible rather than slipped in.
