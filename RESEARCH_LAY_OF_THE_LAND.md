# Research: Lay of the Land (task 222, 2026-11-16) -- what they do, what we could copy

Research only. No engine code, no Unity. Tags: **QUOTED** = the developer's own words (Tooley1998), **PLAYER** = a Steam user,
**INFERRED** = my reading, **SEEN** = what I saw in a frame. Nothing is filled in from memory. Where a source could not be read, it says so.

## Sources and what could not be read
- The dev's YouTube devlogs (@tooley1998, 30 newest listed) have **no caption track at all** (checked in each watch page's player data for
  8 videos; e.g. LOD video 70 s, sand video 127 s, UE5 video 236 s). INFERRED: they are music-only showcases with on-screen titles, so
  there is no spoken technical detail to transcribe. `yt-dlp` is not installed; a direct YouTube API call was refused by the sandbox. I used
  the video descriptions plus the public storyboard frames (one frame per 1-2 s) for the sand and LOD videos.
- "This is Lay Of The Land (EP 1)" (`_uY6UpM6pJY`) is NOT by the dev: it is a 17-min let's-play by the channel "Zanfer" (2024-05-17). Not used.
- X posts: not readable (402); tweet text below comes from search-result snippets only. PCGamingWiki: 403 (supervisor's note).
- Read in full: Steam store data (appdetails API), 83 Steam announcements/patch notes (news API), Steam forum threads with dev replies.

## Key facts
1. **Voxel size 10 cm** -- QUOTED (Steam forum, 2026-03-26): "Each voxel in the game is 10cm. There are 1000 voxels occupying the same
   volume of space as a single voxels in games that use 1m voxels." Same as our 0.1 m shell 0 (Amendment 8.14).
2. **Unreal Engine 5 + Lumen** -- QUOTED (UE5 devlog description, 2023-10-10): moved from UE4 "to make use of Lumen ... realtime dynamic
   global illumination". Patch 1.3.0 (2026-10-14): "Updated to Unreal Engine 5.8.3", "New low global illumination setting",
   "Lights held by the player now cast shadows". Hardware ray tracing is an option (recommended spec: "Hardware Raytracing Enabled").
3. **Terrain is meshed, not ray marched** -- INFERRED from QUOTED patch notes: "voxel mesh shadow imposters" (1.0.9, 1.0.22),
   "voxel nav mesh" (1.0.20), "Improved voxel rendering performance" (1.3.9), "LOD fading ... reduce visual pop-in" (2025-09-11).
   PLAYER: culling is visible in caves; on Linux "blocks/chunks ... flicker". Which mesher (greedy / per-face) is never stated.
4. **Finite, pre-generated world** -- QUOTED: "It is limited to a single landmass"; world creation "from about 2 minutes to 40 seconds"
   on a Ryzen 7 7800X3D (2026-07-27). Voxel data generation on CPU with Intel ISPC, "2-3x speedup", which "reduce[d] the amount of
   caching of voxel data" (tweet snippets, 2024). INFERRED: the world is generated once at creation and streamed from disk, not
   generated while you walk.
5. **GPU target** -- QUOTED (store + dev forum reply): MIN GTX 1660 Ti / RX 5600 XT, 16 GB RAM, **Low, 1080p native, 30 fps**;
   REC RTX 3060 Ti / RX 6700 XT, **High + HW ray tracing, 1080p native, 60 fps**. 1.3.2: new handheld settings "aim for a baseline
   30fps ... on Steam Deck without upscaling". Mac: QUOTED "I plan to investigate apple silicon mac support after launch."
6. **Memory is their weak spot too** -- QUOTED 1.0.4: "out of memory errors on some 16GB systems ... it seems to have a variety of
   causes"; 1.0.25: "approx. 1.5GB reduction on max settings"; 2025-09-11: "GPU VRAM usage has been significantly reduce[d]".
   PLAYER: "it consumes ... 11Gb of ram" (RX 9070 XT, 1080p).
7. **Reviews / performance**: 1,270 Steam recommendations (appdetails, 2026-11-16). PLAYER reports spread widely: RX 9070 XT at 4K
   "barely runs 30fps", 40 with RT off, 60 with FSR Balanced; GTX 1660 + Low + FSR "runs pretty well at 1080p"; RTX 4060 laptop
   "solid 60fps" at 1080p with lowered settings; lowest settings 15-30 fps until reflections were set to 0 in the ini, then 60-70.
   Patch notes since launch are mostly memory, shadow, "game thread", "voxel query" and simulation performance fixes.
8. **Simulation** (see the section below): sand/gravel/snow fall, water flows from source voxels, fire spreads, gas and acid; it runs
   only inside a "simulation update range" around the player. CPU vs GPU is never stated.

## GPU class vs our M1 Air (cited TFLOPS only)
| GPU | FP32 | source |
|---|---|---|
| Apple M1, 8-core GPU | 2.6 TFLOPS | [Apple newsroom, Nov 2020](https://www.apple.com/newsroom/2020/11/apple-unleashes-m1/) ("2.6 teraflops of throughput") |
| GTX 1660 Ti (their MIN) | 5.5 TFLOPS | [NVIDIA GeForce news, GTX 1660 Ti shaders](https://www.nvidia.com/en-us/geforce/news/geforce-gtx-1660-ti-advanced-shaders-streaming-multiprocessor) |
Their MINIMUM GPU is ~2.1x our M1 (a 7-core M1 would be ~2.4x; INFERRED by core ratio, not cited). Their minimum runs 1080p native at
30 fps = ~62 Mpx/s. Ours (task 219/212, High, 432 rows, walk_near p50 ~107 ms) is ~768x432 at ~9.4 fps = ~3.1 Mpx/s. Per TFLOP that is
~11 vs ~1.2 Mpx/s/TF, **a ~9x gap** (INFERRED, rough: different scenes, their Low preset, our pixel width assumed 16:9).

## Visual comparison
Picture: `Screenshots/222/side_by_side.png` (git-ignored, not committed). Left: ours, High, task 219 (`Screenshots/219/sheets/doc_sheet.png`).
Right: official Steam screenshots, **linked, not copied into the repo**:
[canyon](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/2776090/8483af5aaea3501c27304d71225acea1f95914fc/ss_8483af5aaea3501c27304d71225acea1f95914fc.1920x1080.jpg),
[forest](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/2776090/0a2cb8d849e98c33576131bb581b928588ab6e4a/ss_0a2cb8d849e98c33576131bb581b928588ab6e4a.1920x1080.jpg),
[swamp](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/2776090/0b3386341e51286510111f646250edffaaaed30e/ss_0b3386341e51286510111f646250edffaaaed30e.1920x1080.jpg).
- **Alike (SEEN):** the same voxel scale (a 10 cm cube reads the same size at eye height), cube-stepped ground, blocky grass, pale stone.
- **Differs (SEEN):** (1) **shade is lit** in theirs -- under trees and on walls the shadow side is warm/green bounce light; ours goes near
  black (jungle row). (2) **Every voxel face has its own colour/brightness variation** and dark crevices between voxels (AO), so surfaces
  read as material; ours are flat single colours. (3) **Aerial haze, bloom, volumetric clouds** -- their distance fades to soft blue-grey,
  ours has a hard saturated blue sky and sharp far hills. (4) **Trees**: big hand-shaped oaks with thick bent branches and dense
  leaf clumps; ours are thinner and sparser. (5) **View distance**: theirs is short and hidden by haze and trees (forest/LOD video frames
  show far hills only as soft silhouettes, a few hundred m INFERRED); ours draws real 3D terrain to km. (6) Water: theirs is turquoise
  translucent with visible flow (LOD video, SEEN). (7) Theirs is full of authored objects (houses, ruins, statues); ours is bare terrain.

## Large-scale simulation (owner addition)
The video is **"Simulating Thousands of Voxels in my Voxel Game"** (`7QZvYfAwgvw`, 2025-09-24, 127 s, no speech). On-screen titles (SEEN):
"Improved Sand Simulation", "Simulation Performance Improvements", "Improved Fire Simulation". Description QUOTED: "The voxel simulation
system has been completely refactored and is now much more performant ... increased speed and look much more natural. Moving sand ...
can hurt you or enemies if they become buried." Tweet (2025-07, snippet): "thousands of moving voxels can now be simulated with little
performance loss".
- **What:** sand (also gravel, snow -- QUOTED 1.1.9), fire spreading over grass and up a tree, water with "source" and "flowing" voxels
  (QUOTED 1.1.9: "source water voxels", "flowing water effects", "ghost flowing water effects"). INFERRED: water is a source/flow model
  (flowing voxels exist only while connected to a source), not a mass-conserving fluid.
- **How big:** SEEN in the storyboard: a dug pit wall and a cave ceiling collapse, sand pouring in over an area of a few metres; fire over
  maybe 10 m of grass. **Not large-scale.** "Thousands" of moving voxels = a few m^3 at 10 cm (1 m^3 = 1,000 voxels).
  The owner's "large-scale falling sand" recollection is not confirmed by any of the 30 videos' titles or this video's frames.
- **Tick rate, cell count, hardware:** not stated anywhere I could read.
- **Storage:** QUOTED "voxel physics could sometimes overwrite sim voxels in areas with lots of simulated voxels" (1.1.6),
  "regions stepping simulations while being unloaded" (1.1.14), "Voxel simulations ... no longer render when they go out of simulation
  update range" (1.1.14), "simulation render update performance" (1.1.10). INFERRED: per-region stepping of a list of active ("sim")
  voxels inside a range around the player, written back into the voxel data and re-meshed; no evidence of a separate dense grid.
- **CPU or GPU:** not stated. INFERRED CPU: crashes from "regions stepping ... while being unloaded" and "multiple voxel simulation updates
  ... in the same area in the same frame" are CPU-side data races, and the cost they fix is "render update" (re-meshing), not compute.
- **Ours:** GPU CA in 32^3 tiles (3.2 m, `ChunkFluidMask.TILE_EDGE`), pool 480 tiles (<= 15.7 M cells awake), awake-only residency
  (task 54: resting fluid costs nothing), fixed 6.5 Hz + adaptive drain, lava every 6th tick (tasks 91/97); every awake water cell is a
  conserved amount. **Do they get it cheaper?** Yes, in what they choose to simulate: a few thousand moving grains near the player and
  source-fed water that does not have to conserve volume. Our CA updates whole awake 32^3 tiles; a pour of 9,261 voxels cost an apply p99
  of 12 ms on the main thread (task 97). Ways we could too (options, no redesign): (a) **active list instead of whole tiles** -- step only
  cells that moved last tick (our awake-only tiles are the coarse version of this); (b) for falling sand/gravel, if we add it: a CPU worker
  over an active list, written back through `ChunkStore.SetVoxel` (sole-writer rule holds); (c) source/flow water for big bodies -- cheaper
  but **gives up volume conservation, a gameplay change: owner decision**.

## Table
| topic | what they do | source | ours | could we copy? |
|---|---|---|---|---|
| voxel size | 10 cm | QUOTED forum 2026-03-26 | 0.1 m shell 0, coarser shells to 12.8 m (8.14) | same already |
| terrain draw | meshed chunks, LOD fade, shadow imposters | INFERRED from QUOTED patch notes | 1-byte brickmap, GPU ray march | not without replacing the renderer (STOP_FOR_OWNER) |
| GI | Lumen (SW SDF or HW RT), Low GI = lower res | QUOTED 2023-10-10, 1.3.0; PLAYER on res | sky fill 2 (40.4 ms), torch flood (27.8 ms), task 212 | idea only: lower-res GI + upsample |
| sun shadows | UE5 shadows + "voxel mesh shadow imposters" | QUOTED 1.0.9/1.0.22 | ray-marched sun shadow (28.1 ms, 212) | idea: coarse proxy for far shadow casters |
| view distance | short, hidden by haze | SEEN, PLAYER complaints | real 3D to 6.6 km (shells, 8.4 ms) | no; haze yes |
| look | per-voxel colour noise, AO, bloom, haze, clouds | SEEN | flat colours, clear sky | yes, cheap post / shading |
| world | finite landmass, pre-generated (40 s-2 min) | QUOTED | streamed generation (throughput-bound) | gameplay change: owner |
| world gen | CPU, Intel ISPC (SIMD) | QUOTED tweet | Burst jobs on workers | already SIMD (Burst) |
| simulation | active sand/fire/water near player, source/flow water | QUOTED patch notes; INFERRED | GPU CA tiles, 6.5 Hz, conserved | partly, see options |
| target GPU | 1660 Ti (5.5 TF) Low 1080p 30 fps | QUOTED store | M1 2.6 TF, 3 GB cap | n/a: ~9x per-TF gap (INFERRED) |
| memory | 16 GB min, OOM reports, -1.5 GB patch | QUOTED | 3 GB cap, 8 GB Mac | they did not solve it either |

## Options (cost and risk; nothing here is approved work)
1. **Look polish they have and we lack** (per-voxel colour/brightness noise from position hash, cheap AO in the crevices, aerial haze,
   bloom). Cost: small shader work, est. 1-3 ms. Risk: low. Fits stage (1) "look fantastic" now.
2. **Lower-resolution sky fill / bounce + upsample** (Lumen's Low GI idea). Our sky fill costs 40.4 ms (212). Cost: medium; risk: blur and
   leaks at voxel edges; not exact, so it needs the owner's look approval. A performance-stage task.
3. **Cheap far shadow casters** (their "shadow imposters"): shadow rays beyond N m test a coarse shell instead of shell 0. Cost: medium;
   risk: shadow shape changes far away. Performance stage.
4. **Active-list stepping for the fluid CA / any future sand.** Cost: medium-large. Risk: moderate; sole-writer path unchanged.
5. **Pre-generate a finite world at creation** (removes generation from the streaming path, our known bottleneck). Large; changes the
   game's world model (finite) -- **STOP_FOR_OWNER material**.
6. **Meshed, rasterized near terrain** (their main technique). Would replace the ray-marched brickmap renderer and the GPU mirror's role:
   architecture redesign touching memory layout -- **STOP_FOR_OWNER material**. INFERRED, not proven: part of the ~9x gap may be ray
   marching vs rasterizing, but their GI is also cheaper per pixel and the scenes differ; no evidence isolates it.
Cannot copy: Lumen/Nanite themselves (UE5-only); hardware ray tracing on this M1 (INFERRED, not cited: M1 has no RT units).
