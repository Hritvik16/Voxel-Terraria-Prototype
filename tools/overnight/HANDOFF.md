# 204-plan-fluid-pocket-streams: DONE (plan only, no code). Recommendation: (A) streams built the river way, at rest, every fluid; (C) "held water" later for divertable flow; not (B).

No code or tests changed. Read: `Rivers.cs`/`Hydrology.cs` headers, `FluidPockets` (TASKS 183/196/203), `RIVERS_WATERFALLS_PLAN.md` (R3 unbuilt),
`FluidTickScheduler`, KNOWN_ISSUES "POOL FULL" (tile 32^3 = 0.5 MiB, play pool 480 tiles), `Logs/TerrainGen/fluid_at_rest_42.txt`, `[FluidPockets]` line in `Logs/overnight/203/player_after.log`.

## 1. Rivers today, and can a stream be one scaled down? YES
- Map (8 m cells, 1024^2): Hydrology floods, then accumulates catchment (`WorldMap.Flow`, kept). A cell becomes a river at 800 cells (0.05 km^2).
  `Rivers.Profile` sets a level per cell that never rises downstream and ends AT the lake/sea. `FlowDir` (16 bits/cell) carries direction,
  fall, slope class and size; it is uploaded as the renderer's flow map.
- Column: a carved channel along the 179 meander curve; the surface steps 1 voxel every few m. Each step is held by a 1-voxel LIP (GenMat
  RiverLip = engine 136, solid to the CPU, drawn as water by `RiverLipAsWater`). `-riverlook` adds ripples along the flow and foam on steep parts.
  Seed 42: 3,354 river cells, 85.6 M water voxels, UNSETTLED 0. AT REST = 0 fluid tiles, 0 tick cost until touched. Touched river water drains
  for a finite time: no source exists (R3 "held water + source rule" is planned, not built).
- The narrowest river today is 2.4 m wide (HALF_W_MIN 12 voxels). A stream can be the same code with a half width of 3-6 voxels (0.6-1.2 m)
  and 179's section rule: 0.15-0.35 m deep. `Rivers` already takes the fluid as a parameter (Q5).
- **Truly flowing in the sim costs too much.** One awake 100 m stream needs ~40-80 tiles (20-40 MB; 8-17 % of the 480-tile pool). It holds
  them all the time, because flowing water never settles. It is ~36 k voxels, and a large share moves every tick, so several thousand ops per
  tick. 6.5 Hz holds only up to ~1,024 ops/tick at 20 FPS (97), so the tick slows for EVERY fluid in reach. Every tick also dirties chunks:
  CPU SetVoxel + mirror uploads (§4.3 upload p99 is already red under fluid). At the 128 m edge (8.15) the stream freezes mid-flow.

## 2. Sources and surplus, per fluid (every rule data-derived, same for every biome)
- **Path:** from the pond, go UPSTREAM on the 8 m map: next cell = the higher 8-neighbour that drains into this one with the largest
  `Flow` (ties by cell index). Stop at a catchment floor, a 2 m/cell rise (spring at the cliff foot), or a length cap of 6-16 cells
  (48-128 m, scaled by pond size). A pond with no upstream catchment gets a 1-cell spring at its high rim. Avoid route corridors, the
  Walk band, the start zone and signature features (cut the stream there). The inlet comes from the high side; 203's lobe 1 already points
  at the LOW rim, so the outlet goes there.
- **Head per fluid:** water = spring/seep from rock; lava = small vent with magma crust (volcanic only, as today's lava rule); acid = hot
  spring with a sinter head (its lining already is sinter); oil = tar seep from a crack, short; sludge = mud seep, short; honey = trickle
  from rock/tree, short; quicksand = NO stream (not a flowing fluid; a water seep at most). v1 = the channel's head only; head stamps later.
- **Surplus (pond stays at rest by construction in every case):** (i) an OUTLET stream from the low rim down the drainage line to the
  next river/lake/sea, ending in a small terminal seep pool if none is within the cap; (ii) a SEEP: no visible outlet, only a damp lining
  patch on the low rim (cheapest); (iii) NONE: a terminal basin. Suggest: (i) when the pond's catchment is over the floor, else (ii).
  The mouth stands AT the pond's L (the river MOUTH rule); levels never rise downstream.

## 3. Options (what the player sees / gameplay / cost / risk)
- **(A) River way, at rest (REC.)** Sees: a 0.6-1.2 m winding channel into the pond, 10 cm steps, ripples + foam with riverLook
  (`Screenshots/179/river_2_after.png`; the stream is a thin version of the rivers at mid-frame). Gameplay: inside the 128 m reach it acts
  like rivers today. Digging into it releases a finite amount that drains into the hole and stops. It does NOT refill the pond and cannot
  be diverted for good. Cost: 0 tiles, 0 tick. Memory: write the stream cells into the existing map arrays as RIVER class + a 1-bit stream
  mask (128 KB) for the width rule, so every existing river keep-out (trees, ores, stamps, Cover) applies unchanged and the flow map,
  far band and minimap draw it for free. Gen: tracing ~131 ponds x <= 16 cells, est. < 20 ms per world (the pocket plane is 68 ms).
  Columns near streams ~0.6 % of land, chunk gen < +1 % average. Risk: the pond plane must ignore its own stream (32 m keep-out); route
  test (streams cross walk routes; cap at corridors). Lips: non-water lips need either lining-coloured lips (0 new ids, visible lines
  every few m) or 6 per-fluid lip ids drawn as their fluid (a table in place of `RiverLipAsWater`, `FarField.GenToEngine` rows). Lava
  ripples would need a lava flow look (shader). SUITE=all + `FluidAtRestTests`.
- **(B) Truly flowing in the CA.** Sees: real moving voxels, but on a 10 cm stair the water moves as hopping thin film, and lava moves at
  R/6, about 1 voxel/s. Gameplay: divertable, the pond really fills, but only within 128 m; past it, frozen mid-flow. Cost: section 1
  (20-40 MB of tiles per awake stream, thousands of ops/tick, constant uploads). It needs new spring + sink CA rules in BOTH
  `FluidCA.compute` and `FluidReferenceCPU`. Risk: HIGH; performance-changing (needs its own measured task), slows all fluid.
- **(C) A + "held water" (RIVERS plan R3).** Sees: the same as A. Gameplay: touched held cells turn to water and a held cell beside air
  feeds it within the fluid budget. A trench DIVERTS the stream for good, and rivers gain the same. Cost: 0 tiles until touched, then
  tiles only along the player's new flow, with ops capped by the source rule. Memory: material ids only. Risk: medium. It is an engine CA
  change in both implementations, and the motor, underwater view, see-through and edit tools must treat held as fluid. Effort M-L, measured on its own.

## 4. Frozen APIs / amendments touched
- A: none. A generator change (`Assets/TerrainGen`, SUITE=all). 8.15/8.16 untouched. If per-fluid lips: new GenMat ids ->
  `FarField.GenToEngine` (KNOWN_ISSUES row). No memory-layout change.
- B: 8.15 (NEAR 128 m: the stream crosses the reach edge), 8.16 (fall on a stepped bed), 8.14 row 5 fluid tick contract (6.5 Hz), §4.3
  upload budget, new CA rules (CPU/GPU parity), pool sizing (task 55).
- C: new material ids. The held->fluid conversion goes through the op list -> `ChunkStore.SetVoxel`, so D.1 and §4.4 are unchanged and
  the CPU stays the sole writer. `IWorldQuery.GetVoxel`'s signature is unchanged, but "is fluid" callers must learn the held ids. New
  CA rule in `FluidCA.compute` + `FluidReferenceCPU`.

## Not done / next
Nothing built. Seed 42 has 131 surface ponds, ~2 per km^2: ~0.1 inside the 128 m sim disc on average. Deep (sealed) pockets get no
streams under any option. No follow-up queued (it needs the owner's answer).

## For the doc
- Result: plan for streams feeding every pond. Recommended (A): built like the rivers, at rest. 0 fluid tiles, 0 tick, ~128 KB, < 20 ms per world.
- Numbers: river at rest = 0 tiles / UNSETTLED 0 (`Logs/TerrainGen/fluid_at_rest_42.txt`). One flowing 100 m stream ~ 40-80 tiles
  (20-40 MB of the 480-tile pool) and thousands of ops/tick against the ~1,024 that keep 6.5 Hz (TASKS 97). 131 ponds on seed 42.
- **Question (Q-AF, streams):** how should streams feed the ponds?
  (a) REC: like the rivers, a thin carved stream at rest, every fluid but quicksand. Looks fed; digging drains it a little; no refill.
  (b) Truly flowing in the fluid sim: real flow and divertable, but 20-40 MB and a slower fluid tick per stream, frozen past 128 m. Not recommended.
  (c) (a) now, then "held water" (R3) later: streams AND rivers become endless sources a trench can divert. An engine task.
  Sub-questions: surplus = outlet stream to the next river/lake, else a seep (rec.) / seep only / none? Lava/oil/... step lips =
  lining-coloured (free) or drawn as the fluid (6 new material ids)?
- Images: `Screenshots/179/river_2_after.png`: the rivers at mid-frame (blue channels, white foam steps) are what a stream would scale
  down from. `Screenshots/203/side_water_after.png`: the pond at centre has no inlet today; a stream would come in from the upslope side.
  `Screenshots/203/top_lava_after.png`: the lava pond at centre; a lava stream would come from a vent and needs its own lip/flow look.
