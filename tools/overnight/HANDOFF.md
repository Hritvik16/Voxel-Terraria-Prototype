# 45-engine-1-one-generator-path: DONE. Editor map == player map (Burst both sides), SUITE=all 665/0/18.

## What changed (branch `engine-phase`, made from `surface-rebuild` HEAD)
- `1def456` merge `gemini/lava-torch-light` (bd60a30; code reviewed: lava's combine drops pointTerm/UniformFaceHue only).
- `5410b23` the port. The WHOLE map build (regions, feature draw + placement search, easy areas, erosion, fine map,
  hydrology, world record, routes, `Offsets.For`'s drawn volcano) runs in ONE Burst job (Strict/High,
  CompileSynchronously) in Editor and player. Same algorithms; arrays became `MapArena` pointers.
  - `TerrainGen` keeps its portability contract (TerrainSpec.cs, Unity.Mathematics only; test still green): the IJob
    is in a new companion assembly `Assets/TerrainGen/Burst/` (`TerrainGen.Burst`, registers itself at load).
    `MapJob.cs` = job body + conversions to WorldMap/WorldRecord/Feature/Route/EasyArea. Public APIs unchanged
    (`WorldMap.Build`, `WorldRecord.Build/BiomePlane`, `Offsets.For`, `Regions.Build/LandCells`, `Features.Draw` all
    run through the job).
  - No managed fallback: a `[BurstDiscard]` probe + MapJobs.Run THROW if the job ran managed or no runner is
    registered (test `ManagedRun_Throws`). Nothing could not be Bursted.
  - `MapArena`: one block, scratch freed per stage; seed 42 v4 peak 69 MB. On macOS the block is mmap'd: a freed
    malloc block stayed resident (C test: 100 MB touched, freed, still 101 MB; player Malloc Large 23 -> 104 MB).
  - Diagnostics strings (LastPlace/LastPick/LastMark/RouteBuild) are rebuilt from numbers; RouteBuild's
    "search N ms, all N ms" is now "map build N ms (Burst)".
  - `WorldRecordTests.V4SixBiomes_B10Gates`: its B8 reference formula now runs in a test Burst job (Mono differed
    at 1 boundary cell of every-64th sample) -- the only test edit besides the new one.
  - Player tg2 report prints `map v4 Height FNV <hash>` (PlaygroundCapture.FarBand.cs). KNOWN_ISSUES entry marked FIXED.

## Evidence
- Height FNV seed 42 v4 BEFORE (current code, task start): Editor/Mono `7A07FA71CAF75226` cell(613,259) 1070.521
  (`Logs/overnight/45/editor_before.out`) vs player/IL2CPP `E95115D99DDADE63` 1068.809 (`Logs/tg2world_20261010_000003.txt`).
  (32c's `B387F054...` was the Oct-8 code, before B4-B10.)
- AFTER: Editor `639552DD962C4B28` 1068.809 == player `639552DD962C4B28` 1068.809 (`Logs/tg2world_20261010_005419.txt`,
  and again with the mmap arena `Logs/tg2world_20261010_011501.txt`). Pinned in `WorldMapBurstTests.PLAYER_V4_SEED42`.
- Suites: fast 631/0/14 after the merge (`Logs/overnight/45/fast_step1.out`); SUITE=all 665/0/18 at the end
  (`Logs/overnight/45/all2.out`; was 662/0/18, +3 new tests). Re-pinned hashes: 0 (the suite pins none); new pins: 1.
- Lava step 1: player build 0 `Shader error`, 0 `error CS` (`Logs/lavalight_build.log`); pose 4 lava patch mean RGB
  (225,103,32) under 26 torches vs (224,103,32) without (pose 1) -- unchanged; Sep-24 pre-look shot was (249,196,83).
- PlayMetricsReport seed 42 v4 (`Logs/overnight/45/before|after/playmetrics_v4.txt`): every metric within 1% except
  biome patch COUNT 93 -> 95 (+2%; area-weighted median unchanged 327 ha). Same 5 features, same sites, same 4 easy
  areas (44 ha each), all gates PASS. Record hash (unpinned) 30D8356A7FBC4355 -> D780DC3C18291CAE.
- Not a measurement (single cold poses runs): footprint mean over 66 shared samples 3263 -> 3260 MB; peaks 3403 / 3483 MB.

## Notes for the supervisor
- CLAUDE.md says `MAPV=3` gives the "byte-identical pre-rebuild map": v3 now also comes from Burst, so it differs from
  the pre-task v3 at float level (same shapes). Worth updating that line.
- Still Mono-vs-IL2CPP (not the map): managed `ShapeField.Sample` calls outside jobs (StreamManager spawn sample,
  FarBand capture probes). Named in KNOWN_ISSUES; not queued.
- Left untouched (pre-existing): `Assets/Scenes/Playground.unity` modification, `queue/44` deletion in the worktree.

## Next step
Engine plan step 2 (torch shadow rays, BUGS #4) on `engine-phase`.

## For the doc
- Editor vs player seed 42 v4 Height FNV: BEFORE `7A07FA71CAF75226` vs `E95115D99DDADE63` (differ; cell(613,259)
  1070.52 vs 1068.81 voxels) -> AFTER `639552DD962C4B28` == `639552DD962C4B28` (`Logs/tg2world_20261010_005419.txt`).
- Tests: SUITE=all 662/0/18 -> 665/0/18 (`Logs/overnight/45/all2.out`); 0 hashes re-pinned, 1 new pin.
- Editor map build 27.5 s -> 1.5 s (PlayMetricsReport, `Logs/overnight/45/after/playmetrics_v4.txt`).
- Metrics seed 42 v4 before -> after: median slope 14.2 -> 14.2 deg (0%); mountainous 52.5 -> 52.6% (+0.1 pt);
  open 9.3 -> 9.3%; lakes 90 -> 90; river 26.9 -> 26.8 km (-0.4%).
- `Screenshots/Engine01/seed42_v4_relief_before_after.png` -- left: the Editor's old Mono map; middle: the Burst map
  (= the player's); right: the difference x8. Look at the right panel: black except faint specks on the steep central
  mountains (3.7% of pixels differ, 0.1% by >16 levels). No visible change.
- `Screenshots/Engine01/lava_torch_pair.png` -- left: pose 1, lava, no torches; right: pose 4, 26 torches + lava.
  Look at the lava floor: the same deep orange in both (mean RGB 224/225,103,32) while the torches light the walls.
