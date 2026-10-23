# 125-look-sea-horizon-line: DONE. The line is the FAR BAND's square, not the world's edge. It is fixed and ON by default (`-edgefade 0` = old).

No owner decision is needed.

## Verified: which edge it is
- New capture option `-lookedge 1` (look review). For each heading it logs the row where a flat sea would end at the world's edge and at the band's square (`Screenshots/125/lookreview.txt`).
- Old stills (`_old`), High preset, noon, high pose (606 m over the sea, pitch -4 deg):

  | heading | world edge (row) | band square (row) | line seen |
  |---|---|---|---|
  | +x / -x / +z | 4.1 / 3.9 / 4.3 km (611 / 621 / 604) | 5,632 m (575) | row 575 at all three |
  | diagonal | 5.9 km (571) | 8.0 km (546) | row 546, drawn as the square's CORNER (a peak at the frame centre) |
  | pose edge_x, 2 km from the world's +x edge, looking +x | 2.0 km (753) | 575 | row 575, nothing at 753 |

- Past the world's edge the far band keeps drawing sea, so the world's edge is invisible.

## Cause and fix
- Task 121 faded the haze to full at the square. But two things are applied after the haze:
  - the water's see-through blend (`SeeCompose` with `SeeReflection`, which reflects the sky about 6 deg UP: bluer and darker);
  - the grade's saturation.
- So the sea at the edge stayed about 128 -> 112 levels off the sky.
- Fix, in `Raymarch.compute` (far-shade kernel):
  - The same fade (`DrawnEdgeAmount`, the last 45% of the square) now comes out of `FarShadeApplyHaze`.
  - It becomes the pixel's LAST step (`DrawnEdgeFade`): after both see-through blends, and never inside a fluid.
  - It fades toward exactly the colour a miss shows there (`LookShoulder(LookAtmosphere)`, no sun disc), blended in the look's gamma space.
- Flag: `RaymarchFeature.EdgeFade`, `-edgefade`. It needs `-skyhaze`, so `-skyhaze 0` is still the pre-121 haze. It reaches the shader as `_SkyLook` bit 2.
- Tests: `DrawnEdgeFadeTests` (3). TASKS.md has a paragraph. New tool `tools/line-step.py` measures the step at the line.
- Commit: `3198577`. No API, layout or terrain-generator change. Dusk sky strength not touched.

## Evidence
- Fast suite: `PASS 917 FAIL 0 SKIP 15` (`Logs/overnight/125-tests.out`). The filter run of my tests plus `SkyLookTests` / `FarRockSlopeTests`: 9/9.
- Look launch: High preset, FSR 3 at 720 rows, poses high + edge_x, 45 s shell cap.
  - Log: `Logs/lookreview_20261023_074126.txt`. No `SHADER RUNTIME ERROR`.
  - Shells were still missing at the cap: s3-s7 at high, s3-s7 at edge_x (see the `[shells]` lines).
- Step across the line, in green levels over 3 rows at the left / right edge columns (`Screenshots/125/line_step.txt`):
  - noon, every heading, both poses: 19 -> 2.3-3.0. The new figure is the sea texture's own noise.
  - golden away: 11.0 / 3.8 -> 1.8 / 1.8.
  - golden toward the sun: 6.8 / 2.7 -> 1.5 / 1.3.
- Old vs new pixels first differ at row 574 for +x / -x and row 546 for the diagonal. These are the square's rows. Lower rows also change, because shells kept streaming between the two shots.

## Left open
- Golden hour, toward the sun, high pose: in the `_old` still, nearer sea within about 3.2 km is a darker rectangle with a straight far edge. It is probably the shell-water vs far-water seam.
  - That `_old` shot was taken 60 frames after the new one, while shells were still streaming. So this task neither fixes it nor tests it.
  - Next step: a look task with a still taken after the shells fill.
- The fade starts at 3.1 km (unchanged from task 121). From high up the sea now brightens into the haze over about 25 rows instead of stepping. That is the intended look; owner's taste.

## For the doc
- Where the line is, high pose at noon: row 575 at +x / -x / +z, row 546 diagonal. The band's square predicts 575 / 546; the world's edge predicts 611 / 621 / 604 / 571. Source: `Screenshots/125/lookreview.txt`.
- Step across the line, noon: 19 -> <= 3 green levels per 3 rows (`Screenshots/125/line_step.txt`).
- Step across the line, golden hour: 11 -> 2 levels away from the sun, 7 -> 1.5 toward it (same file).
- Images:
  - `Screenshots/125/edge_sheet.png`: LEFT old, RIGHT new; four rows, all cropped to rows 300-800.
    - Row 1, edge_x looking +x (2 km from the world's edge): the hard sea/sky line at the old frame's 1/4 height is a soft gradient on the right.
    - Row 2, high pose diagonal: the old frame shows the far band square's corner (a peak at the frame centre); on the right it is gone into haze.
    - Rows 3-4, golden toward / away: the hard horizon line is gone on the right. Ignore the darker near-sea rectangle in row 3 left (streaming, see Left open).
  - Full frames: `Screenshots/125/<pose>_edge_<heading>[_old].png`.
