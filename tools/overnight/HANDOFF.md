# 188-trees-pool-overflow-and-cost: DONE. Trees (`-trees 1`) no longer overflow the shell pools. The fix is exact: 2-value shell bricks are packed. Trees stay default OFF; the owner decides from the table.

## Cause (found before changing anything)
- `TreePoolTests.Pools_ForestPose` / `Pools_JunglePose` generate every chunk shells 1-5 store at 180's poses (seed 42, High). Arms: OFF, ON, and ADVERSARIAL. ADV is a test-only `treesOn = 2 + biome`: every tree cell that passes the physical rules gets a tree, i.e. a solid forest cover.
- Canopies are a second surface over the ground, so trees add dense bricks. **92 % of the added bricks hold exactly 2 values** (leaf or wood + air), yet each took a full 512 B body.
- **180's FULL was at the JUNGLE pose, not the forest one.** The `[shells]` line prints BEFORE its `WSPOSE` line. With trees ON at the jungle pose, shells 1 / 2 were at 120 / 117 % of PoolCap.

## Fix (commits f3c4792, c7e5d61)
- `Mirror/ShellBrickPack.cs` (an Appendix A addition): a dense shell brick with <= 2 values is stored as 17 uints (`a | b<<8` + a 512-bit plane), packed 7 per 128-uint body. Handle bit 30 = COMPACT; full bricks are unchanged.
- It is decided per brick from its own voxels, for terrain bricks too. No cap, no constant. Tier 0 is untouched.
- `ShellStore.Store`: Burst `ClassifyJob` plus `StageJob`; the job replaces the old managed body copy. The pool unit is still the body.
- Readers that decode the same bits:
  - `Raymarch.compute` `ShellBrickVoxel` (both shell lookups)
  - `ShellRayReference.IView.VoxelOf`
  - `ShellValidator.CompareShadow`
- `-shellpack 0` is a diagnostic flag that restores the old storage.
- Player capture prints a `[pools]` line (bodies/pages used/peak/cap) after each `[shells]` line. It was added to the world-survey path after the shots, so the shots don't show it.
- Tests: new `TreePoolTests.CompactBricks_StoreIsByteExact_PoolCountsBodies`. Two tests now count in bodies with unchanged intent: the `EditOpRedrawTests` replace test, and `ShellSkyExitTests.Store_RaisesTheBound` (its refused chunk now uses 3-value bricks).
- Docs: TASKS 188 paragraph; KNOWN_ISSUES has a new top entry.

## Evidence
- Whole suite **PASS 1037 FAIL 0 SKIP 27** (`Logs/overnight/188/all1.out`); 187a was 1036/0/25. All 180 `TreeTests` are green. The +2 skips are the two Explicit reports.
- Pool reports: `Logs/TerrainGen/trees188/pools_forest.txt`, `pools_jungle.txt`. They give bricks (= bodies before) and bodies (after), pages, upload MB and generation ms.
- Player, one build, `-trees 1`, 45 s wait (`Logs/overnight/188_shots.out`), jungle pose:
  - BEFORE (`-shellpack 0`): `s1 1789/378 FULL, s2 1481/689`, 3,456 stored.
  - AFTER: `s1 2170/0, s2 2170/0`, no FULL, 5,437 stored.
- Generation cost: trees add +0.1 to +4.9 % per stored chunk ON, and up to +9.6 % ADV. That is the column pass +1-21 % (worse at shell 5) plus +23-88 % voxel+fill, but only on the ~15-20 % of chunks the trees reach.
- **180's 55-74 -> 89-156 ms worker mean does not reproduce in the generator.** That mean is averaged over a different shell mix per arm (a shell-5 chunk costs 6x a shell-1 chunk). It is not a per-chunk comparison. This task does not change generation cost.

## Unfinished / notes
- Shell 3 is the closest to its caps:
  - ADV bodies 89-94 %.
  - Its PAGE table (cap 320) runs at 69-84 % with or without trees.
  - Not a tree issue. If a denser cover ever comes, look there first.
- The main-thread cost of the Burst classify is not measured (no rig, per the rules). It replaces a managed 8-byte-MemCpy loop, and upload bytes drop 10-56 %.
- No black-shadow work (left for the lighting task).

## For the doc
| pose, shell | trees ON: before -> after (% of PoolCap) | adversarial cover: before -> after |
|---|---|---|
| jungle s1 | 120 % OVER -> **49 %** | 134 % OVER -> **51 %** |
| jungle s2 | 117 % OVER -> **63 %** | 142 % OVER -> **71 %** |
| forest s1 / s2 | 80 / 78 % -> **41 / 51 %** | 149 / 157 % OVER -> **55 / 76 %** |
| forest s3 / s4 | 80 / 73 % -> **56 / 55 %** | 145 / 106 % OVER -> **94 / 77 %** |
- Jungle s1 upload, trees ON: 92.6 -> 40.5 MB. Trees OFF also gains (s1 forest 42 -> 33 %). Source: `pools_*.txt`.
- Generation time per stored chunk, ON vs OFF: +0.1 to +4.9 % (ADV <= +9.6 %), unchanged by this task (`pools_*.txt`).
- Player, jungle pose, 45 s: before `s1 1789/378 FULL`; after `s1 2170/0, s2 2170/0`, 3,456 -> 5,437 chunks stored.
- Images: BEFORE `LookReview/2026-11-12_144829_high/`, AFTER `LookReview/2026-11-12_150058_high/` (under ~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/).
  - `ws_fixed_jungle_native.png`: near canopy identical. In the lower right, through the trunks, BEFORE shows a red-brown wall where AFTER shows beige ground. Those are shell chunks that BEFORE was missing (pool FULL), so a coarser shell or the far band showed instead.
  - `ws_fixed_jungle_far_native.png` (220 m up, the jungle canopy across shells 1-3 over a pale plain and the sea): looks identical side by side (compared at 960 px, not diffed). This shows the compact decode in the shader is exact. The grey box left of the lake is task 190's known artefact.
