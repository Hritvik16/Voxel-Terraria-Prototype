# 93a1-gameplay-bld1-drag-line: DONE -- Survival drag line (BLD1): press, drag, release builds or digs a line of brush cubes

## What changed (commit a9ca2db)
- `SurvivalBuild.LineCubes`: the brush-grid cubes from the press cube to the release cube. It is an integer 3D DDA
  (Bresenham; grid = `v >> log2(edge)`; adds and compares only), capped at `MaxStrokeCubes` = 256.
  `Cost` gives the preview numbers, `PlaceStroke` pays cube by cube in path order and stops at the first cube the slot
  cannot pay for in full, and `DigStroke` credits the inventory as Dig does.
- `EditService.SetCubes` (ADDED, nothing changed): the whole stroke is ONE call. It runs one wake scan over only the
  cubes that changed (with the one-cell margin). In place mode it fills only Air and fluid cells.
- `Playground.Stroke.cs` (new):
  - RMB press-drag-release places the stroke. LMB held still = the tool dig, unchanged; once the view turns > 4° it
    becomes a dig stroke.
  - Preview while dragging: cyan = paid for, red = past the run-out, grey = skipped, orange = dig. A count line sits
    under the crosshair. Strokes over 96 cubes draw only the ends and the line between them.
  - Esc cancels and keeps the mouse (`PlaygroundFlyCamera.EscapeEatenUntilFrame`). Opening E/F6, or losing capture,
    also cancels.
  - The single-click guards apply per cube: cubes that would enclose you, and mobile blocks outside the wake radius,
    are skipped.
  - Torches stay one per click. F2 help has a new line; there is an entry in CLAUDE.md.
- `GAMEPLAY_PHASE_PLAN.md`: the owner's answers to Q1-Q4 (2026-10-15) are in "For the owner", P27, P22 and §4.
  BLD2 is now face pull (task 93b1). There is a new research row BLD-R (Lay of the Land). 9b now says Q2 (a) and Q3 OPEN.
  BLD1 is in the Done table.
- Frozen APIs, D.1 and memory layout are untouched. Rigs are unaffected (Creative never reaches the stroke path).

## Picks (owner may change)
1. **Dig drag:** LMB held still keeps the tool-tier dig. Turning the view > 4° makes it a dig stroke, which is instant
   (no tool-rate limit) in brush cubes. So the tool tiers only pace held digging.
2. **Place:** RMB held no longer repeats a cube every 0.22 s. Holding RMB now shows the preview, and releasing places it.
3. **Run-out:** whole cubes only, in path order (P7). The preview turns red past that point.
4. **Guards:** per cube, skip rather than refuse the whole stroke. The skipped count is shown in grey.
5. **Line endpoints:** RMB uses the air cell in front of the aimed face, LMB the aimed solid voxel. The line is straight
   in 3D, so on bumpy ground it cuts into rises (solid cells cost nothing) and floats over dips.
6. Max 256 cubes per stroke. Esc cancels without releasing the mouse.

## Evidence
- `StrokeTests` 8/8 (filter run `Logs/overnight/93a1-filter.log`). They cover:
  - lines exact on all 6 axis directions × 4 sizes, and on 10 diagonals or slopes against an integer reference;
    26-connected, no cube repeated, negative coordinates floor, cut at 256;
  - cost = cells placed (also = `VoxelsWritten`); only air is filled through ground;
  - running out stops after 3 of 10 cubes with 10 left in the slot; 63 voxels gives TooFew;
  - a dig stroke credits removed = inventory.
- Fast suite: `PASS 745 FAIL 0 SKIP 14` (737 + 8), `Logs/overnight/93a1-fast.out`.
- Demo `BUILD=1 ./run-play.sh EXTRA="-strokedemo"`, launch 4 (Player.log lines `[StrokeDemo]`/`[Stroke]`):
  - Wall base: 13/13 cubes of 0.8 m, 4,913 cells, slot 10,000 -> 5,087 (conservation OK).
  - Short slot: 5/13 cubes, 2,542 of 6,629 cells, ran out, 118 left.
  - Trench: 19 cubes of 0.4 m, 196 removed = 196 credited.
- Launches 1-3 fixed the demo, not the feature: the live mouse path overwrote and committed the scripted strokes, and
  the wall was the ground's colour. The demo now owns the stroke (`_strokeDemoOwns`) and picks a contrasting block.
- No frame times (owner rule).

## Not done / notes
- The spawn area has no flat 12 m × 6 m ground within 120 m: the flattest stand found steps 1.5 m. The trench shot is weak
  because the straight line passes over dips, so only 196 voxels were dug.
- The demo shots show the "CLICK the window to capture the mouse" banner (it is not an interactive session).
- The preview cost scans up to 256 × 512 GetVoxel calls when the aimed cube changes, or every 0.25 s. Not measured.
- Next gameplay step: BLD2 face pull (93b1), after the next engine task (Q4 (a)).

## For the doc
Task 93a1 (gameplay BLD1): in Survival you can now draw a straight line of blocks, or dig one, in a single drag.
`./run-play.sh`:
- RMB press, turn the view, release: places a line of the held block (Q = cube size 0.1-0.8 m).
- LMB held still digs as before; LMB dragged digs a line.
- Esc cancels the stroke.
- The preview shows each cube and the cell count, and turns red where the slot runs out.

Numbers:
- 10 m wall base: 13 cubes of 0.8 m, 4,913 cells placed = 4,913 taken from the slot, one stroke (`[StrokeDemo]` in Player.log)
- Short slot: 5 of 13 cubes placed, then it stopped (2,542 of 6,629 cells, 118 left in the slot)
- Dig stroke: 196 voxels removed = 196 credited
- Tests: fast suite 737 -> 745 passing (`StrokeTests` 8 new), 0 failing

Picks: see "Picks" above (instant dig strokes, no RMB auto-repeat, whole-cube run-out, per-cube skip).

Images:
- `Screenshots/93a1/1_wall_base_preview.png` -- mid-drag. Across the upper middle is a row of 13 cyan 0.8 m cube outlines, 6 m ahead; the count "13 cube(s) x 0.8 m 4,913 of 4,913 cells" is under the crosshair.
- `Screenshots/93a1/2_wall_base_placed.png` -- the same view after release: the dark basalt wall base stands where the outlines were, across the frame, on savanna grass.
- `Screenshots/93a1/3_short_slot_preview.png` -- the second course with a short slot: the left 5 outlines are cyan and the rest red; the label says "slot runs out after 5 of 13".
- `Screenshots/93a1/4_short_slot_placed.png` -- the second course stands on the LEFT part of the wall only; the status line (top left) says "ran out (4,087 more needed), 118 left".
