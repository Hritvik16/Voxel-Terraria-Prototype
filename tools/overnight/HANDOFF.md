# 93a1-gameplay-bld1-drag-line: DONE -- Survival drag line (BLD1): press, drag, release builds or digs a line of brush cubes

## What changed (commit a9ca2db)
- `SurvivalBuild.LineCubes`: integer 3D DDA on the brush grid (`v >> log2(edge)`, adds/compares only), <= 256 cubes.
  `Cost` = preview numbers; `PlaceStroke` pays cube by cube in path order, stops at the first unpayable cube; `DigStroke`
  credits like Dig. `EditService.SetCubes` (ADDED): the stroke is ONE call, one wake scan over the changed cubes only.
- `Playground.Stroke.cs` (new): RMB press-drag-release places; LMB still = tool dig as before, turned > 4 deg = dig stroke.
  Preview: cyan paid / red past run-out / grey skipped / orange dig, count under the crosshair (> 96 cubes: ends + line).
  Esc cancels and keeps the mouse (`PlaygroundFlyCamera.EscapeEatenUntilFrame`); E/F6/lost capture also cancel. Per-cube
  guards (enclosing you, mobile outside the wake radius) skip that cube. Torches stay one per click. F2 line, CLAUDE.md entry.
- `GAMEPLAY_PHASE_PLAN.md`: owner's Q1-Q4 (2026-10-15) in "For the owner", P27, P22, §4; BLD2 = face pull (93b1); new
  research row BLD-R (Lay of the Land); 9b: Q2 (a), Q3 OPEN; BLD1 in Done. Frozen APIs/D.1/layout untouched; rigs unaffected.

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
- `StrokeTests` 8/8: lines exact on 6 axis directions x 4 sizes and 10 diagonals/slopes vs an integer reference, 26-connected,
  no repeats, negative coords floor, cut at 256; cost = cells placed (= `VoxelsWritten`); air-only fill through ground; run-out
  stops at 3/10 cubes (10 left); 63 voxels -> TooFew; dig removed = credited. Fast suite `PASS 745 FAIL 0 SKIP 14` (737 + 8),
  `Logs/overnight/93a1-fast.out`.
- Demo `BUILD=1 ./run-play.sh EXTRA="-strokedemo"` (launch 4, Player.log `[StrokeDemo]`): wall base 13/13 cubes of 0.8 m,
  4,913 cells, slot 10,000 -> 5,087 (conservation OK); short slot 5/13 cubes, 2,542 of 6,629, 118 left; trench 19 cubes of 0.4 m,
  196 removed = credited. Launches 1-3 fixed the DEMO (live mouse path committed its scripted strokes; wall same colour as the
  ground): the demo now owns the stroke and picks a contrasting block. No frame times (owner rule).

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
