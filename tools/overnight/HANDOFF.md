STOP_FOR_OWNER -- task 138: the tilted sky probe does NOT fix the black shapes and costs ~2 ms, so it stays OFF. Question: what next for
the pure-black overhangs? (a) Recommended: raise the underground ambient floor (`_UndergroundAmbientFloor`, an art constant, 0 ms): no face
goes pure black anywhere, caves turn very dim instead of black; one look-check task with a few floor values. (b) Average two rays (vertical +
tilted): black only where both are blocked (arches stay partly black), about +2 ms. (c) A proper soft sky term (random direction per frame,
averaged over time): the right look, a bigger design task, about +2 ms. (d) Accept the black as it is.

# 138-look-tilted-sky-probe: DONE, result negative. `-skytilt 1|2` built and tested, OFF by default; look check FAILS, cost +1.9 to +3.8 ms.

## What changed (commit 25e7a66)
- `Raymarch.compute`: `SkyProbeDir` / `_SkyProbeTilt`. 1 = up faces straight up, side faces 45 deg up and out, down faces horizontal
  toward the viewer (+x if looking straight up). 2 = side faces only. Same ray count, same TraceShadowRay. 0 = before (default).
- `RaymarchFeature.SkyProbeTilt` (`-skytilt N`). CPU twin `ShadowRayReference.SkyProbeDir` / `TraceSkyProbe`.
- `SkyProbeTiltTests` (9, new): directions; ledge undercut opens; arch underside opens; sealed cave dark from every face and view;
  open cliff face open; the narrow-canyon foot and the straight-tunnel ceiling are pinned as the rule's trade-offs.
- Capture: `-skytiltqa 1` (with `-blackshape 2`) adds arms tilt / tilt2. `PlaygroundCapture.SkyTilt.cs` builds a 15 m stone block with
  a 12 m tunnel and a sealed room using edit ops. Cost-split arms `skytilt_on/skytilt2/skytilt_off`. Sheet: `tools/skytilt138.py`. TASKS.md "Task 138".

## Evidence
- Fast suite PASS 932 FAIL 0 SKIP 15 (`Logs/overnight/138-tests.out`). Filtered run first: 29/29 (`Logs/overnight/138-filter.out`).
- Look launch `Logs/lookreview_20261024_224144.txt`, shots `LookReview/2026-10-24_225411_high/bs_*.png`. Counts: `Screenshots/138/skytilt138.txt`.
  I looked at every row of the sheet plus two zooms:
  - cliff2 A: the upper part of the black crescent turns grey. The black "teeth" at its foot stay black.
  - Arches: solid black undersides become black stripes and dots, and NEW black stripes appear on the arch legs.
  - Built mouth: the sunlit face is unchanged. Step faces on the ground beside the block turn black: their 45 deg ray hits the 15 m block.
  - Tunnel and sealed room: fully black in every arm. With tilt 1 even the tunnel ceiling stays black. My guess (not checked on the CPU):
    its horizontal ray runs out of the mouth and then hits rising ground. A horizontal ray almost always hits terrain somewhere.
- The root cause: ONE yes/no ray, multiplied into both ambient and the shadow floor. Any direction choice leaves some sun-shadowed faces
  "blocked", and those go pure black. The tilt only moves which faces those are.
- Cost (warm steady state; warm-up `Logs/tg2world_20261024_231406.txt` discarded; AC; no pause): see the table below and `Screenshots/138/perf138.txt`.
  Walk arm skipped: the look had already failed, so OFF was fixed whatever the walk showed (decisions written first:
  `Screenshots/138/decisions_before_measuring.txt`).
- The warm-up's 16 "BrickDataPool exhausted" lines also appear in 137's walk logs (not new).

## Left open
- The owner's choice above. `Builds/Playground.app` is built from this tree, default unchanged (tilt 0). The look launch's edit ops stay in its ops.log (launches pass -cleardeltas).

## For the doc
| pure-black px, native 1080 | before (vertical) | tilt 1 | tilt 2 (sides) | probe off (136's target) |
|---|---|---|---|---|
| cliff2 A (ledge), sun 16 h | 35,538 | 23,229 | 23,301 | 189 |
| cliff2 A, noon | 35,345 | 19,002 | 19,074 | -- |
| 15 m closer | 210,605 | 100,425 | 100,786 | 317 |
| stone arches | 93,696 | 127,295 (worse) | 137,859 | 0 |
| built cave mouth, sun 20 | 62,821 | 92,957 (worse) | 92,957 | -- |
| sealed room | all black | all black | all black | -- |

| warm, AC, noon, FSR 3 540, p50 ms | FULL (3 passes) | tilt 1 | tilt 2 | tilt 1 / tilt 2 - FULL (noise) |
|---|---|---|---|---|
| walk_near | 64.9 / 65.2 / 64.2 | 67.5 / 66.8 | 68.5 / 68.8 | +1.9 / +3.8 (0.3 / 1.0) |
| walk_open | 37.5 / 38.2 / 37.5 | 40.2 / 40.4 | 39.2 / 41.0 | +2.4 / +2.3 (0.7) |
(`Logs/costsplit_2026-10-24_232016.txt`)

- `Screenshots/138/skytilt138.png`: one row per place (cliff2 ledge, stone arches, built cave mouth at sun 20, inside the tunnel,
  sealed room). Columns: before | tilt 1 | tilt 2 | red = pixels tilt 1 changed. Top row, right edge: the black crescent shrinks but its
  toothed foot stays. Row 2: red spreads over the arch legs, which is new black. Row 3: the red patches on the ground left and right of
  the block are step faces that turned black. Rows 4-5: all black in every column, so caves stay dark.
- `Screenshots/138/arches_native_left_tilt_right.png`: the arches zoomed, before (left) | tilt 1 (right). Look at the right arch's legs:
  vertical black stripes, and the dotted black patch at right centre.
- `Screenshots/138/mouth20_risers_native_over_tilt.png`: the ground right of the block, before (top) | tilt 1 (bottom). The black
  dashes are step faces that turned black.
- Gameplay consequence in plain words: lighting only. Nothing changes for play: tilt is OFF, so the game looks and runs as before.
