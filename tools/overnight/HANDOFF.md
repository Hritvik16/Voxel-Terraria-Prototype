# 131-qa-q83-cliff-shadow-picture: DONE (capture only). The Q83 bug SHOWS in play: standing in a far cliff's shadow, the ground round the player (the 0.1 m window, a ~205 m square) is drawn LIT, and the shadow starts at the window edge, ~100-115 m away.

## What changed (commit 28f5fec)
- `Assets/Game/PlaygroundCapture.CliffShadow.cs` (new, `-lookreview -cliffshadow 1`) + one hook in `PlaygroundCapture.LookReview.cs`. No default, look or API change.
  It searches a 10 m generator heightfield (+-2.5 km round spawn) for cliff feet in a far shadow, at afternoon sun 30/25/20 deg, then per spot takes
  A (eye 1.6 m, looking at the cliff), C (250 m up, looking down, only the 0.1 m window kept on A's eye, as 129's REF), B (300 m back).
- `tools/cliffshadow131.py` (sheets + numbers; decodes debug view 14: orange = tier 0, blue = shells). TASKS.md "Task 131".
- Text reports committed: `Screenshots/131/lookreview.txt`, `cliffshadow.json`, `sheets.txt`. Images are not committed.

## Evidence
- ONE launch, High preset, play look (FSR 3 dyn): `Logs/lookreview_20261023_165137.txt`, ~13 min incl. build wait. Shots in
  `~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/LookReview/2026-10-23_165635_high/`.
- Fast suite PASS 921 FAIL 0 SKIP 15 (`Logs/overnight/131-tests.out`).
- Shell fill (short fixed waits, as the launch budget asks): A shots had shells 1-2 full (to 275 m, all the ground that matters), s3-s7 partly missing
  (cliff1 1,327 missing, cliff2 1,617); B shots all full. Missing far shells only touch the background behind the cliffs.
- Spots found: sun 30 deg (15.95 h) chosen for both (best contrast); 22 / 42 / 51 candidates at 30 / 25 / 20 deg.
  cliff1 Q (5217, 530, 4769): shadow cast 260 m away by terrain up to 173 m above; window edge 114 m toward the sun, band ceiling 46 m up.
  cliff2 Q (3637, 550, 5219): caster 230 m away, up to 152 m above; window edge 108 m, ceiling 65 m up.
- Looked at every shot (both sheets + crops):
  - cliff1 (snowfield under a 170 m coastal cliff): A = bright white terraced snow out to a straight line ~100 m ahead, grey shadowed ground beyond it.
    C = a bright square (the window) inside the grey shadow, player in it; the shadow outside is continuous. B = the ground round Q is grey (shadowed).
    Numbers (`sheets.txt`): C luma just inside the window edge 195 vs just outside 127; A tier-0 ground luma 146 vs shells 70.
  - cliff2 (sand + grass under a steep valley wall): weaker. The shadow covers only the window's top (toward-sun) edge there, so C shows a straight
    shadow edge exactly on the window line: dark sand above, bright sand below (crop). Overall edge luma 116 vs 114 (the shadow covers only part of
    the edge). A: a near rock wall is itself inside the window, so A mostly shows near-side lighting; the bug is not clear in A here.
  - Unrelated, seen in cliff2 A/C: a pure-black crescent on the near rock wall (tier 0, an overhang underside?). Not investigated.
- How often (grid stats, dry land within 2.5 km of spawn, own ground shadowed by terrain >= 110 m away = drawn lit): 1.8% at sun 30 deg, 3.0% at 25,
  4.8% at 20; ~0 at noon. Lower bound: casters inside the window but above the band ceiling (~45-65 m up) also count and are not in it.
  The straight cut is visible whenever a far shadow edge falls within ~110 m of the player, which is more often than standing in one.

## Left open
- No fix (by design). Next: the supervisor asks Q83 with the sheets below.
- The black crescent on cliff2's rock wall: a separate look issue, maybe worth a QA pose (`Screenshots/131/cliff2_sheet.png`, A, top right).

## For the doc
| | cliff1 (snow, 170 m cliff) | cliff2 (sand, 150 m wall) |
|---|---|---|
| sun / caster distance / window edge toward the sun | 30 deg / 260 m / 114 m | 30 deg / 230 m / 108 m |
| C luma just inside vs just outside the window edge | 195 vs 127 | 116 vs 114 (top edge only 95 vs 92) |
| A: tier-0 ground luma vs shells | 146 vs 70 | 63 vs 74 (near wall in view) |
| dry land drawn lit in a far shadow (sun 30 / 25 / 20 deg) | 1.8 / 3.0 / 4.8 % | |
- `Screenshots/131/cliff1_sheet.png`: row 1 left (A): white snow to ~100 m, then grey: the shadow should start under your feet. Row 2 left (C): the
  bright square inside the yellow outline is the 0.1 m window, the red ring the player; everything outside it is in the cliff's shadow. Row 1 right (B):
  from 300 m back the ground round the stone ring (near Q) is grey, correctly shadowed.
- `Screenshots/131/cliff2_C_crop_window_edge.png`: from above; the dark band at the top ends on a perfectly straight line, which is the window edge,
  not a shadow shape; below it the sand is lit.
- `Screenshots/131/cliff1_AB_strip.png`: top = A's middle strip (bright terraced snow, grey beyond a straight line); bottom = B's (same ground, grey).
- Options for Q83: **(a) fix task**: continue tier-0 sun rays that leave the window (or the band ceiling) into the shell march. Exact, same image
  elsewhere. Cost ESTIMATE, not measured: 93b4's shell shadow costs ~2.4 ms for shell-hit pixels; tier-0 pixels are 52-61% of these A frames and
  every lit one would march, so ~+2-5 ms at 540 rows (130: the march runs ~2x dearer inside the big kernel, so it could fold into R1's pre-pass).
  **(b) leave it**: rare under a high sun (~2% of land at 30 deg, ~0 at noon), but a visible straight shadow cut ~100 m away in low sun.
