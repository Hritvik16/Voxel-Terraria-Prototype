# 204-plan-fluid-pocket-streams: DONE (plan only, no code). Rec.: (A) streams built the river way, at rest, every fluid; (C) held water later; not (B).

No code/tests changed. Sources: Rivers/Hydrology headers, TASKS 183/196/203, `RIVERS_WATERFALLS_PLAN.md`, KNOWN_ISSUES "POOL FULL" (tile 0.5 MiB, pool 480).

## 1. Rivers today, and can a stream be one scaled down? YES
- Map (8 m cells): Hydrology catchment (`WorldMap.Flow`, kept). River = 800+ cells. `Rivers.Profile` gives a level per cell that never
  rises downstream and ends AT the lake/sea. `FlowDir` (16 bits/cell) = direction, fall, slope, size, uploaded as the renderer's flow map.
- Column: a carved channel on 179's meander curve. The surface steps 1 voxel every few m. Each step is held by a LIP (engine 136, solid
  to the CPU, drawn as water by `RiverLipAsWater`). riverLook adds ripples and foam. Seed 42: 3,354 river cells, 85.6 M water voxels, UNSETTLED 0
  = 0 fluid tiles, 0 tick cost until touched. Touched river water drains a finite amount: no source (R3 "held water" is unbuilt).
- Narrowest river = 2.4 m (HALF_W_MIN 12 vox). A stream is the same code at half width 3-6 vox (0.6-1.2 m), 0.15-0.35 m deep by 179's section rule.
  `Rivers` already takes the fluid as a parameter (Q5).
- **Truly flowing costs too much.** An awake 100 m stream needs ~40-80 tiles (20-40 MB, 8-17 % of the pool), held ALL the time: flowing
  water never settles. It is ~36 k voxels, much of it moving each tick, so thousands of ops/tick vs ~1,024 that keep 6.5 Hz at 20 FPS
  (97). The tick slows for every fluid, chunks dirty every tick (§4.3 upload p99 already red under fluid), and it freezes at the 128 m edge (8.15).

## 2. Sources and surplus (data-derived, the same rule for every biome)
- Path: UPSTREAM from the pond on the 8 m map: the higher 8-neighbour draining into this cell with the largest `Flow` (ties by index).
  Stop at a catchment floor, a 2 m/cell rise (spring at the cliff foot) or 6-16 cells (48-128 m, by pond size). No catchment = a 1-cell
  spring at the high rim. Cut at route corridors, Walk band, start zone, signature features. The inlet is on the high side; 203's
  lobe 1 already points to the LOW rim = the outlet side.
- Head per fluid: water = spring/seep; lava = small crusted vent (volcanic only); acid = hot spring with a sinter head; oil = tar seep, short;
  sludge = mud seep, short; honey = trickle from rock/tree, short; quicksand = NO stream. v1 = the channel head only, stamps later.
- Surplus (the pond stays at rest by construction in all): (i) OUTLET stream from the low rim down the drainage line to the next river,
  lake or sea, or a small terminal seep pool within the cap; (ii) SEEP: a damp lining patch on the low rim only; (iii) NONE.
  Suggest (i) where the catchment is over the floor, else (ii). The mouth stands AT the pond level L (the river MOUTH rule).

## 3. Options
- **(A) River way, at rest (REC.)** Sees: a 0.6-1.2 m winding channel into the pond: 10 cm steps, ripples, foam (cf. the rivers in
  `Screenshots/179/river_2_after.png`). Gameplay: like rivers today. Digging releases a finite amount that drains into the hole. The
  pond is not refilled and the stream cannot be diverted for good. Cost: 0 tiles, 0 tick. Memory: stream cells go into the existing
  map arrays as RIVER class + a 1-bit stream mask (128 KB) for the width rule, so every river keep-out (trees, ores, stamps, Cover),
  the flow map, far band and minimap work unchanged. Gen: ~131 ponds x <= 16 cells, est. < 20 ms/world (pocket plane = 68 ms). Columns
  near streams ~0.6 % of land, chunk gen < +1 %. Risk: the pond plane must ignore its own stream (32 m keep-out); route test. Non-water
  lips: lining-coloured (0 ids, visible lines) or 6 per-fluid lip ids drawn as their fluid (table in place of `RiverLipAsWater`, `GenToEngine`).
  A lava flow look = shader work.
- **(B) Flowing in the CA.** Sees: moving voxels, but a hopping thin film on a 10 cm stair; lava at R/6, ~1 voxel/s. Gameplay:
  divertable and the pond really fills, but only within 128 m; frozen past it. Cost: see section 1, plus new spring + sink rules in
  `FluidCA.compute` AND `FluidReferenceCPU`. Risk HIGH, performance-changing; needs its own measured task.
- **(C) A + held water (R3).** Sees: A. Gameplay: a touched held cell turns to water, and a held cell beside air feeds it within the fluid
  budget, so a trench DIVERTS a stream (and rivers) for good. Cost: 0 tiles until touched, then only along the new flow, with ops capped.
  Memory: material ids only. Risk medium: CA change in both implementations; the motor, underwater view, see-through and edit tools must
  treat held as fluid. Effort M-L, measured.

## 4. Frozen APIs / amendments touched
- A: none. Generator change (SUITE=all, `FluidAtRestTests`); 8.15/8.16 untouched; new GenMat ids only for per-fluid lips (KNOWN_ISSUES GenToEngine row).
- B: 8.15 (NEAR 128 m), 8.16 (fall on a stepped bed), 8.14 row 5 tick contract (6.5 Hz), §4.3 upload budget, CPU/GPU CA parity, pool size (55).
- C: new material ids. Conversion goes op list -> `ChunkStore.SetVoxel`, so D.1, §4.4 and the sole writer are unchanged.
  `IWorldQuery.GetVoxel`'s signature is unchanged, but "is fluid" callers must learn the held ids. New CA rule in both CA implementations.

## Not done / next
Nothing built; no follow-up queued (needs the owner's answer). 131 ponds on seed 42 (~0.1 in the 128 m disc on average). Deep pockets: no streams.

## For the doc
- Result: plan for streams feeding every pond. Rec. (A): built like the rivers, at rest. 0 fluid tiles, 0 tick, ~128 KB, < 20 ms/world.
- Numbers: river at rest 0 tiles, UNSETTLED 0 (`Logs/TerrainGen/fluid_at_rest_42.txt`). A flowing 100 m stream: ~40-80 tiles (20-40 MB)
  + thousands of ops/tick vs ~1,024 at 6.5 Hz.
- **Question (Q-AF, streams):** how should streams feed the ponds? (a) REC: like the rivers, a thin carved stream at rest, every fluid
  but quicksand. It looks fed; digging drains it a little; no refill. (b) Truly flowing in the fluid sim: divertable, but 20-40 MB and a
  slower fluid tick per stream, frozen past 128 m. (c) (a) now, then "held water" (R3) later: streams AND rivers become endless sources
  a trench can divert (an engine task). Sub-questions: surplus = outlet stream else a seep (rec.) / seep only / none? Non-water step
  lips = lining-coloured (free) or drawn as the fluid (6 new ids)?
- Images: `Screenshots/179/river_2_after.png`: rivers at mid-frame (blue channels, white foam steps), what a stream scales down from.
  `Screenshots/203/side_water_after.png`: the pond at centre has no inlet today; a stream would enter from the upslope side.
  `Screenshots/203/top_lava_after.png`: the lava pond at centre; a lava stream would come from a vent and needs its own lip/flow look.
