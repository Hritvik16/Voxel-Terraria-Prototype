# 226-look-lotl-polish: DONE -- four look pieces, each its own switch, all ON (pictures better, cost small); commit d428f9d

## What changed (Raymarch.compute, RaymarchFeature.cs, KernelHealth.cs, PlaygroundCapture.CostSplit.cs, LotlLookTests.cs, TASKS.md)
1. Per-voxel colour (`-voxvary`, `_LotlLook` bit 0, `LotlVoxelVary`): each voxel gets x(1 +- A) brightness and +-3 % warm/cool from two
   position hashes. The hashes are tied to world position, so the colour does not flicker under FSR/TAA, and the mean colour is unchanged.
   A comes from the material's own look albedo: 12 % on the darkest, 6 % on white, so light materials do not clip. No hand-picked
   material list. It fades out where a voxel is 4 -> 2 px, which avoids aliasing. Fluids, self-lit and emitting materials are skipped.
2. Crevice shade (`-crevice`, bit 1): the existing FaceAO (the hit's own 8 occupancy reads) now also takes half its depth off the DIRECT
   light. Before, it darkened only the ambient, so crevices never showed on sunlit faces. No new read. It fades where VAO fades (20-50 m).
3. Aerial haze (`-aerial`, bit 2; `-aerialrange`): the sky-haze range goes 1800 -> 1100 m, with the same exponential height falloff.
   Haze desaturation goes 0.5 -> 0.75. Only constants change.
4. Bloom (`-bloom`, `-bloomk`): new kernels BloomDown (quarter of the render rows; per-pixel bright share smoothstep(0.55,1.0) of linear luma),
   BloomBlur x2 (separable Gaussian, sigma 2.5 quarter-texels) and BloomAdd (render rows, 4 loads + read-modify-write). They run after the
   frame's last writer and before FSR 3, so there is NO pass at output resolution. Look 1 only.
- Capture arms: `lotl_off` (= before), `vary_only`, `crevice_only`, `aerial_only`, `bloom_only`, plus `X_off`.
## Evidence
- Fast suite `PASS 1071 FAIL 0 SKIP 25` (`Logs/overnight/226/fast.out`). `LotlLookTests` 5/5: flags, gating (OFF = old path), bloom
  order (after TAA, before FSR 3), and a CPU mirror of the variation (mean within 1 %, bounded by A, stable, exactly 1 when faded).
  `SkyLookTests` anchor updated for the new range expression.
- Launches: day (walk_near, forest_eye, jungle_eye x 6 arms) and night (torch_camp x 4 arms), High, 432 rows, `-costtimed 1`
  (`Logs/overnight/226/run.sh`; reports `costsplit_day.txt`, `costsplit_night.txt`). Diff table: `Logs/overnight/226/diff.txt`.
  Every arm differs from "before" by only ~1-3 levels mean. Part of that is cross-arm noise of unmeasured size (an arm whose piece
  should change little, e.g. bloom at walk_near, still reads 1.6). Judge the pieces from the pictures, not that table.
- What the pictures show: vary is clearly visible. The flat walk_near wall becomes a patchwork of slightly different stones (the biggest
  step toward LotL). Aerial makes forest_eye's far mountain paler and blue-grey, softer against the sky. Crevice is subtle by day: a little
  darkening at concave step corners. Bloom is subtle by day (sky edges round the jungle canopy), but at night each torch head gets a soft glow halo.
- Rule 11/12 (P1): only bloom adds GPU bytes: two quarter-res half4 render-graph transients = 2 x 192 x 108 x 8 B = 0.33 MB at
  768x432 (0.52 MB at 960x540). Pieces 1-3 add 0 bytes. The OFF arms run in the same launch, so peak memory is shared: process peak
  2,548 MB (day) / 2,558 MB (night), vs 2,492 / 2,515 in 225's launches (different poses, no streaming change). GPU refusals: 0 at
  walk_near and forest_eye, 7,979 at jungle_eye. That is the same path effect 225 saw (5,104), from the pool already full after forest
  (254,398 / 254,400). It is not this change: no streaming or pool code was touched. CPU pool peak 496,935 (valve 484,500), the same as 225.
- P2 pass cards (no frame timing; rough cost class): (1) vary: render rows, every frame, 2 hashes + ~10 ALU per hit pixel, 0 B, rides
  the hit shading ("sky fill"/shade step share unchanged). (2) crevice: 1 lerp + mul per hit pixel, 0 reads, 0 B. (3) aerial: 0 extra
  ops (constants). (4) bloom: NEW pass, 3 kernels at quarter render res (16 + 2x16 loads per quarter texel) + 1 at render rows (4 loads +
  RMW), every frame, 0.33 MB. It does not run at output resolution and is not a ray march. Estimated as the cheapest pass in the frame
  (well under 0.5 ms), but not timed. Its own switch is `-bloom`, with capture arm `bloom_off`. P4: no content change. P5: no ray-march change.
  The new pass is below output resolution, nothing printed OVER SHARE, and P1 is within budget, so this task is not HEAVY.
## Left / next
- The budget-check task (230) could time `bloom_off` to give bloom's real ms.
- LotL gaps this does NOT close: green/warm multi-bounce light in the shade (jungle stays dark; see 225), volumetric clouds and a paler
  sky (our zenith is still saturated blue), denser hand-shaped trees, authored objects, turquoise water.
- Aerial 1100 m: at 2 km the haze is ~84 % (was 67 %). Far ridges stay visible as silhouettes, and the 6.5 km terrain still draws, but far
  terrain reads softer. `-aerialrange 1800` (or `-aerial 0`) gives the old haze back if the owner wants crisper far views. Nothing is
  simulated or streamed differently.
- The night launch first ran with the wrong arms (my sed missed). It was re-run. The night stills are from the second run.
- Not committed (not mine): `Assets/Scenes/Playground.unity` (scene builder), `Logs/TerrainGen/*` edits.

## For the doc
- Four look pieces, all ON: per-voxel colour, crevice shade, aerial haze, bloom. Each has its own switch (`-voxvary/-crevice/-aerial/-bloom 0` = off).
- GPU memory added: 0 MB for pieces 1-3, 0.33 MB for bloom (quarter-res). No pass at output resolution. Process peak 2,548 MB (day launch).
- Haze range 1800 -> 1100 m: far mountains hazier and bluer. Desaturation 0.5 -> 0.75.
- Night torch_camp: median luma 24.4 -> 24.6, black px 10.5 -> 9.9 % (`Logs/overnight/226/diff.txt`). Torch heads now glow.
- `Screenshots/226/sheets/doc_sheet.png`: rows walk_near / forest_eye / jungle_eye / torch_camp; columns before, + each piece alone,
  all four. Look at the walk_near wall (column 2 vs 1): flat grey becomes varied stones. Look at the torch row (column 5): glow round torches.
- `Screenshots/226/sheets/forest_eye_full_before.png` / `forest_eye_full_after.png`: full-size pair. Flip between them and watch the far
  mountain top-left turn paler and bluer, and the bark and grass get per-voxel variation.
- `Screenshots/226/sheets/sheet_lotl.png`: ours before | ours after | Lay of the Land. Closer: stone and bark read as many slightly
  different blocks, and far hills fade toward the sky. Still far: their lit, warm shade, clouds and pale sky, big trees, and buildings.
