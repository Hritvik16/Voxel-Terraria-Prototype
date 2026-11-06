STOP_FOR_OWNER -- task 174b: "Which torch-flood setting?" (becomes the default in task 175)
- (a) **Recommended: S3, I 1.6 k 0.35** (the point lights' own values).
  - A warm spot per torch that fades to dark by ~4-5 m, with no black gaps between neighbouring torches.
  - Clipping near the camp posts drops from 6.8 % to 0.3 %; walk flicker drops from 5.2 to 3.7 (today 4.1).
- (b) S2, I 1.4 k 0.20: a little brighter and wider (more light in caves and on walls), same clipping, flicker 3.9.
- (c) S4, I 2.4 k 0.60: hotter cores, but 1 % clipping comes back.
- (d) S5, I 3.0 k 1.20: tight spots, and caves stay 63 % black.
- (e) S1, I 2.0 k 0.10: 172's flood as it is today.

# 174b-torch-flood-falloff-tune: 5 flood settings x 6 night scenes shot in one launch; sheets, profile and viewer built; capture only, no defaults changed

## What changed (`4d03939`, engine-phase)
- `PlaygroundCapture.TorchCine.cs`:
  - `-tcsets "I/k[/wrap],..."` with arm tokens `+fN` (the flood ON with set N). Params are read every frame, so the flood is built once per scene.
  - `tc_<scene>_proj.csv`: torch viewport positions, the 2 m near-post disc, and ground points round one torch for the radial profile.
- `tools/torchtune174b.py`: the table, profile plots, half-size contact sheets, and the full-size viewer (`Screenshots/174b/viewer.html`; arrows change scene, Space changes setting).
- TASKS.md: task 174b paragraph.

## Evidence
- Launch: `Logs/lookreview_20261105_181932.txt`. Shots: `~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/LookReview/2026-11-05_182440_high/`.
- Table: `Screenshots/174b/torchtune174b.txt`.
- All arms use FSR 3 at 540 rows with sc0 and no mask, flood `-tfdir 2`. Each set's truth is its own native.
- Night 00:30. Shells: 4,065 missing after the 45 s cap (far shells s2-s7; the near field was full).
- Fast suite: PASS 997 FAIL 0 SKIP 16 (`Logs/overnight/174b_fast.out`).
- Cave and water poses differ from 172's. The cave is now seen head-on, eye pitch 3 (172: -25). Same terrain and seed.

| setting | camp15 lit / clip near posts % | wall lit % | cave black % | walk flicker (lit) | 40 m strafe flicker |
|---|---|---|---|---|---|
| today (flood OFF) | 5.6 / 0.18 | 0.8 | 82.5 | 4.12 | 4.21 |
| S1 2.0/0.10 (172) | 21.9 / 6.77 | 16.4 | 7.9 | 5.23 | 10.75 |
| S2 1.4/0.20 | 13.7 / 0.28 | 5.3 | 34.4 | 3.93 | 7.42 |
| **S3 1.6/0.35** | **11.6 / 0.31** | **3.6** | **47.1** | **3.73** | **6.93** |
| S4 2.4/0.60 | 11.5 / 1.03 | 3.6 | 50.5 | 4.12 | 7.65 |
| S5 3.0/1.20 | 9.2 / 0.69 | 2.4 | 62.7 | 3.58 | 6.57 |

- Water terrace, native luma along the ground on the land side (r = 0.5 / 2 / 4 / 6 m). Flood OFF has ~24-26 of moonlight on its own.
  - S1: 180 / 116 / 62 / 40
  - S3: 111 / 64 / 36 / 28
  - S5: 101 / 53 / 31 / 26
- The camp profile is not reliable: the ridge crest is not level, and the posts block the rays that run through them. Judge the camp from the images.

## Verdicts (from the sheets and the full-size crops)
- **S1:** the 172 problems are all there: one sheet along the ridge, a yellow clip at the centre post, and a glow up the whole wall to the ridge.
- **S2 / S3:** a warm pool per torch with a gradient. No clipping. Wall torches make connected spots with no black gaps.
  - Cave: a lit interior, with only a faint fan onto the outside face (S1's fan was big).
- **S4:** looks like S3 with hotter cores that clip again.
- **S5:** tight spots; caves and walls fall back toward today's darkness.
- **Strafe at 40 m:** every setting still crawls more than today (6.6-7.7 vs 4.2), though less than S1's 10.8. The tune alone does not fix this; sc10 plus the reactive mask (174) is the other half.
  - Not tested together: the brief said not to use sc10.
- Unchanged from 172: today's flood-OFF path still draws a hard unlit rectangle beside the water post. The flood hides it.

## Next
- Owner picks a setting. Task 175 makes it the default (`TorchFloodPool.Intensity` / `FalloffK`) and turns the flood on in play.
- Worth one look in play at the end: the chosen setting with sc10 plus the mask.

## For the doc
- Clipping near the camp posts: S1 (172) 6.77 % -> **S3 0.31 %** (today 0.18 %). Camp lit pixels 21.9 -> 11.6 % (today 5.6 %) (`Screenshots/174b/torchtune174b.txt`).
- Walk flicker vs native, lit pixels: S1 5.23 -> **S3 3.73** (today 4.12). 40 m strafe: 10.75 -> 6.93 (today 4.21).
- Cave black pixels: today 82.5 %, S1 7.9 %, S3 47.1 %. Wall lit: 0.8 / 16.4 / 3.6 %.
- `Screenshots/174b/tt174b_camp15_crop.png`: full-res crop of the posts. Top row: today | S1 | S2; bottom row: S3 | S4 | S5.
  - S1: the saturated yellow patch at the right-hand post base and the even sheet along the ridge.
  - S3: a warm spot per post that fades between the posts.
- `Screenshots/174b/tt174b_wall_crop.png`: same layout. S1 lights the whole face up to the top; S3 / S4 give a spot per torch and the face fades to dark above.
- `Screenshots/174b/profile_water.png`: native luma vs distance from the lone torch.
  - Orange S1 stays bright out past 5 m; green S3 drops to the moonlight floor (~28) by 6 m.
  - The bump at 8-10 m is the terrace edge, present with the flood OFF too.
- Full-size viewer: `Screenshots/174b/viewer.html` (images in `Screenshots/174b/full/`).
