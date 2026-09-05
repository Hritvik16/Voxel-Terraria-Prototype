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

### Fluid (§7), and what is fixed vs what follows you

Two different things, and it is worth keeping them apart:

- **The arena (region) is fixed** — 64³ voxels in one place. That is deliberate,
  not a gap: the region is the CA's addressing space, and §7.2's op-list is
  indexed by region cell, so moving it would re-index every in-flight batch.
- **The activity inside it follows you** — §7.4's near-player active radius is
  now driven. Fluid ticks only within a radius of you and sleeps back to static
  terrain when you leave.

The scene makes both visible:

- The crosshair box is **amber inside** the arena and **grey outside** it.
- Placing water, sand or lava outside the arena is **refused**, with the reason
  on screen — a fluid voxel written out there would be drawn and never
  simulated, hanging in mid-air forever.
- The state panel shows the **wake / sleep radii**, where the centre currently
  is, how many times it has re-centred, and whether the arena is inside the
  radius right now.

**Try this:** pour some water, then walk away past the radius. It stops moving
and freezes exactly as it was — that is §7.4 working ("distant water is a
settled terrain byte that looks like water but does not tick"), not fluid
breaking. Walk back and it resumes.

> The radius here is **128 voxels (12.8 m), a demo value**. The shipped constant
> is `FLUID_ACTIVE_RADIUS_VOXELS = 1280` (128 m) — 23× this arena's
> half-diagonal, so at the real value the radius would be correct but completely
> invisible in a 64-voxel arena. Change `_activeRadiusVoxels` on the `Playground`
> component to see the shipped behaviour.

Inside the arena: pour water down a slope, drop sand and watch it fall, breach a
wall and watch it drain.

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
| **5 — Fluid** | Vents (`V`), placing water/sand/lava, watching it settle on natural terrain. Arena bounds shown by crosshair colour. | Conservation proofs — Phase 5b rig. Honey/lava viscosity at their real tick intervals is slow enough to be hard to judge here. |
| **7.4 — Active radius** | Walk away from a pool and watch it sleep to static terrain; walk back and watch it wake. Wake/sleep radii, the current centre and the re-centre count are all on the state panel. | The radius VALUE is a demo number, not the shipped one, and neither has been measured. Multi-region behaviour (several pools live at once) is real but not visible here — `run-fluid-activity.sh` covers it. |
| **6 — Physics & editing** | Walking, jumping, 3-voxel steps, coyote time (§8.1); live `PlayerConfig` reload (§8.1); digging with all three tool tiers and placing through `EditService` (§8.3); bombs (§8.5); projectiles (§8.4); buoyancy and swimming (§8.6); the speed-clamp indicator (§8.2). | Swept CCD at 60 m/s — no grapple exists here yet, so `SweptCCD` is constructed but only exercised by its rig. The adversarial checkerboard (§3.6). |
| **7 — Lighting** | *(not started — do not begin without a scoped prompt)* | — |

## 9. Known rough edges

- **No grapple**, so §8.2's swept CCD is not exercised by hand. The 60 m/s case
  lives in `run-phase6-ccd.sh`.
- **The bomb default is radius 20**, not §13's 400K reference (radius 46), so it
  is pleasant to use rather than a stress test.
- **Fly mode has no collision at all** — that is what it is for.
- **The fluid arena is placed once at startup**, in the lowest non-water basin
  near spawn. If the scene opens somewhere flat, the arena may be somewhere
  unexciting; `F` takes you to it.
- **The active radius is a scene demo value** (128 voxels), not the shipped 1280.
  Both the radius and the hysteresis ratio behind it are engineering defaults
  that have never been measured against a GPU-lane budget.
- **Only one fluid region exists in this scene.** The engine now supports
  several at once — and `EditService` wakes all of them, which it did not until
  this was tested — but the Playground makes only one, so you cannot see that
  here.
