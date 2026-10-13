STOP_FOR_OWNER -- Q: should the shell fill also fill ENCLOSED fluids (the sealed underworld lava sea, sealed pockets)? The task
says "fluids are never filled", and with that rule shells 3-6 still need 1.04-1.66x their brick caps at spawn (target missed).
 (a) yes, enclosed fluids only (no ray from outside can reach them; sea, lakes and rivers stay): at spawn s3-s7 then FIT (bricks
     91.6k / 72.2k / 62.3k / 27.5k / 4.2k vs caps 102.4k / 102.4k / 102.4k / 71.7k / 17.3k). One switch, `ShellFillFluids`,
     already built and measured (off). Recommended.
 (b) keep fluids, raise s3-6 brick caps to the need x 1.25: about +190 MB of GPU memory. The run peaked at 2,951 MB (cap 3 GB).
 (c) keep as is: s3-6 stay FULL at most poses; the far band fills what they refuse.
# 83-engine-11d-shell-cave-fill: DONE except the target. Shells >= 3 fill enclosed air, keep cave entrances, flatten what nobody sees.
## What changed (commit `f858dd5`, branch engine-phase)
- `TerrainGenChunkSource.ShellFill.cs` (new): after generation and ops, for shells >= 3 (`FIRST_FILL_SHELL`, every preset):
  - Flood from air above h - V (V = one shell voxel), op boxes +-1, open chunk faces (halo heights); enclosed AIR takes the solid
    below, else above. Fluids are never filled.
  - Mouths: a solid voxel touching open air becomes air when >= 1/4 of its underground 4x4x4 sub-samples (not arch, hoop or ledge
    gaps) are cave air. A per-column prefilter (tunnel bands, stamps in reach) skips the rest.
  - Flatten: interior mixed bricks take their majority material; wholly interior chunks become one material (no page).
- Ops overload: generate, then apply ops, then fill. `ShellStreamer.CatchUp`: a filled chunk that missed an op is REDRAWN, not caught up.
- `ShellLayout.PageCapFor`: s1 / s2 pages = measured 7.70 / 6.96 per column x 1.25 = 1,540 / 1,392 (was 320 each).
- 8.14 row 13b. `ShellChunkTests`: exact with the fill off; with it on, exact at and above h - V (0 differ, s1-7). CLAUDE.md.
## Evidence
- `ShellFillTests` (6; Screenshots/83/fill83_tests.txt):
  - (i) 2.6 M outside rays (top + 4 sides, 80 chunks, s3-7): 0 differ. With mouths on, 96 differ, all at the 130 opened voxels.
  - (ii) 11 cave entrances (4 mouths, 4 fissures, 3 skull caves): shown in s3-6 11 -> 11, s7 4 -> 5; 0 lost. Tunnels never come
    within 6 m of the column top, so seed 42's entrances are stamps.
  - (iii) dense bricks drop to 19-61%. Prefilter byte-identical on 52 chunks. A far crater across a chunk face: 0 of 2.33 M samples closed.
- Whole suite **PASS 762 FAIL 0 SKIP 21** (Logs/overnight/83-all.out). run-phase7-rig **PASS 37 FAIL 0, 19/19 shots byte-identical**
  to 82b's run (Phase7Cycle/2026-10-12_180337 vs _143300).
- 77's streamer fill (fill_77method.txt): s6 67 -> 142 chunks stored, refusals 3 -> 1; s7 13 -> 16 stored, 3 -> 0.
- Poses run (`-shells 1 -shellwait 360`, Logs/overnight/83-poses.out): peak **2,951 MB** (80: 2,959). s3-6 store 2-7x more
  chunks per pose and are still FULL at most poses.
- Sheets looked at (83 vs 80):
  - peak_from_2km: the peak is one shell, and 80's light/dark split is gone.
  - glide_400m: no far-band patches.
  - coast: clean (OFF's cascade arc gone in ON). geyser: fewer grey patches.
  - oblique_3000m / map / rivers_oblique: still all far band. Shells 6-7 are FULL with earlier poses' chunks (s6 139 stored, 38
    missing; s7 20 stored); their box covers the island, so nothing is evicted (eviction not redesigned, per the task).
## Found (not fixed)
- **Shells 1-2 starve.** Coarsest-first ordering now spends minutes on s3-5 (no longer refused). At 75 s waits s1-2 held 0 chunks.
  `ShellResolve` then ends every ray that meets an absent finer chunk inside a coarser hole, so whole frames were far band
  (Screenshots/83/wait75/sheet_shells_1.jpg). Shells needed 127-296 s per pose to fill. Owner call: streaming order / hand-over.
- s1 / s2 need 237k / 361k bricks (caps 150k / 119k; fill is k >= 3 only). s3-5 pages over cap too (657 / 598 / 331 vs 320), not raised.
- Fill cost, Editor Burst, isolated, NOT a performance claim: shell chunk 122-178 -> 167-240 ms. The mouth rule is most of it.
## Next step: the owner's answer above; then 11c / the default switch per the plan.

## For the doc
- Dense bricks needed at spawn, before -> after (cap): s3 1,564k -> 106k (102k); s4 557k -> 142k (102k); s5 521k -> 170k (102k);
  s6 194k -> 94k (72k); s7 20.4k -> 13.3k (17.3k, fits). If enclosed fluids were also filled (Q (a)): 92k / 72k / 62k / 28k / 4k, all fit.
  Screenshots/83/need83.txt.
- Refusals over the 16 poses, run total s1-s7: 80 = 24/36/41/50/48/55/43 -> 83 = 18/30/41/41/45/44/6.
- Pages s1 / s2: cap 320 -> 1,540 / 1,392 (need 1,232 / 1,113).
- GPU memory per shell (bodies + pages + directory): s1 82 -> 102 MB, s2 66 -> 84 MB; s3-5 57.6 MB, s6 40.4, s7 9.7 (unchanged).
  Peak process 2,951 MB.
- Entrances: 11/11 seed-42 cave entrances still shown in shells 3-6 (s7 4 -> 5). The smallest mouth shown is a passage V/2 x V/2:
  s3 0.4 m, s4 0.8, s5 1.6, s6 3.2, s7 6.4 m. That is 1 px at each shell's near edge (275 m, 550 m, 1.1 km, 2.2 km, 4.4 km).
  Anything smaller is under a pixel there.
- `Screenshots/83/sheet_shells_1.jpg` next to `Screenshots/80/sheet_shells_1.jpg`. Rows: peak_from_2km, summit, glide_400m,
  coast, oblique_3000m; columns: OFF / ON / tint.
  - Row 1: the peak is one magenta shell, and 80's grey/cyan split is gone.
  - Row 3: glide is all shells, with none of 80's grey patches.
  - Row 5: still grey (shells 6-7 full).
- `Screenshots/83/wait75/sheet_shells_1.jpg`: same poses after 75 s; rows 1 and 3 all grey in the tint (s1-2 starved, above).
