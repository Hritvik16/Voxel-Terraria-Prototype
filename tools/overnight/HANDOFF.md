# 66b-underwater-view: DONE. With the eye inside water you now see the lake bed and walls through blue-green fog, and the surface from below. Above water nothing changed (rig shots byte-identical).

## Why it was black
With the eye inside a water voxel, the primary ray's first read was that voxel: `mat != 0`, so a "hit" at t = 0 with normal
(0,0,0). Its sun and sky rays also started inside the water, giving sunVis 0 and skyVis 0. The face shaded to near-black.

## What changed (branch `engine-phase`, commit 892c4f9)
- `Raymarch.compute`: `passMat` = the fluid the eye is in (water, honey, oil, acid, sludge, quicksand), or 0. Every primary-march hit
  test is now `mat != passMat`, which is exactly the old `mat != 0` when the eye is not in a fluid. Inside a fluid: air-mip leaps off,
  tier forced to 0, the march ends where per-fluid extinction reaches 98% (17.8 m for water). The ray stops on any other material
  (bed and walls shaded as usual) or on AIR (the surface from below: sky inside Snell's window, cos 0.66, the fluid's light outside it).
  The fog uses per-fluid rgb extinction: transmittance exp(-d x ext) along the view. Light fades with depth below the fluid's top
  (`FluidTopAbove`: <= 48 reads up from the eye, skipping whole fluid bricks).
  Under a fluid: no sun/sky shadow rays (sunVis 0.8, skyVis 1), no FaceAO (it counted the water as occluders), no far band.
  **Lava and steam are unchanged**: the eye in lava still draws near-black (simplest; the player is burning).
- `RaymarchFeature.UnderwaterView` (static, default true) -> `_UnderwaterView`. `PlaySettings.underwaterFog` (default on; an old file
  gets on), `-underwater 0|1`, and an F6 row "Underwater view" On/Off (Off = the old black). Rigs keep the default; no rig pose is under a fluid.
- `-movedemo`: new shots 2a (eye 1 m under), 2b (eye 6 m under), 2b_view_off (same pose, view off), 2c (6 m, looking up 60 deg).
  The eye is held by re-teleporting each frame, and the dive pose is restored before the release check.
- `PlaySettingsTests`: new `UnderwaterFog_OldFileGetsOn_FlagOverridesForTheRun` test; the round-trip test now covers the field.
  CLAUDE.md: one line under Commands.
- No fluid-sim change, no memory-layout change, no frozen API touched. The CPU is still the sole terrain writer.

## Evidence
- Fast EditMode `PASS 691 FAIL 0 SKIP 14`, run before and after the final shader edit (Logs/overnight/66b-fast.out, 66b-fast2.out).
- `run-phase7-rig.sh` PASS 37 FAIL 0 (Logs/overnight/66b-phase7.out, run 2026-10-11_084304). Gate 3: 430 agree / 8 disagree = 98.17%,
  same as every default run since 10-10 (04:59, 20:47, 22:41). **All 19 shots are md5-identical to the 2026-10-10_224138 run.**
  The 02:28 run is the odd one out (426/12, 8 sun shots differ); it was most likely task 61's half-res arm, not the default.
- Play (`BUILD=1 EXTRA="-movedemo" ./run-play.sh`, Logs/overnight/66b-demo-player.log): lake at int3(41612, 6477, 43148), surface 647.8 m,
  bed 6.7 m below the shot spot. Shots at eye 1.00 m and 6.00 m under, noon. Release floats back in 5.6 s (task 65: 5.8 s).
- Shots looked at: 1_swim_surface matches task 65's (opaque blue water from above, the same dark hillside top right).
- Cost: not measured (owner rule). Eye-in-fluid pixels walk dense water bricks voxel by voxel up to 17.8 m, plus <= 48 coherent
  reads up. The demo HUD showed 61-71 fps under water vs 43-45 at the surface shot; that is not evidence, but nothing looks expensive.

## Picks (owner may change)
- Water extinction per metre r/g/b 0.60/0.30/0.22 (bed readable to ~6-8 m). Honey 0.9/1.4/3.0, oil 3.0/3.2/3.5, acid 1.0/0.35/1.4,
  sludge 1.5/0.6/1.8, quicksand 6/7/9. The depth light fade uses half of these.
- No terrain shadows under water. A flooded cave is lit as open water, dimmed only by the depth below the fluid top above the eye.
  The surface from below is a flat colour (no refracted world above). Oil floating on water looks like an opaque ceiling from below.

## Unfinished / next
- Not checked: torch light under water (`TorchRayBlocked` may treat water as a blocker); the eye inside oil/sludge/acid has no shots.
- Next queued task as the queue says.

## For the doc
Task 66b: underwater view. Diving used to show a black screen. Now, with your eyes in water, you see the lake bed and walls tinted
blue-green, fading into fog after a few metres, and looking up shows a brighter patch where the sky is. Above water nothing changed.
F6 "Underwater view" turns it off.
- Frame under water: black -> lit bed + fog (Screenshots/66b/2b_under_6m_view_off.png -> 2b_under_6m.png).
- Phase 7 rig: PASS 37 FAIL 0 both before and after; all 19 shots byte-identical to the last default run; Gate 3 98.17% unchanged.
- Fast EditMode: 691 passing, 0 failing (1 new test).
- `Screenshots/66b/2b_under_6m_view_off.png`: BEFORE. Eye 6 m under at noon: all black except the HUD and the yellow build cursor.
- `Screenshots/66b/2b_under_6m.png`: AFTER, same pose. The sandy lake bed 0.7 m below the eye, its voxel steps green-tinted
  (red absorbed at depth); the step tops are lighter, the sides darker.
- `Screenshots/66b/2a_under_1m.png`: eye 1 m under, looking 35 deg down. The left half is the bed, blue-tinted; the right half is the deeper
  water fading to flat blue fog.
- `Screenshots/66b/2c_look_up_surface.png`: 6 m under, looking up. The brighter top right is the sky through the surface (Snell's
  window); the dark slope bottom left is the lake wall.
