# Phase 7 Completion Record — Lighting & Day/Night

**Project:** Voxel Terraria 1 Byte BrickMap
**Spec:** `ARCHITECTURE_v8.6.md` §13 Phase 7, §6.5, §6.6, §3.8, §3.9, §3.10, §10.3, §10.4, §2.2, §11.2
**Amendments in force:** 8.8 (occupancy bitmask), 8.9 (LOD & resolution, and its
§0 hard rules), 8.10 (LOD cascade performance, and its CORRECTION NOTICE),
**8.12 (this phase's substitute budget gate — new)**
**Date:** September 12, 2026
**Branch:** `phase7-lighting`
**Hardware:** Apple M1 Air (fanless, 8 GB unified memory)
**Engine:** Unity 6000.3.10f1, release standalone

**EditMode:** `PASS 550  FAIL 0  SKIP 0` (baseline at phase start: 499 — 51 new)
**Acceptance rig:** `run-phase7-rig.sh` → `PASS 28  FAIL 0`, 15 screenshots
**Shader:** `Raymarch.compute` compiles clean in a release standalone build

> **This document does not say Phase 7 is complete.** It records what was built,
> what was proven, what was measured, what was disproved, what measured worse,
> and what is open. The owner closes the phase.

---

## 0. Read this first — the four things most likely to be misread

1. **§13's two budget gates were not run, because they cannot be run here.**
   Both ask for per-pass milliseconds with Performance State checked. Neither is
   obtainable in this workflow, for four independent reasons. This is not a
   shortcut; it is `AMENDMENT_8_12_PHASE7_GATE.md`, which finally writes down
   the substitute that Amendment 8.9 §4 recommended in 2026 and nobody adopted.
   **No per-pass ms figure is quoted against §11.2 anywhere in this phase.**

2. **The §6.5 8.4 wave guard is NOT a `WaveActiveAnyTrue` call.** Two attempts
   at wave intrinsics failed on this target; the third is a groupshared
   equivalent at thread-group granularity. §5.2 explains why that is not a
   consolation prize and why it is, for rung 1, strictly better.

3. **Two §6.5 ambiguities are implemented literally and FLAGGED, not decided.**
   A torch behind a wall lights the far side, and the ambient term needed a
   second (cheap, vertical) ray to make caves dark. Both are §10's to answer.

4. **Every art constant is a placeholder.** `k_soft`, the C.11 falloff, torch
   range and colour, the ambient curve, the sun ramp, the day length. They are
   shipped sane and exposed in `LightingConfig.json` for live tuning. §8 lists a
   specific one that the screenshots say is wrong.

---

## 1. §13 Phase 7's acceptance assertions, each marked

| # | §13's assertion | Verdict | Evidence |
|---|---|---|---|
| 1a | A torch lights its surroundings via falloff | **MET** | Rig Gate 1: mean luminance 0.0369 → 0.5019 with one torch; 0.4371 / 0.2764 / 0.1630 at 1.5 / 3 / 6 m, monotone (C.11) |
| 1b | Deep cave genuinely dark | **MET** | Rig Gate 1: 0.0369 mean luminance in a sealed chamber, against a 0.10 bar. Screenshot `01_cave_dark_no_torch.png` |
| 1c | A torch "does not bend around a wall" | **FLAGGED, NOT MET AS WORDED** | §10 flag 1. The literal §6.5/§3.8 reading has no occlusion ray, so a torch behind a wall *leaks* through it. Screenshot `04_torch_through_wall_FLAGGED.png` |
| 2a | Mountain shadow sweeps continuously, no cascade staleness | **MET** | Rig Gate 2: 23-step dawn→dusk sweep, largest single-step change 8.61% of frame. There is no cascade to be stale — the shadow is re-derived from live bytes every frame |
| 2b | A single protruding voxel casts a crisp discrete shadow | **PARTIAL** | *Casts a shadow*: asserted and MET. *Crisp*: inherently visual, **not asserted**. `07_lone_voxel_shadow.png` |
| 2c | Grazing-angle shadows show **zero acne** | **MET** | Rig Gate 2: **0.00% / 0.00% / 0.00%** shadowed at 2° / 6° / 10° sun elevation on a carved flat platform. Literally zero, not "few" |
| 2d | **Zero Peter-Panning** | **MET** | Rig Gate 2: the ground cell touching a wall's base is in shadow |
| 3 | §6.5's silhouette test, as a **pixel-level** assertion | **MET** | Rig Gate 3: GPU sun visibility vs `ShadowRayReference` per sampled pixel — **98.40% of 438** agree. §4.3 explains the residual |
| 4a | Bomb a cave roof at noon → sunlight reaches the floor **the same frame** | **MET** | Rig Gate 4: occluded before, lit after exactly one frame, 2,044 voxels removed (§3.10) |
| 4b | Pour water off a ledge → moving shadow | **NOT SEPARATELY TESTED, and no code exists to test** | A consequence of §3.10 alone: fluid writes terrain bytes, shadow rays read terrain bytes. Exercised by hand in the Playground and under live fluid by the combined arm |
| 5 | §3.8's cap degrades gracefully, furthest-first, no silent drops | **MET** | Rig Gate 5: 32 active / 48 registered; a torch placed at the player's feet while over the cap is registered and lit. Furthest-first proven exhaustively in EditMode, mutation-checked |
| 6 | **Budget gate:** raymarch + shadows ≤ 9.0 ms, Performance State checked | **UNRUNNABLE AS WRITTEN** | `AMENDMENT_8_12` §2. Substitute in §6 below |
| 7 | **Combined gate:** + fluid CA, golden hour, sustained ≤ 13.0 ms | **UNRUNNABLE AS WRITTEN** | Same. Substitute arm (ii) in §6 |

Plus one assertion §13 does not ask for, but Amendment 8.10 §2's method does:

| 0 | Tier-0 traversal unchanged when shadows are off | **MET, by exact counter match** | Rig Gate 0: primary outer steps **5 == 5**, `denseMicroSteps` **0 == 0**, with shadow steps 0 → 3 proving the branch actually ran. Not an argument from code structure |

---

## 2. Evidence classification — kept strictly separate

### CORRECTNESS PROVEN

- **The shadow ray agrees with the Phase-2 oracle.** 4,000-ray differential fuzz
  plus a 2,500-ray grazing / near-axis-aligned fuzz, occlusion bit-identical to
  `RaymarchReference.TracerRaycast` cast from the substituted origin.
- **§6.5's origin rule, by construction.** No bias term exists, and
  `ShadowOriginVoxel` takes no parameter that could become one.
- **§6.5's 8.2 LOD rule.** Tier selection is a function of camera distance only;
  proven by test and by mutation (M2).
- **C.10's penumbra** is monotone in `k_soft`, exactly hard at `k_soft = 0`,
  never changes the occlusion answer, and contact-hardens.
- **§3.8's cap and its furthest-first degradation**, including that registration
  is uncapped so a new torch is never silently ignored. Mutation-checked (M3).
- **A.6's layout**, C# and HLSL, field for field, by a parsing test.
- **§6.6's enqueue through the one §8.3 path**, including removal, overwrite and
  the no-light-buffer-attached case.
- **§6.5's night rule**: sun colour is exactly zero below the horizon; ambient
  does not go to zero.
- **§8.1's hot-reload contract for `LightingConfig`**: malformed input keeps the
  running config, `k_soft = 0` survives validation.
- **The five §13 acceptance gates and Gate 0**, at runtime, on real generated
  terrain, through the real edit path.

### PERFORMANCE MEASURED

- See §6. Every figure carries its run folder. Nothing here is a per-pass ms
  figure and nothing is quoted against §11.2.

### NOT TESTED AT ALL

- **Tiers 1 and 2 have no CPU oracle**, for shadow rays exactly as for primary
  rays (Amdt 8.10 §5.7, PHASE_2 §6.2). Every claim about the coarse-tier shadow
  branch is "this reuses an already-proven primitive with different arguments" —
  real evidence, categorically weaker than a fuzz proof. **Shadow rays inherit
  this gap in full and it is not closed by this phase.**
- **Shadow rung 1 (half-res + neighbour reuse) has no correctness assertion.**
  The rig runs at rungs 0 and 2. Rung 1 is exercised only by the measurement arm
  and by eye.
- **The A.7 `GraphicsBuffer<MaterialData>` still does not exist.** See §7.
- **Whether any of it looks good.** No assertion here covers "crisp", "pleasing",
  "reads as golden hour", or "atmospheric". That is the owner's five minutes.
- **`k_soft`'s visual correctness at distance.** The shader's penumbra is a
  bounded approximation of the oracle's (§4.4); the two agree on occlusion,
  which is what is asserted, and are not claimed to agree on `sunVis`.

---

## 3. What was built

| File | Purpose |
|---|---|
| `CoreEngine/Rendering/ShadowRayReference.cs` | The CPU shadow oracle (§0.1 inv 4, §0.3 Sibling Pattern) |
| `CoreEngine/Tests/ShadowRayTests.cs` | Its differential fuzz + behaviour proof |
| `CoreEngine/Rendering/LightSourceBuffer.cs` | §13 file 1 — the flat A.6 buffer and §3.8's selection |
| `CoreEngine/Rendering/SunCycle.cs` | §13 file 2 — the clock (which did not exist) and the sun uniforms |
| `CoreEngine/Rendering/LightingConfig.cs` | §8.1's hot-reload contract, for the art constants |
| `CoreEngine/Rendering/LightingDriver.cs` | The one component that pumps the clock, the reload and the upload |
| `CoreEngine/Rendering/Raymarch.compute` | §13 file 3 — the fused shadow ray, C.10, C.11, the C.9 combine, two debug views |
| `CoreEngine/Diagnostics/ShadowRayDump.cs` | §13 file 4 / §10.4 — the shadow path's ring-buffer dump |
| `CoreEngine/Tests/LightingTests.cs` | §3.8, A.6, §6.5, §6.6 and §8.1 assertions |
| `Game/Phase7CycleRig.cs` | §13's acceptance gates |
| `Assets/Scenes/Phase 7 Cycle.unity` + its builder | §13's scene |
| `run-phase7-rig.sh` | The acceptance loop |
| `run-phase7-lighting-ab.sh` | The substitute budget gate's arms |
| `AMENDMENT_8_12_PHASE7_GATE.md` | Why §13's gates are unrunnable, and what replaces them |

Also modified, additively: `EngineConfig` (`MAX_ACTIVE_LIGHTS`,
`SHADOW_QUALITY_LEVEL`), `StructHeaders.cs/.hlsl` (A.6), `Content.cs`
(`Materials.Torch`), `MaterialRules.cs` (the emissive tables), `EditService.cs`
(§6.6's enqueue), `RaymarchFeature.cs`, `RaymarchDebugControls.cs`,
`PlaygroundHud.cs`, `Playground.cs`, `PlaygroundCapture.cs`, `PaletteSyncTests.cs`.

---

## 4. Defects found, and where they came from

### 4.1 Four found by tests I wrote, before any of them shipped

None of these would have failed loudly.

**(a) The shadow ray's step guard bound before its distance clamp.** It was
1,000 — copied from `TracerRaycast` — against a 2,900-voxel clamp. A ray that
exhausts its guard returns `occluded = false`, i.e. **lit**. That would have
silently lit things that are in shadow, worst exactly where rays are longest:
golden hour, the pose §13 asks to be captured specifically. The guard is now
derived from the clamp × √3.

**(b) C.10's near-miss scan counted the lit surface as its own occluder.** A
shadow ray starts one cell off the surface it is shading, so that surface is
*always* a face neighbour of the origin cell. Scanned as a near-miss it gives
clearance ≈ 0 at t ≈ 0, C.10's `m` collapses to 0, and the renderer reports
**the entire sunlit world in full shadow**. Found by the most ordinary frame the
game can render: flat ground, sun straight up. Fixed by construction — skip
neighbours strictly behind the advancing ray, which excludes the receiver for
every lit pixel with no epsilon, because `dot(dir, hitNormal) > 0` is exactly
C.9's `max(0, N·L)` being positive.

**(c) and (d) Two penumbra tests asserted the wrong thing** — and the second was
C.10 being *right*. `m = clearance / t` is an angular miss, so the penumbra
**contact-hardens**: a 3-voxel wall puts its shadow edge ~5 voxels from the
caster, where one voxel of clearance subtends ~0.25 rad, and the edge is
legitimately hard. The test geometry was rebuilt with a 25-voxel wall so
`k_soft` has something to act on, and contact hardening is now itself pinned.

### 4.2 Five found by the acceptance rig's first run — all in the rig

`PASS 11 FAIL 15` on the first run, and **every one of the fifteen was the rig,
not the product.** Recorded because the pattern matters:

1. The chamber was sited at `spawnSurfaceY − 45`. The spawn is coastal (y=38),
   so it landed at **y = −7, below the world**, where no chunk exists and every
   write is refused. The tell was luminance identical to four decimal places at
   all three falloff distances: that is the sky colour, not a cave.
2. Gate 0's pose put **sky** at the debug pixel, so the counter match was
   `0 == 0` — vacuously true. **The rig's own anti-vacuity check caught this**,
   which is precisely why it was written.
3. The acne gate *searched* for flat ground, verified flatness over 6 voxels,
   then measured the **whole frame** from 3 m up. At 2° sun elevation the
   horizon is legitimately full of hundred-metre shadows. Its 2.01% was real
   terrain. It now **carves** a 53×53 platform and measures the centre 30%.
4. The Peter-Panning check hardcoded "the sun is in +X at this hour" and was
   wrong — `HoursForElevation(…, evening)` returns the far branch of the arc
   where `cos θ` is negative. It checked the **lit** side and reported a gap
   that did not exist.
5. Gate 4 carved a ±3 shaft, but `sunArcTilt` leans the arc so a ray climbing 23
   voxels drifts 6 sideways — the ray left through the shaft **wall** and was
   correctly reported occluded.

### 4.3 A latent bug in the shipped shader, found by reading rather than failing

`Raymarch.compute` had **no `SelectLODTier` function**; tier selection was four
inline lines comparing `currentDist`. For a *primary* ray that is correct — but
only by accident of where the ray starts, because a primary ray begins at the
camera, so its travelled distance *is* its camera distance. A shadow ray begins
at a surface. Reusing `currentDist` there is **exactly the 8.1 travelled-distance
rule §6.5 retracted**, and it would have compiled, run, and looked like working
code. Extracting the function and naming its parameter `cameraDistanceVoxels`
forces each call site to say which distance it is passing.

### 4.4 A false green in this project's own shader gate

`ShaderCompileCheck.CheckAll` reported `Raymarch.compute: OK (0 message(s))` and
`DONE totalErrors=0` for the exact file the standalone build then refused to
compile (`undeclared identifier 'WaveActiveAnyTrue' … (on metal)`).
`ShaderUtil`'s message list comes from the **Editor's import-time compile**,
which does not cover the player's Metal target. `run-phase7-rig.sh`'s build-log
grep for `Shader error` is what caught it, and is therefore load-bearing rather
than belt-and-braces. **Recorded in CLAUDE.md's known-issues list.**

---

## 5. Where the spec could not be followed literally

### 5.1 §13's two budget gates
See `AMENDMENT_8_12_PHASE7_GATE.md` in full. Four independent reasons, each
sufficient: no Xcode (Amdt 8.9 Rule 1), Performance State unreadable (Rule 2),
`gpuFrameTime` inflated ~2.6–2.7× with a retracted result behind it (Amdt 8.10),
and per-pass attribution non-existent because Unity merges the dispatches into
one Metal encoder.

### 5.2 §6.5's 8.4 wave-uniform branch guard

§6.5 asks in terms for `WaveActiveAnyTrue(hasPrimaryHit)`. Two attempts:

- **plain build:** `undeclared identifier 'WaveActiveAnyTrue' … (on metal)`
- **`#pragma use_dxc`:** `DXC SPIRV-Cross error: Subgroups are only supported in
  Metal 2.0 and up.`

A third attempt meant raising the project's Metal/MSL floor in `ProjectSettings`
— a global change to every shader and to the shipping macOS minimum, for one
branch guard. That is the owner's call, and §0.3's rule says stop after ~3
attempts rather than reach for a cleverer patch.

**The groupshared form that shipped is not a consolation prize:**

- it does what §6.5 asks — "a wave that is *entirely* sky skips instantiating the
  shadow-ray code path" — at 64-thread thread-group granularity instead of
  32-thread wave granularity. Coarser by two; same mechanism.
- for rung 1's neighbour reuse it is **strictly better** than `QuadReadLaneAt`:
  `gsSunVis[(gtid.y & ~1)*8 + (gtid.x & ~1)]` is an exact, stated index, where
  the quad form would have relied on an SM6.0 assumption that quad lane 0 is the
  even/even thread — an assumption that could have been silently wrong on this
  driver and would have shown up only as distant shadows sampling the wrong
  neighbour.
- the group-flag write is a **plain-write race** where every writer writes `1` —
  the same no-atomics pattern §7.3/8.4 already established for fluid claims.
- **and it keeps the measurement clean.** Reverting `use_dxc` means this shader
  is still built by the same compiler as every historical number in Amendments
  8.9/8.10, PHASE_2 §5 and FPS_INVESTIGATION. Had DXC been kept, every Stage 6
  arm would have carried a compiler change as a confound and the pre-Phase-7
  baseline would not have been comparable.

Cost of the barriers: `CSMain`'s out-of-bounds early `return` became a flag
tested at the final write, because a group barrier is undefined unless every
thread reaches it. That is 0.185% wasted threads at 960×540 and **0.000%** at
1920×1080 (both dimensions divide by 8).

### 5.3 C.10's penumbra in the shader is a bounded approximation of the oracle's

The CPU oracle scans near-miss neighbours at **every** cell. The shader scans
them only on the **dense-brick micro-step path**, because paying six neighbour
reads on air-brick leaps would charge for near-misses in bricks that by
definition contain no solid. **What it misses:** a ray skimming a wall built of
*uniform-solid* bricks while travelling through *uniform-air* bricks gets a hard
edge rather than a penumbra, because neither brick is ever micro-stepped.

The two agree **exactly on occlusion**, which is what the oracle property and
Gate 3 assert. `sunVis` is not claimed to match. Stated here rather than left
implicit.

### 5.4 §6.5's "clamps at max distance (island diagonal)"

Implemented as the same `_MaxRayDistance` the primary ray uses — tier 2's outer
bound, 290 m. Taken literally the island diagonal is ~2,700 m, but the renderer
draws 290 m and the window is 409.6 m across, so a literal clamp would walk
thousands of voxels of guaranteed-nothing before the window guard stopped the
ray anyway. **This is not an implementation of Amendment 8.11**, which remains
DRAFT, NOT ADOPTED and out of scope; the clamp follows `LODConfig`, so a future
decision on 8.11 carries it with no edit.

---

## 6. PERFORMANCE MEASURED

**Read `AMENDMENT_8_12_PHASE7_GATE.md` §3.2 and §3.3 before using any of this.**
Headline metric is **TRUE SUSTAINED RATE = frames ÷ elapsed wall time**.
Percentiles are diagnostics only — 120 Hz ProMotion, the app out-presents the
display, and roughly one frame in three lands over 16.6 ms regardless of
workload (FPS §0.2). **p99 is never a headline here.**

*(Results section: see `RESULTS.md` insertion below — filled from the run folder
this phase produced. Arms that did not run are named as not run; a reduced
matrix is a real measurement of what it covered and is **not** §13's gate.)*

**RESULTS — PENDING AT THE TIME THIS SECTION WAS WRITTEN.** The run was launched
from this session; its report lands in
`~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Phase7LightingAB/`.
Until a number here carries a run folder, **there is no Phase 7 performance
number**, and any figure that appears elsewhere without one is not from this
document (Amendment 8.9 Rule 3).

---

## 7. Hypotheses formed and DISPROVED — reported, not buried

1. **"`ShaderCompileCheck` green means the shader compiles."** Disproved by the
   build (§4.4). The Editor's import-time compile does not cover the player's
   Metal target.
2. **"The wave guard is a one-line addition."** Disproved twice (§5.2).
3. **"`_UseDevColors`'s hemisphere/AO/N·L terms can simply be replaced by C.9."**
   Partly disproved: the hemisphere term *is* C.9's `ambientSky` given a
   direction and is what makes a horizontal surface read as horizontal, so it
   was demoted rather than deleted. Only the fake hardcoded-direction N·L was
   replaced outright.
4. **"A plain normal-based hemisphere term satisfies §6.5's ambient
   requirements."** Disproved by inspection and then by measurement: it lights a
   sealed cave ceiling exactly as brightly as open sky, and "deep cave genuinely
   dark" is a §13 acceptance bullet. See §10 flag 2.
5. **"The acne gate's 2.01% was acne."** Disproved: it was real terrain shadow at
   a 2° sun elevation, measured over the whole frame including the horizon. On a
   carved platform, measured in the centre, it is **0.00%**.
6. **"`m = clearance/t` should give a visible penumbra at any shadow edge."**
   Disproved: the ratio is an angular miss, so the penumbra contact-hardens and a
   nearby caster legitimately gives a hard edge.

## 8. Things that measured or looked WORSE, with their numbers

- **`#pragma use_dxc`** — got further (past `WaveActiveAnyTrue`) but failed at
  `Subgroups are only supported in Metal 2.0 and up`, **and** would have changed
  the compiler for the whole shader, making every Stage 6 arm carry a codegen
  change as a confound. Reverted. Do not re-attempt without deciding the Metal
  floor first.
- **The default art constants are visibly wrong in two specific ways**, and the
  numbers are given so the fix is one edit rather than a hunt:
  - **The torch is far too bright and reads flat.** `02_cave_lit_by_torch.png`
    shows a 2.8 m chamber washed out by a single torch at
    `lightIntensity = 1.6`, `pointFalloffK = 0.35`, `lightRangeM = 8`. Every face
    is lit identically regardless of orientation, which is the visible
    consequence of C.9's literal reading (no `N·L` on point lights) — see §10
    flag 3. **Suggested starting point: `lightIntensity` ~0.6,
    `pointFalloffK` ~1.2.**
  - **Golden hour does not read as golden.** At 6° elevation on flat ground the
    sun term is `sunColor × N·L` = `(1.25, 0.68, 0.39) × 0.105` ≈
    `(0.131, 0.071, 0.041)`, while ambient is `(0.52, 0.62, 0.78) × 0.30` ≈
    `(0.156, 0.186, 0.234)`. **Ambient is 1.5–5× the sun at the exact pose §13
    asks to be captured**, so the frame reads cool and flat rather than warm.
    `09_golden_hour_beauty.png` is the evidence. **Suggested starting point:
    `ambientIntensity` ~0.12 and a higher `sunIntensity`.** This is a tuning
    number, not a defect, and it is exactly the kind §13 says to fix by eye.
- **Two pre-existing render artifacts are visible in the Phase 7 screenshots and
  are NOT Phase 7's.** `PHASE_2_COMPLETION.md` §6 item 6 records "small
  sky-coloured gaps along the horizon at certain angles, and a grazing-angle
  terracing artifact", present with the cascade both on and off. The horizontal
  banding on distant terrain in `09_golden_hour_beauty.png` is the latter.

---

## 9. Mutation checks — every one run, not reasoned about

| # | Mutation | Expected | Result |
|---|---|---|---|
| **M1** | Shadow origin moved to the hit voxel's own centre | `Origin_*` and `SelfIntersection_*` red | **PASS 10 FAIL 10** — both `Origin_*`, both `SelfIntersection_*` (the acne tests), both `Silhouette_*`, all four `Penumbra_*`, and the distance-clamp work-saving test |
| **M2** | LOD rule swapped to travelled distance (the retracted 8.1 rule) | `Silhouette_*` red | **PASS 18 FAIL 2**, and *precisely* the two intended: `Silhouette_SubTierOccluderVanishesWhenTheCameraIsFar`, `Silhouette_TierDependsOnCameraPositionOnly`. Nothing else moved |
| **M3** | `SelectActive`'s comparator inverted (nearest drop first) | `Cap_FurthestLightsDropFirst`, `Cap_ANearerTorchDisplacesAFartherOne` red | **PASS 26 FAIL 3** — those two plus `Cap_ActiveSetFollowsThePlayer`, which is the same bug class. The non-ordering cap tests (registration uncapped, unregister, idempotent re-place) correctly stayed **green** |

All three applied, observed, reverted; the suite returned to `PASS 550 FAIL 0`
and a `grep` confirmed no mutation marker survived.

**Rig gates 2, 3 and 5 were mutation-checked by the rig's own first run**, which
is a stronger form of the same evidence: five distinct rig defects produced 15
failures, and fixing each cause turned exactly the expected assertions green.
Gate 0's anti-vacuity guard caught its own gate being vacuous.

---

## 10. FLAG, DO NOT DECIDE — the owner's calls

### 1. Point lights through walls
§6.5 and §3.8 specify pure attenuation, no per-light occlusion ray, and §3.8's
whole cost model (2M pixels × N lights) assumes there is not one. §13's
acceptance bullet says a torch "does not bend around a wall". **With no
occlusion test, a torch behind a wall lights the far side — that is leaking, not
bending.** The literal reading ships; `04_torch_through_wall_FLAGGED.png` is the
consequence.
**Cost of the alternative:** one shadow ray *per light per pixel*. At the cap
that is 32× the shadow-ray cost, against a phase whose entire budget question is
whether *one* extra ray fits. A cheaper middle option is a single occlusion ray
to the *nearest* light only. Neither was built.

### 2. Near-zero ambient underground
§6.5 asks ambient to be three things at once: a **cheap hemisphere term**, the
stand-in for **bounce into cave mouths**, and **near-zero deep underground**. A
plain normal-based hemisphere term cannot be the third — it lights a sealed cave
ceiling exactly as brightly as open sky.
**What ships:** a straight-up sky-visibility probe, which is the *same*
`TraceShadowRay` function with its direction fixed to `+Y` — so it inherits the
air-mip, the content-ceiling exit, the window guard and the CPU oracle, and a
vertical ray is the cheapest possible one. **It is still a second ray and is not
claimed to be free**; it is behind `_UseSkyProbeAmbient` so it can be measured as
its own arm. `01_cave_dark_no_torch.png` shows it delivering exactly what §6.5
asks for. **Alternatives not built:** bake a per-brick sky-occlusion byte at
chunk load (cheap per pixel, costs memory and a generation step), or accept lit
caves and drop the §13 bullet.

### 3. `N·L` on point lights
C.9 attaches `max(0, N·L)` to the **sun term only** and leaves `pointLights`
bare. Implemented literally (`_PointLightNdotL = 0`). The visible consequence is
in §8: every face is lit identically regardless of orientation, which reads
flat. A toggle exists. **One line either way; the owner should look at both.**

### 4. Which shadow rung ships
All four are built and measurable from one binary. §6.5 says stepping down is
the spec's own designed outcome, not a failure. **Recommendation deferred to the
measurement in §6; the choice is a visual-quality judgement and is the owner's.**

### 5. The art constants
`k_soft`, the C.11 falloff `k`, torch range and colour, ambient intensity and
colour, the underground floor, the sun ramp, the day length. All placeholders,
all in `LightingConfig.json`, all live-reloadable. §8 names the two the
screenshots say are wrong and suggests starting values.

### 6. VAO's provenance
The literal token "VAO" appears nowhere in this project. `FaceAO()` **is** voxel
ambient occlusion and is used as C.9's trailing VAO term — but it arrived in a
later *presentation* pass, not as §13 Phase 2's file-4 build step, which
`PHASE_2_COMPLETION.md` never ticked. **No VAO was invented**; the existing one
is used and its provenance is recorded.

### 7. The Metal/MSL floor
Raising it would make §6.5's literal `WaveActiveAnyTrue` available. It changes
every shader and the shipping macOS minimum. Not done.

### 8. `AMENDMENT_8_8_OCCUPANCY_BITMASK.md` and an 8.7 document do not exist
Both amendments are *in force* and *implemented*, and CLAUDE.md cites 8.8 in its
ground-truth ordering — but no `.md` for either is in the repo and `git log`
shows neither was ever deleted.

---

## 11. CARRY-FORWARD — open, and known to be open

1. **Tiers 1/2 still have no CPU oracle.** Carried from Amdt 8.10 §5.7 and
   PHASE_2 §6.2, now inherited by shadow rays as well.
2. **The A.7 `GraphicsBuffer<MaterialData>` still does not exist.** Phase 7
   created the emissive *data* (§6.6's requirement) and mirrored it into the
   shader the way the palette already is, with `PaletteSyncTests` extended to
   pin it. Building the real §3.9 registry buffer is a sync-contract addition
   that wants its own checkpoint.
3. **Shadow rung 1 has no correctness assertion.**
4. **The shader's penumbra is a bounded approximation of the oracle's** (§5.3).
5. **The §6.5 / §13 ambiguities in §10 flags 1 and 3** are undecided.
6. **`run-phase6-brushguard.sh` is still 15 PASS / 15 FAIL** for the stale-rig
   reason CLAUDE.md records. Phase 7 added a 7th hotbar brush (torch) by
   **appending**, so no existing index moved and that rig's premise is unchanged.
7. **Amendment 8.11 remains DRAFT, NOT ADOPTED.** Untouched. §5.4 records the one
   place Phase 7 came near it and why that is not an implementation of it.
8. **The unattributed frame-time tail and the sustained-load stutter** are
   untouched and must not be reported as fixed.

---

## 12. FIVE-MINUTE REVIEW

Build and launch:

```bash
./run-playground.sh          # or: open -n Builds/Playground.app
```

Click the window once to capture the mouse. **`F2`** shows every binding below;
**`F1`** shows sun angle/time, active lights vs cap, and the shadow rung.

**(a) Torch falloff and cave darkness**
1. Press **`Tab`** for fly mode. Look down and hold **`LMB`** to dig straight
   down about twenty seconds, until the screen is genuinely dark.
2. Look around. It should be **almost black** — that is §6.5's "deep cave
   genuinely dark", and the rig measures 0.037 mean luminance here.
3. Press **`7`** to select the torch, then **`RMB`** to place one.
4. Back away from it slowly. Brightness should fall off smoothly (C.11).
   **Judge:** is it too bright? Does it read flat — every face the same
   regardless of which way it points? Both are §10 flags 3 and 5.
5. Place a wall of stone (**`4`**, `RMB`) between you and the torch. **It will
   still light you.** That is §10 flag 1 — decide whether that ships.

**(b) Scrub dawn→dusk and watch the mountain shadow sweep**
6. Press **`R`** to respawn on the surface, then **`Tab`** back to fly.
7. Find a mountain on the horizon. Press **`M`** (noon), then hold **`,`** and
   watch the shadows run backwards, then **`.`** forwards.
   **Judge:** does it sweep *continuously*? Any snapping, stepping or lag is a
   defect — there is no cascade here that could be stale.
8. Hold **`.`** into the evening and stop at a very low sun. Look along flat
   ground. **There must be no speckle at all.**

**(c) The golden-hour quality-level comparison**
9. Press **`N`** — golden hour, the pose §13 asks to be captured specifically.
10. Press **`J`** four times, pausing at each: **0** full soft penumbra →
    **1** half-res beyond LOD1 → **2** hard only → **3** off.
    **Judge:** at which rung does it stop looking acceptable? That answer is
    §10 flag 4 and it is the one number this phase most needs from you.
11. Press **`K`** to flip hard↔soft at rung 0 and see the penumbra directly.
12. Press **`Y`** for the sun-visibility view, then again for the shadow-ray
    step heat (blue cheap, red expensive — the same ramp as the primary heat
    view, so the two are directly comparable). **`Y`** once more returns to
    beauty.

**(d) Bomb a cave roof at noon**
13. Press **`M`** (noon). Fly into a cave, or dig a chamber and stand under
    unbroken rock. Look up.
14. Press **`B`**. **Sunlight should reach the floor the same frame** — §3.10's
    authoritative byte, and there is no invalidation step anywhere that could
    make it late.

**(e) Pour water off a ledge for the moving shadow**
15. Find a ledge with the sun low behind it. Press **`1`** (water), then **`V`**
    to open a vent above the edge.
16. Watch the falling water's shadow on the ground below. It moves because fluid
    writes terrain bytes and shadow rays read terrain bytes — free, by §3.10.

**(f) Edit `LightingConfig.json` live**
17. Open, in any editor, **without quitting the game**:
    `~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/LightingConfig.json`
18. Change `kSoft` from `0.03` to `0.25`, save, and look back at the window.
    **The very next frame is different** — the penumbra widens dramatically.
19. Try `ambientIntensity` `0.30` → `0.12` and `sunIntensity` `1.25` → `2.0` at
    golden hour (**`N`**). §8 argues this is the change golden hour needs.
20. Deliberately break the file — delete a brace — and save. **Nothing happens**:
    the running config is kept and `F1` shows the parse error. That is §8.1's
    contract and it is what makes the loop safe to use while playing.

### The questions this phase needs you to answer

1. **Which shadow rung ships** (§10 flag 4) — step 10 above.
2. **Does a torch lighting through a wall ship?** (§10 flag 1) — step 5.
3. **Should point lights get `N·L`?** (§10 flag 3) — step 4.
4. **Are the art constants close?** (§10 flag 5, and §8's two specific numbers).
5. **Do we raise the Metal/MSL floor** to get §6.5's literal wave guard?
   (§10 flag 7).
6. **Does `AMENDMENT_8_8_OCCUPANCY_BITMASK.md` exist somewhere outside this
   repo?** (§10 flag 8).
