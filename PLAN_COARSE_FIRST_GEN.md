# Plan: coarse-first chunk generation (task 23, 2026-10-04) -- PLANNING ONLY

Order and scope: ROADMAP.md (E2/E3) overrides this plan where they differ.

## Owner decisions 2026-10-04 (Queue decisions Q18, Q19; recorded by task 30)
- **Q18 (a):** column-pass sharing first (exact, no rule change), then coarse-first step 1.
  Step 0 DONE in task 30 (`TerrainGenChunkSource.ColumnPass`, StreamManager column
  batches): byte-identical, EditMode isolated check 1.76x per column, 4.67 -> 1.00
  column-job runs per column (`Logs/TerrainGen/column_pass_sharing.txt`).
- **Q19 (a) to every part:**
  - 2a: allow coarse-first behind `-coarsefirst`, OFF by default, judged from pictures.
  - 3a: water beyond ~64 m stops ticking under the flag. **SUPERSEDED by ROADMAP.md E7**
    (owner 2026-10-08: active fluid keeps simulating wherever it is). Do not implement 3a.
  - 4a: exact point-sample (the coarse voxel is the tier-0 voxel at its corner, no vote).
  - 5a: downgrade later, after task 26 (the save-cap retry; done 2026-10-04).

Status: **proposal for the owner. No code, no builds, no defaults changed.**
Inputs: KNOWN_ISSUES "STREAMING REQUIREMENT" (16b), task 20's contention result,
the per-chunk TSVs of task 20's 8 walk launches, `Logs/TerrainGen/{cost,detail_cull,
v2_chunk_cost}.txt`, and the code cited inline. Task 22 (FarStartT DDA) and task 24
(detail radius at speed) have not run: both are still owner-gated. This plan does
not assume their results.

**Bottom line.**
- Coarse-first is worth about **1.5-2x** on the 16b worst case, not "an order of magnitude".
- Coarse chunks are 3-5x cheaper. But the region that still needs full detail (a
  ~64-77 m sphere inside an 89.6 m tall band) takes 30-40% of the worst-case chunk flux.
- An exact, contract-free alternative found while planning (**column-pass sharing**,
  section 6) is worth about the same (~1.66x) at much lower risk. Do it first.
- The two stack to ~3x. No generator lever closes the 19x gap (ideal workers) on its own.
  The worst case itself (16b item 4) still has to move.
- Coarse-first's other benefits may matter more than throughput:
  - coarse terrain instead of HOLES where a chunk arrives late inside 64 m (21% of chunks in a walk today);
  - a bounded full-detail set, which allows a smaller tier-0 brick pool (memory, 3 GB cap);
  - fewer upload bytes.

## 0. A correction to the premise

The task says the 64-128 m ring is drawn at tier 2. **It is drawn at tier 1.**
- Bounds: `LODConfig.TIER_OUTER_RANGE_M = {64, 128, 290}` (`Assets/ContentModules/Config/LODConfig.cs:64`, amendment 8.9).
- Voxel sizes: 0.1 / 0.2 / 0.4 m (:32).
- `SelectLODTier` (`Raymarch.compute:1201`) picks the tier from the Euclidean distance along the ray.
- So 64-128 m is tier 1: 0.2 m, 1/8 of the voxels. Tier 2 (1/64) covers only the load
  square's corners beyond 128 m.
- New chunks enter at the square's face, 128-181 m away (tier 2). But 84% of the worst-case
  flux crosses into the 128 m sphere almost at once (table 3.2, R = 128: 1,923 of 2,285 /s).
- So the coarse level must be **tier 1**. A tier-2-first level would save little and add a third state.

## 1. What changes

- **Coarse chunk.** One chunk's terrain point-sampled at 0.2 m, i.e. a 64^3 grid (= tier 1).
  Tier 2 is made from it by the existing majority vote (`LODDownsampler.MajorityVote`).
  It is written ONLY into the cascade tier pools (tiers 1 and 2).
  - It never enters `ChunkStore`.
  - It has no tier-0 bricks and no tier-0 clipmap entry.
  - It is a render-only product, like the far band (`FarField`): CPU-generated, deterministic, never saved.
- **Who starts coarse.** Every chunk entering the load square (r = 10, `EngineConfig.cs:268`), except:
  - chunks with a delta file (§4.1: absence of a delta = baseline). They always load full,
    because the coarse sampler cannot apply D.1 bricks;
  - sizeClass 0/1 worlds. The legacy generator stays as it is, gated on the world exactly like V2/V3.
- **Refine.** A chunk is generated in full by the unchanged `TerrainGenChunkSource.Generate` and
  admitted through the unchanged §4.4 path (Unloaded -> Loading -> Resident) when its nearest
  point comes within **R_f = 64 m + margin**.
  - Margin >= v x refine latency. One chunk (12.8 m) is the planning value, so R_f = 76.8 m.
  - On admit, the full chunk's majority-vote tiers OVERWRITE the coarse tiers, so the final
    state is identical to today's (deterministic, validator-checkable).
  - Chunks never within R_f stay coarse until evicted. That is the "only when needed" variant:
    the corners and most of the outer ring never refine.
- **Downgrade** (optional, step 5): a full chunk that leaves R_f + hysteresis frees its tier-0
  bricks and keeps its cascade entries.
  - Its cascade entries are the exact majority vote, better than coarse, so it needs no regeneration.
  - Dirty chunks take the §4.4 Saving path first.
- **Ordering** (replaces the single nearest-first-plus-heading queue, `StreamManager` DispatchLoads; §4.3 "prefetch by angular proximity"):
  1. **Late refines** (nearest point < 64 m, not full): nearest-first. Tier 0 is already showing a coarse chunk or a hole there.
  2. **Coarse**: nearest-first plus heading, as today.
  3. **Margin refines** (64 m to R_f): nearest-first.
  - While queue 1 or 3 is non-empty, at most W-1 workers take coarse jobs, so neither stage starves.
  - Step 3a measures whether this split is right. It is not derived.

## 2. Contracts touched (ARCHITECTURE_v8.6 unless noted)

| contract | effect | status |
|---|---|---|
| §0.1 inv. 1, CPU sole writer (`ChunkStore.SetVoxel`) | Untouched. Coarse data never enters the store and is never written by the GPU. The cascade is still written from CPU-generated data, as the far band already is. | OK |
| `IWorldQuery.GetVoxel` (**frozen**, §12 / line 141) | Unchanged code. A coarse chunk is null in the store, so it returns 0 = air, the documented, deliberate "unloaded" answer (`ChunkStore.cs:352-359`). `IVoxelResidency.IsResident` returns false ("unknown, not empty"). **Consequence:** CPU queries (projectiles §8.4, ray walkers, future NPCs) see voxels only within R_f (~77 m) instead of 128 m. | API OK; **behaviour REDESIGN, owner** (see §6.4 row) |
| `IEditService.SetVoxel` (**frozen**) | Unchanged. On a non-resident chunk it already returns ("Cannot edit unloaded chunks", `ChunkStore.cs:387`). Edits and §8.5 destruction happen within reach, inside R_f. | OK |
| `GenerateChunk` / `TerrainGenChunkSource.Generate` (**frozen**, line 141) | Unchanged. The coarse sampler is a NEW function beside it. It must NOT write into `_columnTop` (a coarse "top" underestimates the true max height and would wrongly air-fast-path full chunks: a correctness bug in exact data). | OK if kept separate |
| §4.1/§4.2 + D.1 deltas (**frozen**) | The baseline is still the deterministic full chunk: saves compare against `Generate` (§4.2). A coarse chunk is never dirty and never saved. Edited chunks skip coarse. | OK |
| §4.4 state table (**frozen**) | Unchanged. A coarse chunk is `Unloaded` in §4.4 terms. Coarse residency is a separate render-only set (Absent -> CoarsePending -> Coarse -> dropped on refine-admit or eviction) owned by StreamManager on the main thread (single-writer). Downgrade uses existing transitions (Resident -> Saving/Unloaded). | OK; new set is internal |
| §4.5 eviction | Coarse eviction frees cascade slots only. Downgrade is a new eviction trigger (radius R_f + hysteresis). It inherits the **save-cap bug from task 21** (save-capped evictions never retried), so task 26 must land first. | new trigger, step 5 |
| §6.4 + 8.9: "LOD0 0-128 m (full raymarch + **physics boundary**)"; "LOD1-2 populated by **majority-vote downsampling** at chunk load" | Both change. Physics, edits and exact CPU data shrink to R_f. Tiers 1/2 hold point-sampled data until refine. `CascadeTierPool.WriteChunkFromDownsampled` keeps its signature, but what its bytes mean changes. | **REDESIGN, owner** |
| GPU mirror tier 0 (`TerrainClipmap.UploadDirty`, the "uploader contract", frozen) | Unchanged. Written only from the store. A coarse chunk's slot stays empty. Rendering never reads tier 0 beyond 64 m: primary rays use `SelectLODTier`, and shadow rays use per-sample camera distance (`SelectShadowTier`, :1210). | OK |
| Cascade tiers 1/2 | Written from coarse data, then overwritten at refine. `CascadeValidator` checks only store-resident chunks (header: "for each resident chunk"), so coarse chunks need their own validator (recompute coarse, diff the GPU tiers). Pool caps (`EngineConfig.CascadeTierPoolCap`) come from majority-vote peaks. Point sampling may make more dense coarse bricks: measure. | step 3a |
| 8.7 air mips / 8.8 occupancy (no docs; read `AirMip.FromStore.cs`, `OccupancyMask.cs`) | Built from the tier-0 store in `UploadDirty`. A coarse chunk reads as air there. That is harmless: the tier-1/2 march reads no air-mip pyramid (`Raymarch.compute` ~2480: "No air-mip pyramid exists for these tiers yet"; empty coarse bricks are skipped one at a time). **If tier-1/2 mips are ever added, they must be built from the cascade pools, not the store.** | OK, note |
| Far-band hand-over (`FarStartT`, `Raymarch.compute:1977-2003`; `FarField.UpdateResidency`, `FarField.cs:287` reads `store.IsResident`) | Must become "full OR coarse resident", else the far band draws over coarse chunks. One line, but it couples to task 22/24's rewrite of the same loop. | step 3a |
| §7.4 fluids (`FLUID_ACTIVE_RADIUS_VOXELS` = 1280 = 128 m, `EngineConfig.cs:449`; sleep x1.15) | The CA reads and writes tier-0 bytes. Active fluid next to a coarse chunk reads its empty tier-0 slot as air. This is the PHASE_5C §9.4 mass-loss precedent. **The wake radius must be <= R_f / 1.15 (~64 m at R_f 76.8)**: distant water stops ticking at 64 m instead of 128 m. Number change (inv. 8), visible behaviour. | **owner** |
| Lighting (§3.8, §6.5) | Point lights are registered on placement (`LightSourceBuffer`), not scanned from generation, and live in full chunks. Sun shadows beyond 64 m already read tiers 1/2. Phase 7 §10's open "lava light" flag: if lava ever emits light scanned from chunk data, coarse chunks would need it too. | OK, note |
| Player collision (§8.1/8.2) | `PlayerMotor` asks `IsResident`, so a coarse chunk means "hold", not "fall". In steady state everything within R_f is full. At teleport or cold start, late refines go first (queue 1). | OK |

## 3. Gain estimate

### 3.1 Per-chunk stage costs: measured, except where marked
Units: G = one non-uniform chunk's `Generate`; F = generate + worker downsample = today's full chunk.
- **Column pass ~ 0.60 G.**
  - Measured: air chunks that ran `ColumnJob` and returned at the air fast path, paired with
    the median non-uniform chunk of the SAME (cx,cz) column. Ratio p50 0.60-0.63, p25-p75
    0.52-0.75, at 1 worker (launches 005336, 010102; n = 51, 42) and 7 workers (003351; n = 500).
  - Source: `streamprobe_chunks.tsv` in `StreamProbe/2026-10-04_*_streamwalk_*`.
  - Independent check, Editor single thread: column pass 24 ms of 41-46 ms
    (design doc V2 item 2; `Logs/TerrainGen/v2_chunk_cost.txt`).
- **Voxel pass + brick copy ~ 0.40 G** (the remainder).
- **Downsample ~ 0.21 G.** Measured: non-uniform downMs p50 / genMs p50 is 14.3/69 and 17.9/83 at 1 worker, 36-41/173-196 at 7. So F ~ 1.21 G.
- **Inside the column pass** (partly a GUESS):
  - `ShapeField.Sample` costs 0.337 us/column (measured, `cost.txt`), so its 130^2 samples are ~5.7 ms.
  - The remaining ~18 ms is attributed to `DetailField.PrepareColumn` (16,384 columns, 6+ Fbm calls each, ~1.1 us). **Inferred by subtraction, not timed.**
- Isolated full chunk on the player's main thread: 56.0 ms p50 (`Logs/tg2world_20261004_003351.txt`).
  Task 20 paired: 70 / 122 / 167 / 164 ms at 1 / 2 / 4 / 7 workers.

**Coarse chunk cost** (64^3 voxels, 64^2 columns; ESTIMATE from the split above):
- **Exact point-sample**: every coarse voxel equals the full chunk's voxel at the sampled position.
  `MaxStep` then needs 4 extra `Sample`s per column because neighbours at +-1 voxel are not on
  the coarse grid, so the column pass costs ~0.48 of today's.
  - Total (0.6x0.48 + 0.4/8 + 0.21/9) G = **0.30 F (3.4x cheaper)**.
  - Testable bit-exactly against a subsample of `Generate`.
- **Approximate `MaxStep`** from the coarse grid: column pass ~0.25 of today's, **0.18 F (5.4x cheaper)**.
  - No exactness test is possible.
  - D's stability step is computed differently, so coarse slopes may differ from full ones.
- **Not modelled:** fixed per-job overhead (TempJob allocations, `.Run()`); a sky memo hit costs
  0.4 ms in the Editor, so assumed small. Also not modelled: whether contention (task 20's 2.4x)
  shrinks for coarse jobs. Their working set is 256 KB instead of the 2 MB `mats` array, so it might,
  but that is a **GUESS** until task 25 attributes the contention.

### 3.2 Flux: how many chunks per second need FULL detail (DERIVED, geometry only)
- Method: chunks whose nearest point is within R of a camera placed uniformly in its chunk
  (200 samples); band 7 layers, square 21x21.
- Nx = full-detail chunks entered per 12.8 m of X/Z travel; Ny = per 12.8 m of Y travel.
- Rate = v/E x sqrt(2 Nx^2 + Ny^2), the same worst heading as `StreamingRequirement`.

| full-detail radius R | Nx | Ny | chunks in set | A_full at 60 m/s dive | level diagonal |
|---|---|---|---|---|---|
| (today: whole square) | 147 | 441 | 3,087 | **2,285 /s** | 974 /s |
| 128 m (tier-1 sphere) | 145 | 355 | ~2,420 | 1,923 /s | 962 /s |
| 76.8 m (64 + 1 chunk margin) | 88 | 138 | ~895 | **870 /s** | 582 /s |
| 64 m (no margin) | 73 | 100 | ~630 | 673 /s | 485 /s |
| 44.8 m (detail radius cut at speed) | 50 | 53 | ~313 | 416 /s | 332 /s |

Why so large: the band is only 89.6 m tall, so a 77 m sphere covers most of its height and
60% of the square's X/Z cross-section (88 / 147). The saving is mostly in the dive (138 vs 441).

### 3.3 Result against 2,285 /s
- Demand per second = 2,285 x coarse cost + A_full x F.
- "Ideal" = 16b's 7 x isolated, 121 /s. "Measured" = task 20, 7 workers, 33.2 /s.

| option | demand (F/s) | gain | ideal capacity, short by | measured capacity, short by |
|---|---|---|---|---|
| today | 2,285 | 1.0x | 121 /s, 18.9x | 33 /s, 68.8x |
| column sharing only (sec. 6; x5 = measured walk repeat) | 1,379 | 1.66x | 201, 11.4x | 55, 41.5x |
| coarse exact, R_f 76.8 | 1,552 | 1.47x | 178, 12.8x | 49, 46.8x |
| coarse approx, R_f 76.8 | 1,292 | 1.77x | 214, 10.7x | 59, 38.9x |
| coarse exact + sharing, R_f 76.8 | 772 | 2.96x | 358, 6.4x | 98, 23.3x |
| coarse exact + sharing, R_f 44.8 (needs a detail radius at speed) | 498 | 4.59x | 555, 4.1x | 152, 15.0x |
| same, dive capped (level only, 974 /s), R_f 44.8 | 306 | -- | short 2.5x | -- |

**Every row is short.** Coarse-first narrows the gap. Closing it also needs one of 16b's
item-4 levers (detail radius at speed; capped vertical speed) and task 25's contention fix.

**Main thread gets WORSE, not better** (estimate):
- A coarse admit still writes the full tier-1/2 cascade entries: ~1.0 ms of today's 1.22 ms
  admit is cascade writes (`tg2world_20261004_003351.txt` section 2).
- Worst case: 38.1 coarse admits x 1.0 + 14.5 refines x 1.22 = **~56 ms/frame vs 46.6 today**.
  Skipping the cascade rewrite at refine makes it ~41 ms.
- So 16b's fix 3 (cheaper cascade writes, a derived admit cap) becomes a prerequisite, not an afterthought.

**Upload** (estimate): tier-0 bytes only for refines, 14.5 /frame x ~260 KB = 3.8 MB, plus
coarse cascade bytes (~1.3 MB), against today's ~9.9 MB estimate and the 3 MB cap.

## 4. Risks
1. **Pop at refine** (64-128 m, tier 1). Point-sampled data differs from the majority vote.
   - The vote is solid-biased (ties go solid, `LODDownsampler.cs` header); point sampling is not.
   - So thin ridges, overhangs and material edges can appear or shift when a chunk refines.
   - Not measured. Step 1 measures the % of differing tier-1 voxels per landform; step 3a takes the pictures.
   - If it is visible, the option of keeping the coarse tiers (skipping the overwrite) makes
     tier data depend on history. Then `CascadeValidator` must accept both.
2. **Holes become coarse terrain, but only with a shader step** (step 4). Inside 64 m the tier-0
   path sees an empty slot for a coarse-only chunk, exactly as for a missing chunk today. A
   per-chunk "tier 0 present" bit lets the shader fall back to tier 1 there.
3. **Memory vs the 3 GB cap** (already exceeded: KNOWN_ISSUES).
   - Without downgrade: no saving. Pools are pre-allocated (inv. 3) and wandering refines everything.
     Coarse data reuses cascade slots that exist today, but more dense coarse bricks may exhaust caps.
   - With downgrade: the full set is bounded (~900-1,230 chunks at R_f 77-90 m, of 3,087), so the
     tier-0 pool (614 MB, mirrored on the GPU, design doc V3 item 6) could shrink. A guess:
     several hundred MB. It must come from `BrickDataPool.PeakUsed`, not this estimate.
4. **The physics/query world shrinks to ~77 m** (projectile range, NPC sight, mining reach). This is a game-design question and no game document exists.
5. **Fluids beyond 64 m stop simulating** (wake radius), or near coasts every water chunk must refine, losing much of the gain.
6. **Two generator paths to keep consistent.** A TerrainGen change (every B step) must update the coarse sampler too. The exactness test (step 1) catches drift only in the exact variant.
7. **Ordering starvation.** Refines and coarse jobs compete for the same 7 workers. A bad split gives holes (refine starved) or far band at mid range (coarse starved).
8. **The latency line is NOT fixed by this.** Requests still happen at R_d = 128 m with margin 0 (16b). Coarse jobs are cheap enough to request further out: radius 13 is the most the 32-wide window allows (`MaxLoadRadiusChunks(32)`). That is 16b's lever 4(b), and it raises coarse A to 3,640 /s.

## 5. Steps (one change each; owner approves the plan first)
Measurement: `MODE=stream ./run-tg2world.sh` on map v4, AC power, pmset and owner-pause NOTE
checked, 2 launches per arm, section 7 plus the LATE line (section 4). Fast EditMode suite each
step; `SUITE=all` for any TerrainGen file.

- **0 (recommended first; separate from coarse-first): column-pass sharing.** Exact,
  bit-identical, no contract change.
  - DONE, task 30 (2026-10-08). Checked by a byte-compare test and one isolated timing check
    instead of the stream measurement (supervisor edit, owner 2026-10-08).
- **1. Coarse sampler + exactness/cost report** (pure function, no runtime effect).
  - Tests: coarse == every 2nd voxel of `Generate` (exact variant) on 32 chunks covering
    landforms and cave layers.
  - Report-only test: coarse vs majority-vote tier 1, % voxels and bricks that differ, and
    Editor Burst ms for coarse vs full.
  - Stop: the measured cost ratio replaces 3.4x. Owner checks the difference %.
- **2. Requirement model** in `StreamingRequirement.cs` plus tests: section 7 prints
  coarse-first demand (A x c + A_full(R_f)) using step 1's c. No runtime change.
  - Stop: the corrected table 3.3.
- **3a. Coarse residency, refine-everything, behind `-coarsefirst 1`** (OFF by default; off must
  be byte-identical, checked with `MODE=poses` BUILD=1 against the previous poses run).
  - Coarse jobs, cascade writes, the coarse validator, and FarResident = full OR coarse.
  - Every chunk still refines (today's full set), so physics and fluids are unchanged.
  - Measures: time-to-visible, LATE, deficit, main-thread admit, pop.
  - Pictures, each with a caption: the fly-56 horizon strip off/on; one walk still at
    64-128 m just before and after a refine.
  - Stop.
- **3b. Refine only within R_f** (plus the fluid wake clamp under the flag).
  - Measures: the throughput row of 3.3 for real.
  - Stop.
- **4. Shader fallback** to tier 1 for coarse-only chunks inside 64 m.
  - Measure: LATE holes per frame (pictures).
  - Stop.
- **5. Downgrade** (needs task 26, the save-cap retry).
  - Then re-derive the tier-0 brick pool cap from PeakUsed.
  - Measure: peak footprint vs 3 GB.
  - Stop.

## 6. Alternatives considered
- **Column-pass sharing (RECOMMENDED, do first).**
  - The column pass (~0.60 G) depends only on (x, z), but runs once per LAYER.
  - Measured from the TSVs: ColumnJob ran **5.0 / 4.8 times per chunk column** at 7 workers
    (6,388 runs over 1,275 columns; 5,330 over 1,105) and 3.6 at 1 worker. Up to 14 with band switches.
  - Generating a column's band layers in one job (or a small LRU of column results, ~2.5 MB
    each) removes ~4/5 of it.
  - That is ~1.66x on the measured workload, bit-identical, no frozen contract touched.
  - Limit: a dive brings new layers to EXISTING columns. Those share only through a cache, and
    caching all 441 columns would cost ~1.1 GB, so it must be a small LRU.
- **Cheaper full generator** (evaluate `PrepareColumn`'s slow fields at 1/8 spacing and interpolate).
  - Their wavelengths are 200-4,000 voxels (`DetailField.cs:111-117`).
  - Could cut a large part of the column pass on EVERY chunk.
  - But it changes the world (a B-step, `SUITE=all`, new goldens) and every baseline that
    deltas compare against.
  - Worth a measured task after step 0. Not instead of coarse-first, and not a substitute for item-4 levers.
- **Exact coarse** (a true majority vote, so no pop): needs every tier-0 voxel and every column. That costs the same as a full chunk. Rejected.
- **Tier-2-first** (0.4 m): 27x cheaper per chunk, but 84% of the flux needs tier 1 within a frame or two (section 0). Rejected.
- **GPU generation:** violates §5's "CPU/Burst, unit-testable" and puts terrain authority on the GPU (inv. 1). Rejected.
- **More workers / priorities:** task 20 found cost flat from 4 to 7 workers and no oversubscription. Task 25 owns this.
