# 195-defaults-ores-on: DONE. Rule ores (task 181) are the default in every tg2 world; `-ores 0` = the pre-195 world byte for byte.

## What changed (commit f658110)
- `TerrainGenChunkSource.OresArg` defaults to true (`Assets/CoreEngine/WorldGen/TerrainGenChunkSource.Ores.cs`). `-ores 0` leaves
  `spec.oresOn` 0 = the exact pre-195 code path (step 3's `Ores.cs`).
- NOT 187a/190b's pattern on purpose: the `TerrainSpec` field is not flipped. The rules need the ore cell plane that only the chunk
  source builds; a bare spec with ores on would read cell 0 (wrong ores, not just different). Bare TerrainGen specs stay OFF.
- Player log now says `rule ores ON (default; plane 4096 KB)` / `OFF (-ores 0)` in the `[StreamManager] tg2 chunk source` line.
- Docs: TerrainSpec / OreRules headers; TASKS.md paragraph 195.
- Places that assumed OFF: no F6/PlaySettings entry exists; no identity/hash rig (acceptance Gates C/D) uses the tg2 world, so no rig
  needed `-ores 0`. Two tests did:
  - `TerrainGenChunkSourceTests.RefJob` (TerrainGen reference) had no ore cell plane -> `AirFastPath_IsExact` failed at surface - 3
    (18 voxels). It now carries the source's plane like ColumnJob does. Test fix, not a product change.
  - `BrickSkipTests`: plain proofs now cover ores ON; the `_Ores` twins became `_OresOff` (cover `-ores 0`), restoring the prior value.
- Leaf fill, trees, towns, fluid pockets, sites: untouched.

## Evidence
- Whole suite (after the fix): PASS 1077 FAIL 0 SKIP 28 (`Logs/overnight/195/all2.out`). First run: PASS 1076 FAIL 1 (the RefJob above,
  `Logs/overnight/195/all.out`). Filter run TerrainGenChunkSourceTests 3/3 pass.
- Generator proof that ON vs OFF only swaps rock for ore (never air/fluid) at the three wall sites: `OreRuleTests.Sites_OnVsOff_...` (Slow, passed).
- Shots: one build, a launch per arm, 181's three wall poses, `-tod 12`, High, native 1080 (`Logs/overnight/195_shots.sh`, `look_*.out`, `player_*.log`).
- Memory (process figures these launches print, `[mem]` line, `Screenshots/195/lookreview_*.txt`): at the last pose, now 2365.5 MB (default)
  vs 2318.0 MB (`-ores 0`), +47 MB; launch peak 2457.1 vs 2572.5 MB (a peak is noisy, it went the other way). Ore plane 4,096 KB.
- Rough cost seen in those launches (not a timing arm, cold, ON arm ran first): shell chunk generation 129/120/114 ms vs 88/85/91 ms per
  chunk on a worker (+30-40 %). Generation is already the streaming bottleneck (KNOWN_ISSUES 10/16b): worth a line in the perf stage.

## Noticed, NOT changed
1. The first `-ores 0` launch drew NO badlands wall at all (open sea where a 50 m rock face stands; settle frame 137 vs 191 ms = less
   drawn; refused 0). Same pose relaunched: the wall is there. Generation is proven the same (test above), so this is a streaming/draw
   miss in one launch -- likely the same family as 194b's note (194's pits fix looks incomplete, 1 of 3 launches).
   Image: `Screenshots/195/badlands_wall_ores0_BROKEN_first_launch.png` (whole frame: sea + far hills; the wall should fill the left 3/4).

## Next
Queue: 195a (trees + towns ON by default), then 196.

## For the doc
- Rule ores: OFF -> ON by default (`-ores 0` gives back the old world exactly). Whole suite PASS 1077 FAIL 0 SKIP 28 (`Logs/overnight/195/all2.out`).
- Memory at the last pose: 2318 -> 2365 MB (+47 MB; the ore plane itself is 4 MB), cap 3,000 MB (`Screenshots/195/lookreview_*.txt`).
- Rough generation cost: shell chunk 85-91 -> 114-129 ms each on a worker (+30-40 %, cold launches, not a timing measurement).
- `Screenshots/195/sheet_badlands_wall.jpg` -- left new default, right `-ores 0`, same frame: the yellow streaks (top right, middle) and the two
  green lenses (left of centre, bottom centre) on the rock face are the new rule-ore veins/deposits; the right panel has none. The orange
  streak at the bottom left is in both (old placement).
- `Screenshots/195/sheet_tall_cliff.jpg` -- left default: ~10 small green lenses and two orange specks scattered across the grey wall around
  the dark slot; right `-ores 0`: plain wall. (The slot's black interior is unlit shade, same in both.)
- `Screenshots/195/sheet_deep_canyon.jpg` -- red canyon walls: left has a few small yellow-green flecks in the upper-left wall; right none. Subtle.
