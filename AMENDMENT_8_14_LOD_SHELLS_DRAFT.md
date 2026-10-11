# Amendment 8.14 (DRAFT, NOT ADOPTED) -- LOD shells, an edit-op log, and a smaller 0.1 m window

2026-10-10 · task 60 (engine plan step 9, planning only). Nothing here is built or approved.
Amends: ARCHITECTURE_v8.6 §3.7, §4.2, §4.3, §6.4, Appendix A/D; 8.9 §2-3; 8.10 (cascade tiers 1-2).
Supersedes: 8.11 (a draft, never adopted; its "Option B, a true cascade" is what this is).
Inputs: `ENGINE_PHASE_PLAN.md` §3 step 9 and §4 rows 1-4, owner answers Q1 (a), Q2 (a), Q38 (a).
A number marked **(est.)** is my estimate. Every other number names its source. Nothing was measured for this draft.

## For the owner

- **What changes.** Today's 3 voxel tiers (0.1 / 0.2 / 0.4 m) draw voxels only out to ~128 m; the flat 2.5D far band draws the rest. They become **8 nested cube shells**, 0.1 m to 12.8 m voxels. Each shell is twice as coarse and reaches twice as far, out to 6.6 km, up and down as well as sideways.
- **What it buys.** Real 3D terrain to the horizon. The 0.1 m area shrinks from 128 m to ~80 m, which frees **~470 MB** of the 3 GB (est.). A late chunk shows a coarser shell instead of a hole.
- **What it does not buy: frame rate.** The main march (~19 ms facing a wall, ~8 ms on an open view, task 57) is not touched. Shells should cost about the same as today's tiers plus far band (−1.6 to +2.7 ms, est.).
- **Costs.** Steps 10-14 as already planned (five steps, two of them L). Full detail keeps up only to **~7-11 m/s** (est.). Faster than that, the next coarser shell shows for a moment (your Q2 (a)).
- **Frozen, and still frozen:** `GetVoxel`, `SetVoxel`, the D.1 delta bytes and the §4.4 state table. **New beside them:** an edit-op log in its own save file, for edits far away.
- Six questions at the end. "All (a)" approves the draft as written.

## 1. The rule that sizes the shells

- A voxel of V metres covers 1 px at distance **c·V**. c = rows / vFOV (radians), from §6.4 C.5 at the design vFOV of 60° (`LODConfig.cs:62`).
  - 540 rows: c = 515.7 (0.1 m at 52 m). 720 rows: c = 687.5 (0.1 m at 69 m). This is the same formula as plan E2.
  - `LODConfig.PixelAngleRad` uses the screen-centre pixel instead, which gives 468·V at 540. Screen edges see voxels up to ~1.4x larger in px (est.). The tables use c.
- **Two possible hand-over rules (Q2):**
  - **Rule A ("finer reaches 1 px").** Shell k ends at c·V_k, where its own voxel shrinks to 1 px. The next shell's voxel therefore arrives at 2 px.
  - **Rule B ("never coarser than 1 px").** Shell k ends at 2c·V_k, where the NEXT voxel is 1 px. That is the plan's wording. It needs 4x the area per shell.
- **Evidence for rule A.** On 2026-09-25, moving the tier bounds from today's ~1.6 px hand-over to "coarser ≤ 1 px" (93.5 / 187.1 m) was built and measured. It was no better to look at (tier-1 pixels differed from an all-0.1 m frame by 11.7/255 vs 11.4/255), and it cost **+6 ms** on the in-air orbit (54.4 vs 48.4 ms). The visible difference was late coarse DATA, not voxel size (`LODConfig.cs:50-58`).
- **Powers of two.** Every ring is a power of two in chunks, horizontally and vertically, and is indexed with `&` (CLAUDE.md invariant; §6.2 phantom terrain). Each shell's "chunk" is 128 of its own voxels (12.8·2^k m), the same 16³ bricks of 8³ as today (`EngineConfig.cs:61-62`).
- **Hollow shells.** A shell stores no chunk that the finer shell fully covers. Holes and edges snap to whole chunks.
- **Vertically too.** Each shell is a cube. The world is 1.1 km tall (−500 to +600 m, design doc §2 line 92), so shells 4-7 hold the whole height in 2-8 layers.

## 2. Shell tables (rule A)

Method (geometry only, nothing run): shell k has V = 0.1·2^k m, chunk E = 12.8·2^k m, hand-over r = c·V (capped at 6.6 km); it stores chunk offsets ±⌈r/E⌉ minus the finer shell's fully covered ±(⌊r_prev/E⌋−1), at most the 8 km world; Y layers ≤ ⌈1,100 m/E⌉+1; flux = √2·v·(side + hole)/E. Columns: ring = the pow2 ring in chunks, XZ x Y. Cols = chunk columns stored (hollow). Bricks = dense bricks (est.): columns × 256 brick columns × a density per brick column. The densities are **measured** for 0.1 / 0.2 / 0.4 m (3.36 / 2.93 / 2.33, from the peaks 379,527 / 82,641 / 16,437 over 21×21 resident columns: `Logs/overnight/55_pool_ledger.out`, task 53 HANDOFF) and **2.0 (est.)** for 0.8 m and coarser.
Shell 0 is today's 0.1 m CPU window and its GPU mirror. Its radius adds a one-chunk prefetch margin (12.8 m). MB = bodies (512 B on GPU; shell 0 also on CPU) + 64 B occupancy per brick (est.) + directory. Flux = chunk columns entering per second in level diagonal flight at 60 m/s (the worst heading).

### 2a. Sized for 720 rows (recommended, Q1 (a))

| shell | voxel | covers (m) | ring | cols | bricks | MB | cols/s at 60 m/s |
|---|---|---|---|---|---|---|---|
| 0 | 0.1 m | 0-69 (+13 margin) | 16×16×16 | 225 | 194k | 317 | 99 |
| 1 | 0.2 m | 69-138 | 16×16×16 | 160 | 120k | 70 | 53 |
| 2 | 0.4 m | 138-275 | 16×16×16 | 160 | 95k | 56 | 27 |
| 3 | 0.8 m | 275-550 | 16×16×16 | 160 | 82k | 49 | 13 |
| 4 | 1.6 m | 550-1,100 | 16×16×8 | 160 | 82k | 49 | 7 |
| 5 | 3.2 m | 1,100-2,200 | 16×16×4 | 160 | 82k | 49 | 3 |
| 6 | 6.4 m | 2,200-4,400 | 16×16×4 | 112 | 57k | 34 | 1.5 |
| 7 | 12.8 m | 4,400-6,600 | 16×16×2 | 27 | 14k | 8 | 0.5 |
| **all** | | to 6.6 km | | **1,164** | **726k** | **632** | **204** |

### 2b. Sized for 540 rows

| shell | voxel | covers (m) | ring | cols | bricks | MB | cols/s at 60 m/s |
|---|---|---|---|---|---|---|---|
| 0 | 0.1 m | 0-52 (+13 margin) | 16×16×16 | 169 | 145k | 270 | 86 |
| 1 | 0.2 m | 52-103 | 16×16×16 | 112 | 84k | 49 | 46 |
| 2 | 0.4 m | 103-206 | 16×16×16 | 112 | 67k | 39 | 23 |
| 3-6 | 0.8-6.4 m | 206-3,300 | 16×16×(16..4) | 112 each | 57k each | 34 each | 12 / 6 / 3 / 1.5 |
| 7 | 12.8 m | 3,300-6,600 | 16×16×2 | 27 | 14k | 8 | 0.5 |
| **all** | | to 6.6 km | | **868** | **540k** | **503** | **178** |

### 2c. What each sizing costs, side by side

Pool caps are set at peak × 1.25, as task 53 set the cascade caps.

| | A at 720 (rec.) | A at 540 | B at 540 | B at 720 |
|---|---|---|---|---|
| shells | 8 | 8 | 7 | 7 |
| voxel size where a shell takes over, at 540 rows / at 720 | 1.5 / 2.0 px | 2.0 / 2.7 px | 1.0 / 1.3 px | 0.75 / 1.0 px |
| 0.1 m CPU window radius | 82 m | 65 m | 116 m | 151 m |
| shell 0 (CPU + GPU bodies + directory) | 364 MB | 305 MB | 484 MB (*) | 685 MB (*) |
| shells 1-7 (GPU only) | 388 MB | 287 MB | 681 MB | 922 MB |
| chunk columns to fill after a teleport | 1,164 | 868 | 2,073 | 2,817 |
| generation needed, per m/s of speed | 3.4 cols/s | 3.0 | 5.1 | 6.2 |
| **full detail keeps up to** (at 23-37 cols/s) | **7-11 m/s** | 8-13 m/s | 4.5-7 m/s | 3.7-6 m/s |
| upload at 60 m/s, MB per frame at 30 fps | 2.8 | 2.5 | 4.2 | 5.1 |
| march cost vs rule A (est.) | -- | ~same | +3-6 ms | +3-6 ms |

(*) Rule B's shell 0 needs a 32-chunk ring in all three axes. With today's flat directory that ring alone is 1 GB (32,768 slots × 16 KB × GPU + CPU copy), so rule B also needs the two-level directory for shell 0. That changes §3.7's layout.

- **The plan's "1.78x for 720" is closer to 1.3x** (est.): rings snap to whole chunks and the horizon cuts the last shells. 720 sizing costs about +150 MB and +15% generation over 540.
- **Capacity source:** warm 23-37 chunks/s at 4 workers (task 28, plan E3). One column costs ~1 full chunk (est.): 0.6 G for the shared column pass + 1.5 non-uniform chunks × 0.4 G (`PLAN_COARSE_FIRST_GEN.md` §3.1; G = one chunk's Generate). Coarse shell chunks still have 128³ voxels, so they cost about the same as a 0.1 m chunk (est.). Step 15's whole-brick skip helps every shell.
- **At walking speed (5 m/s)** rule A at 720 needs 17 cols/s: met, with 1.4-2.2x headroom (est.).
- **Fill after a teleport** (A at 720): 1,164 columns. At PrimeWindow's rate (847 chunks in 5,376 ms, `55_pool_ledger.out`) that is ~7 s (est.). The coarsest shells fill first, so the screen is never empty.

## 3. Memory ledger, before and after (rule A at 720)

"Today" = task 55's ledger at the end of the coast pool probe (`Logs/overnight/55_pool_ledger.out`). Terrain owners only; everything else is unchanged.

| owner | today GPU / native / managed (MB) | after (MB, est.) | why |
|---|---|---|---|
| TerrainClipmap (0.1 m GPU mirror) | 513.1 / 0 / 147.4 | 246 | Directory 32×32×8 → 16×16×16 chunks (256 → 128 MB, GPU + managed copy; `TerrainClipmap.cs:226-241`); bodies follow the pool. |
| BrickDataPool (0.1 m CPU bricks) | 0 / 366.2 / 0 | 118 | Cap 750,000 → ~242,000 (194k peak × 1.25, est.), set from `PeakUsed` after step 14. |
| LODCascadeManager (tiers 1-2) | 79.0 / 67.6 / 46.1 | 0 | Replaced by shells 1-2. |
| **Shells 1-7 (NEW)** | -- | 388 | GPU only; the CPU keeps no copy. |
| FarField (far band) | 27.2 / 13.0 / 0 | 40 | Kept beyond the last shell until step 23 (Q6). |
| ChunkStore + StreamManager | 0 / 13.0 / 49.5 | ~62 | Unchanged. |
| **Terrain total** | **1,322** | **~855** | **−467 MB (est.)** |

- Whole process today: phys_footprint peak 3,077 MB over poses (task 53, before task 55's −272 MB fluid cut), 2,869 MB walk (task 53), 2,472 MB coast probe (task 55).
- After shells: poses ~2,340 MB (est.) = 3,077 − 272 − 467. That is under the plan's 2,500 MB engine target and leaves room for the light grid (~100 MB, step 20).
- Rule A at 540 saves ~630 MB (est.). Rule B at 540 saves ~50 MB. Rule B at 720 ADDS ~390 MB and would put poses back over 3 GB.

### Upload budget

- The cap stays 3 MiB per frame (`EngineConfig.cs:150`, §0.2). Shells share it with the 0.1 m mirror.
- Need at 60 m/s (A at 720): 85 MB/s (est.). That is 2.8 MB/frame at 30 fps and 1.4 MB at 60 fps, but only if generation could keep up, which it can't (above). At 5 m/s: 0.23 MB/frame.
- **Order:** the coarsest missing shell chunk first, then finer ones, nearest the velocity first (§4.3 prefetch ordering). A coarse chunk is cheap and removes a hole, while a missing fine chunk only looks blockier (Q2 (a)).

## 4. Frame time, step by step (all est.)

Baseline: task 57's play frame, FSR 3 at 540, warm: **49.4 ms** facing a wall, **30.4 ms** on an open view (`git show 572f5f8:tools/overnight/HANDOFF.md`). There, tiers 1-2 cost 0.3 / 2.9 ms and the far band 1.8 / 3.8 ms.

| step | change | near | open |
|---|---|---|---|
| 1 | 0.1 m ends at 69 m instead of 64 m | 0 | 0 |
| 2 | Shells 1-2 (69-275 m) replace tiers 1-2. Today those tiers have data only to ~128-141 m, so shells add 3D march where the far band was. | 0 | +0.5 to +1 |
| 3 | Shells 3-7 (275 m-6.6 km) replace the 2.5D far band. Five 3D DDA shells with air mips: ~1-2 / 3-5 ms, against the far band's 1.8 / 3.8. | −0.8 to +0.2 | −0.8 to +1.2 |
| 4 | Sun shadow rays from far pixels march coarse shells instead of tiers + far shadows | 0 to −1 | 0 to −1 |
| 5 | Up to 8 shell hand-overs per sky ray (DDA restart) | +0.2 | +0.5 |
| | **net** | **−1.6 to +0.4** | **−0.8 to +2.7** |

- The ~19 / ~8 ms of main 0.1 m march that task 57 could not switch off stays as it is. Shells are a memory, view-distance and streaming change, not a frame-rate change.
- Rule B adds +3-6 ms (the 2026-09-25 measurement above). Each shell is twice as many voxels deep along a ray.

## 5. Contract changes

| # | Status | What | Amends |
|---|---|---|---|
| 1 | **CHANGED** | 3 tiers become 8 shells. `LODConfig`'s `TIER_*` arrays become `SHELL_COUNT`, `SHELL_VOXEL_SIZE_M`, and ranges computed from the design rows (Q1) and the rule (Q2). Still parameterised, as 8.9 §5.1 asks. | §6.4; 8.9 §2-3; `LODConfig.cs:27-92` |
| 2 | **CHANGED** | Cascade tiers 1-2 (`CascadeTierPool`, `LODCascadeManager`, chunk-major entries tied to tier-0 slots) are replaced by one shell pool per shell. Each shell has its own pow2 ring and a **two-level directory** (NEW, Appendix A): a 4-byte entry per ring chunk (absent / uniform material / page) and a 16 KB page of brick handles only for non-uniform chunks. | 8.10; Appendix A |
| 3 | **UNCHANGED** | **The CPU is the sole terrain writer** (plan §4 row 2). Workers generate shell chunks with Burst into scratch they own; the main thread uploads; the GPU only stores. Every shell directory is zeroed at construction (the `TerrainClipmap.cs:243` lesson). | §3.2, §3.7 |
| 4 | NEW (later, only if needed) | **GPU-built render shells** (from seed + op log) are the step-16 fallback, and only if step 16 shows a gap that Q2 (a) cannot hide (owner Q2 (a)). The CPU would keep the truth, but GPU and Burst floats would then have to agree bit for bit. Not part of 8.14. | -- |
| 5 | **CHANGED** | **Hand-over by distance, not residency.** A ray reads shell k between its bounds. If that chunk is absent, it reads shell k+1 (Q2 (a)). The far band draws only past the last shell or where shell 7 is absent. `FarStartT`'s residency DDA and `-detailspeed` retire (step 12). The prefetch margin is > 0 by construction: each ring stores past its hand-over distance (≥ 12.8 m for shell 0). | §6.2; KNOWN_ISSUES 16b / 22 / 24 |
| 6 | **CHANGED** | **Smaller 0.1 m window** (step 14). §4.3's "128 m LOD0 radius" becomes shell 0's radius plus margin: 82 m at 720. The load radius goes 10 → 7 chunks (`EngineConfig.cs:268`). The CPU ring stays 32 × 32 × 16 (pow2; `WINDOW_CHUNKS_Y` stays 16, CLAUDE.md landmine). The GPU mirror box becomes 16³ (task 32's out-of-box skip already handles chunks outside it). The 0.1 m pool cap comes from `PeakUsed` × 1.25 after the shrink. | §4.3 |
| 7 | **UNCHANGED (frozen)** | `IWorldQuery.GetVoxel` (`IWorldQuery.cs:6`). It still answers from resident chunks only. Coarse shells are render-only and are never queried. A game query beyond ~80 m (NPC sight, projectiles) needs a new API later. | Phase API freeze |
| 8 | **UNCHANGED (frozen)** + NEW beside it | `IEditService.SetVoxel` (`IEditService.cs:6`) keeps its contract: it is refused outside loaded chunks. A far edit uses a **new** op API (e.g. `IEditOps.Record(op)`), main thread only. | Phase API freeze |
| 9 | **NEW**, next to a frozen format | **Edit-op log** (plan §4 row 3; E5), new file D.4 `ops.log`: header `{uint seed, ushort formatVersion, uint opCount}`, ops `{uint seq, byte kind, int3 centreVoxel, ushort size, byte material}` (sphere carve/fill, box, D.3 prefab placement), CRC32 trailer, `.tmp` + atomic rename as D.1. Ops are applied in `seq` order inside generation, at each shell's own voxel size. | Appendix D (adds D.4) |
| 10 | **UNCHANGED (frozen bytes)**, CHANGED meaning | **D.1 stays byte for byte.** §4.2's "baseline" becomes "generation + the ops up to this chunk's saved seq". **Precedence (Q3 (a)):** ops with seq ≤ S, then D.1, then ops with seq > S. S is the op count when the chunk's delta was written. It lives in a side table inside `ops.log` (`{int3 chunk, uint seqAtSave, uint deltaCrc}`), never in D.1. If `deltaCrc` does not match the delta's own trailer (a crash between the two writes), fall back to "all ops, then D.1". The worst case is one chunk's far op lost under edited bricks, in the spirit of §4.2's "edits lost in one chunk". Old saves have no `ops.log`: zero ops, so they load unchanged. | §4.2 |
| 11 | **UNCHANGED (frozen)** | **§4.4 state table.** A fluid patch (plan §4 row 4) inside the CPU ring is just "eviction not selected". The same states and transitions apply, and "enter window" reads as "enter the residency set" (the detail radius plus awake-fluid chunks). A patch OUTSIDE the 32-chunk ring (~±204 m) has no ring slot: it would need a new store or a new state. That is a **redesign**, flagged for step 19's amendment and not done here (Q4). | §4.4 |
| 12 | **NEW** | `GenerateShellChunk(seed, map, shell k, coord, ops) → brick data`, a Burst job on the one compiled generator path (step 1). §5.2's `GenerateChunk` is unchanged. A shell brick in a chunk that has a D.1 delta or a live edit is instead made by downsampling the resident 0.1 m chunk (majority vote, as today's cascades). | §5.2 |
| 13 | **NEW** | **Coarse-first agreement threshold (for step 10, Q5 (a)).** Each coarse voxel is the 0.1 m generator's voxel nearest its centre (index 2^(k−1) on each axis). That is exact against `Generate` in a test, like Q19 4a, but centred instead of at the corner. **Pass:** ≥ 97% of voxels in non-uniform bricks agree solid/air with the majority-vote downsample, AND ≥ 99% of columns have their surface within 1 coarse voxel, for shells 1-3 on the seed-42 landform set. On smooth slopes a centre sample matches the vote almost always (est. > 99%). Misses come from detail finer than the voxel. Material agreement is reported, not gated. | step 10 |
| 14 | **UNCHANGED** | Upload cap 3 MiB/frame (§0.2); shells share it (§3 above). | §0.2 |
| 15 | **CHANGED** | **Shell pool exhaustion does not throw.** Today a cascade pool throws when full (task 53 HANDOFF). A full shell pool skips the chunk (the coarser shell shows) and counts a refusal, like the fluid pool (task 55). | 8.10 |

## 6. Risks, and what is measured when

| risk | what we do | measured at |
|---|---|---|
| Generation keeps full detail only to ~7-11 m/s (est.) | Q2 (a): the coarser shell shows. The GPU-shell fallback only if this looks bad. | step 16: warm `MODE=stream` walk and fly at 60 m/s. Cols/s per shell, seconds to full detail, % of frames showing a coarser shell inside 69 m. |
| A dive (vertical speed) loads whole horizontal faces; not modelled here | Same fallback. A game speed rule is the owner's later call (plan Q2 (b)). | step 16 (fly + dive) |
| Pop when a shell brick switches from generated to downsampled (an edit arrives) | The 97% / 99% threshold (row 13); side-by-side renders | step 10 tests; step 11 pose sheet |
| Shells do not cut frame time; open views may cost +2.7 ms (est.) | Accept. Shadows and the main march are separate steps. | step 23: warm ABBA + Gate C "frame total p50/p99" |
| Pow2 rings alias (§6.2 phantom terrain) | `&` masks only, zeroed directories, one validator test per shell (like `ClipmapValidator.CountStaleSlots`) | step 11 EditMode |
| Less building room once the 0.1 m pool is cut to peak × 1.25 | Valve line at 0.85 × cap (§3.6) stays. Re-measure `PeakUsed` with a build test. | step 14 ledger |
| The physics/query world shrinks to ~80 m | Game-design follow-up; no API change here | -- |
| More evictions churn D.1 saves (save cap 4/frame, task 26) | Watch "deferred evictions" | step 14 rig gates C/D |
| Largest voxel on screen | New pose metric: ≤ 2 px at 720 rows, ≤ 1.5 px at 540 (rule A) | step 11 and step 23 |
| Memory | `-memledger` + peak footprint ≤ ~2,400 MB at poses (est. target) | steps 14 and 23 |

## Questions for the owner

Answer "all (a)" to approve the draft as written.

1. **Q1. Which internal rows size the shells?**
   1. (a) **720 rows.** You chose FSR 3 dynamic 540-720, so voxels arrive at ≤ 2 px at 720 and ≤ 1.5 px at 540. It costs ~+150 MB and ~15% more generation than 540 (est.), not the 1.78x the plan feared.
   2. (b) 540 rows. Cheapest (terrain ~700 MB, est.). At 720 rows a coarser voxel arrives at 2.7 px.
   3. (c) 720 for shells 0-2 (the ground near you) and 540 beyond. The cost is between (a) and (b); one more config table to keep.
2. **Q2. Where does each shell hand over?**
   1. (a) **Where the finer voxel reaches 1 px** (the coarser one arrives at 2 px). The 2026-09-25 test of the stricter bounds looked no better and cost +6 ms. Terrain ~855 MB at 720 (est.).
   2. (b) Strict: never a voxel coarser than 1 px (the plan's wording). ~2x the terrain memory (+855 MB at 720 sizing, over the 3 GB cap), ~1.8x the generation, +3-6 ms march (est.).
   3. (c) Strict for shell 0 → 1 only (the near ground), (a) beyond. About +300 MB (est.), part of (b)'s march cost, and shell 0 then needs the two-level directory (a §3.7 change).
3. **Q3. Far edits (op log) vs saved chunk edits (D.1): which wins?**
   1. (a) **By order of time.** Each saved chunk remembers, in the new op file, how many ops existed when it was saved: older ops, then D.1, then newer ops. Exact, and D.1 stays frozen.
   2. (b) The plan's simple rule, all ops then D.1. A far crater made after you dug in that chunk is lost inside the bricks you dug.
   3. (c) A new D.1 version that carries the op count. Changes a frozen format (a redesign).
4. **Q4. Awake fluid outside the smaller 0.1 m window.**
   1. (a) **Its chunks stay resident while inside the 32-chunk CPU ring (~±204 m).** No new §4.4 state. Beyond that, fluid waits for step 19's amendment.
   2. (b) Add a "patch" state to the §4.4 table now. A redesign of a frozen table, before we know step 19 needs it.
   3. (c) Do not shrink the window near awake fluid. Memory then depends on how much water is moving.
5. **Q5. The coarse-first pass line (step 10).**
   1. (a) **Centre sample, ≥ 97% solid/air agreement in surface bricks, and ≥ 99% of column heights within 1 coarse voxel.** Exact in tests, and close to the majority vote on smooth ground.
   2. (b) Corner sample (Q19 4a as written) with the same line. It shifts surfaces by half a voxel, so expect misses and a pop at refine.
   3. (c) A looser 90% line. Fewer stops, more visible pop.
6. **Q6. The far band once shells reach 6.6 km.**
   1. (a) **Keep it beyond the last shell and as the fallback while shells fill. Retire it at step 23 if the pose sheet shows the shells cover the horizon.** That would then save 40 MB and up to 1.8-3.8 ms (est.).
   2. (b) Retire it at step 11. Saves the same, sooner. A teleport then shows sky until the coarse shells arrive.
   3. (c) Keep it for good.
