# 226-look-lotl-polish
Model: opus
Requires: 225-look-trunks-not-black
Approval: OWNER 2026-11-16 (Q-AN): "cheap look polish now (per-voxel colour variation, crevice shading, haze, bloom) ... do this if we get closer to Lay of the Land's visual aesthetics or at least not stray too far away."
Read `RESEARCH_LAY_OF_THE_LAND.md` ("Visual comparison", "Options" 1) and `Screenshots/222/side_by_side.png`.
## Do (four pieces, each its own switch, each default ON only when its picture is better and its cost is small)
1. Per-voxel colour variation (stable per voxel, from position hash, small amplitude, per material; no flicker under FSR/TAA).
2. Crevice shading (cheap AO from the occupancy/air mip the ray march already has; no new pass if it can ride the existing hit shading).
3. Distance haze (aerial perspective toward the sky colour with a height falloff; hides the hard far-hill edge).
4. Bloom (only if it can be done as a cheap post step at render resolution; skip with a note if it needs a new full-res pass).
## Rules
Efficient by construction (rule 11): each piece reports GPU memory added (target 0) and its per-pixel cost class; no new full-resolution pass without saying so. No terrain-specific constants: derive from material/height/slope data.
Pictures: same poses as 221 (walk_near, forest_eye, jungle_eye, torch_camp) before/after each piece and all four together, a doc-ready sheet, one full-size forest pair, and a side-by-side against LotL's shots in plain words (what moved closer, what did not). Do NOT time frames; add one line of the rough cost class per piece. Fast suite. STOP_FOR_OWNER if a frozen API / memory layout would change.
