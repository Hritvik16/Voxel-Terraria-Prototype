# 176-worldgen-c1-biome-layout

Model: opus

Approval: owner, 2026-11-05, chat (173's Q1 (a) "cover first", Q2 (a) "vary climate"). WORLD GEN, look stage. Read `WORLD_GEN_LOOK_PLAN.md`
C1, `tools/overnight/blocked/173-plan-world-gen-look.HANDOFF.md`, the memory rule no-terrain-specific-engineering (general, data-derived,
proven on several seeds, never tuned to seed 42), and `DESIGN_LARGE_WORLD_AND_TERRAIN_OVERHAUL.md` §2 + the brief's Decided rows.
Owner design direction (2026-11-05): "a wacky multi-topography like One Piece but with a realistic twist" -- distinct, varied regions.

## Do
C1 exactly as the plan: (1) the climate field varies ACROSS the island instead of ramping (scale relative to the island radius and/or a low
second octave; NO domain warp, owner rule); (2) replace the per-voxel border dither with patch-wise transition bands.

## Acceptance (measured on 8 seeds, not a look at seed 42)
- no biome border straight for more than ~1.5 km; every biome in 2+ separate regions; land shares within the brief's targets (easy land, 6 biomes);
- the island map (as `survey173_map.png`) for 4 seeds side by side, before | after, captioned;
- re-shoot 173's worst poses (`feature_great-peak-0`, the start area, one border) with `-worldsurvey`, before | after, full size.
- TerrainGen change: run the WHOLE suite (`SUITE=all ./run-editmode-tests.sh`), not only the fast one. Update any test whose expected
  biome map legitimately moved and say which and why.
- Note for the owner: biome borders move on every seed, so existing saves' deltas would land on shifted terrain.
- Rough cost note from runs you already do (look-stage rule); no timing arms.

## End
STOP_FOR_OWNER with the before | after maps and poses in a full-size viewer: "keep this layout?"
