# 221-look-lighting-off-pictures: DONE -- pictures of each lighting switch off (sky fill, torch flood, sun shadows, all three) at 4 poses
## What changed: no code. TASKS.md "Task 221" (commit below). Runner `Logs/overnight/221/run.sh`, sheets/diff `Logs/overnight/221/sheets.py` (Logs git-ignored).
## How: release player (219's build), play's flags, High, 432 rows, shell wait 60 s, `-costtimed 1` (stills only; NO timing quoted -- the ms below are 212's).
Poses: walk_near, forest_eye, jungle_eye (219's, noon, sun behind) + torch_camp (task 151's 32-torch night scene, 28 placed, flood I 1.6 k 0.35).
NOT one launch: 7 (the tool cannot stack arms; listed in run.sh): day (A,B,C,D) 81 min, dayE (E) 44 min, night/nightE/nightA/nightB 4-5 min each,
jD (jungle reshoot) 21 min. Day launches far over the ~12 min budget: every arm re-settles at 6-9 fps at 3 still poses.
Wasted: one dayE start (killed after 10 min) -- `-shadowlevel 3` on the launch line is NOT applied in `-costsplit` runs (header said rung 0).
Night: the torch scene turns the flood OFF in every arm except `tf_on` (task 158), so night A = `tf_on`, B = `-skyfill 0` + `tf_on`, C = FULL.
## Diff vs A (mean |d| 0-255 / % px > 32; `Logs/overnight/221/diff.txt`). Noise: 219 cross-launch 2.2/0.35, 2.2/0.12, 1.2/0.21; here B vs BC 0.9/0.03, 2.1/0.36, 0.7/0.08; night 0.8/0.01
| pose | B sky fill 0 | C flood off | D sun off | E all off |
|---|---|---|---|---|
| walk_near | 3.8 / 0.96 | 2.6 / 0.38 (noise) | 2.7 / 0.47 (~noise) | 4.5 / 1.26 |
| forest_eye | 8.0 / 7.4 | 3.4 / 0.56 (noise) | 10.5 / 13.8 | 11.4 / 10.8 |
| jungle_eye | 5.2 / 6.4 | 1.5 / 0.48 (noise) | moved, no diff | 11.7 / 15.8 |
| torch_camp (night) | 7.3 / 1.55 | 7.7 / 9.8 | n/a (sun -76 deg; 1.2/0.08) | 9.4 / 12.5 |
## What each switch loses (looked at every image)
- B sky fill 2 -> 0: shade loses its soft sky/ground-bounce light. Forest: tree trunks and crown undersides go near BLACK, the rock's shadow
  goes darker. Jungle: the already-dark trunks go black. walk_near: the shade under the ledge turns smoother and lighter, the dark speckle
  goes, faint vertical stripes show on the cliff. Night: the slopes away from the torches get slightly bluer.
- C torch flood off (back to the old point-light torches): by DAY NOTHING (all three at the noise level -- no torches in view; 212 measured
  the flood twin's 27.8 ms at walk_near, noon, no torches). At night: torch light gets harder and patchier -- black notches between voxels
  on the lit slope in front, light no longer wraps evenly over the steps (9.8 % of pixels change).
- D sun shadows off: no cast shadows anywhere. Forest: no tree shadows on the grass, trunks look pale grey, the whole scene goes flat.
  walk_near (noon, canyon): only the dark crease under the rock lip goes. Night: nothing (sun below the horizon).
- E all three off: flat and shadowless; forest trunks dark grey; jungle trunks dark brown/black with an evenly lit floor (no dappled light);
  night = C's harder torch light plus B's bluer far slopes.
## Cheapest look loss per ms (ms = task 212, High, walk_near, 432 rows, warm; not re-measured)
| arm | ms saved (212) | what you lose |
|---|---|---|
| C torch flood off | 27.8 | by day nothing visible; at night harder, patchier torch light (old point lights) |
| D sun shadows off | 28.1 | every cast shadow; forests look flat (13.8 % px) |
| B sky fill 0 | 40.4 | soft light in shade; trunks/undersides go black in forests (7.4 % px) |
| E all three | not measured stacked (212's "ceiling" is a different stack: 24.6 ms left) | all of the above |
Per ms the cheapest loss is C (nothing by day, a night-only change). Not a recommendation -- the owner picks.
## Unfinished / flagged
- jungle_eye D is broken: camera came out 2 m higher (minimap alt 20 vs 18 m); the reshoot (`jD`) came out at 33 m on top of the canopy. The
  pose is not repeatable across arms (survival player pushed out of a trunk?). Shown flagged in the sheets; E (alt 18) shows sun-off there.
  Not diagnosed (capture-only task). No D with the flood exists at night (not needed: sun below the horizon).
- Memory (rule 11): no code, nothing new stored. Ledger at jungle_eye (all arms the same build): GPU pool full, refused 4,346 (day) / 1,340
  (dayE) / 2,476 (jD) -- the known jungle refusals (201a/216), varies by launch; CPU pool peak ~494-496 k vs valve 484.5 k. Night: 0 refused.
Tests: none run (no code changed).
## Next: owner answers Q-AN (cut lighting / lower target / stronger machine) with these pictures; 220 waits on it.

## For the doc
- 212's cost (High, walk_near, 432 rows) vs what turning it off shows: sky fill 2 40.4 ms -> shade goes black under trees; torch flood 27.8 ms
  -> no change by day, harder torch light at night; sun shadows 28.1 ms -> no cast shadows, flat forest.
- Pixels changed vs play (forest_eye, % > 32): sky fill 0 7.4 %, flood off 0.6 % (noise), sun off 13.8 %, all off 10.8 %. Night: flood off 9.8 %.
- `Screenshots/221/sheets/doc_sheet.png`: rows A play / B sky fill 0 / C flood off / D sun off / E all off; columns walk_near, forest, jungle, night.
  Look at column 2 (forest): row B's black trunks, row D's missing tree shadows; column 4 (night): row C's harder, notched torch light.
- `Screenshots/221/sheets/forest_eye_arms.png` (full size, 2 x 3): the clearest pose -- compare A (top left) with B (top right) and D (middle right).
- `Screenshots/221/sheets/torch_camp_arms.png`: night, A flood (top left) vs C point lights (middle left): look at the lit slope bottom left.
