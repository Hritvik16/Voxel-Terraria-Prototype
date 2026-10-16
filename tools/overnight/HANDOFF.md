# 93c1-gameplay-step-4: DONE -- play starts in the best easy area; R goes back there; KEEP=1 resumes where you stood

## What changed (commit b55743e)
- `Assets/TerrainGen/PlayStart.cs` (new, read-only, no generator change): `BestArea` = the largest B4 easy area, areas
  >= 90% of it tie and the one nearest the island centre wins; `Find` = that area's first cell (nearest its centre first)
  that is FLAG_EASY, Walk band, not sea, map-cell DRY, above the sea with no still water over the column, and whose 8
  neighbours pass too (else the first that passes alone). v3 has no easy areas -> false -> Tg2Spawn.
- `Assets/CoreEngine/Gameplay/PlayerPose.cs` (new): `player_pose.json` beside player.json (P12), refuses bad/other-map files.
- `Phase4Bootstrapper.cs`: interactive Playground tg2 sessions start at PlayStart; `-keepdeltas` (KEEP=1) loads the pose;
  `-worldseed N` = another tg2 island in its own folder (`Phase4World_tg2_sN`; 42 unchanged).
- `Playground.cs`: start pose searches from the play start; R = back to the start (fly while it streams, then walk);
  F still goes to the arena; pose saved every 30 s + on quit (only after the start pose settled); F2 help R line;
  `-playstart 0` = old behaviour; `-playshotquit` quits after `-playshot` (and hides F2 for that shot).
- `run-play.sh` key list/comments, `CLAUDE.md` commands entry, plan Done row 4.
- This run continued an earlier attempt of 93c1 that hit the usage limit at 23:36 with the code written but uncommitted;
  I reviewed it, fixed F (it would have searched from the start, not the arena), added -playshotquit, tested, committed.

## Picks (owner may change)
- "Best" = largest, then most central (the 4 areas per seed are all ~44 ha, so most-central decides on every seed tried).
- Stand on an INTERIOR cell (all 8 neighbours easy/Walk/dry), so you look out over easy ground, not off its rim.
- R in play = back to the start (not "respawn on the surface where you are"); outside play R is unchanged.
- KEEP=1 resumes at the saved pose (feet, look, walk/fly); a fresh world (default) always starts at the play start.
- Arena (fluid basin) is now searched around the start, as it was around Tg2Spawn before.

## Evidence
- `PlayStartTests` (3 seeds, Slow) + `BestArea` rule + v3 none + `PlayerPoseTests` (2): filtered PASS 7 FAIL 0.
  Starts: seed 42 column (26680, 30200) ground 6317; seed 7 (32440, 28600) ground 5300; seed 2026 (39400, 51160) ground 5533;
  all interior. Fast suite PASS 758 FAIL 0 SKIP 14 (`Logs/overnight/93c1-fast.out`, was 755); SUITE=all PASS 808 FAIL 0
  SKIP 23 (`Logs/overnight/93c1-all.out`).
- 4 short launches (logs `Screenshots/93c1/log_*.txt`): s42 and s7 fresh -> "start pose: play start (easy area)", eye on
  the picked column; KEEP=1 with the saved pose moved +20 m x / +8 m up / yaw 225 -> "resumed saved pose", fell to the ground,
  saved y 631.1 on quit; `-playstart 0` -> the old arena start (eye 891.9 m, mountain top).
- R was not pressed in a launch (no input in unattended runs); its path = the start-pose search, which the launches exercise.
- No frame-time measurement (rule 10). Memory: nothing resident added (a pick at start-up, a ~200 B JSON).

## Next step
Next queued task. Owner check: `BUILD=1 ./run-play.sh` -> you stand on easy ground; walk away, R brings you back;
quit, `KEEP=1 ./run-play.sh` resumes there. `EXTRA="-worldseed 7"` for another island.

## For the doc
Task 93c1 (gameplay step 4): play starts on the best easy area of the island instead of a mountain top.
- Start height seed 42: eye 891.9 m on a steep peak (old Tg2Spawn) -> 633.5 m on flat interior easy ground (`Screenshots/93c1/log_before.txt`, `log_s42.txt`)
- Start cell on 3 seeds (42, 7, 2026): FLAG_EASY, Walk band, dry, 8 easy neighbours, same pick twice (`PlayStartTests` 3/3)
- KEEP=1 resume: saved pose -> resumed pose exact (2688.1, 639.9, 3020.1 m, yaw 225) (`Screenshots/93c1/log_keep.txt`)
- Suites: fast 755 -> 758 PASS, all 808 PASS, 0 FAIL
- Keys in `./run-play.sh`: **R = back to the start**; KEEP=1 resumes where you quit; `EXTRA="-worldseed 7"` another island.
Picks: largest then most central area; interior cell; R = back to start; KEEP=1 resumes the pose (see Picks above).
Images:
- `Screenshots/93c1/before_tg2spawn_seed42.png`: BEFORE (`-playstart 0`). Look at the whole frame: you start on a yellow-brown
  mountain top 380 m above the coast, steep slopes falling away on all sides.
- `Screenshots/93c1/start_seed42.png`: AFTER, seed 42, first frame. Look at the ground below the F2 panel: gently terraced
  white (snow) ground, open to the horizon, ridges far off; top-left status "standing at the start (the best easy area)";
  the F2 panel shows the new "R back to the start" line.
- `Screenshots/93c1/start_seed7.png`: AFTER, seed 7 (help hidden). Look at the lower half: an open green plain with a sand
  edge on the right, a mountain ahead; a round terraced hill in the middle distance.
- `Screenshots/93c1/resume_seed42.png`: KEEP=1. Look at the top-left status "resumed where you left off (KEEP=1)": the
  saved pose (moved 20 m east, facing 225 deg) on the same snowy terraces.
