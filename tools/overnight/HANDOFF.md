STOP_FOR_OWNER -- no preset reaches 30 fps on this Mac. Question: (a) cut lighting features (sky fill 2 ~40 ms, torch flood twin ~28 ms,
sun shadows ~28 ms at walk_near), (b) lower the target, or (c) a stronger machine? 220 (keep the budget) needs this answer first (it reads "the chosen floor preset").

# 219-perf-feasibility-ladder: DONE -- every arm fails; the presets barely change frame time (108-111 ms at walk_near)
## Plan written before measuring: none passes -> owner question; Low only -> floor; a middle one -> the answer; 540/720 pass -> rows are the lever.
## What changed: commit 743f139 -- capture only, opt-in (`PlaygroundCapture.CostSplit.cs`): `-costfixed` still poses, `-costarms none`, a `[mem]`
line per path. No engine code. Runner `Logs/overnight/219/ladder.sh`, table/pictures `table.py`, `sheets.py` (Logs are git-ignored). TASKS.md "Task 219".
## How: release player, warm-up `Logs/tg2world_20261116_123335.txt` (discarded), then 7 launches back to back (12:39-15:02, AC). Play flags
(trees, shells, torch flood, sky fill 2, FSR 3) + `-quality X -renderrows R`. Rows 432 = play's dyn floor (`-dynfloor 432`): dyn sits there whenever
a frame is > 30 ms, so 432 IS what play shows on every failing arm. Poses: walk_near (212's, moving), forest_eye and jungle_eye (task 210's, eye
height, noon, looking -Z = sun behind); trees ON (218's fix). One 20 s FULL run per pose; shell wait 60 s (shells never complete; forest/jungle had a
streaming deficit of ~520-950 chunks at the pose). Launches took ~20 min, not 10: at 6-9 fps every settle wait counts frames.
## Table (warm steady state; peak = process peak so far in the launch; full table `Logs/overnight/219/table.md`)
| arm | walk_near p50 / p99 / %>33 | forest_eye p50 / p99 / %>33 | jungle_eye p50 / p99 / %>33 | peak MB | GPU refused | works? |
|---|---|---|---|---|---|---|
| Ultra 432 | 109.8 / 242.7 / 85 | 155.7 / 260.5 / 100 | 115.5 / 201.9 / 94 | 3,122 | 0 | no: p50, tail, memory |
| High 432 | 111.4 / 233.1 / 88 | 148.4 / 247.0 / 100 | 109.5 / 196.3 / 94 | 2,607 | 555 | no: p50, tail |
| Medium 432 | 108.3 / 232.7 / 81 | 141.1 / 242.0 / 100 | 109.7 / 195.3 / 95 | 2,395 | 612 | no: p50, tail |
| Low 432 | 108.0 / 231.3 / 81 | 147.4 / 247.4 / 100 | 114.9 / 199.5 / 86 | 2,297 | 677 | no: p50, tail |
| Ultra 540 * | 169.1 / 306.5 / 100 | 214.3 / 330.2 / 100 | 187.3 / 217.5 / 98 | 3,148 | 0 | no: p50, tail, memory |
| Ultra 720 * | 273.3 / 440.0 / 100 | 383.9 / 464.5 / 100 | 304.7 / 418.8 / 100 | 3,223 | 0 | no: p50, tail, memory |
| Ultra 432 repeat * | 148.7 / 305.9 / 96 | 197.5 / 343.0 / 99 | 141.4 / 385.8 / 96 | 3,122 | 0 | (control) |
\* NOT CLEAN: keyboard/mouse input 14:13-14:53 (`Logs/overnight/219/idlewatch.txt`) = the owner used the Mac during these. The repeat came out 22-35 %
slower than the first Ultra run, so these three are inflated by an unknown share. The first four (12:39-13:58) had one input event (13:42, low's
walk_near; its 108.0 matches the others). Ultra 540's walk_near (14:02) was before the input and is 1.54x Ultra 432 for 1.56x the pixels.
No rerun: every outcome of a rerun gives the same answer (all arms are 3x+ over budget).
## Picture quality (`Screenshots/219/<arm>/<pose>_full_clean.png`, full size; diff vs Ultra 432 in `Logs/overnight/219/diff.txt`)
Mean |diff| (0-255) / pixels differing > 32: High 2.3/0.3 %, 2.9/0.7 %, 1.3/0.4 %; Medium 2.6/0.4, 3.2/1.1, 1.4/0.4; Low 2.4/0.4, 3.3/1.2, 1.3/0.5
(walk / forest / jungle). NOISE FLOOR: Ultra vs its own repeat 2.2/0.35, 2.2/0.12, 1.2/0.21 -- so at walk_near and jungle the preset differences are at
the frame-to-frame (FSR jitter) level; only forest_eye clearly exceeds it. Looked at all of them: what a step down from Ultra loses is the 0.1 m detail
in the ~40-80 m band (Ultra's 0.1 m area reaches 116 m vs High 82 / Medium 65 / Low 59 m): far birch crowns behind the first trees turn speckled with
grey gaps, and the shadow under the rock lip at walk_near becomes blotchy dark speckle. High, Medium and Low look almost alike; sun shadows, torch
light, tree density, sky and far hills are the same in all four. 540 / 720 rows: slightly sharper fine edges (minimap text, leaf edges), nothing else.
## The three answers
(a) BEST setting that works on this Mac: none. The best-looking playable compromise is High (the auto pick): its look is Ultra's minus far 0.1 m detail,
it stays under 3 GB (2,607 MB) where Ultra does not (3,122 MB), but it runs ~110 ms (9 fps) at walk_near.
(b) What works: nothing -- every arm misses p50 and the tail; Ultra also misses memory. Lowering the preset does NOT help: Low is 108 vs High 111 ms,
because the presets size the shells (range, pools), while the frame is spent on lighting that every preset draws the same.
(c) What the highest settings need (measured ms / 33.3): Ultra at 432 rows: about 3.3x (walk_near), 4.7x (forest), 3.5x (jungle) this GPU's
throughput for p50; for the tail (<= 5 % over 33 ms, i.e. p95 <= 33) about 6.5x / 7.6x / 5.8x (p95 217 / 253 / 192 ms). High: 3.3x / 4.5x / 3.3x (p50).
Ultra at 720 rows (its intended resolution): ~8x for p50 (inflated run; from the clean 540 scaling, ~2.6-2.8x the 432 cost -> ~9x). Largest single costs
(212's split, High, walk_near, 432 rows): sky fill 2 40.4 ms, torch flood twin 27.8, sun shadows 28.1, shells 8.4; with sky probe + sun + shells off
("ceiling") 24.6 ms is left. Ultra also needs ~150 MB more memory headroom than the 3 GB cap.
## Why 212's 106.8 ms and the rig's 23.7 ms differ
The rig (`MODE=walk ./run-tg2world.sh`) runs `-renderpreset performance` = 480x270 FSR 1 (~2.6x fewer pixels than play's 768x432), golden-hour sun, and
none of play's flags: torch flood OFF (play: -27.8 ms), sky fill 0 (play's fill 2: -40.4), shells OFF (-8.4), no far shade / see-through / river look,
no minimap. 212 ran exactly play's flags (`tools/play-flags.sh`). The two differ by those features + pixels, not by a bug; today's walk_near here
(108-111 ms) reproduces 212's 106.8.
## Memory (rule 11): capture code only, nothing stored at run time. Memory peaks above are the presets' own (Ultra's 508,800-body GPU pool).
Tests: fast PASS 1061 FAIL 0 SKIP 24 (`Logs/overnight/219/fast.out`).

## For the doc
- No quality preset reaches 30 fps on this Mac: walk_near p50 Ultra 109.8, High 111.4, Medium 108.3, Low 108.0 ms (target 33.3); 81-100 % of frames over 33 ms (`Logs/overnight/219/table.md`).
- Lowering the preset saves memory, not time: process peak Ultra 3,122 (over the 3 GB cap), High 2,607, Medium 2,395, Low 2,297 MB.
- More render rows cost in proportion: Ultra 540 rows 169 ms (1.54x for 1.56x the pixels); 720 rows ~273 ms (owner was using the Mac: inflated).
- To run Ultra at 30 fps needs about 3.3-4.7x this GPU (p50) and 6-8x for the smooth tail; the big costs are sky fill 2 (~40 ms), torch flood (~28), sun shadows (~28) (task 212).
- Picture: High/Medium/Low differ from Ultra by about the frame-to-frame noise (mean 1.3-3.3 of 255); only far tree crowns at 40-80 m get speckled.
- `Screenshots/219/sheets/doc_sheet.png`: rows = walk_near / forest / jungle, columns = Ultra, High, Medium, Low. Look how alike the columns are.
- `Screenshots/219/sheets/crop_forest_far.png`: the forest background, 4 presets (Ultra top-left). Ultra's far birch crowns are full; High/Medium/Low show grey gaps.
- `Screenshots/219/sheets/crop_walk_lowright.png`: the rock lip at walk_near. Ultra's shadow under the lip is smooth; the lower presets show dark blotchy speckle.
