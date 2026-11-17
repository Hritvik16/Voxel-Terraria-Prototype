# 231-look-why-lotl-looks-cinematic
Model: opus
Requires: 230-perf-budget-check-script
Approval: OWNER 2026-11-17 (chat, "yes"): "I can't really tell why [Lay of the Land] looks 'production quality' and 'cinematic' but ours doesn't." Find out which factors matter most, BEFORE any engine work.
CAPTURE-ONLY / OFFLINE EXPERIMENT. No engine, shader, or generator change. No frame timing, no rig. Python image work uses `python3 -I` and runs from a script directory that holds no input images.
## Do
1. Inputs: `Screenshots/222/side_by_side.png` and `RESEARCH_LAY_OF_THE_LAND.md` (their shots), and OUR stills from 226 (`Screenshots/226/sheets/forest_eye_full_after.png`, plus walk_near, jungle_eye, torch_camp from `Screenshots/226/`). Look at every image before using it.
2. Factor ablation, offline, on our stills, each factor alone and then all together. Each factor must be a SIMPLE, nameable change we could later implement in the engine (say how in one line):
   a. Warm sun / cool shade split (tint lit pixels warm, shadowed pixels cool-and-lifted; use a luma mask).
   b. Paler, hazier sky with soft clouds (replace sky pixels only: sky mask = pure-sky pixels).
   c. Colour grade: limit/harmonise the palette (desaturate greens a little, warm midtones, filmic S-curve, slight vignette).
   d. Lifted shade (raise the darkest 20 % of pixels toward a coloured bounce).
   e. Fine texture: overlay a per-voxel grain at ~1/4 voxel scale (stand-in for smaller blocks / textures).
   Also the reverse test: take THEIR shot and remove each factor (flatten the grade, make the sky pure blue, kill the warm/cool split) to see which removal makes it look like ours.
3. Composition: if the capture arms can already place a pose with a low sun and trees/ridge in view without code changes, take up to 3 such poses in ONE short launch (<= 10 min, rule 3 launch budget) so the stills are framed like a screenshot, not a test target. If that needs code, skip it and say so.
4. Output: `Screenshots/231/sheets/` with (i) a grid: our still | +a | +b | +c | +d | +e | all five, for two poses; (ii) their shot with each factor removed; (iii) one full-size flip pair (ours before / all five). Every image captioned in HANDOFF (what to look at).
5. A short ranking in HANDOFF: which factor moves ours closest to theirs, which barely matters, what each would cost in the engine (new pass? constants only? per-pixel work? memory) using rule 12's cost classes. The look is the owner's call: do NOT queue or implement any of it.
## Rules
Efficient by construction (rule 11): this task changes no drawing, so report "no engine change; memory n/a". Commit the script and the text report only (images are git-ignored). Finish HANDOFF with the `## For the doc` block.
