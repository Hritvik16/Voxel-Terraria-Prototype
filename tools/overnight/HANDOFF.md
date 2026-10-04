# 22-farstartt-integer-dda: DONE. The coast black bar is fixed. FarStartT steps an integer chunk cell. No new seams or holes in any pose.

## What changed
- `Assets/CoreEngine/Rendering/Raymarch.compute`, `FarStartT` only:
  - Amanatides-Woo over the window's chunk columns. The cell is computed once at window entry from `floor((O+D*t0)/128)`, clamped into the window, then stepped as an int2.
  - A step that leaves the window returns tLimit (as before: the window's outside is not "a non-resident column of it").
  - `D = FarSafeDir(D)` so an axis-parallel ray can't pick a -inf face.
- `CLAUDE.md`: the PREAMBLE rule 9 bullet (warm steady state), verbatim, added to Measurement discipline. The black-bar index line now says FIXED.
- `KNOWN_ISSUES.md`: the black-bar entry is marked FIXED, with the evidence and the FarMarch finding.
- One commit (see `git log`). `Playground.unity` and the queue deletions in the tree are not mine; they were left out.

## Evidence
- Fast suite: before (HEAD shader) PASS 611 FAIL 0 SKIP 12 (`Logs/overnight/22_suite_before.out`); after, the same (`22_suite_after.out`). Build: 0 error CS, 0 Shader error.
- Probe, `BUILD=1 MODE=poses MAPV=4 EXTRA="-coastprobe"` (`Logs/tg2world_20261004_063855.txt`):
  - Bar pixel (500,720): `FarStartT used 155.1 m` (was 230.9 m). Far hit at 190.4 m, normal (0,1,0), 13 steps. It now matches the control pixel (154.0 m, also (0,1,0)).
  - `coast_x1chunk` gives the same 155.1 m. `coast_yaw180` gives 147.9 / 146.8 m, all hits (0,1,0).
- Dark-run check, rows 700-740, dark = R<12 G<20 B<35 (`Logs/overnight/22_coast/darkrun.py`):
  - Before (`FarBand/2026-10-04_023245_world`): 915-916 px runs in all 41 rows.
  - After (`FarBand/2026-10-04_064124_world`): longest run 0 / 1 / 2 px across the three coast poses x golden/noon. Isolated pixels, no bar.
- Full `MODE=poses MAPV=4`, no probe (`Logs/tg2world_20261004_064339.txt`, shots `FarBand/2026-10-04_064529_world/`): I compared all 61 shots against the pre-fix v4 run `2026-10-04_021148_world`.
  - 52 are pixel-identical (mean diff 0.0): caldera, cliff feet, crystal, eye levels 1 and 3, geyser, glide, ledge, map, oblique, peak, rivers, summit, volcano. No new seam, hole or streak anywhere the far band takes over.
  - Contact sheets: `Logs/overnight/22_coast/22_sheet0..5.png`. Each pair is BEFORE (left) | AFTER (right), with the file name in the label.
- Images:
  - `22_sheet1.png`, top-left pair and the noon pairs: the black bar over the left sea (about 2/3 down) is gone in AFTER. The thin dark arcs below it remain; they are the known voxel LOD tier seams.
  - `22_sheet1.png`, view10 pair (top-right, second row): the blue "immediate far hit" rectangle at bottom-left is gone; the sea is uniformly yellow.
  - `22_sheet2.png`, `eye_level_2_golden` / `_noon` (rows 2-3, left): a black slab on the lake right of centre is now blue water. This is a second instance of the same stall, also fixed. The same holds for `22_sheet3.png` `minimap_check_noon` (row 5, left).
- FarMarch check (asked; NOT changed): `tn = tExit + 1e-3*cs` cannot hard-stall. It sets `t = tn` and `tExit = max(min(tEx), t)`, so t advances at least 0.008 voxels every step.
  - A crossing that rounds back onto the face costs extra steps instead: about 0.24/|D.axis| of them at cs = 8, x ~ 40000. Those steps are zero-length segments, so they can't create false hits.
  - Only near-grazing rays pay that cost; the probe pixels took 13-17 far steps.
- Frame time: NOT measured. Per the task, no rig and no claim. The per-pose ms in the logs are diagnostic only, and both runs were on battery.

## Unfinished / next
- Nothing from this task. Optional, not queued: a rig A/B for FarStartT cost. It should be cheaper (at most ~64 cells, versus 96 when it stalled). It is low value.
- Owner decisions carried from 27/28 are unchanged (see `git log` for 28's handoff).
