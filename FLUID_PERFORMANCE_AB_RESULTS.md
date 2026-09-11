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

---
---

# SESSION 2 — 2026-09-10, commits `95a357d`..`2e7498d`

**This section APPENDS to the session-1 data above; it does not replace it.**
Session-1 rows were measured at `aa91b6b` and remain valid for that commit.
Where a number is superseded, both are shown with their commit.

Same methodology throughout: RELEASE standalone outside the Editor, vsync off,
wall clock, one config per process launch, discarded warm-up, driftcheck twin
on every timing figure. No `gpuFrameTime`, no Xcode, no Instruments, no
Performance State field. All **PROVISIONAL**.

---

## S2.1 — The stray readback error: my harness, not the fluid system

Session 1 reported "exactly one op-list readback error, every combined-load
run" and flagged it unexplained.

**The premise was wrong: it came from n=2.** Instrumented (per-request sequence
number, issue frame, age, in-flight depth, ring index, captured for the *first*
error only so it cannot be overwritten) and re-run, it is **intermittent —
4 of 7 runs had exactly one, 3 had none.**

Both captured instances agree:

```
  seq=248/248  issuedFrame=1048  nowFrame=1073  ageFrames=25  inFlight=1  ring=3
  seq=167/167  issuedFrame=795   nowFrame=821   ageFrames=26  inFlight=1  ring=2
```

The failing request is always the **most recently issued** (`seq == total
issued`, nothing after it — so not a startup race) and always **~25 frames
old**. Twenty-five frames is the tell: session 1's integration ticked the fluid
from `SamplePhases`, which the rig calls only on a gate's *sampled* frames.
Between gates the rig takes screenshots, waits for idle and runs validator
`GetData` storms — synchronous GPU work — while an issued `AsyncGPUReadback`
sat un-polled across all of it. The fluid-only rig, which pumps every frame,
produced **zero errors across 24 configs**: the control this needed.

**Fix:** tick from `Update()`, every frame. **0 errors in 5 runs after, vs 4 in
7 before.** It is also simply more honest — a real game pumps every frame.

Side effect, expected: ticking every frame raises ops total (543K → 756K–1.01M)
and makes `StaleOpsDropped` non-zero (216K–478K). Both follow from simulating
more ticks under active streaming.

**Verdict: not a fluid defect. An artefact of the session-1 measurement
harness, now removed.**

---

## S2.2 — The upload blowout is NEITHER call count NOR byte volume

The question was: do N fluid voxels moving in one chunk cost N dirty-marks, or
does something coalesce them?

**Call count is already solved.** `MarkDirty` is called ~once per voxel write,
and `_dirtyChunks` is a `HashSet`:

| rung | voxel writes | MarkDirty calls | coalesced | distinct chunks | coalesce rate |
|---|---|---|---|---|---|
| 500 | 27,174 | 27,377 | 27,174 | 203 | **99.26%** |
| 8,000 | 450,464 | 450,708 | 450,465 | 243 | **99.95%** |
| 32,000 | 1,844,098 | 1,844,342 | 1,844,099 | 243 | **99.99%** |

Chunks uploaded per frame: **1**. There is no per-voxel upload call to batch.
**Step 2 as specified is not applicable and was not forced.**

**Byte volume is not it either.** Gate C mean upload bytes/frame goes
**0.42 MB → 0.93 MB** with fluid, and the byte cap binds on **0 of 881 frames**.
A 2.2× byte increase cannot produce an 18× time increase.

### What it actually is: the LOD cascade

`StreamManager.LastUploadMs` spans the clipmap upload **and
`_cascades.UploadDirty`**, while the phase breakdown only ever covered the
clipmap half — which is why every named phase summed to 0.08 ms against a
6.6 ms total. The resident-chunk loop marks *both* mirrors from the same
`chunk.dirty` flag, so every fluid-touched chunk forces a tier-1 + tier-2
re-downsample.

| Gate C | terrain only | with fluid |
|---|---|---|
| `upload_ms` p50 | 0.001 ms | **6.595 ms** |
| cascades total p50 / p99 | 0.00 / 0.11 ms | **6.36 / 8.95 ms** |
| — downsample | 0.00 / 0.00 ms | **6.35 / 8.64 ms** |
| — gpu writes | 0.00 / 0.00 ms | 0.30 / 0.83 ms |
| cascade chunks/frame | 0.00 | 1.86 |

**6.36 of the 6.595 ms is the cascade; 6.35 of that is `LODDownsampler`.**
CLAUDE.md already records that it allocates a fresh `byte[]` per chunk *per
tier* (256 KB + 32 KB) on every rebuild — also where the `gc+1` on the worst
frames comes from.

**A wrong turn, recorded.** The dirty-set `Sort` sits after the stopwatch
starts but before the first `phaseStart`, so it was untimed and looked like the
obvious gap. Timed it: **0.00 / 0.00 ms, dirty set p50 2 chunks.** Not the
cause. The timer stays — an untimed region inside a budgeted path is worth
closing regardless.

**Recommended fix, NOT done here:** §8.5's frame-budget pattern applied to
*cascade rebuilds*. That is a change to the LOD cascade, not to fluid, and
deserves its own isolated checkpoint rather than being bundled into a fluid
session.

---

## S2.3 — Frame-budgeting the apply: the burst goes

`PumpAndApply` now stages a batch and applies at most **4096 ops/frame**,
carrying the rest forward.

**Budget ON vs OFF, same build, same session** — a better control than
comparing across sessions, and the mutation check:

| config | pump p99 ON | pump p99 OFF | frame p50 ON | frame p50 OFF |
|---|---|---|---|---|
| tiled_v8000 | **3.905** | 6.758 | 10.494 | 9.309 |
| tiled_v32000 | **4.785** | 26.070 | **11.903** | 19.495 |

The burst returns the moment the cap is removed. Against session 1's
unbudgeted figures (`aa91b6b`):

| config | pump p99 S1 | pump p99 S2 | change |
|---|---|---|---|
| dense_v8000 | 6.712 | **3.771** | −44% |
| tiled_v8000 | 7.083 | **3.905** | −45% |
| dense_v32000 | 21.982 | **4.384** | −80% |
| tiled_v32000 | 22.471 | **4.785** | −79% |

Driftcheck spreads on the budgeted rows: **1.7% / 1.8% / 1.9% / 3.0%.**

**The cost, reported not buried.** Back-pressure means the CA ticks less often
when the CPU cannot keep up, so fluid simulates slower under load — voxel
writes in the same window fall **2.9% at 8,000** and **42.9% at 32,000**. At
32,000 that buys frame p50 19.5 → 11.9 ms as well as the p99, so it is a good
trade. At 8,000 it costs 1.2 ms of frame p50 to halve the apply burst, which is
more arguable. **4096 was chosen a priori, not tuned** — the rig now takes
`-fluidopbudget` and sweeping it is an obvious follow-up.

**§9.4 / §9.5 are not weakened, and this needed no new rule.** `Apply` already
re-validates residency and expected material at the moment of application. A
carried op whose chunk was evicted while it waited hits the existing residency
guard and counts as `OpsDroppedNonResident` exactly as an immediate op would.
Not a design fork.

---

## S2.4 — Combined load, re-verified

| | S1 before (`aa91b6b`) | S2 after (`d4593ee`) |
|---|---|---|
| result | 50 PASS / 6 FAIL | **51 PASS / 5 FAIL** |
| op-list readback errors | 1 | **0** |
| Gate B frame total p50 / p99 | 9.10 / 54.92 | 8.87 / 50.71 |
| Gate C frame total p50 / p99 | 7.90 / 76.53 | 7.49 / 72.81 |
| §4.3 upload p99 (Gate B / C) | 9.843 / 11.162 ms | 8.961 / 9.699 ms |
| PumpAndApply p99 | 1.542 ms | 1.505 ms |
| live slots p50 | 32,128 | 26,118 |

**§4.3 is still ~9× over its 1.0 ms budget, and that is expected**: S2.2 showed
the cause is the LOD cascade, which was deliberately not changed. The apply
budget could not have fixed it.

**`PumpAndApply` p99 barely moved here (1.542 → 1.505 ms), and that is honest
rather than disappointing** — at this rig's ~26K live slots the per-frame op
count rarely reaches 4096, so the budget seldom bites. Its benefit shows at the
higher volumes in the controlled rig, not in this configuration.

**The three terrain-identity failures still fail, for the same documented
reason** — confirmed, not assumed:

- `no reloaded chunk changed content (4 of 9 hash-mismatched)`
- `edits survived a 500m round trip: 0x46EB1687 == 0xABE2659F`
- `no brick that was UNIFORM before the dig failed to coalesce back (23 of 23)`

These are now a standing rule in CLAUDE.md ("RIG SELECTION RULE"): they are
expected under live fluid, and the assertions must not be loosened.

---

## S2.5 — Full re-verification sweep (`d4593ee`)

Every rig from session 1's table, re-run against Steps 0/1/3.

| rig | session 1 | session 2 | verdict |
|---|---|---|---|
| Phase 5a reference | 5 scenarios, ledger OK | 5 scenarios, 0 unbalanced, 0 dup ownership | **unchanged** |
| Phase 5c edit stress | 170 PASS / 0 FAIL | **170 PASS / 0 FAIL** | unchanged |
| Phase 5d streaming × fluid | 19 PASS / 0 FAIL | **19 PASS / 0 FAIL** | unchanged |
| `run-fluid-activity.sh` (dense baseline) | 19 PASS / 0 FAIL | **19 PASS / 0 FAIL** | unchanged |
| Phase 6 brush guard | 30 PASS / 0 FAIL | **30 PASS / 0 FAIL** | unchanged |
| Phase 6 sandbox | 43 PASS / 0 FAIL | **43 PASS / 0 FAIL** | unchanged |
| `run-fluid-scale.sh` | 4 PASS / 0 FAIL | **4 PASS / 0 FAIL** | unchanged |
| `run-fluid-tiled.sh` | 19 PASS / 0 FAIL | **19 PASS / 0 FAIL** | unchanged |
| EditMode | 477 PASS / 0 FAIL | **485 PASS / 0 FAIL** | +8 (OpDrainCursor) |
| Combined load | 50 PASS / 6 FAIL | **51 PASS / 5 FAIL** | readback error gone |

**No rig needed weakening, and none changed what it proves.**

---

## S2.6 — Mutation sweeps

**`OpDrainCursor` — 6 mutants, all killed:**

| mutant | killed by |
|---|---|
| `Take` overruns the batch | 3 tests, incl. exactly-once |
| head advances one short (duplicates) | 3 tests |
| head advances one far (drops) | 3 tests, incl. ordering |
| `Stage` overwrites an undrained batch | the refusal test |
| `WouldCarry` off-by-one | the agreement test |
| zero budget discards instead of stalling | the stall test |

---

## S2.7 — Still open after this session

- **The LOD cascade re-downsample is the §4.3 blowout** and is unfixed.
  Recommended: §8.5's budget applied to cascade rebuilds. Own checkpoint.
- **The 4096 op budget is untuned.** `-fluidopbudget` exists to sweep it; the
  8,000 rung's 1.2 ms p50 regression is the number to optimise against.
- **Fluid throughput falls 42.9% at 32,000 live voxels** under back-pressure.
  Bounded frames were bought with simulation rate; whether that trade is right
  at that load is a design call, not a measurement one.
- **§2.2 remains unanswerable** — GPU-lane budget, no GPU-stage attribution in
  this workflow. Unchanged from session 1.
- Gas / fire / density-layering remain unspecified and untouched.

---
---

# SESSION 3 — 2026-09-10, commits `35baa4f`..`e71cd01`

**Appends to sessions 1 and 2; replaces nothing.**

**METHODOLOGY CHANGE THAT AFFECTS EVERY EARLIER NUMBER.** This machine
throttles monotonically. An uncooled ON/OFF/ON/OFF sequence measured the *same*
config at **4.172 / 4.751 / 11.812 ms** — a 183% spread. Every figure below was
taken with **300 s idle cooldowns between runs** (150 s for the shorter
FluidAB runs). Sessions 1–2's figures were taken back-to-back and carry
unquantified thermal inflation, worst in the tail.

## S3.1 — The cascade was already budgeted; the waste was a duplicated chain

The brief was "apply §8.5's frame budget to cascade rebuilds". **It is already
budgeted**: `MAX_CASCADE_CHUNKS_PER_FRAME = 2`, `MAX_CASCADE_MS_PER_TIER = 2.0`
over 2 tiers ≈ 4 ms/frame by design. And the queue is **not** backing up —
instrumented under live fluid the backlog is p50 2, p99 14, max 16 chunks and
ends where it starts. Deferring more would only make distant terrain staler.

**Wrong guess, measured and discarded:** I assumed the 2 MB tier-0 gather was
the waste. It is 1514 gathers × 0.337 ms against ~3.76 ms per chunk-tier —
**~9%**. (Same lesson as session 2's dirty-set `Sort`.)

**The real duplicate is the halving chain.** Every dirty chunk is marked dirty
on *every* tier, and each tier re-ran the chain from Tier0. At the shipped
sizes `{0.1, 0.2, 0.4}` tier 1 is one step (262,144 majority votes) and tier 2
is two (262,144 + 32,768) — **tier 2 repeats 89% of tier 1's work; 47% of the
combined total is redundant.**

`LODDownsampler.BuildChain` now runs the chain once to the deepest tier;
`LODCascadeManager` drives the tiers chunk-major so gather and chain are paid
once per chunk. The per-tier path is retained byte-for-byte behind
`SharedChainEnabled` as the regression baseline and the mutation check.

### Cooled A/B (300 s between runs, pair order reversed)

| cfg | upload p50 | upload p99 | downsample p50 |
|---|---|---|---|
| ON A | 3.803 | 8.649 | 3.29 |
| ON B | 4.182 | 7.987 | 3.76 |
| OFF A | 6.611 | 8.738 | 6.30 |
| OFF B | 6.586 | 8.920 | 6.27 |

Driftcheck: **ON 10.0%**, **OFF 0.4%**.

- **downsample p50 6.29 → 3.52 (−44%)**, matching the predicted 47% — the best
  evidence the mechanism is the one identified.
- **upload_ms p50 6.60 → 3.99 (−39.5%)**
- The 10.0% ON spread does not threaten the conclusion: worst-ON (4.182) vs
  best-OFF (6.586) is still −36.5%.

### What did NOT improve, and the brief expected it to

- **§4.3 upload p99: ~8.83 → ~8.32 (≈6%).** §4.3 is a **p99** gate, so it
  still fails at ~8× budget. Halving the median does not move the tail.
- **Gate C frame p99: no measurable change** (67.8/54.4 ON vs 57.8/63.3 OFF).

Attributing the remaining p99 tail is a separate isolation job and is **not**
claimed to be understood. Recorded as open item 3.

## S3.2 — Apply-budget curve

See **open item 2**. `16384` is ruled out on evidence; `1024` vs `4096` is a
genuine judgment call and is left undecided.

## S3.3 — Combined load, three-session chain

| | S1 `aa91b6b` | S2 `d4593ee` | S3 `e71cd01` (cooled) |
|---|---|---|---|
| result | 50 PASS / 6 FAIL | 51 PASS / 5 FAIL | **51 PASS / 5 FAIL** |
| readback errors | 1 | 0 | **0** |
| §4.3 upload p99 (Gate C) | 11.162 | 9.699 | **7.64** |
| cascade downsample p50 | 6.35 | 6.31 | **3.52** |
| PumpAndApply p99 | 1.542 | 1.505 | 1.51 |
| Gate C frame p50 / p99 | 7.90 / 76.53 | 7.49 / 72.81 | 7.6–8.4 / **50–68** |

The frame-p99 improvement is **mostly the cooldowns, not the fixes** — the
OFF runs show the same range. Stated rather than claimed.

The **three terrain-identity failures are unchanged, for the same documented
reason** (CLAUDE.md's RIG SELECTION RULE), confirmed by reading them in all
four cooled runs.

## S3.4 — Full regression sweep

| rig | result | vs session 2 |
|---|---|---|
| **TERRAIN-ONLY acceptance (no fluid)** | **53 PASS / 0 FAIL** | **was 51/2 — both prior failures gone; §4.3 passes at p99 0.613 ms** |
| Phase 5a reference | 5 scenarios, 0 unbalanced, 0 dup | unchanged |
| Phase 5c edit stress | 170 PASS / 0 FAIL | unchanged |
| Phase 5d streaming × fluid | 19 PASS / 0 FAIL | unchanged |
| `run-fluid-activity.sh` | 19 PASS / 0 FAIL | unchanged |
| Phase 6 brush guard | 30 PASS / 0 FAIL | unchanged |
| Phase 6 sandbox | 43 PASS / 0 FAIL | unchanged |
| `run-fluid-scale.sh` | 4 PASS / 0 FAIL | unchanged |
| `run-fluid-tiled.sh` | 19 PASS / 0 FAIL | unchanged |
| EditMode | 485 PASS / 0 FAIL | unchanged |

**The terrain-only result is the one that mattered** — Step 1 changed the LOD
cascade, a subsystem fluid does not own. It did not regress; it improved.

## S3.5 — Mutation sweep: the shared chain had no unit coverage

Run against the work built this session. All five mutants **survived** the
suite at 485 PASS / 0 FAIL — `BuildChain` stopping a step short, a
`ChainResultFor` off-by-one, skipping the gather, dropping the residency
re-check, and never consuming the dirty entry. The rigs exercise the path and
passed, but the unit suite could not distinguish correct from broken.

`LODSharedChainTests` closes it (EditMode 485 → **494**), and all five now die.
The load-bearing test compares the shared chain against the per-tier path tier
by tier, byte for byte — the optimisation's actual claim — with a guard test
asserting the fixture isn't vacuous.

The residency test covers the window the shared path itself opened: a chunk
evicted *between* `SelectBatch` and the write. Without the re-check it gets
coarse geometry written for terrain that no longer exists.

---
---

# THERMAL TRUST AUDIT — read this before citing ANY number above

Added 2026-09-10 (session 4). **Nothing above was re-measured for this audit**;
it classifies what is already recorded so nobody cites an uncooled figure as
settled.

## How the classification was made

Not by memory — by run-start gaps on disk and by each harness's own
driftcheck design.

- **Cooled runs show 407–475 s between run starts** (a ~4 min run plus a 300 s
  idle cooldown).
- **Every suspect sequence shows 110–157 s between starts**, i.e. a ~4 min run
  with essentially zero idle. Back-to-back.
- **None of the three committed harnesses** (`run-fluid-ab.sh`,
  `run-acceptance-fluid.sh`, `run-acceptance-rig.sh`) contains a cooldown.
  Every cooled figure on record came from an ad-hoc script. **This is itself a
  gap**: re-running a committed harness today reproduces uncooled numbers.

## The two harnesses are NOT equally affected, and this matters

| | acceptance rig (`run-acceptance-*.sh`) | FluidAB (`run-fluid-ab.sh`) |
|---|---|---|
| run length | ~4 min, heavy | ~90 s |
| driftcheck twin | **none** — single runs | **yes**, all twins run at the END of the sweep, so the pair straddles the whole sweep and genuinely detects cross-sweep drift |
| measured uncooled drift | **183%** on upload p50 across 4 back-to-back runs | median **1.9–6.9%**, max excursions caught and re-run |

The 183% figure that triggered all of this was the **acceptance rig**. The
FluidAB sweeps carry their own evidence that they did *not* drift badly.

## Verdict per figure class

| figures | status |
|---|---|
| **Session 3 cooled A/B** (cascade ON/OFF: upload p50 6.60→3.99, downsample 6.29→3.52) | **KNOWN-GOOD.** 300 s cooldowns, order reversed, ON drift 10.0% / OFF 0.4% |
| **Session 3 budget sweep** (1024/4096/16384 curve) | **KNOWN-GOOD.** 150 s cooldowns, driftchecks 0.2–4.0% |
| **Session 3 terrain-only baseline** (53 PASS / 0 FAIL, §4.3 p99 0.613 ms) | **KNOWN-GOOD** for pass/fail; the 2768 s preceding gap makes it genuinely cold |
| **Sessions 1–2 FluidAB sweeps** (dense-vs-tiled ladder, scatter ladder, PumpAndApply p99 ladder) | **DIRECTION RELIABLE, MAGNITUDE ±~7%.** Uncooled, but each carries an end-of-sweep driftcheck twin with median spread 1.9–6.9%. Do not quote to 3 significant figures; the crossover and the shape of the curves stand |
| **Sessions 1–2 acceptance-rig figures** (Gate B/C frame p50 & p99, §4.3 upload p99, combined-load cascade cost) | **UNVERIFIED MAGNITUDE, DIRECTION ONLY.** Uncooled, 110–157 s gaps, and **no driftcheck twin at all**. This is the suspect class |
| **Gate C frame p99 "72–76 ms"** (sessions 1–2) | **SUPERSEDED.** Measures 50–68 ms cooled, *for both configs*. Do not cite 72–76 |
| Session 1–2 **counts** (ops, voxel writes, live slots, tiles, gathers, backlog) | **TRUSTWORTHY.** Counts are not timings; the 4-trial readback series varied only ±2% |

## The two specific comparisons this weakens

1. **Terrain-only vs combined-load (sessions 1–2).** The pairs were taken
   157 s and 135 s apart — the terrain baseline ran immediately after a fluid
   run. Some of the measured gap is thermal. **Direction is safe** (0.001 vs
   6.8 ms upload p50 is far too large to be drift, and the cascade mechanism
   explains it), but the magnitude is not settled.
2. **"Terrain-only improved from 51 PASS / 2 FAIL to 53 PASS / 0 FAIL."** The
   session 1–2 run was uncooled, the session 3 run had a 2768 s preceding gap.
   **Part of that improvement may be thermal rather than the cascade fix.**
   The pass/fail change is real; attributing it entirely to the fix is not
   supported.

---

# OPEN ITEMS REQUIRING A HUMAN DECISION

**One place, so this does not have to be mined out of five documents.**
Last consolidated 2026-09-10 (`e71cd01`). Every item below is a judgment call
with evidence for each option, deliberately **left undecided** by the agent
sessions that surfaced them. None is a correctness failure.

### 1. Retire the multi-instance fluid-simulation stopgap? — OPEN since 2026-09-06

§9.7's fix had `EditService` hold a **list** of separate `FluidGpuSimulation`
instances so two player-placed pools would both wake. The tiled substrate
subsumes this structurally — acceptance scenario D shows **one** CA instance
covering two pools 30 m apart (2/2 tiles resident, 10,610 voxel writes),
because a tile exists wherever fluid is.

- **Keep it:** the list is still what the **dense** path needs, and the dense
  path is the retained regression baseline for four phases of proofs.
  Removing a working mechanism the older path depends on buys nothing today.
- **Retire it:** it is no longer load-bearing for the tiled substrate, and
  carrying two ways to do the same thing has its own cost.

**Retiring it is really the same decision as retiring the dense path**, which
is the larger call. Evidence: `FLUID_SCALE_ARCHITECTURE_RESULTS.md` §4b.

### 2. Fluid apply budget: 1024 or 4096 ops/frame? — OPEN, measured 2026-09-10

Cooled sweep, driftchecks 0.2–4.0%:

| volume | budget | pump p99 | frame p50 | voxel writes | vs 4096 |
|---|---|---|---|---|---|
| 8,000 | **1024** | **0.995** | **8.900** | 268,990 | **−39.0%** |
| 8,000 | 4096 | 3.549 | 9.701 | 440,786 | — |
| 32,000 | **1024** | **0.969** | **7.605** | 320,356 | **−68.7%** |
| 32,000 | 4096 | 3.794 | 10.223 | 1,022,646 | — |

**16384 is ruled out on the evidence** — at 8,000 it buys +2.9% throughput for
+2.9 ms of pump p99 (throughput has already saturated by 4096); at 32,000 it
costs +9.5 ms pump p99 *and* +5.2 ms frame p50.

1024 vs 4096 is **not** decidable from the numbers:

- **1024** puts the apply burst under 1 ms at both volumes and gives the best
  frame p50 at 32,000 (7.6 vs 10.2). Choose if frame smoothness dominates.
- **4096** simulates fluid 1.6×–3.2× faster under load. Choose if fluid
  fidelity under heavy load dominates.

This is a question about how the game should feel, not a measurement.
**4096 remains the default** only because nothing beats it outright.

### 3. Should §4.3's 1.0 ms upload gate apply under live fluid at all? — NEW

The cascade fix halved the **median** (upload_ms p50 6.60 → 3.99, downsample
p50 6.29 → 3.52) but **§4.3 is a p99 gate and the p99 barely moved**
(~8.8 → ~8.3 ms). It still fails at ~8× budget with fluid, while **terrain-only
now passes at p99 0.613 ms with 0 FAIL**.

- **Keep one gate:** a budget that only holds without fluid is not a budget.
- **Scope it per rig:** the number was derived for terrain streaming; a live
  fluid load is a different workload, and this is the same reasoning already
  accepted for the terrain-identity gates (CLAUDE.md's RIG SELECTION RULE).

Deciding this needs a target for what fluid *should* cost, which does not
exist yet. Attributing the remaining p99 tail is a separate isolation job and
is **not** claimed to be understood.

### 4. §2.2's GPU-lane budget is permanently unattributable here — STANDING LIMITATION, NOT A TASK

§2.2 budgets the fluid CA at ≤3.5 ms **on the GPU lane**. This toolchain
cannot attribute GPU stages: no Xcode and no Instruments (Amendment 8.9
Rule 1), `gpuFrameTime` is inflated ~2.6–2.7× (Amendment 8.10) and is read
nowhere, and per-kernel Metal attribution is a **confirmed dead end** — Unity
merges the CA's eight dispatches into one encoder, so only the first kernel is
ever named.

Every fluid figure on record is therefore **CPU-lane or wall-clock**. Whether
the tiled CA meets §2.2 is **unknown and unknowable in this workflow**. This
is not a pending measurement; it should stop being re-litigated each session.

### 5. Gas / fire / density-layered stacking — UNSPECIFIED, NOT A GAP TO FILL SILENTLY

Real Noita mechanics with **no specification anywhere in this architecture**.
Repeatedly deliberately not invented. Any implementation needs a design
conversation and a spec first. Not blocked by the substrate work.

### A methodological note that outranks most of the above

**This machine throttles monotonically and back-to-back runs are not
comparable.** An uncooled ON/OFF/ON/OFF sequence measured the *same* config at
4.172 / 4.751 / 11.812 ms — a 183% spread. Every timing figure in this document
from 2026-09-10 onward was taken with **300 s idle cooldowns between runs**.
Figures recorded before that date carry unquantified thermal inflation,
particularly the tail: Gate C frame p99 was recorded at 72–76 ms uncooled and
measures 50–68 ms cooled, **for both configs**.
