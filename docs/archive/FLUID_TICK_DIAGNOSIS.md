# Why one fluid CA tick takes 6–7 frames: diagnosis and options

**Owner decision 11 (2026-09-24): diagnose first, no fix until the owner chooses.**
**Nothing in the shipped fluid path was changed.** What was added is
instrumentation only: counters in `FluidOpListReadback` and report lines in
`-sustained`. There's also one diagnostic seam, `-maxqueued N`, and the
`run-fluidtick-diag.sh` runner.

Tags: **MEASURED** (from the runs below), **ESTABLISHED** (read from code),
**INFERRED** (follows from the measurements but wasn't directly observed).

## 1. The measurement

`run-fluidtick-diag.sh`, 2026-09-24 21:49. Each arm is 3 minutes sustained:
release build, fullscreen, golden hour, orbit, rung 0, live fluid from the
arena's water, sand and lava vents. Log: `Logs/fluidtick_diag_20260924_214933.txt`.

| arm | FPS | frames per tick | readback latency | drain | issue gap | ops per tick | ticks/s |
|---|---|---|---|---|---|---|---|
| A shipped (gate 960) | 33.2 | **7.25** | **3.58** | **3.68** | 1.00 | 3,109 | 4.6 |
| B `maxQueuedFrames` 1 | 33.1 | 7.19 | 3.56 | 3.65 | 1.00 | 3,091 | 4.6 |
| C gate 560 | 58.6 | **6.11** | **3.84** | 2.28 | 1.00 | 1,473 | 9.6 |

The period is exactly latency + drain − 1 + gap: A gives 3.58 + 3.68 − 1 + 1 = 7.26,
against 7.25 measured. The three parts account for the whole tick, with nothing
left over. Latency histograms: A 2:132, 3:92, **4:613**, 5:1. C 2:93, 3:95,
**4:1550**, 5:2.

## 2. What that says

**(1) Readback latency: about 3.6–3.8 frames, fixed in FRAMES rather than in
milliseconds.** MEASURED. Nearly doubling the frame rate (33 → 59 FPS) left it
at about 4 frames. In time, that's 108 ms at 33 FPS and 65 ms at 59 FPS. So it's
a pipeline depth: the op-list comes back a fixed number of frames after the tick
is issued. `QualitySettings.maxQueuedFrames = 1` changed nothing (3.56 against
3.58, same FPS). INFERRED: on this Metal player that setting either isn't honoured,
or doesn't govern the depth that matters here. **Where the ~4 frames go
(command-buffer depth, the drawable queue, or when Unity polls readback
completion) can't be attributed without a GPU capture.** This workflow has none,
by rule.

**(2) Drain: half the period, and it scales with the work.** MEASURED. At about
3,100 ops per tick and `MaxOpsAppliedPerFrame` = 1024
(`FluidOpListReadback.cs`), a batch takes about 3.7 frames to apply. At 59 FPS
each tick carries fewer ops (1,473), so the drain drops to 2.3 frames. **The
handoff's hypothesis, that the 1024-op budget holds the next tick behind the
drain, is confirmed for half of the period, not all of it.**

**(3) Issue gap: exactly 1 frame on every tick.** ESTABLISHED + MEASURED.
`Playground.Update` checks `_readback.CanIssue` *before* it calls
`PumpAndApply()`. On the frame a batch finishes draining, `CanIssue` was already
false, so the next tick waits a frame. **This may be load-bearing, not
accidental** (see option A).

**Why fluid speed follows frame rate:** all three parts are counted in frames, so
ticks/s ≈ FPS / 6–7. That's 4.6/s at 33 FPS and 9.6/s at 59 FPS in this scenario.
It is not a performance fault. It's the "one op-list in flight" correctness bound
(`MaxFramesInFlight = 1`, which stops the CA double-committing mass) applied to a
pipeline 3.6–3.8 frames deep.

## 3. Options (none implemented)

The owner's goal is a **fixed simulation rate, independent of frame rate, within
the one-op-list-in-flight bound** (never raise `MaxFramesInFlight`).

**The ceiling any option must respect:** with one list in flight, and a batch
that drains in the frame it lands, the period is latency + gap. That's **~4.6
frames** today (gap 1): **~6.5 ticks/s at 30 FPS**, ~13 at 60. If option A proves
safe (gap 0), it's **~3.6 frames**: ~8 ticks/s at 30 FPS, ~16 at 60. A frame-rate-
independent rate therefore has to be **at or below what the slowest supported
frame rate can deliver**, unless latency itself comes down (option E).

| | Option | Effect (ESTIMATED from §1) | Risk | Size |
|---|---|---|---|---|
| **A** | **Pump before checking `CanIssue`** (swap two calls in `Playground.Update`) | −1 frame per tick: 7.25 → ~6.25 at 33 FPS, about +16% ticks/s | **Must first prove the clipmap upload of the ops applied this frame reaches the GPU before this frame's tick is dispatched.** If it doesn't, the tick decides against stale terrain, which is the mass-duplication bug `MaxFramesInFlight` exists to prevent. The existing conservation ledger (the sand_column GPU-vs-CPU count) is the test. The gap may be what currently guarantees the ordering. | small; verification is the real work |
| **B** | **Adaptive drain**: apply a whole batch in one frame when it's small (e.g. ≤ 4096 ops), keeping the 1024 slicing for big bursts | drain 3.7 → 1 frame in this scenario: 7.25 → ~4.6 frames, **≈ 7 ticks/s at 33 FPS** | the 2026-09-11 siege measurement (320 k live voxels) found 4096 worse at p99 (43.8 vs 30.3 ms). A threshold by batch size keeps big-burst behaviour identical, but needs its own counterbalanced sustained arms (frame-time p99 **and** ticks/s) | small code, one A/B |
| **C** | **Fixed-rate scheduler**: tick at most every 1/R s (e.g. R = 5 Hz), and never more often than the pipeline allows | **fluid speed identical at 30 and 60 FPS**, which is the owner's actual goal. Throughput is then R, with no dependence on FPS. | picking R: R must be ≤ the worst-case capacity at the lowest supported FPS, or fluid slows there. R ≈ 4.5 Hz works with today's pipeline at 33 FPS; **B alone allows R ≈ 6.5 Hz at 30 FPS, and A+B ≈ 8 Hz** | small; stacks with A/B |
| **D** | Time-based apply budget (ms per frame instead of ops) | drain adapts to machine speed | the budget becomes non-deterministic across machines; p99 has to be re-measured | small |
| **E** | **Cut latency**: a GPU-resident fluid authority with a CPU mirror, or a speculative/double-buffered CA | could reach 1–2 frames per tick | **redesign**: it breaks the hard invariant "the CPU is the sole terrain writer" and §7.2's contract. Needs §0.3 review; not recommended | phase-sized |

**Recommended sequence, for the owner to approve or reject:** **C first**, because
it's the goal and needs no correctness argument, at R = 4.5 Hz (today's
worst case at 33 FPS is 4.6). Then **B** as a measured A/B. Then **A** only after
the conservation ledger proves the upload ordering. With B in place R can rise to
~6.5 Hz, and with A+B to ~8 Hz, at the same frame-rate independence (30 FPS
worst case).

## 4. Reproduce

```
SKIP_BUILD=1 ./run-fluidtick-diag.sh        # 3 arms x 3 min, takes the build lock
```
`-sustained` prints the `TICK PERIOD (decision 11)` block on every run, so any
future arm reports this decomposition for free.
