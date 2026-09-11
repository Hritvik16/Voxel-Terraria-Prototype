# Playground Guide — how to touch everything the engine can do

> ## THIS DOCUMENT IS PART OF FINISHING A PHASE
>
> **When a phase's files land, they get woven into the Playground and this guide
> is updated in the same pass.** A phase is not done when its rigs go green — it
> is done when you can walk into the scene and *feel* the thing it added.
>
> The rule, concretely:
>
> 1. Wire the new phase's systems into `Assets/Game/Playground.cs`.
> 2. Add their controls to the in-scene help overlay (**F2**).
> 3. Add a row to [§8 Phase coverage](#8-phase-coverage) below saying what
>    landed and what did not.
> 4. Update [§3 Controls](#3-controls) and [§4 What to look for](#4-what-to-look-for).
> 5. Say plainly in [§7](#7-what-this-scene-is-not) anything the phase built
>    that is **not** reachable here, and why.
>
> A phase whose work you cannot touch in the Playground is a phase nobody can
> form an opinion about.

---

## 1. What the Playground is

A **dogfood scene**. You walk around in the real engine — real Phase 3
generation, real Phase 4 streaming, real Phase 5 fluid, real Phase 6 physics and
editing — and form an opinion.

It is **not** a diagnostic scene. It asserts nothing and proves nothing. Its
header says so, and so does its HUD. See [§6](#6-what-you-can-and-cannot-trust)
before quoting any number off the screen.

## 2. Running it

```bash
./run-playground.sh
```

Builds a release standalone, runs the automated screenshot pass, and prints
where the shots went. To just play:

```bash
open -n Builds/Playground.app
```

**Click the window first** to capture the mouse. **ESC** releases it. Nothing
you click before capture acts on the world — the first click is consumed by
capture on purpose, so clicking back into the window cannot dig a hole.

## 3. Controls

Everything below is also on **F2** in-scene, which is the copy that cannot go
stale.

### Movement

| Key | Action |
|-----|--------|
| `W` `A` `S` `D` | move |
| `Space` | jump *(walk)* |
| `Shift` | sprint |
| `Q` / `E` | down / up *(fly only)* |
| `Tab` | **walk ↔ fly** |
| `R` | respawn on the surface under you |
| `F` | go to the fluid arena |

**Walk** is the Phase 6 `PlayerController`: gravity, jumping, 3-voxel step
climbing, coyote time, and swimming. **Fly** is a noclip camera with no physics
— useful for getting somewhere fast or looking at something from outside.

### Building and digging

| Key | Action |
|-----|--------|
| `LMB` (hold) | **dig**, at the current tool's rate |
| `RMB` (hold) | **place** the held item |
| `1`–`6` / scroll | pick the held item |
| `Z` / `X` | cycle tool tier |

The three tool tiers are §13's: **hand 10 vox/s**, **drill 40 vox/s**,
**bore 200 vox/s**. The rate is real — a slower tool genuinely takes longer to
remove the same rock, and the HUD shows voxels dug in the last second so you can
watch it.

### Fluid

| Key | Action |
|-----|--------|
| `V` | open a vent above the crosshair (needs water/sand/lava held) |
| `0` | close all vents |

### Phase 6 toys

| Key | Action |
|-----|--------|
| `B` | bomb at the crosshair (§8.5 mass destruction) |
| `T` | shoot a projectile (§8.4 DDA trace) |

### Overlays

| Key | Action |
|-----|--------|
| `F1` | performance overlay |
| `F2` | controls help |
| `ESC` | release the mouse |

## 4. What to look for

### The player (§8.1)

- **3-voxel steps** should be walkable without jumping; a 4-voxel wall should
  not. Dig a staircase and try it.
- **Coyote time** — run off a ledge and jump a fraction of a second late. It
  should still work.
- **No tunneling** — sprint into a 1-voxel wall. You stop at its face.

### Live tuning (§8.1's highest-value tool)

**This is the single most useful thing in the scene.** Feel parameters live in
JSON and reload *while the game is running*:

```
~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/PlayerConfig.json
```

Edit `jumpImpulseMps`, save, and **the very next jump is different** — no
recompile, no restart. Same for `accelerationMps2`, `maxSpeedMps`, `airControl`,
`frictionMps2`, `stepHeightVoxels`, `coyoteTimeSeconds`.

A malformed file is ignored and the running config is kept, so you cannot break
a session with a typo mid-edit.

### Editing (§8.3)

- Dig with each tier and watch the **dug/second** readout track 10 / 40 / 200.
- Dig at the very edge of the loaded world. The HUD counts **"refused (not
  loaded)"** — edits to unstreamed chunks are refused and counted, not silently
  dropped.

### Fluid (§7) — THE ARENA IS GONE

**This section was rewritten. The scene used to build the old dense fluid
region and this guide described it; both are now on §7.2's tiled substrate,
and the difference is the kind a player notices immediately.**

There is no arena any more. There is no box you can stand outside of.

- **Fluid lives wherever you put it.** The active set is a pool of 32³ tiles
  acquired around fluid that actually exists, not a fixed region allocated up
  front.
- **What bounds it is §7.4's active radius around you**, and nothing else.
- **The footprint does not grow with the radius.** The state panel reports the
  active set in MB; it reads the same whether the radius is 128 voxels or the
  shipped 1280. That invariance is the entire point of the tiled design.

The scene makes it visible:

- The crosshair box is **amber inside** the active radius and **grey outside**.
- Placing fluid outside the radius is **refused**, with the reason on screen —
  a fluid voxel written out there would be drawn and never simulated.
- The state panel shows **tiles resident / cap**, how many were acquired and
  released, whether the pool is full, and how many slots are actually
  simulating.

### Scattered still water near the radius edge is CORRECT

If you fly out to the edge of the active radius you will see a band of water
that is rendered but completely motionless, scattered rather than pooled.
**That is not fluid breaking and not a wake failure** — it is §7.4's
hysteresis band, and it was measured in the 2026-09-11 QA pass:

```
  inside radius, HAS tile     0
  inside radius, NO tile      0   <-- a real wake failure would appear here
  outside radius, no tile     0
  outside radius, HAS tile 1123
```

Two radii, not one. Fluid promotes inside the **wake** radius (1280) and its
tile is only released past the **sleep** radius (~1478). Between them —
a ~198 voxel / ~20 m band — fluid keeps its tile but does not tick. It is
supposed to sit there. Fly closer and it resumes.

**Try this:** pour water, then fly away past the radius. It freezes exactly as
it was — §7.4 working, not fluid breaking. Fly back and it resumes. Watch the
**tiles resident** count fall as you leave and climb as you return; that is the
tile pool releasing and re-acquiring, which the old dense build could not do.

**Also try:** change `_activeRadiusVoxels` on the `Playground` component from
128 to the shipped 1280 and watch the **active set MB stay the same**. Under
the old dense region that change was unaffordable — a 2048³ region is 128 GB.

> The radius default here is **128 voxels (12.8 m), a demo value**, so you can
> walk out of it. The shipped constant is `FLUID_ACTIVE_RADIUS_VOXELS = 1280`.

Pour water down a slope, drop sand and watch it fall, breach a wall and watch
it drain.

### Fluid no longer stops after you place a lot of it

If you remember fluid mysteriously freezing after a while — that was real, and
it is fixed. `AllocSlot` was a bump allocator with no free list, so slot indices
were consumed by churn rather than by live fluid: **24.5 allocations per live
voxel**, and **406 placed voxels exhausted the region permanently**. Appendix
A.5 specifies an intrusive free list and the CPU oracle implements one; the GPU
port had simply omitted it.

After the fix: **2.2 allocations per live voxel**, and 2,800 placed voxels use
849 of 8,192 slots. If you ever see it stall again, that is a regression worth
reporting — `./run-fluid-activity.sh` pins it.

### Buoyancy and swimming (§8.6)

Fill a basin, walk in. The HUD shows **% submerged**, which fluid, the buoyant
acceleration and the drag. A body at 985 kg/m³ floats in water (1000), sinks in
air, and rides higher in lava (3100).

> **Swimming is composed in the Playground, not in the engine.** §8.6's
> `Buoyancy` only *produces* forces — `PlayerMotor` deliberately does not consume
> them (one-way sampling, §8.6). `Playground.ApplyBuoyancy` joins the two so you
> can actually swim. That join is game-layer, is **not** part of Phase 6's proven
> surface, and no rig covers it.

### Destruction (§8.5)

`B` detonates at the crosshair. Watch the HUD: the blast **drains across
frames** under a work budget rather than stalling one, and produces exactly
**one Proxy Drop** with a per-material tally. Default radius is 20 voxels;
§13's reference 400K event is radius 46 (set `_bombRadiusVoxels` on the
`Playground` component to try it).

### Projectiles (§8.4)

`T` traces a segment down the crosshair and marks the impact voxel for a couple
of seconds. It reports what it hit, how far, and how much fluid it passed
through on the way — a shot through water still notices the water.

### The §8.2 speed clamp

If the fluid op-list readback stalls, the HUD shows **§8.2 SPEED CLAMP ENGAGED**.
In normal play you should essentially never see this. If you see it sitting on
constantly, that is a finding worth chasing — take it to the Phase 5 rigs.

## 5. Reading the HUD

- **Top-left** — what mode you are in, and the last thing that happened.
- **Bottom-centre** — the hotbar. The highlighted slot is what `RMB` places. The
  panel left of it is what `LMB` does (and at which tier); the panel right of it
  is what you are holding. Whichever fired most recently lights up.
- **Bottom-left** — live engine state: grounded/airborne, speed, submersion,
  dug/second, last blast, last shot, edit counters, and the arena's position.
- **Top-right** (`F1`) — performance. **Read §6 before believing any of it.**

## 6. What you can and cannot trust

**Nothing on this screen is evidence.** It is a feel scene with a GUI drawn over
a compute raymarch.

- `WALL` is `Time.unscaledDeltaTime` — the only figure with a defensible
  relationship to reality, and still not a benchmark.
- `GPU` is **inflated ~2.6–2.7× on this Mac** (Amendment 8.10). Watch it move;
  do not read its value. An earlier session produced a result from it that had
  to be retracted.
- `CPU MAIN` is main-thread *work*, not wall time. If it and `WALL` disagree
  wildly, **that gap is the finding**.

The only trusted frame-time source is `./run-acceptance-rig.sh`'s own printed
report, from a release standalone launched outside the Editor. If something here
looks alarming, reproduce it in the phase rig that owns it before treating it as
real.

### The frame-time spikes are real, and nobody knows what causes them

**If you watch `worst/3s` you will see occasional frames far above `WALL`. That
is not this overlay lying. It is a real, measured, still-unexplained tail, and
you should know its status before it alarms you.**

Four sessions have chased it with cooled, driftchecked measurements. Eliminated
by evidence, not argument: GC (zero collections on the affected frames, heap
flat), in-run thermal drift, the LOD cascade, vsync and present-wait, streaming
starvation, upload byte volume, the CPU op-list apply, GPU time (frames are
*long* when GPU time is *low*), CA dispatch rate, and this rig's own overhead.
A per-system toggle sweep then turned **every** subsystem off one at a time —
physics, edits, detonations, projectiles, buoyancy, and fluid itself — and the
tail did not move. Turning fluid off entirely produced the *worst* number of the
eight configurations.

On an affected frame Unity reports main thread ~9 ms, present wait 0.0, GPU
~30 ms (inflated) — **nothing the engine times accounts for the wall clock.**
The time is spent with the main thread not running.

**Treat this as a known, bounded, toolchain-level limitation, not an open
to-do.** Separating the remaining possibilities needs GPU-stage attribution,
which this workflow does not have and cannot get (no Xcode, no Instruments;
Unity merges the CA's dispatches into one Metal encoder). Do not spend a
session re-deriving the ten eliminated causes — they are recorded in
`FLUID_PERFORMANCE_AB_RESULTS.md`.

## 7. What this scene is *not*

- **Not a scale test.** Fluid budgets are tens to low hundreds of voxels — the
  range Phases 5a/5b actually tested. §2.5's ~500,000 near-player active target
  has never been tested and this is not where that should be discovered.
- **Not a demonstration of the shipped active radius.** The radius here is a
  demo value 10× smaller than the engine constant, chosen so the effect is
  visible in a 64-voxel arena. The real value has never been measured either —
  `FLUID_ACTIVE_RADIUS_VOXELS` is marked *"ASSUMPTION, NOT MEASURED"* with a
  Phase 5b GPU-lane measurement named as the only thing that should move it.
- **Not a correctness proof.** That is what the rigs are for:
  `run-phase5b-rig.sh`, `run-phase6-*.sh`, `run-phase6-sandbox.sh`, and
  `run-editmode-tests.sh`.
- **Not the §13 `Phase6_Sandbox` acceptance scene.** That is
  `run-phase6-sandbox.sh` — a scripted rig that asserts and exits. This scene is
  the playable counterpart.

## 8. Phase coverage

What of each phase you can actually touch here. **Add a row when a phase lands.**

| Phase | In the Playground | Not reachable here |
|-------|-------------------|--------------------|
| **3 — Generation** | The whole island. Everything you walk on is real Phase 3 terrain. | Generation parameters; use the Phase 3 scene. |
| **4 — Streaming & persistence** | Streaming runs constantly as you move. Resident chunks, dense bricks and pool pressure are on the F1 overlay. Edits at the window edge are refused and counted. | Save/reload round trips, admission/eviction gates — `run-acceptance-rig.sh`. |
| **5 — Fluid** | Vents (`V`), placing water/sand/lava, watching it settle on natural terrain. Active-radius bounds shown by crosshair colour. | Conservation proofs — Phase 5b rig. Honey/lava viscosity at their real tick intervals is slow enough to be hard to judge here. |
| **7.2 — Tiled active set** | **Wired 2026-09-11.** The state panel reports tiles resident / cap, acquired and released counts, whether the pool is full, live slot count, and the active set in MB. Fly away and watch tiles release; fly back and watch them re-acquire. Change `_activeRadiusVoxels` from 128 to the shipped 1280 and watch the MB figure **not move** — that invariance is the design's whole claim, and the old dense build could not have run at 1280 at all (a 2048³ region is 128 GB). | The 512-tile cap being reached — `run-fluid-tiled.sh` scenario C and the chaos ladder. |
| **7.4 — Active radius** | Walk away from a pool and watch it sleep to static terrain; walk back and watch it **wake again** — that second half was missing until 2026-09-05 and a region that slept mid-flow could never restart (`DESIGN_NOTE_7_4` §8). Wake/sleep radii, the current centre and the re-centre count are all on the state panel. | The radius value here is the **demo** 128, not the shipped 1280. The shipped value has now been measured and **cannot bite inside any region that can exist** — see `DESIGN_NOTE_7_4` §9; that is an open design fork, not something the Playground can show. Multi-region behaviour — `run-fluid-activity.sh` and `run-fluid-scale.sh`. |
| **6 — Physics & editing** | Walking, jumping, 3-voxel steps, coyote time (§8.1); live `PlayerConfig` reload (§8.1); digging with all three tool tiers and placing through `EditService` (§8.3); bombs (§8.5); projectiles (§8.4); buoyancy and swimming (§8.6); the speed-clamp indicator (§8.2). | Swept CCD at 60 m/s — no grapple exists here yet, so `SweptCCD` is constructed but only exercised by its rig. The adversarial checkerboard (§3.6) — now covered by `run-phase6-sandbox.sh` step 3, which reaches the high-water mark and fires the LRU valve. |
| **7 — Lighting** | *(not started — do not begin without a scoped prompt)* | — |

## 9. Known rough edges

- **No grapple**, so §8.2's swept CCD is not exercised by hand. The 60 m/s case
  lives in `run-phase6-ccd.sh`.
- **The bomb default is radius 20**, not §13's 400K reference (radius 46), so it
  is pleasant to use rather than a stress test.
- **Fly mode has no collision at all** — that is what it is for.
- **There is no fluid arena any more.** The scene moved to §7.2's tiled
  substrate on 2026-09-11; fluid lives wherever you put it and is bounded only
  by §7.4's radius. `F` still takes you to the basin the scene seeds, but that
  is now just a nice place to start, not a boundary.
- **Frame-time spikes have a known, unexplained cause** — see §6. Four sessions
  eliminated ten candidates and a full per-system toggle sweep; do not
  re-diagnose it.
- **The active radius is a scene demo value** (128 voxels), not the shipped 1280.
  Both the radius and the hysteresis ratio behind it are engineering defaults
  that have never been measured against a GPU-lane budget.
- **Only one fluid region exists in this scene.** The engine now supports
  several at once — and `EditService` wakes all of them, which it did not until
  this was tested — but the Playground makes only one, so you cannot see that
  here.
