# 103-gameplay-step-8-wall-run: DONE -- wall-run + wall-jump on PlayerMotor's abilities path, WallRunTests 8/8, fast suite green

## What changed (commit b088fbe)
- `PlayerMotor.cs`: `TryStartWallRun` / `StepWallRun` / `WallJump` / `TryWallJump` / `WallBeside` / `WallJumpReady`; state
  `WallRunning`, `WallDir`, `WallRunTimeS`, `LastWallRunStarted`, `LastWallJump`. Hand-over: climb grab is not tried while running
  (turning the keys into the wall ends the run, then the normal 0.2 s grab clings); a fired hook, a dash, swimming, landing and
  Teleport end a run; a glide that qualifies (Space held, air jumps spent, falling) is never caught by a wall; the legacy Step clears it.
  A wall-jump is tried in the jump section AFTER the ground/coyote jump and BEFORE the double jump.
- `PlayerConfig.cs`: `wallRun*` / `wallJump*` (11 numbers, clamped in Sanitize; speed 0 = no run, push 0 = no wall-jump).
- `Playground.cs`: HUD state "WALL-RUN x m/s ...", air hint "wall: Space wall-jump", stamina bar shown while running; F2 help line.
  `-walldemo` registered (damage-free like the other demos). `Playground.WallDemo.cs`: the 5 shots. CLAUDE.md task line.
- No frozen API, D.1, memory-layout or TerrainGen change.

## Picks (owner may change)
- Wall-run starts AUTOMATICALLY (no key): airborne, near the top of a jump (vy <= 2.5 m/s) or falling < 5 m/s, moving >= 3.5 m/s along
  a wall beside you with the keys along it. Speed 7 m/s, gravity x 0.2, at most 1.2 s (~8.4 m, holds about its height: -0.2 m on flat).
- A "wall" = blocks the column beside the body at 0.5 m AND 1.2 m above the feet, within 0.15 m. AXIS-ALIGNED walls only (voxel faces
  along x or z); a diagonal cliff (staircase) mostly will not hold a run. Wall-jump works off any of the 4 sides.
- Wall-jump = Space in the air against a wall: 5.5 m/s out of the face + a full jump (6.2 m/s), speed along the wall kept, the double
  jump is NOT spent. It replaces the double jump only when a wall is beside you.
- The same wall (side + plane within 3 voxels) cannot be run or jumped off twice before landing / entering fluid: bounce between two.
- Both spend the CLIMB stamina pool (run 1 s per s, wall-jump 1 s each; 7 s full): a 1.7 m chimney gives 7 bounces = ~6 m up.
- Climbing's Space still DROPS (task 93f1 unchanged); a second Space in the air then wall-jumps off that face.

## Evidence
- `WallRunTests` 8/8 + every neighbouring motor class (PlayerMotor, ClimbSlope, Glide, Grapple, PlayerVitals, PlayerConfig): filtered
  run 106/106 (`Logs/unity_test_103.log`, `TestResults_103.xml`). Run test log: 7.00 m/s top, ~8 m along in 1.2 s, stamina -1.2 s.
  Chimney test: 7 wall-jumps, then stamina 0 and the 8th press is the double jump. Same-wall press = double jump.
- Fast suite `PASS 865 FAIL 0 SKIP 14` (was 857).
- Demo (`BUILD=1 EXTRA="-walldemo" ./run-play.sh`, 3 launches: shots 3 and 4 retaken once each), log `Screenshots/103/walldemo_log.txt`:
  run 0.51 s in: 3.3 m along, +0.59 m, 7.00 m/s, stamina 6.49; wall-jump velocity (-5.5 out, 6.2 up, 7.0 along); chimney 7 jumps, 6.19 m up,
  stamina 7.00 -> 0.00.
- No frame-time measurement (feature task).

## Unfinished / next
- Not done: non-axis-aligned (diagonal / curved) walls; a wall-run sound/camera tilt. Natural cliffs are rarely axis-aligned, so
  in the real world runs work best along built walls and straight canyon faces: a possible follow-up if the owner wants it.
- Next in GAMEPLAY_PHASE_PLAN: step 9b waits on Q3; the queue's 104 (engine Q1 IFarQuery.GetVoxels) is next.

## For the doc
- Result: the player can now WALL-RUN (jump along a wall: 7 m/s for up to 1.2 s, holds height) and WALL-JUMP (Space against any wall in
  the air: kick off it; bounce up a chimney), both on the climb stamina.
- In `./run-play.sh` (walk mode): run beside a wall with W, jump -> wall-run (HUD "WALL-RUN"); Space during it or against any wall in the
  air -> wall-jump; Ctrl / letting go of W drops; turn into the wall to grab it (climb). F2 lists it.
- Numbers: run 7.0 m/s (walk 5.2), ~8 m per run, -0.2 m height on flat; wall-jump 5.5 m/s out + 6.2 m/s up; chimney 1.7 m wide: 7 bounces,
  6.2 m up, stamina 7.0 -> 0.0 s (`Screenshots/103/walldemo_log.txt`). Tests 857 -> 865 fast.
- Picks: automatic start (no key), axis-aligned walls, no same wall twice per airtime, climb stamina shared (see Picks above).
- Images:
  - `Screenshots/103/1_site.png` -- the test site: the 9 m run wall on the left, the two-wall chimney on the right, noon, sun behind.
  - `Screenshots/103/2_wallrun.png` -- mid-run, wall filling the left half; bottom centre HUD reads "WALL-RUN 7.0 m/s" and the stamina bar (6.5 s).
  - `Screenshots/103/3_walljump.png` -- 0.4 s after the kick, looking back at the run wall's end from ~2 m out, in the air; stamina 5.2 s.
  - `Screenshots/103/4_chimney_up.png` -- inside the chimney after 3 bounces, looking up between the walls; HUD hint "wall: Space wall-jump".
  - `Screenshots/103/5_chimney_top_down.png` -- the top bounce (~6 m up), looking down the chimney to the pad; stamina 0.0 s.
