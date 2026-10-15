# Amendment 8.15 (DRAFT, NOT ADOPTED) -- Simulation reach: what is live, how far, and what happens far away

2026-10-15 · task 90a (planning only; no code, no builds, no launches). **Nothing here is approved.** Adoption is the
owner's call (questions at the end); CLAUDE.md is not edited by this draft.
Amends (if adopted): ARCHITECTURE_v8.6 §3.7 (GPU body index), §4.3 (the 128 m radius becomes a fixed NEAR), §7.4
(activity scope), §8.4-8.5 (projectiles, mass destruction); Amendment 8.14 rows 6, 7, 9, 11, 12 and §3; ENGINE_PHASE_PLAN
steps 14 and 19.
Inputs: `GAMEPLAY_DESIGN_ROUND_2026-10-15.md` (the requirement), `tools/overnight/blocked/87-engine-14-smaller-window.md`,
`AMENDMENT_8_14_LOD_SHELLS.md`, `ENGINE_PHASE_PLAN.md`, `FLUID_TICK_DIAGNOSIS.md`, `GAME_DESIGN_BRIEF_SNAPSHOT.md` open
Q12/Q13, KNOWN_ISSUES (3 GB cap, fluid pool), and the code as of `60ba585`.
A number marked **(est.)** is my estimate. Every other number names its source. Nothing was measured for this draft.

## For the owner

- **What you asked for.** Task 87 would have made the *live* world (digging, physics, fluid, NPCs) as small as the
  *sharp-drawn* world: 128 m -> 82 m on your M1. You stopped it. This draft keeps those two apart, the way most games do.
- **Three zones** (diagram below): **NEAR** = everything fully live, a fixed **128 m** on every machine (today's reach);
  **FAR-ACTIVE** = only the places where something big is happening (a fire, a flood, a lava flow, a sand slide, a
  town), simulated coarsely; **FROZEN** = everything else, paused and caught up when you come back.
- **Far events are real.** A far fire or flood writes its result as the same kind of "edit op" far craters already use
  (task 81). When you return, the burned forest or the flooded valley is there, because the generator applies those ops.
- **Your builds show from anywhere** (brief Q12 "yes, always"): every edited chunk also saves a small coarse summary that
  the distant shells and far band draw. Today block edits do not even show past ~69 m when shells are on (section 5).
- **Memory:** keeping 128 m of live world costs about **+230 MB** against task 87's version (est.), but the GPU side
  shrinks like 87's: the whole process lands near **~2.37 GB, ~2.47 GB with the light grid** (est.; 2,999 MB measured today).
- **Size:** **17 tasks, ~26 sessions (~32 with retries)**, against ~5 sessions for steps 14 + 19 as planned. About half
  of it (fire, floods, nukes) can wait until after the lighting steps if you want the engine phase to finish sooner (Q10).
- **Ten questions** at the end, recommended answer first. "All (a)" approves the draft.

```
   Top view, High preset (M1). Not to scale beyond 300 m.

   +-------------------------------- FROZEN (whole island, paused) --------------------------------+
   |                                                                                                |
   |      [FAR-ACTIVE tile: forest fire 1 km away]          [FAR-ACTIVE: town on routines]          |
   |       coarse 0.8 m cells, 1 tick/s, writes ops          no terrain sim; lights, schedules      |
   |                                                                                                |
   |               . . . . . . . CPU ring edge, +-204 m (awake fluid stays here, 8.14 Q4) . . .     |
   |               .        +------------ NEAR, 128 m: full 0.1 m sim ----------+                 . |
   |               .        |  GetVoxel / SetVoxel / physics / fluid CA / 50-70 |                 . |
   |               .        |  creatures.  Drawn at 0.1 m only inside 82 m;     |                 . |
   |               .        |    +--- drawn 0.1 m (shell 0), 82 m ---+         |                 . |
   |               .        |    |              @ player              |         |                 . |
   |               .        |    +------------------------------------+         |                 . |
   |               .        |  82-128 m: live at 0.1 m, DRAWN by shell 1        |                 . |
   |               .        +--------------------------------------------------+                 . |
   |               . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . |
   |   Shells 1-7 and the far band DRAW everything to 6.6 km: generator + ops + edit summaries.     |
   +------------------------------------------------------------------------------------------------+
```
*Caption: the inner box is what is drawn at 0.1 m; the middle box is what is live at 0.1 m (unchanged from today). Far
events live in small FAR-ACTIVE tiles anywhere; the rest of the island is frozen but still drawn, with your edits.*

## 0. What exists today (code read 2026-10-15)

| thing | state | where |
|---|---|---|
| Live reach | CPU load radius 10 chunks = 128 m; fluid active radius 1,280 voxels = 128 m | `EngineConfig.cs:268`, `:479` |
| Drawn 0.1 m reach, shells on | shell 0's hand-over (69 m on High); beyond it shell 1 draws | `Raymarch.compute:3625` (`tTier0`) |
| Shells' content | generator + D.4 ops only; **no D.1 block edits** | `ShellStreamer.cs` header, `:418-422` |
| Far edits | `IEditOps.Record`: Sphere=1, Box=2, 3 reserved (prefab); redrawn at once in shells and far band | `EditOpLog.cs:46`, tasks 81/82 |
| Fluid | 32³-cell tiles, 0.5 MiB each, 480 in play (task 55); awake-only tiles (task 54); one tick per ~7 frames (task 91 queued: fixed 4.5 Hz) | `EngineConfig.cs:439-461`, `FLUID_TICK_DIAGNOSIS.md` §1 |
| Reactions | water+lava -> obsidian, acid dissolves rock, steam (a gas) | `MaterialRules.cs:139-163` |
| Powders | sand and beach sand fall (~45° repose, §7.5) | `MaterialRules.cs:78,120` |
| Fire | **none.** An `IsFlammable` flag exists (oil); no fire material, no burn reaction | `MaterialRules.cs:37,99` |
| GPU mirror | directory 32×32×8 chunks; body buffer = the CPU pool's capacity, indexed by the CPU handle (§3.7) | `TerrainClipmap.cs:225-241` |
| Memory | peak **2,999 MB** at poses with shells on, tiers 1-2 still allocated (task 85, `390e0de`) | 3 GB cap: 1 MB to spare |

## 1. Render distance vs simulation distance

### 1.1 How other games split them (from general knowledge, not looked up)

| game | drawn | simulated | far away |
|---|---|---|---|
| **Minecraft** (Java / Bedrock) | "Render distance" 2-32 chunks; Distant Horizons mod adds LOD to many km | separate "Simulation distance" (Bedrock 4-12 chunks; Java has the same slider). Mobs, redstone, crop growth, fire only inside it. Hostile mobs despawn beyond 128 blocks (= 128 m) | **frozen, no catch-up**: crops do not grow while you are away (a well-known complaint) |
| **Terraria** | one screen | enemies near players; but the whole 2D world is in memory and random tile updates (grass, trees, evil spread) and liquid settling run everywhere | town NPCs persist world-wide |
| **Noita** | a few 512 px chunks | only the chunks round the player; fire and sand stop dead at the edge | frozen and saved; fire resumes when you return |
| **Valheim** | ~far LOD | ~5×5 zones of 64 m round each player; in co-op the zone's "owner" client simulates it | frozen |
| **Factorio** | screen | **everything, always** (lockstep deterministic, built for multiplayer); idle machines sleep | n/a |
| **Rimworld / Dwarf Fortress** | one map | the map you are on | the world map runs an abstract event sim (caravans, raids, history) |

**What we copy:** (1) two separate distances: drawing is cheap far away (shells, 8.14), simulation is not;
(2) Minecraft's 128 m for creatures and their terrain; (3) Noita's freeze as the default, but (4) Terraria's/Rimworld's
**coarse far simulation for the few things that must keep going** (owner batch 3: fire, lava/floods, powders, town life);
(5) Factorio's lesson for co-op: far simulation must be deterministic so it can be replayed, not streamed (§3.5).

### 1.2 Our zones

| zone | radius | what runs | terrain truth | cost driver |
|---|---|---|---|---|
| **DRAWN-FINE** | shell 0's hand-over + 12.8 m: 59 / 65 / **82** / 116 m (Low/Med/High/Ultra, 8.14 §H.2) | nothing extra; only *what is drawn* at 0.1 m | the CPU chunks below | GPU mirror |
| **NEAR** | **128 m, every preset** (10 chunks; today's) | `GetVoxel`/`SetVoxel`, physics, CCD, projectiles, fluid CA, powders, near fire, creatures | CPU 0.1 m chunks (§3.4) | CPU brick pool, generation |
| NEAR fluid extension | awake-fluid chunks inside the 32-chunk ring (±204 m) stay resident (8.14 Q4 (a), row 11) | fluid CA only | CPU chunks | fluid tiles |
| **FAR-ACTIVE** | anywhere, sparse tiles of 51.2 m | coarse fire / fluid / powder / town routines at ~1 tick/s | world map + ops + edit summaries; writes D.4 ops | far-tile cap (§3) |
| **FROZEN** | everything else | nothing | generator + ops + D.1 on disk | 0 |

**Why 128 m (derived from the eye, as the task asks).** A 1.8 m creature is h = c·1.8/d px tall, c = rows / vFOV
(8.14 §1: 515.7 at 540 rows, 687.5 at 720, 1,031 at 1,080). It is **10 px tall at 93 m (540 rows), 124 m (720, your
M1's High) and 186 m (1,080, Ultra)**. 10 px is about where a figure stops reading as a creature (est.). So 128 m covers
everything the player can recognise and aim at on Low-High; on Ultra a creature 128 m away is 14 px and then gone, which
is the same edge Minecraft has. **NEAR must not depend on the preset** (8.14 §6 risk row "game rules must not depend on
the preset"): a save, a co-op partner on another machine and every game rule see the same live world. 128 m also equals
today's reach, so nothing that works now (fluids, rigs, gates C/D) changes.
- Rejected: NEAR = the drawn radius (task 87: 59-116 m by machine, varies per player); NEAR = 154 m (12 chunks,
  +42% 0.1 m columns and generation, ~+110 MB, est.) -- Q1 (b).
- **Cost of 128 m at speed (est.).** NEAR is 441 chunk columns against 87's 225 on High; generation need rises from
  ~3.4 to ~5 columns/s per m/s (8.14 §H.2's Ultra row, a 116 m window, is the nearest figure). The streamer already
  orders by distance (task 85), so **the drawn 82 m fills first, exactly as with 87**; what lags while flying fast is the
  live 82-128 m ring. Game logic that asks there before the chunk arrives uses the far query (§7). Step 16 measures it.

## 2. Step 14 redone: shrink only what is DRAWN

87 shrank the CPU load radius, the GPU mirror box and the brick pool together (8.14 row 6). Redone, it shrinks the GPU
side only, and keeps the CPU at 128 m:

1. **CPU load radius stays 10 chunks on every preset.** `WINDOW_CHUNKS_*` unchanged (32×32×16; CLAUDE.md landmine).
2. **GPU mirror box 32×32×8 -> 16³ on Low/Medium/High** (Ultra keeps today's box), pow2 asserted. Resident chunks outside
   it are already skipped and counted by task 32's out-of-box rule; shell 1 draws them.
3. **The GPU gets its own brick-body pool** (cap = measured `PeakUsed` of the drawn box × 1.25; 8.14 estimates 242 k on
   High) with its own free list and a CPU-handle -> GPU-slot table filled on upload. **This changes §3.7** ("a mirror of
   the CPU pool's bodies", indexed by the CPU handle): a layout change, not an API change, and `ClipmapValidator` compares
   through the table. Without it the GPU body buffer stays sized to the CPU pool and only the directory saves (Q2 (b)).
4. **CPU brick pool cap** 750 k -> measured peak × 1.5 (379,527 over 21×21 columns, task 55 -> ~570 k; × 1.5, not
   8.14's × 1.25, because players build and blast, est.). The §3.6 valve line stays at 0.85 × cap.
5. **Prerequisite: block edits must reach shell 1 first** (section 5, task E1). Otherwise a wall you build 90 m away
   is live but invisible.
6. Free cascade tiers 1-2 whenever shells draw (they stay allocated today, task 80): -193 MB measured (task 55 ledger:
   79.0 GPU + 67.6 native + 46.1 managed).

| High preset (M1) | today (measured, task 55 ledger) | task 87 / 8.14 §3 (est.) | 8.15 (est.) |
|---|---|---|---|
| live reach | 128 m | **82 m** | **128 m** |
| CPU 0.1 m bricks | 366 MB (750 k cap) | 118 MB | 278 MB (570 k cap) |
| GPU mirror: directory + mips + managed copy + bodies | 513 GPU + 147 managed = 660 MB | 246 MB | ~266 MB (64+10 dir/mips, 74 managed, 118 bodies, 2 table) |
| cascade tiers 1-2 | 193 MB | 0 | 0 |
| **saving against today** | -- | **-855 MB** | **-675 MB** |

(8.14 §3's -467 MB is net of adding the shells; the shells are already inside today's 2,999 MB, so against *today's
measured peak* 87 would save -855 and 8.15 saves -675 MB, est. The difference, **~180 MB, is the price of keeping
128 m of live world**; §9 adds ~50 MB of new far systems.)

## 3. Far simulation, written as edit ops (absorbs step 19 / E7)

### 3.1 The shape

```
  NEAR (0.1 m CA, GPU, 4.5 Hz fixed tick, step 17)            FAR-ACTIVE (CPU Burst, ~1 Hz, deterministic)
  +----------------------------+   chunk with awake fluid /    +---------------------------------+
  | fluid / fire / powder at   |   fire is evicted (§4.4       | tile 51.2 m = 64×64 cells of    |
  | 0.1 m, writes via SetVoxel |-- Resident->Saving) -------->| 0.8 m: fuel, burn state, fluid  |
  +----------------------------+   hand-OUT: D.1 saved, then   | depth+material, powder height   |
            ^                      cell state seeded            +---------------+-----------------+
            | hand-IN: chunk generates with the far ops,                         | commits D.4 ops
            | edge cells woken (fluid) / fire seeded                              v  (main thread, IEditOps.Record)
  +---------+------------------+                               +---------------------------------+
  | generator + ops<=S + D.1 + |<------- applied on generation | ops.log (D.4 v2): BurnMask,      |
  | ops>S (8.14 row 10)        |         shells + far band     | FluidLevel, PowderSettle         |
  +----------------------------+         redraw at once (82)   +---------------------------------+
```
*Caption: the CPU stays the only terrain writer (§0.1 invariant 1). The far sim never touches chunks; it only records
ops, which the generator applies exactly as it applies far craters today.*

- **Cells and tiles.** A far cell is a 0.8 m column (8×8 voxels, the far band's finest texel and shell 3's voxel);
  a tile is 64×64 cells = 51.2 m = 4×4 chunks. All pow2: cell = `voxel >> 3`, tile = `voxel >> 9` (§0.1 invariant 2).
  A tile holds 8 B per cell (ground height 2, fluid depth 2, fluid material 1, fuel/burn 2, flags 1) = 32 KB.
- **Sparse.** Tiles exist only where something is active: a hard cap `MAX_FAR_TILES` = 512 (16 MB, 1.3 km² live at
  once; est.) in `EngineConfig` (invariant 8), pooled at start-up (invariant 3), refused-and-counted when full like the
  fluid pool (task 55). A debug overlay draws them (invariant 7). A tile with no change for N ticks commits its ops and
  goes back to FROZEN.
- **Inputs.** Ground height and surface material per cell from the world map + far band data + ops + edit summaries
  (section 5); fuel from biome × landform + vegetation (Q5); no 0.1 m chunk is generated for it.
- **Tick.** One far tick per second (a divisor of step 17's fixed rate; task 91). Cost (est.): ~20 ns per cell ->
  80 µs per tile -> 41 ms per tick at the 512-tile cap on one worker, about 4% of one core.
- **Output = ops.** New D.4 kinds (header `formatVersion` 2; a version-1 log loads unchanged; additive, flagged in §10):
  - **4 BurnMask** {tile, 64×64 bit mask}: in masked cells, flammable surface materials -> their burnt product
    (grass -> ash/soil, wood -> charcoal or air, leaves -> air). One table in `MaterialRules`.
  - **5 FluidLevel** {tile, material, 64×64 levels (2 B)}: per cell, fluid of that material above the level -> air,
    air below it -> fluid (a flood rises, a drained lake falls). Lava cooled by the far sim -> **6 ReplaceMask** (lava ->
    basalt/obsidian).
  - **7 PowderSettle** {box}: powder in each column drops onto the first solid below (no repose spread; the near CA
    finishes the pile on return).
  - Ops commit when a tile goes to rest, leaves FAR-ACTIVE, or every 60 s (crash safety). Op log budget ~16 MB per
  world (est.); a spatial index per tile keeps generation from scanning every op (today `TouchesChunk` loops over all,
    `ShellStreamer.cs:418`).
- **Determinism (co-op later, owner batch 1).** Integer state, fixed tick, tiles processed in sorted order, randomness
  = hash(seed, cell, tick). Same inputs -> same ops on host and client, so co-op replicates the op log, not the sim.
  The near GPU CA stays non-order-stable (§7.8) and host-authoritative.

### 3.2 Per phenomenon

| | far model (est.) | real on return via | visible while far |
|---|---|---|---|
| **Fire** (forest fire, 1 km) | burn state per cell; spreads to neighbours by fuel, slope and wind; burns out after its fuel | BurnMask | glow + smoke: a sparse list of burning cells (≤ 64 k) drawn as emissive at the shells/far band, and fed to far lights (step 21, aggregated per far cell, owner 2026-10-15) |
| **Lava / floods** | 2.5D "virtual pipes" shallow-water over cell heights; lava every 6th far tick, cools at edges | FluidLevel, ReplaceMask | the far band already draws water depth (task 75); FluidLevel redraws shells + far band at once (task 82's path) |
| **Powders / collapses** | no structural integrity (owner): resolved instantly per column | PowderSettle | redraw at once |
| **Town life** | schedules, NPC positions on a route graph, lights at night; **no terrain writes** unless a game rule records ops | (records, gameplay phase) | far lights (step 21) |

**Fire needs a near model first.** There is no fire in the engine (section 0). Near fire = a CA material (Fire as a
short-lived gas, Smoke, Ash) plus burn reactions on `IsFlammable`, inside today's §7.6 reaction table. Task N1.

### 3.3 Hand-out and hand-in

- **Out** (a chunk with awake fluid or fire leaves the CPU ring, 204 m): the eviction is the normal §4.4 `Resident ->
  Saving -> Unloaded`; D.1 saves the voxels as they are; then the chunk's awake fluid volume and burning voxels seed the
  far cells. Any later far op has seq > S, so row 10's precedence (ops ≤ S, D.1, ops > S) already gives the right answer.
  **§4.4 is unchanged.** Between 128 m and 204 m awake fluid stays resident at full detail (8.14 Q4 (a)).
- **In** (a FAR-ACTIVE tile comes within the ring): commit its ops, drop the tile; chunks generate with them; fluid
  cells at a FluidLevel edge are woken (the wake queue, task 54); burning cells seed near fire on flammable surface
  voxels (seeded hash).
- This **replaces step 19a** ("resident patches beyond the window" needed a new state or store outside the ring: 8.14
  row 11 flagged it as a redesign). Step 19b (render-only river flow) is already done by task 76.

## 4. Catch-up on return (the default for everything else)

What resolves at once when a chunk re-enters NEAR, inside the load, in Burst, on worker scratch (single-writer kept):

| thing | rule | needs |
|---|---|---|
| fluid frozen mid-flow (fluid that was not awake-resident: e.g. a save/quit) | after ≥ 30 s away, each connected body in the chunk settles flat at the same volume (step 18's levelling rule); edges woken | step 18; world clock |
| powder in the air | wakes and falls (the near CA) | none |
| far-sim results | already in the ops (section 3) | -- |
| growth (crops, trees, grass regrowth) | state = f(planted time, now), evaluated on load; no ticking | a world clock; an `OnChunkResident(chunk, secondsAway)` hook for game systems |
| creatures | wild ones were despawned; spawn fresh by biome (gameplay) | -- |

Engine owes: a **persistent world clock** (game seconds, saved), and **last-seen time per chunk** in a side table
(like `ops.log`'s; D.1's bytes stay frozen). Bounded: catch-up work per frame is capped, the rest next frame.

## 5. Far builds visible (brief Q12, owner "yes, always")

**Today's gap (from reading the code, not seen in a shot):** shells are generator + ops (section 0), so with shells on a
block-by-block wall is drawn only inside shell 0's band (~69 m on High), even though it is live to 128 m; past the CPU
window it shows nowhere. 8.14 row 12 planned "downsample the resident chunk" but it was never built.

**Proposal: an edit summary per edited chunk.**
- When a chunk with D.1 edits is saved (eviction or autosave), the same Burst pass also builds its **shell-1 summary**:
  the resident chunk downsampled 2× by majority vote (as today's cascades do), kept only for bricks that differ from
  `GenerateShellChunk` at shell 1. Coarser shells and the far band reduce it further at use (cheap).
- Stored in a new file **D.5 `summary/` (one per edited chunk)**, `.tmp` + atomic rename, CRC, written right after
  D.1; it carries D.1's CRC so a stale summary is ignored (invariant 11).
- Applied in `GenerateShellChunk` after ops ≤ S and before ops > S (row 10's order), at every shell; the far band
  folds it into column height and material (2.5D, as task 82's op patch).
- **Live, inside NEAR:** an edited resident chunk outside the drawn box is re-summarised at most once a second and
  redrawn through task 82's gap-free replace. This is also step 14 redone's prerequisite (section 2.5).
- **Cost (est.):** shell-1 resolution is 1/8 of D.1's voxels -> save size **+~12%** of D.1; RAM: an index of edited
  chunks (16 B each) plus an LRU of loaded summaries, **10-30 MB** for a 1,000-chunk megabase; ~1 ms of Burst per
  summary on save.
- Rejected: regenerate + decode D.1 + downsample whenever a shell needs it (Q3 (b)): no new file, but one full 0.1 m
  generation per edited chunk per shell redraw (a 1,000-chunk base = ~90 s of worker time each time it comes into view).

## 6. Nukes 50 m+ near the player: a debris budget

A 50 m sphere is 5.2 × 10⁸ voxels. §8.5's 400 k per 2-3 frames would take ~100 s. Instead:
- **Carve = one Sphere op** (task 81's path, applied at once to resident chunks). Whole chunks and bricks inside become
  uniform air in O(1); only the ~3 × 10⁶ boundary voxels (~50 k bricks) need per-voxel work, split over frames under
  §8.5's budget. Beyond NEAR, the op already carves shells and the far band.
- **Debris budget per blast** (all in `EngineConfig`, est.):

| becomes | how much | cost |
|---|---|---|
| rubble powder, **at rest**, a layer on the crater floor | 10% of the carved volume, capped at 200 k voxels | no fluid slots until disturbed |
| awake fluid (a lake breached) | ≤ 64 tiles awake (32 MB of the 240 MB pool); the rest flash-settles: the lake's new level is computed and written as one FluidLevel op | bounded tiles; no "pool full" |
| fire | flammable surface in the outer ring ignites: near fire capped; beyond NEAR a FAR-ACTIVE fire tile | caps above |
| dust, smoke, flash | render-only particles (≤ 4 k), no voxels | GPU only |
| the rest | vanishes into the loot tally (§8.5's Proxy Drop) | 0 |

## 7. A far query API (NEW, beside the frozen `GetVoxel`)

`IWorldQuery.GetVoxel` is **unchanged and still frozen**: resident chunks only, i.e. NEAR. This adds a **new** API:
- `IFarQuery.GetVoxels(NativeArray<int3> points, NativeArray<byte> results)` as a Burst job. Answer per point:
  resident chunk -> else D.1 brick if that chunk's delta holds it (an LRU of parsed delta indices) -> else generator
  (`DetailField` column + `ShapeField`, a pure function of (x, z) and y) with ops ≤ S, then ops > S applied. **Exact**,
  and equal to what the chunk would generate (test: random points vs generated chunks, as `EditOpTests` does).
- `IFarQuery.Raycast(origin, dir, maxDist)`: a coarse march over far-band columns + ops, refined with point queries
  near a hit. For spells and NPC sight beyond 128 m.
- Fluid far away = generated water + FluidLevel ops (the FAR-ACTIVE tile's live state is readable through the same call).
- **Cost (est.):** 1-3 µs per point in Burst (the column pass is ~125 ns after task 88; `ShapeField` ~0.6 µs per
  column), so ~1,000 queries per frame fit in ~1-3 ms on one worker.
- Not a redesign: no frozen signature or §4.4 row changes. Game rules may use it anywhere; they must not assume
  `GetVoxel` reaches further than NEAR.

## 8. Creatures

| | now (engine) | gameplay phase |
|---|---|---|
| ~50-70 wild near the player | NEAR = 128 m fixed: every creature stands on resident terrain, `GetVoxel` + CCD as §8.1-8.2 | spawn/despawn by biome (despawn beyond NEAR, as Minecraft's 128) |
| entity physics (brief Q13: PhysX vs voxel probes) | nothing | the 10 / 50 / 100 NPC measured test, as the brief says |
| town NPCs, named creatures | persistence = records, never residency; a town is a FAR-ACTIVE "town" tile only for its lights/routines (no terrain sim) | routines, schedules, combat |
| NPC sight / pathing beyond 128 m | the far query (§7) | uses it |
| town lights at night | far lights (step 21) aggregate per far cell | places lights |

## 9. Memory under the 3 GB cap (High preset, M1)

| | MB | source |
|---|---|---|
| **today, peak at poses, shells on** | **2,999** | measured, task 85 (`390e0de`) |
| free cascade tiers 1-2 | -193 | task 55 ledger |
| GPU mirror box 16³ + own GPU body pool | -394 | est. (§2) |
| CPU brick cap 750 k -> 570 k | -88 | est. |
| edit summaries (cache + index) | +10 to +30 | est. |
| far tiles (512 × 32 KB) + burning-cell list | +17 | est. |
| far query caches, D.4 spatial index | +10 | est. |
| **8.15 total** | **~2,370** | est. |
| light grid (step 20, already planned) | +100 | plan §2 (est.) |
| **with the light grid** | **~2,470** (~530 MB left for the game) | est. |
| task 87's path, for comparison | ~2,140 (+100 = 2,240) | est. (§2) |
| 8.15 without the GPU body pool (Q2 (b)) | ~2,620 (+100 = 2,720) | est. |

## 10. Contract changes

| # | status | what | amends |
|---|---|---|---|
| 1 | CHANGED | §4.3's "128 m LOD0 radius" becomes **NEAR = 128 m on every preset** (load radius 10). 8.14 row 6's load radius 5/6/7/10 is withdrawn; its GPU box half stays | §4.3; 8.14 row 6 |
| 2 | CHANGED (layout, not API) | GPU body buffer gets its own pool and handle table | §3.7 |
| 3 | UNCHANGED (frozen) | `GetVoxel`, `SetVoxel`, D.1 bytes, §4.4 table | -- |
| 4 | NEW beside frozen | `IFarQuery` (§7) | 8.14 row 7 |
| 5 | CHANGED (additive) | D.4 `formatVersion` 2, op kinds 4-7 with payloads; v1 logs load unchanged | 8.14 row 9 |
| 6 | NEW | D.5 edit summaries (§5); world clock + chunk last-seen side table (§4) | Appendix D |
| 7 | CHANGED | §7.4's "active radius" becomes NEAR + awake-resident fluid in the ring + FAR-ACTIVE coarse fluid | §7.4; plan §4 row 4 |
| 8 | CHANGED | §8.5 mass destruction: big blasts are one op + the debris budget (§6) | §8.5 |
| 9 | UNCHANGED | the CPU is the only terrain writer: the far sim writes ops, the generator applies them | §0.1 inv. 1 |

## 11. Tasks (one change each) and where they slot in

S = one session, M = 1-2, L = 3+. Every task: tests + metrics/screenshots; no frame-time claims (rule 10).

| # | task | size | needs | replaces plan step |
|---|---|---|---|---|
| E1 | Edit summaries, live: resident edited chunks outside the drawn box redraw into shells 1+ | M | -- | 8.14 row 12 |
| E2 | Edit summaries, saved: D.5 on save, applied in shells and far band | M | E1 | (brief Q12) |
| D1 | Free cascade tiers 1-2 when shells draw | S | 90 (shells default) | 14 |
| D2 | GPU mirror box 16³ per preset; CPU load radius stays 10 | M | E1 | 14 |
| D3 | GPU own brick-body pool + handle table (§3.7) | M-L | D2 | 14 |
| D4 | CPU brick cap from `PeakUsed` × 1.5 + one memory ledger (counts, not timing) | S | D3 | 14 |
| C1 | World clock + chunk last-seen side table | S | -- | -- |
| C2 | Catch-up on load (fluid settle, powder wake, `OnChunkResident` hook) | M | C1, 18 | -- |
| Q1 | `IFarQuery.GetVoxels` (exact: resident / D.1 / generator + ops) | M | -- | -- |
| Q2 | `IFarQuery.Raycast` (coarse + refine) | S-M | Q1 | -- |
| O1 | D.4 v2: op kinds 4-7 + per-tile spatial index | M | -- | 19 |
| F1 | Far-sim framework: tiles, cap, 1 Hz tick, determinism tests, debug overlay, hand-out/in | M | O1, 17 | 19a |
| F2 | Far fluid (lava, floods) | L | F1, 18 | 19a |
| F3 | Far powder settle | S | F1 | -- |
| N1 | Near fire (Fire/Smoke/Ash CA + burn reactions) | M | 17 | -- |
| F4 | Far fire + glow/smoke overlay | M | F1, N1 | -- |
| B1 | Big-blast path + debris budget | M | O1, F2 | -- |
| (G) | Town routines, NPC physics (brief Q13) | -- | gameplay phase | -- |

**Order (Q10 (a)):** after 91 (step 17): E1 -> 89/90 (shells default) -> D1 -> D2 -> D3 -> D4 -> C1 -> Q1 -> Q2 ->
E2 -> 18 -> C2 -> O1 -> F1 -> F3 -> F2 -> N1 -> F4 -> B1, with steps 20-22 (lighting) after D4 if the owner prefers
lighting sooner (they need 14's memory, not the far sim). Step 16 (streaming milestone) runs after D4.

**ETA (est.):** S = 1 session, M = 1.5, M-L = 2, L = 3 -> **~26 sessions, ~32 with retries**. Steps 14 + 19 as planned
were ~5 (M + L). Net **+~21 sessions (+~27 with retries)**, about **+1.5-2 weeks** of queue time at the terrain phase's
pace (35 tasks in a week), longer when the weekly limit binds. Of it, the "engine owes now" part (E1-E2, D1-D4, C1,
Q1-Q2: ~13 sessions) unblocks steps 16 and 20; the rest (catch-up, far sims, fire, nukes: ~13, ~16 with retries) can
follow lighting.

## 12. Risks

| risk | answer | checked at |
|---|---|---|
| Generation at 128 m NEAR lags while flying (outer ring) | nearest-first order keeps the drawn 82 m identical to 87; far query covers the gap | step 16 |
| Op log grows with far sims | commit only at rest/leave/60 s; per-tile index; compaction later if > 16 MB | F1 tests (log size per simulated hour) |
| Summary and D.1 disagree after a crash | the summary carries D.1's CRC; mismatch -> ignored (the shell shows the generator) | E2 tests |
| Coarse far fluid and the near CA disagree at the hand-in edge | edge cells wake; the near CA settles them (FluidAtRestTests pattern) | F2 tests + one shot |
| Fire everywhere is a fun killer (owner may want limits) | caps in EngineConfig; game rules (rain, firebreaks) later | N1/F4 shots |
| The GPU pool remap is a §3.7 layout change | validator compares through the table; one task (D3) alone | D3 |

## Questions for the owner

Option (a) is recommended in each. "All (a)" approves the draft as written.

1. **Q1. How far is the world fully live (NEAR)?**
   (a) **128 m on every machine** (today's; a creature is 10 px at 124 m on your M1). Cost: ~+180 MB against task 87.
   (b) 154 m (12 chunks): ~+110 MB more and ~40% more generation (est.).
   (c) Per machine, as 87 did (59-116 m): saves ~180 MB, but live reach differs between players and saves.
2. **Q2. Step 14 redone: what shrinks?**
   (a) **GPU directory box AND a separate GPU brick pool** (§3.7 layout change): -394 MB (est.).
   (b) Directory box only: -147 MB (est.), no §3.7 change; ~250 MB less room for the game.
   (c) Nothing on the GPU now; revisit palette bricks (8.13) instead.
3. **Q3. Far builds visible (brief Q12).**
   (a) **Edit summaries saved beside D.1, live inside NEAR**: +~12% save size, 10-30 MB RAM (est.).
   (b) Regenerate + decode D.1 when a shell needs it: no new file, ~90 s of worker time per 1,000-chunk base each time it comes into view.
   (c) Only op-made edits show far away (not "always").
4. **Q4. Which far sims, in what order?**
   (a) **Framework + floods/lava first (replaces step 19), powders, then fire once near fire exists; town life in the gameplay phase.**
   (b) Fire first (needs near fire first anyway).
   (c) Only floods/lava now; fire and powders after the gameplay phase starts.
5. **Q5. Far fire model.**
   (a) **2.5D cells of 0.8 m, fuel from biome × landform + vegetation, burnt result written as ops; glow and smoke drawn far.**
   (b) 3D coarse voxels (shell 3): exact overhangs and caves burn, ~4-8× the cost (est.).
   (c) Visual-only far fire: cheaper, but the forest is unburned when you return (against your answer).
6. **Q6. Catch-up on return.**
   (a) **Instant on load: fluid settles flat, powder falls, growth by timestamp.** Bounded per frame.
   (b) Fast-forward the near CA for a few seconds: closer to "real", costs a hitch on arrival.
   (c) No catch-up: exactly frozen (Minecraft).
7. **Q7. Nuke debris.**
   (a) **One carve op; 10% becomes resting rubble (≤ 200 k voxels); ≤ 64 fluid tiles awake, the rest settles as a level; dust is render-only.**
   (b) Everything becomes simulated powder and fluid (Noita-true): breaks the fluid pool and frame time.
   (c) Pure carve, no debris.
8. **Q8. Far query.**
   (a) **Exact (resident -> D.1 -> generator + ops), plus a coarse raycast.** ~1-3 µs per point (est.).
   (b) Far-band heights only: cheap, wrong in caves and under overhangs.
   (c) None now; far game logic stays abstract.
9. **Q9. Determinism for co-op later.**
   (a) **Far sim deterministic (integer, fixed tick, seeded hashes); near GPU CA host-authoritative (as §7.8 today).**
   (b) Make the near CA deterministic too: a CPU-only near sim, large cost.
   (c) Ignore until co-op is built (risk: the far sim has to be rewritten).
10. **Q10. When?**
    (a) **"Engine owes now" first (E1-E2, D1-D4, C1, Q1-Q2, ~13 sessions), then lighting (steps 20-22), then the far sims (~13 sessions).**
    (b) Everything in this draft before lighting.
    (c) Only E1 + D1-D4 now; the rest when the gameplay phase needs it.
