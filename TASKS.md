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

**Task 161 (perf, `PERF_EXACT_CUTS.md` cut B, supervisor 2026-10-31): kernel diet 2, `-diet2 1|0` (last match; default see HANDOFF 161).**
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
