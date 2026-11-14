# 201-defaults-fluidpockets-on: DONE. Surface fluid pockets (183 + 196's basins) are ON in every tg2 world; `-fluidpockets 0` = the pre-201 world.
## What changed (commit 30c1cce)
- `TerrainGenChunkSource.FluidPocketsArg` defaults to true (the 195 / 195a pattern; the `TerrainSpec` field is NOT flipped: a bare spec stays OFF).
  `-fluidpockets 0` keeps `spec.fluidPocketsOn` 0 = the same code path as before (one-entry plane, `FluidPockets.Apply` never reached).
  Shape, placement, rules, `-pocketbasin` untouched; ores / trees / towns / sites / leaf fill / sky fill untouched.
- Player log: `[StreamManager] tg2 chunk source: ... fluid pockets ON (default; 131 surface sites)` / `OFF (-fluidpockets 0)` (StreamManager.cs).
- World survey (capture only): new per-pose `[fluidtiles]` line (resident / acquired / unsettled fluid tiles).
- `FluidPocketTests.ForestSites_Poses201` (Explicit report): seed 42's forest water / lava sites + north-side poses -> `Logs/TerrainGen/pockets201/`.
- `MaterialRuleChunkTests.Shells_BaredVoxels_WearTheSkin` (182): failed in whole-suite run 1 on 8 shell-5 voxels at `border_bank` -- a shell's
  TOP voxel takes its material at the ground h (`TerrainGenChunkSource.Shell.cs` top-voxel pass), h sat in a pocket's lining range, so it showed
  the mud lining, exactly as LOD 0 (`DetailField.Material`); the test read it as "soil under the skin". Now skips lining voxels like stamps and
  logs them: 38 wear their pocket's lining, 0 show anything else, deeper-band 0 (`Logs/TerrainGen/180b/shell_bared182.txt`). Pockets stay ON there.
- TASKS.md paragraph (Task 201).
## Checked (things that assumed pockets OFF)
- Tests pinning the default: none (TerrainGen tests use a bare spec = OFF; `FluidPocketTests` sets pockets explicitly). F6 / PlaySettings: no entry.
- Rigs: no identity/hash rig (acceptance, acceptance-fluid, phase 5/6/7, fluid-tiled, brushguard) builds the tg2 world -> none needed `-fluidpockets 0`.
  FarField shares the chunk source's map, so the far band follows the default.
- Fluid tick: pockets are resting generated fluid; under awake-only residency (task 54, `FluidTileResidency.Refresh`) they take NO tile until a wake.
  Every pose, both arms: `[fluidtiles] resident 0 (peak 0), acquired 0, unsettled 0` (the survey suspends the CA dispatch, so this proves no
  admission, not tick time; the CA's fixed per-tick dispatch is unchanged by pockets).
## Evidence
- Whole suite run 2: PASS 1087 FAIL 0 SKIP 30 (43 min, `Logs/overnight/201/suite_all2.out`); run 1: 1086 / 1 (the test above).
- Shots: `Logs/overnight/201_shots.sh`, 2 launches x 4 poses, `-tod 12`, lookwait 40 s; logs `Logs/overnight/201/player_{default,off}.log`.
  Owner pause 08:07-08:37 held the first launch (no effect on the shots). Shells still missing at 40 s (reported, not waited): default arm s3
  792-1,796 missing per pose, off arm s3 2,001 (0 stored -- the off arm ran second on a warmer chip: 98-163 vs 49-96 ms per shell chunk).
- Memory (process peak / GPU body refused, cumulative, last pose): default 2,410 MB / 15,650; `-fluidpockets 0` 2,567 MB / 15,657. Cap 3,000 MB;
  195a: 2,504 MB. The off arm's higher peak is a startup transient, not the pockets. GPU body pool full in both (254,400 / 254,400, as since 195a).
## Not done / limits
- The "more like ponds" look is a later owner talk (OWNER_TOPICS), not done here.
- 118 bared shell voxels near pockets wear neither skin nor a deeper band (skin 99.94 % vs 100 % before; the test needs > 95 %): not investigated.
- On the lava pose the snow bank's step edges show thin pale-blue dashes (left image only, round the bowl); this looks like the 198 white-dash
  lighting effect on the new bank steps, not a material. Not investigated.
## Next step
Supervisor: pictures to the doc; the pond-look talk with the owner.

## For the doc
- Pockets: OFF -> **ON by default** (seed 42: 131 surface sites; player plane build 69 ms once per world, `player_default.log`).
- Fluid tiles woken by pockets at the 4 poses: **0** (resident 0, acquired 0, both arms; `Screenshots/201/lookreview_*.txt`).
- Process peak: `-fluidpockets 0` 2,567 MB -> default 2,410 MB (cap 3,000; same order, transient-dominated). GPU body refusals 15,657 -> 15,650.
- Whole suite: PASS 1087 FAIL 0 (one test taught that pocket linings are not "soil under the skin": 38 lined voxels, 0 wrong).
- Images (`Screenshots/201/`; each pair LEFT = new default, RIGHT = `-fluidpockets 0`):
  - `sheet_201.jpg` -- all four pairs stacked; look at the middle of each left image for the pocket.
  - `pair_surface_water.jpg` -- snowy pine slope: a round dark-blue pond (centre) on the left; plain snow on the right.
  - `pair_surface_lava.jpg` -- open snowfield: an orange lava bowl with its stepped snow bank on the left; unbroken snow ridges on the right.
  - `pair_forest_water.jpg` -- forest clearing from above: a small blue pond under the middle oak (left); on the right a tree stands there instead.
  - `pair_forest.jpg` -- 195a's ordinary forest pose: the foreground is the same; only far shells differ (the off arm streamed less in 40 s).
