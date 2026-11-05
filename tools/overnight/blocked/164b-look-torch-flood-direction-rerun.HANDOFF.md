STOP_FOR_OWNER -- task 164b (look: torch-flood direction terms): SHEETS DONE. Two doc questions.

**Q1 "Which direction term (or flat)?" -- recommended (c) mode 2.** (a) 0 = 158's (facets) (b) 1 = central differences (c) 2 = toward the torch
on straight paths (d) 3 = flat. Mode 2 removes the facets everywhere I looked and I saw no new artefact. Mode 1 looks almost the same as 0.
Mode 3 has no facets, but it lights every face equally, so corners and the pillar's sides stop reading as shapes.
**Q2 "Flood ON by default after L3?" -- recommended (a) yes, with mode 2.** Mode 2 removes the facets and keeps sealed spaces exactly dark.
Its cost (a torch loop per lit pixel) has not been measured yet. That is a perf task, so (a) means "after L3 and a cost check".

# 164b-look-torch-flood-direction-rerun: 4 rows x 5 arms shot in one launch; mode 2 is the clear look winner; fast suite green

## What changed
- `d9afad2`: `tools/torchflood164.py` now has crop defaults that hit the facet spots (tunnel: end wall + floor; ground: the post). TASKS.md
  Task 164 paragraph gets the result. `Screenshots/164/torchflood164.txt` (lit px / mean luma per arm, plus the flood and probe log lines).
- The code is unchanged from `d2b7916`. The launch used the existing player (`SKIP_BUILD=1`). The flood stays OFF by default and `-tfdir` defaults to 0.

## Evidence
- Launch: `Logs/lookreview_20261102_002927.txt` (one launch, ~9 min). Shots: `~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/LookReview/2026-11-02_003032_high/tr_*`.
  Shells at the torch site: 0 missing after 31 s. GPU vs C# twin: max |diff| 0 levels -> PASS on every row and every mode.
- Fast suite: PASS 973 FAIL 0 SKIP 15 (`Logs/overnight/164b_fast.out`).
- What the sheets show (I looked at all 8):
  - Room: 0 and 1 have an X of hard wedges on the floor and straight bright bands along the wall corners. 2 has a smooth round pool on
    the floor and a soft glow on the wall. 3 is uniformly lit, and the corner is barely visible.
  - Tunnel: 0 and 1 have a hard vertical step on the end wall (the right half is bright) and a bright wedge on the floor. 2 is smooth. 3
    lights the side walls as much as the end wall.
  - Pillar/ground: 0 and 1 have blocky bright rectangles on the foreground slope and a hard edge on a step face. 2 has a smooth pool round
    the post, the pillar is lit only on its torch side, and no fade band is visible round the pillar. 3 lights the pillar's away side and
    the post grey.
- Mean luma (room): 158 59.7, mode 1 57.0, mode 2 50.8, flat 81.4. Mode 2 is ~15 % dimmer than 158, and flat is brighter.
- CAVEAT: the tunnel row's flooddir3 arm encoded 385 air / 326 mixed bricks, against 90 / 621 for modes 0-2. The geometry is the same:
  a deferred brick compaction finished mid-row, and `TorchFloodPool.BrickAllAir` reads the brick header. So that one tile mixes in
  trilinear air cells and is not like-for-like. The room, ground and pillar rows are consistent across arms.
- Side finding: the flood encodes still-uncompacted all-air bricks as "mixed" (64 cells, 0.2 m max) instead of "air" (8 trilinear cells).
  This is correct but blockier and bigger right after an edit. Worth a look in L3 (encode from voxel content, not the header).

## Unfinished / next
- On (c): set `-tfdir` default to 2 (one line in `TorchFloodPool`) inside L3. Measure mode 2's torch-loop cost in the next perf milestone.

## For the doc
- Facet metric (EditMode, largest direction-factor jump between 0.1 m floor neighbours): 158 0.525 -> mode 1 0.264 -> mode 2 0.018 -> flat 0.
- Sealed spaces: exactly 0 under all 4 terms. GPU = C# twin (0 levels diff) on all 4 rows x 4 modes.
- Room mean luma: 158 59.7 / mode 2 50.8 / flat 81.4 (`Screenshots/164/torchflood164.txt`).
- `Screenshots/164/row_room_crops.png`: native 2x crop of the room floor under the torch. Top middle (158) and top right (mode 1) show
  the hard X of wedges. Bottom left (mode 2) shows a smooth pool. Bottom middle (flat) is uniform.
- `Screenshots/164/row_pillar_crops.png`: the post and pillar. Top middle/right (0/1) show blocky bright rectangles on the slope. Bottom left
  (mode 2) shows a smooth pool, with the pillar lit only on its right (torch) edge. Bottom middle (flat) shows the pillar and post lit grey all over.
- `Screenshots/164/row_tunnel.png`: full frames. In the 158 tile (top middle), look at the end wall's hard bright right half and the floor wedge.
  Mode 2 (bottom left) is smooth. The flat tile has the encoding caveat above.
