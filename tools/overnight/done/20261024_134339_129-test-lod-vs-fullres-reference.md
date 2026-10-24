# 129-test-lod-vs-fullres-reference

Model: opus

Approval: owner, 2026-10-23, in chat. First ask: "a test where we build a 0.1m res of some terrain at every [LOD level distance] away and
compare that with what our game looks like". Clarified: "for terrain 200m away, I want to build that terrain at 0.1 and look at it from
200 m away and compare that with the actual game view (with the shells and lod levels). I want to compare if we are actually seeing the
0.1 granularity shine with all our optimizations or if we're missing something so we might need FSR and TAA on. This is your call as
long as you understand the goal." Test/capture task: no product default changes, no frozen-API or memory-layout changes, no timing.
Queued AFTER 127 and 128.

## Goal
Does the picture the player sees of terrain at distance D keep the detail a true 0.1 m build of that same terrain would show from the same
spot? If the game looks softer/blockier/noisier, find which part loses it: the LOD tier/shell, the lower render resolution, or the
upscaler/resolve. Then say whether FSR 3 / TAA should be on, off, or tuned for that distance.

## Method
Distances D (camera to the terrain of interest): 200 m (the owner's case), plus 50, 100, 400, 800 m, 1.6 km, 3.3 km (and 6.6 km if cheap).
For 3 scenes (a ridge/cliff face, a coast with sea, a plain or forest edge), same seed/map, same camera pose, same sun and time of day
(side-lit as in 127, so relief shows), same output resolution (1080p):
- **REF** : the terrain patch around D (the screen footprint of the region, not the whole world) built at 0.1 m in a scratch/capture
  store and rendered as the near tier would show it, no upscaler, no TAA. This is "what 0.1 m granularity looks like from here".
  Owner 2026-10-23: the reference does NOT have to meet the 3 GB cap or any runtime budget (it is a test, run it as its own capture
  process, never in play), and it need not be the whole view: generate only the patch in the screen footprint around D at 0.1 m (and
  the far terrain behind it only as far as the question needs, e.g. a wedge or strip across the view centre) and let the rest of the
  frame be the normal far terrain, or masked out in the comparison. Pick the patch size so generation fits in a few minutes and the
  Mac's 8 GB (one Unity process, lock held); report the patch size and memory used. Leave all runtime defaults alone. Stop with
  STOP_FOR_OWNER only if even a small patch cannot be rendered at 0.1 m without a frozen-API or memory-layout change in the product.
- **GAME-NATIVE** : the real game render at that pose (shells, LOD tiers, far band, as shipped), rendered at the full 1080 rows, no
  upscaler, no TAA. REF vs this isolates the LOD loss.
- **GAME-PLAYED** : the real game as the player gets it: FSR 3 dynamic from 540 rows (play default). GAME-NATIVE vs this isolates the
  upscaler loss. Also REF rendered through the same FSR 3 path, to show what the upscaler does to a perfect image.
- Optional if cheap: GAME-NATIVE with TAA on, and GAME-PLAYED with TAA on, to answer the TAA question for distant detail.
Use fixed camera, no motion for the stills; for TAA/FSR add a short slow-pan pair (shimmer) only if the stills leave the question open.

## Numbers and images
Per (scene, D): pair/triple side by side (REF | GAME-NATIVE | GAME-PLAYED) at ≥1400 px per row, an amplified difference image
(REF vs each), and 2x crops of the worst region. Numbers: mean absolute luma/colour error, % pixels over a threshold, an edge/sharpness
measure (e.g. gradient energy) of each versus REF, silhouette shift in px, plus the on-screen size in px of one voxel of the tier in
use at D vs a 0.1 m voxel. Captions say which panel is the reference and what to look at.

## Verdict (one paragraph, plain words)
Per distance band: does the game show the 0.1 m detail, or is it missing it? Where lost: which stage (LOD, resolution, upscaler)? Then a
recommendation on FSR 3 and TAA for each band, and if a LOD switch distance looks too near, by how much (suggestion only, no change).
Keep it as a repeatable capture flag so it can be re-run after LOD changes. TASKS.md paragraph, HANDOFF with `## For the doc`, fast
suite once if code under Assets/ changes.
