# Task feature notes (moved out of CLAUDE.md, 2026-10-22)

One paragraph per finished task: what it added, its flags, its tests and its demo shots.
Read the paragraph for a feature before touching it (`grep -n 'Task 93e' TASKS.md`); never read this file whole.
New tasks append their paragraph HERE, not to CLAUDE.md (owner, 2026-10-22: CLAUDE.md is re-read on every turn).

Task 64 (gameplay step 1): play starts in SURVIVAL (F6 "Game mode" / `-gamemode creative`): LMB digs into a
36-slot inventory, RMB places a grid-snapped cube, E bag, Q size; rigs never read the file, so they stay Creative.
Task 65 (gameplay step 2): in play, walk mode has swim/dive (Space up, Left Ctrl down), a double jump and a Left
Shift dash (`PlayerMotor.Step(dt, in MotorInput)`, numbers in PlayerConfig.json); rigs keep the legacy Step.
Task 66 (gameplay step 3): map v4 has ORES (`Assets/TerrainGen/Ores.cs`, GenMat 65-69: copper/iron/gold/cobalt/
emberstone by depth layer; density = layer base x `OreTable` biome x landform x2 favoured), sparse on purpose
(mixed bricks <= +5%, `OreTests`); v3 has none. `EXTRA="-oredemo"` shoots them; dev map Z x3 = 1-voxel slice.
Task 66b: eye inside water/oil/sludge/acid/honey/quicksand sees through it (tinted fog, bed, surface from below;
Raymarch.compute `passMat`, 0 = the old path); F6 "Underwater view" / `-underwater 0|1`; lava unchanged; `-movedemo` shoots it.
Task 73: `EXTRA="-riverdemo [-riverlook]"` shoots 4 river sites (slope, lake inlet, 10 m drop, mouth) near/mid/far, off/on
(task 76: seed 42 uses 73's cells; off = shape only, on = river look + see-through; A also at 20 m/150 m/800 m/2 km).
Task 76 (R1+R2+R5+R6): map v4 rivers are AT REST: `Rivers.cs` (a per-column level along drainage segments, never rising
downstream, ending at the lake/sea level; trapezoid channel, banks; a one-voxel LIP, GenMat 8 = engine 136, under every
step: solid to the CPU, drawn as water by `RiverLipAsWater`). Flow map = `WorldMap.FlowDir` (16 bits/cell) -> `FarField.RiverFlow`.
Play setting `riverLook` (PlaySettings.json; no F6 row yet) / `-riverlook 0|1` (on in play, off in rigs): risers drawn as surface, ripples along the flow,
foam where steep, near and far alike. `FluidAtRestTests` = BUGS #7's acceptance (rivers, sea, lava lake, pockets).
Task 75: liquids seen from above/the side are SEE-THROUGH, each by its own clarity (Raymarch.compute `FluidExtinction`,
one table with 66b: water ~5 m, acid 1.5, honey 1, sludge 0.5, oil 0.2 m; lava opaque; the far band too, from depth bits
25-31 of its water texels). F6 "See-through liquids" / `-seethrough 0|1`: on in play, OFF in rigs/capture runs (byte-identical).
`EXTRA="-liquiddemo"` shoots test pools + a lake shore/near/mid/far, off and on; cost: `run-cost-split.sh` with
`EXTRA="-costarms see_on,see_on -costpaths lake"`.

Task 80 (engine 11b): `-shells 1` now DRAWS shells 1-7 (Raymarch.compute `ShellMarch`; CPU twin `ShellRayReference`,
`ShellHandOverTests`); cascade tiers 1-2 stop drawing while shells draw (still allocated); OFF by default (77's Q1).
`-shellsdebug S` also tints each pixel by shell; with `-shells 1`, `MODE=poses` shoots every pose OFF/ON/tint at noon with
the per-shell px metric (`EXTRA="-shells 1 -shellworkers 3 -gateres 1280"`, sheets: `tools/shells80-sheet.py`).

Task 81 (engine 13a, 8.14 rows 8-10): FAR EDITS are D.4 ops: `IEditOps.Record(EditOp)` (`EditOps`; sphere/box, Air = carve;
kind 3 reserved for D.3 prefabs) appends to `ops.log` in the delta folder (`EditOpLog`; written only once an op exists, cleared
with the deltas) and applies at once to resident chunks via EditService; workers apply the log's immutable `Snapshot` on
generation (`EditOpApply`: 0.1 m chunks, and shells at their own voxel size). Load = ops <= S, D.1, ops > S (S per chunk in the
log's side table; CRC mismatch -> all ops, then D.1). `SetVoxel`/D.1 unchanged. `EditOpTests` (10). `EXTRA="-farop -shells 1
-shellworkers 3 -shellwait 180"` = a 20 m crater shot.

Task 82 (engine 13b): an op recorded LATER redraws at once. Shells: stored chunks it touches are REGENERATED (no CPU brick copy)
and re-stored gap-free (`ShellStore.Store(.., replace: true)`: old copy drawn until the new one is up, old page freed after the
next directory flush; a FULL shell has no room for both -> evict, store fresh, 1 frame of the coarser shell); chunks in flight
are caught up on arrival. Far band: fill jobs apply ops per texel (2.5D: crater / mound / deeper water yes, tunnel / overhang /
floating blob no; `FarField` header EDITS); later ops patch their footprint through the strip staging + FarScatter.
`EditOpRedrawTests` (4, one Slow). `EXTRA="-farop -faroplate -faropr 400 -shells 1 -shellworkers 3 -shellwait 240"` = before/after.

Task 82b (engine 15): GENERATION BRICK SKIP. Generate and GenerateShellChunk write a brick as uniform without the per-voxel
pass when the column data PROVES it one material (`DetailField.UniformSamples`: air above max(ground, water, sea), sea/lake
water, soil, layer rock with no cave/pocket/ore reaching it). Byte-identical on or off (`BrickSkipTests`; `BrickSkip` field,
`SkipStats`). Isolated: 0.1 m chunk 26.8 -> 9.2 ms; shells 1.2-1.45x (their column pass now dominates).

Task 83 (engine 11d, owner Q44 (a), 8.14 row 13b): SHELL CAVE FILL. Shells >= 3 (`FIRST_FILL_SHELL`) are NOT the generator below
the surface: after generation + ops, `TerrainGenChunkSource.ShellFill.cs` fills enclosed AIR and (task 83b, Q46 (a)) enclosed FLUIDS (`ShellFillFluids`; sea, lakes, rivers and any fluid a ray can reach kept), opens cave mouths a
point sample misses (>= 1/4 of a voxel's underground 4^3 sub-samples are cave air), flattens interior mixed bricks and wholly
interior chunks. Rays from outside see the same (`ShellFillTests`); switches `ShellFill*` (tests turn them off). A filled chunk
that missed an op is REDRAWN, not caught up. Shell 1-2 page caps measured x 1.25 (`ShellLayout.PageCapFor`).

Task 85 (engine 12, owner Q47 (a)): shell chunks stream NEAREST ON SCREEN FIRST across all shells (`ShellStreamer.OrderKey`:
max(distance to the chunk, its shell's inner bound) x (1 - 0.25 cos to the velocity)), not coarsest first (shells are hollow:
a missing fine chunk is a hole). With shells on, the hand-over is by distance: shells march from the camera, tier 0's held
chunks inside its band are leapt (`ShellMarch`'s tTier0 / `ShellRayReference.March`), no `FarStartT`, no `-detailspeed`.
`ShellOrderTests` (order, margins > 0 on every preset: `Logs/Shells/margin85.txt`).

Task 86 (owner Q45 (a)): COARSE CAVE FIELDS. `DetailField.PrepareColumn` evaluates the cave noises (tunnels, their depths,
caverns' nR/nF, the underworld chamber) at one node per 8x8 columns (`TerrainSpec.coarseColumnShift` = 3, nodes at 7 mod 8 so
shell >= 4 columns sit on nodes) and interpolates bilinearly; geology/soil/ore stay exact (strata do not move). Pure function of
(x, z); loops pass a `NodeCache`. `-coarsecols 0` = the pre-86 bytes; `CoarseColumnTests` (largest cave shift 1.41 voxels).

Task 93a (engine E1, 8.15 §5 "Live"): EDIT SUMMARIES. With shells on, `ChunkStore.SetVoxel` marks edited bricks
(`Chunk.editMask`, CPU-only; D.1 records on reload; op writes not marked) and tells `ShellStreamer` (IChunkEditSink); an edited
resident chunk a shell >= 1 covers is majority-voted per shell level (`EditSummary.cs`, Burst job on a main-thread snapshot,
<= 1/s per chunk, `EDIT_SUMMARIES_PER_FRAME` = 1, refusals counted) and the covering shell chunks redraw as generator + ops +
summaries, then the fill. Evicted = summary dropped (saved summaries are E2). `-editsummaries 0` = off. `EditSummaryTests` (5).
`EXTRA="-farwall -shells 1 -shellworkers 3"` = a SetVoxel tower near/far, off/on (tier 0 draws it near today; far shows the shells).

Task 85b (BUGS #9, owner Q48 (a)): FAR SHADING = its own kernel `CSMainFarShade` (VE_FAR_SHADE; every line under #ifdef, so
OFF is byte-identical -- runtime ifs were NOT, 1-LSB diffs): hemisphere sky light (`FarShadeSky`, follows time of day), the
far band's column-height slope normal from where a 0.1 m voxel is 2 px (`FarSlopeNormal`), height-aware haze that light
(torches, emission, bleed: `lightAdd`) mostly escapes. Look 1; near, tiers, shells, far band. `-farshade 0|1` / F6 "Far
shading" (PlaySettings.farShade): ON in play, OFF in rigs/capture. Not with `-checker` or the `-detailspeed` square.
`EXTRA="-farshadeshots -gateres 1280 -poses a,b"` = day lit/backlit + night torches, OFF/ON, raw/FSR 3
(`tools/farshade85b-sheet.py`). Far lights (step 21) add into `lightAdd`.

Task 93a1 (gameplay BLD1): Survival DRAG LINE. RMB press-drag-release stamps the brush cube along the integer DDA line
(`SurvivalBuild.LineCubes`, <= 256 cubes) in one `EditService.SetCubes` call; pays cube by cube, stops when the slot runs out;
LMB held still = tool dig as before, turned > 4 deg = dig stroke; preview + count while dragging, Esc cancels (keeps the mouse).
`Playground.Stroke.cs`, `StrokeTests` (8). `EXTRA="-strokedemo"` = the 6 shots (wall base, short slot, trench).

Task 93b1 (gameplay BLD2, owner Q1): Survival FACE PULL. Alt (Option) + RMB drag on a face of the LAST shape placed (click, BLD1
line or previous pull, while every cube's centre is solid; else the aimed Q-size cube): the shape's cubes on that face
(`SurvivalBuild.FaceLayer`) repeat along the normal, N = the aim's closest point on the normal line (`PullSteps`); back in = dig.
Layer order, stops when the slot runs out, <= `MaxPullCubes` (2,048 / 131k cells), one `SetCubes`. `Playground.Pull.cs`,
`PullTests` (10). `EXTRA="-pulldemo"` = 9 shots on a flat map site (block -> thick wall, a hut).

Task 93c1 (gameplay step 4, P21): play STARTS in the best easy area (`TerrainGen.PlayStart`: the largest B4 easy area, ties
-> nearest the island centre; its first easy + Walk + dry cell whose 8 neighbours are too; task 98: MILD FIRST -- Forest/Savanna
cells counted per area, areas with >= 2 ha of them ranked by that count, start cell + neighbours mild; else any biome;
`-playstartmild 0` = 93c1's rule; seed 42 snow -> forest); R goes back there; KEEP=1 resumes
at `player_pose.json` (beside player.json, saved every 30 s and on quit). Rigs/capture runs and `-playstart 0` keep `Tg2Spawn`.
`-worldseed N` = another tg2 island (own folder); `-playshotquit` quits after `-playshot`. `PlayStartTests`, `PlayerPoseTests`.

Task 93b2 (BUGS: shells never fill while walking, part one of Q52 (a)): a shell chunk's COLUMN PASS (~90% of it; (shell, x, z) only)
is kept in its scratch and skipped for the next layer of the same column (`GenerateShellChunk` COLUMN REUSE); `ShellStreamer`
hands a returning scratch its column's nearest missing layer first and the global pick passes over owned columns (header COLUMN
AFFINITY; scratches 2 x workers + 2). Same bytes; `-shellcolreuse 0` = old. Per-shell counters `ShellStreamer.CountersReport()`
(walk logs); `-walkshellwait S` waits at the walk's start pose first. `ShellColumnReuseTests` (3, walking twin Slow).
Part two (owner Q54 (a)): task 83's cave fill runs on shells 1-2 too (`FIRST_FILL_SHELL` = 1; `-shellfillfrom 3` = old): caps no longer stop shells 1-2 (start pose complete), and task 93b2d cut the fill to ~5-15 ms per chunk (seed scans start at `ScanLo`; same bytes; `-shellfillfast 0` = old;
per-phase worker times in the walk logs' `f1..` lines): walking is still short on workers (KNOWN_ISSUES).

Task 93d1 (gameplay step 9a): Survival walk has HEALTH (`PlayerVitals`, ticked after each motor step in `Playground.Vitals.cs`): fall damage
linear in fall height (motor's `LastImpactDownMps`; 4 m safe, 20 m lethal; liquid at the feet cushions; the first landing after entering
walk is free), AIR 20 s under a liquid then 10 HP/s, lava 50 / acid 20 HP/s, regen 2 HP/s after 8 s; 0 HP = SOFT death (screen 3 s, then
the play start, inventory kept). Numbers in PlayerConfig.json. Creative, fly, rigs, older demos and `-vitals 0` take no damage.
`PlayerVitalsTests` (10). `EXTRA="-vitalsdemo"` = 7 shots (full, 12 m fall, under water, drowning, lava, died, respawned).

Task 93d (engine 14d1, 8.15 row D1, Q49): with `-shells 1` cascade tiers 1-2 are NOT ALLOCATED (`LODCascadeManager.TiersWanted`;
`Phase4Bootstrapper.Cascades` = null, every reader checks; no worker downsample, no DownsampleScratch). Shells off: unchanged.
`-keeptiers 1` allocates them anyway (diagnostic arms that set DrawShells false and want tiers 1-2 behind). `CascadeTiersOffTests`.

Task 93e (engine 14d2, 8.15 row D2, owner Q55 (b)): `-shells 1` shrinks the tier-0 GPU mirror box to `ShellLayout.MirrorBox` (DEFAULT;
`-smallmirror 0` = old box; 16^3 Low-High, Ultra keeps 32x8x32; centred by its own size, `StreamManager.MirrorOrigin`); CPU load radius stays 10.
Recentres re-dirty slots held by another chunk (`TerrainClipmap.RecentreDirtiesStaleSlots`); the validator skips residents outside the box.
The GPU fluid CA reads chunks outside the box that fluid touches from the FLUID TERRAIN DIRECTORY (`TerrainClipmap.FluidDir.cs`, `-fluiddir 0` = off):
fluid stays live to 128 m on every preset. `UploadDirtyBoxTests` (9), `FluidTerrainDirTests` (5). `MODE=fluidprobe EXTRA="... -fluidfar 110"` = a pool ~110 m out.

Task 93f (engine 14d3, 8.15 row D3, owner Q2 (a)): with `-shells 1` the GPU BODY BUFFER is its own pool (`TerrainClipmap.GpuPool.cs`;
cap `EngineConfig.GpuBrickCapFor` the box = 254,400 for 16^3, measured peak 203,450 x 1.25), mapped through a CPU-slot -> GPU-slot
table (refcounted per holder: box slot or fluid dir entry); staged handles carry the GPU slot, so shaders are unchanged. Full pool ->
refused + counted (ledger), chunk held as air, its shell draws it, retried when slots free. `-gpupool 0` = old layout, `-gpubrickcap N`.
Shells off: old layout. `GpuBrickPoolTests` (6); validator compares through the table (`[mirror]` lines add `GPU pool ... body mismatches`).

Task 93g (engine 14d4, 8.15 row D4): tg2 CPU brick cap `TG2_BRICK_POOL_CAP` 750,000 -> 570,000 (= worst measured peak 379,527 x 1.5,
rounded to 10 k; valve 0.85 x cap = 484,500; rigs keep `BRICK_POOL_CAP` 500,000). `-cpubrickcap N` overrides it for one run
(`Phase4Bootstrapper.CpuBrickCap`). `-memledger` ends with one `STEP 14 (8.15 D1-D4)` line: CPU pool cap/peak/valve, GPU body pool
cap/peak/refused, mirror box, tiers 1-2 allocated, process phys_footprint now/peak (in-process, proc_pid_rusage) vs 3,000 MB. `CpuBrickCapTests` (2).

Task 93b5 (engine, owner Q58 (a)): SHELL AIR-BRICK MASK. 64 bits per shell page (bit = a 4x4x4-brick group holds a non-air brick;
the page buffer's tail, `ShellStore.GroupMask`, 33 KB High), built on every Store. `ShellMarch` with `-shellmask 1` crosses an empty group's
in ONE leap, exact via the EXIT-PLANE leap the mask turns on (93b5 (b): `ShellLeap`, t from the plane, no eps; CPU twin
`ShellRayReference.March(useMask, planeLeap)` / `PlaneLeap`, `ShellAirMaskTests`): iterations -54% / -28%, p50 vs today -2.1 / +0.8 ms
(walk_near / walk_open), ~0.02% px moved 1-7 levels, so OFF by default (mask off = the old leap, byte for byte). Arms `shellmask_off`, `shellleap_new`.

Task 93e1 (gameplay RX1): ADJACENCY REACTIONS in `MaterialRules`' pair table (both directions via `Pair`): water + lava -> steam
(water cell) + COOLED STONE (lava cell; new id 23, was obsidian + air); water + tg2 loam/red earth -> mud (water kept); lava + sand
(legacy, tg2, beach) -> GLASS (new id 24, acid-proof; lava kept); lava + snow/tg2 snow/packed ice -> water (lava kept, then quenched).
GPU: `CSReact` sets the op wake bit for MOBILE products (air/solid products unchanged). `ReactionPairTests` (6), `EXTRA="-reactiondemo"` = 4 tray shots.

Task 93f1 (gameplay step 5): play walk has the WALK BAND + CLIMBING (`PlayerMotor` abilities path only; rigs keep the legacy
step-up): auto-step only if the next 0.5 m rises <= 30 deg (`walkSlopeMaxDeg`), steeper ground under the feet slides you down
(Space scrambles), pressing into a face rising >= 60 deg (`climbMinSlopeDeg`, any wall >= 0.9 m) for 0.2 s clings: W up 1.5 m/s,
A/D sideways, Ctrl down, release W/Space drops, top mantled; stamina 7 s, refills on a 0.6 m ledge; loose/slick faces
(`MaterialRules.ClimbGrip`) slower. Numbers in PlayerConfig.json. `ClimbSlopeTests` (11). `EXTRA="-climbdemo"` = 6 shots.

Task 93g1 (gameplay step 6): GLIDING on `PlayerMotor`'s abilities path (`CanGlide`/`StepGlide`): airborne, air jumps spent, falling,
Space HELD -> sink eases to 2 m/s, speed to 10 m/s along the look yaw (ratio 5; S brakes to 4, A/D drift 3 m/s); release Space = normal
fall; landing / water / dash / cling end it; lands at ~2 m/s (no fall damage). Numbers `glide*` in PlayerConfig.json (speed 0 = off).
`GlideTests` (7). `EXTRA="-glidedemo -playshotquit"` = a real summit within 220 m of the start, lane away from the sun, 4 shots.

Task 100 (gameplay step 7): GRAPPLING HOOK on `PlayerMotor`'s abilities path (`FireHook`/`RayHook`/`StepReel`/`StepSwing`/`ApplyRope`):
F or middle mouse in play's walk fires from the eye at the first SOLID voxel within 25 m (air + fluid passed, unloaded = miss; ring on the
crosshair = in range); HOLD = reel 12 m/s (no gravity; stops at the wall or 0.3 m from the anchor), released in the air = swing (inextensible
rope, WASD pump); Space lets go (the press still jumps / air-jumps -> glide), dash/swim/cling/landing/dug anchor/Teleport/legacy Step end it.
F in fly mode = the basin toy as before. Numbers `hook*` in PlayerConfig.json (range 0 = off). `GrappleTests` (11). `EXTRA="-grappledemo"` = 5 shots.

Task 103 (gameplay step 8): WALL-RUN + WALL-JUMP on `PlayerMotor`'s abilities path (`TryStartWallRun`/`StepWallRun`/`WallJump`/`WallBeside`):
airborne near a jump's top (vy <= 2.5) or falling < 5 m/s, moving >= 3.5 m/s along a wall beside the body (blocks at 0.5 m and 1.2 m, within
0.15 m; axis-aligned) with the keys along it -> runs 7 m/s, gravity x 0.2, up to 1.2 s; ends with the wall, keys released / into the wall
(-> the climb grab) / away, Ctrl, dash, hook, landing. Space against any wall in the air (not a ground/coyote jump) = wall-jump: 5.5 m/s off
the face + a full jump, along-wall speed kept, air jump kept; the same wall is not reused before landing (chimneys: alternate). Both spend climb
stamina (run 1 s/s, jump 1 s). Numbers `wallRun*`/`wallJump*` in PlayerConfig.json (0 = off). `WallRunTests` (8). `EXTRA="-walldemo"` = 5 shots.

Task 94 (8.16, owner design change): FLUID GRAVITY FALL. A fluid (not steam, not sand) with air below falls every tick, ungated by
viscosity, the same for all fluids: `FluidFall` table (exact g t^2/2 floored to voxels, cap 12 m/s -> 27 voxels/tick at 4.5 Hz), read by
`FluidReferenceCPU` and `FluidCA.compute` alike; k (ticks fallen) in GPU `stateFlags` [15:8] / a CPU side array. Stops above the first
non-air cell; viscosity now gates only sideways/diagonal/draining moves. `-fluidfall 0` = v8.6 rule. `FluidGravityFallTests` (8).
`BUILD=1 MODE=fluidprobe PROBEWORLD="" EXTRA="-fallstrip" ./run-tg2world.sh` = water/lava/honey before/after strip.

Task 99 (engine, owner Q61 (a)): SHELL SKY-RAY EXIT, ON by default (`-shellskyexit 0` = off). A rising shell ray at or above
`ShellStreamer.SolidTopY` (running max, never lowered, of every stored non-air brick's top; `ShellStore.Store` raises it, redraws too)
-- primary rays with the far band on also 2 voxels over the far band's max height -- ends as a no-hit (`ShellSkyY`, `SHELL_END_SKY`).
Exact (0 px, no-upscaler stills; CPU twin `ShellRayReference.March(skyY)`, `ShellSkyExitTests` (5)): p50 -4.7 / -2.6 ms (walk_near /
walk_open, warm ABBA). Cost-split arms `skyexit_on` / `skyexit_off`; the counters line gives sky rays and their iterations (ShadowStats 62-64).

Task 101 (engine C1, 8.15 §4/§14): WORLD STATE `world.state` beside the deltas (`WorldState.cs`; `StreamManager.World`): world clock
(game seconds, `AdvanceClock(Time.deltaTime)` in play and rigs), world stage byte (0), per chunk last-seen (stamped on eviction and at
each save for residents) + generation stage (noted on arrival). Nothing reads them yet (C2, S1). Written atomically, CRC, every 30 game s
and in FlushAllDirty, ONLY once the save is real (file existed, ops, or a delta written); cleared with the deltas. Bad file -> defaults +
warning, moved to `.rejected`. `WorldStateTests` (4).

Task 104 (engine Q1, 8.15 §7): FAR QUERY `IFarQuery.GetVoxels(points, results)` (NEW beside the frozen GetVoxel; `FarQuery`,
`StreamManager.FarQuery`, null on sizeClass 0/1): resident chunk -> ChunkStore; else the D.1 brick (LRU of parsed deltas, validated by
TryDecodeOnto; SaveDelta calls `ForgetDelta`) then ops > S; else `TerrainGenChunkSource.PointVoxel` (Burst, Generate's exact calls) + all ops.
Main-thread plan, Burst `PointJob` parallel over 32-point batches. `FarQueryTests` (4, one Slow). Editor test timing: ~7-10 us/point on a
new column, 0.3 us in vertical runs (single thread), ~2 us wall parallel.

Task 93c (owner Q49 / Q63 (a)): PLAY STARTS WITH THE LOD SHELLS ON (`PlaySettings.shells`, F6 "LOD shells", applied at the next start;
`-shells 0|1` overrides per run; `PlaySettings.PlayShellWorkers` = 3 unless `-shellworkers N`), decided at boot by `PlaySettings.ResolveShells`
(`Phase4Bootstrapper.ShellsOn`/`ShellWorkers`). Rigs, capture runs and -fluidprobe never read the file: shells only with `-shells 1`, as before.
Standing-still frames in play ~8-11 ms slower (estimate, not measured; `later/102-shell-march-cost.md`). `EXTRA="-shellsdemo [-shells 0]"` = 3 poses + 93a's ridge.

Task 105 (gameplay step 10): HARDNESS + PICKS (`Assets/ContentModules/Tools.cs`). Survival's LMB digs with the held pick
(`Inventory.pick` in player.json; Z/X switch it, a dev key until crafting, P11): each voxel costs 1 + max(0, hardness - tier + 1)
of the pick's rate (`EditService.MineSphereCosted`, `SurvivalBuild.DigWithPick`); ores need a tier (stone pick -> copper,
copper -> gold, gold -> iron, iron -> cobalt, cobalt -> emberstone; bedrock never): aimed too hard = REFUSED, else too-hard voxels
stay; strokes/pulls keep them too. Creative and rigs unchanged (hand/drill/bore). `ToolTierTests` (10). `EXTRA="-tooldemo"` = 3 shots.

Task 108 (engine E2, 8.15 §5 / §10 row 6): SAVED EDIT SUMMARIES, save file D.5 `summary/{cx}_{cy}_{cz}.sum` beside D.1
(`SavedEditSummaries.cs`; `StreamManager.SavedSummaries`). `SaveDelta` builds the chunk's summary right after D.1's rename with 93a's
path (`EditSummaryOps.Snapshot/Run/Extract`, main thread) for the bricks D.1 records, and writes it `.tmp` + rename with a CRC and D.1's
CRC (a summary whose D.1 changed is ignored: StaleDelta; corrupt/truncated rejected, never thrown); a D.1 deleted (coalesced) deletes it.
Format: 32 B header (magic, version, seed, chunk, D.1 CRC, S = ops at save, brick count), brick list, level-1 voxels per brick (a uniform
brick = 2 B), levels 4-7 (levels 2-3 re-voted from level 1 on load, so every level == the live 93a summary). Shells: `ShellStreamer.Saved`
publishes the saved summary of every covered chunk without a live one (eviction swaps live -> saved with no redraw; <= 8 disk reads/frame),
applied in row 10's order (`GenerateShellChunk`: ops <= S, summary, ops > S). Far band: per edited brick column the 2 sampled columns as
`FarSpan`s, folded 2.5D (`FarField.ApplySpan`: a build that reaches the top raises it, a dug run lowers it, a buried run changes nothing),
patched when the store changes. Index + far spans + LRU (16 MB cap) + one 4.3 MB scratch (`RamBytes`). `StreamManager.ClearSaveFiles`
(`-cleardeltas`) clears it with the deltas, ops.log and world.state. `-savesummaries 0` = not written, `-savedsummaries 0` = not drawn.
`EditSummarySaveTests` (5). `EXTRA="-farwall -farwallsaved -shells 1 -shellworkers 3"` = near, far saved OFF / ON shots.
Task 113 (111 Q8 (a)): the build is OFF the main thread. `SaveDelta` (after D.1's rename, which stays on the main thread) only snapshots
the chunk into a scratch the store owns and schedules the Burst vote; `SavedEditSummaries.Pump` (top of `StreamManager.Update`) finishes
completed jobs on the main thread (Extract, encode, `.tmp` + rename, index); `FlushAllDirty` and `Dispose` call `CompleteAll` (shutdown
writes every pending one; `-cleardeltas` runs before the store opens, so nothing is pending then). <= `SAVED_SUMMARY_SCRATCH` (4 =
`MAX_CHUNK_SAVES_PER_FRAME`) builds in flight, ~4.3 MB each on demand (`RamBytes` counts them); a 5th finishes the oldest first
(`SyncFallbacks`). PENDING: `Has` true, `TryGet` = NotLoaded (no disk read), `Warm` skips it, the far band keeps its previous spans,
ShellStreamer keeps the dropped live summary as a stand-in (no redraw); a newer D.1 supersedes it, a deleted D.1 drops it (`Superseded`).
Same .sum bytes as 108 (`Synchronous = true` keeps the inline path for tests). Flags unchanged. Tests +2 in `EditSummarySaveTests`
(bytes identical with the pool overwritten and the chunk reloaded while pending; evict-while-pending stand-in / reload / landed swap).

**Task 112 (measure tooling, 111 Q9; no product behaviour change).** `run-cost-split.sh` now honours `-renderrows N`
(`PlaygroundCapture.CostSplit.cs` `CostRows`: last match, clamped to `UpscalerSelect`'s 540-720; "dyn"/none = 540, so older arms are
unchanged; before 112 the flag was silently ignored, e.g. 110's planned 720 arm ran at 540); the header reads `FULL = fsr3 N rows pinned`,
and `ups_none` / `-coststillraw` use the same rows. Rows below 540 are not reachable (`UpscalerSelect.MinRows` = 540). The `-sustained`
WHOLE RUN line (`MODE=walk`) prints p50 / **p95** / p99 (30 fps line: p95 <= 33 ms). Golden-hour walk: no flag needed -- the sustained
probe pins the sun at golden hour (6 deg, 17.59 h) unless `-tod H` is passed (110 passed `-tod 12`); `-tod 17` = ~14.5 deg. Command:
`MODE=walk MINUTES=3 EXTRA="-renderpreset quality -upscaler fsr3 -renderrows 540 -farband 1 -farshade 1 -seethrough 1 -riverlook 1
-tod 17.6 -memledger -frametiming -shells 1 -shellworkers 3" ./run-tg2world.sh`.

**Task 114 (visual look review, diagnosis only; no default or look change).** `-upscalerclips -lookreview -quality low|medium|high`
(`PlaygroundCapture.LookReview.cs`; `run-look-review.sh` builds and runs one launch per preset, `PRESETS=`, `SKIP_BUILD=1`).
Poses: walk_near and walk_open (task 57's arcs), ridge, and high (highest column within 800 m plus 20 m). Arms at each pose:
fsr3 / none at the preset's rows (UpscalerSelect clamps to 540-720), native, and fsr3 + ShellTint, plus per-shell voxel px
(`[px]` lines). Motion bursts (still, walk, turn; 4 consecutive frames) at walk_open and ridge, and golden hour (17 h, High only).
`-lookwait S` sets the shell wait (45 s; what is still missing is logged). `tools/look-review-diff.py bursts|crop|sheet` makes
the difference images, crops and sheets. Report: `docs/archive/VISUAL_REVIEW_114.md`. Top defects: olive side faces and staircases, a
lighting band that moves with dynamic rows (FarShadeBlend), far-band rock contour dashes (FarWallMaterial lip), a flat sky,
and tier 0 drawn below 1 px beyond shell 0.

**Task 115 (stutter isolation, measurement only; no product behaviour change).** `-spikelog` on `-sustained` (implies
`-framegap` + `-frametiming`) writes `spikes.csv` beside `sustained.txt`: one row per frame (EOF-to-EOF ms split into
preUpdate / update / postLate, GC counts, streamer phases, shells.Update ms (`FrameGapProbe.LastShellsMs`, new), far ms,
admits/evictions/saves, clipmap and shell upload bytes, fluid ticks, readbacks landed, light uploads, upscaler rows,
FrameTimingManager fields, camera x/z). `tools/spike-report.py <csv> [<csv>..]` (auto-trims the post-walk rows where the camera
stops): histogram, spikes vs slow stretches (local median +-15 frames), co-occurrence lifts, lagged excess, circle sectors across
walks; `tools/spike-attribution.py`: exclusive spike classes + "spikes removed" counterfactual. Command: `MODE=walk MINUTES=3
EXTRA="-renderpreset quality -upscaler fsr3 -renderrows 540 -farband 1 -farshade 1 -seethrough 1 -riverlook 1 -tod 12 -spikelog
-shells 1 -shellworkers 3" ./run-tg2world.sh`. Result (HANDOFF 115): the walk's tail is slow VIEWS (GPU), not hitches.

**Task 116 (engine, owner Q66 (a), PERF_30FPS_PLAN row 1): SHELL COLUMN SKY LEAP, OFF by default (`-shellcolleap 1` = on).**
Per shell page, 16 bytes of COLUMN TOPS in the page buffer's tail after the masks (`ShellStore.ColumnTops` / `ColTopBase`,
`COLTOP_UINTS` = 4; byte j = (gz << 2) | gx = the highest non-air local brick y + 1 of that 4x4-brick column; 66 KB on High),
built in `Store` and uploaded with the page (redraws carry their own: a page is never rewritten). `ShellMarch`: a rising ray in an air
brick at or above its column's top crosses the column (or the whole chunk's x/z when every column is that low) up to the chunk top in
ONE leap (`ShellLeapBox`), where `ShellBoxLeapOk` (93b5's group test for any box) allows it. Exact: CPU twin
`ShellRayReference.March(colLeap)`, `ShellColumnLeapTests` (6; exit-plane leap: every field equal; default leap: same cells/ends, t in
its last bits; with the sky exit; a mutation caught; uploads + redraws; LeapBox = Leap on cubes); `ShellAirMaskTests.CheckStored` also
checks the tops of real tg2 pages; GPU no-upscaler stills ON vs OFF 0 px at walk_near / walk_open. Shell iterations per sky ray
82.2 -> 29.5 (walk_near), per pixel 30.3 -> 19.0 (walk_open), but p50 ON vs OFF +1.9 / -0.2 ms (`Logs/costsplit_2026-10-22_202941.txt`)
and MODE=walk ABBA p50 41.71 vs 41.70 ms, so OFF (the task's rule). Cost-split arms `colleap_on` / `colleap_off`; counters ShadowStats
65-66 (`[shell ..] column sky leaps`). Driver `Screenshots/116/116-driver.sh`.

**Task 119 (look, owner 2026-10-22, 114 Q1/Q3/Q4/Q6): FAR-BAND DASHES (VISUAL_REVIEW_114 #3), ON by default in the far-shading kernel.**
114 blamed the grey 1-px dashes over gentle far land (and their crawl when moving) on `FAR_WALL_LIP` rock. Wrong: the slope
rule below alone changed 0 px of the dash crop, and a `-farshadow 0` arm removed every dash (pink plain and snow). They are
the far band's STAIRCASE SHADOWING ITSELF: the far sun-shadow ray left the hit 0.05 voxel off the face, and the next texel-wide
step up shadowed the foot of every riser. Two changes, both only in `CSMainFarShade` (VE_FAR_SHADE: play and `-farshade 1`;
rigs and capture runs without far shading keep their pixels byte for byte):
(1) `FarShadowLift` (`-farshadowlift 0` = old): the far sun-shadow ray starts one texel (8 << fh.level voxels) above the hit, so a
step no taller than its width (any slope up to 45 deg) no longer shadows its own staircase; hills and cliffs taller than a texel
still cast shadows. (2) `FarRockSlope` (`-farrockslope 0` = old, the task's asked-for change): a far step side is its biome's rock
(`FarWallMaterial`) where `FarSlopeNormal`'s slope is above ~40 deg (`FAR_ROCK_SLOPE_NY` 0.766), the 1.5 m lip only where
FarSlopeNormal has no answer. Tests `FarRockSlopeTests` (3: flag text, both shader blocks inside VE_FAR_SHADE). Stills:
`./run-look-review.sh` with `EXTRA="-farrockslope 0 -farshadowlift 0"` (before) vs default (after); crops in `Screenshots/119/`.

**Task 117 (perf trial, owner Q65 "Try Medium, show me"; PERF_30FPS_PLAN row 2; NO default change).** `-quality medium` vs High,
both at 540 rows, FSR 3, shells ON. New capture-only look-review flags (`PlaygroundCapture.LookReview.cs`): `-lookrows N` pins the
fsr3/none arms at N rows (UpscalerSelect-clamped; before, High always shot at 720), `-lookposes a,b,..` only these poses,
`-lookbursts 0` skips the motion bursts and golden hour. Driver `Screenshots/117/117-driver.sh` (look stills, warm-up, cost-split
launches H M M H with `-costarms shells_off -quality X`, MODE=walk 3 min x 4 ABBA with `-memledger`). Result (HANDOFF 117): walk p50
-0.2 ms, p95 +2.1 ms, cost-split FULL -0.8 / -1.0 ms (inside the 0.3-1.6 ms noise); shell iterations per frame do not fall
(walk_open 12.4 M High vs 12.5 M Medium); process memory -112 MB median; Medium draws a dashed dark seam over the sea at the
3.3 km shell-to-far-band hand-off (`Screenshots/117/handoff_high_fsr3_540_3x.png`).

**Task 120 (look, VISUAL_REVIEW_114 #1 + #2 + #9, owner Q4 (a) 2026-10-22): CUBE FACE LIGHTING.** Far-shading kernel only
(`Raymarch.compute`, every use under `#ifdef VE_FAR_SHADE`, so rigs without far shading are byte-identical); three parts, all ON,
`-cubelook 0` = task 85b's look. (1) `-cubebounce 0` drops the ground bounce: sun colour x 0.25 (`CubeBounceStrength`) x sun
height x (1 - n.y) on the sky term (x skyOpen, VAO), so noon side faces stop going olive (golden hour barely changes). (2)
`-cubefilter 0` restores 85b's cube-face -> slope-normal switch at the live dispatch rows' 1-2 px (a pale band that moved with
FSR 3 dyn rows); ON lights a hit as the mean of its staircase's cube faces (`CubeLight`: top / x / z face light weighted by
|nS.i| x cosine to the eye, nS = `FarSlopeNormal`), blended from the hit's own face over a FIXED 20-50 m (`CubeFilterStartM/
FullM`); VAO fades over the same distance; near voxels, shells and the far band alike. (3) `-cellhash 0` hashes the colour noise
per 0.1 m voxel again; ON snaps the hashed voxel to the drawn cell (`(v >> s) << s`, s = shell, or cascade tier 1/2), patch
noise keeps its world size. Capture-only: `-lookab 1` shoots each fsr3/native arm also as `<name>_old` with the three parts off
in the same frame state, `-lookgolden 1` adds a 17 h shot (turned away from the sun) at every pose. Tests `CubeLookTests` (3:
flags, shader uses inside VE_FAR_SHADE, the face weights vs a brute-force 2D staircase DDA). Stills: `Screenshots/120/`.

**Task 118 (perf trial, owner Q67 (a); PERF_30FPS_PLAN row 5 (a) / step 4; NO default change): `-shadowclamp 128` is a NO-OP in play.**
With the shells drawn (play's default since 93c) tiers 1-2 are not bound and `RaymarchFeature.MaxRayDistance` (1280 voxels = 128 m)
is `_MaxRayDistance`, and `TraceShadowRay` takes `min(_MaxRayDistance, _ShadowMaxDistVoxels)`: the voxel sun ray is ALREADY clamped
at 128 m. The 290 m (tier 2's outer bound) applies only with shells off (rigs, F6 "LOD shells" off). Shell-hit sun shadows
(`ShellMarch`, iteration cap only) and far-band shadows have no distance clamp, so mountains past 128 m still shadow you. Evidence:
golden-hour (17 h, sun 15 deg) look launch, High, 540 rows, FSR 3, shells ON, 4 poses x {sun behind, sun beside}: no-upscaler and
sun-visibility stills full vs clamp128 0 px differ in 15 of 16 pairs (walk_open away none: 12,901 px in the bottom-left lowland while
shell 4 was still filling; its sun-visibility pair 0 px); `Screenshots/118/look/`, `Logs/lookreview_20261023_014030.txt`.
Timing skipped (identical kernel input: every outcome = no change). New capture-only flags: `-shadowclamp M` is now parsed in
`Start()` for every mode; cost-split arm `clampN` (e.g. `-costarms clamp128,clamp128`; FULL keeps the launch's clamp) and a
`[sun <arm>]` counters line (voxel shadow rays traced / steps, ShadowStats 2 / 4); look review `-lookclamp M` (with `-lookgolden 1`):
golden hour sun behind (`away`) and beside (`side`), fsr3 / none / sunvis, `_full` vs `_clamp<M>`. Driver `Screenshots/118/118-driver.sh`.

**Task 121 -- sky and haze (VISUAL_REVIEW_114 #4 + #6, owner 2026-10-22; LOOK, no timing).** ON by default; `-skylook 0` = the old sky AND haze,
`-skyhaze 0` = the old haze only. Sky (`Raymarch.compute` `LookAtmosphere` / new `LookSkyBackground`): the horizon colour is concentrated
near the horizon (`pow(1 - up, 4)` instead of `dir.y*0.5+0.5`), a time-of-day RIM tint over the whole horizon ring, strongest toward
the sun (`SunCycle.LookSkyRimAt`: 0 above 28 deg of sun, golden -> dusk red, gone by -10 deg; `_LookSkyRim`), the sun halo is never
darker than the rim (the old sky had a dark hole round a setting sun), and a sun disc (0.6-0.8 deg) in the sky only, never in the haze
target. Haze (`FarShadeApplyHaze`, far-shading kernel): range `SkyHazeRangeM` 1800 m instead of `FarShadeHazeRangeM` 3500, desaturation
by half the haze amount, and a fade to full haze over the last 45% of the far band's square (~3.1-5.6 km Chebyshev). Haze colour is
still the sky behind the pixel, so it takes the new horizon and rim. Also: task 119's `bool farRock` is now a `uint` -- with it a bool,
Unity's HLSLcc emitted `as_type<float>(bool)` for Metal and the kernel failed at run time (every frame black); `run-look-review.sh` now
prints `SHADER RUNTIME ERROR` from Player.log. Capture: `-looksky 1` (look review) shoots `<pose>_sky_{noon,golden_away,golden_toward,
sunset_toward,dusk_toward}` and `_old` in the same frame state. Tests: `SkyLookTests` (flags bound, old formula is the off path, no disc
in the haze, rim curve). Stills: `Screenshots/121/{high,walk_open}_sky_sheet.png`.

**Task 122 -- biome border blend + per-biome tint (VISUAL_REVIEW_114 #5, owner Q6 (a), 2026-10-22; terrain generator, no timing).**
ON by default on map v4; `-biomeblend 0` = the old hard borders (`TerrainSpec.biomeBlendOff`, read through `BiomeBlend`; v3 ignores
it and keeps its bytes), `-biometint 0` = no tint. GENERATOR (`ShapeField.SurfaceBiome`): a column's SURFACE, SOIL, beach rule AND rock/strata use
`ColumnSample.surfBiome`, the biome at a jittered point p + J(p): two warp octaves (800 m / 150 m, +-60 / +-15 m) bend the line, a clump
octave (40 m, +-18 m) and a per-0.8 m-cell hash dither (+-14 m) mix both sides. (The first in-game stills showed steep slopes still
changing ROCK along the straight line, so rock follows too.) The BIOME itself (ores, pocket liquids, crystals, danger, the world
record, the biome x landform maps) is unchanged. Early-out: `BiomeAndBorder`
gives the climate-space distance to the nearest bisector; above `BlendClimateReach` (|J|max x the climate's Lipschitz bound, with
`SNOISE_GRAD_BOUND` 8, measured max 7.3) the biome cannot change and the 8 noise calls are skipped (exact; but ~90% of columns are
inside the reach, so the saving is small: ~+8 snoise per surface Sample). Height-only neighbour samples pass `Sample(.., false)`.
The far band and minimap use `TopMaterial`, so they show the blend. RENDERER (`Raymarch.compute` `BiomeTint`, in `LookAlbedo`, so
voxels, shells and the far band agree): each surface material drifts between two tints of its own (grass dry/lush, sand warm/pale,
snow white/blue, savanna golden/olive, clay pink/grey; packed sand/ice follow sand/snow) on a 2-D value noise of ~100 m and ~400 m
cells, +-4% value. Capture: `-lookbiome 1` (look review) adds poses `border_0..2` (land border points within 2 km, one per biome
pair, looking along the border from 150 m back, 35 m up) and `<pose>_<fsr3>_notint` at every pose. Tests: `BiomeBlendTests`
(gradient bound, early-out exact + record biome unchanged, band shape on 64 E-W transects, off/determinism);
`Report_BiomeBlendMapPng` (REPORTS=1) writes `Screenshots/122/biome_{map,crop_1km,crop_200m}_before_after.png`.

**Task 123 -- eviction slicing (perf, supervisor approval of 115 Q73 (a); OFF by default, `-evictslice 32` = on).** On a chunk
crossing `EvictOutOfRange` evicted the whole out-of-range slab in one frame (up to ~1,600 chunks). With `-evictslice N` (N > 0;
`StreamManager.EvictSlicePerFrame`, the value tested is `EngineConfig.EVICT_SLICE_PER_FRAME` = 32) out-of-range chunks still INSIDE
the store window are queued (FIFO + dedupe set) and `DrainEvictQueue` evicts at most N per frame inside the slide timer. Only
in-window chunks wait: an in-window coord owns its ring and cascade-tier slot, so a queued chunk cannot alias; out-of-window ones
(teleports, or queued ones the window slid past) still go at once under task 26's save cap. A queued chunk back in range is kept
(`EvictQueueKeptTotal`); under pool pressure the queue goes first, before an admission's transfer and before §3.6's LRU valve
(`EvictQueueFlushedTotal`); `WaitForIdle` flushes it; `EvictionDeferred` also reads the queue. The save cap is now per frame
(`_savesThisFrame`, shared by both paths). Tests: `EvictionSlicingTests` (slice cap + all resident in window, slice 0 = old slab,
re-entry kept, pool pressure takes the queue, not in-range chunks). Warm A/B (`MODE=walk` 3 min, ABBA, 115's flags): slide > 5 ms
frames 15/15 -> 0/0 per walk, slide max 16-17 -> 2.3 ms, but crossing frames stay 54-60 ms (their excess is GPU wait, `pre_ms`
+10 ms) and p99 97/103 -> 108/108 ms, > 100 ms frames 26/49 -> 52/62 (not on the drain frames) -- so it stays OFF.
`tools/crossing-report.py` (crossing frames, eviction bursts, slide; `SKIP=N`); reports `Screenshots/123/crossing_report.txt`.

**Task 124 (perf trial, owner Q67 / Q75b (a); PERF_30FPS_PLAN row 5 (b); NO default change): `-shadowreuse 8` saves nothing measurable.**
Temporal sun-shadow reuse (`-tshadow`, ON on the preset path) now takes a period: `RaymarchFeature.ShadowReusePeriod`
(`-shadowreuse N`, 1/2/4/8/16, anything else -> 4 = the old 2x2 rotation), passed as `_ShadowReuseMask`; the shader's tile `turn` adds
a checkerboard of 2x2 tile blocks (bit 2) and the block row (bit 3), so mask 3 is byte-for-byte the old test. There is NO sun-change
or edit invalidation of the history (only a resize resets it): a stale value lives until its tile's turn, i.e. up to N-1 frames.
Warm steady state (warm-up `Logs/tg2world_20261023_065810.txt`), cost split F X X F X X F, arms reuse8: noon `Logs/costsplit_2026-10-23_070419.txt`
saved +0.2 / +0.3 ms walk_near, -0.3 / +0.2 walk_open (noise 0.1-1.3); golden 14 deg `Logs/costsplit_2026-10-23_071326.txt` -1.1 / -0.2 near,
+1.6 / -0.6 open (noise 0.3-7.2) -- although voxel sun rays traced halve at walk_near (126,584 -> 65,545 / frame at noon). Look
(`-lookreuse N` with `-lookgolden 1`, look review): still frames 0 px vs trace-every-frame (`_reuse1`); a 120 s-day sun (10x play's)
leaves 0.12% of px lagging at 8 (0 at 4) as a staircase of 8x8-px tiles on a moving shadow edge; after an edit, 8 lags frames 1-5
(<= 1,007 px), 4 only frame 1. `tools/reuse-diff.py <dir>` diffs `*_reuse<N>*` vs `_reuse1`. Cost-split arm `reuseN`; the `[sun]` line
shows the period. Tests: `ShadowReuseTests` (flag bound, shader turn = mirror, every tile once per period). Driver `Screenshots/124/124-driver.sh`.

**Task 125 -- the hard sea/sky line (VISUAL_REVIEW_114, task 121's open item; LOOK, no timing). It is the FAR BAND's square, not the
world's edge; fixed, ON by default.** Verified with `-lookedge 1` (look review): at the high pose (606 m over the sea, pitch -4) the line
sat at row 575 of 1080 looking +x, -x and +z, where a flat sea ends at the band's square (FAR_USE_RADIUS * (8 << 4) voxels = 5,632 m
Chebyshev), not at rows 604-621 where the world's edge is (3.9-4.3 km); along the diagonal it moves to row 546 (square: 546, world
edge: 571) and draws the square's CORNER; from `edge_x` (2 km from the world's +x edge) it is still at row 575, nothing at row 753.
Past the world's edge the far band keeps drawing sea. Cause of the line surviving task 121's fade-to-full: the far (and voxel) water's
see-through blend (`SeeCompose` with `SeeReflection`, the sky reflected ~6 deg UP: bluer, darker) and the grade's saturation come AFTER
the haze. Fix (`RaymarchFeature.EdgeFade`, `-edgefade 0` = task 121's fade inside the haze; needs `-skyhaze`; `_SkyLook` bit 2): the
same fade (`DrawnEdgeAmount`, last 45% of the square) moves out of `FarShadeApplyHaze` to the pixel's LAST step (`DrawnEdgeFade`,
far-shade kernel, after both see-through blends, not inside a fluid), toward exactly what a miss shows there (`LookShoulder(LookAtmosphere)`,
no disc), blended in the look's gamma space. Step across the line (green levels, 3 rows, edge columns): 19 -> <= 3 at noon, 11 -> 2
golden away, 7 -> 1.5 toward the sun (`Screenshots/125/line_step.txt`). Capture: `-lookedge 1` adds pose `edge_x` and
`<pose>_edge_{px,nx,pz,diag,golden_away,golden_toward}` + `_old`, logging the rows where a flat sea would end at the world's edge and at
the square. Tests: `DrawnEdgeFadeTests` (flag bound, off path = task 121, the fade after every see-through blend, the capture's square
= the shader's).

**Task 126 -- dusk calmer red (owner Q77, 2026-10-23: dusk "apocalyptic red", sunset "a little too red"; LOOK, no timing).** ON by
default: `-skyrimscale F` (`RaymarchFeature.SkyRimScale`, default `SkyRimScaleDefault` 0.5, clamped 0..1; `-skyrimscale 1` = task 121's
rim). `SunCycle.RimKeepAt(el, F)` = the share of the rim kept: 1 from 6 deg of sun up (golden hour 14.6 deg and noon are untouched), F at
-4 deg and below, 1 - 0.35 (1 - F) at sunset (2 deg). `LookSkyRimAt(hours, scale)` multiplies the rim strength by it and, once the sun is
down, turns the red toward a violet-brown (none at 2 deg, so sunset stays orange); `LookSkyRimShapeAt` -> `_LookSkyRimShape`: x the rim's
weight away from the sun (121: 0.4, now 0.4 k), y the power of the halo floor's lobe (121: 8, up to 24: a tight glow round the sun, not a
wash over the frame). `LookAtmosphere` now takes the floor as max(lerp(sky, halo, pow8), lerp(sky, rim, pow y)) -- equal to 121's
lerp(sky, max(halo, rim), pow8) at y = 8. At F 0.5, dusk (-3 deg): strength 0.87 -> 0.45, lobe 15.8, away 0.21; sky rows 0-449 mean RGB
(70,29,27) -> (27,13,26) toward the sun, (30,15,27) -> (11,9,27) away; sunset 2 deg: strength 1 -> 0.82, (152,86,63) -> (118,69,57).
Capture: `-looksky 1 -lookrim 1` makes the sky A/B's second arm `<pose>_sky_<t>_rim121` (F = 1) instead of `_old`, and every `-looksky`
run now also shoots `dusk_away`; `tools/rim126-sheet.py <dir>` builds OLD 121 | NEW 126 sheets + px diffs. Tests: `SkyRimCalmTests`.
Stills: `Screenshots/126/{high,walk_open}_rim_sheet.png`.

**Task 127 (capture-only, owner Q75 "both look flat" / Q78 "birds eye view?"; NO default change).** `-lookreview -lookcross 1`
(`PlaygroundCapture.LookCross.cs`) replaces the look review's poses/arms with cross-lit stills: one fixed time, the afternoon hour with
the sun `-lookcrosssun` 40 deg up (15.26 h, sun azimuth -74 deg), every heading `-lookcrossoff` 75 (+-15) deg off the sun's azimuth.
Poses (generator-based, identical across launches): `x_high` (high column, -4 deg), `x_ridge` (walk height, ground rising ~1 km ahead),
`x_coast` (walk height, sea 2.5-4.5 km), `x_bird800` (800 m up, 3.3 km ring on the centre row), `x_bird2300` (2300 m, -45 deg), and with
`-lookbiome 1` `xb_0..2` (task 122's border points, `-lookcrossbh` 700 m over them, -60 deg). Shells prefill round the centre for
`-lookcrossprefill` s (240; 0 = off); the log gives the rows where a flat plane is 1.0/2.2/3.3/4.4 km out. Use `-shellworkers 6` (last
match wins): at the script's 3, shells 6-7 (the 3.3 km hand-off) never filled at walk height. Driver `Screenshots/127/127-driver.sh`
(High+borders, Medium, High `-biomeblend 0 -biometint 0` borders); sheets `tools/cross127-sheet.py pair|crop|mark`. Result (HANDOFF 127):
Medium draws a dashed dark line + a flatter blue on the sea past 3.3 km, loses thin rivers on far snow flats and is blockier at lake
shores 1-3 km out; the same near the camera and from 800 m+ up (the ring is in haze). Stills: `Screenshots/127/sheets/`.

**Task 128 (plan only, owner Q80 (a); no code, no launch): `PERF_SHELL_MARCH_PLAN.md`** -- why the shell march costs +16-18 ms when cutting its iterations does not help: in-pose ms track distinct DIRECTORY reads (chunk visits; 99 cut them 26-38 % -> -4.7/-2.6 ms, 93b5/116/117 left them unchanged -> ~0), walk_near costs as much as walk_open with 10x less work (latency exposed: occupancy, H1), pacing ruled out (flat 16.7 ms phase histogram of 115's CSVs); proposes one capture-only probe build (E1 split-kernel probe, E2 cold/warm double march, E3 per-SIMD divergence counters) and restructures R1-R6; owner Q81.

**Task 129 (capture-only, owner 2026-10-23 "build that terrain at 0.1 and look at it from 200 m"; NO default change).** `-lookreview -lodref 1`
(`PlaygroundCapture.LodRef.cs`, driver `Screenshots/129/129-driver.sh [ridge coast plain]`, one launch per scene, ~30 min each) puts a target
point P per scene (generator-based: steepest cross-lit face, a shore with sea behind the camera, the flattest dry land) at D = `-lodrefd`
(50,100,200,400,800,1600,3300,6600 m), depression angle raised until P is visible from every D, task 127's light (sun 40 deg, h 75 deg off it).
Per pose: GAME (as shipped) then REF: `Phase4Bootstrapper.RefTier0Centre` centres ONLY the 0.1 m window (store + GPU mirror; shells and far
band stay on the camera) on P, and `RaymarchFeature.MaxRayDistance` = `-lodrefmaxray` 4096 so tier 0 marches its 204.8 x 204.8 x 89.6 m
box from the entry face (the camera-outside-window path already existed). Arms (`-lodrefarms`): native 1080, `hit` = NEW debug view 14
`HitInfo` (CSMainFarShade only: what drew the pixel + log distance as 6-bit codes, a grey ramp in rows 0-3), fsr540/fsr720 (FSR 3 fixed),
taa (our TAA at native). `tools/lodref129.py <dir>` masks REF to tier-0 pixels minus box-cut walls, occluders and hits within 4 m of the box
floor/sides, and writes per-pose sheets (REF | NATIVE | PLAYED, x4 diffs, tier map, 2x worst window), `<scene>_contact.png`, summary.txt/csv;
REF TAA is the anti-aliased reference. The 50 m pose is the self-check (GAME = tier 0 there: |d luma| 0.3-0.8). Result table:
`Screenshots/129/lodref129_table.txt`; verdict in HANDOFF 129. Found: tier-0 sun-shadow rays never continue into the shells (lit at the window edge
and the 7-layer band's ceiling) -- owner flag, not changed.

**Task 130 (perf probe, owner Q82 yes; PERF_SHELL_MARCH_PLAN E1-E3; capture only, NO default change): the shell march splits ~55 / 45 between the big kernel's context and the march itself.**
New capture-only kernel `CSShellProbe` (Raymarch.compute, VE_FAR_SHADE, 8x8): cost-split arm `shellsplit` (`RaymarchFeature.DebugShellSkip`
2 + 32) skips the in-kernel shell march like `shellmarch_off` and has CSMainFarShade write each pixel's tTier0 (or -1) to `ShellProbeIO`
(4 B/px, allocated only in that arm; a 1-element dummy is bound otherwise), then the probe runs the same `ShellMarch` on the same rays alone;
`shellsplit2` (+ 64) marches twice (second on warm caches, start = a uniform salt of 0). Misses draw sky: timing only. In those arms the
probe writes the march counters 48-59 and a `[simd]` line (E3, ShadowStats 67-76, STATS_LEN 77): per 32-lane SIMD (8x4 half-tile)
sum / (32 x max) of shell iterations, active lanes, step / end kinds. Warm cost split (battery, `Logs/costsplit_2026-10-23_162359.txt`):
in-kernel march 18.2 / ~21 ms, alone 8.1 / 9.5 ms (walk_near / walk_open), second warm march +11.3 / +12.6 ms, SIMD efficiency 0.91 / 0.93.
Plan rule -> R1 (shell pre-pass kernel), no R2; E2 = execution-bound, so R6 for the rest. Report `Screenshots/130/perf130.txt`; owner Q84 in HANDOFF 130.
Run: `SKIP_BUILD=1 EXTRA="-farshade 1 -shells 1 -shellworkers 3 -shellwait 120 -costarms shellmarch_off,shellsplit,shellsplit2" ./run-cost-split.sh`.

**Task 131 (QA pose for owner Q83, capture only, NO fix / default / look change): tier 0 draws a far cliff's shadow as LIT, and it shows in play.**
`-lookreview -cliffshadow 1` (`PlaygroundCapture.CliffShadow.cs`, hook in LookReview): a 10 m generator heightfield of +-`-cliffr` m (2500)
round the start; per afternoon sun elevation in `-cliffsun` (30,25,20) it picks dry, gentle cliff feet whose ground is shadowed by terrain
160-350 m toward the sun and >= 60 m above, with >= 150 m of shadowed ground in view; best `-cliffspots` (2), >= 800 m apart. Per spot, one sun:
A eye 1.6 m looking at the cliff (play look `fsr3_dyn`, `native`, `hit` = debug view 14), C 250 m up looking down with ONLY the 0.1 m window
kept on A's eye (`RefTier0Centre`, MaxRayDistance 4096 as 129's REF; play + hit), B 300 m back (play + hit). Waits `-cliffwait0` 120 / `-cliffwait` 60 s,
`-cliffframes` 90. Also logs, per elevation, the % of dry land shadowed by terrain >= 110 m away. Sheets: `python3 -I tools/cliffshadow131.py <LookReview dir>`
(A | B, C with the window outline, A native; debug view 14 decoded: orange tier 0, blue shells). Run:
`SKIP_BUILD=1 PRESETS=high EXTRA="-cliffshadow 1 -shellworkers 6" ./run-look-review.sh`. Result in HANDOFF 131 / `Screenshots/131/`.

**Task 132 (engine/perf, owner Q85 (a); PERF_SHELL_MARCH_PLAN R1): the SHELL PRE-PASS, exact, ON by default (`-shellprepass 0` = the one-pass path).**
`RaymarchFeature.ShellPrepass`: with the far-shading kernel, shells on and debug view 0, CSMainFarShade runs twice around a small kernel
`CSShellPrepass` (Raymarch.compute header `ShellPre`). Pass 1 (`_ShellPrepass` 1) does tier 0; an 8x8 group in which any lane would run the
primary shell march writes those lanes' ray direction + tTier0 to `ShellPre` and returns, every other group is drawn as before. `CSShellPrepass`
marches the handed rays (the same `ShellMarch` call) and packs the result (t, voxel, mat / end / shell / normal / inside / iters) back. Pass 2
(`_ShellPrepass` 2) runs only the handed groups: handed lanes skip their tier-0 march and take the result, the others re-run tier 0; whole
groups, so the shadow block's 2x2 reuse and the 8.4 guard see the same lanes as in one pass. Scratch 20 B/px (9.9 MiB at 960x540, ~17.6 MiB at
1280x720), allocated only while on (`[shellprepass] scratch` log line). Not with task 130's split arms or `shellmarch_off`. Cost-split arms
`prepass_on` / `prepass_off`; the counters line says `pre-pass ON/OFF (MB scratch)`; the march counters 48-60 / 65-66 come from the pre-pass
kernel (it does not march the 4 rows below a 540-row frame, so its no-hit rays are ~4 x 960 fewer than FULL's). Evidence (AC, warm; warm-up
`Logs/tg2world_20261023_175145.txt`): raw stills ON vs OFF 0 px at walk_near / walk_open; ABBA p50 73.8 -> 69.6 / 48.8 -> 41.8 ms, p99 128 -> 123 /
90 -> 64 (`Logs/costsplit_2026-10-23_175808.txt`); MODE=walk 3 min ABBA p50 12.73 -> 12.46, p99 37.1 -> 33.8, > 33 ms 1.63 -> 1.14%. Run:
`SKIP_BUILD=1 EXTRA="-farshade 1 -shells 1 -shellworkers 3 -shellwait 120 -coststillraw -costarms prepass_off" ./run-cost-split.sh`. Report `Screenshots/132/perf132.txt`.

**Task 133 (engine/perf + bug fix, owner Q83 (a)): NEAR SUN SHADOWS THROUGH THE SHELLS, ON by default (`-nearshadowshells 0` = before).**
`RaymarchFeature.NearShadowShells` -> Raymarch.compute `_NearShadowShells` (far-shading kernel, shells drawn; set only then): a tier-0 sun
ray (TraceShadowRay) that leaves the 0.1 m window or the band ceiling unblocked (`ShadowHit.exitT`, -1 otherwise) goes on through the shells:
`ShellMarch(origin cell centre, sun, tStart = exitT, shadow = true, ..., ShellSkyY(false))`, as a shell hit's sun ray; a shell hit -> sunVis 0, else
the tier-0 value (penumbra) stays. It runs right after TraceShadowRay, before the 2x2 / temporal reuse, so reusing lanes get the same answer. With
it on, the window and ceiling bound the tier-0 part, not `_MaxRayDistance` (128 m, shorter than the window's far corners); Stage F's clamp still
applies; the step cap is not continued. Not shell-hit pixels (they march the shells already), not point lights. Inline in the big kernel, in BOTH
the R1 pre-pass path and the one-pass path: a true hand-over to CSShellPrepass would make pass 2 re-run tier 0 for every group with a lit ray
leaving the window -- most of the tier-0 screen under a low sun. Counters ShadowStats 77-79 (rays handed on / shell iterations / blocked;
STATS_LEN 80), the cost split's `[nearsh]` line; cost arms `nearsh_on` / `nearsh_off`. `-lookreview -cliffshadow 1 -cliffbefore 1` shoots A/C with
the flag forced OFF first (`*_play0`, `*_native0`); `python3 -I tools/nearshadow133.py <LookReview dir>` writes the before/after sheets and the
window-edge numbers. Evidence: cliff1 C window-edge luma 191/147 -> 127/126 (cliff2 118/111 -> 102/102), A shadowed from the feet out
(`Logs/lookreview_20261024_092606.txt`, `Screenshots/133/nearshadow133.txt`); raw stills ON vs OFF 0 px at walk_near / walk_open at noon and at
sun 20 deg (shells filled); warm AC ABBA p50 +1.1 to +1.9 ms at sun 20 deg, noon walk_open +1.1, noon walk_near +6.1 ms (every lit ray climbs from
the band ceiling to the shells' top, 1100 m, in tier-0-chunk leaps through the hole; flagged), MODE=walk sun ~30 deg +1.45 ms p50, > 33 ms
1.63 -> 2.19%; no memory added. Report `Screenshots/133/perf133.txt`.

**Task 134 (engine/perf, owner Q86 (a), Q72 (b); PERF_SHELL_MARCH_PLAN R6): the shell KERNEL DIET, exact, ON by default (`-shelldiet 0` = the code before).**
Raymarch.compute: the play far-shading kernels `CSMainFarShade` / `CSShellPrepass` are compiled lean (`VE_SHELL_LEAN` = `VE_FAR_SHADE` without
`VE_SHELL_DIAG`): `SHADOW_STATS_ON` is `false` (no ShadowStats writes at all), `ShellHit` loses the 93b4-116 fields (leaps0, leapsChunk, leapsBrick,
dirReads, pageReads, maskGroups, colLeaps, colLeapsChunk; `iters` stays 0), no `_DebugShellSkip` arms (shellmarch_off, shellshadow_off,
shellcache_off, shellmask_off, shellleap_new, the 130 shellsplit hand-off), no debug views 12-14, and `SHELL_MASK_ON` / `SHELL_PLANE_LEAP` /
`SHELL_COLLEAP_ON` are `false`, so the air-brick mask (93b5, `-shellmask`), its exit-plane leap (93b5 (b)) and the column sky leap (116,
`-shellcolleap`) are not in the play kernel; and `ShellMarch` asks `ShellByDistance` only when the distance leaves the band of its last answer
(`ShellByDistanceBand`: lo = the largest outer bound before it, no bound order assumed; CPU twin `ShellRayReference.March(bandCache)`,
`ByDistanceBand`, test `ShellMarchCacheTests.BandCacheChangesNoResult`: 2,376 primary + shell-hit shadow + 4,752 near sun rays, every field and
read count equal). Nothing removed: the twins `CSMainFarShadeDiag` / `CSShellPrepassDiag` (and `CSShellProbe`) compile with `VE_SHELL_DIAG` and are
the pre-134 code line for line. `RaymarchFeature.ShellDiagKernels(debugMode)` picks them for a counter window (`CollectShadowStats`), any debug
view, any `DebugShellSkip`, `-shellmask 1`, `-shellcolleap 1`, or `-shelldiet 0`; `ShellDiagDrawnLastFrame` says which ran. CSMain /
CSMainChecker / CSMainDetail are unchanged. Cost-split arms `diet_on` / `diet_off` (the run line ends `kernel lean|diag`); `-lookreview
-cliffshadow 1 -cliffbefore 2` shoots the cliff A/C arms with the diet forced OFF first (`*0`). Evidence (AC, warm; warm-up
`Logs/tg2world_20261024_140957.txt`): raw no-upscaler stills lean vs diag 0 px at walk_near / walk_open (raw and raw2) and cliff1 A native;
ABBA p50 (p99) 69.0 (124) -> 62.1 (110) / 41.8 (60) -> 36.3 (58) ms, -7.1 (noise 2.2) / -5.5 (noise 0.3) (`Logs/costsplit_2026-10-24_141618.txt`);
MODE=walk 3 min (-tod 16) lean 12.14 / p99 35.0 / > 33 ms 1.59% vs diag 12.52 / 36.4 / 1.77% (`Logs/tg2world_20261024_{142334,142820}.txt`).
No memory added. Report `Screenshots/134/perf134.txt`.
Task 135 (check): fast suite on 73b8ce7 PASS 922 FAIL 0 SKIP 15; the cliff2 1 px (260,547) is NOT the band hoist -- lean without it (`TMP134_NOHOIST` temp build) draws the same pixel byte-identically, so it is GPU codegen only; `Screenshots/135/cliff2_pixel_crop.png`, `Logs/lookreview_20261024_165729.txt`.

**Task 136 (QA, capture + diagnosis only, NO fix / default / look change): cliff2's black shape = the VERTICAL SKY PROBE.** `-lookreview -cliffshadow 1
-blackshape N` (`PlaygroundCapture.BlackShape.cs`, hook in CliffShadow): at spot N (2 = cliff2) shoots A as CliffShadow does (play, native, hit = view
14, sun = view 7, nrm = view 3, nosky = native with `UseSkyProbeAmbient` off), noon native/sun, `close` (15 m nearer T = CPU hit of `-bspx` 1780,520),
`side` (~40 m off) and `other` (`-bsother x,y,z`, default cliff2's stone arches, from 28 m on the shadow side); per pose logs CPU ChunkStore probes
(hit voxel, face, rock straight above the face cell, sun ray, inside the window box). Sheet: `python3 -I tools/blackshape136.py <LookReview dir> <out.png>`.
Run: `PRESETS=high EXTRA="-cliffshadow 1 -blackshape 2 -shellworkers 6 -cliffwait0 90 -cliffwait 45" ./run-look-review.sh`. Result: HANDOFF 136,
`Screenshots/136/blackshape136.{png,txt}`, `Logs/lookreview_20261024_181746.txt`.

**Task 137 (engine/perf, owner 2026-10-24 "1 works for now" + generality rule): the shells' HOLE LEAP, exact, OFF by default (`-holeleap 1` = on).**
Raymarch.compute `ShellHoleBox` / `SHELL_HOLELEAP_ON` (`_ShellHoleLeap`, RaymarchFeature.ShellHoleLeap): after a hole leap in `ShellMarch`
(resolve -2, tier 0 not missing), the iterations that start in shell 1's hole box -- its hole chunks inside its published box, cut to the
ray's side of tier 0's layers, below the sky exit when rising, accepted only when the box's farthest corner is inside shell 1's outer bound --
run the same tier-0-chunk leap alone (no distance / resolve / tier-0 tests), one iteration each, so the leaps (bit for bit), the iteration
count and the step cap are unchanged. Every input is a constant the shader already has: no new data, no memory, edits need nothing (the hole is
air to the shells by definition). All kernels (lean and Diag). CPU twin `ShellRayReference.March(holeLeap)` / `HoleBox`; test
`ShellHoleLeapTests.HoleLeapChangesNoResult` (ShellMarchCacheTests' world with tier 0's layers cut low, plus an adversarial 300 m pillar just
outside shell 1's hole; primary, shell-hit and near sun rays, small caps: every field equal). Counters ShadowStats 80-81 (near rays' tier-0
chunk leaps / in the hole box; STATS_LEN 82), the cost split's second `[nearsh]` line; arms `holeleap_on` / `holeleap_off`. Capture:
`-lookreview -cliffshadow 1 -cliffbefore 3 -holepillar 1 -holeleap 1` shoots cliff A with the leap forced OFF first and then the PILLAR pose
(`PlaygroundCapture.HolePillar.cs`: 42 stacked box ops through EditOps.Record, 65 m toward a 50 deg sun, shots native / native0 / play /
nonear = near shadows off as the control); `python3 -I tools/holeleap137.py look <dir>` / `raw <costsplit dir>` diff them. Evidence: raw stills
0 px (walk_near / walk_open, noon and sun 20), cliff1 / cliff2 A native 0 px, pillar 0 px (control 68,392 px); warm AC ABBA ON - OFF p50 +0.3 /
-0.1 ms noon, -0.3 / +0.65 sun 20, all within noise; MODE=walk 11.12 vs 11.14 ms. Why: at noon walk_near the hole is ~1 of 8.0 shell iterations
per near sun ray; ~6 climb uniform-air shell chunks to the shells' GLOBAL top (~1100 m). Report `Screenshots/137/perf137.txt`; the local bound
that would cut those (per-shell ring-column tops, 28 KB, kept in ShellStore.Store) is a new GPU structure: design only,
`Screenshots/137/height_bound_design.txt`, owner's call.

**Task 138 (look, owner Q89 (a) 2026-10-24): the TILTED SKY PROBE, OFF (`-skytilt 1|2`; 0 = the vertical probe).** Raymarch.compute
`SkyProbeDir` / `_SkyProbeTilt` (RaymarchFeature.SkyProbeTilt): 1 = up faces straight up, side faces normalize(up + n) (45 deg out of
the face), down faces horizontally toward the viewer (horizontal part of -rayDir; +x when the view is straight up); 2 = side faces
only. Same ray count, same TraceShadowRay. CPU twin `ShadowRayReference.SkyProbeDir` / `TraceSkyProbe`; `SkyProbeTiltTests`
(directions; ledge undercut opens; arch underside opens toward the viewer and closes along the span; sealed cave dark from every face
and view; open cliff face open both ways; narrow-canyon foot and straight-tunnel ceiling pinned as the rule's trades). Capture:
`-skytiltqa 1` with `-blackshape 2` adds arms tilt / tilt2 to 136's poses and `PlaygroundCapture.SkyTilt.cs` BUILDS (edit ops) a
15 m block with a 12 m tunnel (mouth toward the sun-20 azimuth) and a sealed room under it; sheet + counts
`python3 -I tools/skytilt138.py <LookReview dir> Screenshots/138`; cost-split arms `skytilt_on` / `skytilt2` / `skytilt_off`.
Run: `PRESETS=high EXTRA="-cliffshadow 1 -blackshape 2 -skytiltqa 1 -shellworkers 6 -cliffwait0 90 -cliffwait 45" ./run-look-review.sh`.
Result (`Logs/lookreview_20261024_224144.txt`, `Screenshots/138/skytilt138.txt`): look FAILS -> OFF. Pure-black px cliff2 A 35,538 ->
23,229 (nosky 189), close 210,605 -> 100,425 (nosky 317), arches 93,696 -> 127,295 (WORSE: striped legs), built mouth 62,821 -> 92,957
(ground step faces beside the 15 m block go black: their 45 deg ray meets the block); sealed room / tunnel black both ways. One binary
ray x the multiplied floor makes some face pure black whatever its direction. Cost (warm, AC, noon cost split
`Logs/costsplit_2026-10-24_232016.txt`, `Screenshots/138/perf138.txt`): tilt 1 +1.9 / +2.4 ms p50 walk_near / walk_open, tilt 2 +3.8 / +2.3
(the vertical ray is the cheap one: axis-aligned leaps, early ceiling exit). HANDOFF 138 lists the owner's options.

**Task 139 (look QA, owner Q91 2026-10-25, CAPTURE-ONLY: no default / look change): the dark-overhang side-by-side.**
`-lookreview -cliffshadow 1 -blackshape 2 -darkcompare 1` (`PlaygroundCapture.DarkCompare.cs`; hook in BlackShape after A; task 138's
site + edit ops now shared as `SkyTiltBuild` / `_st`): rows cliff2 ledge + stone arches + built cave mouth at noon / dusk (sun 10),
inside the tunnel + sealed room at noon / night (0.5 h), then ONE torch each (EditService + `LightingDriver.Attach`, needed or the torch
registers no light) at night. Arms: today (preset floor; High = 0.02), `-dcfloors` (default 0.12,0.25,0.40, set on
LightingConfig.Active.undergroundAmbientFloor per shot), nosky; each shot `_play` (FSR 3 dyn) + `_nat` (native 1080). `-dconly torch` =
build + torch rows only; `-dcnatframes` (20). Sheets + counts: `python3 -I tools/darkcompare139.py <dir> Screenshots/139 0.02`.
Run: `PRESETS=high EXTRA="-cliffshadow 1 -blackshape 2 -darkcompare 1 -shellworkers 6 -cliffwait0 90 -cliffwait 45 -cliffframes 40" ./run-look-review.sh`.
Result: HANDOFF 139, `Screenshots/139/`, `Logs/lookreview_20261025_065701.txt` (+ torch re-shoot `Logs/lookreview_20261025_071200.txt`).

**Task 140 (bug, 2026-10-25): "BrickDataPool exhausted" in the stream walk = the CASCADE TIER pools, not tier 0.**
Stack: `CascadeTierPool.WriteChunkFromDownsampled <- SubmitPrecomputed <- StreamManager.DrainCompletions` (cascades exist only with
shells OFF: rigs, F6 shells off). Cause: task 122's biome blend dithers rock/soil/surface per 0.8 m column cell, so every tier-1 (1.6 m) /
tier-2 brick in a blend band is mixed; tier-1 peak 82,725 (`-biomeblend 0`) -> 176,516 vs cap 104,000. Fixed inside the system: a
throw escaped StreamManager.Update, skipping UploadDirty (the only place evicted chunks' cascade slots come back), so the pool stayed
dry; `SubmitPrecomputed(coord, data, store)` now clears pending evicted chunks first when the pool cannot take a chunk
(`PressureReliefsTotal` / `PressureReliefSlotsFreed`); `ExhaustedTotal` + a `[CascadeTierPool] ... EXHAUSTED` warning (first 3, then every
512th) splits held slots into resident / non-resident / unowned. The CAP is the owner's call (HANDOFF 140). Stream probe prints one
`cascade tier N pool:` line per tier. Measurement flags `-tier1cap N` / `-tier2cap N` (Phase4Bootstrapper; default = EngineConfig).
Tests: `CascadeTierPoolPressureTests` (3).
Task 145 (owner Q93 (a)): caps raised by task 53's rule, measured peak x 1.25: tier 1 104,000 -> 221,000 (176,516 x 1.25), tier 2
21,000 -> 41,000 (32,505 x 1.25), in `EngineConfig.Tg2CascadeTierPoolCap`; +140 MB, only with shells OFF (shells ON allocates none).
Walk (`MODE=stream MOTION=walk`, shells OFF, `Logs/tg2world_20261025_092344.txt`): tier 1 peak 176,516 / 221,000, tier 2 32,505 /
41,000, still exhausted 0, Player.log exceptions 2,777 -> 0 (`Logs/overnight/145-after-Player.log`), peak footprint 2,373 MB (140: 2,206).

**Task 142 (plan only, owner Q91 "Soft sky light" 2026-10-25): `SOFT_SKY_PLAN.md`.** No code, no flag, no look change. Recommends
candidate A: one cosine-weighted sky ray per tier-0 hit pixel per frame (replacing the vertical probe), averaged in a per-pixel history
keyed by `ShadowOriginKey`; sealed caves are exactly 0, open ground exactly 1, only partly open faces carry noise. Rejected: B (fixed
directions: banding/stripes), C (0.8 m skylight volume: leaks through 0.1 m player walls, new layout). Steps S1-S5 and owner questions
Q1-Q6 are in the plan.

**Task 143 (look QA, owner Q92 2026-10-25, CAPTURE-ONLY: no default / look change): how far one torch reaches.**
`-lookreview -cliffshadow 1 -blackshape 2 -torchreach 1` (`PlaygroundCapture.TorchReach.cs`, hook in BlackShape before -darkcompare):
138's built block (`SkyTiltBuild`), night (`-nighttod`, 0.5 h), ONE registered torch per row (the others unregistered): tunnel (eye at the
mouth, torch by the -v wall 10.2 m in), sealed room (eye high in one corner, torch in the opposite corner), open ground (torch on a stone
post 6 m in front of the block, eye 12 m back, 3.5 m up). `-trlift N` voxels (default 15 = 1.5 m; 0 = on the floor). Arms set the three
EXISTING C.11 knobs on LightingConfig.Active (pointFalloffK, lightIntensity, lightRangeM; the torch is re-registered per arm because the
range is baked at Register) and restore them: none (light unregistered, the count baseline), today (preset HighContrast 0.85 / 0.90 /
8.5 m), room 0.35 / 1.6 / 8 (LightingConfig's own C.11 defaults), half 0.15 / 2.4 / 14, tunnel 0.06 / 3.0 / 24 (`-trarms "k,I,R;k,I,R;k,I,R"`).
Sheet + counts: `python3 -I tools/torchreach143.py <LookReview dir> Screenshots/143 <launch log>`.
Run: `PRESETS=high EXTRA="-cliffshadow 1 -blackshape 2 -torchreach 1 -shellworkers 6 -cliffwait0 90 -cliffwait 45" ./run-look-review.sh`.
Result: HANDOFF 143, `Screenshots/143/`, `Logs/lookreview_20261025_084941.txt` (floor-placed run `Logs/lookreview_20261025_083830.txt`).

Task 144 (rig refresh audit, plan only): `RIG_REFRESH_PLAN.md` audits all 54 `run-*.sh` (purpose, last report, verdict,
effort): keep 9, update 6, merge 7 into a proposed `run-gameplay-stack.sh` (phase6-combined on tg2 v4), retire 32 to
`tools/retired-rigs/`; proposes `run-perf-milestone.sh` (play defaults on tg2) as the one frame-time rig. No rig changed
or run; follow-ups R1-R6 wait on owner Q1-Q4.

Task 145b (rig: play-path walk, R5 part 1): `MODE=playwalk ./run-tg2world.sh` = MODE=walk's circle (`-sustained -motion walk`,
150 m at 5 m/s, `MINUTES` default 5, `WALKR`) with the PLAY defaults instead of `-renderpreset performance`: `tools/play-flags.sh`'s
`PLAY_WORLD_FLAGS` (`-world tg2 -mapversion ${MAPV:-4} -farband 1 -farshadow 1`, now also used by `run-play.sh`) + `PLAY_SETTINGS_FLAGS`
(PlaySettings' defaults as flags: `-renderpreset quality -upscaler fsr3 -renderrows dyn -shadowlevel 0 -roofskip 0 -shadowres full
-gamemode survival -underwater 1 -seethrough 1 -riverlook 1 -farshade 1 -shells 1 -shellworkers 3 -minimap`; run-play.sh does NOT pass
these, it reads PlaySettings.json) + `-gateres 0 -tod 12` (noon, like play's start) + fullscreen 1920x1080. Fluid tile pool 480 is the
Playground's in every mode. Prints `PLAYWALK: WHOLE RUN p50/p95/p99, >33 ms share | UPSCALER ROWS (share of frames per internal row
count) | exceptions N`. NOT like play (a -sustained run is a capture run): Survival mode, vitals and movement abilities stay off
(`Playground._moveAbilities = IsPlaySettingsRun`) and the camera is scripted, not the motor. Every run-tg2world mode now prints
`Player.log totals: N 'Exception', N 'exhausted'` (task 140: `head -20` hid them); Phase4Bootstrapper logs `[Phase4Bootstrapper] args: ...`
(flag echo) and `rig run: LOD shells ON|OFF`; the sustained header's `upscale` row names the upscaler when one is active (it said
"RenderPreset Quality" under FSR 3). Test `PlaySettingsTests.PlayFlagsFileMatchesPlaySettingsDefaults` pins PLAY_SETTINGS_FLAGS to
PlaySettings / RenderPreset.Default / PlayShellWorkers. Smoke (1 min, not timed, cold): `Logs/tg2world_20261025_100735.txt`.

**Task 147 (look, SOFT_SKY_PLAN S1, owner Q94 plan A, 2026-10-25): the soft-sky CPU ORACLE, no GPU / shader / default change.**
`Assets/CoreEngine/Rendering/SkyVisReference.cs`: skyVis = the cosine-weighted fraction of a face's hemisphere whose hard shadow ray
(`ShadowRayReference.Trace`, kSoft 0) reaches the sky; directions with y <= 0 are GROUND (0, no ray), so open ground = 1, an open
vertical face = 0.5 exactly, a down face = 0 (as today). Direction sequence (S2's contract): `SamplePoint(seed, frame)` = R2 lattice in
0.32 fixed point + a lowbias32 per-seed rotation, 23-bit + half-step u/v (integer-exact; pinned values in the tests), `CosineDir` (sqrt /
sincos, ulp-close on GPU), seed = `PixelSeed(px)`. `History.Accumulate(key, sample)`: 1/n for 16 samples then 1/16, key =
`OriginKey(voxel + normal)` (twin of ShadowOriginKey), a new key restarts. `Quadrature` = 1,024 midpoint directions (the truth).
Tests `SkyVisReferenceTests` (11; shapes built with Box EditOps): sealed room and 0.1 m shell room exactly 0 for every face / frame /
seed; open ground and a 1-voxel pillar top exactly 1; 0.1 m wall = 2 m wall exactly; 3 m ledge floor / wall in (0.1, 0.6); 2 m hole in a
10 m room (floor below 0.063, corners < 0.05); task 138's site (tunnel darkens inward); history. The running mean matches the quadrature
as RMS over 32 pixels <= 0.05 (0.040-0.047), NOT per pixel (single pixels miss by up to ~0.1). `REPORTS=1` writes
`Screenshots/147/softsky_quadrature147.txt` (`Report_QuadratureTable`, Slow). Next: S2 (`-softsky 1`, OFF).

**Task 148 (rig, RIG_REFRESH_PLAN R2, 2026-10-25): the kept/updated rigs take the shared Unity lock themselves.** After
`set -uo pipefail`, nine runners now `source tools/unity-lock.sh` + `unity_lock_acquire` (3 lines each, nothing else changed):
the 5 kept gates `run-editmode-tests.sh`, `run-phase7-rig.sh`, `run-acceptance-rig.sh`, `run-acceptance-fluid.sh`,
`run-fluid-tiled.sh` and the 4 "update" rigs `run-phase5b-rig.sh`, `run-phase5d-rig.sh`, `run-phase6-brushguard.sh`,
`run-phase6-combined.sh`. Wrapping them in `tools/with-unity-lock.sh` still works (the inner acquire sees the outer holder as
an ancestor and returns). The 20 lockless rigs marked retire/merge were left alone (R1/R3 move them). Evidence: `bash -n` on all
nine; `./run-editmode-tests.sh` unwrapped took and released the lock, PASS 946 FAIL 0 SKIP 15 (`Logs/overnight/148-editmode.out`).

**Task 149 (rig, RIG_REFRESH_PLAN R1, 2026-10-25): one-off rigs retired to `tools/retired-rigs/`.** 31 of the plan's 32
"retire" scripts were `git mv`ed (not deleted) into `tools/retired-rigs/`, whose `README.md` has one row each (why retired,
last report). They still run from the repo root as `tools/with-unity-lock.sh tools/retired-rigs/run-x.sh` (none uses
`dirname "$0"`; none takes the lock itself). Left in place: `run-playground.sh` (`tools/capture-gpu-trace.sh` prints it as the
rebuild command). Checked with `grep -w` over the 22 remaining `run-*.sh`, `tools/`, `Assets/` and CLAUDE.md: no other call
site; mentions left in KNOWN_ISSUES.md / completion docs are history, and `run-play.sh`'s "like run-demo.sh" is a comment.
Root now has 23 `run-*.sh`.

**Task 150 (perf, diagnosis only): the +3.7 ms playwalk regression, isolated.** Rig knob only: `NOMINIMAP=1 MODE=playwalk ./run-tg2world.sh` drops play's bare `-minimap` flag (it cannot be switched off through EXTRA; ArgValue/last-match does not apply to a bare flag). Result (warm, ABBA, `Logs/overnight/150.out`): `-nearshadowshells 0` saves ~1.5 ms over the walk (position-matched 0.5-3.5 min means) and ~2.1 ms on the 1.0-2.0 min leg where the regression vs 2026-10-22 lives (about half of it there); `-underwater 0` + no minimap ~0.3-0.9 ms (noise); shell held counts in today's base runs match 10-22's, so not cause (2). Whole-run p50 of the identical base arm spread 42.4-47.2 ms over four runs on one build: use per-half-minute window means for A/Bs under ~3 ms. No default changed.

**Task 151 (perf, owner Q95): torch reach vs cost; SHIPPED the "room" arm.** HighContrast preset (play's) torch knobs k 0.85 / I 0.90 / 8.5 m -> **0.35 / 1.60 / 8 m**
(model reach 1.2 -> 2.9 m; `LightingPresets.HighContrast`, and the same three fields in the owner's `LightingConfig.json` + `LightingConfig_HighContrast.json`,
backups `*.bak151` beside them: play reads the FILE, not the code). Harness: `-costsplit -costpaths torch` (`PlaygroundCapture.TorchCost.cs`, hooks in CostSplit):
night (`-nighttod`), 32 torches on 1.5 m posts (16 in an 8 m ring 12 m ahead, 16 in a `-tlouter` m ring round the eye, default 40), still eye, yaw +-30 deg;
arms `tl_K_I_R` set the three C.11 knobs and re-register the torches, `tl_none` unregisters them; then lava stills `lava_<arm>.png` (3 x 3 m slab behind the eye).
Lit px: `python3 -I tools/torchcost151.py <run>/stills [sheet.png]`. Run: `SKIP_BUILD=1 EXTRA="$PLAY_SETTINGS_FLAGS -renderrows 540 -shellwait 90 -costtimed 15 -costpaths torch -costarms tl_none,tl_0.35_1.6_8,..." ./run-cost-split.sh`.
Result (`Screenshots/151/torchcost151.txt`): per-pixel cost follows the RANGE only (k / I change no shader work); every range > 8.5 m was over the +1.0 ms budget
(14 m +2.6..+24, 10 m +3.7, 24 m +15..+23 ms); today's torches themselves cost +5.7 / +8.3 ms vs none (22 / 31 torches). Lava's aggregate reads the same knobs:
the room arm looks unchanged, wider arms add a red wash.

**Task 152 (perf, owner Q97 (a); PERF_SHELL_MARCH_PLAN R2): ray compaction NOT built -- decision-table row A (SIMD efficiency >= 0.6 at both poses).**
No code, no launch. Task 130's E3 counters (`Screenshots/130/perf130.txt`): per 32-lane SIMD sum / (32 x max) of primary shell iterations 0.906
(walk_near) / 0.932 (walk_open); 95% / 99% of SIMDs >= 0.6, 4% / 1% < 0.35; active lanes 30.6 / 31.7 of 32; one end kind per SIMD (1.01 of 5).
They still describe today's kernel: R1's `CSShellPrepass` uses the same 8x8 pixel layout as the probe (tid -> pixel, unflagged lanes return) and
the same `ShellMarch` call; R6 changes no iteration or read count (`ShellMarchCacheTests.BandCacheChangesNoResult`); 133 adds shadow rays only;
137 / 138 are OFF. Task 146's counters on today's build (`Logs/costsplit_2026-10-25_115751.txt`) give the same per-ray means as 130's mean lanes:
83.6 iters/ray walk_near, 38.8 walk_open. So no re-run (every outcome led to the same action). Ceiling: a perfect, free compaction recovers at
most (1 - eff) of the shells path: < 0.83 ms walk_near (of 8.8, which also holds 133's near sun rays) and < 0.37 ms walk_open (of 5.5), under
the >= 1 ms keep bar before the append / indirect-dispatch overhead. Next cut: task 153 (column height bound).

**Task 153 (perf, owner Q97 (a)): the shells' COLUMN HEIGHT BOUND, exact, OFF by default (`-shellheightbound 1` = on) -- gain < 1 ms.**
`ShellStore` keeps, per shell and RING COLUMN, the height above which every chunk of that column in the published box (outside the hole) is
held and all air (header COLUMN HEIGHTS; an unheld chunk counts as solid to its top; `HEIGHT_NONE` outside the box). It is rebuilt from `_dir`
in `FlushDirectory` and uploaded in the same call as the directory, so the GPU's table always matches the GPU's directory and box. Edit ops,
edit summaries and streaming all come through `Store` / `Evict`, so they raise or lower it the frame they are published. The table is the tail
of `ShellPages` (`Gpu.HeightBase`, per-store offset in `_ShellBounds[k].w`; <= 28 KB). Raymarch.compute `SHELL_HEIGHTBOUND_ON` in `ShellMarch`:
a rising SHADOW ray in an air cell of shell k's own chunk, at or above its column's height, moving away from the camera, crosses the column
up to a chunk-aligned top (the published top, the hole bottom when below it, the chunk where it reaches the outer band less 3) in ONE leap.
That leap is accepted only if the ray leaves the box's lower part (the last chunk's bottom plane or the column sides, whichever comes first)
nearer than the outer bound less 2. CPU twin `ShellRayReference.March(heightBound)` / `HeightLeapBox`; tests `ShellHeightBoundTests`
(a pillar and an edit-op tower redraw must raise the bound and still block rays; a lying table must leak). Cost-split arms
`heightbound_on` / `heightbound_off`; look review `-cliffbefore 4` (with `-holepillar 1 -shellheightbound 1`); diff `tools/heightbound153.py`.
Evidence (warm, noon, `Logs/costsplit_2026-10-30_130145.txt`): near sun ray shell iterations 8.0 -> 5.0 per ray at walk_near (-37%), no frame
gain (ON - OFF +1.5 ms p50 / +0.8 pass means, FULL spread 1.1; walk_open 0.0). Raw stills 0 px (4 pairs), edit-op pillar 0 px (control 68,392 px).
cliff2 A native differs by 10,597 px while shell 3 was still filling: not isolated, so re-check before turning it on. Report `Screenshots/153/perf153.txt`.

**Task 154 (cleanup, owner 2026-10-25: "remove the bloat"; no code/shader/test/rig-behaviour change):** CLAUDE.md 30,781 -> 24,621 B (rule
sections word for word; the dated "Current state" paragraphs moved verbatim to `PROJECT_HISTORY.md`, newest first; the known-issues index is
one short line per entry, the 10 old long index lines kept verbatim at the end of `KNOWN_ISSUES.md`). The ~18 KB target is not reachable
without editing rule sections, which the task forbade (they total ~20 KB). Root .md 54 -> 41: 14 finished/one-off reports `git mv`'d to
`docs/archive/` (README there: what each was, why archived; live references updated). `run-playground.sh` retired to `tools/retired-rigs/`
(`tools/capture-gpu-trace.sh`'s rebuild hint now names `BUILD=1 ./run-play.sh` + the trace build). Answered/superseded `blocked/` and
done/replaced `later/` items moved to `tools/overnight/archive_work/{blocked,later}-2026-10-30/`. `.gitignore`: `__pycache__/`, `*.pyc`,
`TestResults*.xml`.

**Task 155 (plan, owner Q99 (a) 2026-10-30): cheaper 10-15 m torch light -- PLAN ONLY, no code, no default change.** `TORCH_LIGHT_PLAN.md`:
A = a light flood exact at 0.1 m (6-connected, no corner cutting, chamfer distance, per-channel max RGB), sparse per brick, sampled once per
pixel (the spec's §3.8/§6.5 v1.5 "sparse BFS light pool"; RECOMMENDED); B = per-brick light lists + 1 temporally reused torch ray per pixel;
C = a 0.8 m brick flood (leaks through 0.1 m walls; later as the far/shell light). Step list L1-L7 (L1 = the CPU oracle `LightFloodReference` + tests).
Six owner questions in the 155 HANDOFF (round corners vs crisp shadows, reach, torches per base, colour, fill-in delay, light beyond the window).

**Task 156 (look, PERF_30FPS_PLAN step 3 / owner Q1, CAPTURE-ONLY: no default / look change): FSR 3 stills at 540 vs 432 internal rows.**
`UpscalerSelect.CaptureMinRows` (360): a PINNED `-renderrows N` / `Apply(k, N, false)` now clamps to 360-720 (was 540-720); play cannot reach
it (PlaySettings passes only 540 / 720 / dyn; the dyn controller still moves in 540-720). `-lookreview -lookrowsab N`
(`PlaygroundCapture.RowsLook.cs`, hook in LookReview): every LookReview pose shoots `<pose>_fsr3_540` and `<pose>_fsr3_N` in the same frame state
(no none/native arm; the shell tint still), then `cliff` (the nearest sun-behind cliff foot of `FindCliffFeet`) and `torch` (task 151's 32-torch
scene, `-nighttod` 0.5 h). Run: `PRESETS=high EXTRA="-lookrowsab 432 -lookposes walk_near,walk_open,high,cliff,torch -lookbursts 0 -lookwait 60
-shellworkers 6" ./run-look-review.sh`; sheets `python3 -I tools/rows156.py <LookReview dir> Screenshots/156 [N]` (per pose LEFT 540 / RIGHT N,
plus 3x crops of each pose's 3 most-different 160x90 windows). Result: HANDOFF 156, `Screenshots/156/`, `Logs/lookreview_20261030_154829.txt`.

**Task 157 (TORCH_LIGHT_PLAN step L1, owner "a to all" on 155, CPU ONLY: no GPU / shader / default / look change): the torch light-flood oracle.**
`Assets/CoreEngine/Rendering/LightFloodReference.cs` (global namespace, like SkyVisReference): light in AIR voxels (resident, material 0; every other
material blocks, as today's torch rays); 26 moves with NO corner cutting (an edge move needs both face voxels it cuts past open, a corner move all 6),
so reachability == 6-connected air; chamfer 4 / 6 / 7 read at 4.2913 units per 0.1 m (centres the error band: +-6.8% of Euclid; 1/1.41/1.73 would be
0..+12.8%); level_c = max over torches (seed_c - path cost), 10-bit per channel packed r | g<<10 | b<<20 (4 B per voxel); 12 m = 515 units
(`ReachLevel`), 10 bits reach 23.8 m. `SeedFor(r,g,b)`: brightest channel reaches the full reach, a channel at fraction f starts log2(f) stops lower
(`STOPS_AT_REACH` 8 over the reach: L1 default, L2 may revisit). Storage sparse per brick (16 KB slot table per lit chunk, 2 KB per lit brick, a brick
allocated iff non-zero). `AddTorch` = one Burst SSSP (Dial's buckets, `FloodJob`) over the torch's brick-aligned box + max-merge; `RemoveTorch`,
`OnVoxelsChanged(changed)` (after ChunkStore writes), `OnChunkEvicted` / `OnChunkAdmitted` (after the store change) = darken (zero every voxel whose
level equals a darkened neighbour's old level - move cost, or came through a move that is invalid now) + refill (relax from every lit voxel / torch
next to a zeroed one). Torch voxels must be solid while registered. `FromScratch`, `Diff` (byte for byte incl. allocations). Tests
`LightFloodReferenceTests` (plan tests 1-6): sealed axis / staircase / x==z edge-touching / x+y+z plane / closed box (built before AND after the torch)
exactly 0; doorway open -> sealed; open air flood/Euclid 0.932..1.068 at 12 m; L-bend lit round the corner at 0.956..1.016 of the geodesic; fuzz 80
ops (fast) / 2 x 300 (`Fuzz_Long`, Slow) incremental == scratch after every op, then every slot freed; evict / edit / re-admit == scratch. Report
(`REPORTS=1`, Slow, ~2.5 min): `Screenshots/157/torchflood157.txt` + `slice157_{tunnel138,sealedroom,lbend,diagonalwall,outdoor}.png` (top-down at the
torch height: grey solid, black unlit air, light = level / seed per channel, dark contour every 1 m of path). Result: HANDOFF 157.

**Task 158 (TORCH_LIGHT_PLAN step L2, owner "a to all" on 155; OFF by default): the torch-flood GPU light pool, `-torchflood 1`.**
`TorchFloodPool.cs` (VoxelEngine.Rendering) + `TorchFlood.hlsl` + `Resources/TorchFloodProbe.compute`. CPU sole writer: each frame (LightingDriver.Update)
`Tick` refills FROM SCRATCH when stale -- the registered non-mobile light set changed (`LightSourceBuffer.Revision`, then a sorted signature), a chunk in
any torch's box was admitted / evicted (Chunk refs remembered), or an EditService write landed in one (`NotifyVoxelChanged`, beside
NotifyLightingEdited) -- by flooding every torch through task 157's `LightFloodReference`, `Encode` and three uploads. Layout (157's decision):
dense dir over the lit-chunk AABB -> 4,096-entry brick table per lit chunk -> data; MIXED brick = 4^3 cells at 0.2 m, value = per-channel max (nearest
cell, never blended across a wall), a cell whose air splits into >= 2 face-connected groups is an ESCAPE (bit 30) to 8 per-voxel values; pure-AIR brick
(uniform-air header) = 2^3 cells at 0.4 m holding the mean of the 2^3 voxels round each centre, trilinear. `SampleLevels` = the HLSL's C# twin.
Shader: kernels `CSMainFlood` / `CSMainFarShadeFlood` (VE_TORCH_FLOOD; every task-158 line is under that #ifdef, so CSMain / CSMainFarShade compile from
the pre-task source -- the stripped file == HEAD's byte for byte), picked by RaymarchFeature while `TorchFloodPool.Ready`; tier-0 hits that are not self-lit
take `FloodLight(air cell, normal)` + the LightBuffer entries from `_FloodLightFirst` on (lava's aggregates stay analytic); shell hits keep the full analytic
loop (plan Q6 (a)). Level -> light: d = (top - max level) / 42.9 m of PATH, I / (1 + k d^2) x (1 - (d/R)^4)^2, colour = exp2(-8 (max - L_c) / top), x a
DIRECTION term wrap + (1 - wrap) x saturate((level(v + 2n) - level(v)) / 8). Defaults I 2.0, k 0.10, reach 12 m, wrap 0.3 (`-tfi -tfk -tfreach -tfwrap`).
Known gaps (L3 / L4): fluid writes do not refill; no pool cap; refills are synchronous (~0.25 s per outdoor torch, 157's flood). Tests `TorchFloodPoolTests`
(9): sealed axis / x==z diagonal (escapes) / x+y+z plane / closed box stay exactly 0 through the encoding; mixed cells never darker, <= 7 levels brighter;
air bricks <= 12 levels; split-cell table; Raymarch flood code only under the #ifdef. Capture: `-lookreview -cliffshadow 1 -blackshape 2 -torchreach 1
-trflood 1 [-trfloodb I,k,wrap]` adds the flood column(s), a GPU == oracle probe at 16 x 9 pixels per row (`probe (...)` lines), an OFF re-shoot
(`tr_ground_today_nat_off2`) and a pillar row; sheet `python3 -I tools/torchflood158.py <dir> Screenshots/158 <log>`. Cost arm `tf_on` (`-costpaths torch`,
`PlaygroundCapture.TorchCost.cs`): the launch's torches with the flood on, built once before the first timed window. Result: HANDOFF 158.

**Task 159 (perf, PERF_30FPS_PLAN step 3, owner Q100 (a) 2026-10-31; PLAY DEFAULT CHANGED): FSR 3 dyn 432-540 rows at a 30 ms target.**
`UpscalerSelect.UseDynRange(floor)`: floor >= 540 = the old controller exactly (540-720, target 16.7 ms, dead band 15-18 ms); floor 360-539 = floor-540
at `PlayDynTargetMs` 30 (dead band 27-33 ms, +-10%; one 36-row step is ~13% of the pixels), starting at 540. `Playground.Start` picks it before any
`Apply`: a PlaySettings (interactive) run defaults to `PlayDynFloor` 432, every other run (captures, rigs, -fluidprobe) to 540 = unchanged;
`-dynfloor N` overrides either (`-dynfloor 540` = the old play behaviour). `tools/play-flags.sh` passes `-dynfloor 432` so MODE=playwalk follows play
(pinned by `PlaySettingsTests.PlayFlagsFileMatchesPlaySettingsDefaults`). Pinned `-renderrows N` / task 156's `CaptureMinRows` unchanged; FSR 3's max
render size stays 720. F6: the rows row reads "Dynamic 432-540 (now N)", its hint names the target. `Describe()` / the `[UpscalerSelect]` line print the
dyn range + target. Rig output: `sustained.tsv` gained a 7th column `meanRows` (the window's mean internal rows). Capture: `-lookreview -lookrowsab N`
with bursts on (default) now also shoots walk_open's still / walk / turn bursts at 540 and N (`walk_open_fsr3_<rows>_<motion>_0..3.png`; diff with
`tools/look-review-diff.py bursts`). Run: `PRESETS=high EXTRA="-lookrowsab 432 -lookposes walk_open -lookwait 60 -shellworkers 6" ./run-look-review.sh`.
Result: HANDOFF 159 (playwalk p50 47.4 -> 32.7 ms on battery ABBA; rows at 432 on 81-89% of walk frames; no new shimmer in the bursts).

**Task 160 (plan only, owner 2026-10-31 "all of those options are bad picture quality"; no code, no default / look change): `PERF_EXACT_CUTS.md`.**
Exact-only (0 px) cuts on top of 159, from existing logs (146 cost split, 115 spikes, 123, 124, 130, 133, 150, 152, 153, 59). No launch: the Mac was
on battery at 15 %, so the one allowed warm launch moved into cut B's task (its capture-only `skyprobe_off` arm + the cost split's 432-row clamp were
written and reverted). Pass split at 540 (measured) and 432 (est.): sun shadows 17.2 / 5.5 ms (near / open), shells 8.8 / 5.5, FSR 3 5.7 / 5.4 (fixed
at 1080p output), CPU floor 7.1 / 4.4, residual (tier-0 march + shading + the never-timed per-pixel sky probe) ~22 / ~13. Finding: iteration cuts
paid ~0 (116 / 93b5 / 153 / 137 / 124), and structural cuts paid 4-7 ms (132 R1, 134 R6), so the candidates are structural. Ranked: B kernel diet 2 (OFF-in-play paths
compiled out; est. 1-4 ms near at 432) > C1 shadow continuation out of the big kernel (2-4; ~3-6 MB scratch, owner) > D incremental roof pyramid
+ roof skip ON (0-3) > C2 deferred sun pass (3-7, design first) > E upload pacing (p99 -10..-20) > I FSR 3 output copy (0.2-0.5). Honest sum:
p50 <= 30 plausible; <= 5 % > 33 ms NOT reachable with exact cuts alone (est. p95 ~45-55; cliff views ~36-44 vs 33). A/Bs must pin
`-renderrows 432`, because the dyn controller spends cuts on rows. Owner questions: HANDOFF 160.

**Task 161 (perf, `PERF_EXACT_CUTS.md` cut B, supervisor 2026-10-31): kernel diet 2, `-diet2 1|0` (last match; default ON since task 167, `-diet2 0` = task 134's lean kernels).**
`Raymarch.compute` compiles `CSMainFarShadeLean` / `CSShellPrepassLean` (`VE_FAR_SHADE VE_DIET2`): the play kernels with what is OFF in play
folded to `static const` (traversal mode 4, no tier 1-2 cascade, debug view 0 + no `DebugOut` / `isDebugPixel`, no half-res / rung-1 quad shadow
reuse, roof skip / hole leap / height bound / sky tilt 0, `_Look 1`, dev colours 1, and no one-pass primary `ShellMarch` with the pre-pass on).
`CSMainFarShade` / `CSShellPrepass` are untouched twins. `RaymarchFeature` picks the lean pair only when `KernelDiet2Rule.Settings` (every folded
setting at its folded value, flag on, `DebugPixel` never assigned) and the frame allow (task 134's lean kernel picked, no torch flood, no cascade,
pre-pass on or <= 1 shell); `Diet2DrawnLastFrame` reports it. Tests `KernelDiet2Tests` (the shader's folded list vs the C# rule). Cost split:
arms `diet2_on` / `diet2_off`, `skyprobe_off` (vertical sky probe off, capture-only look change), run lines end `kernel diet2|lean|diag`,
`-renderrows` now clamps from `UpscalerSelect.CaptureMinRows` (432 reachable), `-costd2pair` (with `-coststillraw`: raw / raw2 / flip / back
stills). Look review `-cliffbefore 5` = cliff A/C with diet 2 forced OFF first. Identity: `python3 -I tools/diet2_161.py raw|pair|look <dir>`;
release-trace per-encoder table: `python3 -I tools/gpu161.py <trace>.export/gpu.xml`. Result: HANDOFF 161.

**Task 162 (look, owner Q100 note 2026-10-31, CAPTURE-ONLY: no default / look change): native 1080 vs FSR 3 at 720 / 648 / 540 / 432.**
`-lookreview -lookladder native,720,648,540,432` (`LadderArms` in `PlaygroundCapture.RowsLook.cs`; generalises task 156's `-lookrowsab N`, which
is unchanged): every pose (walk_near / walk_open / high, then 156's cliff + night torch) shoots `<pose>_<arm>` for each listed arm in the same
frame state ("native" = no upscaler at 1920x1080, N = `fsr3_N`), and walk_open's still / walk / turn bursts for `-lookladderbursts` (default
`native,540,432`). Run: `PRESETS=high EXTRA="-lookladder native,720,648,540,432 -lookposes walk_near,walk_open,high,cliff,torch -lookwait 60
-shellworkers 6" ./run-look-review.sh`; sheets `python3 -I tools/ladder162.py <LookReview dir> Screenshots/162` (per pose 5 tiles with mean
|dlum| and % px > 8 levels vs native; 3x crops of the 3 windows that differ most from native; motion sheet with 159's flicker numbers).
Result: HANDOFF 162, `Screenshots/162/ladder162.txt`, `Logs/lookreview_20261031_210547.txt`.

**Task 164 (look, owner Q101 part 3 (a); flood stays OFF): torch-flood DIRECTION terms, `-tfdir 0|1|2|3` (default 0 = task 158's).** The direction
factor of `TorchFlood.hlsl FloodLight` is `wrap + (1 - wrap) x ndl`, ndl by `_FloodParams2.w` (`TorchFloodPool.DirMode`): 0 = 158's rise to the next
0.2 m cell / 8 (piecewise constant: the chamfer's 4/6/7 gradient -> the facets); 1 = `FloodGradCos`, the normalised 3-axis gradient by central
differences over 0.4 m (one-sided where a side is dark); 2 = `FloodTorchCos` (Raymarch.compute, under VE_TORCH_FLOOD, hook `VE_FLOOD_TORCH_DIR`): the
Lambert cosine toward the selected torch (LightBuffer[0, _FloodLightFirst)) whose straight distance matches the flood's path (|path - de| <= 8 % + 0.25 m,
weight fading to 0 by twice that as the path bends; mode 1 elsewhere); 3 = flat 0.5. Every mode only scales a nonzero level: sealed spaces stay exactly
dark. C# twins `TorchFloodPool.LightTwin / GradCos / TorchCos`. Tests (`TorchFloodPoolTests`, +2): every mode x 6 normals, the 0.1 m closed box stays
exactly 0; floor beside a 1.5 m post torch, max dir-factor step between 0.1 m neighbours: mode 0 0.525, 1 0.264, 2 0.018, 3 0. Capture: `-trarms none`
(today only), `-trfloodmodes 0,1,2,3` (one flood arm per mode: `flood`, `flooddir<N>`, rows + pillar); sheets `python3 -I tools/torchflood164.py <dir>
Screenshots/164 <log>` (per row a 3x2 grid of 960x540 tiles + a 2x native crop sheet; `CROP_<ROW>=x,y`). Result (164b, `Screenshots/164/`, launch log `Logs/lookreview_20261102_002927.txt`): mode 2 removes the room floor's X wedges, the tunnel end wall's stripe and the blocky ground patches with no visible fade band; mode 1 looks like 0; mode 3 washes every face equally (the pillar's and tunnel's corners stop reading). Recommended mode 2; owner question in HANDOFF 164b. Note: the TUNNEL row's flooddir3 arm encoded 385 air / 326 mixed bricks vs 90 / 621 for the other arms -- a deferred brick compaction finished mid-row (`BrickAllAir` reads the brick header), so that one tile is not like-for-like.

**Task 165 (TORCH_LIGHT_PLAN step L3, owner doc Q101 1 (a) + 2 (a); flood stays OFF, `-tfdir` default unchanged): the INCREMENTAL torch flood.**
`TorchFloodBuild.cs` (new) + `TorchFloodPool.cs` (rewritten around it; sampler twins untouched). Each torch keeps its OWN flood in the GPU's compressed
form as a min path cost per cell (`TorchFloodCache`: ushort per 0.2 m mixed cell / escaped voxel / 0.4 m air cell; "direct" bricks = 1 bit); the union
(the CPU copy of the GPU buffers) = per-channel max over the caches covering a brick, which is EXACT because cells are maxes (L3 changed the air cell
from 158's MEAN of its 2^3 centre voxels to their MAX, and `Encode` reads a brick's kind from its CONTENT, not the header: 164b's caveat). Per frame
(`Tick`, main thread): torch-set diff; torches whose 12 m box an edit touched (EditService on air<->solid changes, `FluidOpListReadback` on air<->fluid,
the edit-op replay via `NotifyBox`) or whose box chunks were admitted/evicted are queued; up to 2 flood jobs (`FloodBrickJob`, one torch per job, Burst,
worker) run on a main-thread `Snapshot` of the box (brick kinds + 1 bit/voxel of mixed bricks, ~0.1-0.3 ms); a finished torch re-merges the bricks it lit
before or lights now (`MergeJob`, worker, batches of 4,096) and `ApplyJob` (Burst on the main thread) writes the union + uploads only dirty table/data
runs. The flood: bricks whose bbox to the torch is all air at voxel precision (27 sign cases x summed-area tables) get d = closed-form 4/6/7 chamfer, no
search; the rest is Dial's buckets on a sparse per-brick scratch seeded from the direct faces. Tests `TorchFloodL3Tests` (8 fast + Slow `Fuzz_Long` +
report `Report_FloodPhases_OpenGround`): scratch == `Encode(oracle)` exactly (axis wall, x==z diagonal with 450 escapes, closed box + L-bend, open
ground 12 m); incremental == scratch after every fuzz op (80 + 600) and every oracle 10th; doorway sealed / diagonal slot / reopened; evict/re-admit;
removing a torch re-floods nobody else; all torches removed frees every brick. Capture: `run-cost-split.sh EXTRA="-costpaths torch -tfl3 1"` (151's camp,
INSTEAD of timed arms): async build, edits D1 wall / D2 pit / D3 break + re-place, after each: live == fresh rebuild (`TorchFloodPool.Diff`), GPU buffers
read back == CPU copy (`VerifyGpu`), 16x9 GPU-vs-twin probe; per-phase main-thread ms. Result (`Screenshots/165/tfl3_165.txt`): exact + GPU PASS at every
step; flood 250 ms/torch on one thread in tg2 (158: 316; open-ground EditMode 62 vs oracle 215); camp 5.9-13.7 s to fill, edits 2.1-2.6 s (each re-floods
10-11 overlapping camp torches); main thread mean 0.59-0.78 ms/busy frame, p99 1.3-2.8, max ~5; memory content 11.2 MB union + 8.1 MB caches (allocated
21.1 + 9.9), job scratch peak 27 MB (freed when idle), GPU 20 MB allocated. Owner questions in HANDOFF 165.

**Task 166 (look, owner Q103 (a) 2026-11-01, CAPTURE-ONLY: play unchanged unless a flag is passed): why FSR 3 softens and draws dark block edges.**
Inputs audited, all correct: motion 0 on a still camera (FSR debug view), reversed-Z depth with `EnableDepthInverted` (the port sets it; near/far swap
is the port's own), jitter convention (`-jitter` flipped or zeroed both make 432 worse: walk_open |dlum| 3.88 -> 5.88 / 5.37), LDR in-shader-tonemapped
input with exposure 1 (no HDR flag needed), no textures (no mip bias), HUD drawn after FSR. Missing: reactive / T&C masks (matter for fluids only).
Found: (1) the dark outlines are anti-aliasing of sub-pixel dark step faces -- already there with FSR at 1080 rows and RCAS off; 16x16-px block means
match native within 0.77 levels (per pixel 2.53). (2) FSR's AUTO shading-change detection fires on static terrain (jittered samples of sub-pixel steps
differ frame to frame) and zeroes accumulation there -> soft. Knobs (capture arm tokens in `PlaygroundCapture.RowsLook.cs Fsr3ArmVariant`, play flags in
`UpscalerSelect.Fsr3KnobsFromArgs`): `_sNN` / `-fsr3sharp N` RCAS sharpness N/10 (0 = off; default 8); `_scNN` / `-fsr3sc N` cut the auto shading change
by N/10 (`RaymarchFeature.Fsr3ShadingChangeCut` -> `_VeShadingChangeCut` in the vendored `ffx_fsr3upscaler_prepare_reactivity.h`; 0 = upstream);
diagnostics `_dbg` (FSR debug view, now in release players), `_jf`, `_j0`; `-lookladder` takes `1080` and `none432`. Run:
`PRESETS=high EXTRA="-lookladder native,432,432_s10,432_sc10,432_sc10_s10,540,540_sc10,540_sc10_s10 -lookladderbursts native,432,432_sc10,432_sc10_s10
-lookposes walk_near,walk_open,high,cliff,torch -lookwait 60 -shellworkers 6" ./run-look-review.sh`; table `python3 -I tools/fsr166.py <dir> <out> <arms>
[crop arm] [burst arms]`. Result: `Screenshots/166/fix/fsr166.txt`, `Screenshots/166/diag/fsr166.txt`, HANDOFF 166.

**Task 167 (perf, task 161's warm AC measurement): `KernelDiet2` default ON (`ParseFlagOn`, `-diet2 0` = before).** Warm AC cost split at 432
(`Logs/costsplit_2026-11-01_151803.txt`, warm-up `Logs/tg2world_20261101_151159.txt`): diet 2 -6 ms walk_near, -4.5 ms walk_open p50. The 14:36 run
(`Logs/costsplit_2026-11-01_143603.txt`) is NOT valid for B: launched without the play flags (shells off) and without `-diet2 1` (every row `kernel lean`).
Next cut per the table: D (`skyprobe_off` -6 ms walk_near). GPU trace `gpu-traces/walknear161_full.trace` (`python3 -I tools/gpu161.py <trace>.export/gpu.xml`).

**Task 168 (perf, `PERF_EXACT_CUTS.md` cut D, owner Q102): incremental roof pyramid + roof skip in the diet-2 kernel; `-roofskip 1` (stays OFF in play: measured ~0), `-roofinc 0|1` (default 0: NOT exact under streaming, below), `-roofverify N`.**
The shadow roof skip's HeightMip pyramid is a pure function of `ClipmapBuffer` (+ window Y origin / height, content ceiling); `TerrainClipmap.WriteClipmapChunk`
is its only writer, so `TerrainClipmap.HeightDirty.cs` records the ring CHUNK columns written and `RaymarchFeature.RoofInc.cs` rebuilds only those
(`Raymarch.compute BuildHeightRawCols / DilateCols / LevelCols`, the same per-word functions as the full build; full build on first use, a new buffer
or clipmap, a frame without the skip, a Y origin / height / ceiling change, > half the columns dirty, or `-roofinc 0`; nothing at all when no column
was written). `-roofverify N`: every Nth build also does a full build into a second buffer and counts differing words on the GPU (`HeightVerify`;
`[roofverify]` log lines + `RoofIncSummary()`). Roof skip ON now draws `CSMainFarShadeLeanRoof` (`VE_DIET2 VE_DIET2_ROOF`: `_ShadowRoofSkip`
folded to 1; `KernelDiet2Rule.RoofKernel`) instead of falling back to the twin. EXACTNESS SCOPE (`TraceShadowRay`'s `allowRoof`): the skip changes
WHERE a ray leaves the window (its air-mip leaps start elsewhere), and task 133's shell continuation starts at that exitT, so the skip is used only
by rays whose exitT nobody reads: the sky probe, and sun rays with no shell continuation (twin kernels); the diet-2 roof kernel never skips the sun
ray. Level search now tests level 0 first (1 read below the roof instead of 6; same level by monotonicity). Cost split arms `roof_on` (diet2+roof),
`roof_full` (full rebuild each frame), `roof_off`; `-costroofpair` (with `-coststillraw`: roof flipped 10 frames after raw2, then back; `python3 -I
tools/roof168.py pair|arms <stills>`), look review `-cliffbefore 6` (roof forced OFF first; launch `-roofskip 1`). Tests `RoofPyramidIncrementalTests`
(CPU twin of the region formulas vs a full rebuild after random writes incl. the ring seam), `KernelDiet2Tests` (roof fold + rule).
RESULT (warm AC, 432 rows, play flags, `Logs/costsplit_2026-11-02_075812.txt`, warm-up `Logs/tg2world_20261102_075153.txt`; streaming pinned, so the
timed passes paid NO pyramid upkeep = D's best case): roof_on (diet2+roof) - FULL p50 walk_near -0.3 / -0.4 / +0.1 ms (FULL range 0.6), walk_open
-0.7 / -0.2 / +0.4 (FULL range 1.6) -> ~0, kept behind `-roofskip 1`. Exact: in-place flip and arms 0 px at walk_near / walk_open
(`2026-11-02_064444` stills, `roof168.py`), cliff1 A native 0 px. Roof skip cut far PRIMARY steps at walk_near 9,044 -> 1,945/frame and sky-exit
ends 89 -> 97 %, but no time: the sky probe's 6.2 ms (167's arm) is not in its window walk. INCREMENTAL BUG, open: the streaming warm-up with
`-roofverify 4` (`Logs/overnight/168_warmup_Player.log`) found incremental != full rebuild on 774 of 3749 checks (29-4,718 words, idle frames too,
transient), so `-roofinc` defaults OFF (full rebuild per frame with `-roofskip 1`, task 59's exact build). Next cut: C1.

**Task 168b (perf, measure-only, owner-requested: is 30 fps reachable at all?).** Cost split gains stacked arms `skysun_off` (sky probe + sun),
`sunshells_off` (sun + shells) and `ceiling` (sky probe + sun + shells; FSR 3 kept), and every run line / table row now prints p95 and the share of
frames > 33 ms (`Over33`). `-sustained` (MODE=walk / playwalk) now honours `-noskyprobe` and `-nosim` (the walk header prints `sky probe ON|OFF`).
Ceiling walk: `MODE=playwalk MINUTES=3 EXTRA="-renderrows 432 -noskyprobe -shadowlevel 3 -shells 0 [-spikelog] [-nosim]" ./run-tg2world.sh`.
Warm AC results: `Logs/costsplit_2026-11-02_095908.txt`, walks `Logs/tg2world_20261102_094834` / `_095315` / `_101810` / `_102253` (ABBA), spikelog walks
`_103551` (ceiling, no fluid) and `_102802` (ceiling); script `Logs/overnight/168b_measure.sh`. Capture-only: no change to play.

**Task 168c (perf, `PERF_EXACT_CUTS.md` cut C1, owner Q102): sun-ray shell marches out of the big kernel; `-sundefer 1` (OFF: NOT exact, and slower).**
Linearity check first (`SunDeferTests.SunVis_EntersTheShadingNonLinearly`): sunVis does NOT enter the colour linearly (sunExposure's lerp,
UniformFaceHue, haze, the grade's shoulder, `LookToLinear`'s pow 2.2 and saturate, SeeCompose), so the shading is SPLIT. `CSMainFarShadeLeanSun`
(`VE_FAR_SHADE VE_DIET2 VE_SUNDEFER`) does not run task 133's near continuation nor task 80's shell-hit sun march; a pixel that would shades from the
colour onward twice (a `[loop]`: pass 0 unblocked sunVis -> target, pass 1 sunVis 0 -> `SunDeferB`, VAO / point lights / bleed kept from pass 0),
and writes the ray (start, tStart, `SUN_DEFER_MARK` 4) into its `ShellPre` entry. `CSSunDeferLean` (after pass 2) marches it alone and on a hit copies
`SunDeferB` into the target and 0 into the temporal history. Needs the shell pre-pass (`KernelDiet2Rule.SunDeferKernel`); roof skip ON keeps the roof
kernel. `SunDeferB` = one render-graph texture in the target's format (B10G11R11, 4 B/px: 1.27 MB at 432 rows, 3.5 MB at 720). Cost split arms
`sundefer_on|off` (run line `kernel diet2+sun`), `-costsdpair` (with `-coststillraw`; `python3 -I tools/roof168.py pair <stills> sd`,
`arms <stills> sundefer_on`), look review `-cliffbefore 7` (+ `[cliffkernel]` log lines). Tests `SunDeferTests` (non-linearity, march arguments
identical to the inline ones, every task-168c line under `VE_SUNDEFER`, rule). RESULT (`Logs/costsplit_2026-11-02_145837.txt`, stills
`UpscalerClips/2026-11-02_145837`): NOT exact -- in-place flip 12 px (walk_near) / 55 px (walk_open), all +-1 level in one channel, deterministic
(control 0 px, flip/back the same count): float code-gen of the restructured shading, not wrong sunVis. Indicative (cold, one launch, not a
measurement): sun kernel p50 67.7 / 68.0 vs FULL 64.7 / 59.9 ms walk_near, 41.7 / 42.4 vs 30.1 / 34.0 walk_open -- the double shading costs more
than the march saves. STOP_FOR_OWNER; no warm measurement (the task's rule for a non-exact result).

**Task 168d (cut E, streamer admission time-slice; flag OFF).** `-admitbudget <ms>` (`StreamManager.AdmitBudgetMs`, default 0 = the count cap
`MAX_CHUNK_LOADS_PER_FRAME` only, as before): the per-frame admission drain (`DrainCompletions` in `Update`) stops once it has spent the budget,
after at least one admitted chunk, and leaves the rest of the FIFO for the next frame (checked before dequeuing). Startup / `WaitForIdle` drains are
not budgeted. The completion queue is bounded by the scratch pool (2 x workers), so a slow drain throttles dispatch instead of building a backlog.
Every sustained/playwalk report now prints an `ADMIT (task 168d)` line: budget, admitted/s, completion->admit wait mean/p50/p95/max, budget stops,
load deficit at the end. Test `AdmitBudgetTests` (a ~0 ms budget admits 1/frame; driven with Update only to rest, it gives the same resident set,
voxels (strided) and brick layout as the unbudgeted drain; budget OFF keeps the count cap). RESULT (warm, AC, 432 rows, `-spikelog` both arms,
ABBA `Logs/tg2world_20261102_153238/153721/154216/154709.txt`, warm-up `_152217`): > 33 ms A 44.3 / 39.9 %, B (4 ms) 36.9 / 43.4 %; p95 A 58.1 / 55.4,
B 55.0 / 57.0 ms -- within noise. Reason: with shells ON (play) an admission costs ~0.1-0.25 ms (168b's ~1.1 ms came from its `-shells 0` CEILING walk,
which allocates cascade tiers 1-2 and pays `SubmitPrecomputed` per admit), so the drain is 1.8 ms on a > 33 ms frame and removing it ENTIRELY would
cut the share only ~3 points. Kept OFF.

**Task 169 (torch flood, supervisor "going ahead with (a)" on 165's Q2, doc Q106; flood stays OFF; exact cuts only): two cuts to the per-edit cost.**
Cut 1, ANCHORED bricks (`TorchFloodBuild.AnchorsOn`, `-tfanchor 0|1`, default ON): an all-air brick is also direct when every voxel v has a valid first
move m from the torch (destination + footprint open) with w(m) + C(v - t - m) == C(v - t) and bbox(t + m, v) open (the same summed-area pass, origin
t + m, only m's sign cases, skipped when no in-reach candidate brick lies on m's side): the path then costs the lower bound, so d = C exactly. The
summed-area pass was made ~4x cheaper (plane flags precomputed per origin, no per-case divisions). Cut 2, LIT-ONLY edits (`TorchFloodPool.EditLitOnly`,
`-tfeditlit 0|1`, default ON): an edit (now kept as a per-brick voxel bbox, not just the brick) re-floods a torch only if the torch lights a brick of
the edit's box grown by 1 voxel or that box reaches the torch's own brick (closing an unlit voxel or opening one with no lit neighbour changes no cost;
the kind / split change of the edited brick matters only where the torch lights it); torches with a flood in flight or no cache re-flood as before.
Tests `TorchFloodL3Tests`: `Anchored_PostWithObstacles_EqualsOracle_AndMoreDirect` (both settings == oracle; direct 3,462 -> 5,814),
`EditCut_SkipsUnlitRefloodsLitAndNewPaths` (a block behind a 1.6 m wall: only the near torch re-floods; a sealed cavity: none; a tunnel: both),
`EditCut_SealedTorch_OpeningItsShellRefloods`; all 165 cases incl. Slow `Fuzz_Long` stay exact. Report-only in-player run, same build OFF then ON
(`run-cost-split.sh EXTRA="-costpaths torch -tfl3 1 [-tfanchor 0 -tfeditlit 0]"`, `Screenshots/169/tfl3_169.txt`): per torch 260.6 -> 225.8 ms
(searched voxels 2.41 -> 2.19 M), re-floods per camp edit 10-11 -> 7, settle 2.17-2.77 -> 1.23-2.05 s, camp fill 13.5 -> 5.3 s; exact + GPU PASS at
every step. Open-ground EditMode flood 61 -> 32 ms; small rooms +1.4 ms (anchor passes that find nothing).

**Task 168e (cut E, upload pacing through a staging ring; flag OFF).** `-uploadring 1` (`UploadRing.Enabled`; `Assets/CoreEngine/Mirror/UploadRing.cs`)
routes every per-frame mirror write through the ring instead of `SetData`: the TerrainClipmap entries, bodies, packed air-mip and fluid directory, plus the ShellStore
bricks, pages, directory and heights. A write is copied into a `LockBufferForWrite` staging slot (4 x 1 MiB + 4 x 128 KiB records = 4.5 MiB). Each producer pass then flushes
once: `UploadScatter.compute` copies staging -> destination on the GPU. Records that overlap within a flush split it into ordered dispatches. A slot is reused only
after a 4-byte AsyncGPUReadback ticket shows the GPU has passed its copies. With no slot free, a temporary of at most one slot is allocated; above `TEMP_LIMIT` 32 MiB the CPU waits for the GPU.
Writes go in slot-sized pieces. While the ring is on, the clipmap + shells per-frame budget is capped at `-uploadcap KB` (default 1024; 0 = keep 3 MiB). Progress rule: with the
ring on, the first non-exempt chunk of a pass always uploads. Without it, a dense chunk costing more than the cap minus the ~585 KB air-mip reserve never landed while
the camera stood still (first run: 24,455 px of stale far terrain at walk_open). `-uploadcheck` (`UploadShadow`) records every written word on the CPU and reads
the GPU buffers back for comparison. Capture `-costringpair` flips the ring, re-uploads the whole mirror (`TerrainClipmap.DebugDirtyAllHeld`), drains it and shoots `_raw_ringflip/_ringback`.
Tests `UploadRingTests`: a CPU "GPU" runs dispatches 0-4 frames late, and drained == sequential SetData byte for byte over 12 seeds. The negative control (guards ignored)
corrupts. No temporary is larger than a slot. In-player check (`Logs/costsplit_2026-11-02_193709.txt`): 0 of 57.1 M / 67.2 M written words differ, ring ON raw == ring OFF raw 0 px
at walk_near and walk_open, and the whole mirror drains in 10.3-10.9 s (SetData at 3 MiB: 4.2-4.4 s). Warm ABBA playwalk (pre-progress-rule build): > 33 ms 19.1 -> 16.2 %,
p95 44.9 -> 41.8, p99 65.3 -> 52.9 ms, > 100 ms 6 -> 5. That misses 168e's ON gate (-5 points or > 100 halved), so the flag stays OFF.

**Task 171 (look QA, owner Q107 (a) 2026-11-02, CAPTURE-ONLY; PLAY DEFAULT NOT CHANGED): task 166's F2 (`-fsr3sc 10`) under live light and moving water.**
`-lookreview -fsrlive 1` (`PRESETS=high EXTRA="-fsrlive 1 -lookwait 40" ./run-look-review.sh`; `PlaygroundCapture.FsrLive.cs`) shoots bursts of consecutive
frames per arm (default native, fsr3_432, fsr3_432_sc10, fsr3_540, fsr3_540_sc10; `-fsrlivearms`), every moving thing stepped per FRAME so native frame k is
the truth: sun at walk_open from 16 h (`-fsrlivesunh`) still / play's 1200 s day / the rigs' 120 s day; a 1.5 m water cube slid 0.1 m/frame (sim suspended);
a 2x2x3 m water pour with the sim RUNNING (not frame-exact, judged by eye; removed afterwards); night torch scene (task 151) with one torch lit / put out
(frames before + 6 after) and one moved 0.1 m/frame. Numbers + 4x sheets: `python3 -I tools/fsr171.py <LookReview dir> Screenshots/171`. Result
(`Screenshots/171/fsr171.txt`): sun clean (inside the moving-shadow mask 432 20.5 vs sc10 21.3, fast 26.9 vs 27.1); water slide about equal (432 5.36 vs
5.42, 540 4.69 vs 5.33; both FSR arms trail the cube); the MOVING TORCH ghosts with sc10 (in the change mask 17.2 -> 47.2 at 432, 12.8 -> 49.7 at 540:
doubled torch + bright speckle trail), and torch on/off lags one frame more (frame 1 after: on 13.0 -> 16.4, off 6.8 -> 11.6; equal from frame 2).
So the play default stays sc 0 (STOP_FOR_OWNER in HANDOFF).

**Task 172 (look, owner Q105 / Q107 2026-11-02, CAPTURE-ONLY; nothing in play changed): do the RECOMMENDED torch settings together look cinematic?**
`-lookreview -torchcine 1 -tfdir 2` (via `PRESETS=high EXTRA="-torchcine 1 -tfdir 2" ./run-look-review.sh`; `PlaygroundCapture.TorchCine.cs`,
hooked in `PlaygroundCapture.LookReview.cs`). Night (`-nighttod`, default 0.5 h), clock paused, fluid suspended. Arms (`-tcarms`, LadderArms
syntax, `+f` = flood ON with dir mode 2): today = `540,720` (play's FSR 3 dyn 540-720, flood OFF), recommended = `432_sc10+f,540_sc10+f`, truth =
`native,native+f`. Scenes (`-tcscenes` filters the shots): camp15 / camp40 (9 torches on posts, 40 m ahead of the start on its most open heading),
camp40pan (4-frame strafe burst), walk (8-frame burst at 4 m/s through the camp), wall (6 torches on the nearest cliff foot's face), cave (a 3 x 3.2 x
12.5 m tunnel carved 22 m beside the row, 2 wall torches), water (nearest shore within 90 m; none -> a SYNTHETIC level stone terrace with a flush
8 x 5 m pond). Sheets: `python3 -I tools/torchcine172.py <LookReview dir> Screenshots/172` (full-size today | recommended per scene, 4x crops,
burst flicker vs native, GIFs). Result in `Screenshots/172/torchcine172.txt` and HANDOFF (STOP_FOR_OWNER): the flood makes torches read at all
(wall lit px 0.8 -> 16 %, cave 0.8 -> 16 %, water 4.5 -> 37 %), but its pools are flat and over-saturated near posts, and moving shots flicker more
vs truth (walk lit 4.1 -> 6.8, 40 m strafe 4.4 -> 11.0).

**Task 173 (look, PLAN + CAPTURE only, owner 2026-11-05 "look fantastic first": world gen; no generator / shader / default change): world-gen look survey + `WORLD_GEN_LOOK_PLAN.md`.**
`-lookreview -worldsurvey 1` (`PlaygroundCapture.WorldSurvey.cs`, hook in LookReview): poses chosen from the world record -- every biome x
landform pair >= `-wspaircells` (300) cells, up to `-wspairs` (18), at its deepest-inside cell from 160 m back / 35 m up; each signature feature
(canyon also from inside); coast (highest relief + median); the highest cell from 1.5 km / 200 m up; the biggest river valley; the tallest sun-lit
Sheer face; the nearest CaveMouth stamp (outside + 20 m down its tube); a store scan for the most open standing-height air 40 m under the start
and 200 m below sea (`native_lit` arm: capture-only `undergroundAmbientFloor` `-wsfloor` 1.0). Arms `-wsarms` (native,540); `-wsposes` takes names
or kinds (`feature,river`). Writes `worldsurvey_map.png` + `WSPOSE` lines. Run: `PRESETS=high EXTRA="-worldsurvey 1 -mapversion 4 -shadowlevel 0
-shadowres full -roofskip 0 -underwater 1 -lookwait 30 -shellworkers 6" ./run-look-review.sh` (~40 min, 36 poses); viewer + annotated map + sheets:
`python3 -I tools/survey173.py Screenshots/173 Screenshots/173/findings173.json <LookReview dir> [<reshoot dir> ...]` (a later dir replaces whole
kinds). Result: HANDOFF 173, `WORLD_GEN_LOOK_PLAN.md`, `Logs/lookreview_20261105_134936.txt` + `_143742.txt`.

**Task 174 (visuals, owner Q4 (c) 2026-11-05; PLAY DEFAULT CHANGED): FSR 3 reactive mask, so play gets 166's crisper sc10 without 171's ghost.**
`-fsr3reactive 0|1` (`RaymarchFeature.Fsr3Reactive`; capture arm token `rm`, diagnostics `rm1` = light/colour test only, `rm2` = new-voxel test only).
Play (`IsPlaySettingsRun`) now defaults to `-fsr3sc 10 -fsr3reactive 1`; `-fsr3sc 0 -fsr3reactive 0` = the old FSR 3; rigs/capture keep sc 0, no mask.
How: CSMain's TAA block writes `TaaDynW` (only when `_TaaEnabled & 2`): per pixel in display levels, the dynamic light's share (point lights / torch
flood + emissive bleed), or the whole pixel's luminance for fluid / lava / emitter pixels. `VoxelUpscaleInputs.compute` kernel `UpscaleReactive`
reprojects by the camera motion and flags (1) the GAP between this frame's 3x3 [min,max] and last frame's at the reprojected position (0 when they
overlap: jitter aliasing on still terrain samples the same set of values; mask = saturate((gap - 0.5) / 1.5) levels), (2) in a chunk the mirror
rewrote this frame, a surface key (voxel + material) unseen in last frame's 3x3, where a voxel is >= 2 px or is an emitter (torch / lava / sludge).
FSR 3.1 feeds the app's reactive mask into its shading-change channel, which `-fsr3sc` does not cut. QA: `-lookreview -fsrlive 1 -fsrlivescenes task174`
(sun_still, night torch still/on/off/move, lava_near 7 m / lava_far 45 m streams advancing 0.1 m/frame, and torch_move / lava with the flood ON `_f`),
`python3 -I tools/fsr174.py <LookReview dir> Screenshots/174` (table, 4x sheets, full-size `viewer.html` with `captions174.json`). Result in
`Screenshots/174/fsr174.txt`: moving torch, error in the change mask today 17.3 / sc10 51.6 / sc10+mask 8.9 (432); still sun sharpness vs native 0.80 /
0.85 / 0.87; mask 0 on every pixel of both still scenes (FSR debug view).

**Task 174b (look, owner "Tune first, then on"; capture only, NO default change -- task 175 sets them): torch-flood intensity / falloff sweep.**
`PlaygroundCapture.TorchCine.cs` gains `-tcsets "I/k[/wrap],..."` and arm tokens `+fN` (flood ON with set N; `+f` = the launch's own `-tfi/-tfk/-tfwrap`;
TorchFloodPool.Params is read every frame, so no rebuild), and writes `tc_<scene>_proj.csv` per still (viewport positions of every torch, torch + 2 m
= the near-post disc, and ground points r 0.25-10 m on 16 azimuths round the camp centre / water torch). Launch: `PRESETS=high EXTRA="-torchcine 1
-tfdir 2 -tcarms 540,native,540+f1,native+f1,...,540+f5,native+f5 -tcsets 2.0/0.10,1.4/0.20,1.6/0.35,2.4/0.60,3.0/1.20" ./run-look-review.sh`.
Sheets / table / viewer: `python3 -I tools/torchtune174b.py <LookReview dir> Screenshots/174b <lookreview log>` (lit %, clip % near posts = any
channel >= 250 inside the discs, radial profile from native, walk / strafe flicker vs each set's own native). Result: HANDOFF 174b; recommended
I 1.6 k 0.35 (the point lights' own values): clip near camp posts 6.8 -> 0.3 %, walk flicker 5.2 -> 3.7, strafe 10.8 -> 6.9.

**Task 176 (world gen C1, owner 2026-11-05 Q2 (a) "vary climate"; GENERATOR CHANGE, STOP_FOR_OWNER "keep this layout?"): biome layout varies across the island + patch transitions.**
`ShapeField.Biome6`: on v4's six-biome field (`TerrainSpec.BiomeVary`) the climate wavelength is `CLIMATE_WAVE_R` 0.75 x islandRadius
(temperature x1.25), three octaves 1 / 0.35 / 0.2 (`Climate3`, Fbm's lacunarity), normalised PER WORLD to mean 0, sd `CLIMATE_SD` 0.7
over the island disc (`ClimateNormalise`, 64^2 samples in `Offsets.Compute`; seed noise only, no map). No domain warp; centres unchanged.
`-biomevary 0` / `spec.biomeVaryOff = 1` = B10's one-octave field, bit for bit (the B1/B10 gates in WorldRecordTests now measure it).
Border blend (task 122): the per-0.8 m-cell hash dither is replaced by a 10 m PATCH octave (`BLEND_PATCH_*`, +-14 m) on every v4 world.
Tests: `BiomeLayoutTests` (8 seeds `BiomeLayoutDump.LayoutSeeds`: straight border <= 1.7 km at +-30 m, every biome >= 2 regions of
>= 25 ha, shares 5-35 %, change E-W >= 300 m, area-wtd median >= 35 ha; `Logs/TerrainGen/c1/c1_layout.txt`). Tools: `REPORTS=1
C1TAG=<tag> [C1SEEDS=sweep] tools/run-editmode-filter.sh BiomeLayoutDump` dumps land/biome planes; `python3 -I tools/biome176.py stats|maps|viewer`
(also a Python port of snoise for prototyping candidate climates). Survey: `-wsfixed "name|ex,ey,ez|ax,ay,az;..."` adds fixed poses (`fixed_<name>`).
Result: HANDOFF 176, `Screenshots/176/viewer.html`.

**Task 175 (look, owner 2026-11-05 "Tune first, then on" + 174b answer S3; PLAY DEFAULT CHANGED): the torch flood is ON in play.**
`PlaySettings.torchFlood` (default true; a file saved before 175 gets on; `-torchflood 0|1` overrides for one run, bad values keep the saved one)
-> `SettingsMenu.LoadAndApply` -> `TorchFloodPool.ApplyPlayDefaults(on)`: `PlayDirMode` 2 (Q105), `PlayIntensity` 1.6, `PlayFalloffK` 0.35 (174b S3,
the point lights' own values), then ParseArgs so this run's `-tfdir/-tfi/-tfk/...` still win, then `SetEnabled`. F6 has a live "Torch light" row
(Flood / Point lights; `SetEnabled(true)` from OFF re-floods every torch from scratch, since edits are not tracked while OFF). L3 incremental path and
169's edit cuts unchanged. Rigs and capture runs never call ApplyPlayDefaults: they keep I 2.0 k 0.10 dir 0, flood OFF unless flagged. EXCEPT
`tools/play-flags.sh` PLAY_SETTINGS_FLAGS (MODE=playwalk) now carries `-torchflood 1 -tfdir 2 -tfi 1.6 -tfk 0.35`, as its guard test requires
(`PlayFlagsFileMatchesPlaySettingsDefaults` also pins the three values to the constants). Tests: `PlaySettingsTests.TorchFlood_*`.
QA: `-torchcine` arm token `+p` = the flood exactly as play's default path sets it (no -tf* flags may be passed), scene `edit` (survival-style
EditService with the light buffer attached: place a torch on a post, dig a 1 x 1 x 0.5 m pit, dig the middle torch; settle + re-flood counts +
memory logged per step): `PRESETS=high EXTRA="-torchcine 1 -tcscenes camp15,camp40,camp40pan,walk,edit,cave,wall -tcarms
540_sc10_rm+p,432_sc10_rm+p,native+p,540_sc10_rm,native,540" ./run-look-review.sh`, then `python3 -I tools/torchplay175.py <LookReview dir>
Screenshots/175` (stats, burst flicker, viewer.html, tp175_edit.png). Result: HANDOFF 175.

**Task 176b (world gen C1 follow-up, owner 2026-11-05 "Show me sizes"; MAP-ONLY, no generator change, STOP_FOR_OWNER "which size?"): biome region size candidates.**
`BiomeSizeDump.Report_DumpBiomeSizes` (TerrainGen.Tests, Slow + report-only: `REPORTS=1`, filter run) builds the real v4 map for seeds
42/7/2026/31337 and writes biome planes on its land mask to `Logs/TerrainGen/c1/sizes/<plane>/`: `b10` (biomeVaryOff 1 via ShapeField.Biome),
`c176` (the record plane) and the `C1SIZES` variants (`name:k:amp2,amp3,...`; default w100 1.0 x R, w150 1.5 x R + 4th octave .12, w200 2.0 x R,
w350 3.5 x R). The variant path is checked against the generator (octave chain vs ShapeField.Climate3 <= 1.2e-7; the 0.75 variant reproduces the
record plane except 27-134 of 1,048,576 cells per seed, Burst vs Mono rounding; gate 1e-3). `python3 tools/biome176b.py stats|sheets` -> 
`Screenshots/176b/` (sizes_176b.txt, sheet_seed*.png, full_seed*.png, viewer.html). Applying a size is a follow-up: CLIMATE_WAVE_R (+ an
octave for w150), BiomeLayoutTests gates, re-pinned goldens, whole suite. Result: HANDOFF 176b.

**Task 177 (world gen C2 + C3 merged, owner 2026-11-05 "use materials from that biome ... it might not always be snow"; GENERATOR CHANGE, STOP_FOR_OWNER "change any palette cell?"): ground cover = shape-chosen SLOT x biome PALETTE.**
`Assets/TerrainGen/Cover.cs`: `ShapeField.Sample` (surfaceBiome on, `TerrainSpec.CoverSlots` = v4 and `coverSlotsOff` 0) stores
`ColumnSample.cover` = slot (bits 0-2: flat / steep / hollow / high / shore) | patch variant (bit 3), from `Cover.Choose`: shore = ground <= 2.2 m
over the sea (beach band + sea bed); hollow = under a lake/river; steep = 6 m-scale slope of the smooth height (macro + octaves 0-2, +-6 m probe)
over 30 deg (Danger.SCRAMBLE_DEG, +-6 deg patch jitter); high = altitude over 2/3 of `spec.peakHeight` (the record's top third, +-5 %
jitter), where only FACES over 45 deg (Danger.CLIMB_DEG) stay steep; below it a crest (>= 0.5 m over its ring, above the +-120 m mean) is steep; hollow = a dip >= 0.4 m below the +-120 m mean, or a lake/river cell within ~16 m
and <= 2.5 m below; else flat. One patch noise (6 m + 25 m) jitters every cut; variant noise (15 m + 4.5 m) > 0.3. BAND LIMIT: cover octaves shorter than 2 footprints
drop (`Sample(.., coverTexel)`: chunks 1, shells 2^shell, far band its texel), so far texels / coarse shells show patches, not per-texel camo;
ShellChunkTests' reference samples the same way. The rules never name a
material and take no biome: `Cover.PALETTE` (6 rows x 5 slots x main/variant; `BARE` = no skin, the column's own rock shows) is the only
per-biome data. `DetailField.Material` / `UniformSamples` / `TopMaterial`: skin = `Cover.Settle(palette cell, step)` (sand/beach sand/snow
pack where the voxel step > 1), soil under it except on steep, rock where BARE or the voxel step > `SHEER_STEP` 3 (before: every step > 1.5
showed rock = the speckle). New `GenMat.Gravel` 17 (palette D `8e877c`, Tools/Items/MaterialRules). `-coverslots 0` / `coverSlotsOff 1` = the
pre-177 surface bit for bit; v3 unchanged. Tests `CoverTests` (palette cells solid, TopMaterial = the row lookup, a test-only "fire" row
gets magma crust on high ground with no rule change, cover off = old, slot ignores the far footprint; Slow: slot shares + speckle on 4 seeds
-> `Logs/TerrainGen/c2c3/shares177.txt`; report `REPORTS=1 tools/run-editmode-filter.sh CoverTests.Report_CoverMaps` -> `cover_seed*.bin`,
`cost177.txt`). OreTests' ring gate is now the absolute bound it was (ore adds <= 8.9 mixed bricks per ring chunk = 5 % of the pre-177
base): the cover cut the base 28 % (5,677 -> 4,093) while ore still adds +215. `python3 -I tools/cover177.py maps|palette|shots|viewer Screenshots/177 ...` -> island slot maps, palette177.png/.md,
before|after shots (`-wsfixed` 173 poses, `-coverslots 0` arm), viewer.html. Result: HANDOFF 177.

**Task 178 (WORLD_GEN_LOOK_PLAN C4, cliffs with geology; owner 2026-11-05 "C4 cliffs approved").** v4 only, `spec.Cliffs`
(`cliffsOff` / `-cliffs 0` = the pre-178 cliffs; v3 unchanged). (1) THICK ROCK LAYERS: above the stone / deep-stone boundary
(sea - 50 m) `DetailField.LayerRock` is a stack of horizontal layers by height, `LAYER_THICK` 24 m +- 30 % (9.6-38.4 m), boundaries
waved by `LayerWave` (+-4 m / ~90 m, +-1.5 m / ~26 m); each layer has a ROLE from a hash of its index (main ~1/2, light ~3/10, dark
~1/5) and `LAYER_ROCKS` (6 biomes x 3 roles) is the only per-biome data. Same boundary heights in every biome. Badlands' 3.2 m bands
are gone (owner: no thin stripes in any biome until said otherwise). The brick prover bounds the layer offset (`LAYER_WAVE_BOUND`).
(2) FACE RELIEF (`ShapeField.FaceRelief`, driven by the map's Catmull-Rom slope and height only): vertical fluting (+-0.8 m sideways,
from 70 deg), ledge treads every 4 m along the layer wave, a scree apron at the foot from the map's concavity at +-20 m (<= 10 m,
rubble on top; scree columns take Cover's `steep` cell); skipped near lake/river cells and under the sea. `BandLimitBound(spec, ..)`
adds the relief's band bound. Tests `CliffTests` (layers >= 9.6 m and only the biome's rocks in all 6 biomes; relief only on faces /
scree; far-band bound + LOD-0 contract on face columns; report `REPORTS=1` `Report_Cliffs` -> `Logs/TerrainGen/c4/` sections, top
views, mixed bricks, Sample cost). `python3 -I tools/cliff178.py sheets|viewer` -> `Screenshots/178/viewer.html` (173's `-wsfixed`
cliff / coast_cliff / forest_breaks poses, `-cliffs 0` arm). Result: HANDOFF 178.

**Task 176d (world gen C1, owner 2026-11-06 on 176c Q1 (a) "allow one big area"; GENERATOR CHANGE): biome Size 2 ships.**
176c's patch applied on top of 177/178: `ShapeField.CLIMATE_WAVE_R` 0.75 -> 1.5 x islandRadius, a 4th climate octave `CLIMATE_OCT4` 0.12
in `Climate3` (name kept), and `BlendClimateReach`'s gradient bound gains the `OCT4 * 2.03^3` term (`BiomeBlendTests` prove it).
`BiomeLayoutTests`' regions gate is now PRESENCE: every biome has >= `REGIONS_MIN` 1 land region of >= 25 ha (was 2); every other C1 gate
unchanged (straight <= 1.7 km, shares 5-35 %, change >= 300 m, median >= 35 ha). `-biomevary 0` (B10) unchanged. Re-pinned: ShellFill GOLDEN
(same 85 chunks), `WorldMapBurstTests.PLAYER_V4_SEED42` from the survey launch's player log (the `-worldsurvey` header now prints
the tg2 report's "map v4 Height FNV" line, so a survey launch can re-pin it). Maps: `python3 -I tools/biome176.py maps sizes/c176 size2c/c176
Screenshots/176d/map_seed<S>.png <S> 1024` (left 176 size, right Size 2). Result: HANDOFF 176d.

**Task 177b (world gen C2/C3 palette, owner 2026-11-06 on 177 Q1 "tweak all three"; GENERATOR DATA CHANGE): palette tweaks.**
`Cover.PALETTE` only: desert `steep` variant cream sandstone -> BARE (one cell, no cream patches; the bare rock is 178's thick layers);
savanna `flat` variant red earth -> packed sand (a dusty tan earth, CIE dE 18 from savanna grass vs red earth's 43; same ~23 % of flat,
the variant noise is biome-agnostic). Savanna `hollow` variant and savanna soil stay red earth. Badlands: the palette never made stripes;
178's thick layers already replaced B10's 3.2 m bands with cliffs on, and 177b deletes `DetailField.BadlandsBand`, so `-cliffs 0` shows
plain `BiomeRock(Badlands, 0)` (red rock) instead of stripes (`CliffTests.Layers_...` now asserts one rock per biome with cliffs off).
Shares: `Screenshots/177b/shares177b.txt` (from `CoverTests.Report_CoverMaps`, REPORTS=1). Shots: `-worldsurvey -wsarms native -wspairs 60
-wsposes fixed,pair_savanna_wetland-basin,pair_desert_canyonlands,pair_badlands_canyonlands -lookwait 30 -wsfixed <176d's 5>`;
`python3 -I tools/palette177b.py Screenshots/177b` (pairs, sheet, viewer); `cover177.py palette` draws BARE as 178's three layers. Re-pinned: ShellFill GOLDEN (18 of 85 chunks; heights and the map hash do not move).
Whole suite PASS 1063 FAIL 2 = the golden (since re-pinned, filtered run green) + the known route test. Result: HANDOFF 177b.

**Task 178b (owner 2026-11-06, 178 Q2 / 176d Q1 (a) "keep routes smooth"; GENERATOR CHANGE): the cliff relief skips route cells.**
`MapView.route` (new, OPTIONAL, null = no skip) is `WorldMap.RouteMask`: 1 per map cell the world record flags `FLAG_ROUTE`, derived and
pinned in `WorldMap.SetRecord` (MapJob's FromCore). Every job that builds a `MapView` carries it beside `flow` as a `routeMask` NativeArray
(length 1 = none): chunk ColumnJob, ShellColumnJob, ShellFillJob, FarQuery, FarField's FillTg2Job, and the ShellChunk / ColumnPassSplit test
jobs. `ShapeField.FaceRelief` (fluting, ledges, scree apron) returns the un-relieved height on a route cell and fades back in over
`ROUTE_BLEND_CELLS` = 2 map cells (16 m) beyond it (`RouteWeight`: smoothstep of the distance from the column to the nearest route cell's
square; scree cover only where (1 - w) x fill >= SCREE_MIN). Map-only, so every footprint agrees; a convex blend, so the band-limit bound
holds. Cliffs-off and v3 are untouched; the cliff LOOK off-route is bit-identical to 178. Test: `CliffTests.Relief_SkipsRouteCells_UnchangedBeyondTheBlend`
(route-cell columns = cliffs off, land beyond the blend = 178's view without routes, blend columns between them, plus the 176d / 178 probes;
log `Logs/TerrainGen/c4/routes178b.txt`). Result: HANDOFF 178b.

**Task 178c (owner 2026-11-06 on 178's shots, "this looks horrible ... rework it"; GENERATOR CHANGE, cliffs on only): cliff look rework.**
(1) THE DRIPS: the rock wore the column's `surfBiome`, whose border jitter has 40 m / 10 m patch octaves; projected onto a steep face
each patch edge became a vertical streak (also in the plain pre-178 faces). The rock now has its own biomes `ColumnSample.rockA/rockB`
(`ShapeField.RockBiomes`: the border's two SMOOTH warp octaves only, shifted +-29 m), and each layer takes A or B by a hash of its index
(`DetailField.LayerBiome`), so layers interfinger at a border. (2) FLAT STRATA: `LayerWave` is +-1.2 m / 90 m + +-0.2 m / 26 m plus a DIP
(+-25 m over ~4 km); p99 boundary rise 0.87 m per 10 m. (3) CLOSE TONES: `LAYER_ROCKS` rows are each biome's close rocks (CIE dE 7-22,
was 22-62). (4) NO FLAT FIELDS: each 0.8 m brick whose 3-D value noise (12 m x 1.6 m lenses) is > 0.62 wears its rock's PARTNER,
the nearest-tone rock in palette D (`LAYER_PARTNERS`, dE 5-18), 28 % of bricks; a brick stays uniform (brick proofs keep their
`MixState` check). (5) Fluting is plain snoise 9 m x 0.5 m (was ridged 5 m: fine vertical hatching); the ledges are one bench at each
layer's base. The far band / shell top colour is the layer rock without the mix. Tests: `CliffTests.RockBiome_IsSmooth_NoPatchStreaks`,
`Mix_ShareAndPartnerRock`, `Layers_NearlyFlat`. Shots: `Screenshots/178c/viewer.html` (Space cycles before / 178 / rework) and
`<pose>_compare.png`; `python3 -I tools/cliff178c.py shots|sheets|tones`. 178's black `viewer.html` (rows were arrays, the page read
object fields) is fixed in `tools/cliff178.py`. World survey: `-wsposes cliff_<biome>` picks a face inside that biome. `-rockmix 0`
(spec.rockMixOff, `TerrainGenChunkSource.SetRockMix`) = the reworked layers without the mix (4th panel `<pose>_4_nomix.png`). The mix makes BURIED
bricks mixed in the coarser shells; the shell fill pass flattens them, but tests that run shells with the fill OFF fill their pools: the
streamer redraw test (`Streamer_EditedChunk...`) now turns the mix off. ShellFill GOLDEN re-pinned (68 of 82 chunks, fast == reference). Result: HANDOFF 178c.

**Task 179 (world gen C5, owner 2026-11-06 "queue C5"; GENERATOR + MAP CHANGE, shape only, the fluid simulation untouched): rivers that meander and carve.**
`Rivers.cs`, on by default on v4 (`spec.RiverShape`; `-rivershape 0` = spec.riverShapeOff, the pre-179 rivers, map AND columns, read by
`TerrainGenChunkSource` and `FarField`). (1) INCISED LEVELS (`Profile(..., incise)`): a river cell's water stands FREEBOARD (0.4-1 m by size)
under its map height and under the lower of its two side cells (at most SIDE_DROP 2 m deeper), taken upstream first as the lowest of the
cell and everything upstream, so humps are cut through instead of dips filled; a lake's OUTLET keeps the lake's level (else the lake's rim
buried the river's start). (2) THE COURSE (`GatherShaped`, 9 x 9 cells): each river cell's NODE moves SIDEWAYS (across the flow two cells on)
by `MEANDER_AMP` 2.5 half widths x sin(2 pi 0.75 snoise(p / max(24 W, 6 cells)) + seed offset), shrinking to 0 where the map ground there rises
4-20 voxels or drops 2-8 below the cell's, at a lake/sea cell, and at a mouth; A + W <= one cell (the renderer's flow map and Gather's window).
The course is the quadratic B-spline of the nodes (a Bezier round each node, 3 straight subsegments; level / width / depth / floodplain ride it).
Overlapping channels take the NEAREST centre line's level (was the highest: steps inside bends). Section: 1.5 voxels of water at the edge,
+0.5 per voxel inward (was 3 + 0.6). (3) CARVED BANKS (`Column`): beside a channel the ground is cut to a 27 deg slope from the water's edge,
by at most max(2 m, the river's incision + 0.8 m) <= 6 m, tapering linearly to 0 at 10 m; never below a lake's rim; the old bank RAISE stays
for ground below the water. Test: `RiverShapeTests.Shaped_Rivers_FourSeeds` (Slow; seeds 42/7/2026/31337 shaped vs a `-rivershape 0` map,
per class all / walled (canyon floor) / lake / biome border; report `Logs/TerrainGen/river_shape179.txt`). `FluidAtRestTests.RiverSeaLava_AreAtRest`
now also runs seeds 2026 and 31337 (2026's sea is reported, not gated: KNOWN_ISSUES "seed 2026 fossil stamp beside the sea", predates 179).
The route test is green on all four seeds (31337's ford). Shots: `python3 -I tools/river179.py Screenshots/179 <before dir> <after dir>`
(`<pose>_compare.png`, `viewer.html`), poses via `-worldsurvey 1 ... -wsposes pair_snow_river-plains,river,pair_desert_canyonlands,feature_mesa-4,pair_badlands_high-plateau`.
NOT changed: the renderer's flow map (`RiverFlow`, D8 directions per 8 m cell) -- ripples follow D8, not the curve. Result: HANDOFF 179.

**Task 180 (placement, first test, owner 2026-11-06 "trees are voxels"; GENERATOR CHANGE behind a flag, DEFAULT OFF): generated voxel trees, `-trees 1`.**
`Assets/TerrainGen/Trees.cs`: one tree per 6.4 m TREE CELL at most (`CELL_SHIFT` 6, root jittered to [8, 56) of the cell by an integer hash),
decided by `Trees.Build` from data the generator already has: `TreeTable.DensityPlane` (one byte per 8 m map cell from the world record:
landform x movement band, 0 on sea / route-corridor / signature-feature / Sheer cells; plumbed like the ore plane as `MapView.trees`), the root
column's own LOD-0 `ShapeField.Sample` (biome = `surfBiome`; cover slot x `SLOT_MUL` flat 16 / steep 6 / hollow 10 / high 8 / shore 0, a patch
variant x 10/16 = clearings; none in water, below the sea, on a stamp / ledge / lip), 4 height samples for the foot (> 45 deg across trunk + 2
voxels = none) and 4 for crown clearance. Density per biome slot `DENSITY` (per mille per cell): forest 800, desert 60 (cactus), snow 300,
jungle 950, savanna 110, badlands 0 -- no seed constant. FAMILIES (3 shapes per biome, every number from the cell hash, `Emit`): forest oak /
poplar / bushy fork; jungle buttressed emergent with umbrella crown / curved palm / dense round; savanna umbrella acacia / baobab / two-tier
acacia; snow spruce (4 cones) / tall pine / fir; desert saguaro / column / barrel. Primitives: tapered capsules (wood), ellipsoid BLOBs and
CONEs (leaves) with star-shaped edge noise (noise read on the surface point in the voxel's direction: no detached crumbs). New GenMats (palette
D, `tools/palette_defs.py` -> `gen-tg2-materials.py --write`; Items/Tools tier 0): leaves 55-58 (forest, jungle, savanna, pine needles; the
biome's flat cover colour in shade, snow's dark blue-green), woods 76-79 (forest, jungle, acacia, pine), cactus 93; `GenMat.IsTreeTrunk` /
`IsTreeLeaf` (chopping later: any trunk voxel cut -> rebuild the tree from its cell, drop the wood, remove top-down). RULE AT A VOXEL: trees
overwrite AIR only; every trunk primitive first, then every leaf one, trees in cell order (z, x); the first containing one wins.
WHERE (`TerrainGenChunkSource.Trees.cs`): ColumnJob / ShellColumnJob gather the trees of the cells round the footprint (`TreeScratch`, crowns
raise the column top so the air fast paths stay exact); VoxelJob / ShellVoxelJob end with `OverlayTrees` (coarse voxels test their sample
point: shell k = the 0.1 m voxel there); `PointVoxel` (FarQuery) applies the same rule. Shells > `Trees.MAX_SHELL` 5 (beyond 2.2 km on High)
and the far band draw no trees. `-trees 1` -> `TerrainSpec.treesOn` (`Trees` = v4 && on); `TerrainGenChunkSource.SetTrees` for tests.
Tests `TreeTests` (all Slow): `Chunks_Deterministic_Border_PointAndShellsAgree` (seed 42, a forest tree across a chunk border: ON twice same
bytes, ON vs OFF only air -> tree material on both sides, FarQuery points = chunk, shell 0 = Generate, shells 1-3 tree voxels = the 0.1 m
voxel at the sample; `Logs/TerrainGen/trees180/chunks.txt`), `FourSeeds_Rules_Sheets` (42 / 7 / 2026 / 31337, 1/16 of the world: record /
water / badlands rules, reach; 30 trees per family + 30 steep-foot trees voxelised on the LOD-0 ground, rejected if any voxel floats (not
6-connected to the ground), the trunk misses the ground or > 2 % of the crown / any trunk above its foot is in rock; sheets
`Screenshots/180/sheet_<seed>_<biome>.png`, `Logs/TerrainGen/trees180/rules.txt`), `Cost_OnVsOff` (dense bricks + gen time, LOD 0 and
shells 1-5, `cost.txt`), `Poses_DensestSpots` (Explicit: `-wsfixed` poses at the densest spots, `poses.txt`). Shots: `-lookreview
-worldsurvey 1 -wsposes fixed,range -wsfixed "$(cat Logs/overnight/180_poses.txt)" -trees 1|0`; `python3 -I tools/trees180.py`. Result: HANDOFF 180.

**Task 180b -- simple material rules, flag-off prototype (`-matrules 1`, default OFF).** Owner 2026-11-06 (memory simple-material-rules):
BIOME PAINTS EVERYTHING -- one palette row per biome (skin, soil, subsoil, stone, deep stone, cavern rock + flat / steep / hollow / high /
shore / rubble entries and their patch variants), the inputs only choose WHICH entry goes WHERE. `Assets/TerrainGen/GroundRules.cs` (header =
the rule list, first match wins: shore -> water bed -> steep (bare on a crest or the sunny side) -> wet bank (fresh water within 12 m, ground
<= 1 m over it) -> high (snow line, lower in shade) -> scree (ground > 40 deg within 20 m upslope; a route keeps its surface) -> dip -> crest
-> flat). Per column once (`GroundRules.Column`, last in `ShapeField.Sample`): 6 m slope / curvature, height over sea, sun side (+Z), the
map-baked shore field (`WorldMap.EnsureShoreField`, one pass per map), steepest ground upslope, route flag, one 300 m 2-D noise per biome;
per voxel only the depth below the GENERATED top (`Band`: skin 0.3 m, soil, subsoil, stone to 11-19 m, deep stone; cavern rock / underworld
stay absolute). Border: skin from the rock border warp in 25 m plan patches (a mud bank becomes a sand bank over ~60 m), stone from rockA.
Non-water fluids own their shore (`FluidShore`: lava -> magma crust, oil -> shale, ...), lining the caldera bed and every sealed pocket's
wall. Badlands: one red stone, no stripes. No LAYER_ROCKS / partner mix / rock A-B on this path (they stay for OFF). Ore hook for 181 noted
in the header. Edits never feed the rules (pit = original surface's bands; regeneration = seed + edit log). Runtime regrowth (grass over
exposed dirt, a new wet bank behind a dam) is out of scope: it would be one deterministic tick re-applying `ChooseSlot`'s skin to exposed soil.
Far band (`FarField` FillTg2Job), shells, `PointVoxel` and `UniformSamples` (brick skip) follow the same rules when on. `-matrules 1` ->
`TerrainSpec.matRulesOn` (`MatRules` = v4 && on); `TerrainGenChunkSource.SetMatRules` for tests. OFF = bit-identical (whole suite + goldens).
Tests: `MaterialRuleTests` (palette is data, conflict pairs, OFF untouched / ON keeps the ground, near == far slots to texel 22, depth
bands + pit, pocket lining, Slow `FourSeeds_AdversarialCases_AndTheTable` -> `Screenshots/180b/table180b.txt`), `MaterialRuleChunkTests`
(Slow: chunks deterministic + air/fluid unchanged + dig never changes beneath, `Shells_And_SnowCliff_AirAndFluidUnchanged`, `Cost_OnVsOff`
-> `cost180b.txt`, `CutAway_Pits` -> `Screenshots/180b/pit_*.png`; Explicit `Poses` -> `Logs/TerrainGen/180b/poses.txt`),
`BrickSkipTests.*_MatRules`. Shots: `-lookreview -worldsurvey 1 -wsposes fixed -wsfixed "$(cat Logs/overnight/180b_poses.txt)" -matrules 1|0`;
`python3 -I tools/matrules180b.py Screenshots/180b <ON dir> <OFF dir>` -> `Screenshots/180b/viewer.html`. Result: HANDOFF 180b.

**Task 181 -- resource deposits by rule (placement plan, owner-approved 2026-11-06 incl. batch 2; `-ores 1`, DEFAULT OFF).** `Assets/TerrainGen/OreRules.cs`
(header = the rules). The five-tier ladder and host layers are step 3's (`Ores.cs`: copper in stone, iron above sea - 100 m, gold below, cobalt
in cavern rock, emberstone in the underworld); OFF = step 3 byte for byte. Sources, all generator data, no seed constant: DEPTH (`Ores.BaseChance`);
BIOME x LANDFORM (`PairMul`, favoured x2) scaled by HARSHNESS = Danger biome tier + `Regions.ClassOf` (0-4): copper / gold x 16 14 12 10 8 /16 and
x1.25 on easy cells, x1.25 on mild biomes; iron x1; cobalt only harshness >= 3 (x 40 / 56 /16), emberstone only 4 (x 80/16); WALL VEINS: a column
with spacing-1 step > 1.5 (56 deg) carries thin tilted lenses (one per 64^3 cell at most, 0.4-0.6 m thick, 3.6-5.6 m long) down to step + 2 m below
its top, above the 5x5 cells' still water + 1 m, none on route cells; chance per biome (badlands x3), ore from a per-biome table (copper, gold,
iron; never latent); RICH: x2 deposits / veins on signature-feature cells. START (owner R1 (b)): cells of either play start's easy area + 128 m
round either start column: no veins, deposits only deeper than 6 m + step + 1 in a column with no opening / lip / stamp. LATENT (owner R2 (a)):
cobalt / emberstone are written as a TWIN per host rock (GenMat 26-29, 38, 39 = cobalt in each biome's cavern rock; 59, 94, 95 = emberstone in
magma crust / ash / obsidian); a twin's palette row IS its host's; `ContentModules/LatentOres.cs` resolves a twin to its host until
`world.state`'s stage reaches 1 (cobalt) / 2 (emberstone), then to the ore -- in `Tools` (hardness, tier, cost), `Items` (drop, name),
`MaterialPalette`, and the shader (`Raymarch.compute` `LatentResolve` on `_WorldStage`, a global set by `LatentOres.SetStage`, called by
`StreamManager` on world load). No ground rewritten, no save change, no frozen API touched. Plane: `OreRulePlane.CellPlane` (one uint per 8 m
cell) -> `MapView.oreCell` -> `ColumnDetail.oreCell` in ColumnJob / ShellColumnJob / PointVoxel (`TerrainGenChunkSource.Ores.cs`); brick-skip
proofs: `OreMay` with the rule chance + `VeinColumn`. The far band's top never reaches rock and its dug floors see no plane: no rule ore there.
DevMap / the ore and tool demos build their own columns: no rule ore there. Tests `OreRuleTests` (`Twins_ContentResolves`; Slow
`Sites_OnVsOff_Deterministic_PointShellsSkipAgree_Images` -> `Logs/TerrainGen/ores181/sites.txt`, `poses.txt`, `Screenshots/181/face_*.png` /
`cut_*.png`; Slow `FourSeeds_StartHidden_AndTable` -> `table.txt`), `BrickSkipTests.*_Ores`. Shots: `-lookreview -worldsurvey 1 -wsposes fixed
-wsfixed "$(cat Logs/TerrainGen/ores181/poses.txt)" -ores 1|0`. Result: HANDOFF 181.

**Task 182 -- material rules: two fixes behind `-matrules 1` (default still OFF).** Fixes 180b's two defects; no new flag. (1) DEEP STONE
BY ABSOLUTE HEIGHT: `GroundRules.Band` is now 0 skin / 1 soil / 2 subsoil / 3 rock, and WHICH rock is absolute (`GroundRules.RockAt` /
`RockRange`): the biome's stone down to the generator's own stone / deep-stone line (`GroundRules.DeepTop` = sea -
`DetailField.STONE_BOTTOM_BELOW_SEA` + the wavy deepOffset, the old path's `LayerRock` line), deep stone under it, then cavern rock and the
underworld. `ColumnSample.mrDeep` and `DEEP_BASE/AMP` are gone; a tall face is one stone. (2) SHELL EXPOSURE: `ColumnSample.mrExpose`
(ushort, 0 outside shells) = max(2^k, h - the lowest of the 4 coarse neighbours), set by `ShellColumnJob.Expose` (shift > 0, MatRules on;
edge neighbours sampled); a coarse voxel shallower than it is bared on top or a side and is band 0 (skin); deeper voxels keep their own
depth's band. At 2^k = 1 it would be a no-op (drop to a fine neighbour = stepAtTop <= SHEER_STEP = SKIN), so chunks are untouched. Material's
brick zone and `GeologyRock` return the skin for band <= 2 (only an exposed coarse voxel gets there). Tests: `MaterialRuleTests.
TallFace_IsOneStone_NoDeepStoneAboveTheAbsoluteBoundary` (sheer faces up to 41 m on 3 seeds -> `Logs/TerrainGen/180b/tallface182.txt`),
Slow `MaterialRuleChunkTests.Shells_BaredVoxels_WearTheSkin` (shells 3 / 5 round the 9 sites; also pins mrExpose ->
`shell_bared182.txt`); `ShellChunk.Column/ColumnStep` expose the voxel pass's own column to tests. Shots: `Logs/overnight/182_shots.sh`
(180b's 8 poses, ON), viewer `Screenshots/182/viewer.html` (182 ON | 180b ON | OFF). Result: HANDOFF 182.

**Task 183 -- fluid pockets at rest (placement plan step 4, owner-approved 2026-11-06 incl. batch 2; `-fluidpockets 1`, DEFAULT OFF).**
`Assets/TerrainGen/FluidPockets.cs` (header = the rules). SURFACE pockets: one candidate per 51.2 m plane cell (512 voxels, `>> 9`), a small
elliptical basin (integer ellipse, r 1.4-5.2 m, fluid 0.4-2 m deep) on dry flat ground; fluid by the world record + column sample: volcanic
landform -> lava (1 in 4 acid), badlands -> oil (the tar seep), desert -> quicksand, jungle valley / wetland -> sludge (bog), other jungle ->
honey, a valley (>= 4 m below the +-120 m regional mean) or wetland in forest / savanna / snow -> water. Keep-outs (every record cell under
basin + rim): not sea, >= 32 m from lake / river cells, Walk band, no route corridor, no signature feature, not the start zone (either
start rule's easy area + 128 m); 24 base samples on the rim: dry, stamp / lip / river-free, within 2.4 m; L = their lowest - 1. AT REST BY
CONSTRUCTION in `ShapeField.Sample` (`FluidPockets.Apply`, every path: LOD 0, shells, FarQuery, far band): footprint height := L - depth,
fluid (height, L]; rim (an 8-neighbour of the footprint) height := max(h, L + 1); a thin LINING the fluid never reacts with (water mud,
acid sinter, lava magma crust, oil shale, sludge mud, honey ochre band, quicksand packed sand) on every solid voxel that can touch the fluid;
caves capped under L - D - 0.8 m (`riverFloor`); stamp openings off. The fluid's code rides in `ColumnSample.waterKind` (0 water, 1 lava as
before, else the GenMat; `FluidPockets.FluidOf`); far band, minimap and `DetailField` read it. DEEP pockets (the old sealed ellipsoids, same
shape and place): rule fluid (`DeepFluid`: volcanic land -> lava; cavern deep half -> lava; cavern jungle sludge / desert + badlands acid /
snow water / else mix; stone layers badlands oil, desert oil / water, jungle honey / water, snow water, else water / some oil) and a lining
(`DeepLining`, acid -> sinter: the old acid pocket dissolved its wall). Steam: not generated (the lava + water reaction makes it). Plane:
`WorldMap.EnsurePocketPlane` (~0.35 s per world) -> `MapView.pocket` in ColumnJob / ShellColumnJob / ShellFill / FarQuery / FarField
(`TerrainGenChunkSource.Pockets.cs`). OFF = every byte as before. Tests `FluidPocketTests` (`Rules_LiningsDoNotReact`; Slow
`FourSeeds_Plane_Table_KeepOuts` -> `Logs/TerrainGen/pockets183/table.txt`; Slow `Sites_AtRest_PathsAgree_Adversarial` -> `sites.txt`,
`poses.txt`, `Screenshots/183/cut_*.png`: CPU fluid oracle settle 150 ticks + negative controls, FarQuery / shells / far texels / brick skip,
forced sites on a cliff and a canyon side). Shots: `PRESETS=high EXTRA="-worldsurvey 1 -wsposes fixed -wsarms native -wsfixed
$(cat Logs/overnight/183_wsfixed.txt) -fluidpockets 1|0" ./run-look-review.sh`. Result: HANDOFF 183.

**Task 184 -- town building kit, KIT ONLY (placement STOPPED FOR OWNER: no town / village sites exist).** `Assets/TerrainGen/BuildingKit.cs`:
parts (stone foundation to the ground, floor, 0.2 m wall ring, 3x3 corner posts, door gap 1 x 2.1 m from the floor, windows with a 1 m sill,
wood frames round every opening, 45 deg gable roof 0.2 m thick with 0.4 m eaves, gable-end walls) and 3 shapes from them: HUT 4 x 4 m (1 room),
HOUSE 9 x 5 m (2 rooms + inner door), HALL 13 x 7 m (double door, wall posts at the thirds). A building is a pure integer function
(shape, rot 0-3, biome, origin, floor y, foundation depth) -> part -> GenMat (`Role`/`Voxel`), ready for the Trees.cs overlay pattern
(ColumnJob / ShellColumnJob / PointVoxel); `BuildingFootprint` (integer box + id) and an EMPTY `WorldRecord.BuildingList` hold footprints
for NPCs (the record is never saved: no save-format change). Style by biome rule, existing GenMats only (header lists them: biome tree wood,
the biome's own rock, timber / adobe / stone walls, thatch / slate / clay-tile roofs). NOT placed anywhere and no `-towns` flag yet: B7
(the site planner) was never built, so nothing reserves town sites. Tests `BuildingKitTests` (6: sealed room + negative control, player
walks from outside through the front door to every room and not through windows or a shut inner door, roof over every column, no floating
voxel, rotations agree + footprint tight + door on FrontDir, every material solid / static / coloured / GenToEngine-mapped); report
`Logs/TerrainGen/kit184/kit.txt`; sheets `Screenshots/184/kit_sheet.png` (rows hut/house/hall, columns forest snow jungle savanna desert
badlands) and `kit_cutaway.png` (roofless, forest | desert). Result: HANDOFF 184.

Task 184b (test-suite trim, owner 2026-11-07: fast <= 3 min, all <= 15 min): `run-editmode-tests.sh` has THREE tiers --
`fast` = `!Slow` (every change), `SUITE=all` = `!Nightly` (every generator / rule test at least once; generator changes),
`SUITE=nightly` = everything (~1 h, before a merge to main only; also copies `TestResults_nightly.xml`); `FILTER='<testFilter>'`
runs only those tests (regex: write `(42)` as `\(42\)`), `EXTRA="<unity args>"` passes more. A `[Category("Nightly")]` test is
always also Slow. Nightly holds the shell fills (EditOpRedraw Shells1to7 774 s, ShellFill, ColumnReuse WalkingTwin, BrickSkip
Shells/Chunks, ShellChunk CoarseFirst ...), the long fuzzers, the two cost reports (`Cost_OnVsOff`), every engine test that took
>= 2 s, and seeds 7 / 2026 of FluidAtRest / LakeShore / LakeLevel / OreRule (42 + 31337 stay in all). OreRuleTests.FourSeeds is one
case per seed (`table_<seed>.txt`). Exact cuts: `FastAssert` (CoreEngine/Tests) and guarded per-voxel asserts (`if (x != y)
Assert...` -- same check, same message, no NUnit call when it passes): MaterialRuleChunk Chunks_Deterministic 52.9 -> 6.2 s,
OccupancyMask/AirMip 15-20 s -> 1.6-4.8 s (two AirMip tests back in fast), Tree Chunks 9.8 -> 6.3 s. The Editor already runs
managed code in Release (EditorPrefs ScriptDebugInfoEnabled = 0): `-releaseCodeOptimization` measured no gain. Result: HANDOFF 184b.

**Task 185 -- world gen C7, stamps that sit in the land (`TerrainSpec.StampFit`, v4 only; DEFAULT ON since task 187a,
`-stampfit 0` = `stampFitOff` = every byte as before).** Rules in `Stamps.cs` (header "TASK 185") and `ShapeField.Ledge`, each exact on the stamp's own footprint:
CAVE MOUTH placed only where its tube is under cover (`Stamps.FitCave`): stations every 5 m along the tube (axis and both sides), the
lowest S can put the column (map - `DETAIL_AMP_SUM` 94.3 x rough - 1, minus `RELIEF_BAND_BOUND` on a face) stands `CAVE_ROOF` 2 m over the
tube top from <= `CAVE_MOUTH_MAX` 40 m out to its end; the hashed axis first, then its 3 quarter turns (`ColumnSample.stampRot`, read by
`Carve3D`), none -> no cave. LEDGES: the shelf top is a rock-layer boundary (`LedgeFitTop`, flat but for LayerWave) and 3.5 m thick
(`LEDGE_FIT_THICK`). GEYSER BASIN: sinter terraces of 1.5 m (`GEYSER_STEP`) = the map ground rounded up below the vents' level, outline by
the ground (<= 2 m over / <= 40 m under the vents, reach 90-120 m, longer downhill, +-12 % wobble); a pool keeps only columns whose 8
neighbours share its terrace. HOOPS only on a rise (>= 5 m over the +-120 m ring mean) or in rugged rock (map rough >= 0.8). SEA KEEP-OUT
(KNOWN_ISSUES seed 2026 fossil): no cave / fissure / skull cave / arch / hoop / rib opening within 2.5 cells of a sea cell (`NearSea`).
Tests `StampFitTests` (Slow: caves 4 seeds + a forced downhill trench site; ledges 4 seeds + the tallest shelf's 64x64 window; geyser
4 seeds + the basin moved onto a 56-degree slope, pools via FluidAtRestTests' tile job; fast: hoops 4 seeds + the flattest plain,
DETAIL_AMP_SUM) and `FluidAtRestTests.Sea_AtRest_StampFit_Seed2026` (tile (190,545) OFF = negative control). Reports
`Logs/TerrainGen/stampfit185/`. Shots: `PRESETS=high EXTRA="-worldsurvey 1 -wsposes fixed,cave_in -wsarms native -wsfixed
$(cat Logs/overnight/185_wsfixed.txt) -stampfit 1|0" ./run-look-review.sh`. Result: HANDOFF 185.

**Task 186 -- the simple material rules are the DEFAULT (owner 2026-11-07, Q-M (a)).** `TerrainSpec.matRulesOn` became `matRulesOff`
(the `riverShapeOff` pattern): 0 = `GroundRules` (tasks 180b + 182), so every v4 spec -- play, rigs, capture runs, tests -- paints by the
rules; `-matrules 0` (`TerrainGenChunkSource.MatRulesArg` now defaults to true; chunk source + `FarField`) sets 1 = the 177-179 cover +
layered rock, byte for byte. No SerializeField / scene-builder copy exists. Effective value in the player log:
`[StreamManager] tg2 chunk source: map v4, material rules ON (default)` and the `[FarField] tg2 ...` line's `material rules ON`.
Tests pinned to the path they describe (assertions unchanged): `CoverTests` and `BiomeBlendTests` (task 177 cover / task 122 surface)
set `matRulesOff = 1`, taking the default spec's shared map (the rules never change the map, no extra build); `MaterialRuleTests` /
`MaterialRuleChunkTests` arms use `matRulesOff`; `BrickSkipTests.*_MatRules` became `*_MatRulesOff` (the plain proofs now cover the rules).
`FluidPocketTests.Sites_AtRest_PathsAgree_Adversarial` (183, `-fluidpockets 1`) told the lining by MATERIAL id; the rules paint the same
ids beside a fluid (sludge shore = mud, quicksand = packed sand, wet bank = mud), so shells 1-2 vs the 0.1 m voxel failed at 3 sites on rule
paint, not on the pocket. It now tells the lining by POSITION (the column's pkLineLo..pkLineHi, or DeepPocket's shell) -- stricter at every
lining voxel; fluid voxels compared as before.
Shots: `Logs/overnight/186_shots.sh` (3 of 180b's poses: `oasis_or_swamp` desert river, `cliff_snow`, `beach`; default vs `-matrules 0`).
Result: HANDOFF 186.

**Task 187 -- world gen B7, the town / village SITE PLANNER (`-sites 1` = `TerrainSpec.sitesOn` / `Sites`, v4 only, DEFAULT OFF; owner
Q-S (a) 2026-11-07).** `Assets/TerrainGen/Sites.cs`: integer rules on the map + record (no seed constant, no place constant), records only --
NO terrain change (the flatten rule is stored, not applied), so ON and OFF worlds are byte-identical and only `WorldRecord.SiteList` + the
record's site bytes (ids >= 16 = SiteList index + 16, on each disc's cells) differ. `WorldMap.EnsureSites()` runs it once (chunk source,
`[Sites] plan .. ms` + one `[Sites] #id` line per site in the player log); tests call `Sites.Plan(Sites.FromMap(map))` (never on a shared
map). Every site is a disc (town R 64 m range <= 8 m, frontier 48 m / 8 m with <= 1/4 Scramble, village 32 m / 3 m, outpost 16 m / 2 m):
all land, dry, >= 16 m from lake / river, 32 m clear of sea and signature features, not in the start zone (128 m round both PlayStart
columns), not on a stamp keep-out disc, not on another site + 32 m; Walk band only. Towns: one per easy area, best disc; target land /
90 k cells (3 on the medium island). Frontier: hard ground (Volcanic landform or Snow biome) within 256 m of its edge, higher danger scores
better; 1 + towns / 4. Villages (fresh water <= 6 steps ~100 m, danger <= 3) and outposts (the rest, <= 16 steps) within 2 km of a route,
route distance a score term; land / 27 k cells together (~10), villages up to 6 in 10. GROUND CHECK on the real columns (ShapeField, 2 m
grid): range <= max + 3 m and THE STEP RULE (no 2 m neighbour step steeper than Scramble 30 deg, `StepTooSteep`: a canyon terrace's cliff
edge is invisible on the 8 m map; it removed every terrace village). Each site records its biome x landform pair, nearest route + join point
+ spur length (no route re-searched: `RouteList` unchanged), pad level (median map height) / blend R/2 / cut / fill. Tests `SiteTests`
(fast: `Adversarial_FewFlatCells_OnlyTheFlatPatches` -- a 4 km Climb island with four flat patches, no-flat and Scramble negative controls;
`GroundCheck_StepRule_WallInDisc_NoVillage` -- a 3 m wall the map cannot see; Slow: `FourSeeds_EveryRule_CountsTable` -- an independent
checker of every rule, `Logs/TerrainGen/sites187/table.txt`, `map_<seed>.png`, `sites_<seed>.txt`, `poses.txt`). Labels:
`python3 tools/sites187.py` -> `Screenshots/187/sites_<seed>.png`, `sites_sheet.png`. Shots: `Logs/overnight/187_shots.sh` (seed 42 town /
village / frontier, eye level, `-sites 1`). Next: BuildingKit on the sites behind `-towns 1`. Result: HANDOFF 187.

**Task 187a -- stamps that sit in the land are the DEFAULT (owner 2026-11-12, Q-F (a); task 186's pattern).** `TerrainSpec.stampFitOn`
became `stampFitOff`: 0 = task 185's fitted stamps (cave mouth under cover, cap-rock ledges, terraced geyser basin, hoops on a rise, sea
keep-out), so every v4 spec -- play, rigs, capture runs, tests -- fits; `-stampfit 0` (`TerrainGenChunkSource.StampFitArg` now defaults
to true; chunk source + `FarField`) sets 1 = the pre-185 stamps, byte for byte. No SerializeField / scene-builder copy exists. Effective
value in the player log: `[StreamManager] tg2 chunk source: map v4, material rules ON (default), stamp fit ON (default)` and the
`[FarField] tg2 ...` line's `stamp fit ON`. Tests: `StampFitTests` / `FluidAtRestTests.Sea_AtRest_StampFit_Seed2026` take the default spec
as ON and pin OFF with `stampFitOff = 1` (assertions unchanged; the map ignores the flag, so they take the default spec's shared map);
no other test encoded the old stamp shape. Site planner (187) unaffected by construction (map + record only) and its tests stay green.
Shots: `Logs/overnight/187a_shots.sh` (185's cave42_over, wetland_basin ledge, river geyser basin; default vs `-stampfit 0`). Result: HANDOFF 187a.

**Task 188 -- the shell pools no longer overflow with trees: COMPACT 2-value shell bricks (owner 2026-11-07 Q-T (a); trees stay `-trees 1`, default OFF).**
CAUSE (`TreePoolTests.Pools_ForestPose` / `Pools_JunglePose`, Explicit: every chunk shells 1-5 store at 180's forest / jungle pose, seed 42 High,
trees OFF / ON / ADVERSARIAL cover = `TerrainSpec.treesOn` 2 + biome, test-only, every tree cell passing the physical rules gets a tree;
`Logs/TerrainGen/trees188/pools_<pose>.txt`): canopies are a second surface over the ground, so trees add dense bricks, and 92 % of those hold
exactly two values (leaf or wood + air) yet took a full 512 B body. 180's FULL was at the JUNGLE pose (the `[shells]` line prints BEFORE its
`WSPOSE` line): trees ON took shell 1 / 2 to 120 / 117 % of PoolCap. FIX (`Mirror/ShellBrickPack.cs`, an Appendix A addition, EXACT): a dense
shell brick whose voxels take <= 2 values is stored as 17 uints (a | b << 8, then a 512-bit plane), 7 packed into one 128-uint body, handle
bit 30 = COMPACT (low 30 bits = shell-local uint offset); full bricks unchanged. Decided per brick from its own voxels (terrain too), no cap or
constant. `ShellStore.Store` classifies (Burst `ClassifyJob`) and stages (Burst `StageJob`, replaces the managed copy); the pool unit stays the
body (`BricksUsed`, `PoolCap`, `UploadBytes`' `dense` = bodies; new overload returns full / compact bricks). Readers: `Raymarch.compute`
`ShellBrickVoxel` (both shell lookups), `ShellRayReference.IView.VoxelOf` (default = plain body; shadow views use `ShellBrickPack.ShadowVoxel`),
`ShellValidator.CompareShadow`. `-shellpack 0` (diagnostic) = every dense brick a full body, as before 188. Tests: `TreePoolTests.CompactBricks_
StoreIsByteExact_PoolCountsBodies` (fast: 1-, 2-, 3+-value bricks byte-exact through a real store; refusal by bodies; redraw / alias / abort
free them); `EditOpRedrawTests` replace test and `ShellSkyExitTests.Store_RaisesTheBound` now count bodies (the latter's refused chunk uses
3-value bricks). Player: the capture `[pools]` line after each `[shells]` (bodies / pages used/peak/cap, refusals, pack ON/OFF). Shots:
`Logs/overnight/188_shots.sh` (forest, jungle, forest_far, jungle_far; `-trees 1`, `-shellpack 0` vs default). Result: HANDOFF 188.

**Task 188a -- `-matvariant 0|1`: the material rules without the random variant spray (owner 2026-11-12 (a): "just randomly spraying it is weird ... like blotches"; DEFAULT unchanged = 1, the owner picks from the pictures).**
`TerrainSpec.matVariantOff` (read through `MatVariant`): 1 (`-matvariant 0`) = `GroundRules.Column` gives every column its slot's MAIN palette
entry (`mrSlot` never carries VARIANT_BIT); 0 (default, `-matvariant 1`) = today's `VariantNoise > VARIANT_ABOVE - 0.15 x BiomeNoise` spray
(~22 % of land columns), byte for byte. Rule-made materials (shaded-steep ice, frozen / mud / green banks, scree, bare sunny faces) are slot
MAIN entries and stay. One switch point, so near voxels, `TopMaterial` (far band, minimap) and shells all agree; `FarField` sets the far
band's own spec from the arg like `-matrules`. Rules path only (`-matrules 0` ignores it); the map and record ignore it; TREES are unchanged
(`c.cover` keeps the noise patch's VARIANT bit, so tree density patches -- `Trees.VARIANT_MUL` -- are identical with either setting). Where a
variant entry was BARE, the slot's patches lose bare rock (soil depth follows the main entry). Tests `MatVariantTests`: `-matvariant 1` FNV
(height, slot, cover, soil depths, top material, top 4.8 m of Material; seeds 42, 7) == the pre-188a pin; `-matvariant 0` zero variant
columns, same slot / cover / ground, skin == main entry; near == far slots at texel 4 / 16; Slow `FourSeeds_ShareOfGroundPerMaterial_
BeforeAfter` writes `Logs/TerrainGen/188a/share188a.txt` (surface share per material per biome, 4 seeds, and the palette's variant-only
materials). Shots: `Logs/overnight/188a_shots.sh` (187's frontier + town poses, 180's desert + savanna flats, two seed-42 forest river banks;
default vs `-matvariant 0`). Result: HANDOFF 188a.

**Task 189 -- world gen C6, landform signatures at walking scale (`-landsig 1` = `TerrainSpec.landSigOn` / `LandSig`, v4 only, DEFAULT OFF; the owner picks from the pictures).**
`Assets/TerrainGen/LandSig.cs`, applied in `ShapeField.Sample` before the face relief: each landform gets a 20-200 m shape from the
landform weight and the ground's own data, never the biome. DUNES: the map's own dune train (`WorldMap.DuneField`, 160 m, the seed's
wind) sharpened IN PHASE at the column (`DUNE_GAIN` 0.5 of its height as a straight stoss, sharp brink and straight slip face); the 16 m /
8 m generic octaves are damped in dune fields too (wind-sorted ground). CANYONLANDS: benches on the rock layers (`DetailField.LayerBoundary`,
flat tread + `BENCH_RISER` riser) and slot canyons (zero lines of a noise stretched along the 200 m regional downslope, flat floor, steep
walls, in patches, drawn at footprints <= 8 voxels). ALPINE: ridgelines and side valleys (a ridged noise stretched down the regional slope,
scaled by the slope, mostly carved). Stretched noise uses 4 FIXED frames (0/45/90/135 deg) blended by orientation (no rotating frame, no
shear). In every signature landform the 64 m / 32 m generic octaves give way (`MESO_GIVE` 0.75). WHERE: a per-cell weight plane
(`WorldMap.EnsureLandSigPlane`, 3 bytes per map cell, built only when the flag is on): `Regions.Weights` x a fade (16-48 m chamfer) from sea,
lake, river, easy areas and route corridors; bilinear, so heights stay continuous. Plumbed through every MapView (near chunks, point
queries, shells, shell fill, FarField, FarQuery); `BandLimitBound` widened by `LandSig.BandBound`. Tests `LandSigTests` (12): OFF == the
188a FNV pins with the plane built, and a landform-column FNV; dune slip face downwind, ~3x the stoss, < 45 deg; dune phase == the map's
`DuneField` (to 1 mm); oriented noise continuous across bins and the 180-deg wrap; slots / ridges run down the slope; bench continuous at
layers; plane 0 on keep-out; adversarial one-cell patch and biome border; Slow band limit, no new needle (`StampTests.NEEDLE_DROP`), and
`ShapeStatistic_PerLandform_FourSeeds` -> `Logs/TerrainGen/189/shape189.txt` (OFF vs ON per landform, 4 seeds). Shots:
`Logs/overnight/189_shots.sh` (task 173's `pair_jungle_dune-sea`, `pair_desert_canyonlands`, `range_200up`; default vs `-landsig 1`).
Result: HANDOFF 189.

**Task 190 -- TOWNS ON THE SITES (`-towns 1` = `TerrainSpec.townsOn` / `Towns`, also turns `-sites 1` on; v4 only, DEFAULT OFF; owner
2026-11-12 (a)).** (1) SPACING (`Sites.TownSep2`, task 187's plan changes for `-sites 1` too): no town inside another town's FAIR SHARE of the
land (a disc of area land / towns, radius sqrt(land / (pi towns)), integer, pi = 355/113; ~1.35 km on the 4 seeds); an easy-area town
closer is dropped and the missing towns go on WALK ground anywhere (TIER 2: every town disc rule, whole disc Walk, ground check, within
ROUTE_FAR of a route corridor); none fits = fewer towns (`Plan(.., spread: false)` = 187's plan). (2) THE FLATTEN (`Assets/TerrainGen/Towns.cs`,
`Towns.Apply` in `ShapeField.Sample` after the pockets, so every path agrees): disc = the pad (187's stored median), ring R/2 blended back by
an integer smoothstep on d^2 (rounded, so the ring edge IS the natural ground); the ring stops short of sea / lake / river / feature cells and
half the gap to another site (`BlendWidth`); ring columns with still water, a river lip, a stamp or ledge opening are never moved; caves only
6 m (`CAVE_CAP`) under the new ground; no tree (`ClearCells` on the tree density plane) and no fluid pocket (`FluidPockets.StartZone`) in a
disc + ring. (3) BUILDINGS (`Towns.Layout`, BuildingKit 184): programs town 1 hall + 8 houses + 8 huts, frontier 1 + 4 + 4, village 3 + 3,
outpost 2 huts; the hall at the centre (door toward the route join), the rest on rings from an 8 m plaza in 1 m steps x 32 integer
directions (start = a hash of the site centre), door facing the centre; a slot = box (eaves incl.) 2 m inside the disc, 4 m (`GAP`) from every
box, every map cell under it dry land, not sea / lake / river / feature / route corridor / stamp / start cell, natural ground passing 187's
step rule; floor = pad + 1, 0.6 m foundation. `WorldRecord.BuildingList` = the footprints (id = the site id). PLANE (`WorldMap.EnsureTowns`,
`MapView.town`, int[]: sites + buildings, ~3.7 KB): LOD 0 / point query = the exact kit part (`Towns.VoxelAt`; PointVoxel first, VoxelJob
`OverlayBuildings` after the trees), shells = `Towns.CoarseRole` (the sample point's part if solid, else the highest solid part on the cell's
stride-2 sub-grid: walls / roofs are >= 2 voxels, so far houses keep them; shell 1 is exactly the sample point); column tops raised (air fast
paths exact; shells + 2^shift); far band = flattened heights only. Engine: `TerrainGenChunkSource.Towns.cs` (`TownsArg`, `SetTowns` for tests,
`TownArray` also used by FarField). Player log: `[Towns] plan + layout .. ms: ..`. Tests `TownTests` (fast: `Spacing_Adversarial_OneCorner_
FewerTowns` -- one Walk corner with 3 easy areas -> 1 town of 2, negative control 2, + a far Walk patch -> tier-2 town there;
`Layout_Adversarial_RouteWallBlocked`; `Flatten_OffPins_OutsideIdentical` -- 188a pins with the plane in the view, outside-column FNV ON ==
OFF, disc at the pad, ring between; Slow `FourSeeds_TownsTable_Maps_Poses` -> `Logs/TerrainGen/towns190/table.txt`, maps, plan sheets,
`poses_<seed>.txt`), `TownChunkTests.Hall_ChunksPointShellsAgree_PoolCost` (LOD 0 = kit, ON vs OFF only in discs + rings, FarQuery = chunks,
shell 0 = Generate, shell 1 exact, shells 2-3 every cell with a part shows one, no hall column holes, dense bricks ON vs OFF ->
`chunks.txt`); `SiteTests` checker knows tier 2 + the spacing. Labels: `python3 tools/towns190.py` -> `Screenshots/190/towns_<seed>.png`,
`spacing_7_before_after.png`, `plans_<seed>.png`. Shots: `Logs/overnight/190_shots.sh` (per seed town / village eye + town from 150 m); the world survey now prints task 188's `[pools]` line after each pose's `[shells]`.
Result: HANDOFF 190.

**Task 190a -- materials by the rules only, no random variant spray, is the DEFAULT (owner 2026-11-12, Q-V (a); tasks 186 / 187a's pattern).**
`TerrainSpec.matVariantOff` became `matVariantOn`: 0 = task 188a's no-spray look (every column its slot's MAIN palette entry), so every
v4 spec -- play, rigs, capture runs, tests -- has no spray; `-matvariant 1` (`TerrainGenChunkSource.MatVariantArg` now defaults to false;
`FarField` sets the far band's spec the same way) = the pre-190a spray, byte for byte. Trees unchanged (`c.cover` keeps the noise VARIANT
bit). The effective value is logged by `StreamManager` ("material variant spray OFF (default)") and `FarField`. No SerializeField carries it.
Tests: `MatVariantTests` pins 188a's FNV (`PIN_42` / `PIN_7`) on the explicit `-matvariant 1` arm and the new default on `PIN_DEFAULT_42`;
`LandSigTests.Off_IsTodayByteForByte` and `TownTests.Flatten_OffPins_OutsideIdentical` compare their 188a pins on a `-matvariant 1` copy of
the OFF spec. Shots: `Logs/overnight/190a_shots.sh` (188a's desert flat, savanna flat, forest bank a; default vs `-matvariant 1`). Result: HANDOFF 190a.

**Task 190b -- landform signatures are the DEFAULT (owner 2026-11-12, Q-W (b): dunes as they are; tasks 187a / 190a's pattern).**
`TerrainSpec.landSigOn` became `landSigOff`: 0 = task 189's signatures (dunes, canyonland benches + slot canyons, alpine ridges), so every
v4 spec -- play, rigs, capture runs, tests -- has them; `-landsig 0` (`TerrainGenChunkSource.LandSigArg` now defaults to true; chunk source
+ `FarField`) sets 1 = the pre-189 columns, byte for byte. `WorldMap.Build` now runs `EnsureLandSigPlane` whenever `spec.LandSig` (else
`ShapeField.Sample` would quietly take the OFF path for a map built without the plane), so every map a default spec builds carries the
plane (1024^2 x 3 B = 3,072 KB; `WorldMap.LandSigBuildMs`, 34 ms in the player). No SerializeField carries it. Effective value in the
player log: `[StreamManager] tg2 chunk source: ... landform signatures ON (default; plane 3072 KB in 34 ms)` and the `[FarField] tg2 ...`
line. Tests: `LandSigTests` builds its world from the default (ON) spec, `off` = `landSigOff = 1`; 188a's pins stay on OFF; new
`Default_IsOn_Pinned` (`PIN_ON_42` 0x5647ec7f / `PIN_ON_7` 0x08a2a095). `MatVariantTests` pins 188a on `-matvariant 1 -landsig 0`,
190a's `PIN_DEFAULT_42` on `-landsig 0`, today's default on `PIN_DEFAULT_190B_42`; `TownTests.Flatten_OffPins_OutsideIdentical` pins on
`-landsig 0` (towns run on the ON default, whole suite green). Shots: `Logs/overnight/190b_shots.sh` (189's canyonlands + range_200up;
default vs `-landsig 0`), `Screenshots/190b/viewer.html`. Result: HANDOFF 190b.

**Task 191 -- placement preview, ALL switches ON vs OFF (pictures only, owner 2026-11-12; no default or generator change).** ALL ON =
`-trees 1 -ores 1 -fluidpockets 1 -sites 1 -towns 1` on today's defaults (material rules, stamp fit, landform signatures ON; variant spray OFF);
ALL OFF = the defaults. One build, world survey (`-worldsurvey 1 -wsposes fixed -wsfixed <poses> -wsarms native -lookwait 35`, PRESETS=high),
seeds 42 (10 poses) and 7 (4), plus `-trees 1` alone (forest, forest_far) and `-towns 1` alone (town eye / 150 m); the 400 m wide shots again
with `-lookwait 90` (35 s leaves shells 2-3 empty = no distant trees). Scripts `Logs/overnight/191_shots.sh`, `191_wide.sh`,
`191_wide_retake.sh`; logs `Logs/overnight/191/`; per-run `lookreview_*.txt` and the flip viewer in `Screenshots/191/` (Space cycles
ON / OFF / switch alone). Capture change: the world survey prints a `[mem]` line (`MemoryLedger.Step14Line`: CPU / GPU body pool peak,
refused, process now / peak) after each pose's `[pools]` line. Result: HANDOFF 191.

**Task 192 -- trees look unique, with OPEN crowns (owner 2026-11-12: "very generic and not unique ... the density of the leaves is too much"; trees stay `-trees 1`, default OFF).**
`Assets/TerrainGen/Trees.cs`: no more "3 families x jitter". `Pick` draws EVERY number of a tree from its cell hash inside RANGES its biome and
cover slot set (age: 15 % saplings x0.42-0.6, mature x0.85-1.05, 18 % old x1.05-1.15 with thicker trunks and an extra limb; slot scale steep/high
x0.85, hollow x1.1): forest broad deciduous (H 10-16 m, crown radius 0.24-0.4 H, 3-5 limbs x 2-3 twigs; conifers mix in on HIGH 50 % / steep 20 %),
jungle tall emergents (H 11-20 m, crown base rising with height, buttress on old / tallest) + palms (hollow 35 %, flat 15 %), savanna flat-topped
acacias (crown radius 0.45-0.65 H, flat pads) + 12 % bottle-trunked giants, snow conifers (spruce-to-pine continuum q: crown base, radius, 3-8
lobed whorl tiers + a bark limb under each), desert cacti (arms 0-3 by height, 22 % barrels). `EmitLook` grows a SKELETON -- trunk -> limbs ->
twigs (capsules, wood, radius >= 1 voxel so 6-connected) -- and puts the leaves as CLUMPS (lumpy blobs, edge noise 0.3) on the twig ends, the
clump size from the fill target; sparse crowns put clumps on the upper shell (open interior), dense ones also inside. Trees on slopes lean out
over the foot's own downhill gradient (the 4 foot samples, no extra sample). One leaf GenMat per tree, a shade picked per tree from the existing
leaf GenMats (forest 60 % forest / 25 % jungle (dark) / 15 % savanna (olive) leaves; jungle 70/30; savanna 70/30; conifers pine needles): no new
materials. `MAX_PRIMS` 14 -> 28 (tree scratch: a shell-5 chunk's worst-case prim array doubles, ~5 -> ~10 MB per shell scratch). FLAGS:
`-leaffill 30|45|60` (`TerrainSpec.treeLeafFill`, measured leaf fill = leaf voxels / the ellipsoid (cone for conifers) inscribed in the leaves'
box; default `Trees.DEFAULT_LEAF_FILL` 45; calibrated so 30 / 45 / 60 measure about 32 / 47 / 59 %, task 180's trees 60-81 %),
`-treestyle 0` (`treeStyle180`, diagnostic: task 180's families byte for byte). `TerrainGenChunkSource.SetTreeLook(fill, style180)` for tests.
Tests: `TreeTests.Look_FillSkyUniqueness_Sheets` (Slow; seeds 42 / 7, per biome the 30 trees nearest its densest block, arms 180 / 30 / 45 /
60: fill, sky through the crown, "twin" = mean side-silhouette IoU with the most similar of 12 neighbours, <= 2-value bricks, prims; every new
arm passes the physical rules; `Logs/TerrainGen/trees192/look.txt`, sheets `Screenshots/192/look_42_<biome>.png` = 12 neighbours, rows 180 /
30 / 45 / 60), `FourSeeds_Rules_Sheets` and `Chunks_Deterministic_Border_PointAndShellsAgree` now write to `trees192` / `Screenshots/192`
(task 180's sheets stay in `Screenshots/180`), `Poses_UnderCrown` (Explicit: the under-crown pose). `TreePoolTests.Pools_ForestPose` /
`Pools_JunglePose` arms OFF / ON-180 / ON-30 / ON-45 / ON-60 / ADV-x60 -> `Logs/TerrainGen/trees192/pools_<pose>.txt`. Shots:
`Logs/overnight/192_shots.sh` (180's five poses + `under_crown`, four launches) and `192_jungle_retake.sh` (180's jungle eye sits INSIDE
the taller new jungle crowns = black frames; `jungle_high` = same target, eye 20 m higher, all four arms), `Screenshots/192/viewer.html`.
`Cost_OnVsOff` now runs both looks on the same patches (`trees192/cost.txt`). Result: HANDOFF 192.

**Task 193 (look, lighting; owner 2026-11-12 "Yeah"; SOFT_SKY_PLAN S2 + a ground-bounce half; OFF by default): SKY FILL, `-skyfill 1|2`.**
CAUSE (Raymarch.compute AMBIENT SKY VISIBILITY): one ray STRAIGHT UP per tier-0 hit, skyVis 0/1; skyOpen = lerp(floor 0.02 (High), 1,
skyVis) scales the ambient AND the sun-shadow floor, so any face blocked straight up (ground and trunks under a crown, a leaning tube wall,
every down face: its own voxel blocks it) and out of the sun drew at 2 % = black. FIX (`RaymarchFeature.SkyFill`, `_SkyFill`): the same
call site traces ONE cosine-weighted direction per pixel per frame over the face's hemisphere (SkyVisReference's R2 sequence, seeded by the
surface cell's key, so a voxel face converges to its own stratified mean while the camera moves). S = open share of the SKY half (a side
face's lobe folded up: open side face = 1, not 0.5); mode 2 adds B = share of the GROUND half landing on SUNLIT ground (the ray to its first
solid voxel, entry face re-derived from t (`ShadowEntryNormal`), then one sun ray from there; `ShadowHit.hitV/hitT` new). Side faces in mode
2 alternate halves per frame. Per-pixel history `SkyHistR/W` (uint2: S 12b | B 12b | counts 4b+4b, key; ping-pong, reprojected like the sun
history; 1/n for 16 samples then 1/16; mode change / resize drops it; OFF frees it; 2 x W x H x 8 B on). skyVis = S (mode 1; down faces 0 as
before) or S and B mixed by the lumas of FarShadeSky(n) and CubeBounceLight(n, sun) (mode 2). Shells / far keep 1 (plan Q5 (a)); a sky ray
leaving the window sideways counts open (plan S3, not done). CPU twin `SkyFillReference` (+ `EntryNormal`, `FirstSolid`); tests
`SkyFillReferenceTests` (7): sealed 2 m room + 0.1 m shell room exactly 0 every face/frame both modes; 1-voxel crack through a 1.1 m roof
(floor under it 1.2 %, 2 m aside exactly 0); open ground + pillar side over sunlit ground exactly 1; S(side) = 2 x SkyVisReference;
3 m ledge (underside B 0.42, wall S 0.56) + running mean vs quadrature (rms <= 0.07); 1 m x 6 m trench (wall S 0.29 / 0.047 / 0.012 at
0.9 / 2.9 / 5.8 m down -- darker than today's 1.0 there: physical); lattice canopy (ground S 0.50, trunk 0.52, crown underside B 0.54);
GPU contracts (fold, halves, record, entry face = DDA's on 1,765 rays). Capture: WorldSurvey arm suffix `_sfN` (`-wsarms
native_sf0,native_sf1,native_sf2`), each arm's 60 settle frames logged as `[frame]` mean/p50. Shots: `Logs/overnight/193_shots.sh`,
`193_trench.sh`; `Screenshots/193/viewer.html`, `black193.txt`. Result: HANDOFF 193.

**Task 194 (bug, default ON): the RECTANGULAR PITS near the full GPU body pool = the refused-chunk hand-over.** Cause: a chunk the
near GPU body pool refuses (TerrainClipmap.GpuPool.cs, FULL POOL) is held as air in tier 0 and its column marked non-resident so the
shells draw it -- but tier 0 still drew the rest of the column (the uniform rock under a surface chunk needs no GPU body) and its
neighbours, and tier 0 marches first: a ray through the refused chunk's air hit that rock = a 12.8 m box pit with straight walls.
Shell 1 STORES every refused chunk seen (probe: 0 in its +-1-chunk hole at High). Fix (Raymarch.compute, REFUSED-CHUNK HAND-OVER block
before tVoxelLimit): while `_Tier0Refused` (RaymarchFeature: GpuPoolRefusedNow > 0, shells + far band on), a tier-0 hit past the ray's
entry into a column holding a REFUSED chunk is dropped; tier 0 ends at that entry and the shell march draws from there. Refused columns
only: FarField.UpdateResidency writes them as a second bitmap after the residency bits in the same `FarResident` buffer
(`Tier0ColumnRefused`; `ColumnWalkT` = FarStartT's DDA with a mode flag; `FarField.ColumnResident/ColumnRefused` statics). A first
version stopped at ANY non-resident column and lost houses at eye level (streaming columns near the camera lie in shell 1's hole:
`Screenshots/194/seed42_town_eye_after_v1_REJECTED.png`). No pool size, eviction, layout or API change; refused counts unchanged.
`-refusedhandover 0` = old behaviour (shots only); WorldSurvey arm `native_rh0` = OFF for one shot; capture-only `[refused]` lines per
shot (PlaygroundCapture.RefusedProbe.cs: count, on-screen refused chunks with distance / chunk below / shell 1 stores / px). CPU twin
`Tier0HandOverReference`; tests `RefusedHandOverTests` (7: real TerrainClipmap with a 25-body pool reproduces the pit; the floor hit is
dropped at the refused column's entry; earlier hits kept; a streaming-only column is never handed over; the column is tier 0's again
once the chunk lands; twin = a fine column walk on 500 rays). Not covered: a refused chunk inside shell 1's hole still has no shell
data (ABSENT -> far band), and sun shadows across a handed-over column may differ slightly. Shots: `Logs/overnight/194_proof2.sh`,
`Screenshots/194/` (`refused_*.txt`, `diff194.txt`). Result: HANDOFF 194.

**Task 194a (play default; owner Q-Z (a), 2026-11-13: "Mode 2 ON in play now; the performance stage makes it cheaper"): PLAY STARTS
WITH SKY FILL MODE 2.** `PlaySettings.skyFill` (int, default 2; 0 off / 1 sky / 2 sky + ground bounce; out of range -> 2; a file saved
before 194a gets 2), `-skyfill 0|1|2` overrides it for one run (last match wins; a bad value keeps the saved one). `SettingsMenu.LoadAndApply`
sets `RaymarchFeature.SkyFill`; F6 row "Shade light" (Sky + bounce / Sky / Off), live, saved (panel 1152 -> 1226 px, ui scale 1158 -> 1232).
`tools/play-flags.sh` PLAY_SETTINGS_FLAGS gains `-skyfill 2`, so `run-tg2world.sh MODE=playwalk` (the play-like capture run) matches play.
RIGS DO NOT CHANGE: only `Playground.IsPlaySettingsRun()` (not a capture run, not `-fluidprobe`) reads PlaySettings; every other run keeps
`RaymarchFeature.SkyFill`'s own flag read, default 0. Checked: `run-tg2world.sh:83` (MODE=orbit, `-sustained`) and `:93` (`-fluidprobe`)
pass no `-skyfill`; `run-acceptance-rig.sh` launches the Phase 4 scene, which has no SettingsMenu. Test
`PlaySettingsTests.SkyFill_PlayDefaultIs2_OldFileGets2_FlagOverridesForTheRun`; `PlayFlagsFileMatchesPlaySettingsDefaults` now pins
`-skyfill 2`. No F6 screenshot: the menu shows only in an interactive run (`-settingsopen` + `-playshot`), not in a capture mode.
Result: HANDOFF 194a.

**Task 194b (build plumbing, owner 2026-11-13 "Yes this next to speed up development"; NO runtime change): player builds leave five dev
kernels out of Raymarch.compute.** `Assets/Editor/DevKernelStrip.cs` (IPreprocessComputeShaders, only while a player is built) drops
`CSMainFarShadeDiag`, `CSShellPrepassDiag`, `CSShellProbe`, `CSMainChecker`, `CSMainDetail` -- play never dispatches them. `VE_ALL_KERNELS=1` in
the build's environment keeps them; `run-cost-split.sh` and `run-farband.sh` set it (their reports read the counters / probe) and give those
builds a 60-min compiler timeout. Editor play and the EditMode suite always have every kernel. BuildStamp gains a `kernels   all|stripped A,..`
line; the player reads it (`RaymarchFeature.KernelStripped`, parser `VoxelEngine.Rendering.KernelStripStamp`), and a run that ASKS for a missing
kernel logs one `[kernels]` warning and draws with play's kernel: `-checker 1` -> plain TAA, `-detailspeed 1` -> CSMain, the *Diag cases (counter
windows `CollectShadowStats`, debug views, `_DebugShellSkip` arms, `-shelldiet 0`, `-shellmask 1`, `-shellcolleap 1`) -> the lean twins, so
shadow/shell counters read 0 (look review's `[px]` lines, the walk's end-of-run shadow step count) and debug views 12-14 draw as play: build with
`VE_ALL_KERNELS=1` for those. Measured (cold, all kernels compiling at once): Raymarch 1503.7 -> 1128.8 s wall, 8179 -> 5551 s of kernel compile;
slowest kernel now CSMainFlood 1129 s (CSMainFarShadeDiag was 1245 s); whole look-review build 31.0 -> 20.3 min. A rebuild after editing only C#
reuses every kernel (0.01 s, whole build 4.7 min). The cache is per kernel, keyed on the preprocessed source INCLUDING line numbers: any
Raymarch.compute edit that adds/removes a line recompiles all kernels; an edit inside one `#ifdef` that keeps line counts recompiles only the
kernels that see it (task 194's build: 19 hits, 7 compiled); Unity keeps the last 5 source versions (`Library/ShaderCache/compute/Raymarch*/00..04`),
so reverting an edit is a cache hit. Splitting the file would not help (the shared body is in every kernel). Proof nothing changed: build 2 (same
source) compiled 0 of 21 kept kernels (byte-identical binaries); stills before vs after differ only as much as two launches of the SAME player do
(A/A control). Tests `DevKernelStripTests` (list = five real non-play kernels, every one guarded, stamp parses back). Files: `Screenshots/194b/`
(`compile194b.txt` per-kernel tables + pixel diffs), scripts `Logs/overnight/194b/` (`binmap.sh`, `table.py`, `pxdiff.py`). Result: HANDOFF 194b.

**Task 195 -- rule ores are the DEFAULT (owner 2026-11-13, Q-X (a) "ores ON now"; leaf fill unchanged at 45, Q-Y (b)).**
`TerrainGenChunkSource.OresArg` now defaults to true: every tg2 chunk source (play, rigs, capture runs, EditMode tests that build one) runs
task 181's rule ores; `-ores 0` leaves `spec.oresOn` 0 = step 3's `Ores.cs` placement, the pre-195 world byte for byte (same code path as
before). Unlike 187a / 190b the SPEC field is NOT flipped: a bare `TerrainSpec` stays OFF, because the rules need the ore cell plane only the
chunk source builds (`BuildOreCellPlane`, 4,096 KB); a bare spec with `Ores2` would read cell 0. Player log: `[StreamManager] tg2 chunk source:
... rule ores ON (default; plane 4096 KB)` / `OFF (-ores 0)`. No F6 entry (there never was one); no identity/hash rig uses the tg2 world, so no
rig needed `-ores 0`. Tests: `TerrainGenChunkSourceTests.RefJob` (the TerrainGen reference) now carries the source's ore cell plane like
ColumnJob (it failed `AirFastPath_IsExact` at surface - 3 without it); `BrickSkipTests`' plain proofs now cover the rule ores and the
`_Ores` twins became `_OresOff` (`-ores 0`, reports `brick_skip_*identical_oresoff.txt`), restoring the source's previous value.
Shots: `Logs/overnight/195_shots.sh` (181's three wall poses, `-tod 12`, default vs `-ores 0`), `Screenshots/195/sheet_*.jpg`.
Rough cost from that launch (not a timing arm): shell chunk generation 114-129 ms vs 85-91 ms per chunk on a worker. Result: HANDOFF 195.

**Task 195a -- trees and towns are the DEFAULT (owner 2026-11-13, Q-X (a) "trees and towns ON once 192, 193 and 194 are done").**
`TerrainGenChunkSource.TreesArg` and `TownsArg` now default to true: every tg2 chunk source (play, rigs, capture runs, EditMode tests that
build one) places task 180/192's voxel trees and task 190's towns; `TownsArg` is also what FarField's own map reads, so the far band draws
the flattened town pads by default. Towns need the site planner: `-towns` already forces `spec.sitesOn = 1` (constructor and FarField), so
`-sites` keeps its OFF default (records only) and needs no flip. `-trees 0 -towns 0` = the pre-195a world byte for byte (`spec.treesOn`,
`townsOn`, `sitesOn` stay 0 -- the same code path as before; add nothing else). As 195, the `TerrainSpec` fields are NOT flipped: a bare spec
stays OFF (trees need the source's tree plane, towns its town plane). Player log: `[StreamManager] tg2 chunk source: ... trees ON (default),
towns ON (default), sites ON` / `OFF (-trees 0)`. No F6 / PlaySettings entry exists for either; no identity/hash rig uses the tg2 world, so
no rig needed the `0` flags. One test fixture did: `MaterialRuleChunkTests` (180b's "the rules repaint solids only, air/fluid unchanged ON vs
OFF") now pins trees OFF -- a tree's density reads the column's cover slot, which the material rules set (`GroundRules`, "trees read the
Cover slot"), so rules ON vs OFF legitimately moves trees, i.e. air (whole-suite run 1: 2 failures there, nothing else). Towns stay ON in it.
Shots: `Logs/overnight/195a_shots.sh` (191's forest + snow + town_eye + village_eye, seed 42, `-tod 12`, default vs `-trees 0 -towns 0`),
`Screenshots/195a/`. Result: HANDOFF 195a.

**Task 196 -- fluid pockets get natural basins (owner 2026-11-13, Q-X (a): "fluid pockets stay OFF until a small task softens their walls into natural basins"; `-fluidpockets` stays DEFAULT OFF).**
Why the walls were sheer: `FluidPockets.BuildPlane` puts the fluid level L one voxel under the LOWEST of 24 rim samples, and the samples may
spread up to FLAT_SPAN (2.4 m); `Apply` then cut every footprint column straight down to L, so the high side of a pocket on a gentle slope
was a vertical wall up to ~2.5 m tall (191's `surface_lava`). Now (`FluidPockets.Apply`, header "TASK 196"): the RIM (8-neighbours of the
footprint) sits flush at L + 1 (one voxel over the fluid, lined as before); the BANK beyond it moves the ground toward L + 1 by at most
UP - drop(t) (cut, uphill) or DN - drop(t) (fill, downhill levee), drop(t) = (t - 1) * BANK_Q8 >> 8, t = the smallest nesting ellipse
(rx + t, rz + t) holding the column (`Ring`). BANK_Q8 = 147 = tan(`Cover.STEEP_DEG` 30 deg) * 256 rounded down: the slope rule the ground
already has (soil stays, a walker can scramble out). UP / DN = the site's EXACT rim excess over / under L + 1 (every rim column of the base
terrain, `MeasureBank`, once per site in BuildPlane), so the rim lands on L + 1 and the change tapers to 0 at `BankBand(UP, DN)` -- no step
anywhere. The bowl's depth is `BowlDepth` = min(D, 1 + rmin * BANK_Q8 / 512) (its steepest under-fluid wall under the same slope). The band
must stay inside the plane cell and the record cells the planner checked; a larger excess is capped (`PlaneStats.bankCapped`; 0 sites on
seeds 42 / 7 / 2026 / 31337). Placement unchanged (same candidates, rules, L); plane words [1] / [2] carry UP / DN in bits 24-31 (0 / 0 =
183's shape). Same rule for every fluid and biome. `-fluidpockets 0` never reaches Apply (unchanged byte for byte). `-pocketbasin 0`
rebuilds 183's exact shape for before / after pictures. Gameplay: 111 of 131 seed-42 bowls are shallower (water up to 2.0 m -> 1.4 m at
r 4.7 m); trees on a bank stand on the lowered ground. Plane build (main thread, once per world, only with pockets
ON): player 27 ms -> 70 ms (seed 42 log); the Editor's Mono 0.4 s -> 1.2-1.9 s. Tests (`FluidPocketTests`): new `Bank_Rules` (fast); `FourSeeds_Plane_Table_KeepOuts` asserts every uncapped rim is flush;
`Sites_AtRest_PathsAgree_Adversarial` adds a FORCED site on a ~20 deg slope, widens every box to the bank, and per case checks the rim and
that no 4-neighbour step outside the footprint grows by > 2 voxels ON vs OFF (tree voxels moved by the bank are counted, not failed).
Shots: `Logs/overnight/196_shots.sh` (191's surface_lava + water / quicksand / oil from the north, seed 42, `-tod 12`, `-pocketbasin 0` vs
default), `Screenshots/196/viewer.html`; CPU cut-aways `Screenshots/183/cut_*.png` (before: `Screenshots/196/before_cutaway/`). Result: HANDOFF 196.

**Task 197 (bug, default ON): the pits came back with 0 chunks refused = two more ways a column shows air in tier 0.** Capture probe
`-mirrorprobe 1` (PlaygroundCapture.MirrorProbe.cs; read at END of frame, because streaming / uploads run in Phase4Bootstrapper.LateUpdate
after the survey coroutine; the shot is that same frame): per band chunk of the mirror box, slot owner / held / dirty / refused, streaming
stage, the GPU's own ClipmapBuffer and packed air mip against the CPU mirror (`[gpu]`: 0 differing in every frame), store AIR chunks against
the generator (`[gen]`: 0), and the same chunks 5 s later. Causes found (jungle_high, launches q1 / h1): (1) RETRY GAP: RetryGpuRefused
clears the whole refused set as soon as one GPU slot frees and re-dirties the chunks, but the re-upload waits in the budgeted, nearest-first
dirty queue (up to ~1,250 chunks), while the slot still holds the refusal's air: "refused 0, 192 resident chunks missing, hand-over off";
(2) VALVE EVICTION: the CPU pool's LRU valve evicts visible chunks (~28-41k valve evictions per survey run, peak 495k of 570k, valve 484.5k)
and they reload; the eviction's clear is air and the column is non-resident (194 never handed those over). Fix (no pool / eviction / layout /
API change; shader unchanged: the existing refused-bit now means hand-over): `TerrainClipmap.Tier0LacksChunk` (resident chunk whose slot does
not hold its own write: refused, retried-but-queued, first upload queued, or another chunk's write; air over an empty slot is fine) and
`FarField.ColumnState` (one store lookup per chunk for both bits): hand-over = refused, or resident and lacking, or NOT resident where shell 1's
PUBLISHED directory holds it (`ShellStreamer.PublishedHolds`, outside its hole: inside the hole such a column stays tier 0's, 194's lost-houses
rule). `FarField.HandOverColumns` gates `_Tier0Refused` (was GpuPoolRefusedNow > 0). `FarField.LackHandOver` false = 194's rule (survey arm
suffix `_lh0`, before shots). Tests `Tier0LackHandOverTests` (6: the retry gap with a real budgeted pass, its pit ray old vs new rule,
landing, first upload / air slot, the evicted column in vs out of the shell hole, a foreign slot after a recentre). Side effects: a shell
draws a handed-over column with ITS copy, which can differ from live tier 0 at a lake shore (jungle_high right edge: a vertical blue water face
and grey rock face, f1-f3) and shows the far band where the shells lack data (the 194 limitation, now for more columns). 195's badlands open
sea did not reproduce (6 launches); it had 0 refusals and the wall columns lay in shell 1's hole, so this rule would not hand them over.
Shots: `Logs/overnight/197_proof.sh`, `Screenshots/197/viewer.html` (pairs), `Screenshots/197/{p1,q1,g1,h1,f1,f2,f3}/`. Result: HANDOFF 197.

**Task 198 (bug investigation, 2026-11-14): white dashes on steep faces -- RENDERER (lighting rule), real voxels; not fixed (look change, options in HANDOFF 198).**
The dashes are sun-lit 1-voxel treads of the voxel staircase (real top faces, sub-pixel at 100-200 m) lit by their OWN face where
`FarSlopeNormal`'s cave/overhang test fails (`p.y < lo - tv || p.y > hi + tv`), so `CubeFilterWeight` is 0 and task 120's filtered cube
light does not average them away (it does everywhere else). Capture probe `-dashprobe 1` (with `-worldsurvey`; `PlaygroundCapture.DashProbe.cs`):
finds 1-px light lines in the beauty frame, marches the same camera rays on the CPU through the generator's shell cells (`GenerateShellChunk`,
shell by distance, never coarser mid-cell), sun-shadow ray by the GPU's 3-cell rule, and emulates `FarSlopeNormal` on the far texels' CPU copy
(`-dashprobe 1` sets `FarField.ShadowTexels`, +20 MB); `-dashwin "pose:x,y;..."` picks 192x108 windows (else the densest) -> `ws_<pose>_cputop*.png`
(white = predicted dash, cyan = tread averaged away, blue = shadowed tread, red tint = test fails). Debug views could not be used: see
KNOWN_ISSUES (CSMainFarShade fails in player builds). Driver `Logs/overnight/198/shoot1.sh`; pictures `Screenshots/198/viewer.html`. Result: HANDOFF 198.

**Task 199 (plan only, owner 2026-11-13 topic 3 P1-P4 + R1/R2; no code, no default change, no launch): render steps + frame budget.**
`AMENDMENT_8_17_RENDER_STEPS.md` (DRAFT, NOT ADOPTED) + owner doc https://claude.ai/artifact/6JF22Hg7USmgb75F19UQgB: Full vs Air
(M1, 30 fps) presets; a 33.3 ms budget per step for Air from 168b / 146 cost splits (sun ~5, sky ~2, shells ~1.3 ms over share on
walk_near); seven steps S1 Surface -> S2 Sun -> S3 Sky -> S4 Local light -> S5 Reflections -> S6 Shade + compose -> S7 Post sharing a
per-pixel hit buffer (+40 B/px: +19.8 MB at 540 rows); ordered tasks T0-T8 with est. ms; R1 water reflections and the R2 per-material
shine table (no voxel-layout change); cinematic techniques vs shader packs / Lay of the Land; owner questions Q1-Q7. Finding (code
reading, unmeasured): play draws the torch-flood twin `CSMainFarShadeFlood` since task 175, so diet 2 is bypassed in play. Result: HANDOFF 199.

**Task 200 (EXAMPLES ONLY, owner 2026-11-13 trees talk T1-T4; no default, no world-gen change): tree growing methods at 0.1 m, `-treegallery <layout>`.**
`Assets/TerrainGen/TreeGallery.cs`: prototype trees built ONCE from an integer seed (species, method, seed) into an 8^3-brick volume, then
queried (Trees.cs's contract: pure function of a hash, fills AIR only). Methods: `M_SPACE` space colonization (attraction points in a
species crown ENVELOPE, outer shell only where the crown is open; nodes step toward the points that see them; pipe-model radii; leaf clumps
on the tips), `M_LSYS` rule-based recursive branching (phyllotaxis 137.5 deg, down angles, lengths from the envelope, 3 levels), `M_TEMPLATE`
an authored limb list + jitter. Species (`Pick`, every number a per-tree range): oak, banyan (wide dome, horizontal limbs, aerial roots ->
prop trunks with their own flare), redwood (60-70 m fluted leader + buttress flare, branches in the top half), birch (pale stand-in bark
`MushroomStem`, 1-3 stems), spruce, acacia. Leaves: lumpy porous clumps (0.2 m hash holes) + a crumb filter (leaf voxels not 6-connected to
wood go). Engine: `TerrainGenChunkSource.Gallery.cs` -- `-treegallery <file>` (absent = off; lines `tree sp method seed x z`, `today cx cz x z`
= Trees.Build's tree moved, `marker x z` = 1.8 m post, `clear x0 z0 x1 z1` = no natural tree rooted there) builds the volumes in the
constructor and publishes them through a `SharedStatic` (`TreeGalleryShared`); hooks: `GatherTrees` raises the column top, `OverlayTrees`
calls `OverlayGallery` first (LOD 0 + shells <= `Trees.MAX_SHELL`), `PointTree` asks `GalleryAt`. Off = every hook returns at its first line.
Tests `TreeGalleryTests`: `TodayTrees_Pinned` (Slow; FNV of Trees.Build over seed 42's first 64x64-cell block with >= 50 trees: 1,663 trees,
`38EF6100C34F1928`), `Methods_Species_SheetsCost` (Slow; 6 species x 3 methods x 3 seeds on flat ground, CPU side-view sheets
`Screenshots/200/proto_<species>.png`, `Logs/TerrainGen/trees200/cost.txt`), `Gallery_Chunk_AirOnly` (Slow; ON vs OFF chunks round an oak:
only air changes, exactly to `GalleryAt`), `Gallery_Layout` (Explicit; flat open sites on seed 42, no town, no snow top, biome per species ->
`gallery_42.txt` + `poses_42.txt`). Shots: `Logs/overnight/200_shots.sh` (16 poses) + `200_reshoot.sh` (5 re-sited blocks);
`python3 -I tools/trees200.py` -> `Screenshots/200/sheet_{methods,species,closeup}.jpg` + `viewer.html`. Result: HANDOFF 200.

**Task 201 -- surface fluid pockets are the DEFAULT (owner 2026-11-13, Q-AA (a): "fine for now but these need to be more like ponds"; the pond look is a LATER talk, `OWNER_TOPICS.md`).**
`TerrainGenChunkSource.FluidPocketsArg` now defaults to true: every tg2 chunk source (play, rigs, capture runs, EditMode tests that build
one) places task 183's surface pockets with task 196's natural basins; `-fluidpockets 0` leaves `spec.fluidPocketsOn` 0 = the pre-201 world
byte for byte (same code path as before: the plane stays a one-entry dummy and `FluidPockets.Apply` is never reached). Shape, placement,
rules and `-pocketbasin` are unchanged. As 195 / 195a, the `TerrainSpec` field is NOT flipped (a bare spec stays OFF: the pockets need the
site plane only the chunk source builds); FarField shares the source's map, so the far band follows the source. Player log:
`[StreamManager] tg2 chunk source: ... fluid pockets ON (default; 131 surface sites)` / `OFF (-fluidpockets 0)`. No F6 / PlaySettings entry
exists; no identity/hash rig (acceptance, acceptance-fluid, phase 5/6/7, fluid-tiled) builds the tg2 world, so no rig needed
`-fluidpockets 0`. Fluid sim: pockets are resting generated fluid, which takes NO fluid tile under awake-only residency (task 54,
`FluidTileResidency.Refresh`): they cost the tick nothing until a wake (an edit, a flow into them). The world survey now prints a
`[fluidtiles]` line per pose (resident / acquired / unsettled tiles; capture only). New Explicit report `FluidPocketTests.ForestSites_Poses201`
(seed 42's forest water / lava sites + north-side poses -> `Logs/TerrainGen/pockets201/`). One test followed the new default: `MaterialRuleChunkTests.Shells_BaredVoxels_WearTheSkin`
(182) failed on 8 shell-5 voxels at `border_bank` -- a shell's TOP voxel takes its material at the ground h, and there h lay inside a pocket's
lining range, so it showed the mud lining (as LOD 0 does, `DetailField.Material`), which the test read as "soil under the skin". The test
now skips lining voxels like stamps (sampled y, or h for the top voxel) and logs them: 38 voxels wear their pocket's lining, 0 show anything
else (`shell_bared182.txt`). Pockets stay ON in it (no pin). Shots: `Logs/overnight/201_shots.sh` (196's water
+ lava, a forest water pocket, 195a's forest; `-tod 12`; default vs `-fluidpockets 0`), `Screenshots/201/`. Result: HANDOFF 201.

**Task 201a (bug, owner 2026-11-14 "efficient by construction"; no bigger pool): SEALED BRICKS take no GPU body, ON by default (`-gpuseal 0` = the old upload).**
The GPU body pool (task 93f, 254,400 bodies = 124 MB) was full at every pose since 195a: demand at the 5 test poses 194-321 k bodies,
refused chunks drawn by the shells (grey walls, a missing redwood). Accounting (`-gpupoolprobe 1`, capture only,
`PlaygroundCapture.GpuPoolProbe.cs`: per pose, the box's dense bodies by source / kind / hidden / ring / layer / duplicates) found
26-37 % of the bodies are bricks NO ray can reach: every voxel and the 1-voxel shell around the brick opaque. Trees+towns add ~55 k at
the forest pose but the pool was full without them too (`-trees 0 -towns 0`: town_eye and the redwood pose still refuse).
`TerrainClipmap.Sealed.cs`: at each box chunk upload a Burst scan (`SealedBricks.Mask`, main thread, ~0.1 ms per dense chunk) marks the
chunk's INNER bricks (1..14 on every axis, so the shell lies in the same chunk: a refused / evicted neighbour cannot expose them) whose
brick and shell are all seal-solid (not air, fluid 6/11/12/18-22, torch 14-17, glass 24 or river lip 136); those are staged as a
UNIFORM handle of their voxel 0's material and acquire no GPU slot and upload no body. Exact: every tier-0 ray passes air or a clear fluid
and stops at the first differing voxel, stepping face to face, so it stops on the shell first; neighbour reads (AO, emissive bleed,
river lip, normals) only touch voxels next to air / fluid; shadow / torch / height reads test non-zero. Nothing is cached: every write path
re-dirties the chunk, and the mask is recomputed from the CPU state at its upload. CPU side unchanged (`_clipmapLocal` keeps CPU handles: air
mips, fluid twin); the fluid terrain directory still uploads every body (its CA reads them). Validators compare against
`ExpectedGpuHandle` (sealed -> uniform). Pool off (rigs, `-gpupool 0`): never runs. Tests `SealedBrickTests` (14^3 inner bricks sealed,
none on the border; one air / water / glass / torch voxel unseals exactly the 8 bricks around it; an oracle chunk with caves, a pond,
glass and a torch: GPU view = CPU at every voxel next to a non-solid one, solidness and non-zero equal everywhere; too big unsealed fits
sealed; switch off / off mid-run). Ledger `[mem]` line now prints `in use N, sealed N`. Capture flags: `-gpupoolprobe 1` (the accounting
lines per survey pose), survey arm suffix `_gs0` (seal OFF for that shot: every box chunk restaged, then back) and `-gpusealcmp 1` (shells,
far band and river time held during a pose's arms, for identity stills). Scripts `Logs/overnight/201a_{account,after}.sh`,
`201a_table.py`, `201a_diff.py`; table `Screenshots/201a/table201a.txt`. Result: HANDOFF 201a.

**Task 202 (plan only, owner 2026-11-13 G1 (a); no code, no launch, no tests): the GAME-DESIGN DOC, DRAFT.** Claude Doc "Game design"
(https://claude.ai/artifact/BprGXbxRr6W5MPdDAXxYp8, the live version; the owner answers there) + repo snapshot `GAME_DESIGN.md`. Pitch from
the owner's words; nine areas (world and travel, settlements, resources, building, survival, combat and skills, creatures, progression, core loop)
with every row tagged Decided / Leaning / Open + source and date (brief, design round 2026-10-15, placement batch P1-P6 of 6 Nov, 13 Nov talks
V1-V4 / T1-T4 / W1); the placement-phase checklist (resources -> towns -> lairs, flora alongside) and the before-characters checklist; questions
A1-A6 (placement: core loop, cities, humanoid design tool, what trees give, town safety, own-settlement residents) and B1-B6 (classic death, hunger,
night, how moves are learned, fast travel, companions), recommendation first.

**Task 203 -- surface fluid pockets get NATURAL OUTLINES (owner 2026-11-13, Q-AA "these need to be more like ponds", W1 (a): every fluid; DEFAULT ON).**
`FluidPockets` (header "TASK 203"): a surface pond's footprint is no longer one ellipse but the union-like blob of 3-4 overlapping LOBES
(scaled copies of the site's ellipse): G = sum_i max(0, 1 - rho_i), footprint = { G > TAU (0.05) }, integer Q16 (`Shape`, `Field`,
`InLobes`). Lobe 0 on the centre; lobe 1 points to the LOWEST of the 16 rim samples the planner already takes (data-derived: the pond reaches
toward the low ground and draws back from the high side, so less is cut); lobes 2-3 on its flanks at angles / sizes hashed from the site's
own centre. Every lobe lies inside the site's ellipse and box, so the pond, its rim and every bank ring stay inside what the planner checked
(183 / 196's containment holds). The bed shelves at 196's 30 deg bank slope from the shore (`LobeDepth`); rim flush at L + 1 and bank rings
as 196 (`LobeRing`; G grows with t, so rings nest); UP / DN measured on the new rim (`MeasureBank(in Site)`). Same rule for every fluid and
biome; at rest by construction. Plane bits: word [0] bits 28-31 the low direction, word [3] bit 31 = outline on; `-pocketoutline 0` leaves
both 0 = 196's plane byte for byte (FNV 5A0794B7B9ECA569, seed 42); `-fluidpockets 0` never reaches Apply. No extra memory: 5 bits in the
existing plane words, the shape is recomputed per column from the hash. Gameplay: ponds cover ~55 % of the old ellipse's area (smaller
ponds, less fluid); a tree can now stand on ground the old ellipse took (seed 42's jungle honey pond is hidden under a crown).
**IL2CPP bug found and worked around:** `Pack` with a `?:` inside the word-3 expression compiled in the PLAYER to a `uint8_t` temp
(rx / rz / depth = 0 in the player only, EditMode green); now statements (KNOWN_ISSUES). The player log's `[FluidPockets] plane ...` line
ends with `plane fnv <hex>`: it must equal an EditMode build of the same seed. Tests (`FluidPocketTests`): new `Outline_Rules` (fast; 600
random sites: lobes in the box, one 4-connected pond, rings nested, bed step <= 2 voxels, reaches toward the low side on 599 / 600, outline
off = the ellipse, Pack / Decode; writes `Logs/TerrainGen/outline203/shapes.txt`); `Sites_AtRest_PathsAgree_Adversarial` also renders CPU
top views `Screenshots/203/cpu_top_*.png`. Shots `Logs/overnight/203_shots.sh` (seed 42, `-tod 12`, `-pocketoutline 0` vs default),
`Screenshots/203/viewer.html`. Result: HANDOFF 203.

**Task 205 (look, 2026-11-14; flag only, default OFF -- the look call is the owner's): white dashes in play + `-dashfix 0|1|2`.**
Play check (198's poses, noon, sun behind): FSR 3 dyn does NOT hide the dashes. It turns them into a fainter light line (forest_breaks wall box:
66 predicted-dash px showing at native -> 14-18 in play; cliff_snow ledge 122 -> 8-9; tree crowns 784 -> 296-321). New finding (oracle side split): EVERY
predicted dash lies ABOVE the four far-texel column tops (forest 374/374, cliff_snow 1,296/1,296, 0 below), so trees and cacti (also above) fail the bracket the same way.
`-dashfix` (`RaymarchFeature.DashFix`, `_DashFix`; `Raymarch.compute` `FarSlopeCode` / `CubeFilterWeightOpen`, voxel / shell path only, the far band's call unchanged):
where the bracket test fails, sky-open hits (x skyVis; caves keep face light; undersides never) take task 120's filtered cube light with the column slope.
**2 = TOP faces only (recommended)**: dashes gone at native and in play (forest box 66 -> 0, cliff_snow 122 -> 1-6), side faces keep their light; changes
0.42 % (forest) / 1.05 % (cliff_snow) / 4.4 % (overhang: crowns lose their tread stripes, a little darker) / 0.16 % (cave) of pixels.
1 = every face but undersides: also kills them, but flattens trees / cacti and adds pale side-face stripes (7.4 % on overhang). 0 = byte for byte the old
call (`fixed_cliff` identical to 198's pre-change frame; others within launch-to-launch streaming noise). Survey arms `native_df2` / `fsr3_dyn_df2`
(suffix `_dfN`); `-dashprobe 1` now also counts later arms (`DashCount`) against the first arm's predicted pixels and splits predictions ABOVE / below the
bracket. The world survey disables `PlaygroundFlyCamera` (a click on the window turned the camera; KNOWN_ISSUES). Tests `DashFixTests` (2). Drivers
`Logs/overnight/205/shoot1-4.sh`; pictures `Screenshots/205/viewer.html`. Result: HANDOFF 205.

**Task 206 (bug from 198, 2026-11-14): debug views, the debug pixel and the torch-flood kernel load in a player again; a kernel that does not load is LOUD.**
Three kernels failed Metal's compile at their first dispatch in a player and silently kept the last image (KNOWN_ISSUES): `CSMainFarShade` (bool select from
SkyHistFetch's early return) and the flood twins `CSMainFarShadeFlood` / `CSMainFlood` (the same, plus 33 Metal buffer slots, cap 31, since task 193). Fix in
`Raymarch.compute`: `HEIGHT_MIP_READ` = read-only `HeightMipRO` outside diet 2 (RaymarchFeature binds the same buffer to it; 9 -> 8 UAVs), SkyHistFetch's
probes walked backwards outside diet 2 (same first match), `_UseLODCascade` folded to 0 in the flood twins (macro after diet 2's pair) and RaymarchFeature
picks a flood twin only while the cascade is off. Diet-2 play kernels: Metal source byte-identical (17 of 21 kernels identical, `Screenshots/206/msl206.txt`).
`KernelHealth` (Assets/CoreEngine/Rendering): IsSupported per kernel + a GPU heartbeat (DebugBuffer[0..2] = NaN before an unverified non-diet kernel's dispatch,
read back at the next record; NaN = it never ran; 2 good reads = verified, then no per-frame work) -> one `[kernels] FAILED` error, the blit pass clears to
MAGENTA, `RaymarchFeature.KernelFailFrames / KernelFailedLastFrame / PrimaryKernelLastFrame`. World survey arm suffixes `_tf1` (torch flood on for the shot),
`_dvN` (debug view N), `_px` (debug pixel, view 0); `_dvN`/`_px` print `[dbgpx]` (put them LAST: the pixel keeps diet 2 off), every arm prints `[kernel]`, the
survey ends with `KernelHealth.Summary()`. Tests `KernelHealthTests` (10). Driver `Logs/overnight/206/shoot.sh`; pictures `Screenshots/206/`. Result: HANDOFF 206.

**Task 207 (bug, default ON; owner Q-AB (a), fix 1 of 2): the GPU retry re-queues only refused chunks that fit.** `TerrainClipmap.RetryGpuRefused`
(TerrainClipmap.GpuPool.cs) used to clear the WHOLE refused set as soon as one GPU body slot freed (task 197's "retry gap" churn). Now, at the
start of every upload pass, it re-queues refused chunks nearest the camera first (Chebyshev XZ, ties by y/z/x). A chunk is re-queued, and leaves
the refused set, only if the bodies it asked for when it was refused (its dense, non-sealed bricks, stored by `Refuse`) fit in free bodies minus
those promised to earlier re-queued chunks still in the dirty queue (`_gpuRetryPending`, purged every pass). A chunk that does not fit stays refused
(197's hand-over keeps drawing it), and a farther, smaller one may still go. Evicted refused chunks are forgotten. A refused chunk that has left the
box is re-queued so that it parks. The sort only runs when free-minus-promised or the camera chunk changed. No pool size, layout, shader or
eviction change. `-retryfit 0` = the old retry (launch-wide, Phase4Bootstrapper; `TerrainClipmap.RetryOnlyWhatFits`). New counters
`GpuPoolRetriesTotal`, `GpuPoolReRefusedTotal` (a retried chunk refused again = the churn), `GpuPoolRetryPending`, printed on `-mirrorprobe 1`'s
`[mirror] box` line. Tests `GpuRetryFitTests` (8: min(k, N) nearest first for k = 1/2/4/6, promises held across passes, a near chunk too big
stays refused while a farther one goes, evicted ones are forgotten, `-retryfit 0` churn). Driver `Logs/overnight/207_pair.sh` (b1 ON / b2 OFF,
197's poses); shots `Screenshots/207/{a1,b1,b2}/`. Result: HANDOFF 207.

**Task 208 (bug, default ON; owner Q-AB (a), fix 2 of 2): the CPU pool valve spares chunks the player can see.** §3.6's valve
(`StreamManager.ApplyPoolPressureValve`) used to take the oldest-admitted chunks outside the camera's 5x5. Those are the NEAR ones, because
admission runs nearest first, so 23 % of its victims were in view (19,488 of 83,149 in the proof's OFF launch). Now, when a camera frustum is
set (`SetViewFrustum`, from `Phase4Bootstrapper.LateUpdate` via `GeometryUtility.CalculateFrustumPlanes`, no alloc), one scan + one sort per
call ranks the victims: out of view first, then in view beyond the load radius, then in view within it (last resort: counted and logged as
`[valve] evicted an IN-VIEW NEAR chunk`). Within a class the farthest (Chebyshev XZ) goes first; LRU then the coord break ties. The pressure
test, the 32-victim guard per call, the 5x5 exemption and EvictChunk (save before drop) are unchanged. There is no per-chunk memory (reused
scratch list), and the old code scanned every resident chunk once per victim. With no frustum, or with `-valveview 0` (launch-wide,
`StreamManager.ValveViewOrder`), the old LRU order runs. New counters `ValveInViewEvictionsTotal` / `ValveInViewNearEvictionsTotal` (counted in
both orders), printed on `-mirrorprobe 1`'s `[mirror]` lines. Tests `ValveViewOrderTests` (6: a full pool evicts 32 out-of-view farthest-first
chunks = the order's first 32, the order is deterministic and class/far/LRU ranked, a 1-brick overshoot stops on the order's prefix, everything
in view still relieves the pool and counts, the old order's per-call count and in-view counter, no frustum = LRU). Driver
`Logs/overnight/208_pair.sh` (b1 ON / b2 `-valveview 0`; 197's poses + a 3-step walk from jungle_high); shots `Screenshots/208/{b1,b2}/`.
Side effect: the GPU body pool stays full (the old valve freed GPU bodies by dropping in-view chunks), so refusals went up. See HANDOFF 208.

**Task 209 (look, owner 2026-11-14 Q-AF (a): "why can't we use the same rules we use for the island coast? I want the ponds to have a natural outline"; DEFAULT ON, `-pocketshore 0` = 203).**
`FluidPockets` (header "TASK 209"): a surface pond's shore is now the ISLAND COAST'S RULE. `WorldMap.Coast` is the envelope 1 - d plus two fBm
bands (wavelength 9000 / 2600 voxels, 5 / 3 octaves, amplitude 0.45 / 0.12 island radii, R 24,000), land where the sum > 0; task 209 names those
numbers (`WorldMap.COAST_*`, Coast / Macro compile unchanged) and the pond takes them scaled from the island's radius to the pond's: shore radius
(in units of a base ellipse S * (rx, rz)) = 1 + N, N = the two bands with the island's amplitudes, lattice units per radius (K = R / wavelength),
lacunarity 2.03 and octave shift, sampled on the nominal shore in the column's direction (`ShoreOf`, `ShoreAt`, `InShore`, `ShoreRing`, `ShoreDepth`,
`ApplyShore`). What could not be copied, and why: (1) the 2-D threshold makes islets and closed-off lakes; a pond must be ONE body, so N is a function
of direction only (every ray leaves the pond once), faded in from the centre (w = min(1, rho); any fade stays star-shaped) so the bed has no centre
seam; (2) Unity's float simplex is not byte-identical across Burst / Mono / IL2CPP and the seal needs one footprint everywhere, so the noise is an
integer Q16 gradient noise (8 gradients, quintic fade, x sqrt 2, clamped to [-1, 1]) with Noise.Fbm's sums; (3) the island is drawn at ~300 map cells
of radius, a pond at 10-50 voxels: an octave is kept only while its lattice unit spans >= 4 voxels along the shore (bumps >= 2 voxels; the first octave
always), so ponds show the coast's 1-2 coarsest octaves (band 2 never fits). Containment is exact, not tuned: S = 1 / (1 + B), B = the exact bound of the
KEPT octaves, so the shore never leaves the planner's ellipse (Shore_Rules checks InFoot(rx, rz) with no + 1). Depth: the ray distance to the shore
measured on the short axis, shelving at 196's 30 deg (no steeper in any direction); rim / bank / lining as 203 on the new shore (each column's shore
ellipse grown by t: rings nest). 203's lowest-rim lobe is gone (the coast rule has no such term); `lowDir` is still stored. Ponds are ~63 % of the
planner's ellipse (203: 55 %): slightly MORE fluid per pond. Plane bit: word [3] bit 30 (depth <= 20 needs bits 24-28; Decode masks 63);
`-pocketshore 0` leaves it 0 = 203's plane byte for byte; the shore needs the outline (`-pocketoutline 0` = 196's ellipse). No stored data, no memory:
the shape is recomputed per column from the site hash. Tests (`FluidPocketTests`): `Shore_Rules` (fast; 600 sites: the Q16 numbers equal WorldMap's,
inside the planner's ellipse, centre in, one 4-connected body, rings nested and in the box, bed step <= 2, not its base ellipse, shore off = 203's
lobes, Pack / Decode with bit 30 only; `Logs/TerrainGen/shore209/shapes.txt`), `Shore_PlaneFingerprint` (Slow; seed 42: off = 203's FNV
72CEA9A36AECA569, no site moved; `plane_fnv.txt`). Shots `Logs/overnight/209_shots.sh` (seed 42, `-tod 12`, `-pocketshore 0` vs default),
`Screenshots/209/viewer.html`. Result: HANDOFF 209.

**Task 210 -- WORLD space-colonization trees behind `-trees2 1` (owner 2026-11-14 Q-AD "a for all"; default OFF = today's trees byte for byte).**
`Assets/TerrainGen/Trees2.cs` + `Assets/CoreEngine/WorldGen/TerrainGenChunkSource.Trees2.cs`. A tree is an INSTANCE of a shared prototype, not a
per-position build: task 200's method (a) (`TreeGallery.Build`, M_SPACE, `world: true` = aerial roots 4 m into the ground) grows a LIBRARY once
per process (oak, birch, spruce, acacia x 6 variants, banyan, redwood x 3; fixed seeds 1..V, so variants 1-3 are the trees of task 200's
sheets), each prototype on its own worker thread (main thread packs + publishes through a SharedStatic `Trees2Shared.Lib`, refcounted by the
sources; 30 prototypes, 10.6 MB, 2 bits a voxel; player 550 ms on 6 threads, EditMode ~4.3 s). An instance = (prototype, quarter turn, root
snapped to a multiple of 8 on every axis): prototype bricks land on world bricks, so every copy of a prototype has the same bytes in the bricks
it does not share with terrain. Placement, a pure function of seed + cell: NORMAL grid 12.8 m (one tree a cell; species by the biome's BRICK
BUDGET: `BUDGET` = today's trees' expected bricks per 51.2 m square at full plane (density x 64 cells x 266), `SHARE`/16 per species, p =
budget share / (16 cells x the species' MEAN prototype bricks, from the built library) -- a bigger prototype makes its species rarer, never
the area costlier; x tree plane (capped 16/16) x `Trees.SlotMul`); GIANT grid 102.4 m (a banyan per jungle cell p 0.30; a grove of 3-5
redwoods p 0.35 in forest cells with tree plane >= 18 = river plains / wetland). Site rules = Trees.Build's (land, dry, no stamp / opening,
45 deg foot, canopy clearance) + the crown's reach over tree-plane cells (no crown over road / town / sea) + giants' root feet reach the ground.
Suppression one level per tier (deterministic from any footprint): giants > oaks > birch / spruce / acacia, then 192's trees inside a library
tree's keep radius are emptied (`Suppress192`). 192's trees kept (`Trees2.Keep192`, Trees.Build): forest none, desert all (cactus), snow 6/16
(pines), jungle 14/16 (emergents + palms; the 2/16 pay for the banyans), savanna baobabs only (`Look.baobab`). Birch bark is a real GenMat
(`GenMat.BirchBark` = 19, engine 147, palettes + Items / Tools; `FarField.GenToEngine` maps it generically). Hooks: `GatherTrees` ->
`GatherLib` (library trees appended as `Tree` entries, shape `SHAPE_LIB` | turn, prim0 = prototype), `OverlayTrees` -> `OverlayLib` first
(library wood, then leaves, AIR only; coarse bricks skipped by the prototype's brick flags at shells <= 2), `PointTree` -> `Trees2.At`;
`MaxContentY` + 760 (a redwood) only with the flag; scratch room via `TreeScratch.Ensure(n, extra)`. Flag `-trees2 1` (needs trees on);
tests `Trees2TestOverride`, `SetTrees2`, `LibTreesIn` (Burst gather, same view as the jobs). Tests `Trees2Tests`: `Library_Turns_Budget`
(Slow; two builds equal, turns invert and map bricks to bricks, bounds, expected bricks <= budget; `Logs/TerrainGen/trees210/library.txt`),
`Chunks_Deterministic_Border_PointAndShellsAgree` (Slow; an oak across a chunk border: runs equal, only air -> tree, FarQuery and shells
0-3 agree; `chunks.txt`), `Poses210` (Explicit; `poses.txt`). Shots `Logs/overnight/210_shots.sh` (20 poses, both arms), `210_far.sh`,
`210_final.sh`; table `Logs/overnight/210_table.py` -> `Screenshots/210/table210.txt`; `python3 -I tools/trees210.py` -> sheets + `viewer.html`.
Result: HANDOFF 210.
**Task 211 (plan only, owner 2026-11-14 Q-AE; no code, no launch, no tests): game design batch A folded in.** Claude Doc "Game design"
(https://claude.ai/artifact/BprGXbxRr6W5MPdDAXxYp8) + repo copy `GAME_DESIGN.md`: A1 (b) late first world flip (core loop Decided,
session row changed, Stages row), A2 (c) 2-3 cities per Medium world (How many, Size tiers, towns checklist: 187's 3 towns + 1 frontier
+ ~10 villages/outposts, ~1.35 km fair-share spacing), A3 (a) one humanoid generator for NPCs + player creator, A4 (a) wood/fruit/seeds/
regrowth (trunk block = wood; 6 new ids = 3 woods + 3 fruits of the 11 free GenMat ids 9, 18, 26-29, 38, 39, 59, 94, 95; seeds are
items), A5 (c) dynamic town safety (new Open section "Town safety by NPC behaviour": 5 rules, a needs table, questions C1-C5), A6 (a)
Terraria move-in rule + the DESIGN PRINCIPLE under the pitch ("keep all rules simple and depend on emergent behavior"). Result: HANDOFF 211.

**Task 209b (look, owner 2026-11-14 Q-AG (a): "still stream ... Pond overflow streams is exactly what I was thinking, just to make the world and ponds feel more natural"; task 204's option (A); DEFAULT ON, `-pocketstreams 0` = task 209 byte for byte).**
`TerrainGen/PocketStreams.cs` (header "TASK 209b"): every surface pond but quicksand gets narrow carved channels built the way
`Rivers.cs` builds rivers, AT REST (0 fluid tiles, 0 tick until touched). Network on the 8 m map: INLET = upstream from the pond's cell,
the higher neighbour that DRAINS INTO the current cell (D8 on `WorldMap.Height`) with the largest catchment `Flow`; stops at a keep-out
cell (sea, lake / river, route, feature, not Walk band, start zone / towns, another pond's box), a rise > 2 m per cell (spring at the cliff
foot) or 6-16 cells by pond size; nothing drains in = a one-cell spring. SURPLUS: OUTLET iff catchment area >= 16 x pond area (the
simplest water balance), down D8 to a river (water joins at the river's own level) or it ends (sealed by the lip rule); else a SEEP (a
lining-material trail from the low rim, one cell). Channel = Rivers' shaped section at stream scale: half width 3-6 voxels by
log2(Flow) (0.6-1.2 m wide), 0.25-0.35 m of fluid; course = Rivers' B-spline over the cell centres bent by Rivers' meander rule at the
stream's scale (2.5 half widths, one bend per 24 half widths, seeded noise on the phase), nodes every 12 voxels; level = 2 voxels under
the pond-shaped ground at the node, running minimum downstream, never under L upstream / over L downstream, AT L within the pond and
4 voxels + half width past its shore (flush junction). Banks: Rivers' carve / raise at stream scale (24-voxel reach, cut <= 1.2 m); LIP
rule as Rivers' (water lips = RiverLip drawn as water; other fluids' lips = the pond lining, 0 new ids). Seals kept: never carves under a
pond rim (unless that column becomes stream fluid at L), a river's hold or a lake rim; never carves / fills a column that already holds
water. Non-water channels LINED (`FluidPockets.Lining`) over [level - 6, level + 2]; no stamp opening within 0.8 m; caves stay 8 under the
bed. Texel 1 only (LOD 0, shells, FarQuery); the far band does not draw a 1 m stream. Storage: built once per world in
`FluidPockets.BuildPlane` and APPENDED to the site plane (`MapView.stream` = `PocketStreams.Section`), so every generator path carries it
(6 MapView sites + WorldMap.View); seed 42: 63.9 KB per plane copy, 177 ms build in the Editor / 20 ms in the player. The course is computed in doubles relative to the pond; still, the IL2CPP player's stream plane differs from the Editor's in a few node x values (fnv C5DE7646D4DF6CA9 vs CACEE583F814B040; z / width / depth hashes equal, logged per field): one-ulp rounding flips, each world self-consistent and at rest by construction (an integer course would make them byte-identical). Tests (`FluidPocketTests.Streams.cs`):
`Streams_Rules` (fast), `Streams_Seed42_AtRest_PathsAgree` (Slow: plane deterministic; OFF = 209's FNV 38AF614C8AECA569; every stream's
channel + 2 rings at rest + lined; per fluid the longest stream's shore and middle in the CPU fluid oracle 150 ticks + a negative control;
ON vs OFF only inside the reach; FarQuery = chunk; shell 0 = Generate; streams-off chunks = a fresh `-pocketstreams 0` source;
`Logs/TerrainGen/streams209b/streams.txt`, CPU top views `Screenshots/209b/cpu_top_*.png`), `Streams_Diag_CrossSections` (Explicit).
The pond tests now run with `StreamTestOverride = 0` (OneTimeSetUp). Shots `Logs/overnight/209b_shots.sh`, `Screenshots/209b/viewer.html`.
Result: HANDOFF 209b.

**Task 212 (8.17 T0, performance):** adopted amendment 8.17 (874f431). Cost split, capture only and opt-in (7232a42):
`-costarms flood_off` (torch flood off for the arm), `-costarms skyfill0` (sky fill 0 for the arm), `-costpaths cliff` (walk_near
+ task 199's cliff1 still pose, task 131's cliff1 A, sun 30 deg at 15.95 h; every path now carries its own sun hours). The runner
`Logs/overnight/212/measure.sh <tag> [arms]` does the warm-up, then one cost split with `$PLAY_SETTINGS_FLAGS -renderrows 432
-shellwait 90 -costpaths cliff`, and logs keyboard/mouse idle every 5 s (`Logs/overnight/212/hididle`, CoreGraphics per event type:
ioreg's HIDIdleTime is reset every ~10 s by a running player, so it cannot be used). Result (warm, AC, 432 rows): play walk_near
p50 106.8 ms, flood twin 27.8, sky fill 2 40.4, sun 28.1, shells 8.4, ceiling 24.6; cliff1 135.3. Next step T2, then T4 before T3.
Result: HANDOFF 212.
**Task 213 -- denser forest for the world trees (owner 2026-11-15 Q-AJ (a); still behind `-trees2 1`, default flip = task 214).**
`spec.trees2On` now has 3 values: 0 today's trees, 1 = task 213's DENSE placement (the meaning of `-trees2 1`), 2 = task 210's placement
byte for byte (`-trees2 1 -trees2dense 0`; `Trees2Mode()`, `SetTrees2Mode(byte)`, `Trees2TestOverride` 0/1/2). Dense mode (`Trees2.GatherDense`):
oaks (tier 1) alone on the 12.8 m grid (`lib.pqD`), the small trees (birch, spruce, acacia) on a FINE 6.4 m grid (`F_SHIFT` 6, root at [8, 56),
`SALT_F`, `lib.pqF`, 64 cells per 51.2 m square); forest budget `BUDGET_D` = 1.4 x 210's, oak 8/16 + birch 8/16 (snow / savanna keep 210's
budget, only their grid changes). Same site rules, tiers and Near ranges; rivals found through CELL INDEX maps (keep <= 96 voxels: 3 x 3 normal
cells for oaks, 5 x 5 fine cells for tier 2; `MaxCells` int scratch in `GatherLib`), so the gather and `Suppress192` are O(candidates) instead of
210's O(n^2) (a 4x finer grid at shell 5 would have been ~16x dearer). `MaxTrees` / `MaxCandidates` take `dense`. Full plane: forest 25.7 trees
per 51.2 m (210: 15.1). Calibration `Trees2Tests.Dense_Sweep` (Explicit) -> `Logs/TerrainGen/trees213/sweep.txt`; test
`Dense_Density_FootprintsAgree` (Slow: 210's counts pinned 109 / oak 17 / birch 92; 256 m forest window dense 233 trees vs 150 = 1.55x for
1.33x world tree bricks, <= 1.40 asserted; 16 chunk gathers == one 256 m gather; `density.txt`); `Library_Turns_Budget` also checks the dense
budget and grids. Shots `Logs/overnight/213_shots.sh` (3 arms d / s / t0, 4 poses), table `Logs/overnight/213_table.py` ->
`Screenshots/213/table213.txt`, `python3 -I tools/trees213.py` -> `sheet213.jpg` + `viewer.html`. Result: HANDOFF 213.

**Task 214 -- the world trees are the DEFAULT (owner 2026-11-15, Q-AJ (a): "the new trees become the default in every world").**
`TerrainGenChunkSource.Trees2Mode()` now defaults ON: every tg2 chunk source (play, rigs, capture runs, EditMode tests that build one)
gets `spec.trees2On` 1 = task 213's dense world trees (Trees2Library built once per process, 30 prototypes, ~10.6 MB, ~0.5 s on workers);
`-trees2dense 0` = task 210's placement; `-trees2 0` leaves `trees2On` 0 = today's (pre-210) trees byte for byte (the library is never
built). `-trees 0` still turns both off. As 195a / 201, the `TerrainSpec` field is NOT flipped (a bare spec stays 0). The tests that
pin today's trees pass the flag explicitly (`src.SetTrees2(false)`): `TreeTests` (its `Ctx`), `TreeGalleryTests` (`TodayTrees_Pinned`,
`Gallery_Chunk_AirOnly`, `Gallery_Layout`), `TreePoolTests` (task 188's arms), and `FluidPocketTests.Streams_Seed42_AtRest_PathsAgree`
(`Trees2TestOverride = 0`: its out-of-reach tree filter is IsTreeTrunk / IsTreeLeaf, which misses the birch's `GenMat.Leaves`; on the default
world its only out-of-reach change was 2,908 air <-> Leaves voxels at water_shore); `Trees2Tests` already sets `Trees2TestOverride`. No
assertion changed. Player log: `[StreamManager] tg2 chunk source: ... world trees ON (default, dense)` / `ON (210 placement,
-trees2dense 0)` / `OFF (-trees2 0)`. No identity/hash rig builds the tg2 world, so no rig needed the flag. Shots
`Logs/overnight/214_shots.sh` (default vs `-trees2 0`, 3 poses), table `Logs/overnight/214_table.py` -> `Screenshots/214/table214.txt`.
Result: HANDOFF 214.

**Task 215 (defaults, 2026-11-15, owner Q-AH (a)): `-dashfix 2` is the DEFAULT (task 205's white-dash fix, top faces only); `-dashfix 0` = the old call.**
`RaymarchFeature.DashFix` parses `-dashfix` with default 2 (play, rigs, capture; voxel / shell path only, the far band's call is unchanged; F6 has no
field). Survey arm suffix `_df0` still forces the old call per shot. Test `DashFixTests.Flag_Default2_LastMatchWins` pins the default. Check: one launch,
205's flags + `-gpupoolprobe 1`, arms `native_df0,native,dyn_df0,dyn` at forest_breaks / cliff_snow / cave_mouth (`Logs/overnight/215/shoot.sh`):
predicted dash pixels still dashing, cliff_snow 1,296 -> 6 native, 12 -> 1 FSR 3 dyn; forest_breaks 374 -> 0 native, 15 -> 0 dyn; pixels changed > 8
levels: forest dyn 0.51 %, cliff_snow native 1.09 %, cave_mouth native 0.16 % (205: 0.42 / 1.05 / 0.16). forest's native pair is NOT comparable
(the first arm was shot before the wall's near detail streamed in). No storage (one shader int). Pictures `Screenshots/215/sheet215.png`,
`zoom215.png`. Result: HANDOFF 215.

**Task 216 (bug, owner 2026-11-15 Q-AI (a): new eviction design approved; DEFAULT ON since task 218b, `-gpuviewevict 0` = 207's path).**
`TerrainClipmap.ViewEvict.cs`: when a chunk IN VIEW (or in the camera's 5x5) is refused by the full GPU body pool, mirror-box chunks that
are OUT OF VIEW and FARTHER (Chebyshev XZ) give up their bodies first, farthest first (ties: higher box slot), and only if the victims cover the
whole shortfall (nothing is cleared for a chunk refused anyway). Victims: held by their slot, in the box, not dirty, not touched by a live fluid
tile (`SetFluidWanted`), outside the 5x5, outside the frustum. A victim is written as air (entries, air mips, height, sealed mask, the refusal's
write), its bodies freed, and it joins the refused set with its body count: FarField's hand-over draws it (IsGpuRefused) and 207's retry brings it
back when it fits. The retry ranks requesters (in view / 5x5) before the rest and may evict for an in-view refused chunk too; with view eviction
on it also re-runs when the frustum changed (`StreamManager.ViewVersion`). The frustum is 208's (`StreamManager.ChunkInView`, handed over by
`TerrainClipmap.SetView` before every UploadDirty). One lazy scan of the box's slots per pass (only when a requester is short), one sort of a
reused list of packed longs, at most 16 victims per pass (their 16 KB entry writes are charged to the pass's bytes). No per-chunk storage, no
new GPU memory, no pool, layout or API change. Off / no view = 207's code path. Counters `GpuViewEvictedTotal`, `GpuViewEvictedBodiesTotal`,
`GpuViewEvictSavedTotal`, `GpuRefusedByView` (refused now split in view / 5x5 vs other), printed on `-mirrorprobe 1`'s new `[mirror gpuview]`
line; `UploadStats.viewEvicted / viewEvictBytes`. Tests `GpuViewEvictTests` (11: farthest-first order and landing, victims must be farther and
cover the shortfall, out-of-view far requesters do not evict, the 5x5 does, in-view chunks never are victims, off / no view = today, the retry
evicts when the view turns, ranks in-view first, off keeps 207's order, victims come back through the retry). Driver `Logs/overnight/216_pair.sh`
(b1 `-gpuviewevict 1` / b2 `0`, jungle_high + walk1/walk2, `-trees2 0`). NOT YET MEASURED: with today's defaults both arms of the first pair were
killed by the memory watchdog at jungle_high (9,088 / 9,782 MB footprint, KNOWN_ISSUES "JUNGLE_HIGH FOOTPRINT"), and the owner pause stopped the
rerun. Result: HANDOFF 216.

**Task 217 (plan only, owner 2026-11-15 Q-AK "recommended for all Qs"; no code, no launch, no tests): town safety C1-C5 decided.** Claude Doc
"Game design" + `GAME_DESIGN.md`: section "Town safety by NPC behaviour" is Decided as six rules (one danger map, 1 byte per 8 m cell ~0.55 MB
Medium; lights/rooms/people lower it; guards = residents walking between lights at night; a resident leaves on no valid room or high home danger
for a few days, checked once a day; a settlement = rooms/residents within ~64 m, no claim; size steps by resident count, e.g. 5/15/40). Missing
pieces table with a build order (0 furnished buildings in placement; 1 danger map, 2 room detection, 3 NPC day routine, 4 spawn rule before
character work). Settlements rows (react to edits, size tiers, own settlement, safety), A6 wording, placement checklist (64 m rule, danger-map
memory) updated. New questions C6 (64 m as chain vs radius) and C7 (anger and repair requests). Result: HANDOFF 217.

**Task 218 (bug, supervisor 2026-11-16, Q1 (a) of HANDOFF 216): the jungle_high 9-10 GB footprint = the point query's tree gather (FIXED, no switch).**
Isolation (`Logs/overnight/218_iso.sh <arm> <flags>`, 216's survey flags at jungle_high / walk1 / walk2 (+ `XPOSE=` an extra pose), sampler
`Logs/overnight/218_memsample.sh`: footprint every 4 s, `footprint` category snapshots at 2.0 / 3.5 / 5.0 / 6.0 GB, SIGTERM at 6.6 GB before the 8 GB
watchdog): `-trees2 0` peak 3,120 MB; trees ON: Untagged (native) 978 -> 5,114 MB while the main thread sat in the survey's `-mirrorprobe`
(`GenProbeLines`: one `FarQuery.GetVoxels` of ~1 M points). Cause: `PointTree` re-gathered per voxel column and `GatherLib`'s ~23 KB `Allocator.Temp`
scratch piles up inside FarQuery's single `IJobParallelFor` (Temp is given back only when the whole job ends). Fix: `GatherLib` scratch =
`UnsafeUtility.Malloc`/`Free`; `PointTree` gathers once per 128-aligned CHUNK column (Generate's footprint, `& ~127`) into `PointColumn.treeList` /
`treePrims` (raw Malloc'd pointers: a job may not create a Persistent NativeArray), freed by `PointColumn.Dispose()` at the end of each
`PointJob` batch. Same bytes (Slow `Trees2Tests.Chunks_Deterministic_Border_PointAndShellsAgree`, `FarQueryTests`, `TreeTests`). Tests:
`PointTreeMemoryTests.PointQuery_Trees2_FootprintFlat` (16,384 air points in distinct columns: footprint growth < 48 MB; was +332 MB / 4,060 ms,
now +0 / 377 ms; `Logs/TerrainGen/temp218/point.txt`), `TempAllocThreadProbeTests` (Explicit probe: Temp in `job.Run()` on a worker thread is
given back, in an IJobParallelFor it is not; `probe.txt`). Diagnostic flag `-memwatch <MB>` (`Assets/Game/MemWatch.cs`, default off): a
`[memwatch]` line every 10 s (footprint, Unity allocated / reserved, managed heap) and `MemoryLedger.Report()` once at <MB>; no line for > 10 s
= the main thread is stuck in one frame. Result: HANDOFF 218.

**Task 218b (bug proof, supervisor 2026-11-16: 216's Q2 (b)).** Ran 216's launch pair with today's defaults (world trees ON, 218 fixed the
jungle_high footprint): `Logs/overnight/218b_pair.sh`, one build, b1 = `-gpuviewevict 1`, b2 = `0`, jungle_high / walk1 / walk2, `-mirrorprobe 1`,
logs `Logs/overnight/218b/`, shots `Screenshots/218b/b1|b2/`. ON vs OFF: GPU refusals over the run 58 / 70 / 70 vs 61 / 81 / 81; refused in view
or 5x5 at the shot 22 / 6 / 0 vs 23 / 8 / 0; victims 17 (8,183 bodies), 13 requesters mapped by it; valve evictions 32,088 vs 35,204; CPU pool peak
496,264 vs 495,941; process peak 2,598 vs 2,510 MB, already reached at the first shot when the eviction had acted once (1 victim, 370 bodies), so
start-up load variance, not this code (it holds < 0.1 MB). Pictures: walk2 0 px differ, walk1 2,373 px (distant trees), jungle_high 36 k px in the
far top band; nothing missing in either arm. Default flipped ON (`Phase4Bootstrapper` `-gpuviewevict` default 1, `TerrainClipmap.ViewEvict = true`).
Small effect: the chunks still refused at the shot sit on the box's far edge, where no out-of-view chunk is farther. Result: HANDOFF 218b.
