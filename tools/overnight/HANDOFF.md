# 100-gameplay-step-7-grapple: DONE -- grappling hook (fire at solid <= 25 m, hold to reel, swing), tests 11/11, fast suite green

## What changed (commit 29e8937; docs 35ddb17)
- `PlayerMotor.cs`, ABILITIES path only (the legacy Step clears it). `MotorInput` + `LookDir`, `HookPressed`, `HookHeld`.
  - Fire (`FireHook`/`RayHook`): from the eye, VoxelRayWalker; air and fluid passed; the first SOLID voxel within 25 m (its face point) is the anchor;
    an unloaded chunk = miss.
  - Hold = `StepReel`: no gravity, 12 m/s toward the anchor; stops at 0.3 m or when terrain takes 90% of the pull (at the wall, via
    MoveWithCollision); slides up a face beside the anchor first.
  - Air, released = `StepSwing`: gravity, WASD pump, inextensible rope (`ApplyRope`; a wall-refused correction is closed vertically, so you hang
    instead of sliding down). On the ground = walk + tether.
  - Ends: Space (the press still jumps/air-jumps), dash, swim, cling, a swing landing (a normal landing for vitals), dug/unloaded anchor,
    Teleport, legacy Step. F again re-aims.
- `PlayerConfig.cs` `hook*` (range 25, reel 12, accel 40, min 0.3, swing pump 5).
- `Playground.Grapple.cs` (new): keys, the rope overlay + anchor diamond, the in-range ring, HUD states.
  - `Playground.cs`: wiring; the F toy stands aside in play's walk mode; F2 line. Also `run-play.sh` keys, `-grappledemo`, CLAUDE.md, plan Done row.
- No frozen API, no memory layout, nothing in `Assets/TerrainGen/`.

## Evidence
- `GrappleTests` 11/11: bites the first solid face through water (x = 10.000 m); misses at 26 m, sky, water-only, unloaded chunk; reel stops
  flush at the wall, never inside (every frame), at 12.00 m/s; reel up a sheer face + hang; swing energy (> 85% ideal) and far side; Space ->
  double jump -> glide; swing landing ends the hook (0 damage, 2 m); reel into the ground 0 damage; dash/swim/cling/dug anchor/legacy Step end it; re-aim; range 0 = off.
- Fast suite `PASS 853 FAIL 0 SKIP 14` (13:33). Demo (2 launches; run 1's swing shot was weak): `Screenshots/100/run2/grappledemo_log.txt`.
- Shots looked at: run1 1-4, run2 1, 4, 5. run2's `1_site.png` looked the wrong way; that angle was reverted to run1's, so use run1's site shot.

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
