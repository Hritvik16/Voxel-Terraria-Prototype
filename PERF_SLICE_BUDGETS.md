# PERF_SLICE_BUDGETS.md -- per-system budgets per quality tier, and how features get admitted (task 241, 2026-11-19, PLAN ONLY)

Owner, 2026-11-19: features keep being added -- how do we hold performance, and is the M1 Air 8 GB the minimum spec or only the dev
machine? This doc answers with per-tier budgets (ms of frame, MB of memory per system) and a rule for admitting features. It also
says how the dense forest could fit WITHOUT asking for better hardware (owner: "8x is much too much ... top-end hardware is not
acceptable"), and whether the architecture itself is the problem.

No code, no builds, no timing in this task. Every number cites its source. **est.** = arithmetic on measured numbers (the arithmetic is
in `Logs/overnight/241/scale.py`). **guess** = a judgement with no measurement behind it. **unknown** = nobody has timed it.

Main sources: task 235's budget check `Logs/budget/2026-11-17_235256/check.txt` (+ its four `costsplit_*.txt`: warm, AC, play flags, High,
FSR 3 pinned at 432 rows), `PLAN_PATH_TO_33MS.md` (task 237), `PERF_BUDGET_GUARD.md` + `PERF_BUDGET.json`, `PERF_EXACT_CUTS.md` (160),
`AMENDMENT_8_17_RENDER_STEPS.md` sec. 3 (adopted shares), `AMENDMENT_8_14_LOD_SHELLS.md` (preset table), task 219's ladder
`Logs/overnight/219/table.md`, task 170's HANDOFF (torch flood memory), `RESEARCH_LAY_OF_THE_LAND.md` (GPU TFLOPS).

![Worst poses today](Screenshots/241/budget_bars.png)
*`Screenshots/241/budget_bars.png` (git-ignored; source `budget_bars.svg`): one bar per worst pose, p50 ms. Look at the two dashed lines
(33.3 ms = 30 fps, 50 ms = 20 fps): only walk_near is near the first one. In the forest bar, orange (sun) + blue (sky fill) are 80 of
the 135 ms; in jungle and night, the grey "rest" (march, shading, shells, CPU, FSR 3) is the biggest part.*

## 0. Short answer

- **The M1 Air can honestly be tier A ("low"), not more -- and only after the cuts in sec. 5.** Today it misses even tier A on three
  of four worst poses: forest at a 16 deg sun 135.4 ms, jungle 70.0, night torches 52.7, walk 35.4 (235). Today's play is ~8-30 fps.
- Tier A target: **30 fps on typical poses, the dense forest at dawn/dusk capped at 50 ms (20 fps), memory under 3,000 MB**.
- **Hardware does not fix the forest.** By the arithmetic in sec. 3, the forest at dawn needs **~4.9x the M1's GPU for 30 fps even at
  today's 432 rows**, ~7x at 540 rows (est.). A "Pro"-class Mac (~2-2.5x, guess) is not enough. The forest has to get cheaper by design
  (sec. 6).
- **The architecture is not proven to be the problem, but part of it is design-shaped**: the sun and sky light are per-pixel rays whose
  length grows with a low sun and with foliage, and that is 80 of the forest's 135 ms. The other 55 ms ("rest") has never been split, so
  a bug there cannot be ruled out. Do not replace the renderer. Run the one forest capture first (sec. 2), then do targeted lighting and
  foliage changes (sec. 6, sec. 7).

## 1. The three tiers

| | **A "Low" = M1 Air 8 GB** | **B "Mid" = M-series Pro class** | **C "High" = 60 fps** |
|---|---|---|---|
| GPU class (FP32) | Apple M1 8-core, 2.6 TFLOPS (Apple, cited in RESEARCH_LAY_OF_THE_LAND) | ~2-2.5x M1 (guess: M1/M2/M3 Pro GPUs, Apple marketing ratios, not cited in this repo) | ~4-8x M1 (guess: M-Max class or a desktop GPU) |
| RAM | 8 GB | 16 GB+ | 16 GB+ |
| frame target, typical poses | p50 <= 33.3 ms, <= 5 % of frames > 33 ms (PERF_BUDGET.json targets) | p50 <= 33.3 ms at EVERY worst pose; ~22 ms typical (guess target) | p50 <= 16.7 ms typical; <= 33.3 ms worst |
| frame target, worst poses | jungle / night <= 33.3 with rows dropping; **forest at 16 deg sun <= 50 ms** (237's recommendation; owner decision D1) | <= 33.3 | <= 33.3 |
| render rows (FSR 3 to 1080p) | dyn 432-540 today; heavy views may drop to 360 / 288 (237 R1/R2; owner decision D3) | 540-720 | 720 (1080 native = too costly, sec. 3) |
| output | 1080p | 1080p-1440p | 1440p |
| view range (8.14 presets) | **High today**: 0.1 m window 82 m, 8 shells to 6.6 km (or Medium: 65 m, 7 shells to 3.3 km; decision D2) | High | Ultra: 116 m window, shells to 6.6 km |
| simulation reach | NEAR 128 m on every machine (AMENDMENT_8_15: only the drawn side shrinks) | same | same |
| memory cap (process) | **3,000 MB** (hard limit, PERF_BUDGET.json); peak today 2,512-2,621 MB (235) | 4,000 MB (8.14's Ultra cap on >= 16 GB) | 4,000 MB (8.14); more is possible but nothing needs it yet |
| can we measure it here? | yes (the only machine) | **no** -- numbers stay est. | **no** -- numbers stay est. |

**Which tier is the M1 Air, honestly?** Tier A, as the minimum spec, *if* the owner accepts sec. 5's package (low-sun shadow cap,
cheaper bounce at low sun, rows dropping in heavy views) and a 20 fps worst case in the dense forest at dawn/dusk. Without those cuts the
Air is below tier A on every worst pose except walk. It cannot be tier B or C: by sec. 3, tier B's own targets need ~2.4x (jungle) to
~4.9x (forest) the Air's GPU at 432 rows.

**A caution about the walk number.** walk_near at noon was 60.6 ms in task 232 (warm ABBA, TASKS.md) but 34.1 ms in task 235 one day
later, with no performance change in between that I found. 235's walk arc heads straight away from the sun and "depends on the sun
azimuth" (PERF_BUDGET.json `paths.walk_near`), so it may simply be a lighter view. **Unexplained; do not read 35 ms as "walking is solved".**

## 2. Slice tables

### 2a. Tier A frame slices (ms)

The slices are AMENDMENT_8_17 sec. 3's adopted shares (they already sum to 33.3 ms). Beside them, what each system costs today at the
two worst day poses. Arms are not additive; "rest" pieces are subtractions and carry that error.

| system | tier-A slice | walk_near 16 deg (235) | forest_eye 16 deg (235) | source / note |
|---|---|---|---|---|
| CPU + streaming floor | 5.5 | 7.1 | **unknown** (>= 7.1? forest keeps a 573-chunk deficit) | 146 `tiny_gate` (540 rows, older build) |
| FSR 3 upscale (1080p output) | 5.0 | 5.6 | 5.6 est. (fixed: output size) | 146 `ups_none` |
| shells | 4.0 | 7.0 | **unknown** | 235 `shells_off` |
| far band past the shells | (in the march slice) | ~0-2.4 | **unknown** | 146 `far_off` - `shells_off` |
| tier-0 march + shading + look passes (226 vary/crevice/haze/bloom, 233 grade) | 5.0 | ~1-5 est. (rest 23.0 - CPU 7.1 - FSR 5.6 - shells 7.0 - far 0-2.4; low confidence, walk noise 13.5) | **unknown**; biggest suspect | subtraction |
| sun shadows | 5.0 | 12.9 | **54.7** (noon 18.2, 233) | 235 `sun_off` |
| sky fill (mode 2, low-res 4x4) | 2.5 | -0.5 (~0) | **25.5** (noise 11.2; ~20 on the clean passes) | 235 `skyfill0` |
| torch flood | 1.0 (night only) | 0 (day skip, 229) | 0 | night: **8.4** (235 `tf_on` - `tl_none`) |
| trees | **no own slice** (they lengthen the march, sun and sky rays) | -- | est. ~49 of 135 (ratio 1.57, sec. 6) | 210/213 cold ratio, noon, sky fill 0 |
| fluids | 0.5 | ~0 (110: no surface fluid on the walk) | ~0 | 8.17 reserve; fluid tick is fixed-rate CPU (97) |
| reserve: reflections | 2.0 | -- | -- | 8.17 |
| reserve: characters / NPCs | 1.5 | -- | -- | 8.17; the placement phase draws from here |
| reserve: spikes / pacing | 1.3 | p95 86.9 | p95 198.2 | 8.17; 168e upload ring (built, OFF) cuts p99 65.3 -> 52.9 |
| **total** | **33.3** | **35.4** | **135.4** | |

Jungle noon (70.0) = sun 15.5 + rest 55.3; night (52.7) = flood 8.4 + rest 43.0 (235). Their "rest" is split into nothing yet.

**Heavy views.** Tier A cannot hold these slices in a dense forest at 432 rows. The rule for tier A is therefore: the slices hold at
432 rows on typical views; in heavy views the dyn controller may drop rows (floor 360, then 288: owner decision D3), and the forest at
a low sun is capped at 50 ms.

### 2b. Tier A memory slices (MB, process cap 3,000)

| pool | today | proposed slice | source |
|---|---|---|---|
| CPU brick pool (0.1 m) | 278.3 (cap 570,000 bricks; peak 496,071 forest > valve 484,500) | 278 fixed | 235 `[mem]` line, costsplit_forest.txt |
| GPU body pool | 124.2 (cap 254,400; **full at jungle: 4,472 refusals**) | 124 fixed; refusals 0 is the target | 235 `[mem]` |
| shells 1+ (GPU) | **unknown measured**; 388 est. at High | 390 | 8.14 table (est.); 219: High 2,607 vs Low 2,207-2,297 MB peak |
| far band + ChunkStore / StreamManager | 102 est. | 100 | 8.14 table |
| fluid pool (480 tiles) | ~240 est. (task 55: 1,024 -> 480 tiles saved 272 MB, i.e. 0.5 MB / tile) | 240 | AMENDMENT_8_14 notes (task 55), est. |
| torch flood | CPU 15.9 + GPU 9.25 + job scratch 13.1 (peak) = 38.3 | 40 | 170 HANDOFF |
| per-pixel render targets | 22.8 est. at 432 rows (72 B/px) | 40 (8.17's hit buffer +12.7 at 432; low-res sun +2.65 if turned on, 232) | 8.17 sec. 4, 232 |
| worker / generator scratch | **unknown** (223/224 made it Persistent, < 50 MB each per 223) | 50 | AUDIT_TEMP_ALLOC.md |
| everything else (Unity, Mono, textures, Metal staging) | ~1,483 est. (8.14, older) | -- | 8.14; not split since |
| **process peak** | **2,512-2,621** | **<= 2,750 before the placement phase** | 235 |
| **headroom** | **379** | **~250 kept for objects / NPCs / towns + ~130 safety** | owner decision D2 |

The one cheap capture that fills the memory unknowns: **one `-memledger` start-up print at High with every pool's allocated bytes**
(shells per shell, fluid tiles, scratch). If the ledger cannot print a pool, that is a capture-only line, not a launch.

### 2c. Tier B and tier C slices (est. only)

Same steps, same ratios, scaled to the tier's frame (B 33.3 ms at 540-720 rows, C 16.7 ms at 720 rows). The proportions of 8.17 sec. 3
hold; each step's ms on a faster GPU is today's ms / k (sec. 3), except the CPU floor (not scaled, assumption). Nobody can measure
these here. They exist so a feature can say "costs 2 ms on A, ~0.8 on B" and so the reserves (reflections, characters) are known in
advance. Tier B/C get more of the same features (rows, shells, Ultra window), not different features -- one renderer, one look.

### 2d. The ONE cheap capture per unknown (do not run here)

| unknown | capture | cost |
|---|---|---|
| forest / jungle / night "rest" split (CPU, FSR, shells, far, look passes, march) | 237 sec. 2: one cost split at forest_eye, arms `tiny_gate,ups_none,shells_off,far_off,lotl_off,look233_off` (all exist; checked in `PlaygroundCapture*.cs`) | ~25 min of Unity + warm-up |
| do the sun levers pay at a low sun? | the same launch + arms `sunres4,clamp64,sunfar64,reuse16` (exist) | +~1.5 min per arm |
| sky fill at forest: low-sun effect or forest effect? | the same launch, at `-startsun 0` (noon) with `skyfill0` | one extra arm |
| tree share, warm, 432 rows | one FULL-only forest launch with `-trees2 0` (a launch flag, cannot be an arm) | ~10 min |
| memory per pool | sec. 2b's ledger print | no launch beyond one already run |

## 3. What would a tier-C (or tier-B) machine need?

**Model (est., all assumptions written out):** frame = CPU floor 7.1 ms (146; NOT scaled -- the main thread on a Pro/Max Mac is about
the same core) + GPU work / k, where k = GPU speed vs the M1 8-core. GPU work = FULL - 7.1, split into a pixel part that scales with
(rows / 432)^2 and a fixed part. Pixel shares from task 219 (pre-227 build): forest 67-82 %, walk / jungle 95-100 %, night 85 % (guess).
Script: `Logs/overnight/241/scale.py`.

**k needed (x M1 GPU) to hit a target:**

| pose (235, 432 rows today) | 30 fps @ 432 | 30 fps @ 540 | 30 fps @ 720 | 60 fps @ 432 | 60 fps @ 540 | 60 fps @ 720 |
|---|---|---|---|---|---|---|
| forest_eye 16 deg (135.4) | **4.9** | 6.8-7.3 | 11-12 | 13 | 19-20 | 30-34 |
| jungle_eye noon (70.0) | 2.4 | 3.8 | 6.7 | 6.6 | 10 | 18 |
| torch_camp night (52.7) | 1.7 | 2.7 | 4.8 | 4.8 | 7.4 | 13 |
| walk_near 16 deg (35.4) | 1.1 | 1.7 | 3.0 | 2.9 | 4.6 | 8.2 |

Read it this way: **tier C as "60 fps typical at 720 rows" needs ~8x the M1's GPU on the walk alone; 60 fps in the dense forest at
720 rows needs ~30x.** No consumer machine is 30x an M1 GPU. Even tier B's "30 fps everywhere at 540 rows" needs ~7x for the forest.
So hardware tiers only work if the forest (and the jungle "rest") get cheaper first; after sec. 6's levers, tier C at 60 fps on
typical views is plausible on a ~3-5x machine (guess, from the walk row).

**What this ignores (it is an estimate, not a prediction):**
- **Memory bandwidth.** A voxel ray march is mostly memory reads with divergent rays; FLOPS barely matter. M1 has ~68 GB/s, Pro-class
  ~150-200 GB/s, Max ~400 GB/s (guess, from public specs, not cited here). Bandwidth ratios are not FLOPS ratios, and our cost may
  follow either. Task 124 found halving the traced sun rays saved 0 ms (PERF_EXACT_CUTS), a sign that something other than ray count
  (latency, divergence, bandwidth) bounds the sun pass.
- **Metal vs other APIs.** Everything here is Metal on Apple silicon (tile-based GPU, unified memory). A Windows / D3D12 / Vulkan build
  has never been measured; FSR 3 and compute costs on a desktop GPU may differ either way. "TFLOPS" across vendors is not comparable for
  this workload (desktop GPUs count dual-issue FP32).
- **Thermals.** The Air is fanless and runs ~2.2x slower when hot (tasks 27/28); a fan-cooled Pro holds its clocks. That alone could be
  worth part of a k (guess).
- **The CPU floor and streaming.** More P-cores generate chunks faster (8.14: "full detail keeps up to" grows with cores); the jungle /
  forest streaming deficit (942 / 573 chunks, 235) may shrink on tier B and change "rest". Unknown.

## 4. Admission rule for new features

1. **Declare.** Every task file that adds drawing, content or a system says, per tier: est. ms at the worst pose it touches, MB (pool and
   process peak), and **which slice it lives in** (sec. 2a/2b). Tier A numbers come from its launches (rule 12 P1-P5); B/C are tier A
   divided by k (sec. 3), marked est.
2. **Fit or name the trade.** If it fits its slice: admitted. If not, the task file names what it takes from (another step's slice, a
   reserve, or a lever from sec. 5/6 that pays for it) and the owner signs that trade in the task file. "Leave it for the performance
   stage" is not an option (PREAMBLE rule 11, unchanged).
3. **Proxy.** Where a rule-12 proxy exists (P1 memory, P2 pass card, P3 rough ms, P4 counts, P5 ray counters), the HANDOFF reports it
   against the slice. Where none exists, the HANDOFF writes a one-line **"timing note for the next milestone"** (which arm, which pose)
   and the supervisor appends it to the next budget check's arm list.
4. **Switch + arm.** A new pass keeps its own switch and a `-costarms <name>_off` arm (rule 12, unchanged).
5. **Worst poses decide.** A feature is judged at forest 16 deg / jungle noon / night torches / walk 16 deg (235), not at noon.

**Where `PERF_BUDGET.json` could get new proxies** (proposals only; this task does NOT edit the gates):
- `steps[*].share_ms` becomes `share_ms: {A, B, C}` (B/C est.), so a task's declared ms has a number to compare with per tier.
- A forest "rest" split (`tiny_gate`, `ups_none`, `shells_off`, `far_off`, `lotl_off`, `look233_off`) once per milestone, not every
  check (~9 min at forest's frame rate).
- `forest_eye_noon` with `skyfill0` (separates the low-sun and forest parts of sky fill).
- A no-timing **tree proxy** (P4 at forest_eye: tree count, leaf bricks, GPU bodies) with a +10 % limit.
- A **sun-steps-per-ray counter** at forest_eye (P5 names `[sun]` counters; I found none in code -- only `-shelliters`, `-farsteps`).
  Capture-only code, ~20 lines; then sec. 6's foliage levers become checkable without timing.
- **Memory per pool** rows (flood CPU/GPU/scratch, render targets, shells, fluid) in `baseline.memory` as info rows.
- **Main-thread p99** at the night camp (170: torch flood upload spikes 2.6-6.1 ms) as a CPU-side proxy for the spikes reserve.

## 5. Decisions for the owner

| # | question | recommendation |
|---|---|---|
| D1 | Is the M1 Air the **minimum spec (tier A)**, with the dense forest at dawn/dusk allowed to drop to **20 fps (50 ms)** and 30 fps everywhere else? | **Yes.** It is the only machine we can measure, so it has to be the floor we promise. |
| D2 | Tier A view range: keep **High** (2.6 GB peak, ~380 MB headroom) or move to **Medium** (~2.35-2.4 GB, 219) to keep more room for the placement phase? | **Keep High now**; reserve ~250 MB for objects/NPCs; revisit when the placement phase measures its memory. 219 showed presets change memory, not time. |
| D3 | What tier A drops first in heavy views? | In this order: (1) long low-sun shadows (sun-ray cap from the sun angle, 237 S2), (2) the ground-bounce sun ray at low sun (S5), (3) render rows to 360, then 288, only in heavy views (R1/R2), (4) forest density **last** (W). |
| D4 | Approve the forest plan in sec. 6, starting with the one forest capture? | **Yes**: one performance task (capture), then levers one per task. |
| D5 | Tier B / C: promises or labels? | **Labels with estimates.** No player-facing claim until someone tests on that hardware. |
| D6 | Adopt sec. 4's admission rule (task files declare ms/MB per tier and their slice)? | **Yes**; the supervisor adds a `Slice:` line next to the existing `Cost:` line. |

## 6. Making the forest fit (without a ton fewer trees)

**What we know (sources in sec. 2a):** forest_eye at noon 99.2 ms; at 16 deg 135.4 (low sun +36.2). Of the 16 deg frame: sun 54.7,
sky fill 25.5, rest 55.2. Trees: ~+57 % FULL (210/213 cold native ratio 163.9 -> 257.2, noon, sky fill 0), i.e. **est. ~49 ms of the
135** if the ratio holds at 16 deg (guess that it does). The dense placement alone (213 vs 210, 257.2 / 222.8) is est. ~18 ms. 67-82 %
of the forest frame scales with pixels (219).

**Why the forest is expensive (code reading, not a new run).** Empty space is skipped with coarse occupancy (8.7/8.8 air mips,
occupancy mask). A leaf crown is many PARTLY-filled 0.8 m bricks with air gaps, so the mips cannot skip them, and every ray -- primary,
sun, sky -- steps 0.1 m cells through each crown it passes. At a 16 deg sun the sun ray crosses ~3.5x more ground (237), i.e. more
crowns. Every lever below attacks one of: crowns being partly empty, rays crossing too many crowns, or too many pixels.

| # | lever | est. ms saved at forest 16 deg | look cost | memory | risk | amendment? | one cheap capture to confirm |
|---|---|---|---|---|---|---|---|
| F1 | **Cheaper low sun**: sun-ray length cap from the sun angle and window size (237 S2), plus S1 low-res sun / S4 reuse if they pay at 16 deg | -30..-37 (guess, upper bound: sun back to its noon 18-25) | dawn/dusk: distant hills/trees no longer shade far clearings; more sun patches on the forest floor | 0 (S1 +2.65 MB) | medium: cap must come from sun angle + window, not terrain; prove on a tall cliff + thin pole | no (a ray parameter) | forest split with `clamp64,sunres4,reuse16` at 16 deg (sec. 2d) |
| F2 | **Bounce light at low sun**: cap the ground-bounce's sun ray, or sky fill mode 1 when the sun is low (237 S5) | 0..-25.5 (unknown which part is low-sun) | cooler, slightly darker shade under canopy at dawn/dusk only | 0 | low | no | `skyfill0` at forest noon (sec. 2d) |
| F3 | **Solid crown cores**: the tree generator fills each crown's interior (leaf voxels stay ragged on the outside 1-2 voxels) | guess -10..-25 (primary rays stop at the crown skin; sun/sky rays stop at the first crown instead of threading it) | from outside: almost none (you see the outer leaves); from inside a crown: solid; under trees: shade a little denser, fewer tiny sun flecks | **saves**: more fully-solid bricks; sealed bricks take no GPU body (201a), so jungle refusals may fall | low-medium: content change (SUITE=all), owner's tree look (Q-AJ) must be re-approved by eye | no (content) | report-only TerrainGen test: brick fill histogram of leaf bricks in forest_eye's 82 m footprint (EditMode, no player) |
| F4 | **Foliage early-out for light rays**: a shadow / sky ray that enters a leaf brick filled above X % counts as blocked (or accumulates a leaf transmittance and stops near 0) | guess -10..-20 over sun + sky fill | dappled light under crowns becomes more uniform; edges of crown shadows slightly larger | +~0.25 MB (1 byte per GPU body: 254,400 B, est.), written from the CPU state at upload | medium: a new per-brick value; must stay CPU-derived (invariant) | yes if folded into the brick layout (Appendix A / 8.13); no if a side buffer -- flag either way | a sun-steps-per-ray counter (sec. 4, capture-only code) at forest_eye vs walk_near: if forest rays take >> steps, F4 pays |
| F5 | **Dynamic rows in heavy views** (floor 360, then 288; 237 R1/R2) | 360: est. -27..-35; 288: est. -50..-62 | soft picture, FSR shimmer on leaves (owner called cheaper upscaling "bad picture quality", 2026-10-31) | -1..-1.5 | low (play has `dyn` today) | no | none needed (219 measured the scaling); a FULL at `-renderrows 288` to confirm |
| F6 | **Coarser leaves with distance**: leaf bricks beyond ~40 m stored/marched at 0.2 m (one leaf cell = 2x2x2), trunks and ground stay 0.1 m | guess -5..-20 | est. ~none at 432 rows: a 0.1 m voxel at 40 m is ~1 px (guess, assuming a ~60 deg vertical FOV), so 0.2 m there is ~2 px | saves bricks / bodies | high: a per-material LOD in the mirror is new | **yes** (8.14 shells are per-distance, not per-material) | the sun-steps counter split by distance band (same capture code as F4) |
| F7 | **Lighter trees** (density last: 213 dense -> 210 placement; or old trees) | est. -17 / est. -49 | sparser forest (6.0 vs 9.3 trees per 51.2 m square) / old tree shapes | -25 % tree bricks (210 vs dense) | low technically; undoes the owner's Q-AJ look | no | one FULL-only forest launch `-trees2dense 0` |

**Stack (guess; levers overlap, so ranges do not add):** F1 + F2 take 135 -> ~70-90 (237's P2). F3 + F4 take a further guess ~10-30
-> ~50-75. F5 at 360 rows multiplies by ~0.75-0.79 (pixel share) -> **~40-60**; at 288 -> ~30-45. So **50 ms (tier A's forest cap) is
reachable at the middle of the guesses with rows at 360 and full tree density; 33 ms needs the optimistic end or 288 rows.** F7 is
only needed if the guesses come out at their pessimistic end.

**Recommended order:** (1) the forest capture (sec. 2d) -- it decides F1/F2 and splits "rest"; (2) F1 + F2 as one look-checked task
with dawn stills; (3) F3 solid crown cores (content task, also frees GPU bodies); (4) the sun-steps counter, then F4; (5) F5 dyn floor;
(6) F6 only if F3/F4 leave the primary march heavy (amendment); (7) F7 last.

## 7. Architecture alternatives (paper only)

### 7a. Is it a design problem or a bug in a sound design? (CLAUDE.md "Working style")

**Evidence it is (partly) DESIGN:**
- 95-100 % of the walk / jungle frame and 67-82 % of the forest frame scale with pixels (219): the cost is per-pixel ray work, which is
  what this renderer does by design.
- 219: Low / Medium / High / Ultra all ~108-111 ms at walk_near -- presets (data size) change memory, not time. The time is in per-pixel
  lighting.
- The two biggest forest terms are rays whose length is set by geometry: the sun at 16 deg (+36.5 over noon, 233/235) and the bounce's
  sun ray in sky fill mode 2 (25.5). A shadow map or a lighting cache would not care about either.
- Exact cuts are exhausted (`PERF_EXACT_CUTS.md`; CLAUDE.md state note).
- Rough comparison: Lay of the Land (meshed, UE5) gets ~9x our pixels per second per TFLOP (222, INFERRED, different scenes).

**Evidence it may still be a BUG or an inefficient implementation:**
- The forest / jungle "rest" (55 ms) and night "rest" (43 ms) have never been split (237 sec. 2). Nobody knows what is in them.
- Task 124: halving the traced sun rays saved 0 ms -- the sun pass is bound by something other than ray count. That smells like an
  implementation limit (one big kernel, register pressure, divergence), not the design. 168c's first 8.17 split came out slower and
  task 198 found a 9-UAV kernel fails in players: the monolithic kernel itself is a suspect.
- History: the big wins were often bugs or implementation (227 sky fill 106.8 -> 72.3 ms; 218's 9-10 GB, 236's 6 GB, 201a's full pool).
- The walk_near 60.6 -> 34.1 ms jump between 232 and 235 is unexplained (sec. 1).

**Verdict:** the lighting cost in foliage at a low sun is design-shaped; the rest is unproven. That justifies a *targeted* lighting /
foliage redesign after the forest capture, not a renderer replacement.

### 7b. Alternatives

| | alternative | est. ms saved at worst poses | look change | memory | work size | frozen APIs / amendments touched | reversible? |
|---|---|---|---|---|---|---|---|
| (a) | **Cheaper sun shadows**: a sun-space shadow map of the near window, built by compute from the CPU-written mirror and refreshed in slices (the sun moves slowly; edits are rare) -- 237 S7; screen-space contact shadows for the last few voxels | guess: forest sun 54.7 -> 3-8 (and the bounce's sun ray); walk 12.9 -> ~3-5; jungle 15.5 -> ~3-5; night 0 | edges at texel size (blockier unless the map is large) + contact shadows to keep voxel-sharp near edges; shadows of off-window casters handled by shells/far as today | +6-26 MB (guess, by map size) | 3-5 tasks | no frozen API; new 8.x amendment (replaces the sun ray inside 8.17's S2 step; §6.5 shadow ladder) | yes: the ray path stays behind a switch |
| (b) | **Meshed / rasterized near field** (greedy meshes for the 0.1 m window, raymarcher kept for shells / far) | primary march share only: walk est. ~1-5, forest **unknown** (guess 10-30); sun / sky rays would still be traced unless also replaced | same voxels; different edges/AA; seams at the shell hand-over | **+200-800 MB guess** (leaf-heavy forests mesh badly) -- threatens the 3 GB cap | very large (months: meshing workers, remesh on edit, uploads, seams, FSR motion) | 8.14 shells hand-over, 8.17 S1, §6 rendering; CPU-sole-writer stays | costly to keep two paths |
| (c) | **Different foliage**: a light-only foliage proxy (coarse leaf density per brick for sun/sky rays; visible leaves stay voxels) -- = F3/F4 | guess -15..-40 at forest; jungle -5..-15 (guess) | dappled light more uniform; visible trees unchanged | +0.25..2 MB | medium (2-3 tasks) | amendment if it changes the brick layout; otherwise a side buffer | yes |
| (d) | **Dynamic resolution as a standing mechanism**: per-tier floor + ceiling, controller from frame time (already in play: FSR 3 dyn 432-540) | forest -27..-62, jungle ~-21..-37, night ~-14..-25 (237 R1/R2, est.) | softer, shimmery leaves in heavy views only | -1..-1.5 | small (one task) | 8.14 sizing rows only; no frozen API | trivially |
| (e1) | **Sky light as a world-space flood** (like the torch flood: CPU workers, incremental, read per pixel) in place of the per-pixel sky / bounce ray | guess: forest sky fill 25.5 -> a read cost (night's flood read costs 8.4 with 28 torches, so not free); jungle / walk ~0 today | sky light at brick resolution: softer, no per-voxel sky occlusion detail unless a short ray is kept | guess +10-40 MB (170: one flood = 38 MB) | large (3-5 tasks) | new 8.x amendment | yes (switch) |
| (e2) | **Split the monolithic kernel** (8.17 T1-T5, already adopted) | walk est. -6..-23 (low confidence); forest guess -10..-25 | none (exact) | +12.7 MB at 432 | 5 tasks | 8.17 (adopted) | yes |
| (e3) | **Upload ring ON** (168e, built, OFF) | p50 0; p99 65.3 -> 52.9 (walk) | none at rest | +4.5 MiB | flip a default | none | yes |

**Recommendation.** **Do not replace the renderer; the numbers do not force it.** The walk is within ~6 % of 30 fps; the dominant
worst-pose terms are lighting rays in foliage at a low sun, which (a), (c) and the F-levers attack directly, and the unknown "rest". A
meshed near field (b) attacks only the primary march, which is not shown to dominate anywhere, and would cost hundreds of MB we do not
have. Order: forest capture -> F1/F2 -> F3 -> (d) -> (e2) as already planned -> (a) or (c)/(e1) if justified.

**Evidence that would justify starting an architecture change:**
- **(a) shadow map**: after F1 (cap) and the S1/S4 arms, the sun step at forest 16 deg is still > 20 ms, or the sun is > 10 ms at walk
  at 16 deg. Then the per-pixel sun ray is the design limit.
- **(c) / (e1)**: sky fill at forest NOON (`skyfill0` at `-startsun 0`) > ~10 ms, i.e. the bounce cost is foliage, not low sun.
- **(b) meshing**: only if the forest split shows the tier-0 primary march itself > ~25 ms at 432 rows after F3. Today there is no
  number for it at all.
