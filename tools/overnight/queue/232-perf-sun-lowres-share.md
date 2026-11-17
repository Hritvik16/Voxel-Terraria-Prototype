# 232-perf-sun-lowres-share
Model: opus
Requires: 227-perf-sky-fill-lowres
Approval: supervisor, "going ahead with (a) unless the owner objects" (reversible engineering choice; task 228's STOP_FOR_OWNER recommended (a); recorded in the owner's doc 2026-11-17). Owner direction Q-AN: lower-resolution / cheaper lighting toward Lay of the Land.
Performance task (timing allowed, owner rule 10; warm steady state, interleaved arms; write each possible outcome and its action FIRST in `Logs/overnight/232/PLAN.md`).
## Why
228 measured sun shadows at 22.7 / 30.0 / 18.0 ms (walk / forest / jungle: exact minus sun off). Far receivers are only 2-6 ms of that, and "coarse far" draws false shadows on steep slopes (dark brick triangles). At least 2/3 of the cost is NEAR receivers. 227 cut sky fill from ~40 to ~6 ms by tracing ONE ray per 4x4 block and sharing it across same-plane faces. Do the same for the sun ray, EXACT (no coarse bricks).
## Do
1. Read the HANDOFFs / TASKS.md paragraphs of 227 and 228, `AMENDMENT_8_17_RENDER_STEPS.md` step S2, and SkyFillReference's low-res tests (the template).
2. Design (rule 11: efficient first; no new G-buffer or UAV, the kernels sit at Metal's 8-UAV cap): trace the sun ray once per 4x4 block for one face of the block and share the result with pixels on the SAME face plane (same normal, same coordinate along it, within the block's footprint), with 227's depth/normal-aware rule. A face with no record takes a compatible reprojected neighbour, else traces inline (exact). Keep the soft-shadow edge: a block that straddles a shadow edge (neighbouring blocks disagree) falls back to per-pixel exact. Switch `-sunres 1|2|4` (1 = today's path, default until proven; flip the default to 4 only if PLAN.md's outcomes say so). Capture arms `sunres_off` / `sunresNN`. Avoid HLSL `bool` selects in shared expressions (task 227's Metal bug).
3. CPU twin and tests like 227's: ledge top vs slot floor, thin pole stays exact, slope with no false triangles, shadow edge within 1 voxel of exact.
4. ONE cost-split launch, ABBA, at walk_near / forest_eye / jungle_eye (night torch_camp for pictures only): sun exact vs sunres 4 vs sun off. Pictures: a sun-visibility sheet, and 3x zooms of a shadow edge and of the steep hillside that broke in 228 (it must show NO triangle field).
5. Report rule 11/12 (memory vs OFF, refusals, pass card). Fast suite.
## Done when
No false shadows on the 228 hillside, edge error <= 1 voxel, ms saved reported per pose. If the saving is under 5 ms at walk_near, keep the switch OFF and say so; that is not a STOP_FOR_OWNER.
