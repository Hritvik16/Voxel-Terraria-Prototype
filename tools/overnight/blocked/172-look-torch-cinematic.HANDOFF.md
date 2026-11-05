STOP_FOR_OWNER -- task 172: is the recommended torch look cinematic? **Partly. Torches finally light their surroundings, but not cinematic yet.**
Question: "Fix the flood's look before turning it on in play?" (a) **Recommended:** yes. A capture-only `-tfi/-tfk` sweep on these same 6 scenes
(the 172 rig re-runs as-is) gives a steeper falloff and less yellow clipping. Then pick from full-size sheets. (b) Turn it on now as it is: big gain in
caves, walls and shores, but flat, yellow camps. (c) Keep today's look.

# 172-look-torch-cinematic: 6 night scenes shot (today vs recommended); sheets built; verdicts below. Capture only, play unchanged.

## What changed (`f2d0e24`)
- `Assets/Game/PlaygroundCapture.TorchCine.cs` (new) + hook in `PlaygroundCapture.LookReview.cs`: `-lookreview -torchcine 1 [-tcscenes a,b] [-tcarms ..]`.
- `tools/torchcine172.py`: full-size sheets, 4x crops, burst flicker vs native, and GIFs. TASKS.md: Task 172 paragraph.
- Arms: today = FSR 3 at 540 and at 720 rows (play's dyn range), flood OFF. Recommended = 432 and 540 rows with sc10, flood ON with dir 2. Truth = native with and without the flood.

## Evidence
- Launch 1 (all scenes): `Logs/lookreview_20261105_130445.txt`. Cave + water reshot (`Logs/lookreview_20261105_131807.txt`, `_132738.txt`): the cave camera stood below the mouth and my diagonal carve left rock bars; the water torch stood 4.7 m above the pond.
- All final shots were merged into `~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/LookReview/2026-11-05_130919_high/`.
- Numbers: `Screenshots/172/torchcine172.txt`. Night 00:30. Shells at the camp after the 45 s cap: s2-s7 still had 3,464 missing (far shells only). Fast suite: PASS 997 FAIL 0 SKIP 16 (`Logs/overnight/172_fast.out`).
- The start is mountain: no shore within 90 m, so water uses a SYNTHETIC level stone terrace + pond; the "open ground" camp is a ridge crest, and the "40 m" view looks down from 25 m above.

## Verdicts (I looked at every sheet, crop and burst)
1. **camp15 -- acceptable.** Today: small dim pools and hard black shadow pockets. Recommended: one big warm pool that reads as a camp.
   - Floors near the posts clip to saturated yellow; the pool is an even plateau, not a falloff round each torch; one hard post-shadow line.
2. **camp40 -- good from afar.** A warm glow on the ridge. Smooth fade at the rim; no pop or ring at the 12 m reach.
   - The 4x crop shows a washed-out, flat pink fill.
   - Mushy yellow-green blotches at the rim, where FSR 432 blurs native's high-contrast stripes (yellow tops vs pink sides).
3. **cave -- good, both.** Today is already a nice warm doorway in the dark. Recommended: a brighter, even interior.
   - Light wraps 90 deg round the mouth onto the outside face as an upward fan. That is too much, but it reads as bounce light.
4. **wall -- acceptable.** Today: torches against the face barely light it (lit px 0.8 %). Recommended: 16 %, a warm lit face.
   - The glow covers the whole slope up to the ridge, flatly.
   - Small dark dashes in native flood, which FSR hides.
5. **water -- acceptable.** Recommended: a smooth round pool on the stone, and the post is lit. Today the post is a black silhouette.
   - The water gets NO torch light or glint: the pool stops at the edge.
   - Separately, TODAY's path (native too, so not FSR) draws a hard-edged unlit rectangle (~2.5 x 5 m) beside the post. The flood hides it. Not explained; maybe stale sky/ambient data after the large terrace edit. Worth a bug check.
6. **walk / strafe -- not yet.** Flicker vs truth in lit pixels:
   - walk: today 540 4.1 -> rec 432 6.8 (540: 5.8)
   - 40 m strafe: 4.4 -> 11.0
   - The walk burst shows a bright speckled line along moving silhouette edges (sc10, the same family as 171's ghost).
   - The strafe crawls because the flooded slope is high-frequency yellow/pink stripes that 432 rows cannot hold still.

**Verdict:** the recommended set is a clear step up where today's torches fail: walls, caves, and the post's own light. Not cinematic yet:
- Pools are flat and too even out to 12 m.
- Near-torch floors clip to yellow.
- In motion it crawls about 2x more than today.

**Single biggest fix (follow-up, not done):** the flood's intensity/falloff curve (`-tfi 2.0`, `-tfk 0.10` vs the point lights' k 0.35). A steeper falloff and a lower peak would:
- give each torch a bright core and a dark rim (shape instead of a plateau);
- stop the yellow clipping;
- cut the per-face stripe contrast that drives the crawl.

Task 174 (reactive mask) already covers the sc10 speckle.

## Not done
- Lava; a real lake shore; dusk (night only). No queue follow-up added: the tuning is a look decision for the owner.

## For the doc
- Lit pixels (luma > 40), today 540 -> recommended 432: wall 0.8 -> 16.2 %, cave 0.8 -> 16.4 %, water 4.5 -> 36.8 %, camp40 1.5 -> 3.2 % (`Screenshots/172/torchcine172.txt`).
- Flicker vs native in lit pixels: walk 4.1 -> 6.8, 40 m strafe 4.4 -> 11.0 (same file).
- `Screenshots/172/tc172_camp15.png`: full size; LEFT today, RIGHT recommended (top row 540 vs 432, bottom 720 vs 540). Look at the ridge between the posts: today small dim pools; recommended one big pool, saturated yellow right at the posts.
- `Screenshots/172/tc172_wall.png`: same layout. Today the cliff is nearly black around its 6 torches; recommended lights the whole face warm, evenly up to the ridge.
- `Screenshots/172/tc172_walk_burst.png`: 8 walk frames left to right, rows today 540 / rec 432 / native+flood. Look at the top edge of the blocks in row 2: a speckled bright line that native does not have.
