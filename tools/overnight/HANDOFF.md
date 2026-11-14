# 195a-defaults-trees-towns-on: DONE. Trees and towns are now ON in every tg2 world by default; `-trees 0 -towns 0` gives back the pre-195a world byte for byte.

## What changed (commit 303f564)
- `TerrainGenChunkSource.TreesArg` / `TownsArg` default to true (`TerrainGenChunkSource.Trees.cs`, `.Towns.cs`). FarField's own map reads
  `TownsArg` too, so the far band draws the flattened town pads by default.
- Sites (asked to check `Towns.cs`): towns are placed ON the sites, and `-towns` already forces `spec.sitesOn = 1` (constructor line ~280
  and FarField). So `-sites` keeps its OFF default (records only) and needed no flip. `-trees 0 -towns 0` leaves treesOn/townsOn/sitesOn 0,
  which is the same code path as before.
- Same approach as 195: the `TerrainSpec` fields are NOT flipped, so a bare spec stays OFF (trees need the source's tree plane, towns its
  town plane). Docs updated in TerrainSpec.
- Player log: `[StreamManager] tg2 chunk source: ... trees ON (default), towns ON (default), sites ON` / `trees OFF (-trees 0), towns OFF (-towns 0), sites OFF`.
- Things that assumed OFF: there's no F6/PlaySettings entry for either, and no identity/hash rig uses the tg2 world, so no rig needed the
  `0` flags. One test fixture did: `MaterialRuleChunkTests` (180b: "the rules repaint solids only, air/fluid unchanged ON vs OFF") failed
  2 cases (oasis_or_swamp LOD 0, cliff_forest shell 3). Cause: a tree's density reads the column's cover slot, and the material rules set
  that slot (`GroundRules.cs:444`, "trees read the Cover slot"). So rules ON vs OFF moves trees, and with them the air around them. That
  is by design, not a bug. The fixture now pins trees OFF; towns stay ON there and passed.
- TASKS.md paragraph 195a. Ores, fluid pockets, leaf fill (45) and sky fill are untouched.

## Evidence
- Whole suite: run 1 PASS 1075 FAIL 2 (the fixture above, `Logs/overnight/195a/all.out`); filter run MaterialRuleChunkTests 6/6; final run
  **PASS 1077 FAIL 0 SKIP 28** (`Logs/overnight/195a/all2.out`).
- Shots: one build, one launch per arm (the flags are per launch). Script `Logs/overnight/195a_shots.sh`: seed 42, `-tod 12` (sun 78 deg),
  High, native 1080, `-lookwait 45`, 191's forest, snow, town_eye and village_eye. Per-pose figures are in `Screenshots/195a/lookreview_{default,off}.txt`.
- Memory, process peak: **2,503.6 MB default vs 2,462.9 MB off** (+41 MB). Cap 3,000 MB, so no STOP.
- Near GPU body pool (cap 254,400), refused totals by pose (forest / snow / town / village):
  default 10,032 / 10,096 / 15,657 / 22,105, pool full at every pose; off 0 / 0 / 57 / 93. Same pattern as 191's all-ON run.
  None of these shows a hole: at the village pose, 38 of the 45 still-refused chunks are drawn solid by tier 0 below and all are stored by shell 1.
- Rough generation cost (not a timing arm, cold): shell chunk 107-122 ms vs 82-95 ms per chunk on a worker (+25-30 %).

## Noticed, NOT changed
1. The near body pool is full at every pose in the new default world. No pits are visible in these 4 frames, but 197 (pits returning)
   should be checked on the trees+towns default now, not the old one. Worth a line in the perf stage (3 GB / pools).
2. In these capture shots, shaded tree undersides and sunless house walls are pure black. Look review runs sky fill 0; play's default is 2 (194a).
   Not a regression.

## Next
Queue: 196 / 197. No follow-up added.

## For the doc
- Trees + towns: OFF -> ON by default (`-trees 0 -towns 0` = old world exactly). Whole suite PASS 1077 FAIL 0 SKIP 28 (`Logs/overnight/195a/all2.out`).
- Process memory peak: 2,463 -> 2,504 MB (cap 3,000) (`Screenshots/195a/lookreview_*.txt`).
- Near GPU body pool refusals at the forest pose: 0 -> 10,032 (pool full at all 4 poses; no visible holes in the shots).
- Rough shell chunk generation: 82-95 -> 107-122 ms per chunk on a worker (cold launches, not a timing measurement).
- `Screenshots/195a/sheet_forest.jpg`: left new default, right old. A dense broadleaf forest now covers the meadow; a small town (thatched roofs,
  right of centre, middle distance) sits where the right panel shows open grass and the river.
- `Screenshots/195a/sheet_snow.jpg`: left, a snow-floor conifer forest with tree shadows on the snow; right, bare snow plain. Same cliff and peaks behind.
- `Screenshots/195a/sheet_town_eye.jpg` / `sheet_village_eye.jpg`: left, houses and halls on a flat pad, with a path (town) or flattened
  sand (village); right, bumpy grass / dunes with no buildings. The pitch-black walls are capture sky fill 0, not the town.
