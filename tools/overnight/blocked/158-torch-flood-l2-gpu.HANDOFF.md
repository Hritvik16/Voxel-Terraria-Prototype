STOP_FOR_OWNER -- 158 done (torch flood L2, OFF by default). Before L3, three questions (recommendation first):
Q1 (157 flag, L3 CPU copy). The exact per-voxel CPU copy is far bigger than the plan's "same size again": the 31-torch tg2 scene below
   needed 152,644 lit bricks x 2 KB = 298 MB of oracle storage during each rebuild (GPU pool 11.2 MB). (a) Recommended: the CPU keeps the
   GPU's compressed form (11.2 MB here) and edits / removals re-flood only the torches whose box they touch. (b) Per-voxel only in mixed
   bricks (18,794 x 2 KB = 37 MB here), air bricks rebuilt from their 1-voxel shell. (c) Keep the exact copy (~300 MB: over the 3 GB cap).
Q2 (157 flag, L3 flood cost). In the player, flooding 31 outdoor torches took 9.8 s on the main thread (316 ms per torch; 157's EditMode figure
   was 215 ms). (a) Recommended: L3 floods pure-air bricks as a unit plus a sparse scratch (157 est. ~10x fewer steps), on a worker, one
   torch per job, and lights stream in over a few frames. (b) The worker job alone, as today's flood (one outdoor torch ~0.3 s late).
Q3 (new, the look). The direction term gives hard-edged facets (a bright square under the room torch, flat wedges along wall corners,
   vertical stripes on the tunnel wall), because the chamfer flood's gradient is piecewise constant. (a) Recommended: one short look task
   before L3: a smoother direction (central differences over 0.4 m on 3 axes, or blending toward the nearest torch's direction while the
   flood path is near-straight), a sheet beside today's. (b) Drop the direction term and lower the intensity (flat, no facets). (c) Accept.
   "Flood ON by default?" is NOT asked: the cost passes but the look does not yet.

## What changed (`79d5dc5`, branch engine-phase)
- `TorchFloodPool.cs` (new): refills FROM SCRATCH when stale (torch set / box chunk admit-evict / EditService write), floods with 157's oracle,
  `Encode`s to 157's chosen storage (0.2 m mixed cells, value = max; ESCAPE to 8 per-voxel values for split cells; pure-air bricks 2^3 cells
  at 0.4 m, trilinear), uploads 3 buffers. `SampleLevels` = the HLSL's C# twin; `Probe` = GPU readback. CPU sole writer; no frozen API or layout.
- `TorchFlood.hlsl` + `Resources/TorchFloodProbe.compute` (new); `Raymarch.compute`: kernels `CSMainFlood` / `CSMainFarShadeFlood`, every line
  under `#ifdef VE_TORCH_FLOOD` (strip them and the file == HEAD byte for byte). Tier-0 hits: flood + lava aggregates only; shells unchanged.
- `RaymarchFeature` picks the twin while `TorchFloodPool.Ready`; `LightSourceBuffer.Revision`; hooks in LightingDriver / EditService /
  Phase4Bootstrapper; `-torchflood 1 -tfi -tfk -tfreach -tfwrap` (defaults I 2.0, k 0.10, 12 m, wrap 0.3).
- Capture: `-torchreach 1 -trflood 1 [-trfloodb I,k,wrap]` (flood columns, GPU probe, OFF re-shoot, pillar row); cost arm `tf_on`.
  `tools/torchflood158.py`; `TorchFloodPoolTests` (9); TASKS.md paragraph.

## Evidence
- Fast suite PASS 969 FAIL 0 SKIP 15 (`Logs/overnight/158_tests.out`). Tests: sealed axis / x==z (450 escapes) / x+y+z plane / closed box
  EXACTLY 0 through the encoding; mixed cells never darker, <= 7 levels brighter; air bricks max 12 levels (0.28 m of path), mean 2.8.
- GPU == C# twin: 0 levels max difference at all 16 x 9 probed pixels in every row (tunnel, room, ground, pillar; both maps). GPU vs oracle
  max 7-10 levels (of 515); 2 of 132 ground pixels oracle-dark but GPU-lit (the reach-edge band, light ~0 there). `Logs/lookreview_20261031_095257.txt`.
- OFF: CSMain / CSMainFarShade compile from HEAD's source (strip check). In-launch re-shoot of `tr_ground_today_nat` after the pool existed:
  2,275 px differ, ALL on the animated river surface (`_RiverTime`), 0 px anywhere the torch lights. No cross-build raw still (a 19 min
  rebuild of HEAD): the source identity is the proof.
- Cost (warm steady state; warm-up `Logs/tg2world_20261031_101855.txt` discarded; `Logs/costsplit_2026-10-31_102459.txt`, 151's 31 torches,
  night, 540 rows, F X X F X X F): FULL (today) p50 62.0 ms (60.0 / 63.3 / 62.6), p99 76.8; tf_on p50 56.1 / 56.7, p99 67.3 / 67.8 ->
  **saved +5.6 / +6.3 ms p50** (noise 3.3 / 0.7), p99 -9 ms. Pool 11.21 MB GPU (162 lit chunks, 133,850 air + 18,794 mixed bricks, 14 escapes).
- Look sheet `Screenshots/158/torchflood158.png` (counts `torchflood158.txt`): lit px today / flood: tunnel 107,664 / 428,115, room 384,248 /
  2,073,600 (the whole frame), ground 80,085 / 542,137. First launch (no direction term, I 3.0) blew the room out (mean luma 209); the
  direction term brought it to 59.7 (analytic 24 m: 41.8). Pillar: lit front face, no crisp shadow (as Q1 of 155 chose).

## Unfinished / next
- L3 after Q1-Q3. Fluid writes do not refill the flood (L3); no pool cap (L4); coloured torch materials all reach the same brightness.
- The tunnel's flood row stored 90 air bricks in launch 1 and 385 in launch 2 for the same built tunnel (the brick headers differ between
  launches: uniform-air vs dense); harmless (both encodings are exact to the bound), noted.

## For the doc
- Frame time, 31 torches at night (warm, ABBA): today p50 62.0 ms -> flood 56.1-56.7 ms (**-5.6 / -6.3 ms**), p99 76.8 -> 67.3-67.8 ms
  (`Logs/costsplit_2026-10-31_102459.txt`).
- GPU pool 11.2 MB for those 31 torches; rebuild 9.8 s on the main thread (L3 must move and cut it); exact CPU copy would be 298 MB (Q1).
- Correctness: GPU sample == C# twin at 100% of probed pixels; sealed walls (incl. 0.1 m diagonals) exactly dark through the encoding.
- `Screenshots/158/torchflood158.png`: rows tunnel / sealed room / open ground / pillar at night; columns today, analytic 14 m, analytic 24 m
  (task 143), FLOOD 12 m (recommended map), FLOOD brighter map. Look at the room row's flood cells: the whole room is lit softly, but see the
  bright square under the torch and the straight wedges along the wall corners (Q3); the tunnel row's left wall shows vertical stripes.
- `Screenshots/158/torchcamp158.png`: the cost scene, left today, right flood. Look at the lower-left slope: today shows small dots of light
  round each post, the flood lights the slope out to ~12 m round each post.
