# 93b1-gameplay-bld2-face-pull: DONE -- Alt + RMB drag pulls a face of the last shape: block -> line -> wall -> thick wall

## What changed (commit 58eab9d)
- `SurvivalBuild.cs`: `Shape` (the last shape's cubes + set, box, `Intact`, cap 8,192), `EntryFace`, `FaceLayer`,
  `PullCubes` (layer by layer from the face; N < 0 = the face layer and behind), `PullSteps` (aim -> N), `MaxPullCubes`,
  `PlacePull` / `DigPull` (BLD1's `PlaceStroke` / `DigStroke`: same costs, ONE `EditService.SetCubes` call).
- `Playground.Pull.cs` (new): the input, preview (yellow face layer, cyan paid / red past the run-out / orange dig / grey
  skipped; boxes past 96 cubes), commit, status lines, `-pulldemo`. `Playground.Stroke.cs`: StrokeKind.Pull, Esc cancels
  it, a BLD1 line becomes the shape; stand search moved to `FindDemoStand` (same order, -strokedemo unchanged).
  `Playground.cs`: a click-placed cube becomes the shape, `DrawBoxHighlight`, F2 help line, `-pulldemo` hook.
- `PullTests.cs` (10), CLAUDE.md commands entry, plan Done row BLD2. No TerrainGen, engine, API or layout change.

## Picks (owner may change)
- Input: hold **Alt (Option)** + **RMB drag**; release commits, Esc cancels. (No middle button on a trackpad; RMB = placing.)
- N = where the crosshair ray passes closest to the line through the face along its normal, rounded to cubes (SketchUp-like).
- The face = every shape cube exposed on that side (not only one plane), so a stepped BLD1 line on a slope pulls up into one wall.
- Pulling back in DIGS (included): the face layer first, credited like a dig stroke; it can dig past the shape into terrain.
- "Intact" = every cube's centre voxel still solid, checked on press. Otherwise the pull uses the aimed cube at the Q size,
  and that cube plus the pulled cubes become the shape.
- Pulled cubes use the SELECTED slot's block, at the shape's own cube size (Q only matters for the fallback). Torches can't be pulled.
- Caps: 64 layers, and at most 2,048 cubes / 131,072 cells per pull (= BLD1's longest stroke at 0.8 m), whole layers only.
  The pull is written in one frame like BLD1's stroke, not spread over frames.

## Evidence
- Filtered `PullTests` PASS 10 FAIL 0 (Stroke + SurvivalBuild tests passed in the same run); fast suite
  `PASS 755 FAIL 0 SKIP 14` (`Logs/overnight/93b1-fast.out`; 93b was 745). SUITE=all not needed (no `Assets/TerrainGen/` change).
- Demo `BUILD=1 ./run-play.sh EXTRA="-pulldemo"`, launch 3 (1-2 had poor sites; their logs are overwritten). Log: `Screenshots/93b1/pulldemo_log.txt`.
  The spawn's mountain has no flat stand within the loaded 120 m (best step 1.5-1.7 m), so the demo picks a flat dry 14 m
  square from the world map (880 m away, height range 0) and walks there. Pulls: line 10, wall 70, thick wall 210 cubes; hut
  floor 64 + foundation 64; walls 48/42/42/36; roof 64. Every pull came from the shape, and every slot delta = cells placed.
  The launch-2 hut on a 4.2 m slope also ran out of stack mid-foundation (158/704 cubes), stopping cleanly in layer order.
- Memory: Shape sets <= 8,192 cubes (< 1 MB). Mixed bricks: only what the player builds.
- Looked at all 9 shots (sheet below): the blocks are where the log says, nothing floats except the 0.2 m gap below.

## Not done / notes
- The 0.4 m grid row sits 0.2 m above this ground (ground top 5489, rows at 5488/5492), so part 1 shows a thin shadow gap
  under the block/line/wall. That's P6's grid snap (task 64), not the pull. The hut's pulled foundation fills it.
- No undo (none exists yet). The plan's BLD-R research row already exists (93a1). No frame-time measurement (rule 10).

## Next step
Next queued task (Q4 (a): alternate gameplay with engine). Owner check: `./run-play.sh`, F6 Survival, place a block (RMB),
then Alt + RMB drag its face.

## For the doc
Task 93b1 (BLD2): face pull. Alt (Option) + RMB drag on a face of your last block/line/wall repeats it along the face:
block -> line -> wall -> thick wall in three pulls; dragging back in digs. Keys in `./run-play.sh` (Survival): RMB click = block,
RMB drag = line (BLD1), **Alt + RMB drag = pull**, Esc = cancel, Q = cube size.
- Pull sizes, exact (EditMode `PullTests` 10/10): block +N -> line of N+1 on all 6 faces; line 8 +3 -> plane 8 x 4; plane +2 -> box
- Cost = cells placed: demo slot deltas 576 / 3,840 / 8,960 cells for line / wall / thick wall (`Screenshots/93b1/pulldemo_log.txt`)
- Per pull: at most 2,048 cubes and 131,072 cells, one EditService call; fast suite 745 -> 755 PASS, 0 FAIL
- Picks: see "Picks" above (Alt+RMB, aim -> N, inward digs, last shape while intact)
Images:
- `Screenshots/93b1/sheet.png`: 3 x 3 contact sheet in shot order. Top row: one blue-slate block on white sand, the line
  pulled from it, then the wall pull mid-drag. Middle: the wall, the thick wall (2 layers toward the camera), the hut's four walls.
  Bottom: the roof pull's preview over the hut, the hut with roof and door, both builds from above.
- `Screenshots/93b1/3_wall_preview.png`: the preview mid-pull. The yellow outlines along the bottom are the line being pulled,
  the cyan grid above is the 6 layers about to be placed, and the count under the crosshair reads "PULL +6 60 cube(s) x 0.4 m 3,840 of 3,840 cells".
- `Screenshots/93b1/8_hut.png`: the hut at eye level: 3.2 m front wall with a dug 0.8 x 2 m door, its roof edge on top, and the
  thick wall at the left edge of the frame. `9_hut_above.png`: the flat roof, and the thick wall in the foreground.
