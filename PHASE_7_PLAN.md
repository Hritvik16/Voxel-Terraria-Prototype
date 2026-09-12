# Phase 7 — Stage Plan

**Written after Stage 0, before any production code.** Read
`PHASE_7_READING_NOTES.md` first: its FINDINGS and CONFLICTS sections are what
this plan is built on, and three things in them change the shape of the work
(no `MAX_ACTIVE_LIGHTS`, no A.7 registry buffer at all, no `SelectLODTier`
function despite §6.5 writing the rule as if one existed).

**Baseline:** EditMode `PASS 499  FAIL 0  SKIP 0`, 2026-09-12.
**Discipline:** green suite at every commit; CPU oracle before GPU (§0.1 inv 4);
one new untrusted thing (§0.1 inv 5) — the shadow ray; the three-attempt rule
(§0.3) per subsystem; no §0.2 limit raised to fix a symptom.

---

## Stage 1 — `ShadowRayReference.cs` + fuzz tests (§0.1 inv 4, §0.3 Sibling Pattern)

The CPU oracle, before any HLSL exists. Built by **reusing**
`RaymarchReference.TracerRaycast` with the shadow origin and direction
substituted — §13 Phase 7's own "if stuck" instruction, applied up front rather
than after something misbehaves.

Implements, exactly:
- **§6.5 origin rule** — `hitVoxel + faceNormal`, centre of that cell, i.e.
  `(float3)(hitVoxel + normal) + 0.5` in voxel units. **No bias term.** The
  function signature does not accept one, so a later caller cannot add one.
- **§6.5 8.2 LOD rule** — `SelectLODTier(length(samplePos − cameraPos))`, by
  **camera** distance to the sample point. Passed as an explicit `cameraPos`
  argument for the same reason: a caller cannot accidentally supply travelled
  distance.
- **Occlusion-only early exit** (first hit ends the ray); **sky pixels skipped**
  (no primary hit ⇒ no shadow ray, enforced at the call site and asserted);
  **max-distance clamp** at the effective island diagonal — tier 2's outer
  bound, for the reason recorded in the reading notes' 8.11 row.
- **C.10 penumbra** — track `m = min(clearance_i / t_i)` over near-miss cells;
  `sunVis = smoothstep(0, k_soft, m)` on miss, `0` on hit; `k_soft → 0` gives
  hard shadows and that degenerate case is a test, not a comment.

**Fuzz**, in the shape `RaymarchMacroSkipTests` / `RaymarchDenseSkipTests`
already use: thousands of rays against the per-voxel walk, including grazing
and near-axis-aligned regimes, plus a **work-saving assertion** so a silently
disengaged early-out cannot pass by doing the full walk.

**Mutation checks (minimum, both required by the prompt):**
1. Move the origin to the hit voxel's own centre → the self-intersection test
   must go **red**.
2. Swap the LOD rule to travelled distance → the silhouette-agreement test must
   go **red**.
Each is applied, observed, reverted, and recorded with its result.

**Stated honestly in the completion doc:** the tier-1/2 traversal branch still
has **no** CPU oracle (Amdt 8.10 §5.7, PHASE_2 §6.2) and shadow rays inherit
that gap. Tier 0 is fuzz-proven; tiers 1/2 are not, for shadow rays exactly as
for primary rays.

**Commit:** oracle + tests green.

---

## Stage 2 — `LightSourceBuffer.cs`, `SunCycle.cs`, emissive registry

- **A.6 `LightSource`** into `StructHeaders.cs` + `.hlsl`, alongside the
  existing structs, `[StructLayout(LayoutKind.Sequential)]`, field order
  identical: `float x,y,z / r,g,b / range / k`. Eight floats, 32 B. No invented
  fields, no reordering.
- **`LightSourceBuffer.cs`** — flat `GraphicsBuffer<LightSource>` sized
  `MAX_ACTIVE_LIGHTS`, plus the §3.8 **selection** logic: register/unregister a
  light at a voxel, and each frame emit the `MAX_ACTIVE_LIGHTS` **nearest to the
  player**, furthest dropping first. Selection is pure and static so EditMode
  can prove the degradation rule without a GPU.
- **`EngineConfig.MAX_ACTIVE_LIGHTS = 32`** (§0.2's value, §0.1 inv 8).
- **Mutation check:** invert the comparator to drop *nearest* first → the
  degradation test must go **red**.
- **`SunCycle.cs`** — the in-game clock (none exists, finding (d)) plus
  clock → sun direction and colour. Below the horizon: `sunVis` path skipped
  entirely, sun term 0, ambient carries the night. Pure functions
  (`DirectionAt(t)`, `ColorAt(t)`) so they are testable without a scene.
- **§6.6 emissive** — additively in `MaterialRules.cs`, beside the density and
  drag tables PHASE_6 §2.3 added for exactly this reason: a per-material
  `Emissive(byte)` strength and a `LightOf(byte)` (colour, range, k). A new
  `Materials.Torch = 14`, **excluded from `ContentVersionHash()`** the same way
  `Lava`/`Honey`/`Obsidian` already are, for the same stated reason.
- **The enqueue hook** goes in `EditService.TrySetVoxel` — the single §8.3
  choke point, after the write, beside the existing `MarkDirty` and
  `NotifyEdited` calls. **Not a second edit path.** Additive hook to a frozen
  system's internals, which §12 names as normal; `IEditService.SetVoxel`'s
  signature is untouched.

**Commit:** buffer + cycle + registry + tests green.

---

## Stage 3 — the shadow ray in `Raymarch.compute` (§13 file 3)

§13's order, and it is not negotiable: **hard first, verify against the Stage 1
oracle, then penumbra (C.10), then the point-light loop (C.11), then the
combine (C.9).**

Built in from the start, not retrofitted:
- **`SelectLODTier(float camDistVoxels)`** extracted from the four inline lines
  at `Raymarch.compute:701-706` and called from **both** sites, so §6.5's rule
  is defined once and the shadow call site must pass camera distance explicitly
  (finding (g)).
- **§6.5's 8.4 wave-uniform guard** — `WaveActiveAnyTrue(hasPrimaryHit)` around
  the shadow branch, so an all-sky wave never instantiates the path. §6.5 says
  in terms to build this from the start rather than retrofit after a budget
  miss.
- **The fallback ladder is config from day one**, all four rungs behind one
  `EngineConfig.SHADOW_QUALITY_LEVEL`-driven number so switching is a number
  change (§0.1 inv 8) and Stage 6 measures every arm **in one binary**:
  `0` full penumbra · `1` half-res beyond LOD1 with neighbour reuse ·
  `2` hard only · `3` off (flat N·L + ambient). Stepping down is the spec's own
  designed outcome, not a failure.

**The unchanged-when-off proof**, in the shape Amdt 8.10 §2 used for the
cascade: an **exact counter match** at a pose where the shadow branch cannot
engage — not a "should be identical" argument from code structure.

`ShaderCompileCheck` after **every** shader edit.

**Commit:** shader + proof green.

---

## Stage 4 — diagnostics (§10.3 item 6, §10.3 item 7, §10.4)

Into `CoreEngine/Diagnostics/` (today: `CpuTopology.cs`, `DebugPasses.cs`):
- Two new debug views following the existing `_DebugModePacked` convention —
  **7 SunVis**, **8 ShadowHeat** — plus their `RaymarchDebugControls.cs`
  bindings.
- Time-of-day scrubber.
- §10.3 item 7 HUD additions in `PlaygroundHud.cs`: sun angle / time of day,
  active lights vs `MAX_ACTIVE_LIGHTS`, current shadow quality level.
- §10.4 ring-buffer dump for the new path, **same shape as the existing
  raymarcher dump**: over a step threshold, log origin, direction, sun
  direction, and the last ~10 clipmap indices traversed.

**Commit.**

---

## Stage 5 — the scene and `run-phase7-rig.sh`

`Assets/Scenes/Phase 7 Cycle.unity`, generated by an Editor scene-builder
method following the `Phase5aSceneBuilder` / `CommandLineBuild` pattern —
setting every value explicitly, and referencing constants rather than retyping
them, because a builder's values override the component's `[SerializeField]`
defaults silently (the trap recorded in the builder's own comments).
`Game/Phase7CycleRig.cs` + `run-phase7-rig.sh`: one config per process launch,
unattended, screenshots at every gate, `phase7_report.txt` ending in a
**COMMIT READINESS** block stating correctness / performance / visual
**separately** (PHASE_3 §8).

§13's five acceptance items as machine-checkable gates where possible, with the
inherently-visual ones named as such:

| # | Gate | Checkable? |
|---|---|---|
| 1 | torch falloff lights surroundings; deep cave genuinely dark | **yes** — sample sun-vis/luminance at known voxels at known distances |
| 2 | dawn→dusk sweep continuous, no cascade staleness; single protruding voxel casts a crisp discrete shadow; **zero acne, zero Peter-Panning** at grazing angles | **partly** — continuity and acne are pixel-checkable; "crisp" is visual |
| 3 | **§6.5's silhouette test as a pixel-level assertion** — shadow edge aligns with the on-screen silhouette | **yes**, and this is the gate that catches a regression to the 8.1 rule |
| 4 | bomb a cave roof at noon → sunlight reaches the floor **the same frame**; pour water off a ledge → moving shadow | **yes** — same-frame is a one-frame assertion (§3.10) |
| 5 | §3.8 cap — place `MAX_ACTIVE_LIGHTS` torches, then more; graceful furthest-first degradation, not silent drops | **yes** |

**Mutation-check gates 2, 3 and 5 at minimum.** Every mutation and result
recorded.

**Commit.**

---

## Stage 6 — measurement, and the gate that cannot be run as written

**Neither §13 budget gate is runnable here** — no per-pass GPU attribution
(Amdt 8.9 Rule 1, no Xcode), Performance State unreadable at all (Rule 2),
`gpuFrameTime` inflated ~2.6–2.7× (Amdt 8.10), Unity merging dispatches into
one Metal encoder. PHASE_2 §5 hit the same wall; Amdt 8.9 §4 recommended
formalising a substitute and nobody ever did. **`AMENDMENT_8_12_PHASE7_GATE.md`
is that substitute**, written in 8.9/8.10's discipline: the literal spec text,
why it is unrunnable, exactly what was measured instead, what the substitute can
and cannot establish, and every number tagged VERIFIED (with its run) or OPEN.
**No per-pass ms figure is quoted against §11.2.**

**The substitute gate** — end-to-end sustained rate, which *is* measurable:
fullscreen, release standalone, **frames ÷ elapsed wall time**, 300 s cooldowns,
counterbalanced, **all arms in the same session**, reusing `PlaygroundCapture`'s
existing levers rather than a new harness.

| arm | config |
|---|---|
| A | pre-Phase-7 commit build — the honest regression baseline |
| B | Phase 7, shadow level 3 (off) |
| C | Phase 7, shadow level 2 (hard only) |
| D | Phase 7, shadow level 1 (half-res distant) |
| E | Phase 7, shadow level 0 (full penumbra) |

× two scenarios — **(i) golden hour** (longest grazing rays, captured
specifically as §13 demands) and **(ii) §13's true combined gate**, golden hour
with near-player fluid live — × two gates, the shipped 960×540 and a forced
`-gateres 1920` that pushes cost above one refresh interval where it becomes
visible.

**Pass criterion:** 60 FPS sustained with margin at 1080p output from the
960×540 internal gate, with the **shadows-off baseline re-established in this
session** (not compared against the 69.8 ± 0.4 FPS from a prior one — FPS §3.2
records a cross-session comparison producing a wrong number).

**Expect to step down the ladder and treat that as normal.** Raymarch alone
measured ~11.95 ms at `GroundHorizon` (PHASE_2 §5); current sustained play is
14.33 ms mean with ~2.34 ms margin (FPS §2.1); PHASE_3 §5 says plan against ~6
ms of headroom, not 16.67 ms; §6.5 says shadow rays double ray count worst-case;
Amdt 8.10 §6.4 already predicted the rungs would be needed rather than optional.

**If arm (ii) breaches while the isolated arms pass**, §13 calls that new
information — but **rule out the known CPU cost first**:
`FLUID_PERFORMANCE_AB_RESULTS.md` §3 measured `upload_ms` p50 going 0.001 →
6.595 ms under live fluid, of which **6.36 ms is the LOD cascade re-downsampling
fluid-touched chunks**. That is a main-thread cost, it is documented, and it is
not shadow/fluid GPU contention. Only after that is excluded does §2.2's
fallback fork apply — and §2.2 states two orderings that disagree, so both
directions get measured and the fork gets **flagged, not picked**. Any touch of
`FLUID_ACTIVE_RADIUS_VOXELS` is written as a visible fraction (`/ 10`), never a
literal.

**Every arm's raw output reported. A shipping quality level is recommended, not
decided** — that is a visual-quality judgement and it is the owner's.

---

## Stage 7 — Playground, and the five minutes

`PLAYGROUND_GUIDE.md`'s own five steps, all of them. Torch place/remove on a
key and in the hotbar; time-of-day scrub keys **plus single keys that jump
straight to golden hour, noon and midnight** — the three poses the owner has to
judge in minutes; toggles for the sun-visibility view, the shadow step heat,
hard vs soft, and quality level 0–3; the HUD additions; and **every one of them
added to the F2 overlay**, which the guide calls the copy that cannot go stale.

**`LightingConfig.cs` + `LightingConfig.json` is the highest-value deliverable
of this phase**, per §13's own solo-dev note: the fused shadow ray is a day of
code and the real work is tuning the penumbra constant, the ambient curve and
the sun-colour ramp by eye, in real time. It copies `PlayerConfig.cs` **exactly**
— same persistent-data-path location, same poll-on-timestamp reload, same
`Parse`/`Validate` split so EditMode can prove the malformed-file behaviour
without a scene, and the same "a half-written file leaves the running config
untouched" guarantee. It covers at minimum: `k_soft` (C.10), the C.11 falloff
`k`, torch range and colour, ambient sky intensity and colour, the underground
ambient falloff, the sun-colour ramp across the cycle, and the day-length
multiplier. **Pinned with tests the way `PlayerConfigTests` pins
`PlayerConfig`.**

Then `PLAYGROUND_GUIDE.md` updated in the same pass — §3 Controls, §4 What to
look for, a new §8 row, and §7 stating plainly what Phase 7 built that the
Playground cannot show. `./run-playground.sh`, **screenshots viewed, not
counted**, and what is actually in frame reported at noon, golden hour and
midnight, both above ground and in the deep cave chamber.

---

## Out of scope — named so I can tell when I am near them

Amendment 8.11 (render range vs world size — DRAFT, NOT ADOPTED, wants the
owner's decision); the feel-based carry-forwards; the unattributed frame-time
tail (ten candidates eliminated across five sessions — a standing toolchain
limitation, not a to-do); the mild sustained-load stutter (FPS §6c); the two
deferred optimizations (CA region passes scaling with resident tiles; the LOD
cascade air-mip gap); the two rejected shader fixes in FPS §3; and the v1.5
items §3.8/§6.5 defer (BFS light propagation, `MAX_LIGHTS_PER_BRICK`).

**Nothing in this phase writes "Phase 7 is complete."** The owner closes it.
