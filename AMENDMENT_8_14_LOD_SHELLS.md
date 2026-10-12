# Amendment 8.14 (ADOPTED 2026-10-11) -- LOD shells, an edit-op log, and a smaller 0.1 m window

2026-10-10 · task 60 (engine plan step 9, planning only). Revised 2026-10-11 · task 62: shells scale to the hardware.
**ADOPTED 2026-10-11** (owner, in chat: Q43 "Approve all (a)"; recorded by task 74). Owner answers:
Q1 replaced by hardware-scaled shells (§H; the M1 gets High = 720 rows); Q2 (a) rule A, hand over where the finer
voxel reaches 1 px; Q3 (a) op log and D.1 by order of time (seq side table, D.1 frozen); Q4 (a) awake fluid stays
resident inside the 32-chunk CPU ring; Q5 (a) coarse-first pass line = centre sample, >= 97% solid/air, >= 99% heights
within 1 coarse voxel; Q6 (a) far band kept beyond the last shell; Q7 (a) four presets Low/Medium/High/Ultra;
Q8 (a) auto-pick = lowest of memory/GPU/CPU tiers, no benchmark; Q9 (a) preset change applies on restart;
Q10 (a) the preset sets the default FSR 3 row range; Q11 (a) render range never changes on its own.
Built so far: nothing before task 74 (engine step 10, coarse-first generation). The text below is the draft as
approved; "draft" in it means this document.
Amends: ARCHITECTURE_v8.6 §3.7, §4.2, §4.3, §6.4, Appendix A/D; 8.9 §2-3; 8.10 (cascade tiers 1-2).
Supersedes: 8.11 (a draft, never adopted; its "Option B, a true cascade" is what this is).
Inputs: `ENGINE_PHASE_PLAN.md` §3 step 9 and §4 rows 1-4, owner answers Q1 (a), Q2 (a), Q38 (a); owner answers to
this draft's Q1-Q6 (2026-10-11: Q2-Q6 (a); Q1 replaced by hardware-scaled shells, §H below).
A number marked **(est.)** is my estimate. Every other number names its source. Nothing was measured for this draft.

## For the owner

- **What changes.** Today's 3 voxel tiers (0.1 / 0.2 / 0.4 m) draw voxels only out to ~128 m; the flat 2.5D far band draws the rest. They become **up to 8 nested cube shells**, 0.1 m to 12.8 m voxels. Each shell is twice as coarse and reaches twice as far, out to 6.6 km, up and down as well as sideways.
- **New in this revision: it scales to the machine.** The shell sizes, how far they reach and the size of the 0.1 m area are not fixed. They come from a **quality preset** (Low / Medium / High / Ultra) picked on first launch from the machine's memory, GPU and cores, and changeable in the F6 menu. Your M1 8 GB gets **High**: everything below that was already in the draft (§2a, §3) is the High preset. Low and Medium cover weaker machines, Ultra stronger ones (§H).
- **What it buys.** Real 3D terrain to the horizon. On High the 0.1 m area shrinks from 128 m to ~80 m, which frees **~470 MB** of the 3 GB (est.). A late chunk shows a coarser shell instead of a hole.
- **What it does not buy: frame rate.** The main march (~19 ms facing a wall, ~8 ms on an open view, task 57) is not touched. Shells should cost about the same as today's tiers plus far band (−1.6 to +2.7 ms, est.). Frame rate stays FSR 3's job (dynamic rows, task 52); the preset only sets which row range FSR 3 starts with.
- **Costs.** Steps 10-14 as already planned (five steps, two of them L), plus a small preset/auto-pick step (S, est.). On High, full detail keeps up only to **~7-11 m/s** (est.). Faster than that, the next coarser shell shows for a moment (your Q2 (a)).
- **Frozen, and still frozen:** `GetVoxel`, `SetVoxel`, the D.1 delta bytes and the §4.4 state table. **New beside them:** an edit-op log in its own save file, for edits far away.
- Your answers to Q1-Q6 are recorded at the end. **Five new questions (Q7-Q11).** "All (a)" approves the draft as written.

## H. Scaling to the hardware

### H.1 How shipped games do it (from general knowledge, not looked up)

- **Minecraft (Java):** "Render distance" 2-32 chunks and a separate "Simulation distance", plain menu sliders. Every chunk is full detail, so memory and generation grow with distance² (doubling the distance costs 4x). No auto-detect on Java.
- **Distant Horizons (Minecraft mod):** beyond the normal render distance it draws simplified LOD terrain whose detail halves with each step out, built on CPU threads and cached. Settings: a quality preset (horizontal/vertical LOD quality) and an LOD render distance. Very long range for little memory, because each farther ring is coarser.
- **Unreal Engine scalability:** per-group levels Low / Medium / High / Epic / Cinematic (view distance, shadows, textures, effects, ...). View distance quality sets `r.ViewDistanceScale` (cull distances); HLOD replaces far geometry with merged proxies. A first-launch auto-detect runs a short synthetic CPU/GPU benchmark and picks the levels; the player can override any group.
- **Unity:** `QualitySettings` levels per platform, no automatic detection; games read `SystemInfo` themselves.
- **How defaults are picked in practice:** a GPU/RAM/VRAM class table (often a vendor/model list), sometimes a short benchmark, always a manual override saved to a settings file. Memory-heavy settings (texture quality, view distance in some games) often say "requires restart".
- **Frame rate:** consoles and most modern PC games hold frame rate with dynamic resolution (DRS + an upscaler). View distance stays a fixed menu setting; it does not move on its own.

**What we copy:**
1. One overall preset, auto-picked on first launch, saved, and overridable (Unreal and most PC games).
2. Distant Horizons' shape, not Minecraft's: each farther shell is twice as coarse, so **range is cheap** (one more shell = ~10-120 MB, §H.2) and **the 0.1 m area and the sizing rows are what cost memory**. The presets mostly differ there.
3. Auto-pick from a device-class table on `SystemInfo`, **no benchmark**: on this fanless M1 a warm chip runs generation up to ~2.2x slower than a cold one (tasks 27/28), so a 10-second first-launch benchmark would measure the temperature, not the machine.
4. Dynamic resolution (FSR 3 dyn, task 52) holds frame rate; the preset is static while playing (§H.5).

### H.2 The presets

Each preset fixes the **sizing rows** (the internal render rows the shells are sized for; §1's c = rows / vFOV) and the **shell count**. Everything else follows from §1-§2's formulas. The FSR 3 row range defaults to "top = sizing rows, bottom = ¾ of that", like your M1's 540-720 (Q10). With rule A (Q2 (a)) a coarser voxel then always arrives at 1.5-2.0 px, on every preset.

Method (est., geometry only, the same as §2): shell k's hand-over is c·0.1·2^k m. Rings: shell 0 stores ±(⌈c/128⌉+1) chunks (the +1 is the 12.8 m prefetch margin), shells 1+ store ±⌈c/128⌉ minus the finer shell's covered ±(⌊c/256⌋−1). Brick densities and MB per brick as §2 (3.36 / 2.93 / 2.33 / 2.0 bricks per brick column; pools capped at peak × 1.25, ~0.73 MB per 1,000 coarse bricks incl. occupancy and pages). Shell 0 = directory + CPU and GPU brick bodies at the cap. "Rest of the process" = **1,483 MB** for every preset = §3's ~2,338 MB at poses minus High's 855 MB terrain (task 53 + task 55 ledgers); this amendment does not scale it (render targets grow a little with rows, est. < 100 MB). Generation capacity on the M1: warm 23-37 cols/s at 4 workers (task 28). For other machines it is **(est.)** linear in performance cores: 2-core class ~12-18, 8-core class ~46-74 cols/s.

| | **Low** | **Medium** | **High = M1 8 GB (headline, rigs)** | **Ultra** |
|---|---|---|---|---|
| meant for | weak iGPU (Intel / AMD APU), ≤ 2 GB VRAM, < 7 GB RAM, ≤ 2 P-cores | 8 GB PCs with a 3-6 GB GPU | Apple M1/M2/M3 base, 8-16 GB; PCs with a 6-10 GB GPU | ≥ 16 GB RAM, ≥ 10 GB GPU or Apple Pro/Max, ≥ 6 P-cores |
| sizing rows (FSR 3 dyn default) | 480 (360-480; 720p output) | 540 (405-540) | **720 (540-720, task 52's play default)** | 1080 (810-1080) |
| shells (render range in 3D) | 6 (to 1.5 km) | 7 (to 3.3 km) | **8 (to 6.6 km)** | 7 (to 6.6 km) |
| beyond the last shell | far band (Q6 (a)) | far band | far band | far band |
| 0.1 m window radius | 46 + 13 = **59 m** | 52 + 13 = **65 m** | 69 + 13 = **82 m** | 103 + 13 = **116 m** (today: 128 m) |
| CPU load radius (`EngineConfig.cs:268`, today 10) | 5 | 6 | 7 | 10 |
| rings: shell 0 / shells 1+ (chunks, XZ; all pow2) | 16 / 16 | 16 / 16 | 16 / 16 | 32 / 32 |
| shell-0 GPU directory | 16³ flat | 16³ flat | 16³ flat | 32×32×8 flat (today's box) |
| pool cap, shell 0 (bricks) | 130 k | 182 k | 242 k | 474 k (today: 750 k) |
| pool caps, shells 1+ (bricks, total) | 288 k | 475 k | 665 k | 1,158 k |
| terrain: shell 0 (CPU + GPU + dir) | 255 MB | 305 MB | **364 MB** | 718 MB |
| terrain: shells 1+ (GPU only) | 169 MB | 277 MB | **388 MB** | 677 MB |
| terrain: far band + ChunkStore/StreamManager | 102 MB | 102 MB | **102 MB** | 102 MB |
| **terrain total** (today 1,322 MB) | **526 MB** | **684 MB** | **855 MB** | **1,497 MB** |
| of it on the GPU (shell 0 GPU half + shells + far band) | ~320 MB | ~460 MB | ~600 MB | ~1,060 MB |
| **whole process** (rest 1,483 MB + terrain) | **~2.0 GB** | **~2.2 GB** | **~2.3 GB (< 3 GB cap)** | **~3.0 GB** (cap 4 GB on ≥ 16 GB) |
| chunk columns to fill after a teleport | 521 | 841 | 1,164 | 2,052 |
| fill time at PrimeWindow's M1 rate (157 cols/s, `55_pool_ledger.out`) | ~3 s | ~5 s | ~7 s | ~13 s |
| generation needed per m/s of speed | 2.3 cols/s | 3.0 | 3.4 | 5.1 |
| needed at walking speed (5 m/s) | 11 cols/s | 15 | 17 | 26 |
| **full detail keeps up to**, on its target class | **5-8 m/s** (12-18 cols/s) | **8-12 m/s** (M1-like) | **7-11 m/s** (M1, measured capacity) | **9-14 m/s** (46-74 cols/s) |
| ... the same preset on the M1 | 10-16 m/s | 8-12 m/s | 7-11 m/s | 4.5-7 m/s |
| a coarser voxel arrives at, over the FSR 3 range | 1.5-2.0 px | 1.5-2.0 px | 1.5-2.0 px | 1.5-2.0 px |

Notes on the table:
- **Why the presets differ mostly near the camera.** A coarse shell is ~10-120 MB; shell 0 is 255-718 MB. Low saves 329 MB against High, and 4/5 of that is shell 0 and the sizing rows, not the 2 dropped shells. Low still drops shells 6-7 because each extra shell is one more DDA restart per sky ray (§4 step 5) and more far march on a weak GPU (est. 0.2-1 ms per shell on the M1); the far band is the cheaper horizon there.
- **Ultra's shell 0** is today's 0.1 m area almost exactly: its 21×21 resident columns are the same footprint whose measured peak was 379,527 bricks (`55_pool_ledger.out`), and it keeps today's 32×32×8 GPU box, so shell 0 covers ±51 m vertically and shell 1 takes over above and below sooner than sideways (est.: fine, near vertical views are short). A 16-tall box would double the flat directory to 512 MB. It is still ~310 MB below today's terrain because the pool cap falls from 750 k to peak × 1.25.
- **Ultra's shells 5-6** are cut by the 8 km world (est.): shell 6's 819 m chunks fit only ~10×10 in the world, so it holds ~51 columns, not 312.
- **Low cannot go much lower with this amendment alone.** 1,483 MB of the 2.0 GB is not terrain. A machine under ~6 GB RAM would also need the fluid pool cut (task 55: 1,024 → 480 tiles saved 272 MB; 240 tiles would save ~140 MB more, est.) and the shell-0 flat directory replaced by the two-level one (row 2; ~−100 MB, est., a §3.7 change). Neither is proposed here.
- **Only High can be checked on real hardware here.** Low, Medium and Ultra can be run on the M1 for correctness and memory (`-quality low` etc.), but their frame rate on the machines they are meant for cannot be measured in this workflow. Their numbers stay (est.).

### H.3 Auto-pick on first launch

On the first launch (no `PlaySettings.json`, or `quality = "auto"`), a pure function `QualityAuto.Pick(DeviceInfo)` reads `SystemInfo` once and takes the **lowest** of three tiers:

| tier | Low | Medium | High | Ultra |
|---|---|---|---|---|
| memory: `systemMemorySize` (MB) | < 7,000 | -- | 7,000-14,999 | ≥ 15,000 |
| GPU, discrete: `graphicsMemorySize` (MB) | < 3,000 | 3,000-5,999 | 6,000-9,999 | ≥ 10,000 |
| GPU, Apple (`graphicsDeviceVendor` / name "Apple") | -- | -- | M-series base | name has "Pro", "Max" or "Ultra" |
| GPU, PC integrated (Intel; AMD "Radeon(TM) Graphics" / "Vega" without a model number) | always | -- | -- | -- |
| GPU, name not recognised | -- | always | -- | -- |
| CPU: performance cores (`CpuTopology`; else `processorCount` / 2) | ≤ 2 | 3 | 4-5 | ≥ 6 |

- **Apple unified memory counts once.** On Apple silicon `graphicsMemorySize` is a share of the same RAM (from general knowledge: Metal's recommended working set, roughly ⅔-¾ of RAM; not yet logged on this Mac, the first build of this step prints it). So the memory tier reads `systemMemorySize` only and the GPU tier reads the GPU name, never the VRAM number. The same holds for PC integrated GPUs.
- **The memory tier has no Medium.** An 8 GB machine fits High's ~2.3 GB (that is the M1 case); Medium is reached through the GPU or CPU tier.
- **VRAM is a speed proxy, not a fit test.** Even Ultra's GPU terrain (~1.06 GB) plus fluid (277 MB, task 55) fits a 3 GB card. The thresholds follow card generations, as shipped games' tables do.
- **Worked examples (est.):** M1 Air 8 GB, 4 P-cores → memory High, GPU High, CPU High → **High**. M2 Pro 16 GB, 6-8 P-cores → **Ultra**. Intel MacBook Air with Iris → **Low**. A 16 GB laptop with a 4 GB GTX 1650 and 4 cores / 8 threads → memory Ultra, GPU Medium, CPU High → **Medium**. A 32 GB desktop with an 8 GB RTX 3070 and 8 cores → **High**.
- The pick and its inputs go to the player log in one line (`quality auto: high (ram 8192, gpu 'Apple M1' apple, pcores 4)`), and the result is saved, so the next launch does not re-detect unless the player sets "Auto" again.

### H.4 The F6 menu and what a change costs

- Task 58's `PlaySettings` gets one field, `quality`: `auto | low | medium | high | ultra` (default `auto`), and the flag `-quality <name>` overrides it for one run, as every other field. F6 shows a "Quality preset" row.
- **A change applies on the next launch** (Q9 (a)); the row says "(restart)". Why: shells, the 0.1 m pool, the GPU mirror box and every directory are allocated once at start-up (row 1). Changing them live needs a teardown-and-re-prime path through the single-writer streaming system (stop workers, drop every pool, rebuild rings, re-prime, a few seconds of coarse view, est. size M) that nothing else needs, and every frozen API would have to survive it. A restart reuses the start-up path every rig already runs. Shipped games mark memory settings "requires restart" for the same reason.
- The preset's FSR 3 row range is only a default: the F6 "Render rows" row still overrides it live, as today. If the chosen rows go above the sizing rows, the row's hint says coarse voxels will show at more than 2 px.

### H.5 Frame rate, and whether range ever changes on its own

- **Presets scale memory and view distance. FSR 3 dynamic rows (task 52) scale the frame.** They are separate jobs.
- **The preset sets the default FSR 3 row range (Q10 (a))**, because the shells are sized from rows (§1). Rows lower than the sizing waste memory and march; rows higher show coarse voxels at more than 2 px.
- **Render range never changes on its own while playing (Q11 (a)).** Most games treat it as a menu setting and let dynamic resolution hold the frame rate. A horizon that comes and goes when the frame rate dips is more visible than a softer image, and the pools are fixed at start-up anyway. Q2 (a) already handles the one runtime shortfall that matters (generation falling behind): a coarser shell shows for a moment, and the range does not move.

### H.6 What every preset must keep

- **Powers of two.** Every ring, horizontal and vertical, is a power of two in chunks and indexed with `&` (CLAUDE.md invariant; §6.2 phantom terrain). The layout function rounds every ring up to a power of two and asserts it; one EditMode test per preset checks it, and a stale-slot validator per shell (as `ClipmapValidator.CountStaleSlots`) runs for every preset.
- **`WINDOW_CHUNKS_Y` stays 16, and the CPU ring stays 32 × 32 × 16 for every preset** (CLAUDE.md landmine; several oracle tests build 16-tall windows). Presets change only the load radius inside it (5-10 chunks).
- **The CPU stays the sole terrain writer** (row 3), and only the main thread mutates pools and tables, on every preset.
- **Frozen APIs untouched:** `GetVoxel`, `SetVoxel`, D.1, §4.4. A preset changes how far `GetVoxel` reaches (59-116 m), not its contract.
- **Unchanged constants:** 128-voxel chunks of 16³ bricks of 8³ (`EngineConfig.cs:61-62`), the 3 MiB/frame upload cap (§0.2), vFOV 60° (`LODConfig.cs:62`).
- **Rigs pin `-quality high`** and never auto-detect or read `PlaySettings.json` (as today, task 58). High is the M1 preset all of §2a-§4 describes, so the first shells rig runs start the new baseline on the preset the draft's estimates are for.

## 1. The rule that sizes the shells

- A voxel of V metres covers 1 px at distance **c·V**. c = rows / vFOV (radians), from §6.4 C.5 at the design vFOV of 60° (`LODConfig.cs:62`). **"rows" is the preset's sizing rows (§H.2).**
  - 480 rows: c = 458.4 (0.1 m at 46 m). 540 rows: c = 515.7 (0.1 m at 52 m). 720 rows: c = 687.5 (0.1 m at 69 m). 1080 rows: c = 1,031 (0.1 m at 103 m). This is the same formula as plan E2.
  - `LODConfig.PixelAngleRad` uses the screen-centre pixel instead, which gives 468·V at 540. Screen edges see voxels up to ~1.4x larger in px (est.). The tables use c.
- **Two possible hand-over rules (Q2, answered (a)):**
  - **Rule A ("finer reaches 1 px").** Shell k ends at c·V_k, where its own voxel shrinks to 1 px. The next shell's voxel therefore arrives at 2 px. **Chosen.**
  - **Rule B ("never coarser than 1 px").** Shell k ends at 2c·V_k, where the NEXT voxel is 1 px. That is the plan's wording. It needs 4x the area per shell.
- **Evidence for rule A.** On 2026-09-25, moving the tier bounds from today's ~1.6 px hand-over to "coarser ≤ 1 px" (93.5 / 187.1 m) was built and measured. It was no better to look at (tier-1 pixels differed from an all-0.1 m frame by 11.7/255 vs 11.4/255), and it cost **+6 ms** on the in-air orbit (54.4 vs 48.4 ms). The visible difference was late coarse DATA, not voxel size (`LODConfig.cs:50-58`).
- **Powers of two.** Every ring is a power of two in chunks, horizontally and vertically, and is indexed with `&` (CLAUDE.md invariant; §6.2 phantom terrain). Each shell's "chunk" is 128 of its own voxels (12.8·2^k m), the same 16³ bricks of 8³ as today (`EngineConfig.cs:61-62`).
- **Hollow shells.** A shell stores no chunk that the finer shell fully covers. Holes and edges snap to whole chunks.
- **Vertically too.** Each shell is a cube (except Ultra's shell 0, §H.2). The world is 1.1 km tall (−500 to +600 m, design doc §2 line 92), so shells 4-7 hold the whole height in 2-8 layers.

## 2. Shell tables (rule A)

Method (geometry only, nothing run): shell k has V = 0.1·2^k m, chunk E = 12.8·2^k m, hand-over r = c·V (capped at 6.6 km); it stores chunk offsets ±⌈r/E⌉ minus the finer shell's fully covered ±(⌊r_prev/E⌋−1), at most the 8 km world; Y layers ≤ ⌈1,100 m/E⌉+1; flux = √2·v·(side + hole)/E. Columns: ring = the pow2 ring in chunks, XZ x Y. Cols = chunk columns stored (hollow). Bricks = dense bricks (est.): columns × 256 brick columns × a density per brick column. The densities are **measured** for 0.1 / 0.2 / 0.4 m (3.36 / 2.93 / 2.33, from the peaks 379,527 / 82,641 / 16,437 over 21×21 resident columns: `Logs/overnight/55_pool_ledger.out`, task 53 HANDOFF) and **2.0 (est.)** for 0.8 m and coarser.
Shell 0 is today's 0.1 m CPU window and its GPU mirror. Its radius adds a one-chunk prefetch margin (12.8 m). MB = bodies (512 B on GPU; shell 0 also on CPU) + 64 B occupancy per brick (est.) + directory. Flux = chunk columns entering per second in level diagonal flight at 60 m/s (the worst heading).
The two tables below are the High (720) and Medium (540, which stops at shell 6) presets in detail; Low and Ultra are summarised in §H.2.

### 2a. High preset: sized for 720 rows (the M1 8 GB headline)

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

### 2b. Sized for 540 rows (Medium uses shells 0-6)

| shell | voxel | covers (m) | ring | cols | bricks | MB | cols/s at 60 m/s |
|---|---|---|---|---|---|---|---|
| 0 | 0.1 m | 0-52 (+13 margin) | 16×16×16 | 169 | 145k | 270 | 86 |
| 1 | 0.2 m | 52-103 | 16×16×16 | 112 | 84k | 49 | 46 |
| 2 | 0.4 m | 103-206 | 16×16×16 | 112 | 67k | 39 | 23 |
| 3-6 | 0.8-6.4 m | 206-3,300 | 16×16×(16..4) | 112 each | 57k each | 34 each | 12 / 6 / 3 / 1.5 |
| 7 | 12.8 m | 3,300-6,600 | 16×16×2 | 27 | 14k | 8 | 0.5 |
| **all** | | to 6.6 km | | **868** | **540k** | **503** | **178** |

### 2c. What each sizing costs, side by side

Pool caps are set at peak × 1.25, as task 53 set the cascade caps. The rule B columns are kept for the record; Q2 (a) rejected them.

| | A at 720 (High) | A at 540 (all 8 shells) | B at 540 | B at 720 |
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

## 3. Memory ledger, before and after (High preset, the M1 8 GB headline)

"Today" = task 55's ledger at the end of the coast pool probe (`Logs/overnight/55_pool_ledger.out`). Terrain owners only; everything else is unchanged. Low / Medium / Ultra totals are in §H.2.

| owner | today GPU / native / managed (MB) | after (MB, est.) | why |
|---|---|---|---|
| TerrainClipmap (0.1 m GPU mirror) | 513.1 / 0 / 147.4 | 246 | Directory 32×32×8 → 16×16×16 chunks (256 → 128 MB, GPU + managed copy; `TerrainClipmap.cs:226-241`); bodies follow the pool. |
| BrickDataPool (0.1 m CPU bricks) | 0 / 366.2 / 0 | 118 | Cap 750,000 → ~242,000 (194k peak × 1.25, est.), set from `PeakUsed` after step 14. |
| LODCascadeManager (tiers 1-2) | 79.0 / 67.6 / 46.1 | 0 | Replaced by shells 1-2. |
| **Shells 1-7 (NEW)** | -- | 388 | GPU only; the CPU keeps no copy. |
| FarField (far band) | 27.2 / 13.0 / 0 | 40 | Kept beyond the last shell until step 23 (Q6 (a)). |
| ChunkStore + StreamManager | 0 / 13.0 / 49.5 | ~62 | Unchanged. |
| **Terrain total** | **1,322** | **~855** | **−467 MB (est.)** |

- Whole process today: phys_footprint peak 3,077 MB over poses (task 53, before task 55's −272 MB fluid cut), 2,869 MB walk (task 53), 2,472 MB coast probe (task 55).
- After shells: poses ~2,340 MB (est.) = 3,077 − 272 − 467. That is under the plan's 2,500 MB engine target and leaves room for the light grid (~100 MB, step 20).
- Per preset (est., §H.2): Low ~2.0 GB, Medium ~2.2 GB, High ~2.3 GB, Ultra ~3.0 GB.

### Upload budget

- The cap stays 3 MiB per frame (`EngineConfig.cs:150`, §0.2) on every preset. Shells share it with the 0.1 m mirror.
- Need at 60 m/s (High): 85 MB/s (est.). That is 2.8 MB/frame at 30 fps and 1.4 MB at 60 fps, but only if generation could keep up, which it can't (above). At 5 m/s: 0.23 MB/frame (High); Ultra needs ~1.5x that (est.).
- **Order:** the coarsest missing shell chunk first, then finer ones, nearest the velocity first (§4.3 prefetch ordering). A coarse chunk is cheap and removes a hole, while a missing fine chunk only looks blockier (Q2 (a)).

## 4. Frame time, step by step (all est., High preset)

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
- Other presets: the march cost per pixel is about the same (every preset hands over at the same px size); the frame scales with the FSR 3 rows the preset starts at. Low's 6 shells skip the two farthest DDA restarts.

## 5. Contract changes

| # | Status | What | Amends |
|---|---|---|---|
| 1 | **CHANGED** | 3 tiers become up to 8 shells, **sized at start-up from the quality preset, not at compile time.** `LODConfig`'s `TIER_*` arrays become a runtime `ShellLayout`, built once by a pure function `ShellLayout.From(preset)`: sizing rows, shell count, per-shell voxel size, hand-over distances, pow2 ring sizes (asserted), shell-0 margin and load radius, pool caps (peak × 1.25 of the §H.2 estimate until a measured `PeakUsed` replaces it; only High can be measured here). Read-only after start-up. Every shell pool, directory and the GPU mirror box are allocated from it at start-up. Shaders read shell count and bounds from a constant buffer (arrays sized for 8), not from `#define`s. Still parameterised, as 8.9 §5.1 asks. | §6.4; 8.9 §2-3; `LODConfig.cs:27-92` |
| 2 | **CHANGED** | Cascade tiers 1-2 (`CascadeTierPool`, `LODCascadeManager`, chunk-major entries tied to tier-0 slots) are replaced by one shell pool per shell. Each shell has its own pow2 ring and a **two-level directory** (NEW, Appendix A): a 4-byte entry per ring chunk (absent / uniform material / page) and a 16 KB page of brick handles only for non-uniform chunks. | 8.10; Appendix A |
| 3 | **UNCHANGED** | **The CPU is the sole terrain writer** (plan §4 row 2), on every preset. Workers generate shell chunks with Burst into scratch they own; the main thread uploads; the GPU only stores. Every shell directory is zeroed at construction (the `TerrainClipmap.cs:243` lesson). | §3.2, §3.7 |
| 4 | NEW (later, only if needed) | **GPU-built render shells** (from seed + op log) are the step-16 fallback, and only if step 16 shows a gap that Q2 (a) cannot hide (owner Q2 (a)). The CPU would keep the truth, but GPU and Burst floats would then have to agree bit for bit. Not part of 8.14. | -- |
| 5 | **CHANGED** | **Hand-over by distance, not residency.** A ray reads shell k between its bounds. If that chunk is absent, it reads shell k+1 (Q2 (a)). The far band draws only past the last shell or where the last shell is absent. `FarStartT`'s residency DDA and `-detailspeed` retire (step 12). The prefetch margin is > 0 by construction: each ring stores past its hand-over distance (≥ 12.8 m for shell 0). | §6.2; KNOWN_ISSUES 16b / 22 / 24 |
| 6 | **CHANGED** | **Smaller 0.1 m window** (step 14). §4.3's "128 m LOD0 radius" becomes shell 0's radius plus margin, per preset: 59 / 65 / 82 / 116 m (Low / Medium / High / Ultra). The load radius goes 10 → 5 / 6 / 7 / 10 chunks (`EngineConfig.cs:268`). The CPU ring stays 32 × 32 × 16 on every preset (pow2; `WINDOW_CHUNKS_Y` stays 16, CLAUDE.md landmine). The GPU mirror box becomes 16³ (Ultra keeps today's 32×32×8; task 32's out-of-box skip already handles chunks outside it). The 0.1 m pool cap comes from `PeakUsed` × 1.25 after the shrink. | §4.3 |
| 7 | **UNCHANGED (frozen)** | `IWorldQuery.GetVoxel` (`IWorldQuery.cs:6`). It still answers from resident chunks only, so its reach is 59-116 m depending on the preset. Coarse shells are render-only and are never queried. A game query beyond that (NPC sight, projectiles) needs a new API later, and game rules must not depend on the preset's reach. | Phase API freeze |
| 8 | **UNCHANGED (frozen)** + NEW beside it | `IEditService.SetVoxel` (`IEditService.cs:6`) keeps its contract: it is refused outside loaded chunks. A far edit uses a **new** op API (e.g. `IEditOps.Record(op)`), main thread only. | Phase API freeze |
| 9 | **NEW**, next to a frozen format | **Edit-op log** (plan §4 row 3; E5), new file D.4 `ops.log`: header `{uint seed, ushort formatVersion, uint opCount}`, ops `{uint seq, byte kind, int3 centreVoxel, ushort size, byte material}` (sphere carve/fill, box, D.3 prefab placement), CRC32 trailer, `.tmp` + atomic rename as D.1. Ops are applied in `seq` order inside generation, at each shell's own voxel size. The log does not depend on the preset: a save made on Low loads on Ultra. | Appendix D (adds D.4) |
| 10 | **UNCHANGED (frozen bytes)**, CHANGED meaning | **D.1 stays byte for byte.** §4.2's "baseline" becomes "generation + the ops up to this chunk's saved seq". **Precedence (Q3 (a)):** ops with seq ≤ S, then D.1, then ops with seq > S. S is the op count when the chunk's delta was written. It lives in a side table inside `ops.log` (`{int3 chunk, uint seqAtSave, uint deltaCrc}`), never in D.1. If `deltaCrc` does not match the delta's own trailer (a crash between the two writes), fall back to "all ops, then D.1". The worst case is one chunk's far op lost under edited bricks, in the spirit of §4.2's "edits lost in one chunk". Old saves have no `ops.log`: zero ops, so they load unchanged. | §4.2 |
| 11 | **UNCHANGED (frozen)** | **§4.4 state table.** A fluid patch (plan §4 row 4) inside the CPU ring is just "eviction not selected". The same states and transitions apply, and "enter window" reads as "enter the residency set" (the detail radius plus awake-fluid chunks). A patch OUTSIDE the 32-chunk ring (~±204 m) has no ring slot: it would need a new store or a new state. That is a **redesign**, flagged for step 19's amendment and not done here (Q4 (a)). The ring is the same on every preset, so this holds on all of them. | §4.4 |
| 12 | **NEW** | `GenerateShellChunk(seed, map, shell k, coord, ops) → brick data`, a Burst job on the one compiled generator path (step 1). §5.2's `GenerateChunk` is unchanged. A shell brick in a chunk that has a D.1 delta or a live edit is instead made by downsampling the resident 0.1 m chunk (majority vote, as today's cascades). | §5.2 |
| 13 | **NEW** | **Coarse-first agreement threshold (for step 10, Q5 (a)).** Each coarse voxel is the 0.1 m generator's voxel at index **2^(k−1) − 1** on each axis (one fine voxel below the centre; task 74 Q2 (a), amended 2026-10-11 by task 77: it matches the cascades' tie-to-solid vote better, pooled shell 1 voxels 97.2 → 98.5%, heights 99.0 → 99.6%). **The top voxel** (task 74 Q1 (a)): the coarse voxel whose sample is the highest at or below the column's ground height h takes, if it is solid, the material of the fine voxel at h, so distant land keeps its 0.3 m grass / sand / snow colour. Both are exact against `Generate` in a test, like Q19 4a. **Pass:** ≥ 97% of voxels in non-uniform bricks agree solid/air with the majority-vote downsample, AND ≥ 99% of columns have their surface within 1 coarse voxel, for shells 1-3 on the seed-42 landform set. On smooth slopes a centre sample matches the vote almost always (est. > 99%). Misses come from detail finer than the voxel. Material agreement is reported, not gated. | step 10 |
| 14 | **UNCHANGED** | Upload cap 3 MiB/frame (§0.2); shells share it (§3 above), on every preset. | §0.2 |
| 15 | **CHANGED** | **Shell pool exhaustion does not throw.** Today a cascade pool throws when full (task 53 HANDOFF). A full shell pool skips the chunk (the coarser shell shows) and counts a refusal, like the fluid pool (task 55). This is also what makes estimated caps safe on the presets that cannot be measured here. | 8.10 |
| 16 | **NEW** | **Quality preset.** `PlaySettings.quality` = `auto | low | medium | high | ultra` (task 58's file; default `auto`), flag `-quality`. Applied at start-up only (Q9 (a)); F6 row "Quality preset (restart)". Sets `ShellLayout` (row 1) and the default FSR 3 row range (Q10 (a)); the "Render rows" row still overrides rows. Rigs and capture modes pin `high` and never read the saved file. | -- |
| 17 | **NEW** | **Auto-pick.** `QualityAuto.Pick(DeviceInfo) → preset`, a pure function over a snapshot of `SystemInfo` (`systemMemorySize`, `graphicsMemorySize`, `graphicsDeviceName`, `graphicsDeviceVendor`, `processorCount`) plus `CpuTopology`'s performance cores; the lowest of three tiers (§H.3); unified memory counted once. EditMode tests with fixture devices (M1 8 GB → high, Intel iGPU → low, ...). Logged once; saved after the first pick. | -- |

## 6. Risks, and what is measured when

| risk | what we do | measured at |
|---|---|---|
| Generation keeps full detail only to ~7-11 m/s on High (est.) | Q2 (a): the coarser shell shows. The GPU-shell fallback only if this looks bad. | step 16: warm `MODE=stream` walk and fly at 60 m/s. Cols/s per shell, seconds to full detail, % of frames showing a coarser shell inside 69 m. |
| A dive (vertical speed) loads whole horizontal faces; not modelled here | Same fallback. A game speed rule is the owner's later call (plan Q2 (b)). | step 16 (fly + dive) |
| Pop when a shell brick switches from generated to downsampled (an edit arrives) | The 97% / 99% threshold (row 13); side-by-side renders | step 10 tests; step 11 pose sheet |
| Shells do not cut frame time; open views may cost +2.7 ms (est.) | Accept. Shadows and the main march are separate steps. | step 23: warm ABBA + Gate C "frame total p50/p99" |
| Pow2 rings alias (§6.2 phantom terrain), on any preset | `&` masks only, zeroed directories, one validator test per shell **per preset** (like `ClipmapValidator.CountStaleSlots`) | step 11 EditMode |
| Less building room once the 0.1 m pool is cut to peak × 1.25 | Valve line at 0.85 × cap (§3.6) stays. Re-measure `PeakUsed` with a build test. | step 14 ledger (High; the other presets' caps stay est.) |
| The physics/query world shrinks to ~59-116 m by preset | Game-design follow-up; no API change here; game rules must not depend on the preset | -- |
| More evictions churn D.1 saves (save cap 4/frame, task 26) | Watch "deferred evictions" | step 14 rig gates C/D |
| Largest voxel on screen | New pose metric: ≤ 2 px at the preset's top rows, ≤ 1.5 px at its bottom rows (rule A) | step 11 and step 23 |
| Memory | `-memledger` + peak footprint ≤ ~2,400 MB at poses on High (est. target); `-quality low/medium/ultra` ledgers on the M1 for the others | steps 14 and 23 |
| Only the M1 exists here: Low / Medium / Ultra frame rate and the auto-pick thresholds are never checked on the machines they are for | Presets run on the M1 for correctness and memory; fixture tests for the pick; numbers stay (est.) until someone runs another machine | step 23 (memory only) |
| Auto-pick puts a machine on the wrong preset | Logged inputs and pick; F6 override, saved | -- |

## Questions for the owner

### Answered (2026-10-11, in chat after task 61)

1. **Q1. Which internal rows size the shells?** **Replaced:** "we are trying to stay open to as many devices as possible so the render range and LOD tiers need to be dynamic based on the hardware. Or however popular games do it." The rows now come from the quality preset (§H): 480 / 540 / 720 / 1080. The M1 gets 720 (High), which was this question's (a).
2. **Q2. Where does each shell hand over?** **(a)** where the finer voxel reaches 1 px (rule A).
3. **Q3. Far edits (op log) vs saved chunk edits (D.1)?** **(a)** by order of time; the seq side table in `ops.log`, D.1 frozen.
4. **Q4. Awake fluid outside the smaller 0.1 m window?** **(a)** its chunks stay resident inside the 32-chunk CPU ring; beyond it waits for step 19.
5. **Q5. The coarse-first pass line?** **(a)** centre sample, ≥ 97% solid/air, ≥ 99% heights within 1 coarse voxel.
6. **Q6. The far band once shells reach 6.6 km?** **(a)** keep it beyond the last shell and as the fallback; retire at step 23 if the pose sheet allows. With presets it also stays the horizon past Low's 1.5 km and Medium's 3.3 km.

### New in this revision

Answer "all (a)" to approve the draft as written.

7. **Q7. Which presets?**
   1. (a) **Four: Low / Medium / High (= your M1, and what every rig pins) / Ultra, one combined menu row.** Covers iGPU laptops up to 16 GB+ machines. Cost: four layouts to test (one pow2/validator test each, cheap); three of them can only be checked for memory and correctness here.
   2. (b) Three: drop Medium (8 GB PCs with a mid GPU get Low). One less layout; those machines lose ~160 MB of detail they could afford and see a 1.5 km 3D range instead of 3.3 km.
   3. (c) Four, plus a separate "View distance" row (shell count 5-8) beside the preset, as Unreal's groups. More control for players; one more setting to explain and test, and range then varies on the same preset.
8. **Q8. How the first launch picks the preset.**
   1. (a) **The lowest of a memory, a GPU and a CPU tier from `SystemInfo`, thresholds as §H.3, no benchmark.** Deterministic and testable with fixture devices. Cost: the GPU-name rules need updating as new chips appear; an unknown GPU falls to Medium.
   2. (b) Memory only (`systemMemorySize`). Simplest, but a 16 GB laptop with a weak integrated GPU would get Ultra and run at a low frame rate.
   3. (c) (a) plus a ~10 s benchmark on first launch. Closer to Unreal; but on a fanless Mac the result depends on how warm the chip is (up to 2.2x, tasks 27/28), and it adds a startup scene (est. size M).
9. **Q9. Changing the preset in F6.**
   1. (a) **Applies on the next launch; the row says "(restart)".** Reuses the start-up path every rig runs; no new teardown code in the streaming system.
   2. (b) Applies live: tear down every shell pool and the 0.1 m window, re-prime (a few seconds of coarse view). A new teardown path through the single-writer system that nothing else needs (est. size M) and that the frozen APIs must survive.
   3. (c) Shell count live (drop or add the outer shells; each shell is its own pool), the rest on restart. Half of (b)'s work for the cheapest part of the preset.
10. **Q10. Does the preset also set FSR 3's row range?**
    1. (a) **Yes, as a default: top = the sizing rows, bottom = ¾ of it (High = your current 540-720).** Keeps shells and rows matched so a coarser voxel always arrives at 1.5-2 px. The "Render rows" row still overrides it, with a hint when rows go over the sizing.
    2. (b) No: rows stay dyn 540-720 on every preset. Low then sizes for 480 but renders up to 720 (coarse voxels up to 3 px), and Ultra sizes for 1080 but renders at most 720 (memory and march spent on detail nobody sees).
    3. (c) Locked together: no separate rows choice. Simplest menu; players lose the live frame-rate knob task 58 gave them.
11. **Q11. Should render range ever change on its own while playing?**
    1. (a) **No. It is a menu setting; FSR 3 dynamic rows hold the frame rate, and Q2 (a)'s coarser shell covers slow generation.** What most shipped games do; no horizon popping.
    2. (b) Yes: drop the outer shells when frame time stays over budget, restore them when it recovers. A visible horizon change, a controller to tune, and shells cost little frame time anyway (§4).
    3. (c) Not now; revisit after step 23 measures what the shells really cost per preset.
