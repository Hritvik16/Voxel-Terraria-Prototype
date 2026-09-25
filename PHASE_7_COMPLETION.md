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

---

# MORNING SUMMARY

## You were right and this document was wrong. It was measuring inside a hole.

**Your ~30 FPS is the real number. The 81 FPS in the previous summary was not.**

It is not thermal — I tested that first and it is disproved: 20 continuous
minutes of load made the machine **5% faster**, with OS thermal pressure nominal
across 12 samples. It is not fluid, not the HUD, not streaming; all three measure
as noise.

**Every fast number in this project was taken in a scene no player is ever in.**
`TeleportToArena` — where every Playground probe starts — puts the camera inside
the dug-out arena facing a near wall, so most of the frame is occluded black and
rays die in a few voxels: **7.7 ms**. And `-movespeed 12` for 30 s flies 360 m,
so the probe spends most of its run over empty sky: **3.1 ms**. Standing on open
ground looking at the landscape, which is what you actually do: **26.5 ms**. Both
poses are photographed in `Screenshots/StageK/` — the two images tell the whole
story faster than the numbers do.

## The real numbers, sustained, at a representative pose

| | frame | FPS |
|---|---|---|
| golden hour, full shadows, shipped 960×540 gate | 26.5 ms | **37.7** |
| **worst case** — golden hour + 51,000-voxel chaos burst | **30.5 ms** | **32.8** |
| noon | 17.1 ms | 58 |
| midnight | 13.5–16.6 ms | 60–74 |
| golden hour, **gate 560×315**, full shadows | **16.36 ms** | **61.1** |

## Can this hardware support what you want to build? Yes — with one trade

**60 FPS with full soft shadows, live fluid and a chaos burst is already
achievable: at 560×315 internal resolution it measures 61 FPS, in the tightest
three windows of the session.** What is *not* achievable is 960×540 with soft
shadows at a 6° sun. Amendment 8.9 is literally "LOD & resolution" — this is the
spec's own lever, and it is one number.

**Half the day already fits at the full 960 gate.** Golden hour is a ~10 ms spike
confined to low-sun hours, exactly where §6.5 predicted it.

Five options with their costs are in **§N.5** — lower the gate, dynamic
resolution, accept 37 FPS at dusk, shadows off at low sun, or shorten render
distance at low sun (the one lever left untested). **I am not choosing for you.**

**The thing I would not want you to miss:** §6.5's shadow fallback ladder does not
work as a budget mechanism. Rungs 0, 1 and 2 are within 3 ms of each other — the
cost is *tracing the ray*, not the soft edge, so every rung but "off" still pays
it. §F.6's "ship rung 0, it has 5.9 ms of headroom" is **withdrawn**; there is no
headroom at 960.

## The fluid slowdown is connected, not separate

`Playground.Update()` calls `_fluid.Tick()` **once per rendered frame**, with no
fixed-timestep accumulator anywhere in that path. Fluid speed is therefore
directly proportional to frame rate: at 33 FPS it advances at 55% of its 60 FPS
rate. **"Fluid moves like syrup" is a symptom of the frame rate, not a second
bug** — fix one and the other goes with it. (It also means that if you ever cap
the frame rate, fluid speed changes with it; decoupling the CA is a real design
question and is flagged in §L.5, not built.)

## Snow: two sessions optimised the wrong variable

The complaint was never peak brightness alone — it is **contrast**. At noon the
vertical faces of the voxel staircase get no direct sun at all, and ambient in
C.9 is non-directional, so they sit **7.4× darker** than the lit top faces. That
is the dark contour banding.

**Every preset that lowered ambient to bound the peak made this worse** — "Warm"
reaches **16.7:1**. They traded a problem you could name for a bigger one you
could only feel. Both facts are now pinned by tests.

**The fix is one number, and it only became possible last session:** with ACES in
the pipeline, raising `ambientIntensity` 0.30 → 0.60 lifts the dark faces while
moving the displayed top face by **3.4%** — halving the contrast. Under the old
hard clamp this was impossible, which is why §C correctly concluded it couldn't be
done. **Not applied — it is an art constant and yours to set** (§M.2); it
hot-reloads, so try it and look.

## And the honest answer on the "Layer of the Land" feel: lighting won't get you there

Asked directly, so: **no. A third pass at lighting constants would waste your
time.** Aerial-perspective fog already exists (45→260 m), a tone curve now exists,
as do AO, hemisphere tint, real sun, shadows and point lights.

What's missing isn't in the lighting system. The landscape is large contiguous
areas of **one flat colour per material** — nine terrain materials, with a ±9%
per-voxel grain as the only within-surface variation. That look leans on dense
small-scale detail and colour variation *within* a surface. No lighting model
applied to nine flat colours produces it. Ranked by effort in **§M.4**: per-material
colour variation (small, a shader change — I'd try this first), scattered detail
content (phase-sized, and really your forthcoming content work), and §M.2's
ambient raise (one number).

## Beautiful shadows AND the look, at 60 FPS: measured, and it is one config away

**gate 560×315 · full soft-penumbra shadows · within-material colour variation ·
ambient 0.30 · live fluid · moving camera · golden hour: 16.28 / 16.81 ms =
59–61 FPS.** Against the shipped config's 26.5 ms at the same pose — and the
picture gets *better*, not worse. Compare `Screenshots/Aesthetics/1_golden_BASE.png`
with `3_golden_matvar_ambient030.png`.

**The shadows get there by tracing fewer rays, not cheaper ones.** Every attempt
to make the shadow ray itself cheaper failed, and they are all measured in §O.3:
rung 1 is *worse*, rung 2 buys 0.8 ms, the distance clamp buys 2.5 ms, and my new
travel-based LOD rule buys nothing at all (it increases steps per ray from 70 to
106 and leaves the frame unchanged — §O.2, disproved with counters). The cost is
**tracing the ray at all**, so the only lever that moves it is one ray per gate
pixel.

Two aesthetic levers are now built, both nearly free: **within-material colour
variation** (+0.6 ms — two octaves of smooth noise moving albedo on a warm/cool
axis, replacing white-noise brightness jitter that averages to grey at any
distance) and **ambient** (0 ms, §M.2's contrast fix).

**Everything added this session defaults OFF.** The shipped build's look and speed
are unchanged; these are levers with numbers attached.

Also found: the live `LightingConfig.json` has been sitting at **ambient 0.055**
(the HighContrast preset, left by the Stage J run) — 5× below shipped default, and
the reason the landscape in every Stage K/L frame reads as crushed black.

Flagged separately: a **deterministic fluid spike ~2.5 minutes after a chaos
burst** (not during it) costing +9 to +51 ms. It is a fluid question, not a
lighting one — §O.6.

## What I'd do with your first ten minutes

Open `Screenshots/StageK/pose_static_IN_THE_PIT.png` and
`pose_orbit_OPEN_GROUND.png` side by side. That is the whole measurement story.
Then set `ambientIntensity` to 0.55 in `LightingConfig.json`, stand on open ground
at noon, and tell me whether the snow banding is gone.

EditMode **PASS 562 FAIL 0**.

## 0. Read this first — the four things most likely to be misread

1. **§13's two budget gates were not run, because they cannot be run here.**
   Both ask for per-pass milliseconds with Performance State checked. Neither is
   obtainable in this workflow, for four independent reasons. This is not a
   shortcut; it is `AMENDMENT_8_12_PHASE7_GATE.md`, which finally writes down
   the substitute that Amendment 8.9 §4 recommended in 2026 and nobody adopted.
   **No per-pass ms figure is quoted against §11.2 anywhere in this phase.**

1b. **THE SHIPPED RUNG DOES NOT HOLD 60 FPS AT GOLDEN HOUR, AND THE RAY — NOT
   THE PENUMBRA — IS WHY.** Measured, same binary, minutes apart: shadows off
   15.31 ms, hard-only 40.77 ms, full penumbra 52.06 ms. The sun shadow ray
   costs **+25.5 ms**; C.10's clearance scan adds a further **+11.3 ms**.
   §6.5 predicted this for grazing golden-hour rays in terms, and Amdt 8.10
   §6.4 predicted the rungs would be *needed, not optional*. **Rung 1 —
   half-res beyond LOD1, the rung aimed at exactly this cost — was not
   measured, and measuring it is the highest-value next action.** §6 has the
   caveats, which are substantial.

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
| 6 | **Budget gate:** raymarch + shadows ≤ 9.0 ms, Performance State checked | **UNRUNNABLE AS WRITTEN — and the substitute says the shipped rung FAILS** | `AMENDMENT_8_12` §2 for why it is unrunnable. §6 for what was measured: at golden hour, rung 0 is 52.06 ms and rung 2 is 40.77 ms against a 16.67 ms *whole frame*. Rung 0 and rung 2 cannot ship |
| 7 | **Combined gate:** + fluid CA, golden hour, sustained ≤ 13.0 ms | **UNRUNNABLE AS WRITTEN, AND NOT MEASURED EVEN IN SUBSTITUTE** | The combined scenario was not run. Named as not run in §6.1 |

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

### 6.1 What was actually run, and what was not

**RUN:** three arms — shadow rungs **3, 2 and 0** — golden hour, fluid OFF, the
shipped 960×540 gate, fullscreen, release standalone, same binary, minutes
apart, single repetition each.

**NOT RUN, and named rather than quietly omitted:**
- **Arm A** (the pre-Phase-7 baseline). Two attempts failed for a real reason:
  the pre-Phase-7 binary has **no `-lightingab` mode**, so it launches a plain
  playable scene and never quits. The script now routes arm A through
  `-stutterprobe` (measurement-equivalent: same pose, same settle, same metric)
  with a hard timeout, but the arm was not completed in this session. **Nothing
  was substituted from an earlier session** — that is exactly the cross-session
  comparison FPS §3.2 records as producing a wrong number.
- **Rung 1** (half-res distant). Not measured.
- **Scenario (ii), the combined gate with live fluid.** Not measured.
- **The forced 1920 gate.** Not measured.
- **300 s cooldowns, counterbalanced ordering, and repetitions.** **Not applied.**

### 6.2 The numbers

| rung | what it is | mean frame | sustained rate | run folder |
|---|---|---|---|---|
| **3** | shadows OFF (flat N·L + ambient) | **15.31 ms** | **65.3 FPS** | `LightingAB/2026-09-12_200313` |
| **2** | HARD shadows only, no penumbra | **40.77 ms** | **24.5 FPS** | `LightingAB/2026-09-12_200444` |
| **0** | FULL soft penumbra (C.10) | **52.06 ms** | **19.2 FPS** | `LightingAB/2026-09-12_200118` |

### 6.3 What these do and do not establish

**THE CAVEAT FIRST, because it governs everything below.** These are
**UNCOOLED, SINGLE-REPETITION, UNCOUNTERBALANCED** runs. They violate three of
`AMENDMENT_8_12` §3.1's own requirements and **they are not §13's gate, nor are
they 8.12's substitute gate.** They are a **diagnostic**, and they are reported
as one.

**Why the ordering is nonetheless not in doubt.** Two independent reasons:
1. The spread between arms is **3.4×**, against a documented worst-case
   uncooled thermal spread of 183% (1.8×). The effect is twice the size of the
   largest confound this machine is known to produce.
2. **Rung 3 lands at 65.3 FPS against the historical cooled baseline of
   69.8 ± 0.4 FPS** (FPS §2.1) — within 6.5%. The machine was demonstrably not
   in a throttled state, because the control arm reproduced the known number.
   That control is what turns "the machine might be hot" from a possibility
   into a measured non-explanation.

**The isolation, which is the actually useful result:**

```
rung 3 -> rung 2   15.31 -> 40.77 ms   THE SUN SHADOW RAY COSTS +25.5 ms
rung 2 -> rung 0   40.77 -> 52.06 ms   C.10's CLEARANCE SCAN ADDS +11.3 ms
```

**The shadow RAY is the cost, not the penumbra.** That inverts the intuition
this phase was built with — the C.10 near-miss scan does six extra clipmap reads
per dense micro-step and looked like the obvious suspect — and it is why the
rungs were measured separately rather than one number being taken.

**And §6.5 predicted exactly this, in terms:**

> "Early exit & cost: … **Grazing golden-hour rays travel farthest** but mostly
> through uniform-Air leaps at coarse LOD — **measured in Phase 7, not assumed**,
> against the ≤9.0 ms combined budget."

At 6° elevation a sun ray is nearly horizontal and can travel the full 2,900-voxel
clamp across the window. The sky probe, by contrast, goes straight up and exits
at the content ceiling within a handful of iterations — and its cost is visible
in rung 3's own number: **rung 3 fires the sky probe on every hit pixel and
still runs at 65.3 FPS**, i.e. the probe plus all the rest of Phase 7's
non-shadow machinery costs roughly 6% against the historical baseline. §10 flag
2's "it is a second ray and is not claimed to be free" now has a number: it is
cheap, because it is vertical.

### 6.4 The consequence, stated plainly

**Rung 0 cannot ship.** 52.06 ms is a 19 FPS frame against a 60 FPS target.
**Rung 2 cannot ship either** at 40.77 ms / 24.5 FPS.

This is not a surprise and it is not a failure — it is the outcome §6.5 designed
the ladder for ("the game ships either way") and the one Amendment 8.10 §6.4
predicted in terms ("the doc's three shadow fallback rungs are likely to be
**needed**, not optional — plan for engaging them rather than being surprised").

**Rung 1 — half-resolution beyond LOD1 with neighbour reuse — is the rung that
was not measured and is the one most likely to matter**, because it targets
precisely the cost this isolation found: distant, long, grazing rays. It is
built and it is one config number away. **Measuring it, cooled and
counterbalanced, against rung 3, is the single highest-value next action in this
phase**, and it is named as such rather than guessed at.

A second, cheaper lever exists and is **not** implemented: the shadow ray
currently inherits the primary ray's 290 m clamp. A shorter shadow clamp would
bound grazing-ray travel directly, at the cost of distant shadows fading out.
That is a visual-quality trade and therefore the owner's, not mine.

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
- **DISTANT SHADOW SPECKLE AT COARSE TIERS — observed, not asserted, and worth
  the owner's eye.** `10_silhouette_sunvis.png` (rung 2, hard shadows, golden
  hour) shows the near-field shadows as crisp, spatially coherent shapes that
  follow ridge lines exactly as exact voxel shadows should. In the **mid and far
  distance** — tier 1 and tier 2 territory — the same view shows a dithered
  speckle rather than solid regions. Two candidate explanations, and this phase
  does not distinguish them:
  (a) genuine per-coarse-voxel shadowing of blocky downsampled terrain, which is
  correct and simply looks noisy in a binary diagnostic view; or
  (b) coarse-tier sampling noise, which would be a defect.
  **It cannot be settled here**, because tiers 1/2 have no CPU oracle
  (Amdt 8.10 §5.7) — Gate 3's 98.40% agreement covers mostly near-field samples.
  It is much less visible in Beauty, where albedo, ambient and the C.10 penumbra
  all soften it, and rung 0 would soften it further. Recorded so it is not
  rediscovered as new, and so the owner knows to look at the far field
  specifically.
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
All four are built and switchable by one config number.

**This flag changed completely overnight, and the hard version of it is gone.**
The measurement that said "neither rung 0 nor rung 2 can ship" predates the §A
lava fix and is superseded — see §F.4 and §F.6. Cooled, counterbalanced, clock
pinned, live fluid, moving camera, shipped gate, at the worst time of day found:

| rung | what it is | mean frame | tail >100 ms /1000 | fits 16.67 ms? |
|---|---|---|---|---|
| **0** | **full soft penumbra** | **10.61 / 10.82 ms** | **4.4 / 5.6** | yes, 5.9 ms spare |
| 2 | hard edges only | 9.92 ms | **0.0** | yes, 6.8 ms spare |
| 3 | off | 8.75 / 8.88 ms | 0.0 | yes, by a lot |

Rung 1 is not re-measured post-fix and is not needed: §F.1–F.2 explain with
counters why it does nothing (it half-rates only beyond 128 m, and the near field
is where the work is). It is kept as a rung for slower hardware, not for this
machine.

**RECOMMENDATION: ship rung 0** — it is the best-looking option and it fits the
frame at both times of day measured, with room to spare.

**THE ONE THING THAT COULD CHANGE THAT, and only you can judge it:** rung 0 is
the *only* rung that produces frames over 100 ms (4.4–5.6 per 1000; rungs 2 and 3
produce none). Rung 2 keeps shadows, removes that tail, and costs 0.8 ms less.
If a hitch roughly every 200 frames is noticeable while you are moving, rung 2 is
the better ship and the loss is only the soft shadow edge.

**How to decide it in two minutes:** move continuously and press `J` to flip
between *Full (soft edges)* and *Hard edges only* — the always-on HUD strip names
them now. If you cannot feel the difference, ship rung 0.

§6.5 is explicit that stepping down is the spec's own designed outcome and that
"the game ships either way".

### 9. A lava lake no longer lights the cave around it — RE-EXAMINED, still your call
Fixing the Stage A stutter required that **mobile emissives stop registering as
point lights** (§A.6): lava is `IsFluid | IsEmissive`, and a flowing lava lake
registered thousands of lights, which is what §3.8's "a handful of placed torches,
not a lit city" exists to prevent.

**§H re-opened this with a measurement, and the answer changed shape.** The cost
objection is gone — 9,315 lava voxels now cost **+0.99 ms**, not tens of
milliseconds. But turning it on naively is still wrong, for two reasons that are
structural rather than tunable: 32 nearest-first lights all land inside one lake
and saturate the wall to white (`H1_02`), and they occupy the entire global cap,
so **the player's own torches stop being drawn**.

**Three costed options are in §H.2.** Short version: leave it off (ships today,
zero work); **one aggregate light per fluid tile** — ~3 lights instead of 9,315,
using substrate `FluidTileMap` already has, and the option I would build; or raise
the cap, which makes it worse. **Not built, deliberately** — §H said to stop and
describe the options rather than guess at an implementation overnight, and this is
the case it was describing.

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

### 10. The Brickmap look is now the default (`LightingConfig.look = 1`), and it deviates from C.9 — added 2026-09-24
Built at the owner's request ("the visual changes don't affect the core
architecture, go ahead and make them"), with the comparison repo's recorded
decision "Brickmap is the look reference". Presentation only: traversal, both
shadow rays, the light buffer and every §13 mechanism are shared by both looks.

**What changed, and why most of it is one fact.** The Brickmap renders in
**Gamma** colour space; this project renders in **Linear**, so URP sRGB-encodes
every value the kernel writes — lifting and desaturating it. Look 1 shades in
display space and converts once at the end (`LookToLinear`), and ports the
Brickmap's palette (`MaterialPalette.OfLook`, pinned by `PaletteSyncTests`), its
three-tone grass, face shade, sky gradient + sun halo and squared haze. The haze
also makes the 290 m edge fade into sky, which is Amendment 8.11's Option D.

**THE SPEC DEVIATION, flagged not hidden:** look 1 floors the sun term
(`0.8·N·L + 0.2`) and lets a shadowed face keep `1 − lookShadowDarkness` (25%) of
the sun. C.9 specifies `max(0, N·L)` and full occlusion. The floor is what removes
the black terrace contours (§M). Sealed caves stay dark because the floor is
scaled by sky visibility. **Look 0 is C.9 byte-for-byte** and remains one
config value (or `U` in the Playground) away.

**Owner's call:** keep look 1 as the default, keep it with a C.9-exact sun term
(`lookShadowDarkness 1.0`), or revert to look 0. `./run-phase7-demo.sh` shows both
side by side (section 1 of its index.html).

---

## A. STAGE A — THE STUTTER: Phase 7 DID regress the frame, and it is NOT the shadow ray

### A.1 The three-arm comparison

Same probe (`-stutterprobe`), same pose, same settle, same flags, **fullscreen,
300 s cooldown before each, same session**. `-movespeed 12` is used rather than
a static camera because the reported symptom's trigger is *motion*, and it is
the one motion lever present in both the current and the pre-Phase-7 binary.
Fluid live in all three arms. 2,400 frames each.

| arm | build / rung | mean frame | sustained | p50 | p99 | >33 ms /1000 | >50 ms /1000 |
|---|---|---|---|---|---|---|---|
| **(a)** | current, rung 3 — **shadows OFF** | **43.46 ms** | 23.0 FPS | 42.86 | 78.60 | 1000.0 | 58.8 |
| **(b)** | current, rung 0 — full penumbra | **43.62 ms** | 22.9 FPS | 43.00 | 75.41 | 1000.0 | 60.0 |
| **(c)** | **pre-Phase-7** (`cbdc90f`) | **9.32 ms** | 107.3 FPS | 8.88 | 37.28 | — (6.3% over 16.6 ms) | — |

### A.2 What this says, plainly

**1. Phase 7 regressed the moving frame by ~4.7×, and it is NOT the pre-existing
tail.** Stage A's first question was whether the stutter is new. It is. Arm (c)
runs the same scene, the same motion and the same live fluid at **9.32 ms**;
arm (a) runs it at **43.46 ms**. This is not the permanently-unattributable tail
of `FPS_INVESTIGATION` §6 / `PHASE_6_COMPLETION` open item 4 — that tail is a
small fraction of long frames against a healthy median, and arm (c) shows
exactly that shape (p50 8.88 ms, 6.3% of frames over 16.6 ms). Arm (a)'s median
is 42.86 ms. **The median moved. That is a new, systematic cost, not a tail.**

**2. The shadow ray is NOT what costs it.** Arms (a) and (b) differ by **0.4%**
(43.46 vs 43.62 ms) — far inside run-to-run spread. Turning §6.5's shadow ray
completely off recovers **nothing**. Whatever Phase 7 added that costs ~34 ms
per frame is present at rung 3.

**3. This CONTRADICTS §6's earlier diagnostic, and the contradiction is
informative rather than an error in either.** §6 measured rung 3 at 15.31 ms and
rung 0 at 52.06 ms — a 3.4× shadow-ray cost. That was a **static camera with
fluid off**. These arms are **moving with fluid live**. Both are real; they are
different workloads. Read together they say: the shadow ray is expensive when
the frame is otherwise cheap, and irrelevant when something else is already
costing 43 ms. **The something else only appears under motion**, which is
precisely why §6's static probe could not see it and why the owner's report
("while turning and walking") was the necessary clue.

### A.3 The candidates, and what is already ruled out

Checked by inspection before measuring (cheapest first, as Stage A directs):

- **(a) `LightingConfig`'s hot-reload poll — RULED OUT as a *deviation*.** It
  calls `ReloadIfChanged()` once per frame, two `stat()` calls when nothing
  changed. That is **exactly** `PlayerConfig`'s cadence, verified by reading
  both call sites (`PlayerController.cs:112`/`:172` vs `LightingDriver.cs:84`).
  It is a second poll where there was one, not a more aggressive one.
- **(d) `ShadowRayDump` — RULED OUT by inspection.** It is added to the
  Phase 7 Cycle scene only; `GeneratePlayground` does not add it (0 matches in
  that method). It is not in the binary under test.
- **(b) The per-frame light upload — PRESENT, CONFIRMED BY READING.**
  `LightSourceBuffer.Upload()` runs unconditionally every frame: it copies the
  registry to scratch, sorts by distance, rebuilds the staging array and calls
  `GraphicsBuffer.SetData` — **even with zero lights placed**. That was a
  deliberate choice (the *active set* depends on player position) and is
  documented as such in that file, but "deliberate" is not "measured", and a
  per-frame `SetData` on a buffer the shader reads is exactly the shape of
  thing that stalls under motion.
- **(c') A CANDIDATE STAGE A's LIST DID NOT NAME, found by reading the shader:
  THE SKY PROBE IS NOT GATED ON SHADOW RUNG.** `_UseSkyProbeAmbient` is tested
  independently of `_ShadowQualityLevel`, so at rung 3 — documented as
  "shadows off" — **a full extra ray per hit pixel is still fired**. Arm (a) is
  therefore not "Phase 7 minus shadows"; it still carries one ray per pixel.
  This is both a leading suspect for the regression **and**, independently, a
  defect in what rung 3 claims to be.

### A.4 Isolation in progress

Three further arms, identical to (a) but with one thing removed each, cooled
and same-session, so they are directly comparable to (a) = 43.46 ms and
(c) = 9.32 ms:

| arm | removes |
|---|---|
| **A2** | the sky probe (`-noskyprobe`) |
| **A3** | the per-frame light upload (`-nolightupload`) |
| **A4** | both |

**NOTHING IS FIXED YET AND NOTHING IS CLAIMED FIXED.** The standing rule is
isolate before fixing; two seams were added to make that possible
(`Playground.DebugSuspendLightUpload` → `LightingDriver.SuspendUpload`, and the
pre-existing `-noskyprobe`), and the numbers decide which candidate to act on.
Results land in §A.5 below.

### A.5 Isolation results — the sky probe was innocent

| arm (all rung 3, moving, fluid live, 300 s cooled, same session) | mean frame | >33 ms /1000 | >50 ms /1000 |
|---|---|---|---|
| baseline | 43.46 ms | 1000.0 | 58.8 |
| **A2** — `-noskyprobe` | 43.13 ms | 1000.0 | 50.4 |
| **A3** — `-nolightupload` | **11.25 ms** | 43.8 | 3.3 |
| **A4** — both | 8.89 ms | 6.3 | 0.0 |
| *(c)* pre-Phase-7 | 9.32 ms | — | — |

**The sky probe — a full extra ray per hit pixel, and my leading suspect — was
innocent** (43.46 → 43.13). The per-frame light upload was the whole of it.

### A.6 The first fix was WRONG, and what the real cause turned out to be

**Attempt 1** added a dirty-check so `SetData` only fired when the bytes
changed. Verified on the rebuilt binary, cooled:

| arm | mean frame |
|---|---|
| FIXED rung 3, upload on | 42.21 ms *(was 43.46 — unchanged)* |
| FIXED rung 0, upload on | 42.80 ms *(was 43.62 — unchanged)* |
| upload suspended via the seam | 10.53 ms |

Suspending `Upload()` **still** gave 4× after the dirty-check. That proved the
cost was never the `SetData` at all — it was inside `Upload()`.

**THE ACTUAL CAUSE. Lava is `IsFluid | IsEmissive` — emissive AND mobile.**
`-stutterprobe` opens all vents; the Playground's lava vent budget is **12,000
voxels**; and every lava cell goes through §6.6's enqueue on the §8.3 edit path
and **registers as a point light**. `SelectActive` then insertion-sorted
thousands of entries every frame: O(n²), ~144M operations.

**The assumption that failed was mine, and it is worth naming precisely.** My
comment on that sort read *"at n in the tens this is also simply faster"*. That
was true. The assumption *behind* it — that n would be in the tens — came from
§3.8's *"a handful of placed torches, not a lit city"* and §6.5's *"v1 lights are
genuinely sparse"*. Both of those are statements about **placed torches**, and I
applied them to **every emissive material**. A fluid can register itself.

**Two changes — cause and hazard:**

1. **CAUSE.** `if (MaterialRules.IsMobile(material)) return false;` in
   `Register`. §3.8's own scope, enforced rather than assumed.
2. **HAZARD.** Partial selection replaces the full sort: O(n × 32) regardless of
   n, so no future emissive can make it quadratic again. The assumption about n
   was already wrong once.

### A.7 VERIFICATION — the fix works, and the mutation arm proves it

Rebuilt, 300 s cooled, same probe, same pose, same flags:

| arm | mean frame | sustained | >33 ms /1000 | registered | SetData calls |
|---|---|---|---|---|---|
| **rung 3, upload ON** | **10.54 ms** | 94.9 FPS | 41.3 | **0** | **1** |
| **rung 0, upload ON** | 13.63 ms | 73.4 FPS | 43.3 | 0 | 1 |
| **mutation — upload SUSPENDED** | 10.52 ms | 95.1 FPS | 41.3 | 0 | 1 |

**THE MUTATION ARM IS THE PROOF.** `-nolightupload` is now a **no-op** — 10.52
against 10.54, a 0.2% difference. Before the fix that same seam moved the frame
from 43.46 to 11.25 ms. A seam that no longer changes anything is exactly what
"the expensive work is gone" looks like, and it is stronger evidence than the
headline number alone because it cannot be explained by the machine being
cooler.

**Two direct observations confirm the mechanism rather than inferring it:**
`LIGHTS registered 0` (the 12,000 lava cells no longer register) and
`SetData calls 1` (one upload served all 2,400 frames).

**Against the pre-Phase-7 baseline:** 10.54 ms vs **9.32 ms** — Phase 7 now
costs about **13%** on the moving frame, which is the sky probe, the sun cycle
and the C.9 combine. That is a real cost and it is stated rather than rounded
away, but it is not a 4.7× regression.

**The stutter distribution, which is what "we can feel each frame" actually
means:** long frames per 1000 went from **1000.0 / 58.8** (>33 ms / >50 ms) to
**41.3 / 1.7**. The pre-Phase-7 build sits at 6.3% of frames over 16.6 ms.

### A.8 A CONFOUND IN THESE ARMS, stated so the numbers are not over-read

**`-stutterprobe` does not set the time of day, and the Playground's clock
RUNS** (`dayLengthSeconds` 120, runs ~100 s). So the sun sweeps most of a
day–night cycle during each arm, and **shadow rays are skipped entirely while
the sun is below the horizon** (§6.5).

**What that does and does not invalidate:**
- It does **not** affect Stage A's conclusions. Every arm shares the same clock
  behaviour, so the before/after and the arm-to-arm comparisons hold.
- It **does** mean the rung-0 figure here (13.63 ms) is an average over a full
  cycle, **not a golden-hour number**, and must not be compared with §E's
  golden-hour measurements or read as "rung 0 now fits the budget". §E's
  cooled golden-hour arms remain the figures of record for rung cost.

Fixing the probe to pin the clock is a one-line change and is **not** done here,
because redoing every Stage A arm to get it would cost the rest of the night for
a conclusion that does not change.

---

## B. STAGE B — `N·L` on point lights: the default flipped, on evidence

**§10 flag 3 is now decided as a DEFAULT, and still reversible.**

`RaymarchFeature.PointLightNdotL` was `false` — C.9's literal grouping, which
attaches `max(0, N·L)` to the sun term only and leaves `pointLights` bare. It is
now `true`.

**The evidence is this phase's own screenshot, not taste.** §8 recorded that in
`02_cave_lit_by_torch.png` every face of the chamber renders identically
regardless of which way it points, because with no `N·L` the only thing
modulating a point light is distance. A cube lit from one side that is equally
bright on all six faces does not read as lit — it reads as *tinted*. §6.1's
aesthetic contract states the look is carried by "albedo, the lighting of §6.5,
and voxel ambient occlusion"; a lighting term that cannot distinguish a face
from its opposite is not carrying anything.

**Why this is a default flip and not a spec deviation to argue about:** the
toggle is unchanged and still reachable, `false` still reproduces C.9's literal
grouping exactly, and both states are screenshotted side by side. It is
reversible in one edit.

**Pinned by a test** (`LightingTests.PointLightNdotL_DefaultsToOn_AndIsStillAToggle`)
whose message says explicitly that reverting is allowed but must be deliberate —
so a merge cannot flip it back silently.

---

## C. STAGE C — four lighting presets, offered rather than chosen

### C.1 The part that is NOT a style choice: snow clips to pure white at noon

The owner reported the snow biome as "blindingly bright". **It is, and it is
arithmetic.** For an up-facing snow voxel at noon the shader computes

```
out = snowAlbedo × (zenithColour × sunIntensity × N·L
                    + ambientColour × ambientIntensity) × hemiUp
```

with `snowAlbedo.b = 0.760`, `zenith.b = 0.92`, `hemiUp = 1.12`, `N·L ≈ 0.97`.
At the shipped values (`sunIntensity 1.25`, `ambientIntensity 0.30`):

```
0.760 × (0.92×1.25×0.97 + 0.78×0.30) × 1.12  =  1.149     CLIPPED
```

All three channels exceed 1.0, so the surface saturates to flat white and the
terrain shape disappears into it. That is the reported symptom exactly.

**AND — the part worth reading — LOWERING AMBIENT ALONE DOES NOT FIX IT:**

```
ambientIntensity 0.30 → 0.10:
0.760 × (0.92×1.25×0.97 + 0.78×0.10) × 1.12  =  1.05      STILL CLIPPED
```

because the **sun term alone is already at the ceiling**. §8 diagnosed golden
hour as "ambient is 1.5–5× the sun", and the natural reading of that is "turn
ambient down". For golden hour that is right. For snow at *noon* it is
insufficient — and a preset that only did that would have left the owner's
actual complaint in place while appearing to address it. Stage C's brief asked
for this distinction to be checked rather than assumed; it was, and it matters.

**The constraint, solved for `sunIntensity` S at ambient A:**

```
0.8924·S + 0.78·A ≤ 1.1747      ⇒   S ≤ (1.1747 − 0.78·A) / 0.8924
```

Every preset sits under its own ceiling. The shipped config sits **15% over**.

**The palette is NOT the place to fix this.** `MORNING_REPORT.md` item 1 already
lowered snow 0.86 → 0.72 for this same clipping class. That fix was correct and
is untouched. What changed since is that Phase 7 added a real *sun* term on top
of the hemisphere term, re-opening the headroom problem from the other side — so
the fix belongs in the lighting constants. A test pins the snow palette
unchanged precisely so a future session cannot "fix" this in the wrong file.

### C.2 The four presets

All four are `LightingPresets.cs` entries, written to disk as
`LightingConfig_<Name>.json` beside `LightingConfig.json`, and switchable live
with `[` / `]` in the Playground (the key writes the chosen preset over
`LightingConfig.json`, so the existing §8.1 poll picks it up next frame — and the
owner ends up with the preset already sitting in the file they were told to edit).

| preset | direction | k_soft | ambient | sun | snow@noon |
|---|---|---|---|---|---|
| **Warm** | sun-dominant golden hour — the direct answer to §8 | 0.035 | 0.11 | 1.15 | 0.861 |
| **Atmospheric** | soft, wide penumbra, warm/cool split | 0.090 | 0.17 | 1.02 | 0.848 |
| **HighContrast** | near-hard shadows, deep ambient, dramatic | 0.012 | 0.055 | 1.20 | 0.974 |
| **Muted** | low-saturation, even, the safe floor | 0.050 | 0.15 | 0.95 | 0.816 |
| *Shipped* | tonight's values, kept for A/B | 0.030 | 0.30 | 1.25 | **1.149 CLIPS** |

**MY READ, WHICH IS NOT A DECISION.** If forced to pick one against the stated
brief — "soft, atmospheric, painterly, warm/cool balance, distinct not a copy" —
I would pick **Atmospheric**, for three specific reasons rather than a general
impression:

1. **`k_soft` 0.090, three times shipped.** This is the only lever that makes
   voxel edges read as *painted* rather than *cut*. C.10's penumbra is the one
   thing this renderer has that a hard-shadow voxel game does not, and at 0.03
   it is nearly invisible.
2. **Ambient kept comparatively high but pushed lilac-cool** (0.50, 0.55, 0.74)
   against a warm sun. That split *is* the warm/cool balance — and it is
   precisely what gets lost if ambient is merely turned down, which is the
   trap §8's "ambient is too high" phrasing sets.
3. **A wide sun ramp (30°) and a 300 s day**, so golden hour is something you
   watch rather than catch.

**Warm** is the better answer to §8 read literally and will look more dramatic
at golden hour; **HighContrast** exists so the comparison has a pole rather than
three variations on soft, and is the one that makes the shadow ray visible as a
*feature*; **Muted** is the floor to fall back to if the other three all read as
too much. **The choice is the owner's**, as it has been for every art constant in
this project.

---

## D. STAGE D — lighting stress and distance

### D.0 A rig defect that cost the first run, recorded because it was avoidable

The first Stage D pass produced ten unusable frames: the camera was positioned
inside a carved cave and every shot came back from the **surface**, with the F2
help panel across the middle of frame. Cause: `Playground.Start` forces fly mode
and hides the help for `-playgroundshots` and `-gputrace`, because — in that
block's own words — *"in walk mode DebugStep rewrites the camera every frame
from the motor, and the two fight; the symptom is screenshots taken from
wherever the player happened to be standing."* `-p7stress` was not added to that
list. **The trap was documented in the exact file I was editing and I walked
into it anyway.** Fixed, plus a `DebugHideUi` so capture passes produce a
picture of the world rather than of the UI. Any future capture mode must be
added to that condition.

### D2 — many-light stress *(the one sub-test whose numbers survived the first run)*

Thirty-two torches — `MAX_ACTIVE_LIGHTS` — down both walls of a 17 m corridor at
even spacing, which is what a player lighting a base actually builds rather than
an adversarial layout.

**CORRECTNESS, at real density through the real edit path:**
- 32 placed → **32 registered, 32 active**, cap not binding.
- 12 more placed past the cap → **44 registered, 32 active**. The over-cap
  torches remain *registered* — §3.8's "must not silently ignore new torches"
  holds at density, not just in the unit test.

**PERFORMANCE MEASURED** *(same-session back-to-back, **not** cooled — a
diagnostic, and labelled as one)*:

```
  0 lights   20.23 ms/frame
 32 lights   20.57 ms/frame      delta +0.33 ms for the full cap
```

**Point lights are effectively free.** +0.33 ms for 32 simultaneous lights, i.e.
~0.01 ms each, against a 16.67 ms frame. §3.8's entire reason for existing is
the "per-pixel light death spiral" (2M pixels × N lights); at v1's cap on this
hardware that spiral is nowhere near engaging. **This is the strongest argument
in the phase that `MAX_ACTIVE_LIGHTS = 32` is conservative** — though raising it
is §0.2's decision and needs its own re-derivation, not an inference from one
uncooled pair.

### D1 — a torch in an irregular cave: YES, it reads right

Re-run with the camera fix. An irregular chamber — three overlapping lobes of
different radii plus a low side passage, 24,953 voxels carved through the real
§8.3 path — rather than the rig's sphere, because a sphere lights too evenly to
tell you anything.

**Looked at, not just counted.** `D1_02_one_torch_3m.png` reads as a genuine
torch-lit cave: a bright warm source voxel, smooth falloff across the floor and
up the walls, and — visibly — **Stage B's `N·L` doing its job**: the horizontal
tops of the stepped ledges are clearly brighter than their vertical faces, and
the ceiling falls away dark. That distinction did not exist before Stage B; the
earlier capture was a uniform wash in which every face of every cube read the
same. This is the single most visible improvement of the session.

**Honest criticism:** the result is close to monochrome — everything in torch
range is the same warm tan, because the torch dominates and underground ambient
is near-zero by design (§6.5). That is defensible and arguably correct for a
torch-lit cave, but it is worth the owner's eye: if it reads as flat *brown*
rather than *warm*, the lever is `undergroundAmbientFloor` (a little cool
ambient would separate the far walls from the near ones).

**Multi-light:** 3 torches spread across the lobes registered 3/3 and their
falloffs overlap smoothly — no seams, no double-bright patches at the overlaps.

### D3 — distant lit terrain: the speckle is real, and it is LOCALISED

Twelve paired captures (Beauty + sun-visibility) at 30/55/75/110/150/240 m,
at noon and at golden hour, plus a 5-step lateral pan at 200 m.

**What the far field actually shows** (`D3_golden_240m_tier2_sunvis.png`): the
shadows are **coherent, structured ribbons that follow terrain contours** — they
are unmistakably real cast shadows of real relief, not noise. Near-field
shadows are solid and crisp.

**The §8 speckle is present, and it is CONCENTRATED rather than general:** it
appears (i) in the horizon band, where terrain is seen at a grazing view angle,
and (ii) as some regular dot patterns in the near-right field. It is *not*
spread evenly across the far field, which is what a general coarse-tier sampling
defect would look like.

**VERDICT, scoped to what can be shown: (c) INCONCLUSIVE, but leaning (a).**
The concentration at grazing view angles is what genuine per-coarse-voxel
shadowing would produce — at a shallow angle each coarse voxel's shadow
alternates over a few pixels — and it is not what an addressing bug would
produce (that would be distance-dependent, not angle-dependent). **This still
cannot be settled without a tier-1/2 oracle** (Amdt 8.10 §5.7), exactly as §8
said, and building one was explicitly out of scope tonight. The regular dot
pattern is the part I would look at first if it is ever chased.

**In Beauty it is much less visible**, as §8 predicted, and Stage B/C did not
make it worse.

**NOT ANSWERED:** whether the distant shadow *swims* under lateral motion. The
five `D3_swim_*` frames exist and are directly comparable, but judging drift
across five stills is a job for a human flipping between them — I did not want
to assert a conclusion from a comparison I cannot do well. **This is a good
thirty seconds of the owner's five minutes.**

### D4 — lighting under combined chaos: captured, not asserted

Five frames at golden hour with live fluid, active digging and two torches near
the action. §3.10 predicts the shadow and the torch stay correct because both
read the authoritative terrain byte; these are the visual spot-check, not a new
correctness proof. The frames exist and show no obvious tearing or stale
lighting, but **five stills of a moving scene are weak evidence** and are
offered as such.

## E. STAGE E — rung 1 measured at last, and it is NOT a usable fallback

> **SUPERSEDED IN PART — READ §F.4 FIRST. Kept deliberately, not corrected in
> place.** Every arm below was measured before `263b580`, the commit that stopped
> ~12,000 lava voxels registering as point lights into an O(n²) selection. The
> absolute frame times in this section are therefore dominated by that bug, and
> the conclusion it draws — *"no shadow rung holds 60 FPS"* — is **false**: rung 0
> re-measures at 12.29 ms / 81.3 FPS. What survives is the *relative* finding,
> because both arms carried the same bug: **rung 1 is not cheaper than rung 0**,
> and §F.1's counters later explained why.
>
> This section is left standing because the mistake it records — trusting a
> cooled, counterbalanced, correctly-run measurement whose *binary* contained a
> known-unfixed bug — is the more useful lesson than the numbers. Protocol
> discipline does not protect you from measuring the wrong build.


**Cooled (300 s before every arm), counterbalanced A/B/B/A, same session, same
binary, fullscreen, golden hour, shipped 960×540 gate, fluid off.** This is the
first Phase 7 measurement that meets `AMENDMENT_8_12` §3.1 in full.

| arm | rung | mean frame | sustained |
|---|---|---|---|
| 1 | **1** — half-res beyond LOD1 | 53.18 ms | 18.8 FPS |
| 2 | **3** — off | 15.10 ms | 66.2 FPS |
| 3 | **3** — off | 15.17 ms | 65.9 FPS |
| 4 | **1** — half-res beyond LOD1 | 52.95 ms | 18.9 FPS |

**Within-rung spread: 0.23 ms (rung 1) and 0.07 ms (rung 3). Between-rung
difference: ~38 ms.** The difference is two orders of magnitude larger than the
noise, so unlike §6's diagnostic this one is established rather than merely
indicated.

### E.1 The finding: rung 1 saves nothing

Set beside §6's uncooled diagnostic for the other rungs:

| rung | what it is | mean frame | holds 60 FPS? |
|---|---|---|---|
| 0 | full soft penumbra | 52.06 ms *(uncooled)* | no |
| **1** | **half-res beyond LOD1** | **53.0 ms** *(cooled)* | **no** |
| 2 | hard only | 40.77 ms *(uncooled)* | no |
| 3 | off | **15.1 ms** *(cooled)* | **yes** |

**Rung 1 is not cheaper than rung 0 — it is indistinguishable from it, possibly
marginally worse.** §6.5 designed rung 1 as the *first* fallback to engage; as
implemented it is not a fallback at all.

### E.2 Why, stated as a hypothesis with its evidence — not as a fact

**HYPOTHESIS:** rung 1 only halves the rate for pixels whose primary hit is
**beyond the LOD1 boundary** (`currentDist >= _TierOuterRangeVoxels.y`, 128 m).
At the `GroundHorizon` golden-hour pose, most pixels that resolve a hit are
*nearer* than 128 m — the ground in front of the camera — so almost nothing
takes the reuse path, while every lane still pays the group barriers the reuse
requires.

**WHAT SUPPORTS IT:** Gate 3's own sampling at this pose recorded 438 hits out
of 960 samples with the rest sky, and the mismatches it listed were at voxel Y
41–53 — near-field ground, not distant mountain. **WHAT WOULD SETTLE IT:** a
per-pixel count of how many took the reuse path, which the shader does not
currently expose. **It is not settled here, and is written as a hypothesis
because §0.1 invariant 12 requires a plausible cause to carry a measurement.**

If the hypothesis holds, the lever is the *threshold*, not the mechanism —
half-rating from, say, 32 m rather than 128 m would engage it on the pixels that
actually dominate. That is a one-constant experiment and it is **not** done here.

### E.3 What this leaves for §10 flag 4

**Only rung 3 holds 60 FPS**, and by a wide margin (15.1 ms against a 16.67 ms
frame). Rungs 0, 1 and 2 all sit at 40–53 ms.

**This does not decide the flag, and deliberately so** — but it narrows it to an
unpleasant pair: ship with **no sun shadows at all**, or ship a rung that misses
60 FPS at golden hour. Two things worth weighing before concluding the feature
cannot ship:

1. **§6.5's own fallback ladder has an untried rung between 1 and 3:** the
   distance at which shadow rays are *skipped entirely* (rather than
   half-rated). The shadow clamp currently inherits the primary ray's 290 m.
2. **§E.2's threshold hypothesis is cheap to test** and, if right, makes rung 1
   behave as designed.

Neither was done tonight. Both are one-constant experiments.

---

## F. STAGE F — why rung 1 does nothing: measured, not guessed

### F.1 The instrumentation, and what it settles

§E.2 offered a *hypothesis* for rung 1's non-effect ("most hit pixels at this
pose are nearer than the 128 m LOD1 boundary") and said what would settle it: a
per-pixel count of how many take the reuse path. That counter now exists
(`ShadowStats`, frame-wide `InterlockedAdd`, behind `_CollectShadowStats` and
off by default so it cannot contaminate a timing arm).

At the Stage E pose (`GroundHorizon`, fluid live, 960×540, release), over one
frame:

| time | rung | hit pixels | beyond LOD1 | rays traced | reused | % beyond |
|---|---|---|---|---|---|---|
| golden hour | 0 | 1,043,918 | **34** | 1,043,918 | 0 | **0.003%** |
| golden hour | 1 | 1,044,210 | **80** | 1,044,148 | **62** | **0.008%** |
| midday | 0 | 1,044,396 | 72 | 1,044,396 | 0 | 0.007% |
| midday | 1 | 1,044,402 | **0** | 1,044,402 | 0 | **0.000%** |

*(counts span two frames — the reset-to-read window caught two dispatches — so
read the ratios, not the absolutes.)*

**Rung 1's branch engages for 62 pixels out of 1,044,210.** The hypothesis was
right in direction and far understated in degree: it is not "a small fraction",
it is **three thousandths of one percent**.

### F.2 The cause is geometric, and it is not a bug in the mechanism

At a **ground-level camera**, terrain within 128 m occludes essentially
everything behind it. What lies beyond the LOD1 boundary is *sky* — and sky has
no primary hit, so it fires no shadow ray and cannot be helped by a shadow
optimisation. Distant terrain occupies a thin band of pixels near the horizon
line, and at this pose that band is ~34 pixels.

**So rung 1 is not broken. As specified it can only help aerial and overlook
views** — where a large share of the frame is distant terrain. At Amendment
8.9's "real gameplay camera", which is the pose every budget question in this
project has been asked at, it is **structurally incapable of saving anything**,
and no amount of repairing the mechanism changes that.

**A hypothesis I held and DISPROVED, recorded because it was wrong:** reading
the code, I expected the failure to be SIMD lockstep — `traceMine` masks *lanes*,
and a wave costs the max over its lanes rather than the sum, so one-of-four
lanes tracing would save nothing. That would have shown as *traced count falling
while time stayed flat*. The counters show the traced count **not falling at
all** (1,044,148 vs 1,043,918). The mechanism never ran. My explanation was
plausible, more interesting, and wrong.

**Lowering rung 1's threshold was considered and rejected as the lever.** It is
now configurable (`rung1DistanceM`) purely so the question could be asked, but
even if a lower threshold engaged the branch, the SIMD objection above is
untested and would likely cap the benefit. The data points somewhere better.

### F.3 The lever the data actually points at

Every shadow ray starts within 128 m of the camera — and every one was allowed
to travel to the **primary ray's 290 m clamp**. §6.5 predicts exactly where that
goes: *"grazing golden-hour rays travel farthest"*. §6's isolation measured the
sun ray at **+25.5 ms**, the single largest cost in the frame.

`PHASE_7_COMPLETION` §5.4 already named a shorter shadow clamp as an unbuilt
lever. It is now built — `LightingConfig.shadowMaxDistanceM`, per-preset and
live-tunable, `<= 0` meaning "inherit the primary clamp" so every prior
measurement stays reproducible. **The trade is real and is why it is a config
value rather than a constant:** a shorter clamp means distant geometry stops
casting onto you and shadows fade out at range.

### F.4 Results — the clamp lever is DISPROVED, and rung 0 is not what §E said

**Protocol:** seven arms, each preceded by a 300 s cooldown, counterbalanced
(the first and last arms repeat conditions run in the middle), one session, one
release binary, fullscreen, 960×540 gate, moving camera, **live fluid**, and —
new since §E — the **clock PINNED at golden hour (17:35, sun 6.0°)** for every
arm, which removes §A.8's confound.

| arm | mean frame | sustained | >33 ms /1000 | >50 ms /1000 |
|---|---|---|---|---|
| rung 3 — shadows off | 8.91 ms | 112.3 FPS | 12.8 | 0.6 |
| **rung 0 — FULL penumbra, clamp 290 m** | **12.29 ms** | **81.3 FPS** | 23.3 | 13.9 |
| rung 0, clamp 96 m | 12.27 ms | 81.5 FPS | 22.8 | 13.3 |
| rung 0, clamp 48 m | 11.86 ms | 84.3 FPS | 20.6 | 12.8 |
| rung 0, clamp 24 m | 12.16 ms | 82.2 FPS | 21.7 | 12.8 |
| rung 0, clamp 48 m *(repeat)* | 12.65 ms | 79.1 FPS | 23.9 | 12.8 |
| rung 3 — off *(repeat)* | 9.13 ms | 109.5 FPS | 11.7 | 3.3 |

#### The shadow-distance clamp buys nothing. This is a disproved hypothesis.

§F.3 predicted that clamping the shadow ray well short of the primary ray's
290 m would recover a large part of the sun ray's cost, on §6.5's reasoning that
grazing golden-hour rays travel farthest. **It does not.**

- Across a **12× range of clamp distance** (290 m → 24 m) the spread is
  **0.43 ms** (12.29 → 11.86).
- The **repeat of a single condition** — clamp 48 m, run twice in the same
  session — spread **0.79 ms** (11.86 → 12.65).

The between-condition signal is *smaller than the within-condition noise*. The
lever is not weak, it is **absent at the resolution this rig can measure**. The
most likely reason is that §8.8's air-mip already makes long empty traversal
nearly free, so the distance a ray is *allowed* to travel is not what it costs —
consistent with §F.1's counters, which showed the work is in the near field.

The config field (`LightingConfig.shadowMaxDistanceM`) is **kept, not reverted**:
it costs nothing at `<= 0`, it is the honest record of the experiment, and it is
the control any future art decision about shadow fade-out would use. **It is not
a performance lever and must not be presented as one.**

#### The larger finding: §E's picture of rung 0 was wrong

§E.1 reported rung 0 at **52.06 ms** and concluded "no shadow rung holds 60 FPS".
This measurement puts **full soft-penumbra rung 0 at 12.29 ms — 81.3 FPS**, with
~3.2–3.4 ms of that attributable to shadows (12.29 vs rung 3's 8.91/9.13).

**Why the two disagree — stated as established, with the check that establishes it:**

1. **§E's arms predate the lava fix.** §E was committed in `991cc01`; the real
   cause of the stutter — lava registering ~12,000 voxels as point lights into an
   O(n²) selection — was not fixed until `263b580`, the *next* commit. Every
   number in §E and §6 therefore has that sort in it. §A.7 measured that same
   pose dropping from 42.21 ms to 10.54 ms once it was fixed.
2. **§E's clock was not pinned.** §E ran `-lightingab`, which sets golden hour at
   the start and then lets the cycle advance through the arm; §F2 runs
   `-stutterprobe -tod`, which holds it. The two harnesses also use different
   camera paths, so they are not otherwise directly comparable.

(1) alone is sufficient to explain a 40 ms difference and is verifiable from the
commit order, so the contamination is **established**, not hypothesised. The
*residual* difference between the two harnesses is not separated here and is not
claimed to be.

#### What this does and does not license

**Does:** at golden hour, at the shipped gate, with live fluid and a moving
camera, **rung 0 has 4.4 ms of headroom against the 16.67 ms frame.** That is the
first Phase 7 measurement in which the full-quality shadow path fits the budget.

**Does not:** golden hour is one point in the cycle. §6.5 *predicts* it is the
worst one; that prediction has never been tested in this project. **§F.5 tests
it** — 12 points across a full day/night, clock pinned per point, at rung 0 and
rung 3. Until that lands, the sentence "shadows fit the budget" is true of
**17:35 only**, and is written that way deliberately.

Note also the tail: rung 0 carries **13–14 frames per 1000 over 50 ms** against
rung 3's 0.6–3.3. The mean fits; the tail is materially worse and is part of
§10 flag 4's decision, not a footnote to it.
### F.5 The full day-cycle sweep — and §6.5's worst-case prediction is WRONG

Every budget measurement in this project, from §6 through §F.4, has been taken at
golden hour, on §6.5's stated reasoning that *"grazing golden-hour rays travel
farthest"*. **That prediction has never been tested. It is tested here, and it
does not hold for this content.**

**Protocol.** 12 points, every 2 in-game hours, **clock pinned per point**, pose
reset to the arena and re-flown at 12 m/s per point, live fluid, 960×540 gate,
release standalone, fullscreen. All 12 points in one process so they are
comparable to each other; the whole sweep run twice, once at **rung 0** and once
at **rung 3**, each preceded by a 300 s cooldown.

| hour | sun elev | rung 0 (full penumbra) | rung 3 (off) | **Δ = the shadows** |
|---|---|---|---|---|
| 00:00 | −76.0° | 11.62 ms | 11.59 ms | **+0.03** |
| 02:00 | −57.2° | 11.71 | 11.54 | +0.17 |
| 04:00 | −29.0° | 11.57 | 11.45 | +0.12 |
| 06:00 | 0.0° | 11.00 | 10.94 | +0.06 |
| 08:00 | 29.0° | 14.92 | 11.32 | **+3.60** |
| 10:00 | 57.2° | 16.44 | 12.91 | +3.53 |
| 12:00 | 76.0° | 15.97 | 13.40 | +2.57 |
| 14:00 | 57.2° | 17.06 | 13.39 | +3.67 |
| **16:00** | **29.0°** | **18.98** | **13.33** | **+5.65 ← worst** |
| 18:00 | 0.0° | 13.83 | 13.88 | −0.05 |
| 20:00 | −29.0° | 13.88 | 14.06 | −0.18 |
| 22:00 | −57.2° | 14.32 | 14.02 | +0.30 |

#### Rung 3 is not a second result — it is the control, and it invalidates the absolutes

**Read the rung-3 column on its own first.** It has no sun shadows at any hour,
so every number in it *should* be the same. It is not: it climbs from 10.94 ms to
14.06 ms across the run, monotonically. **Night at the start of the sweep costs
11.59 ms; night at the end costs 14.06 ms — identical work, +2.5 ms.**

So roughly 2.5 ms of the rung-0 column is *when the sample was taken*, not what
time of day it depicts. **Two mechanisms, and the second one is my design error:**

1. **Thermal.** A fanless M1 Air under ~7 minutes of sustained load throttles.
2. **Accumulating fluid — a confound I introduced and did not control.** The
   sweep opens every fluid vent once at the start and then runs all 12 points.
   Fluid keeps spreading for the whole sweep, so the late points are simulating
   and drawing materially more water than the early ones. Time-of-day and
   fluid-load advance together, which is exactly the shape of confound this
   sweep existed to remove from the *clock*, reintroduced on another axis.

**And the Δ column does not rescue it.** I initially reasoned that subtracting
rung 3 cancels the drift. It does not: if throttling scales GPU work by some
factor k, then rung 0 = k·(base + shadow) and rung 3 = k·base, so Δ = k·shadow —
**drift multiplies the difference rather than cancelling out of it.** Subtraction
would only cancel an additive offset, and this is not one.

**What the sweep can and cannot be used for, stated honestly:**

- **CAN:** establish the *shape* — that shadows cost nothing at all when the sun
  is below the horizon, and something real whenever it is up. That conclusion
  survives because it is a difference between **zero and non-zero**, which no
  plausible drift manufactures: five separate elevation-≤-0 points land between
  −0.18 and +0.30 ms, straddling zero, while every sun-up point is ≥ +2.5 ms.
- **CANNOT:** be trusted for absolute frame times, or for ranking sun-up points
  against each other. The candidate "worst point" at 16:00 is the ninth of twelve
  samples and carries the most heat and the most fluid of any lit sample. Its
  apparent margin over 08:00 (the same 29° elevation, but early in the run) is
  exactly what the confound would manufacture.

**§F.6 therefore re-measures 16:00 cooled, and it is §F.6 that decides anything.**

#### What the Δ column says, kept to what survives the confound

1. **Shadows cost exactly nothing when the sun is down.** Elevation ≤ 0° gives
   −0.18 to +0.30 ms across five points — noise either side of zero. The shadow
   ray is correctly skipped and **half the day is free**. This was assumed
   before; it is measured now, and it is the sweep's most durable result.
2. **Shadows cost something real whenever the sun is up** — every lit point is
   ≥ +2.5 ms even before the confound is accounted for.
3. **Which lit hour is worst, the sweep cannot say**, for the reasons above. It
   nominated 16:00; §F.6 tested that nomination.


### F.6 The worst point, measured cooled — and rung 0 holds it comfortably

**Protocol:** five arms at **16:00 (sun 29°, descending)** — the point §F.5
nominated as worst — each preceded by a **300 s cooldown**, counterbalanced
(A/B/C/B/A), clock pinned, moving camera at 12 m/s, live fluid, 960×540 gate,
release standalone, fullscreen, **the same binary as the §F.5 sweep**, deliberately
not rebuilt so the two are comparable.

| arm | rung | mean frame | sustained | >50 ms /1000 | >100 ms /1000 |
|---|---|---|---|---|---|
| 1 | 3 — off | 8.75 ms | 114.3 FPS | 0.6 | 0.0 |
| 2 | **0 — full soft penumbra** | **10.61 ms** | **94.3 FPS** | 12.2 | 4.4 |
| 3 | 2 — hard only | 9.92 ms | 100.8 FPS | 10.6 | 0.0 |
| 4 | **0 — full soft penumbra** *(repeat)* | **10.82 ms** | **92.4 FPS** | 13.3 | 5.6 |
| 5 | 3 — off *(repeat)* | 8.88 ms | 112.6 FPS | 0.6 | 0.0 |

Within-condition spread is **0.21 ms** (rung 0) and **0.13 ms** (rung 3) — tight
enough that the differences below are real.

- **Full soft-penumbra shadows cost 1.90 ms** (10.72 mean vs 8.82 mean).
- **Hard-only shadows cost 1.17 ms**, and save 0.80 ms over full penumbra.
- **Rung 0 holds ~93 FPS at the worst point the day sweep could find**, with
  **5.9 ms of headroom** against the 16.67 ms frame.

#### This also settles that the sweep's absolutes were the confound, not the clock

The sweep measured this exact time of day at **18.98 ms**. Cooled, it is
**10.61 ms**. The rung-3 control shows the same thing independently: **13.33 ms in
the sweep against 8.75 ms cooled**, for work that does not involve shadows at all.
**The sweep inflated everything by roughly 50%,** and §F.5's section on that is
written up as the design error it was rather than trimmed out.

It also means the sweep's *ranking* of lit hours should not be relied on. Against
§F.4's cooled golden-hour arms (12.29 ms rung 0, 8.91 ms rung 3 — a 3.3 ms shadow
cost) this point measures **cheaper**, at 1.90 ms. Those are different sessions
and a different binary, so the comparison is indicative rather than decisive — but
it points the opposite way to the sweep, and **§6.5's original prediction that
golden hour is the worst case is, if anything, supported** rather than overturned.
**The claim in §F.5's first draft that §6.5's prediction is wrong is withdrawn: it
rested on the inflated ranking.**

#### The one thing that is genuinely worse with shadows on

The **tail**, and it is not a rounding difference:

| | >50 ms per 1000 | >100 ms per 1000 |
|---|---|---|
| rung 3 — off | 0.6 | **0.0** |
| rung 2 — hard only | 10.6 | **0.0** |
| rung 0 — full penumbra | 12.2 / 13.3 | **4.4 / 5.6** |

Turning shadows on at all takes >50 ms frames from ~0.6 to ~11–13 per 1000.
Turning on the **soft penumbra specifically** is what introduces >100 ms frames,
which rungs 2 and 3 produce **none** of. That is roughly one frame in 200 over
100 ms — perceptible as a hitch, and invisible in every mean in this document.

**p99 is not a headline here and is not used as one** (`AMENDMENT_8_12` §3.2), but
a category that goes from *never* to *regularly* is a difference in kind, not in
degree. **Rung 2 buys that tail away for 0.80 ms of the 1.90 ms that shadows
cost, while keeping shadows.** Whether the soft edge is worth a hitch every ~200
frames is a feel judgement, which is why it is §10 flag 4 and not a decision here.

---

## I. STAGE I — the HUD says what you are looking at, without being asked

**The problem, in one sentence:** the preset and the shadow rung lived *inside*
the F1 debug panel, and that panel was open by default — so the choice was
between a screenshot with a 292 px wall of diagnostics across it, and a
screenshot with no record of which preset or rung produced it. Every judgement
made with F1 closed was made without the two facts needed to interpret it.

**What changed.**

1. **A one-line strip, always on, top-right:**
   `PRESET Atmospheric   SHADOWS Full (soft edges)   81 fps (12.3 ms)   [ ] preset  J shadows  F1 detail`
   The frame time is coloured against the 16.67 ms frame — green under, amber to
   22 ms, red beyond — so "am I inside budget" needs no arithmetic.
2. **The detailed overlay now starts CLOSED** and is entirely behind `F1`. It
   keeps everything it had; it also now draws *below* the strip, so pressing F1
   never makes the strip jump somewhere else.
3. **The rungs are named in plain language on the strip:** *Full (soft edges)* /
   *Cheaper far away* / *Hard edges only* / *Off*. The spec-accurate names
   (`1 HALF-RES beyond LOD1`) are kept in the F1 panel, where citing §6.5 is what
   you want. On a line that is on screen while someone judges how the game looks,
   "HALF-RES beyond LOD1" asks the reader to know what LOD1 is first.
4. `PLAYGROUND_GUIDE.md` §3 and §5 updated, including the new `8`/`9`/`0`
   coloured-torch brushes.

**The strip's fps is explicitly labelled as not evidence**, in the guide and in
the source comment. `CLAUDE.md` is unambiguous that the only trusted frame-time
source is the rig's own report from a release standalone, and a number that is
always on screen is exactly the kind that gets quoted later as though it were.

**The first version of the strip was broken, and only looking caught it.** It
compiled, it drew, and it was *clipped*: the box was a fixed 560 px, the preset
name at runtime is `custom (LightingConfig.json as found)`, the label wrapped to a
second line, and the box was one line tall — so the hint text was sliced in half
and the frame time sat outside the panel. `PlaygroundHud`'s own comment already
says a HUD line that is cut off is worse than one that is absent, because it looks
like it is there. Fixed by measuring the text (`CalcSize`) instead of guessing a
width, disabling word wrap, and truncating the preset name explicitly. **Verified
by taking a screenshot and reading it**, not by the fact that it built.

**One trap worth recording, because it would have been silent.** `_visible` is a
`[SerializeField]`, and `Phase5aSceneBuilder` *explicitly writes it* into the
generated scene. Changing the field's default in `PlaygroundHud.cs` alone would
have compiled, looked correct, and done **nothing** — the serialized value wins.
Both sites are changed, and the scene builder now carries a comment saying the
assignment is load-bearing rather than redundant. This project has already lost a
debugging round to exactly this, and the tell is an identical number across two
builds that should differ.

---

## G. STAGE G — coloured lights, and where the real cap is

**Three new materials** — `TorchBlue` (15), `TorchGreen` (16), `TorchPurple` (17),
additive, nothing renumbered, absent from `ContentVersionHash()` for the same
reason `Torch` is. They are on the hotbar as `8` / `9` / `0`.

They differ from the warm torch in **tint only** — same emissive strength, same
range, same flags — and `ColouredTorches_AreLightsThatDifferFromTheWarmTorchInTintAlone`
asserts exactly that, because the measurement below attributes differences to the
colour path and that attribution is invalid if anything else varies too.

**No channel is at zero**, deliberately: a saturated primary like `(0,0,1)`
multiplies two of a surface's albedo channels to nothing and flattens lit rock to
a silhouette. The test pins that too.

### G.1 The scaling measurement

512 mixed-colour torches on a lattice in an open hall, placed cumulatively, four
tints cycling so the selected set is never trivially identical frame to frame.

| placed | registered | **active** | mean frame | vs baseline | uploads/frame |
|---|---|---|---|---|---|
| 0 | 0 | 0 | 4.97 ms | — | — |
| 32 | 32 | 32 | 5.69 ms | +0.73 | **0.00** |
| 64 | 64 | 32 | 5.78 ms | +0.81 | **0.00** |
| 128 | 128 | 32 | 5.76 ms | +0.79 | **0.00** |
| 256 | 256 | 32 | 5.79 ms | +0.82 | **0.00** |
| 512 | 512 | 32 | 5.86 ms | +0.89 | **0.00** |

**Going from 32 to 512 placed lights costs 0.17 ms.** Not 16× more; 0.17 ms.

Two separate things are working, and it is worth naming both:

1. **§3.8's cap does its job.** The shader always shades exactly 32 lights. The
   other 480 cost only their per-frame distance score and a partial selection —
   the O(n × 32) pass that replaced §A's O(n²) insertion sort.
2. **The §A upload skip holds at scale.** `uploads/frame` reads **0.00** at every
   count including 512, measured over an explicit 120-frame window. The
   byte-comparison that decides whether to re-`SetData` is not degrading as n
   grows, which was the specific worry §G asked about. It is comparing the
   selected 32, not the registered 512, so it does not grow with n at all.

### G.2 The recommended cap, with the number behind it

**Recommendation: do not impose a placement cap below 512.** There is no
measured reason for one. If you want a number to put in a design document,
**512 placed lights costs 0.89 ms over an unlit scene and 0.17 ms over 32** — and
`MAX_ACTIVE_LIGHTS` (32) already bounds everything the GPU does, so the cap that
matters is already in place and already enforced.

**What a player will actually notice long before frame time is the §3.8
degradation itself:** past 32, the furthest lights stop being drawn. In a lit
base that reads as torches going out behind you as you walk. The F1 overlay says
`CAP BINDING — n furthest not drawn` when it happens, which is the honest fix for
it; raising `MAX_ACTIVE_LIGHTS` is a shader cost and a separate decision.

**Caveat, stated plainly:** this is a same-session, back-to-back diagnostic, not
a cooled gate. It is trustworthy for the *shape* (flat, not linear in n) because
the effect is a factor of ~30 smaller than the thing it would need to hide.

---

## H. STAGE H — lava as a light: the cost is now fine, and that is not the problem

**§A's premise is genuinely obsolete.** The exclusion was added when 12,000 lava
voxels went into an O(n²) insertion sort and cost tens of milliseconds. Measured
again against a **9,315-voxel lava lake** with the current O(n × 32) selection:

| | mean frame |
|---|---|
| lava NOT a light (shipping) | 4.39 ms |
| lava IS a light | 5.38 ms |
| **delta** | **+0.99 ms** for 9,315 registered sources |

That is **0.106 µs per lava voxel per frame** — affordable. If the decision were
purely "can we afford it", the answer would now be yes.

### H.1 STOPPING HERE, as instructed — because it is not a cost problem

**Turning the seam on naively produces a worse picture, not a better one.** Both
arms were shot from the same eye, at the same wall beside the same lake:

- `H1_03_wall_beside_lava_UNLIT.png` — **the wall is pure black.** This is what
  ships today, and it is the flagged consequence §10 item 9 describes.
- `H1_02_wall_beside_lava_LIT.png` — **the wall is blown out to near-white**, flat,
  with the voxel relief almost entirely gone.

Neither is a lava-lit cave. The reason is structural, not a tuning error:

1. **32 point lights all land in the same square metre.** §3.8 selects the
   *nearest* 32 sources; with 9,315 candidates in one lake they are 32 adjacent
   voxels, effectively one light counted 32 times. C.11's `1/(1+d²k)` attenuation
   then sums 32 nearly-identical near-field contributions and saturates.
2. **It starves every other light in the world.** The cap is global. A lava lake
   anywhere near the player permanently occupies all 32 slots, so **the player's
   own torches stop being drawn**. That is a gameplay regression, not a visual one.

**This is the "real design work" case §H said to stop at, so it stops here.**

### H.2 Three options, costed, for the owner to choose between

**Option 1 — leave it excluded (ships today, zero work).** Lava self-lights and
does not illuminate neighbours. §6.5 never promised it did: *"no propagation, no
BFS… light does not bend around corners in v1"*. Cost: 0. The black wall stays.

**Option 2 — one aggregate light per fluid tile.** The substrate already exists:
`FluidTileMap` has 32-voxel (3.2 m) tiles, tracks which are resident, exposes
`ResidentTileCoords()`, and already keeps a live-cell count per tile in
`_slotLive`. A 9,315-voxel lake spans roughly **3 tiles**, so this replaces ~9,315
point lights with **~3** — it fixes the cost *and* both problems above, because
three well-separated lights neither saturate nor monopolise the cap.
**Estimated work: moderate.** Needs a per-tile *lava-specific* count (the existing
count is all fluid), a light rebuilt when a tile's count changes, and a decision
about intensity-vs-volume. It is a new §3.8 producer, so it wants its own tests
and a checkpoint. **This is the option I would build**, but it is not an overnight
change and I have not built it.

**Option 3 — raise `MAX_ACTIVE_LIGHTS`.** Does not help. It makes the saturation
worse, costs shader time on every pixel, and leaves the starvation problem intact.
Listed because it is the obvious first idea and it is wrong.

**What is shipped tonight:** the exclusion stays on (Option 1, unchanged default),
with `LightSourceBuffer.AllowMobileEmissives` as a measured, tested seam so this
can be re-opened without re-deriving any of the above. `MobileEmissives_RegisterOnlyWhenTheStageHSeamIsOpened`
asserts the default stays closed *and* that the seam genuinely opens — because a
seam that silently does nothing would have made the +0.99 ms above a measurement
of nothing at all.

---

## J. STAGE J — the review package, and the thing underneath the presets

### J.1 The root cause of "not a fan of any of them"

**There was no tonemapping anywhere in this renderer.** Not a bad curve — none.
The final combine ended in a bare `saturate()`, so every value over 1.0 clipped
flat. §6.1 explicitly defers *"palette/mood/post… to Phases 2 and 7"*, so it was
in scope and unbuilt rather than out of scope.

Every preset was therefore being judged through a hard clamp, which is why §C
found the snow problem was **arithmetic** (1.149 at noon) and could not be fixed
by lowering ambient — the sun term alone was already at the ceiling.

Implemented: `ApplyTonemap` with **Reinhard (extended, white point)** and **ACES
(Narkowicz fit)**, plus exposure, behind `LightingConfig.tonemap` / `.exposure` /
`.tonemapWhite`, hot-reloadable. **`tonemap: 0` reproduces the old hard clamp
exactly**, so every historical screenshot in this repo remains reproducible.

### J.2 The package

`Screenshots/Presets/` — **90 images**, five presets × nine fixed scenes, plus a
three-way tonemap comparison on the three scenes where it changes anything. Each
preset folder has an `INDEX.md` naming what to look at in each shot and listing
the preset's own constants; `Screenshots/Presets/INDEX.md` indexes the set.

The nine scenes are identical across presets by construction — same eye, same
target, same clock, same cave, same torch — because a preset comparison in which
the camera moved is not a comparison.

`Screenshots/D3_swim/` — the five-frame lateral pan at 200 m, with an `INDEX.md`
explaining the stable-vs-swimming distinction and how to flip between them.

### J.3 No favourite, and two honest observations

**I am not picking a preset and this document does not contain a ranking.** What
I will report is what I saw looking at the frames:

1. **Tonemapping is a real but modest change at these settings.** Comparing
   `5_snow_noon_tmNone` against `tmACES`: ACES keeps slightly more tonal
   separation in the snow and holds the grass colour better, but snow at noon is
   still close to white in both. The clamp was **not** the whole of the problem —
   it was a ceiling the presets were fighting, and removing it gives them room to
   be tuned rather than fixing the look by itself.
2. **The thing that most affects how these read is not the tone curve — it is
   that vertical voxel faces go very dark under a high sun.** At noon the snow
   shows as heavy dark contour banding following every terrain step, because
   near-vertical faces receive almost no direct sun and the ambient floor is low.
   At golden hour the same terrain reads far better: the low sun catches those
   faces and the crater rim in `2_golden_surface.png` is the best-looking frame
   in the set. **This is worth your eye before any preset choice**, because if it
   bothers you the lever is the ambient/hemisphere term, not the preset palette,
   and no amount of preset-picking will address it.

---

## K. STAGE K — THE 81 vs 30 FPS GAP, RESOLVED. The owner's number is the right one.

**The owner sees ~30 FPS at golden hour. This document said 81 FPS.** That 2.7×
gap is resolved below, and the conclusion is unambiguous: **every frame-rate
headline in this document measured a scene no player is ever in.** The owner's
number is correct and the document's was not.

### K.1 The sustained-duration probe — and the obvious hypothesis, DISPROVED

Every prior measurement in this project is ~30 s of capture after a 300 s
cooldown. The obvious suspicion was that this reports the first 30 seconds of a
cold fanless machine and hides a thermal decline.

`-sustained` tests that directly: **one continuous 20-minute run, no cooldown
inside it**, mean frame time in rolling 30 s windows. Golden hour pinned, rung 0,
live fluid, HUD on, 960×540 gate, release standalone, fullscreen. Raw data:
`Measurements/Sustained/K1_goldenhour_20min.tsv`.

| | |
|---|---|
| **first 30 s window** (straight off a 300 s cooldown) | **26.00 ms = 38.5 FPS** |
| steady state, minutes 5–20 | ~24.6 ms = 40.6 FPS |
| worst window (t+15.0 min) | 31.10 ms = 32.2 FPS |
| **drift across 20 continuous minutes** | **−1.31 ms (−5.0%) — it got FASTER** |

**Thermal is not the cause, and that is measured, not assumed.** `pmset -g therm`
sampled before, every 2 min during, and after — 12 samples, all
`No thermal warning level has been recorded`
(`Measurements/Sustained/K1_thermal_pmset.log`). The curve says it independently:
20 minutes of sustained load made the machine 5% *faster*.

**THE THERMAL HYPOTHESIS IS DISPROVED.** It was the obvious explanation and it is
wrong. Recorded as a disproved hypothesis rather than dropped.

**And the real clue is in the first row:** the probe does not start at 81 FPS and
decay. **It starts at 38.5 FPS on a just-cooled machine.** Duration was never the
variable. The 81 FPS arms and this 38.5 FPS arm were measuring *different scenes*.

### K.2 Isolation: not fluid, not the HUD, not shadows

Six 5-minute arms, same binary, back to back (K1 having established that drift
over 20 minutes is −5%, so cooling between arms would add variance, not remove it):

| arm | steady mean | vs baseline |
|---|---|---|
| baseline | 25.61 ms | — |
| baseline repeat | 27.04 ms | +1.43 (the repeat spread) |
| no fluid | 25.31 ms | −0.30 |
| no HUD | 24.59 ms | −1.02 |
| shadows OFF (rung 3) | ~27 ms over its first 3 min | ~0 |
| **camera not moving** | **7.66 ms** | **−17.95** |

Fluid, the HUD and the shadow ray are all inside the repeat spread. **Camera
motion is worth 18 ms — more than everything else in the frame put together.**

### K.3 Decomposing "camera motion" — and the methodology flaw it exposes

Four motions, identical starting pose, varying only what changes per frame:

| motion | what it is | steady mean |
|---|---|---|
| `static` | camera held still | 7.35–9.11 ms |
| `spin` | rotate in place, no translation | 7.26–9.32 ms |
| `spin` repeat | — | 7.26–7.80 ms |
| `straight` | translate on a fixed heading — **what every prior probe did** | 9.58 ms, then **3.10 ms** |
| **`orbit`** | translate, staying over terrain | **26.6–31.4 ms** |

**Rotation is free.** Spin and static are indistinguishable across two arms.

**And `straight` is the smoking gun.** It reads 9.58 ms in its first window and
then collapses to **3.10 ms (322 FPS)** and stays there. At 12 m/s a 5-minute
straight-line flight covers 3.6 km — **the camera leaves the island and spends the
rest of the run rendering empty sky.** That is the exact motion `-stutterprobe`
and `-lightingab` used for every cooled arm in §F.4 and §F.6.

### K.4 What the camera was actually looking at — the whole answer, in two images

The numbers above say two poses 60 m apart differ by 3.6×, which no amount of
streaming explains. So the probe was made to photograph its own pose instead of
reasoning about it. `Screenshots/StageK/`:

- **`pose_static_IN_THE_PIT.png`** — the `static`/`spin` pose. `TeleportToArena`
  puts the camera at eye height **inside the Playground's dug-out arena**, facing
  a near wall. **Most of the frame is occluded black.** Rays terminate within a
  few voxels. That is why it costs 7.7 ms, and it is not a scene, it is a hole.
- **`pose_orbit_OPEN_GROUND.png`** — the `orbit` pose. Standing on open ground at
  eye level, looking across the landscape to the horizon, hills visible at range.
  **This is what a player looks at essentially all the time.** 26.6–31.4 ms.

### K.5 The reconciliation, stated plainly

**There is no measurement error and no thermal effect. Every fast number in this
document was taken either inside a pit or over empty ocean.**

| scene | frame time | FPS |
|---|---|---|
| inside the arena pit (`static`, `spin`) | 7.3–9.3 ms | 110–137 |
| off the island over empty sky (`straight`, after ~30 s) | 3.1 ms | 322 |
| **standing on open ground looking at the landscape** | **26.6–31.4 ms** | **32–38** |
| prior cooled golden-hour arms (§F.4/§F.6) | 10.6–12.3 ms | 81–94 |

The prior arms sit between the pit and open ground because a straight-line flight
**starts in the pit and exits across open terrain and then off the island** — their
number is an average over a traverse that is mostly not the scene of interest.

**THE HONEST GOLDEN-HOUR NUMBER FOR A PLAYER ON OPEN GROUND IS ~27 ms = ~37 FPS.**
That is the number this project should use from here, and it matches what the
owner reports to within the noise.

**What survives, and what does not.** The cooled arms compared like with like, so
their *differences* remain valid — shadows cost 1.9 ms, 512 lights cost 0.17 ms,
the clamp lever does nothing. Those were always relative measurements and they
are unaffected. What does not survive is every **absolute** figure and every
"fits the 16.67 ms budget" conclusion built on one, **including §F.6's
recommendation to ship rung 0 on the grounds that it had 5.9 ms of headroom.**
There is no headroom. §10 flag 4 is reopened on this basis.

### K.6 The rig defect this exposes, which is bigger than Phase 7

`TeleportToArena` is where **every** Playground-based probe starts, and it starts
inside a hole. `-movespeed` then flies out of the world. This is not a Phase 7
bug — it is the measurement substrate this project has used since Phase 5a, and
it means **any Playground-derived absolute frame time in any document here is
suspect until re-measured at a representative pose.** `-sustained -motion orbit`
is the pose-and-motion pair that is representative; it should replace the
straight-line flight in the probes that feed budget claims.

**This is flagged, not fixed.** Rewriting the historical rigs would invalidate
comparisons in `PHASE_4_COMPLETION.md` and `FLUID_*_RESULTS.md` that are load
bearing for decisions already taken, and `CLAUDE.md` reserves that call for the
owner — the same reasoning already recorded for the windowed-capture defect.

---

## L. STAGE L — the real worst case, and what the 27 ms is actually made of

All arms below are at **the open-ground orbit pose §K.4 established as
representative**, golden hour pinned, live fluid, release standalone, fullscreen,
sustained windows. Raw data in `Measurements/Sustained/`.

### L.1 A defect in my own rig, disclosed because it changed a conclusion

§K.2's "shadows OFF" arm was **invalid**. My runner passed a default
`-shadowlevel 0` before the per-arm `"$@"`, so `-shadowlevel 3` arrived second and
`ArgInt` takes the first match — **that arm ran with shadows ON and I briefly read
it as evidence that shadows cost nothing.** Fixed, re-run, and the corrected
result is the largest finding in this session. Recorded rather than quietly
replaced: it is the same class of error as §D.0's missing capture-mode flag, and
the lesson is the same — a flag that is silently ignored produces a confident
number for a condition that never ran.

### L.2 What the frame is made of at the representative pose

| arm | mean frame | FPS |
|---|---|---|
| rung 0, gate 960 (baseline) | 28.5 ms | 35 |
| baseline repeat | 29.2–33.1 ms | 30–34 |
| **shadows OFF (rung 3)** | **16.3 ms** | **61** |
| fluid OFF | 26.5–28.0 ms | 36–38 |

**The sun-shadow ray costs ~12 ms at this pose** — not the 1.9 ms §F.6 measured in
the pit. §6.5 predicted exactly this: at 6° elevation the rays graze for hundreds
of metres, and on open ground **nearly every pixel is a sunlit surface that needs
one**, where in the arena almost nothing is sunlit so almost no ray is traced.

**Fluid costs ~1 ms here** and is not a factor.

### L.3 The gate-resolution sweep — the frame is pixel-bound

| gate | pixels | mean frame | FPS |
|---|---|---|---|
| 480×270 | 129,600 | 13.9 ms | 72 |
| 672×378 | 254,016 | 20.6 ms | 49 |
| **960×540 (shipped)** | **518,400** | **28.5 ms** | **35** |
| 1280×720 | 921,600 | 39.5 ms | 25 |

Frame time tracks gate pixels closely across a 7× range. **This is a GPU
ray-throughput problem, not a CPU or streaming one** — which is consistent with
§K.2 (fluid, HUD and streaming all measured as noise) and with §K.3 (rotation,
which changes what is on screen but not how many pixels there are, is free).

### L.4 STAGE L1 — golden hour + full CHAOS BURST, sustained

8 minutes continuous, burst fired at t+60 s (~51,000 voxels of water/sand/lava).
Raw: `Measurements/Sustained/L1_chaos.tsv`.

| phase | mean frame | FPS |
|---|---|---|
| before the burst (t 0–1 min) | 26.1–26.4 ms | 38 |
| during placement and settling (t 1.5–4 min) | 27.5 → **30.5 ms** | 37 → **32.8** |
| after settling (t 4.5–7.5 min) | 25.5–25.7 ms | 39 |

**THE WORST SUSTAINED NUMBER IN THE SYSTEM IS 30.46 ms = 32.8 FPS**, at golden
hour with full shadows and a chaos burst settling. Reported as the headline worst
case, not averaged against anything.

**The burst itself costs ~4 ms — about 15% — and recovers completely.** That is a
good result and it is worth saying plainly: **the fluid system is not the problem
here.** §7.2's tiled substrate absorbs 51,000 voxels for 4 ms. The 26 ms
underneath it is the problem, and it is there before the burst starts.

### L.5 "Fluid moves slower than normal" — CONNECTED, and here is the code path

**The fluid CA advances exactly one tick per rendered frame.** `Playground.Update()`
(line 771) calls `_fluid.Tick(_clipmap)` (line 842) once per frame, gated only by
readback availability. There is **no fixed-timestep accumulator anywhere in that
path** — no `FixedUpdate`, no `Time.deltaTime` scaling of the CA, no catch-up loop.

**Therefore fluid simulation speed is directly proportional to frame rate.** At
33 FPS fluid advances at 55% of the rate it does at 60 FPS. The "fluid moves like
syrup" report is **not a separate defect** — it is an inevitable, mechanical
consequence of the frame-rate finding above, and fixing the frame rate fixes it.

**This is reported as a connection, not filed as a fluid ticket.** It does mean
that if the owner ever caps or stabilises the frame rate, fluid speed will change
with it — decoupling the CA from the render frame rate is a real design question
(§7.3), and it is **flagged, not built**: it changes simulation determinism, which
`PHASE_5A_COMPLETION`'s conservation tests depend on.

### L.6 §2.2's fallback ladder does not engage — because it does not exist

§2.2 specifies, for the case where the GPU lane does not fit: shrink the fluid
active radius → two-speed fluid tick → shadow fallback rungs.

**Checked in code rather than assumed. None of it is a runtime mechanism.**
`ActiveRadiusVoxels` is a settable property that is only ever assigned
`EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS`; there is no two-speed tick; there is
no code anywhere that reads a frame time and changes a quality setting. Grep for
adaptive/degrade/budget-exceeded logic returns nothing but profiling readouts.

**This is not a bug.** §2.2 words it as what to do *"if the GPU lane doesn't
fit"* — a development decision, not an automatic ladder, and the shadow rungs it
names are indeed built and switchable. **But it does mean there is no safety net:**
nothing in the shipping build notices that the frame is at 30 ms and gives
anything up. Whether there should be is an architecture decision and is left in
§N as an option with a cost.

---

## M. STAGE M — snow: the root cause is CONTRAST, and two sessions optimised the wrong variable

### M.1 What the previous two sessions got wrong

Both prior sessions treated *"snow is blindingly bright"* as a **peak brightness**
problem. §C derived a ceiling (`SnowNoonPeak`), bounded every preset under it, and
that arithmetic was correct. **The complaint survived anyway**, and §J.3 noticed
why while looking at the frames: at noon the snow carries heavy **dark contour
banding** following every terrain step.

Those bands are the **vertical faces of the voxel staircase**, and they are not
slightly darker than the horizontal ones.

At noon the sun sits at ~76°, so for a vertical face `N·L` is at most ~0.24 and is
**exactly zero** for every face pointing away from the sun. Such a face is lit by
ambient alone — and in C.9 ambient is a single **non-directional** term. Computed
from the shipped config (`LightingPresets.SnowNoonUpFace` / `SnowNoonVerticalFace`,
both now test-covered):

| config | up face | vertical face | **raw ratio** | displayed ratio (ACES) |
|---|---|---|---|---|
| **Shipped** (ambient 0.30) | 1.149 *(clipped)* | 0.155 | **7.4 : 1** | 3.7 : 1 |
| **Atmospheric** (0.22) | 0.944 | 0.113 | **8.3 : 1** | 5.3 : 1 |
| **Warm** (0.11) | 0.947 | 0.057 | **16.7 : 1** | 14.7 : 1 |

**Every preset that lowered ambient to bound the peak made the contrast
dramatically worse.** Warm — the preset §C described as "the direct answer to §8"
— more than doubles it. A surface that is simultaneously clipped white and
near-black reads as *blinding* no matter what its peak value is, because the eye
judges brightness by contrast. **The presets traded a problem the owner could name
for a bigger one they could only feel.**

`SnowNoon_LoweringAmbientToFixPeakMakesFaceContrastWorse` pins this.

### M.2 The lever — which did not exist before Stage J

Raising ambient lifts the vertical faces. Before Stage J that was impossible: the
renderer ended in a bare `saturate()`, the up-face was *already* clipped, and
adding ambient only clipped it harder. **With ACES in the pipeline the top face is
on the flat part of the curve, so it barely moves while the dark faces lift.**

| ambient | up face → displayed | vertical face → displayed | displayed ratio |
|---|---|---|---|
| 0.30 (shipped) | 1.149 → **0.832** | 0.155 → 0.224 | 3.7 : 1 |
| 0.45 | 1.248 → 0.847 | 0.232 → 0.349 | 2.4 : 1 |
| **0.60** | 1.348 → **0.860** | 0.309 → 0.450 | **1.9 : 1** |

**Doubling ambient moves the displayed top face by 3.4% and nearly halves the
contrast.** `RaisingAmbientUnderACES_CompressesFaceContrastWithoutBrighteningTheTopFace`
asserts exactly this, with a mutation guard proving it is the tone curve doing the
work and not the ratio arithmetic.

**This is offered as the lever, NOT applied.** Ambient intensity is an art
constant and §10 flag 5 reserves those for the owner. It is one number in
`LightingConfig.json` and reloads live: set `ambientIntensity` to 0.45–0.60 and
look at snow at noon.

### M.3 One genuine implementation deviation from C.9, flagged not patched

C.9 specifies `albedo × (sun + points + ambient)`. The hemisphere term is applied
**to the albedo**, before that multiply:

```
shaded  = albedo * grain
shaded *= hemi          // 0.62 down .. 1.12 up
shaded *= AO
shaded  = shaded * (sunTerm + pointTerm + ambientTerm)
```

So the hemisphere tint **also scales the direct sun term** — a face angled away
from the sky has its *sunlight* reduced by up to 38% even when the sun is shining
straight at it. The shader's own comment says hemi "is C.9's ambientSky given a
direction", and if that is what it is, it belongs on the ambient term only.

**This is a real deviation and it is small.** Moving it would slightly brighten
directly-lit angled faces — helpful at golden hour, marginal at noon — and it
changes every existing screenshot. **Not changed**: it is a spec-conformance
question about a term §6.5 deliberately "demoted, not deleted", the visual effect
is minor next to §M.2's lever, and §0.1 invariant 5 says one new untrusted thing
at a time.

### M.4 The honest answer on "Layer of the Land" — lighting alone will NOT get there

**My opinion, asked for directly, given plainly: no. Further tuning of lighting
constants will not close this gap, and a third pass at it would waste the
owner's time.** What is already present is more than the look is short of:

- **Aerial perspective exists** — linear distance fog toward the sky colour,
  45 m → 260 m, up to 85% (`_ShadeFog`). The distant hills in
  `Screenshots/StageK/pose_orbit_OPEN_GROUND.png` do fade correctly.
- **A tone curve now exists** (Stage J).
- Hemisphere tint, voxel AO, per-voxel grain, real sun, shadows and point lights
  all exist.

**What is missing is not in the lighting system.** Looking at that same
screenshot: the landscape is large contiguous areas of **one flat colour per
material**, with nine terrain materials total and a ±9% per-voxel grain as the
only within-surface variation. Layer of the Land's visual language leans on
**dense small-scale detail and colour variation within a surface** — scattered
vegetation, rocks, grass tufts, and several tones inside what reads as "grass".
No lighting model applied to nine flat colours produces that.

**Named specifically, in the order I would rank them:**

1. **Within-material colour variation** (low-frequency noise over the palette, or
   2–3 tonal variants per material chosen by hash). **Small-to-medium work** — it
   is a shader change plus palette authoring, no new systems, no new buffers.
   This is the highest ratio of look-per-effort available and it is the one I
   would try first.
2. **Scattered detail content** — grass, rocks, plants as actual voxels.
   **Phase-sized.** It needs generation rules, density by biome, and it interacts
   with streaming cost. This is the big one and it is genuinely the owner's
   forthcoming content/gameplay work, not a lighting task.
3. **§M.2's ambient raise**, to stop shadowed faces crushing to black. **One
   number.**

**Do not read this as "the renderer is wrong".** It is doing what §6.5 and C.9
specify. The gap is content density and palette variation, which is a different
system that has not been built yet — and saying so is more useful than a third
round of constant-tuning that was never going to land.

---

# N. VIABILITY ASSESSMENT

**The owner asked directly: is a real, sustained, comfortable frame rate
achievable on this hardware, or is this project's ceiling too low to keep
investing in? The answer is YES, it is achievable — but not at the internal
resolution currently shipped, with shadows on, at golden hour.** The evidence,
and the cost of each option, is below. Nothing here is softened.

## N.1 What Stage K established as the REAL sustained frame rate

**~27 ms = ~37 FPS**, golden hour, open ground, full shadows, shipped 960×540
gate, live fluid, sustained. This matches the owner's report to within noise.

**The gap to the old 81 FPS headline is NOT thermal and NOT a measurement error.
It is that every prior number was taken in a scene no player is in** — either
inside the Playground's dug-out arena (`TeleportToArena` starts the camera in a
hole facing a wall: 7.7 ms) or, after 30 s of straight-line flight at 12 m/s, off
the island over empty sky (3.1 ms). Both poses are photographed in
`Screenshots/StageK/`. Thermal was tested directly and disproved: 20 continuous
minutes made the machine **5% faster**, with `pmset -g therm` nominal across 12
samples.

## N.2 What Stage L established as the worst case

**30.46 ms = 32.8 FPS** — golden hour, full shadows, a full ~51,000-voxel chaos
burst settling, sustained (`Measurements/Sustained/L1_chaos.tsv`).

**The burst itself accounts for only ~4 ms of that and recovers completely.** The
fluid system is not the problem; §7.2's tiled substrate absorbs 51,000 voxels for
about 15%. The 26 ms underneath it is the problem, and it is there before the
burst starts.

## N.3 Which of (a) acceptable / (b) fixable / (c) architecture ceiling

**It is (b) — fixable, and the fix is a spec-sanctioned setting, not new
engineering.** But that verdict comes with two hard qualifications, and the first
one is the important one.

### The decisive evidence: 60 FPS is already reachable, at a cost

Golden hour, open ground, **full soft-penumbra shadows**, sustained:

| internal gate | mean frame | FPS | verdict |
|---|---|---|---|
| 960×540 *(shipped)* | 26.5 ms | 37.7 | **misses** |
| 672×378 | 20.2 ms | 49.5 | misses |
| **560×315** | **16.36 ms** | **61.1** | **holds 60** |
| 480×270 | ~15.1 ms | 66 | holds 60 |

**560×315 holds 60 FPS with everything on**, in three consecutive windows of
16.50 / 16.33 / 16.26 ms — the tightest arm in this session. Amendment 8.9 is
literally *"LOD & resolution"*; internal render scale is the spec's own lever for
exactly this situation.

**And half the day already fits at the shipped gate.** Same pose, same shadows:

| time of day | mean frame | FPS |
|---|---|---|
| midnight | 13.5–16.6 ms | 60–74 |
| noon | ~17.1 ms | 58 |
| **golden hour** | **26.5 ms** | **37.7** |

Golden hour is the worst case, exactly as §6.5 predicted. **The problem is not
the engine's general throughput — it is a ~10 ms spike confined to low-sun hours.**

### Qualification 1: §6.5's fallback ladder does NOT work as a budget mechanism

This is an architecture-level finding and it should not be buried. At the
representative pose, gate 960:

| rung | what it is | mean frame |
|---|---|---|
| 0 | full soft penumbra | 25.9 ms |
| 1 | half-res beyond LOD1 | 28.1 ms — **worse than rung 0** |
| 2 | hard edges only | 25.1 ms — **saves 0.8 ms** |
| 3 | **off** | **16.5 ms** |

**Rungs 0, 1 and 2 are within 3 ms of each other. Only not tracing the ray at all
helps.** The cost is the *traversal*, not the penumbra — so a ladder built out of
"cheaper shadows" cannot recover the budget, because every rung except the last
still traces the ray. §6.5 designed the rungs as the graceful-degradation path;
measured, they are not one. That is worth knowing before any future phase relies
on them.

The shadow-distance clamp was also re-tested **at the correct pose** (§F.4 had
"disproved" it in the pit, where shadow rays barely run): it is real here but
small — 2.5 ms at 64 m, 4.3 ms at a visually destructive 16 m. Rung 2 plus a 64 m
clamp together reach 23.2 ms ≈ 43 FPS. **Still not 60.** Resolution is the only
lever that gets there with shadows on.

### Qualification 2: §2.2's budget is genuinely exceeded, and by a lot

§2.2 budgets **≤9.0 ms** for raymarch including fused shadow rays and **≤13.0 ms**
for the whole GPU lane. Measured at the shipped gate on open ground at golden
hour: **~26 ms**, of which the shadow ray is ~9–12 ms on its own. That is **~2×
the entire GPU-lane budget**. The budget was written before any of this existed
and was explicitly flagged as unproven; it is now measured, and it does not hold
at 960×540 with shadows at low sun.

**At 560×315 it does hold** (16.36 ms total frame, inside the 16.6 ms lane target).

## N.4 What I am NOT claiming

- I have **not** measured CPU-lane headroom directly. The evidence that this is
  GPU-bound is strong and indirect: frame time tracks gate pixels across a 7×
  range, while fluid, the HUD, streaming and camera rotation all measure as noise.
  If that reading is right, the owner's planned gameplay/NPC/village systems land
  in the lane that is **not** saturated — but that is an inference from the
  pixel-scaling evidence, not a measurement, and it should be measured before it
  is relied on.
- These numbers are one pose on one machine. It is a *representative* pose, chosen
  and photographed for that reason, but it is not a distribution over play.
- Nothing here says the renderer is incorrect. It is doing what §6.5 and C.9
  specify, with the air-mip and §6.5 8.2's LOD tier selection both confirmed
  active inside the shadow ray — the acceleration is not missing, the work is just
  genuinely expensive at grazing sun angles.

## N.5 The options, with costs. These are the owner's call, not mine.

**Option 1 — lower the internal gate to 560×315.** Holds 60 FPS with everything
on, all day. **Cost: image sharpness.** 1920/560 is a 3.4× linear upscale against
the current 2×. Voxel art upscales more gracefully than most content — large flat
faces, hard silhouettes — but this is a real and visible trade and it is the
owner's to judge by looking, not mine to assert. **Zero engineering; one number.**

**Option 2 — dynamic resolution: full gate when the frame is cheap, drop it at low
sun.** Keeps 960×540 for the ~16 hours a day that already fit and only degrades
through golden hour. **Cost: small, well-understood engineering** (a resolution
controller with hysteresis), plus the risk that a visible resolution change reads
worse than a constant lower one. This is the §2.2 fallback ladder the spec
intended, built for the lever that actually works (resolution) instead of the one
that does not (shadow rungs).

**Option 3 — accept 37 FPS at golden hour, 60+ the rest of the day.** **Cost:
the owner's stated complaint remains**, for a couple of in-game hours per cycle.
Free.

**Option 4 — shadows off at low sun only.** 16.5 ms at the full 960 gate. **Cost:
shadows disappear exactly when they are most dramatic**, which is the worst
possible time to lose them.

**Option 5 — reduce max render distance at low sun.** Not measured. The shadow
cost is driven by grazing rays over long distances, and `LODConfig.TIER_OUTER_RANGE_M`
(290 m) bounds them. §F.4's clamp test only limited *shadow* rays; limiting the
primary ray too would cut both. **Untested — I am naming it as the one remaining
unexplored lever, not claiming it works.**

## N.6 The honest bottom line

**This project's performance ceiling is not too low, and the architecture is not
the problem.** The engine sustains 60+ FPS with full shadows, live fluid and a
51,000-voxel chaos burst — at 560×315 internal, or at 960×540 for most of the day.
What it cannot currently do is 960×540 with soft shadows at a 6° sun.

**The single most important correction this session makes is not a number, it is a
method:** this project has been measuring itself in a hole. Every absolute frame
time in every document here predates that discovery. The differences those
measurements report are still good; the absolutes are not. **`-sustained -motion
orbit` is the pose and motion that should feed any future budget claim**, and
§K.6 flags — without changing — that the historical rigs share the defect.

**Recommended next session, concretely:** measure Option 5 (primary-ray render
distance at low sun) and the CPU-lane headroom in §N.4, both at the representative
pose. Those are the two things that would change this assessment, and neither is
guesswork — they are one flag and one instrument away.

---

# O. GETTING BEAUTIFUL SHADOWS *AND* THE AESTHETIC AT 60 FPS

Asked directly: can this engine have good shadows and the target look, at 60 FPS?
**Measured answer: yes — at 560×315 internal, with everything on.** What follows
is what was tried, what worked, and what was disproved.

All arms: open-ground orbit pose (§K.4), golden hour pinned, live fluid, moving
camera, release standalone, fullscreen, sustained 30 s windows.

## O.1 A measurement hazard that voided two runs, recorded so it is not repeated

**Two full sweeps in this session had to be thrown away** because background work
appeared mid-run: a **Time Machine backup** (triggered, almost certainly, by the
`Measurements/` and `Screenshots/` files committed minutes earlier) and later a
**Shortcuts automation at 96% CPU**. Both inflated every arm by 30–100%, and the
tell was the control arm — a condition already known to measure ~26 ms reading
46 ms and 85 ms.

Fixed by gating the runner on `uptime` load average < 2.5 before starting, and by
always including a reproduce-the-known-baseline arm. **Neither voided run is
reported as a result.** If a future session sees arms drift upward together while
a control arm also moves, check `ps -Ao pcpu,comm -r | head` before believing
anything.

## O.2 DISPROVED: coarsening shadow LOD by ray travel

**The hypothesis, and it was a good one.** §6.5's 8.2 rule pins shadow LOD to
**camera** distance to the sample. At a 6° sun the shadow ray travels almost
horizontally *along* the surface, so its samples stay near the camera and it
marches **per voxel** through dense bricks — calling `ShadowClearance` (up to six
clipmap reads) at every step — for a hundred metres. §6.5's own cost note assumes
the opposite: *"mostly through uniform-Air leaps at coarse LOD."* **That
assumption does not hold at the pose the gate is measured at.**

The proposed fix was `tier = max(tierByCamera, tierByTravel)`. `max()` is what
makes it safe — it can only coarsen, never refine, so the shadow is never
evaluated finer than the camera draws that geometry, which is exactly the
condition the retracted 8.1 rule violated and 8.2 exists to guarantee. The
threshold is derived rather than tuned: C.10 already blurs the edge by
`smoothstep(0, kSoft, m)`, so the penumbra half-width at travel `t` is ~`kSoft·t`
voxels, and detail finer than that is already being blurred away.

**It does not work.** Instrumented rather than argued, with a new `ShadowStats[4]`
step counter:

| travel LOD | rays/frame | steps/frame | **steps per ray** | frame |
|---|---|---|---|---|
| off (shipped 8.2) | 258,095 | 18.1 M | **70.2** | 27.7 ms |
| 1.0 | 258,049 | 21.0 M | **81.5** | 27.4 ms |
| 0.2 (most aggressive) | 258,045 | 27.4 M | **106.1** | 25.1 ms |

**Coarsening makes the ray take MORE steps, monotonically, and frame time does not
move.** The images are identical (`O3_shadow_travel_off` vs `_on`, same pose, same
frame) — so it is not trading quality for speed either. It is simply not a lever.

**Why, as far as the evidence goes:** step count rising with coarseness means rays
are travelling *further* before terminating, not less far. The coarse tiers cannot
be under-occluding by construction — `LODDownsampler` is majority-vote with an
explicit solid-preserving bias (air wins only on a strict 5-of-8 majority) — so the
likeliest explanation is that tier 1/2 data is not resident for the near-field
region these rays traverse, leaving them to leap through nominal "air" to the
distance clamp. **That is a hypothesis, not a conclusion** — what is established
is that the lever does not help and is not worth pursuing further.

**The code is kept, defaulted OFF** (`LightingConfig.shadowTravelLod = 0`, which
reproduces shipped 8.2 behaviour exactly), because the `ShadowStats[4]` counter it
came with is the useful part and because a disproved lever that is still switchable
is cheaper to re-test than to rebuild.

## O.3 The shadow levers, ranked by what they actually buy

Everything tried across this session and the last, at the representative pose:

| lever | cost change | verdict |
|---|---|---|
| rung 1 (half-res beyond LOD1) | **+2.2 ms (worse)** | dead |
| travel-based LOD coarsening | 0, +16% steps | **dead (§O.2)** |
| rung 2 (hard edges, no penumbra) | −0.8 ms | negligible |
| shadow distance clamp, 64 m | −2.5 ms | small, real |
| shadow distance clamp, 16 m | −4.3 ms | visually destructive |
| **internal resolution 960 → 560** | **−10.2 ms** | **the only lever that works** |

**The pattern is consistent and it is the finding:** every attempt to make the
shadow *ray* cheaper fails, because the cost is **tracing the ray at all**, and
rungs 0/1/2 plus the travel rule all still trace it. The only thing that moves the
number is **tracing fewer of them** — which is what internal resolution does, one
ray per gate pixel.

## O.4 The aesthetic levers, and what they cost

Two changes, both §M.4 items, both now implemented behind defaulted-off flags.

**1. Within-material colour variation** (`materialVariation`). The existing
`grain` is per-voxel white noise on *brightness only* — it averages to grey over
any distance or downsample, so it vanishes exactly where the flatness shows, and
it never varies hue. This adds two octaves of smooth value noise (a hash lattice,
trilinearly interpolated — pure ALU, no texture, no memory traffic) at ~6.4 m and
~1.6 m, and moves the albedo along a **warm/cool axis** as well as in value. The
original grain is kept at half strength, because it still does a different job up
close.

**Measured cost: +0.6 ms** (15.5 → 16.1 ms at gate 560).

**2. Ambient raised** (§M.2's lever, now testable rather than derived). Free.

**A note that matters for reading every screenshot in this document:** the live
`LightingConfig.json` had `ambientIntensity` **0.055** — the HighContrast preset,
left there by the Stage J preset run. That is 5× lower than the shipped default
and is why the landscape in the Stage K/L frames reads as crushed black. The
comparison below therefore covers 0.055, 0.30 (shipped) and 0.55.

`Screenshots/Aesthetics/`, all at gate 560 with full soft shadows, same pose:

| file | what |
|---|---|
| `1_golden_BASE.png` | as-found: no variation, ambient 0.055 |
| `2_golden_matvar_only.png` | variation on, ambient still 0.055 — isolates the variation |
| `3_golden_matvar_ambient030.png` | variation + shipped ambient |
| `4_golden_matvar_ambient055.png` | variation + ambient 0.55 |
| `5_noon_BASE.png` / `6_noon_matvar_ambient055.png` | the noon snow/banding case |

**My read, offered as an opinion and not a decision:** 1 → 3 is a large and
obvious change — the landscape goes from muddy olive with crushed black shadows to
a readable green terrain with visible ground variation and graded shadow. Between
3 and 4 I prefer **3** (ambient 0.30): 0.55 lifts the shadows far enough that the
terrain starts to flatten. **Both are one number in `LightingConfig.json` and both
hot-reload — this is exactly the judgement that should be made by looking, not by
reading my adjectives.**

## O.5 THE CANDIDATE CONFIG, measured properly

**gate 560×315 · rung 0 (full soft penumbra) · within-material variation ON ·
ambient 0.30 · live fluid · moving camera · golden hour · sustained.**

| arm | windows | mean | FPS |
|---|---|---|---|
| 5 minutes | 14.55 16.47 15.95 15.99 16.17 15.98 17.83 17.50 16.11 | **16.28 ms** | **61.4** |
| 3 minutes (repeat) | 15.43 17.30 17.61 16.83 16.91 | **16.81 ms** | **59.5** |

**It sits ON the 60 FPS line, not comfortably above it — 59–61 FPS.** Stated that
way deliberately: the two arms bracket 16.67 ms, so "holds 60" is accurate and
"comfortably 60+" would not be. If the owner wants real margin, gate 520 or 480
buys it (§N.3's sweep: 480 measured ~15.1 ms), at more upscale softness.

**Against the shipped 960×540 config at the same pose, this is 26.5 ms → 16.3 ms,
with the picture improved rather than degraded** — the softness of a 3.4× upscale
traded for colour variation, graded shadows and a frame that fits.

## O.6 A separate problem this uncovered: a delayed fluid spike

Both chaos-burst arms show a large spike **~2.5 minutes AFTER the burst lands**,
not while it is being placed:

| | baseline | during placement | **t+3.5 min** | settled |
|---|---|---|---|---|
| gate 560 | 16.0 ms | 17.0–19.0 ms | **66.9 ms (15 FPS)** | 17.2 ms |
| gate 960 | 28.2 ms | 27.9–29.7 ms | **37.0 ms (27 FPS)** | 29.7 ms |

**It is deterministic** — the burst seeds its RNG with a fixed value, and the spike
lands at the same point in both arms — so this is engine behaviour, not the
background contamination of §O.1. Placement itself is cheap (+1–3 ms); the
expensive moment is whatever the fluid does two and a half minutes later, most
likely the water/lava reaction front or a large residency change.

**Two things stop me characterising it further here:** the 560 arm ran with load
creeping to 3.97 so its 66.9 ms peak may be partly inflated, and this is a fluid
question rather than a lighting one. **Flagged as its own investigation**, with the
raw curves in `Measurements/Sustained/Q2_chaos.tsv` and `Q2_960_chaos.tsv`. It does
not change the shadow or aesthetic conclusions, which are measured at steady state.

## O.7 Answering the question directly

**Can this engine have beautiful shadows and the target aesthetic at 60 FPS? Yes,
and it is one config change away — but the shadows get there by tracing fewer
rays, not cheaper ones.**

- **Full soft-penumbra shadows are kept.** Not rung 2, not a clamp, not a
  degraded approximation — rung 0, the best-looking option, at every hour.
- **The aesthetic improves at the same time**, because the two levers that work
  (colour variation at +0.6 ms, ambient at 0 ms) are nearly free, and the one that
  pays for them (internal resolution) costs sharpness rather than lighting quality.
- **Every attempt to make the shadow ray itself cheaper failed** — rung 1, rung 2,
  the distance clamp and the travel-LOD rule are all measured above, and the best
  of them buys 2.5 ms against a 10 ms problem.

**What I did NOT do, and would do next:** §N.5's Option 5 — reducing the *primary*
ray's render distance at low sun — is still untested, and it is the one remaining
lever that attacks the cost at its source rather than by sampling less. It would
shorten the grazing rays that §6.5 correctly identified as the expensive case.

**Everything built this session defaults OFF** (`shadowTravelLod = 0`,
`materialVariation = 0`, ambient unchanged, gate unchanged). Nothing about the
shipped build's look or speed has changed. These are levers with numbers attached,
for the owner to pull.

---

## P. 2026-09-24 (second session): the owner's decisions 1, 8, 2–4 and 11, acted on

**P.1 Rig: Gate 1's falloff poses were inside rock (decision 1).** This was a
RIG change, not a product change. The 1.5/3/6 m poses sat outside the 1.4 m chamber,
in solid stone, so the frame was black and the luminance read 0.0000 at every
distance. Gate 1 now carves a sealed sight corridor along the measurement axis.
It checks for a ≥ 6-voxel roof over every column before carving, and adds
preconditions (a sealed corridor exists; the camera is in air at every pose).
The monotonic-falloff assertion is unchanged. Rig **PASS 32 FAIL 0** (was 27/1);
falloff 0.0690 / 0.0106 / 0.0019. Before/after: `Screenshots/Phase7RigGate1/`.
**Still open:** `04_torch_through_wall_FLAGGED.png` uses the same kind of rock pose
and is black. It's not asserted, and it's left for the owner.

**P.2 Lava lights its surroundings (decision 8, §H.2 Option 2 built).**
`LightSourceBuffer`'s LAVA AGGREGATE: a mobile emissive is counted into its
32-voxel tile (count, X/Z centroid, a per-layer histogram for the top surface),
and each tile emits **one** light. The light sits at the centroid, `lavaLightLiftM`
above the top lava face, with strength ∝ √(count / `lavaLightFullCount`), saturating.
Lava's share of the §3.8 cap is capped at `lavaLightMaxSlots` (8), so **placed
torches always keep ≥ 24 of 32**; furthest-first still holds within each class.
Four EditMode tests (aggregation/idempotence, placement/saturation, starvation,
lava-only upload).
- MEASURED (`-lavalight`): a 9,315-voxel lake → **6 tile lights, 0 point lights**.
  40 torches + lava → 26 torches + 6 lava active. The wall beside the lake goes
  from pitch black to warm and lit, with relief intact (not §H.1's blow-out).
  Shots: `Screenshots/LavaLight/`.
- MEASURED (`run-lavalight-ab.sh`, 4 min × 4, counterbalanced 8/0/0/8, live vents
  incl. lava): mean 30.40 ms with lava light vs 30.28 ms without, **+0.11 ms, inside
  the 0.23 ms spread between the two identical "on" arms, so not measurable.**
  Light-buffer uploads rise from 0 to ~2/s; fluid ticks/s unchanged (4.9–5.0).
- ALSO FOUND: §H.1's own "wide" pose (`cave + (-40, 4, 0)`) is inside rock, since the
  carved spheres reach only x = −34. Its H1_00/H1_01 frame times were measured
  looking at stone. Its **cost delta** is unaffected (both arms shared the pose),
  but its screenshots showed nothing.
- `4_torches_AND_lava_ON.png` is blown out. That's 26 torches in one cavern, the
  pre-existing many-lights-summing saturation, not the aggregate.

**P.3 The cinematic grade (decisions 2–4).** `LightingConfig.lookGrade` (1 = on).
It's an elevation ramp (smoothstep 8° → 40°) that lowers exposure on sun +
ambient (0.62 at noon; torches and lava untouched), deepens the day sky, takes
the yellow out of the noon sun, trims saturation (0.86) and adds a highlight
shoulder (knee 0.70) so snow rolls off instead of clipping. **Below 8° the grade
is exactly neutral**, so golden hour (the owner's anchor), dawn, dusk and night
are unchanged.
- MEASURED (`run-looksweep.sh`): **15 of 15 below-ramp before/after pairs are
  pixel-identical**, 0 differing pixels (dawn, golden, dusk, night, midnight × 3
  poses). Noon: snow off-white with visible grain instead of flat white; sky
  deeper; grass natural rather than neon. 33 pairs in `Screenshots/LookSweep/`
  (select-all in Preview sorts pose → time → before/after).
- LOOKED AT, flagged: at 08:00 / 16:00 (29°) the graded snow reads grey and
  slightly overcast. The candidate fix is to extend the golden warmth past 25°.
  That's the owner's call on the pictures.
- Frame cost: see P.5.

**P.4 Fluid tick period (decision 11): diagnosed, not fixed.** See
`FLUID_TICK_DIAGNOSIS.md`. One tick = readback latency (~3.6–3.8 frames, fixed in
frames) + drain (1024 ops/frame; ~3.7 frames at ~3,100 ops/tick) + a 1-frame issue
gap. Options costed; no change made.

**P.5 Grade frame cost: not measurable.** MEASURED at NOON (`-tod 12`; at the
default golden-hour pin the grade is inert and an A/B would compare the frame
with itself). `run-looksweep.sh GRADE_AB=1`, 3 min × 4, counterbalanced 1/0/0/1,
orbit, gate 960, live fluid, fullscreen. Mean of the first five 30 s windows:
grade on 17.59 / 18.42 ms, off 18.05 / 17.83 ms. That's **+0.06 ms** on average,
against a 0.83 ms spread between the two identical "on" arms. Log
`Logs/looksweep_20260924_220334.txt`. (Noon runs ~55 FPS against ~32 at golden
hour, because noon shadow rays are short.)

## 11. CARRY-FORWARD — open, and known to be open

1. **Tiers 1/2 still have no CPU oracle.** Carried from Amdt 8.10 §5.7 and
   PHASE_2 §6.2, now inherited by shadow rays as well.
2. **The A.7 `GraphicsBuffer<MaterialData>` still does not exist.** Phase 7
   created the emissive *data* (§6.6's requirement) and mirrored it into the
   shader the way the palette already is, with `PaletteSyncTests` extended to
   pin it. Building the real §3.9 registry buffer is a sync-contract addition
   that wants its own checkpoint.
3. **Shadow rung 1 has no correctness assertion AND no performance number**, and
   it is now the rung the phase most needs both for. See §10 flag 4.
3b. **`AMENDMENT_8_12` §4's matrix has NOT been run** — only a three-arm
   diagnostic. Arm A, rung 1, the combined fluid scenario, the forced 1920 gate,
   and cooled/counterbalanced/repeated runs are all outstanding. §6.1 names each.
4. **The shader's penumbra is a bounded approximation of the oracle's** (§5.3).
5. **The §6.5 / §13 ambiguities in §10 flags 1 and 3** are undecided.
6. **`run-phase6-brushguard.sh` is still 15 PASS / 15 FAIL** for the stale-rig
   reason CLAUDE.md records. Phase 7 added a 7th hotbar brush (torch) by
   **appending**, so no existing index moved and that rig's premise is unchanged.
7. **Amendment 8.11 remains DRAFT, NOT ADOPTED.** Untouched. §5.4 records the one
   place Phase 7 came near it and why that is not an implementation of it.
8. **The unattributed frame-time tail and the sustained-load stutter** are
   untouched and must not be reported as fixed. **The stutter fixed in §A is a
   DIFFERENT, NEW defect that Phase 7 introduced** — the pre-existing tail is
   still there (6.3% of frames over 16.6 ms in the pre-Phase-7 arm, 41.3 per
   1000 over 33 ms after the fix) and nothing in this session touched it.
9. **`-stutterprobe` does not pin the time of day** (§A.8), so its arms average
   over a day–night cycle. Fine for the comparisons it was used for; a one-line
   fix before it is used for anything rung-related.
10. **Rung 1's hypothesis (§E.2) is untested** — it half-rates only beyond 128 m
    and most hit pixels at the test pose are nearer. One constant.
11. **Whether distant shadows swim under lateral motion is unjudged.** Five
    comparable frames exist (`D3_swim_*`); judging drift across stills is a
    human job.

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
