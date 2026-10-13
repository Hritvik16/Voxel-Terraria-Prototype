STOP_FOR_OWNER -- Which upscaler + which internal resolution?
**Recommendation: FSR 3 at dyn (540-720 rows; on this Mac it sits at 540 today).** Edges are the cleanest of any arm,
with no tone shift. It is sharp in motion where our TAA smears (edge ratio vs taa 1.2-1.9x), and its shimmer is second only to
taa: all-band mean|dL| 4.81 / 4.91% at 540 vs no-AA 7.27 / 9.82%. The crop that shows it: `sheet_crops.png`, column 5
(smooth cliff and peak silhouettes) vs column 1 (native: stairs) and 2 (taa: blur). Alternatives: taa (lowest shimmer
3.80 / 2.74%, but visibly soft), stp (sharp, but greyer and never settles at rest: still-frame floor 2-7.5%).

# 48-engine-3b-upscaler-compare: DONE. 5 arms x 540/720/dyn on 6 poses, shimmer per arm, risk set per temporal upscaler.

## What changed (branch `engine-phase`, `f792468`)
- `Assets/Game/PlaygroundCapture.UpscalerCompare.cs` (`-upscalercompare`, `run-upscaler-compare.sh`): one launch,
  each pose streamed once, every arm applied in process via UpscalerSelect.Apply. Shimmer first (no edits):
  -nearshimmer's poses and protocol, with the nudge fixed at a quarter 960x540 pixel for every arm (= the baseline's).
  Then the stills. Native = raymarch at 1920x1080, no AA.
- `PlaygroundCapture.TaaRisk.cs`: with `-upscaler`, it keeps UpscalerSelect's resolution (no forced 960 gate), and the folder is named after the arm.
- `tools/upscaler-compare-sheets.py` builds the contact sheets. No defaults changed.

## Evidence
- Fast suite PASS 634 FAIL 0 SKIP 14 (`Logs/overnight/48_fast.out`). Build: 0 `error CS` / `Shader error`.
- Compare run `Logs/upscalercompare_2026-10-10_063429.txt`: every arm traced 190/190 (shimmer) and 90/90 (stills)
  frames. All poses were in air with deficit 0. **dyn settled at 540 rows in all 24 pose x arm cases** (smoothed 34-62 ms, 0 steps),
  so the dyn column equals 540 today. Frame time per arm: **not measured** (plan step 7).
- Risk runs (`Logs/tg2world_20261010_070208/070603/071018.txt`; reports `Screenshots/Engine03/compare/risk_*/report.txt`).
- I looked at sheet_540, sheet_crops, sheet_motion_crops, and the risk fly sheets for stp and fsr3. Same view in every tile; no black or garbled frames.
- Pose notes: tg2 has NO vegetation (the world tree is off), so slope_detail (a sub-pixel grass slope at 56 m)
  stands in for "forest edge". In fluid_pools only the lava pool is in view (it reads as an orange box on the slope);
  water is in spawn_day (lakes, sea).

## Risk-set findings (each vs taa_540, same frames)
- Ghosting: none visible in the fly / fast-spin / stop crops for stp or fsr3. Both are sharper than taa in motion, and taa
  is the one that smears (edge ON/OFF: fsr3 1.22 still, 1.46-1.9 moving; stp 1.14-1.7).
- **Not judged:** edits (the dig changed only 142 px, so the ghost ratio is noise) and fluid (no motion in view across
  frames, as in task 07/09). A dedicated edit/fluid pose is needed if the owner wants these checked.
- stp: tone bias -1.6 to -10 luma (URP post chain, see 47), and a still-frame shimmer floor of 2-7.5% (fsr3 0.4-1.7%).
  STP does not settle at rest. This may be our input integration rather than STP itself; NOT investigated.

## Unfinished / next
- The owner picks. Then plan step 7 measures frame time warm. Open follow-ups (not queued): the fsr1/stp tone shift;
  STP's unsettled floor; an edit/fluid risk pose that the dig and the flow actually reach.
- The raw stills (190 MB) are uncommitted in `Screenshots/Engine03/compare/*__*.png`; shimmer frames are in the player's data folder `UpscalerCompare/2026-10-10_063429/`.
  Disk was at 97% (6.7 GB free) before the run.

## For the doc
Shimmer, mean |dL| (0-255 luma) / % of pixels > 24, under quarter-pixel nudges, lower = steadier (blur also lowers it):
| arm | near 540 | all 540 | near 720 | all 720 |
|---|---|---|---|---|
| native 1080 (no AA) | 3.80 / 2.83 | 7.91 / 6.82 | -- | -- |
| none (bilinear, old baseline path) | 3.61 / 4.35 | 7.27 / 9.82 | -- | -- |
| taa | 2.48 / 1.65 | 3.80 / 2.74 | 2.39 / 1.86 | 4.07 / 3.83 |
| fsr1 | 4.62 / 4.65 | 8.59 / 9.75 | 4.56 / 4.55 | 8.54 / 9.70 |
| stp | 3.38 / 3.93 | 6.19 / 7.93 | 3.39 / 3.79 | 6.29 / 7.97 |
| fsr3 | 3.06 / 3.16 | 4.81 / 4.91 | 3.31 / 3.65 | 5.51 / 6.60 |
(2026-10-02 baseline was near 4.14 / 5.84, all 5.89 / 7.59, but on the older map; none_540 is this run's like-for-like reference.)
- `Screenshots/Engine03/compare/sheet_540.png`: rows are spawn_day, near_cliff, horizon, slope_detail, fluid_pools and night_torches; columns
  native / taa / fsr1 / stp / fsr3. Look at the greyer sky in columns 3-4 (fsr1, stp), and the same view in every tile.
  `sheet_720.png` and `sheet_dyn.png` have the same layout (dyn = 540 today).
- `Screenshots/Engine03/compare/sheet_crops.png`: the cliff silhouette (rows 1-2) and the peak ridge (rows 3-4) at 3x, at 540
  and 720. Compare the edges: native stair-steps, taa blurs, fsr1 is sharpened but stepped, stp is crisp, fsr3 is smooth and clean.
- `Screenshots/Engine03/compare/sheet_motion_crops.png`: taa / stp / fsr3 at 540 in motion (fly, fast spin, the first frame
  after the stop, the edit view) at 2x. Look at the pillars and the ridge: taa smears, stp and fsr3 stay sharp, no trails.
