# 100-gameplay-step-7-grapple: DONE -- grappling hook (fire at solid <= 25 m, hold to reel, swing), tests 11/11, fast suite green

## What changed (commit 29e8937, + docs commit after it)
- `Assets/CoreEngine/Gameplay/PlayerMotor.cs`: the hook is on the ABILITIES path only (rigs keep the legacy Step, which clears it).
  - `MotorInput` gains `LookDir` (set by FromLook), `HookPressed`, `HookHeld`.
  - Fire (`FireHook`/`RayHook`): from the eye along LookDir, VoxelRayWalker; air and fluid passed; first SOLID voxel within `hookRangeM` = anchor
    (a point on its face); an unloaded chunk or nothing in range = miss.
  - Hold = `StepReel`: no gravity, eases to 12 m/s toward the anchor, rope shortens; stops at 0.3 m or when terrain takes 90% of the pull
    (through MoveWithCollision, so it stops AT the wall); a face beside the anchor lets the body slide up it first.
  - Released in the air = `StepSwing`: gravity, WASD pump 5 m/s^2 (no cap), inextensible rope (`ApplyRope`: project back + drop outward
    velocity; if a wall refuses the correction, close it vertically, so you hang against a face instead of sliding down it).
  - On the ground, not reeling: walk physics + the rope as a tether.
  - Ends on: Space (the press still jumps or air-jumps, so swing -> double jump -> glide works), dash, swim, cling, landing after a swing (a normal
    landing; vitals read LastImpactDownMps as before), the anchor voxel dug out or unloaded, Teleport, the legacy Step. Pressing again re-aims.
- `PlayerConfig.cs`: `hookRangeM` 25, `hookReelSpeedMps` 12, `hookReelAccelMps2` 40, `hookMinLengthM` 0.3, `hookSwingAccelMps2` 5 (+ Validate).
- `Assets/Game/Playground.Grapple.cs` (new): keys, the rope (screen line from the right hand to the anchor + a diamond), an in-range diamond ring
  on the crosshair, and the HUD state (REELING / HOOKED / SWINGING).
  - `Playground.cs`: DriveWalk reads the hook keys. The F toy ("go to the basin") stands aside in play's walk mode. F2 help line added.
- `run-play.sh` key list; `Playground.GrappleDemo.cs` (`-grappledemo`); `CLAUDE.md` task line; `GAMEPLAY_PHASE_PLAN.md` Done row 7.
- No frozen API, no memory layout, nothing under `Assets/TerrainGen/`.

## Evidence
- `GrappleTests` (11, all pass). They cover:
  - the hook passes air and water and bites the first solid face exactly (x = 10.000 m);
  - misses at 26 m, in open sky, on water-only and on an unloaded chunk;
  - the reel stops flush at the wall, never inside it (every frame), at 12.00 m/s;
  - reel up a sheer face, then hang;
  - swing energy (bottom speed > 85% of ideal) and the far side reached;
  - Space -> double jump -> glide;
  - a swing landing ends the hook, 0 damage on a 2 m drop;
  - a reel into the ground does no damage (12 < 13.3 m/s);
  - dash, swim, cling, dug anchor and the legacy Step each end the hook;
  - re-aim, and range 0 = off.
- Fast suite `PASS 853 FAIL 0 SKIP 14` (13:33). The filtered runs with Glide/Climb/MovementSetA also passed (48 + 2 fixed).
- Demo, 2 launches (the first one's swing shot was weak): `Screenshots/100/run2/grappledemo_log.txt`.
  - Anchor 8.95 m up the face from 13.2 m away.
  - Reel at 12.00 m/s, arrived 0.31 m from the anchor, no overlap, feet 7.53 m up.
  - Swing at 9.5 m/s, top speed 12.1 m/s.
- Screenshots looked at: run1 1, 2, 3, 4 and run2 1, 4, 5. The descriptions are in "For the doc" below.
  - run2's `1_site.png` looked the wrong way (the code was reverted to run1's angle; use run1's site shot).

## Picks (owner may change)
- Key: **F** (or the middle mouse button) in play's walk mode. Every letter is already bound; F's old toy (teleport to the basin) stays in fly mode.
- Hold-to-reel / release-to-swing; Space lets go, and the press is NOT eaten (a jump or double jump follows).
- Reel 12 m/s, under the 4 m fall-damage speed, so reeling into the ground never hurts.
  - Swing landings can hurt like any landing.
  - The hook does not reset air jumps.
- Minimum rope 0.3 m. The reel has no gravity, so holding F hangs you on a wall indefinitely (no stamina).
- Not in liquid: no firing while swimming, and entering a liquid lets go.
- The rope does not wrap around terrain. There is no 3D rope model: it is a screen-space line drawn from the right hand.

## Not done / next
- Not measured (feature task).
- The rope is a 2D overlay. A world-space rope, like any third-person view, belongs to a later visual task.

## For the doc
- Result: grappling hook in play. F or middle mouse hooks solid terrain within 25 m. Hold F to reel in at 12 m/s; it stops at the wall.
  Let go in the air to swing. Space lets go and still jumps. GrappleTests 11/11; fast suite 853/0.
- In `./run-play.sh`, walk mode:
  - F (or middle mouse) fires at the crosshair; a diamond ring on the crosshair means "in range".
  - Hold F to reel; release F in the air to swing; WASD pumps the swing; Space lets go; F again re-aims.
- Numbers (`Screenshots/100/run2/grappledemo_log.txt`):
  - anchor 8.95 m up a sheer face, from 13.2 m away;
  - reel 12.00 m/s, ending 0.31 m from the anchor with 0 overlap;
  - swing 9.5 m/s near the bottom of the arc, top 12.1 m/s;
  - damage from a reel landing: 0 (test).
- Picks: see above (key F/MMB, no wrap, reel 12 m/s, hang without stamina, no hook in liquid).
- Images (midday, sun behind the camera):
  - `Screenshots/100/run1/1_site.png` -- the test site. The dark 15 m stone face fills the middle; the black slab at the top is the floating
    swing beam's unlit underside.
  - `Screenshots/100/run2/2_aim_in_range.png` -- aiming 9 m up the face from 11 m. Look at the yellow diamond around the crosshair: the hook would bite there.
  - `Screenshots/100/run2/3_reeling.png` -- half way in. The beige rope runs from the lower right to the anchor diamond near the top centre;
    the HUD says "REELING 6.3 m to go".
  - `Screenshots/100/run2/4_hooked_on_sheer_face.png` -- hooked 7.5 m up the sheer face, looking along it and down. The face is on the left, the
    stone pad and grass far below on the right; the HUD says "HOOKED 0.3 m".
  - `Screenshots/100/run2/5_swinging_under_beam.png` -- swinging under the beam at 9.5 m/s. The rope runs up to the beam's black underside;
    the HUD says "SWINGING rope 5.2 m".
