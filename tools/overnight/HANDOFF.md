# 139-look-dark-overhangs-comparison: DONE (capture only, nothing shipped). Sheets in Screenshots/139/; the supervisor asks the owner.

## What changed (commit below)
- `Assets/Game/PlaygroundCapture.DarkCompare.cs` (new, `-darkcompare 1`, `-dcfloors`, `-dconly torch`, `-dcnatframes`), hook in
  `PlaygroundCapture.BlackShape.cs` (+ `BsOtherPoint` helper); `PlaygroundCapture.SkyTilt.cs`: 138's site/build split out as
  `SkyTiltBuild` (shared, unchanged behaviour). `tools/darkcompare139.py` (sheets + counts). TASKS.md "Task 139". No default/look change.
- Arms: today (High preset floor = **0.02**, not 0.05 -- the preset overrides LightingConfig's default), floor 0.12 / 0.25 / 0.40, sky check off.

## Evidence
- Fast suite PASS 932 FAIL 0 SKIP 15 (`Logs/overnight/139-tests.out`).
- Launch `Logs/lookreview_20261025_065701.txt` (shots `LookReview/2026-10-25_070057_high`, 113 shots, ~10 min incl. load). The torch rows
  came out broken (the capture's EditService had no light buffer: lights registered 0 -> 0); fixed with `LightingDriver.Attach`, re-shot only
  those rows: `Logs/lookreview_20261025_071200.txt` (lights 0 -> 2), copied in; the broken ones kept in `.../broken_torch_run1/`.
- Counts: `Screenshots/139/darkcompare139.txt` (pure black = max channel <= 4, native 1080, 136's metric).
- Looked at all three sheets + a native zoom. Main finding: **the pure-black COUNT drops to ~0 at floor 0.25, but the LOOK barely changes on
  surface overhangs**: the ledge crescent and the arch undersides still read as near-black silhouettes at 0.40 (crescent-region luma 54 -> 58,
  sky check off 65). Only "sky check off" makes them look like shaded rock -- and it also lights every cave (sealed room at noon luma 72).
- Natural cave / deep canyon row: skipped -- no quick tg2 pose exists (tg2 is a heightfield, no generated caves).
- No timing run (not a perf task). Cost from 136/138: floors 0 ms (a constant); sky check off saves the probe ray; a soft sky term ~+2 ms (138).

## Left open
- The owner's choice (supervisor asks). Builds/Playground.app is built from this tree, defaults unchanged.

## For the doc
| pure-black px (native 1080) | today 0.02 | 0.12 | 0.25 | 0.40 | sky check off |
|---|---|---|---|---|---|
| cliff ledge, noon | 35,345 | 161 | 2 | 0 | 0 |
| stone arches, noon / dusk | 87,621 / 94,209 | 37,296 / 42,406 | 1,515 / 29,333 | 0 / 28,917 | 0 / 28 |
| cave mouth, noon | 62,821 | 62,821 | 598 | 5 | 0 |
| sealed room noon (mean luma 0-255) | all black (0.0) | all black (2.0) | 118,151 (10.5) | 7,196 (23.4) | 12 (72.4) |
| tunnel / sealed room, night, no torch | all black | all black | ~all black | ~all black | 347,568 / 117,309 (luma 5-7) |
| sealed room night + 1 torch | 1,711,563 | -- | 1,537,803 | -- | 1,977 |
(`Screenshots/139/darkcompare139.txt`, `Logs/lookreview_20261025_065701.txt`)

- `Screenshots/139/dc139_surface.png`: rows = cliff ledge, stone arches, built cave mouth, each at noon and dusk; columns = today | 0.12 |
  0.25 | 0.40 | sky check off. Look at the black crescent top-right of rows 1-2 and the arch undersides in rows 3-4: they stay dark through
  0.40 and only turn into lit rock in the last column. Rows 5-6: the mouth goes from black to a dark-grey opening.
- `Screenshots/139/dc139_caves.png`: inside the tunnel / sealed room, noon and night, no torch. Rows 1 and 3 (noon): the floor makes the
  walls visible from 0.25 up; the last column lights the sealed room like open air. Rows 2 and 4 (night): black in every floor column.
- `Screenshots/139/dc139_torch.png`: one torch at night, today | 0.25 | sky check off. The torch lights only a ~1-2 m glow around itself;
  the floor changes almost nothing at night.
- `Screenshots/139/dc139_zoom_native.png`: native close-ups (noon) of the ledge crescent (top) and arch undersides (bottom) across all five
  arms, with region luma: the floor values lighten the black only to near-black.

Gameplay trade in plain words: a raised floor is free and removes the "pure black" pixels, but surface overhangs in daylight still read as
dark holes; only treating everything as open sky makes them look like shaded rock, and that also lights sealed caves as if outdoors (option D
-- dark caves with lit open overhangs -- is NOT reached by any arm; it needs a real soft sky term, ~+2 ms). Any floor ties a sealed cave's
brightness to the time of day (a sealed room is dim grey at noon, black at night). Pitch-black caves at night are what every floor gives
today; they would make light sources a real gameplay item, but one torch currently lights only a ~1-2 m patch, so dark-cave play would also
need a brighter / longer-reach torch.
