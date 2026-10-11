# 65-gameplay-step-2: DONE. Swim/dive, double jump and dash work in play; 21 new tests green. Underwater view renders BLACK (renderer gap, not this step).

## What changed (branch `engine-phase`, commits 9ba2bca, 33248b7)
- `PlayerMotor.cs`: new `MotorInput` (+ `FromLook(move, yaw, pitch)`) and `Step(dt, in MotorInput)` = movement set A.
  The legacy `Step(dt, wish, jump)` runs the old arithmetic exactly (rigs, `PlayerMotorTests` untouched).
- `PlayerConfig.cs`: swim/air-jump/dash numbers + Validate; an old PlayerConfig.json gets missing fields appended at defaults.
- `PlayerController.DebugStep(dt, in MotorInput)`; `Playground.cs`: interactive runs (not capture, not -fluidprobe)
  use set A (Space, Left Ctrl, Left Shift); HUD dash bar + state line above the hotbar; F2 help line;
  `Playground.MoveDemo.cs` (`-movedemo`, scripted screenshots; finds a lake on the terrain map, builds a gap runway).
- `run-play.sh` key list, `CLAUDE.md` one paragraph. No frozen API, memory layout or TerrainGen change.

## Evidence
- Fast EditMode `PASS 687 FAIL 0 SKIP 14` (Logs/overnight/65-fast2.out). New `MovementSetATests` (21): swim level/up/down
  reach 3.0 m/s and never exceed it; dive follows the look (60 deg); idle swimmer floats to the 1.3 m point at the water
  line (+-0.05 m, at rest); lava cap 0.75; steam not swum; jump out at the surface; legacy Step still sinks; double jump
  once per airtime at 0.85x, reset by landing and by entering water; legacy has none; dash 2.3-2.7 m at 30/60/144 Hz,
  peak 14 m/s; dash into a 1-voxel wall at dt 1/60 and 0.1 s: 0 penetration, flush; cooldown 0.8 s; one per airtime;
  no dash swimming; new path == legacy bit-for-bit over 400 frames of walk/jump/step-up; FromLook == old walk formula; config.
- Play (`BUILD=1 EXTRA="-movedemo" ./run-play.sh`, Logs/overnight/65-demo-player.log): dive trace 3.00 m/s to 6.37 m
  deep; released, back at the water line in 5.8 s; jump 2.76 m -> double jump 4.32 m -> landed on the far runway at
  7.31 m over a 3.8 m gap; dash peak 14.00 m/s, 2.61 m. Build = 9ba2bca + the demo edits of 33248b7 (one log-format fix after).
- No frame-time measurement (owner rule). Memory: a few floats per motor.

## Top speeds (P14)
| move | top speed | where |
| --- | --- | --- |
| walk | 5.2 m/s | config; play "walk top 5.20" |
| swim, water | 3.0 m/s (oil 1.8, acid 2.6, lava 0.75, sludge 0.5) | tests + play dive "3.00" |
| idle float up | 1.0 m/s | config |
| dash peak | 14.0 m/s for 0.18 s = 2.52 m | tests + play "14.00" |
| fall terminal | 55 m/s | config (unchanged) |

## Picks (owner may change)
- Swim = Buoyancy Center probe (half the body) in a LIQUID; steam (gas) is not swum. Idle float point 1.3 m (eye 0.3 m out).
- Space at the surface (head dry) jumps out at the normal impulse; swimming locked 0.35 s after. Swimmers may step up 0.3 m.
- Thicker fluids: cap = 3.0 x water drag / fluid drag. A fast entry bleeds speed by the fluid's drag, never below the cap.
- Wading (under half submerged) keeps the old game-layer buoyancy drag.
- Double jump SETS vertical speed to 5.27 m/s (also when falling).
- Dash: level (gravity off) for 0.18 s, then keeps walk speed 5.2 along it; a wall ends it; cooldown from dash start;
  no dash while swimming; other input ignored during the dash. Direction = move keys, else look.
- Set A in interactive Survival AND Creative; rigs/captures/-fluidprobe keep the legacy step.
- HUD: a 200 px bar above the hotbar (cyan = ready) + "Shift dash ready / double jump spent / swimming in water".

## Unfinished / notes
- **Underwater is black.** With the eye inside a water voxel the raymarcher draws black (and water is opaque from above),
  so the plan's "underwater view of a lake bed" shot can't exist yet. Recommended task (renderer, needs owner approval
  as it touches the raymarch shader): when the eye is in fluid, march through fluid cells with a depth tint/fog.
- Tests use a synthetic IWorldQuery (as PlayerMotorTests do), not a ChunkStore.
- Re-run check (2026-10-11): the session was restarted after the task was done; verified fast suite ran on the final
  code (03:55:55, before commit 33248b7) and shots exist; only this HANDOFF was committed.
- The demo leaves two stone runways in that world (fresh world by default, so gone next run).

## Next step
- Task 66 = plan step 3 (ores by depth and biome x landform). Supervisor: consider queueing the underwater view.

## For the doc
Task 65: gameplay step 2, movement set A. In `./run-play.sh` (walk mode): **Space** in the air = double jump;
**Left Shift** = dash (bar above the hotbar shows the 0.8 s cooldown); in water **WASD** swims where you look,
**Space** up (at the surface: jump out), **Left Ctrl** down; let go and you float back up.
- Swim cap 3.0 m/s, dash peak 14.0 m/s over 2.52-2.61 m, walk 5.2, terminal fall 55 (tests + Logs/overnight/65-demo-player.log).
- Double jump cleared a 3.8 m gap (jump 2.76 m -> landed 7.31 m); a single jump reaches ~2.9 m.
- Dive to 6.4 m at 3.00 m/s; idle float back to the surface in 5.8 s.
- Tests: fast suite 687 passing, 0 failing, 21 new (Logs/overnight/65-fast2.out). Picks: above.
- `Screenshots/65/1_swim_surface.png` -- floating at a lake's surface at noon, eye 0.3 m above the blue water; bottom
  centre: "swimming in water  Space up  Ctrl down" and the full cyan dash bar.
- `Screenshots/65/3_double_jump_over_gap.png` -- mid-air over the 3.8 m gap, looking down at the far grey stone runway
  above the lake; HUD line "double jump spent". The black wedge under the runway is the same missing underwater view.
- `Screenshots/65/2_dive_lake_bed.png` -- 6 m under water: the frame is all BLACK except the HUD ("swimming in water").
  That is the missing underwater rendering, not a bug in the dive.
