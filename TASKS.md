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
the difference images, crops and sheets. Report: `VISUAL_REVIEW_114.md`. Top defects: olive side faces and staircases, a
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
