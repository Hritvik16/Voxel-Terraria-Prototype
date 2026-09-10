# Fluid performance: dense vs tiled, scatter, combined load, CPU apply

**PROVISIONAL — every number here.** RELEASE standalone, launched outside the
Editor, wall clock from `Time.unscaledDeltaTime`. Good for magnitude and
direction; **not** a GPU-stage attribution.

Measured 2026-09-10 against `aa91b6b`. Rigs: `run-fluid-ab.sh` (steps 1/2/4),
`run-acceptance-fluid.sh` (step 3), `run-acceptance-rig.sh` (step 3's baseline).

---

## What may and may not be quoted

- **No `gpuFrameTime`, anywhere.** Amendment 8.10 measured it inflated
  ~2.6–2.7× on this hardware. It is read nowhere in any rig below.
- **No Xcode, no Instruments** (Amendment 8.9 Rule 1). Per-kernel Metal
  attribution is a confirmed dead end here — Unity merges the CA's eight
  dispatches into one encoder, so only the first kernel is ever named.
- **There is no Performance State field.** `FrameTimingManager` cannot report
  it on this platform. Permanent accepted limitation, not worked around.
- **§2.2's ≤3.5 ms fluid budget is a GPU-LANE budget.** A wall-clock frame
  total is a different quantity. Nothing below is scored against it, and the
  question "does the tiled CA meet §2.2" remains **unanswerable in this
  workflow** — see §6.
- Every config was run with a `REPEAT_driftcheck` twin per Amendment 8.9 §0
  Rule 2. **Spread is printed beside every number.** Two configs came back
  wider than 15% and were **re-run, not averaged**; the re-run reversed one of
  them, which is exactly why the rule exists.

---

## 1. Headline

| Question | Answer |
|---|---|
| Does tiling cost more or less than dense, per voxel? | **Neither, mostly.** Within ±1–7% at every volume tested, and the sign *changes* with volume: tiled is ~6–7% slower at 500–2,000 voxels, level at 8,000, and ~5% **faster** at 32,768. |
| Does scatter cost anything beyond raw volume? | **No measurable p50 cost.** 1 → 512 disconnected pockets at the same volume spans 9.11–9.70 ms, inside the driftcheck spread of the individual rows — while dispatched cells go 7.4M → 16.8M. |
| What is the CPU-side apply cost at scale? | **The dominant scaling term, and it is bursty.** `PumpAndApply` p50 is ~0.0006 ms at every volume; p99 is 0.52 → 1.84 → 6.7 → 22 ms across the ladder. |
| Does fluid fit inside the real acceptance rig? | **Frame p50 barely moves; p99 and the terrain upload budget both blow out.** §4.3's 1.0 ms upload budget goes to 9.8–11.2 ms with a live fluid load. |

**The single most useful finding is not in step 1.** Dense vs tiled is close
to a wash. What actually costs is the **CPU-side op-list apply** (step 4) and
the **clipmap upload pressure fluid creates** (step 3).

---

## 2. Step 1 — matched volume, dense vs tiled

Identical scenarios; the only difference is whether `FluidGpuSimulation` was
handed a `FluidTileMap`. `placed` and `live` are per-row evidence that the
volumes really were matched.

**The dense region is steelmanned**: re-sized per config to the smallest
power-of-two box holding the scenario, not the fixed box the shipped dense
path used. A tiled win here is conservative; a tiled loss is not automatically
a loss against the real dense configuration.

| placed | path | live slots | dispatch cells | p50 ms | drift | p99 ms | region |
|---|---|---|---|---|---|---|---|
| 512 | dense | 1,535 | 524,288 | **7.809** | 0.2% | 34.60 | 64×128×64 |
| 512 | tiled | 1,535 | 7,330,747 | **8.294** | 0.4% | 32.29 | — |
| 2,028 | dense | 6,068 | 524,288 | **7.991** | 1.1% | 31.19 | 64×128×64 |
| 2,028 | tiled | 6,069 | 7,405,568 | **8.583** | 1.0% | 27.20 | — |
| 8,000 | dense | 23,421 | 2,097,152 | **9.106** | 0.1% | 40.91 | 128³ |
| 8,000 | tiled | 23,453 | 7,405,568 | **9.202** | 3.2% | 27.99 | — |
| 32,768 | dense | 65,420 | 2,097,152 | **20.407** | 1.0% | 41.24 | 128³ |
| 32,768 | tiled | 65,536 | 7,435,059 | **19.405** | 0.1% | 29.23 | — |

| placed | dense p50 | tiled p50 | delta | significant? |
|---|---|---|---|---|
| 512 | 7.809 | 8.294 | **+6.2%** | yes — drifts 0.2% / 0.4% |
| 2,028 | 7.991 | 8.583 | **+7.4%** | yes — drifts 1.1% / 1.0% |
| 8,000 | 9.106 | 9.202 | +1.1% | **no** — inside tiled's own 3.2% |
| 32,768 | 20.407 | 19.405 | **−4.9%** | yes — drifts 1.0% / 0.1% |

**There is a crossover, around 8,000 voxels.** The mechanism is in the
dispatch-cells column: tiled dispatches 32³ = 32,768 cells per *resident*
tile, and tile residency is driven by where fluid *exists*, not where it is
moving — so on an island world the ocean inside the active radius keeps ~226
tiles resident even for a 512-voxel test pool. That is 14× the dense box's
cells at the small end. As the real load grows, the dense box has to grow with
it (64³ → 128³) while tiled's already-paid tile set barely changes, and tiled
comes out ahead.

**Tiled's p99 is better at every volume** (32.3 vs 34.6, 27.2 vs 31.2, 28.0 vs
40.9, 29.2 vs 41.2) — a more consistent frame, even where p50 is worse.

**Caveat on the top rung.** At 32,768 placed, both paths sit at ~65,500 live
slots against a 65,536 cap. That rung measures a **slot-saturated** system.
Both saturate equally so the A/B stands, but it is not a clean "32,768 live
voxels" measurement.

### The two rows that had to be re-run

`tiled_v32000` first measured **42.9% driftcheck spread** and `tiled_s8_v8000`
**20.4%**. Rule 2 says re-run, not average. On the re-run:

| config | first p50 | re-run p50 / twin | new spread |
|---|---|---|---|
| dense_v32000 | 22.514 (8.5%) | 20.407 / 20.211 | **1.0%** |
| tiled_v32000 | 34.309 (42.9%) | 19.405 / 19.423 | **0.1%** |
| tiled_s8_v8000 | 11.988 (20.4%) | 9.412 / 9.459 | **0.5%** |

The first sweep would have reported tiled as **+52.4% slower** at 32k. The
re-run shows **−4.9% faster**. The noisy figure was not a small error, it was
the wrong sign — printed with two decimal places.

---

## 3. Step 2 — same volume, varying scatter (tiled)

~8,000 voxels split across 1 / 8 / 64 / 512 disconnected pockets, each pocket
in its own tile (spacing 48 voxels > the 32-voxel tile edge).

| pockets | placed | live slots | tiles | dispatch cells | p50 ms | drift | p99 ms |
|---|---|---|---|---|---|---|---|
| 1 | 8,000 | 23,361 | 226 | 7,405,568 | 9.703 | 6.2% | 45.20 |
| 8 | 8,000 | 21,312 | 253 | 8,312,149 | 9.412 | 0.5% | 31.54 |
| 64 | 8,000 | 21,055 | 412 | 13,531,545 | 9.107 | 3.4% | 43.22 |
| 512 | 8,192 | 14,551 | 512 | 16,777,216 | 9.299 | 7.5% | 35.54 |

**Scatter does not independently add cost at p50.** The whole ladder spans
9.107–9.703 ms — 6.5%, comparable to the driftcheck spread on the individual
rows — while active tiles rise 226 → 512 and dispatched cells rise 2.3×. If
per-tile dispatch were the dominant term, s512 would be far worse than s1. It
is not.

**Two real caveats, neither of which the p50 column shows:**

1. **Scatter reduces how much fluid can be live.** Live slots fall 23,361 →
   14,551 as pockets go 1 → 512. The 512-tile pool cap is reached at s512 and
   refuses further acquisitions, so that rung sustains ~38% less live fluid
   than s1. It is the same volume *placed*, not the same volume *simulating*.
2. **p99 is noisy and does not trend cleanly** (45.2 / 31.5 / 43.2 / 35.5).
   No scatter conclusion should be drawn from the tail from this data.

---

## 4. Step 4 — CPU-side op-list apply

`System.Diagnostics.Stopwatch` bracketed around `FluidOpListReadback.PumpAndApply`
and nothing else. CPU main-thread lane, entirely separate from the GPU CA.
Flagged NOT MEASURED since Phase 5b.

| config | live slots | voxel writes | pump p50 | **pump p99** | submit p50 | frame p50 |
|---|---|---|---|---|---|---|
| dense_v500 | 1,535 | 27,274 | 0.0006 | **0.52** | 0.0005 | 7.809 |
| tiled_v500 | 1,535 | 27,232 | 0.0006 | **0.57** | 0.0004 | 8.294 |
| dense_v2000 | 6,068 | 111,152 | 0.0006 | **1.84** | 0.0004 | 7.991 |
| tiled_v2000 | 6,069 | 113,130 | 0.0007 | **2.33** | 0.0005 | 8.583 |
| dense_v8000 | 23,421 | 451,866 | 0.0006 | **6.71** | 0.0004 | 9.106 |
| tiled_v8000 | 23,453 | 449,872 | 0.0006 | **7.08** | 0.0004 | 9.202 |
| dense_v32000 | 65,420 | ~1.85M | 0.0007 | **21.98** | 0.0005 | 20.407 |
| tiled_v32000 | 65,536 | ~1.86M | 0.0008 | **22.47** | 0.0007 | 19.405 |

**The shape matters more than the magnitude.** p50 is ~0.0006 ms at *every*
volume — the median frame applies nothing, because the readback is async and
only lands on some frames. All the cost is in the tail, and the tail scales
roughly linearly with live voxels: **0.52 → 1.84 → 6.7 → 22 ms**.

At 8,000 live voxels the apply spike alone is **~7 ms** — more than a whole
60 fps frame, on the main thread, in one burst. This is a CPU lane, so it is
not covered by §2.2's GPU budget at all, and it is the clearest scaling
problem this sweep found.

`submit` (the CA dispatch call) is negligible at p50 (0.0004–0.0008 ms) and
2.1–3.9 ms at p99.

Dense and tiled apply costs are within a few percent of each other, as
expected — the op list is the same either way.

---

## 5. Step 3 — combined load inside the real acceptance rig

The Phase 4 acceptance rig, same gates and legs, with a live **tiled** fluid
load running while the camera flies and the window slides. This had never been
measured: every Phase 4 figure on record is terrain-only.

**The baseline is not disturbed.** The load is opt-in (`-fluidload`, default
0), the scene is a *clone* of `Phase 4 Streaming.unity` with one asset
reference added, and it builds to a separate app. `run-acceptance-rig.sh` is
byte-identical in behaviour and its build does not contain `FluidCA.compute`.

Both runs below are from the same session, minutes apart, same machine.

| | terrain only | + 8,000-voxel tiled fluid |
|---|---|---|
| result | **51 PASS / 2 FAIL** | **50 PASS / 6 FAIL** |
| Gate B frame total p50 / p99 | 7.69 / **15.98** | 9.10 / **54.92** |
| Gate C frame total p50 / p99 | 7.91 / **30.59** | 7.90 / **76.53** |
| §4.3 terrain upload p99 (Gate B) | — | **9.843 ms** |
| §4.3 terrain upload p99 (Gate C) | **1.216 ms** | **11.162 ms** |
| fluid live slots p50 | — | 32,128 (cap 65,536) |
| fluid active tiles p50 | — | 512 |
| CA submit p50 / p99 | — | 0.0005 / 2.92 ms |
| PumpAndApply p50 / p99 | — | 0.0006 / 1.54 ms |

**Frame p50 is essentially unaffected** (Gate C: 7.91 → 7.90). **p99 is 2.5–3.4×
worse.** The frame does not get slower on average; it gets much spikier.

**§4.3's 1.0 ms terrain upload budget is blown ~10×** — 1.216 ms terrain-only
becomes 9.8–11.2 ms with fluid. This is the most actionable result in the
document: every fluid voxel write marks its chunk dirty, and the clipmap
uploader pays for it. The fluid CA is not what breaks the frame; **the upload
pressure fluid creates** is.

### The four extra failures, and why three of them are not defects

| failure | reading |
|---|---|
| reloaded chunk changed content (4 of 9 hash-mismatched) | **Rig-design conflict, not a defect.** The gate hashes chunk content across a reload and asserts identity. Water flowed into those chunks between the two hashes; the content genuinely did change. |
| edits survived a 500 m round trip (hash mismatch) | Same conflict — Gate D's persistence check assumes no other writer. |
| no brick that was UNIFORM before the dig failed to coalesce back (23 of 23) | Same — the tunnel refilled with water, so the bricks legitimately are not uniform air. |
| terrain upload p99 9.8 / 11.2 ms | **Real, and the headline of this section.** |
| op-list readback errors (1) | **Unexplained.** Exactly one, in both fluid runs. Not chased down; flagged. |

**Gates C and D's content-identity assertions cannot pass with a live fluid
load, by construction.** They are not evidence of a persistence bug. If a
combined-load gate is wanted permanently, those assertions need a fluid-aware
variant — that is a design question, not something to paper over by loosening
them.

### The first attempt measured a flood

Recorded because the corrected number is only trustworthy next to it. The
first version re-seeded on every chunk change; at 60 m/s that is ~5 times a
second, so the run seeded 320 times and put 2.5M voxels in the world — slots
pinned at the cap, 4.7M pool exhaustions, pump p99 **54 ms**, 47/8. Re-seeding
is now conditional on the live population having fallen below half target
*and* a minimum interval, giving 1 top-up and the bounded numbers above. The
rig now asserts the load is **neither dormant nor saturated**, because both
produce a confident report about the wrong thing.

---

## 6. Plain answers

**Is the tiled approach within, near, or over §2.2's ≤3.5 ms fluid budget?**

**Unknown, and not claimable from this work.** §2.2 budgets the fluid CA on
the GPU lane. This workflow cannot attribute GPU stages — no Metal capture, and
`gpuFrameTime` is inflated ~2.6–2.7× here so it is read nowhere. What can be
said: the two CPU-lane fluid costs are **submit p50 ~0.0005 ms / p99 2.1–3.9 ms**
and **apply p50 ~0.0006 ms / p99 0.5–22 ms depending on volume**. Neither is
the GPU CA. The honest position is unchanged from
`FLUID_SCALE_ARCHITECTURE_RESULTS.md` §6: whether the constant is affordable is
a separate, currently unanswerable question.

**Does scatter pattern independently add cost beyond raw voxel count?**

**No, not at p50, over 1 → 512 pockets at matched volume.** 9.107–9.703 ms
across the ladder, within the rows' own driftcheck spread, while dispatched
cells rise 2.3×. The two things scatter *does* cost: it reduces how much fluid
can be simultaneously live once the 512-tile pool cap binds (23,361 → 14,551
live slots), and the p99 data is too noisy to conclude anything about the tail.

**Does tiling cost more, less, or the same per voxel than dense?**

**The same, to within a few percent, with a crossover around 8,000 voxels.**
Tiled is 6–7% slower below ~2,000 live voxels, level at 8,000, and ~5% faster
at 32,768 — and has a better p99 at every volume. Tiling was never justified
on speed; it was justified on the memory scaling in
`FLUID_SCALE_ARCHITECTURE_RESULTS.md`. **This sweep says it did not cost speed
to get that**, which is the useful result, rather than that it bought any.

---

## 7. Limitations

- **Wall clock only**, and no GPU-stage attribution anywhere. Direction and
  magnitude, not absolute per-kernel cost.
- **The 32,768 rung is slot-saturated** (~65,500 of a 65,536 cap) in both
  paths. Matched, but not a clean measurement of that volume.
- **Tile residency includes the ocean.** On this island world ~226 tiles stay
  resident within the active radius even for a 512-voxel test pool, so tiled's
  dispatch-cell counts are dominated by resident-but-dormant water. That is
  realistic for this world and would differ on another.
- **Step 2 is tiled-only.** A 512-pocket scatter spans ~1,100 voxels; the dense
  box for it would be ~1.4 billion cells. That inability is itself a result.
- **`SizeDenseRegion` reaches only to the centre surface.** Correct for step 1's
  single centred pocket; would need revisiting before running a *dense* scatter
  over varied terrain.
- **One unexplained op-list readback error** per combined-load run.
- **Step 3's terrain-identity failures are a rig-design conflict**, deliberately
  reported rather than resolved by loosening the assertions.

Out of scope and untouched, as instructed: the §9.7 stopgap-retirement
question, and any gas / fire / density-layering work.
