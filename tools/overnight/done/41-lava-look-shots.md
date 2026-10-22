# 41-lava-look-shots

Requires: 37e-lake-flat-water

Approval: owner, 2026-10-09 in chat (lava should look like the SVO build's: deep orange, glowing;
see `Screenshots/SVOLook/lava_night_svo_vs_ours.jpg`). The change was written by Gemini CLI and
reviewed by a Claude side session (`tools/overnight/REVIEW_GEMINI.md`); it has NEVER been compiled.
Queued by that side session 2026-10-09.

Branch `surface-rebuild`. Shader + rendering flag only; no terrain-generator change, so the FAST
suite is enough.

## What is being merged
Branch `gemini/lava-look` (worktree `../VT_gemini`, commit 73a625b; read the "Review" section at the
top of its `GEMINI_HANDOFF.md`):
- LOOK 1: lava (mat 11) shades `baseColor * faceLight + albedo * emission * 2` with per-voxel hot/cool,
  instead of `saturate(base * 1.35 + 0.06) + tint * 0.55`. Every other material is unchanged.
- Emissive bleed (port of the SVO RayTracer's): uniform `_EmissiveBleed`, `LightingConfig.emissiveBleed`
  (default 0), `-emissivebleed 0|1`. OFF by default.

## Steps
1. Before merging, note `HEAD` of `surface-rebuild` (the "before"). Build the player at that HEAD
   and take the BEFORE shots (step 4) -- or reuse the build already there if it is that HEAD.
2. `git merge --no-ff gemini/lava-look` into `surface-rebuild`. Conflicts: resolve keeping both
   sides; stop and report if a conflict is inside the LOOK 1 combine in `Raymarch.compute`.
3. Correctness: fast EditMode suite (`./run-editmode-tests.sh`, under the lock) plus
   `ShaderCompileCheck`. KNOWN_ISSUES: CheckAll can report green for a shader that does not build,
   so ALSO confirm the player build log compiles the Raymarch kernels with no `Shader error`
   (`run-lavalight-ab.sh` greps for it). A shader error: fix it if it is plainly a typo in the merged
   block, otherwise STOP_FOR_OWNER.
4. Shots: the existing lava-light scene, `-lavalight` (`SKIP_BUILD=1 SUSTAINED=0 ./run-lavalight-ab.sh`,
   or its launch line by hand; poses as in `Screenshots/LavaLight/lavalight.txt`: cavern wide,
   wall beside lava, cavern far end, torches AND lava, surface pool at night, surface pool golden
   hour). Lava light ON (shipped share) in every arm. Three arms, same poses:
   - BEFORE: the pre-merge HEAD;
   - AFTER `-emissivebleed 0`;
   - AFTER `-emissivebleed 1`.
   Save to `Screenshots/LavaLook/`.
5. ONE captioned contact sheet `Screenshots/LavaLook/contact_sheet.png` (rows = poses, columns =
   before / bleed 0 / bleed 1), captions naming what to look at (lava colour deep orange vs pale
   gold; torches identical before vs bleed 0; walls/floors beside lava and torches warmer with
   bleed 1 only). Add the SVO reference crop from `Screenshots/SVOLook/lava_night_svo_vs_ours.jpg`
   beside the night-pool row if it fits.
6. Say in HANDOFF what you SEE, specifically: is the CAVERN lava as deep orange as the night pool?
   (Lava faces still take point light from the lava aggregate lights; if the cavern lava is pale,
   report it -- the candidate fix is to drop `pointTerm` on lava faces -- do NOT make that change.)
   Is anything other than lava different between BEFORE and bleed 0? (It should not be.)

## NO performance arms
PREAMBLE rule 10: this is a look task. No sustained / frame-time runs (`SUSTAINED=0`). The bleed's
cost (four extra window reads per pixel when on) is measured at the engine milestone.

## End
Commit (by path), then STOP_FOR_OWNER with the contact sheet path and the individual shots: the
owner decides whether lava is right and whether bleed stays on.
