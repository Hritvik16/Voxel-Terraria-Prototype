# 48-engine-3b-upscaler-compare

Requires: 47-engine-3a-upscalers-plugin

Approval: owner, 2026-10-09 ~23:20 (see 47). Engine plan step 3, the comparison half. Branch
`engine-phase`. Ends with STOP_FOR_OWNER: the owner picks the upscaler and resolution (look and feel).

## Goal
A side-by-side comparison of every WORKING upscaler from 47 (fsr1, stp, fsr3, taa reference, plus
native 1080p as ground truth) at 540, 720 and dyn, so the owner can pick.

## Steps
1. Stills, same poses for every arm: tg2 spawn day; a near cliff; distant mountains/horizon; a forest
   or vegetation edge; water/lava; a night pose with torches. Save to `Screenshots/Engine03/compare/`.
2. Motion quality: the `MODE=shimmer ./run-tg2world.sh` metric per arm (baseline before TAA: mean of
   poses near 4.14 / 5.84%, all 5.89 / 7.59%). And task 07's TAA risk set (edits, fluid, turns, seams)
   per temporal upscaler (stp, fsr3): ghosting/smearing called out with crops.
3. Contact sheets, captioned (what to look at per row): one per resolution (rows = poses, columns =
   native / taa / fsr1 / stp / fsr3), plus ONE crop sheet zoomed 3x on the cliff edge and the horizon.
4. NO frame-time numbers (plan step 7 measures them warm). State what each arm costs only as
   "not measured".

## Finish
`STOP_FOR_OWNER` at the top of HANDOFF with ONE question: which upscaler + which resolution
(recommendation first, with the shimmer numbers and the crop that shows the difference). Do not change
any default.

## Report for the owner (`## For the doc` in HANDOFF)
Shimmer table (arm x resolution), the contact sheet paths with captions, risk-set findings.
