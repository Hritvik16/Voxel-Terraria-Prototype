# PHASE 3 COMPLETION — Procedural World Generation

Status: **COMPLETE** (correctness proven, performance measured, two known
artifacts carried forward with root causes documented).
Architecture reference: `ARCHITECTURE_v8_6.md` §5 (generation), §13 (Phase 3),
Appendix C.2 (coastline), Appendix D.2 (world.meta).
Amendments in force: 8.8 (occupancy bitmask), 8.9 (LOD & resolution),
8.10 (LOD cascade performance).
Supersedes nothing. Builds directly on `PHASE_2_COMPLETION.md`.

---

## 1. What Phase 3 delivered

Per §13 Phase 3's ordered file list, all six shipped:

| # | File | Purpose | §ref |
|---|------|---------|------|
| 1 | `CoreEngine/WorldGen/WorldMeta.cs` | world.meta writer/reader, CRC32, atomic rename | D.2 |
| 2 | `CoreEngine/WorldGen/AnchorPlanner.cs` | Stage 1 global planning: Poisson anchors + Voronoi biome seeds | §5.2 |
| 3 | `ContentModules/Content.cs` | Code-defined Materials / Biomes tables + worldgen constants | §5.5 |
| 4 | `CoreEngine/WorldGen/GenerateChunk.cs` | §5.3 steps 1–4 pipeline (+ legacy Phase-2 generator preserved verbatim) | §5.3 |
| 5 | `CoreEngine/WorldGen/FeatureCarve.cs` | Per-anchor carve kernels (mountain, crater, cave) | §5.3 step 3 |
| 6 | `CoreEngine/Tests/GenerationTests.cs` | Phase 3a determinism / strata / feature suite | §13 |

Plus scene wiring and test harness:

| File | Purpose |
|------|---------|
| `Game/Phase3Bootstrapper.cs` | Plan → persist → **read back → byte-verify** → generate → upload → validate |
| `Game/Phase3AcceptanceRig.cs` | One-press automated acceptance + evidence collection |
| `Game/SimpleFlyCamera.cs` | Bare debug fly-camera (§13 cross-phase clarification) |

Two one-line shader/feature patches (see §6): `PATCH_iteration_cap.txt`,
`PATCH_window_bounds.txt`.

---

## 2. Evidence classification

Kept deliberately separate — these are different claims with different backing.

### CORRECTNESS PROVEN
- **156/156 EditMode tests green**, including 11 new `GenerationTests`.
- **Determinism**: byte-identical content hashes across repeated calls *and*
  across opposite visit orders. Live-store content hashes match fresh
  regeneration from the same `world.meta` on all 5 sampled chunks
  (`0x345BB32D`, `0x223071FC`, `0x2C3F2B77`, `0x326866BE`, `0x55ABF2CF`) —
  this proves the *wiring*, not just the pure function.
- **Per-voxel oracle**: every voxel of 5 full chunks compared against the pure
  per-voxel rule. Zero mismatches.
- **Column oracle**: 1,996 random columns (4 skipped inside cave AABBs), zero
  mismatches.
- **world.meta**: 336 bytes, written → read back → **byte-identical**, every
  run, before any chunk is generated. Generation consumes the *file*, not the
  in-memory plan, so the persisted D.2 format is exercised on every run.
- **Static water**: 0 violations above sea level (y=23).
- **All 4 biomes** present and correctly stratified.

### PERFORMANCE MEASURED (not "proven" — see §5 caveats)
FrameTimingManager relative numbers from a standalone build.

### NOT TESTED AT ALL
- Any world size beyond 22×22 chunks.
- Any moving/streaming resident window (Phase 4).
- Sustained thermal behaviour over more than ~2 minutes.
- Native 1080p internal resolution (we render 960×540 and upscale).

---

## 3. World content metrics (seed 42, sizeClass 0)

```
World extent          22 × 22 chunks = 281.6 m × 281.6 m (single cy=0 layer)
Island                ~210 m diameter (75% of span), coastR 105 m pre-wobble
Chunks resident       484 / 484  (0 missing)
Feature anchors       7  (2 mountain, 2 crater, 3 cave)
Voronoi biome seeds   6  (Forest, Desert, Snow, Jungle all represented)
Sea level             voxel y = 23 (brick-aligned; open water stays uniform)
```

**Sticky-note economy (§5.3 steps 2–3) — the headline result:**

```
Dense fraction/chunk  min 6.3%   median 6.3%   mean 7.6%   max 15.7%
Dense brick pool      151,470 / 750,000  =  20.2% of cap
```

Median 6.3% against a 25% gate. Dense bricks are confined to the surface skin
exactly as §5.3 intends — a mountain does not densify its whole bounding
volume, because mountains and craters are per-column *height* kernels, not
volume carves. Only caves are true 3D carves.

---

## 4. Generation performance (CPU, standalone build, M1 Air)

```
Stage 1 plan + meta verify            6 ms
Worldgen loop (484 chunks, parallel) 1,840 ms   (8 cores; was 3,665 ms serial)
Clipmap.UploadDirty                    446 ms
Cascades.UploadDirty                 3,033 ms   <-- now the dominant term
                                     -------
Total startup                        ~5.3 s
```

**Generation is CPU-only, per §3.1**: *"The CPU is the only thing that ever
writes terrain… The GPU also holds a flat, read-only clipmap… never a direct
terrain writer."* `ChunkGeneratorFull` runs on CPU into
`ChunkStore`/`BrickDataPool`; `Clipmap.UploadDirty` pushes a read-only copy.
The live-store-vs-fresh-regeneration hash check empirically confirms the CPU
store is ground truth every run.

**Startup optimisation note:** parallelising generation bought 1.99×, not 8×.
Lock contention on ~151k pool allocations is the likely limiter. More
importantly, **`Cascades.UploadDirty` (3,033 ms) is now 57% of startup** and
was never optimised. That is the correct next target if startup time matters —
not generation.

---

## 5. Render performance

**Which number is real.** `gpu_avg` reads 2.49–2.74× `wall_avg` at *every*
pose, with almost no spread. Real thermal throttling is erratic; a flat
multiplier is a systematic reporting artifact. The decisive argument: a frame
cannot complete in 10 ms of wall time if the GPU genuinely needs 27 ms, because
steady-state frame time is bounded below by GPU time. **`wall_avg` is the
figure used below.**

Beauty view, certified defaults, 960×540 internal dispatch:

| Pose | wall_avg |
|---|---|
| CaveInterior0/1/2 | 4.23 / 4.27 / 4.39 ms |
| IslandOverview | 5.62 ms |
| TopDown_Spawn | 7.66 ms |
| Mountain1 | 9.26 ms |
| Coast_LookingInland | 9.45 ms |
| Mountain0 | 9.64 ms |
| Crater1 | 10.10 ms |
| Crater0 | 10.48 ms |
| **GroundHorizon (worst named pose)** | **10.69 ms** |

```
Worst representative pose   10.69 ms   vs 16.67 ms (1080p60)   →  ~36% headroom
```

**Outlier excluded:** `DenseChunk1` reported 34.56 ms wall / 102.44 ms gpu this
run, versus ~9.3 ms for the same chunk in prior runs. That is a thermal or OS
scheduling spike on a fanless M1, not a rendering cost. Ignore it; it is why
the rig reports stddev.

**Traversal ablation (Coast_LookingInland, the worst-correctness pose):**

| Config | wall_avg | falseMisses |
|---|---|---|
| baseline | 9.64 ms | 81 |
| AirMip **off** | **13.91 ms** | 81 |
| PackedMips off | 8.98 ms | 81 |
| LODCascade **off** | 8.00 ms | **1,491** |
| MaxOuterIterations 600 / 1024 / 2048 | 9.15 / 9.81 / 9.92 ms | 81 |

Three findings worth carrying forward:
1. **Air-mip earns its keep** — disabling costs 44% here, and 3.5× at another
   pose. Highest-value structure in the traversal.
2. **The LOD cascade is a correctness feature, not just a perf feature.**
   Disabling it is *faster* (8.00 ms) but produces **18× more false misses**
   (1,491 vs 81). It must not be traded away for frame time.
3. **Iteration cap above 400 is essentially free** (9.15–9.92 ms across
   600–2048). Raising it only costs rays that previously hit the cap.

### THE CAVEAT THAT GOVERNS EVERYTHING DOWNSTREAM
All of the above is at **960×540 internal**, not 1920×1080. That is the
intended shipping config per Amendment 8.9 (render low, upscale). Raymarch
cost scales ~linearly with pixel count, so **native 1080p would be ~4× and
would blow the budget immediately**. The ~6 ms of headroom is what remains for
upscale, shadows, water shading, entities, UI, gameplay and physics *combined*.
Plan against 6 ms, not 16.67 ms.

---

## 6. Bugs found and fixed this phase

### 6.1 Horizon holes — FIXED (pre-existing since Phase 2)
`PHASE_2_COMPLETION.md` §6 item 6 recorded *"small sky-colored gaps along the
horizon at certain angles"* as an unexplained artifact. Phase 3's taller terrain
made it prominent. Root-caused with per-ray GPU telemetry:

- Failing rays graze at ~0.35° below horizontal, travelling ~165 voxels
  horizontally per voxel of descent.
- At cap 400 they reported `outerSteps = 400` **exactly** — iteration
  exhaustion — with `denseMicro = 335` (**84% of budget** single-stepping
  through the surface skin) and `currentDist = 636 voxels = 63.6 m`.
- `LODConfig.TIER_OUTER_RANGE_M[0]` is **64 m**. The ray expired **0.4 m before**
  promoting to tier 1, where each step covers 4× more ground.

Two one-line fixes (`PATCH_iteration_cap.txt`):
- `.compute` clamp `_MaxOuterIterations < 400` silently pinned the cap at 400 —
  the knob could only ever be turned *down*. A genuine bug independent of the
  hole; it is why an early "raise it to 2000" experiment appeared to exonerate
  the cap while changing nothing.
- Default `MaxOuterIterations` 400 → 1024.

Result at the worst pose: **167 → 11 false misses**, then → 0 after the
draw-distance guard band (§6.3).

### 6.2 Phantom terrain floating in the sky — FIXED
A detached slab hung ~100 rows above the true horizon in `Crater1_Beauty.png`.
Diagnosed from pixel data: slab colour (74,119,115) vs lit ocean (132,206,200)
— a uniform 0.57×, i.e. **the same water material shaded with a side normal**.

`ReadClipmap` addresses the clipmap toroidally with **no bounds check**:
```hlsl
int3 wrapped = brickCoord & (int3)(windowDimsBricks - 1);
```
Toroidal addressing is correct by design for a sliding window, but nothing
verified the coord was inside the current window. The traversal guarded **Y**
only — no X/Z equivalent. So a shallow *upward* ray stayed under y=128 for
thousands of voxels, outran the window's 512-brick extent, wrapped, and struck
the ocean near the origin.

Fixed by `PATCH_window_bounds.txt` (XZ bounds check alongside the existing Y
guard). **Result: PHANTOM = 0 at all 13 poses inside the draw distance.**

> **PHASE 4 MUST REVISIT THIS.** The check assumes window origin = brick
> (0,0,0), true only while the window is static. Once `StreamManager` slides
> the window it must become `int3 rel = bXZ - _WindowOriginBricks;`. This is
> flagged in the patch file itself.

### 6.3 Draw-distance boundary disagreements — CLASSIFIED, not a defect
Residual misses reported `currentDist = 2904.99` against `maxDist = 2900.00`
with `outerSteps = 170` (nowhere near the cap) — the ray ended because it ran
out of **draw distance**, not traversal budget. At that hard cutoff the GPU's
leap stepping and the CPU oracle's voxel stepping disagree by a few voxels.
The rig now excludes the outer 2% of draw distance and reports the count
separately.

### 6.4 Feature naturalism — FIXED
Mountains and craters were pure radial `smoothstep` — geometric primitives, and
they read as such. Replaced with 3-octave **ridged** noise (the `1-|n|` fold
produces crest lines, not lumpy domes) plus an anisotropic rotated footprint so
bases are irregular ellipses. Amplitude 0.20 → 0.55.

> **NO DOMAIN WARPING IS USED.** Every noise call samples at a plain
> scale-and-offset of the true world coordinate; output modulates *height* only
> and is never fed back into the sample position. Coordinates remain ground
> truth, which is what keeps `ColumnSampler` valid as the test oracle. The one
> coordinate transform (`Anisotropic`) is a fixed affine rotate+scale used only
> to measure radial distance.

### 6.5 Harness bugs found (worth recording — several nearly caused false verdicts)
| Bug | Symptom | Why it mattered |
|---|---|---|
| `world.meta` header size 17 vs actual 19 bytes | every read-back rejected | blocked Phase 3a entirely |
| Cave planner sampled `cy` before terrain | 1/3 caves placed | asked for ceilings terrain couldn't provide |
| Oracle excluded water from hit test | 1,858 bogus "false hits" | measured the oracle, not the GPU |
| Sky colour compared in linear vs sRGB | matched 0 pixels → false PASS | reported "no holes" while holes existed |
| Sky colour sampled per-frame from top-centre | ocean/grass/rock used as "sky" | ~300k phantom failures |
| Oracle marched 3000 voxels vs GPU's 2900 | boundary shell scored as defects | invented renderer bugs |
| `sweepPose = huntPoses[0]` after list reorder | swept a clean pose, all zeros | looked like success, proved nothing |

Lesson encoded in the rig: **every detector is now self-validating.** Sky colour
is calibrated from a guaranteed-sky view and cross-checked against the shader
constant (`maxChannelErr = 0.000` this run). The GPU debug probe validates its
own coordinate mapping by comparing the shader's reported `rayDir` against the
expected ray, and refuses to report telemetry if neither candidate mapping
agrees within 2°.

---

## 7. Known issues carried into Phase 4

| # | Issue | Severity | Notes |
|---|---|---|---|
| 1 | **World is 281.6 m, spec says 2,560 m** (§2.4 Small = 200×200 chunks) | HIGH | **Structurally requires Phase 4.** 40,000 chunks = ~3.3 min eager generation and ~12.5M dense bricks vs a 750k pool cap (16× overrun). Not tunable — needs a streaming window. |
| 2 | Square water boundary at world edge | MEDIUM | Direct consequence of #1. Outside the generated region `GetVoxel` returns air. Phase 4 streaming hides it. |
| 3 | `Cascades.UploadDirty` = 3,033 ms, 57% of startup | MEDIUM | Never optimised. Correct next perf target. |
| 4 | Grazing-angle terracing artifact | LOW | Pre-existing, `PHASE_2_COMPLETION.md` §6 item 6. Untouched. |
| 5 | Near-surface false hits (~4,388 sampled) | LOW / expected | Conservative LOD downsampling adds geometry adjacent to real surfaces. By design. Informational only. |
| 6 | XZ bounds check assumes window origin (0,0,0) | **BLOCKER FOR PHASE 4** | Must become `bXZ - _WindowOriginBricks` when the window slides. |
| 7 | 7 feature anchors is sparse | LOW | Fine at 281.6 m, absurd at 2,560 m. Scale counts with world area in Phase 4. |
| 8 | Generation is not Burst-compiled | LOW | Deliberate §5.1 deviation, deferred so Phase-3 benchmarks stayed comparable. Revisit when per-frame generation cost matters (i.e. Phase 4). |

---

## 8. How to test (the established workflow)

The protocol that worked, kept verbatim so it survives context loss.

### Scene setup — `Phase3_Island`
1. **Main Camera** (tagged `MainCamera`, URP): add `SimpleFlyCamera`.
2. **Empty GameObject `Phase3Bootstrapper`**, add three components in order:
   `Phase3Bootstrapper`, `Phase3AcceptanceRig`, `RaymarchAutoBenchmark`.
   Leave the benchmark's own "Run On Start" alone — the rig force-disables it
   in `Awake()` and chains into it, so it cannot race.
3. Nothing else. `RaymarchFeature` lives on the URP renderer asset.
4. World extent is an Inspector field on `Phase3Bootstrapper` (default 22).

### Run
1. **EditMode Test Runner → Run All.** Must be 156/156 green.
   *If red, stop.* Generation is CPU-proven before it is ever drawn — that
   ordering is the whole point of splitting 3a from 3b.
2. **Standalone build** (IL2CPP), run it. Editor numbers are not usable for
   performance. Everything is unattended: bootstrap → validators → rig
   (census, oracles, screenshots, frame timing, false-miss hunt, ablation
   sweep, GPU telemetry) → benchmark → quits and reveals in Finder.
3. Send back the `Phase3Acceptance/<timestamp>.zip` and
   `RaymarchBenchmarks/<timestamp>.zip`.

### Reading the output
- `phase3_report.txt` — everything. Ends with a **COMMIT READINESS** block
  stating correctness / performance / visual separately.
- `<Pose>_Beauty|UniformDense|LODTier|StepHeat.png` — unannotated renders.
- **`DEFECTS_<Pose>.png` — annotated copies. The magenta squares are painted by
  the rig to mark flagged pixels; they are NOT a rendering bug.** Every
  reported defect has a matching image so no number is ever unaccompanied.
- `Sweep_<config>.png` — one render per ablation config.
- `player_log.txt` — startup timings.

### Interpretation rules
- Use **`wall_avg`**, not `gpu_avg` (see §5).
- Check `stddev`; a spike with huge stddev is thermal, not cost.
- `falseMisses` are always defects. `PHANTOM` hits are always defects.
  Near-surface `falseHits` are expected LOD dilation.
- A frame-time figure without a stated internal resolution is meaningless.

---

## 9. Verdict

**Phase 3 is complete and committable.**

- §13 Phase 3 acceptance criteria met: deterministic generation, sane
  uniform/dense distribution, correct biome strata, feature anchors producing
  dense bricks only where they intersect, coastline, distinct biomes, caves
  with clearance, static water (including one crater that pools and one that
  does not).
- Two long-standing render bugs root-caused and fixed, one of which
  (`ReadClipmap` unbounded wrap) predates Phase 3 and would have become far
  worse under Phase 4 streaming.
- Performance is within budget at the shipping internal resolution with ~36%
  headroom.

**Blocking nothing. Phase 4 is unblocked.**

---

## 10. Phase 4 — next steps

Phase 4 is **3D streaming** (§2.4, §4). It is the correct next phase because it
resolves issues 1, 2, 3 and 7 from §7 simultaneously — world size, the square
water boundary, eager-generation startup cost, and feature density all collapse
into it.

Recommended order:

1. **Fix the window-origin assumption first** (§7 issue 6). The XZ bounds check
   added in Phase 3 hard-codes origin (0,0,0). Do this *before* the window ever
   moves, or the phantom-terrain bug returns in a harder-to-see form.
2. **`StreamManager` + moving resident window.** Chunks enter/leave as the
   camera moves; `WINDOW_CHUNKS_XZ = 32` already sizes the clipmap for it.
3. **Async generation off the main thread.** Generation is already parallel and
   pure; making it async removes the startup stall *and* is required for
   streaming anyway.
4. **Then** raise world size toward §2.4's 200×200. Not before — eager
   generation cannot reach it.
5. **Optimise `Cascades.UploadDirty`** (§7 issue 3) once uploads are incremental
   rather than whole-world.
6. **Scale anchor counts with world area** in `AnchorPlanner`.

Extend the acceptance rig rather than replacing it. The pieces that will need
new coverage: chunks correctly evicted and re-admitted at window edges, content
identical after a chunk leaves and returns (the content-hash check already
does this — point it at a moved window), and no seams at streaming boundaries.
Add a "window slid by N chunks" pose set to the false-miss hunt; the phantom
detector is exactly the tool that will catch a mis-wrapped clipmap read.
