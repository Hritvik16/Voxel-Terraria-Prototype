# 98-gameplay-start-mild-biome: DONE. Play now starts in a mild biome. Seed 42 moved from a snow plain to a forest plain.

## What changed (commit `ad8e1dd`, branch engine-phase)
- `Assets/TerrainGen/PlayStart.cs`:
  - **Mild biomes** are `GenBiome.Forest` (0) and `GenBiome.Savanna` (4). These are the two biomes Danger's tier table rates 0.
    Snow, Desert, Jungle (tier 2) and Badlands (tier 1) are not mild. There is no separate "grass" or "plains" biome id. Plains is a landform.
  - An area's **mild size** is the number of its cells whose biome is mild.
  - Areas whose mild size is at least `MIN_MILD_HA` = **2 ha** (313 cells of 8 m, a small clearing about 140 m across) are ranked by mild size.
    The ranking is 93c1's rule unchanged: the largest, then areas within AREA_TIE 0.9 count as tied and the one nearest the island centre wins.
  - The start cell and its 8 neighbours must also be mild, on top of 93c1's checks (easy, Walk band, dry).
  - **Fallback:** if no area has 2 ha of mild cells, or no mild cell passes the checks, 93c1's rule runs on any biome.
  - New API: `IsMild`, `MildSizes`, `MinMildCells`, `BestMildArea`, `ChooseArea`, `Find(map, out pick, preferMild = true)`.
    `Pick` now carries `mild` and `biome`. `Find` is still a pure function of the map, and its old signature still works.
- `-playstartmild 0` brings back 93c1's rule (`Playground.PlayStartPrefersMild`, read in `Phase4Bootstrapper`).
  Rigs, capture runs and `-playstart 0` still use `Tg2Spawn`. KEEP=1 still resumes the saved pose; that path was not touched.
- `PlayStartTests`:
  - Seed 31337 added to the seed cases.
  - Each seed logs its before (93c1) and after pick.
  - New assertion: seed 42 must start mild.
  - New test `ChooseArea_MildFirst_FallsBackToLargest`, on a hand-made record. It covers: no mild area -> the largest; a mild part 1 cell under 2 ha does not count; at 2 ha a small forest area beats a large snow area; ties go by mild size, then nearest the centre; same inputs give the same answer.
- CLAUDE.md task-93c1 line updated.
- **A choice the owner may want to know about:** "largest" is counted in MILD cells, not whole cells. B4's areas are all about the same size (43-45 ha), so counting whole cells would let a mostly-snow area that sits nearer the centre win over a mostly-forest one. The side effect: seed 31337's start was already in forest and it still moved, to the area with more forest.

## Evidence
- Filtered run `PlayStartTests`: 7/7 pass (`Logs/overnight/98_filter.log`). Fast suite: **PASS 837 FAIL 0 SKIP 14**, wall 4m19s.
- Start per seed, before (93c1) -> after (heights are ground above sea; area mild share in brackets):

| seed | before | after |
|---|---|---|
| 42 | Snow, 44 ha (8 ha mild), +119.7 m | **Forest**, 44 ha (14 ha mild), +27.3 m |
| 7 | Jungle, 43 ha (24 ha mild), +18.0 m | **Forest**, 43 ha (27 ha mild), +49.7 m |
| 2026 | Desert, 45 ha (3 ha mild), +41.3 m | **Savanna**, 45 ha (42 ha mild), +37.1 m |
| 31337 | Forest, 43 ha (26 ha mild), +42.8 m | Forest, 43 ha (32 ha mild), +35.0 m (other area) |

- Seed 42 log lines are in `Screenshots/98/after.out` and `before.out`:
  - after: `area 2 cell 292305 column (37240, 22840) ground 5393 interior True mild True biome 0`
  - before: `area 1 ... biome 2`
- Two short launches. One `BUILD=1 SHOT=60 EXTRA="-playshotquit"` run gave the after shot; one more run, with `-playstartmild 0` added, gave a matching before shot. Both are at `-startview` noon, facing the most open direction. The HUD's left panel is still drawn.

## Left / next
- Not checked: whether the sun is behind the camera. `-startview` faces the most open direction at noon, so the sun is high in both shots and they are well lit.
- Next: whatever is next in the queue. No follow-up added.

## For the doc
- Seed 42 start: Snow plain +119.7 m above sea -> **Forest** plain +27.3 m (`Screenshots/98/after.out`, `before.out`).
- All 4 tested seeds now start in a mild biome (before: 1 of 4). Biomes: 42 Snow->Forest, 7 Jungle->Forest, 2026 Desert->Savanna, 31337 Forest->Forest.
- Mild share of the chosen area, seed 2026: 3 ha -> 42 ha of 45 ha (`Logs/overnight/98_filter.log`, `[PlayStart]` lines).
- Images:
  - `Screenshots/98/start_seed42_before.png`: the 93c1 start at noon. Almost the whole lower half is white snow plain with ice-blue streaks; the orange-banded mountains are far off on the right.
  - `Screenshots/98/start_seed42_after.png`: the new start at noon. Green grass fills the right and bottom of the frame and banded badlands ridges run along the horizon. In the minimap (top right) the marker sits inside the green forest patch, with the grey snow region to its west.
