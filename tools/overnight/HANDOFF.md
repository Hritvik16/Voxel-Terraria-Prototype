# 91-engine-17-fixed-rate-fluid-tick: DONE. Fluid CA ticks at a fixed 4.5 Hz at any frame rate (option C); decision 11 superseded.

**Owner, please note (gameplay consequence, not blocking):** in light scenes the old tick ran FASTER than 4.5 Hz
(9.5 ticks/s at 29 FPS, 6.35 at 19 FPS; arena pour), so at 30 FPS **water now falls and spreads about half as fast
as before** (strip rows 1 vs 3). R = 4.5 is what the task specified (the diagnosis's heavy-scene worst case). Raising
it is one flag (`-fluidhz R`) / one constant; above ~4.5 the low-FPS shortfall grows unless option B lands first.

## What changed (commit `73024a8`, branch engine-phase)
- New `Assets/CoreEngine/Simulation/FluidTickScheduler.cs`: due every 1/R s (phase-locked to a deadline, so no rate
  is lost to frame quantisation), issued only when `CanIssue`; at most one banked tick, missed periods are dropped and
  counted (`MissedTicks` = SHORTFALL), never replayed as a burst. `DefaultRateHz = 4.5`. `MaxFramesInFlight` untouched;
  options A/B/D/E untouched. Lava keeps `MaterialRules.TickInterval` = every 6th tick -> 0.75 Hz.
- `Playground.cs`: the tick in `Update` goes through the scheduler (play, tg2 and every capture mode); `-fluidhz R`
  sets R, `-fluidhz 0` = the old behaviour (comparison arm). `DebugTickFluid` and the stand-alone rigs
  (FluidTiledRig, Phase 4/6 rigs, ...) still tick on `CanIssue` alone -- unchanged, so their histories stay comparable.
- Fluid probe: a `FLUID RATE` line at every report point (ticks/s since placement, R, lava rate, SHORTFALL);
  new `-fpscap N` and `-fluidstrip` (report/shots at wall-clock 1/2/4/8/12/20 s). `-sustained` prints the scheduler line.
- Decision 11 marked SUPERSEDED: `ENGINE_PHASE_PLAN.md` (note under amendment row 5), `KNOWN_ISSUES.md`, CLAUDE.md index.
- `FluidTickSchedulerTests` (5): 30 vs 60 FPS fake clocks issue the same ticks (+-1 over 60 s), never > R*T+1 or a
  1 s burst, never while canIssue is false; jitter; 15 FPS / 3 s hitch shortfall counted; legacy arm.
- Queued follow-up `93-bug-fluidprobe-tg2-water-frozen.md` (see below).

## Evidence
- `SUITE=all`: **PASS 778 FAIL 0 SKIP 23** (`Logs/overnight/91-suite.out`); FluidAtRestTests 5/5, FluidTickSchedulerTests 5/5.
- `run-fluid-tiled.sh`: **PASS 19 FAIL 0**, incl. "active set ... under 544 MB" (2026-09-11 rebase) (`Logs/overnight/91-fluidtiled.out`).
- `MODE=poolprobe` (tg2, lake + coast): **refusals 0** over 246 residency passes, pool peak 32/480, water box drains
  (coast 1029 -> 9 by +480 frames) (`Logs/tg2world_20261015_115238.txt`). Peak footprint 2,395 MB.
- Ticks/s, 20 s after placement (summary `Screenshots/91/91-rates.txt`; the machine reached ~30 FPS max, so `-fpscap 60`
  gave 31.8 FPS -- the comparison is caps 30 vs 20):
  | arm | FPS | ticks/s | shortfall |
  |---|---|---|---|
  | fixed, cap 30 (arena) | 29.1 | **4.50** | 0 |
  | fixed, cap 20 (arena) | 19.3 | **4.44** | 1 tick in 20 s (0.05/s) |
  | legacy, cap 30 (arena) | 29.1 | 9.50 | -- |
  | legacy, cap 20 (arena) | 19.3 | 6.35 | -- |
  | fixed, cap 30 (tg2 fluidprobe) | 29.2 | 4.49 | 0 |
- Same front at the same time: water in its box at +2/+4 s = 341/134 (cap 30) vs 323/127 (cap 20); legacy 140/0 vs 251/22.
- Not a performance task: no frame-time numbers quoted (rule 10).

## Found, NOT caused by this task
`MODE=fluidprobe` in **tg2** (eye_level_1 site): the placed water never moves (ops total 0, `promote.seen=0`, lava
`no_destination=784`) -- **identically with `-fluidhz 0`** (`Logs/tg2world_20261015_112947.txt` vs `_113531.txt`).
It moved on 2026-10-03 (task 09). The arena and the tg2 lake/coast (poolprobe) move fine. Queued as task 93.

## Next step
Owner: keep R = 4.5 or pick another (see the note at the top). Then option B (adaptive drain, a measured A/B) would
let R rise to ~6.5 Hz with the same 30 FPS independence; A only after the conservation ledger proves upload ordering.
Note: Playground's vents still emit per FRAME (`Emit`), so a vent's pour rate still follows FPS (out of scope here).

## For the doc
- Fluid ticks/s at 30 FPS: 9.50 -> **4.50**; at 20 FPS: 6.35 -> **4.44** (was 1.50x apart, now 1.01x) -- arena pour,
  20 s windows, `Screenshots/91/91-rates.txt` (logs `Logs/tg2world_20261015_1138*/1143*/1146*/1148*.txt`).
- Lava: every 6th CA tick -> **0.75 Hz** at any frame rate (was 1.6 Hz at 29 FPS, 1.06 at 19 FPS).
- Shortfall line: fixed at 19 FPS `SHORTFALL missed 1 = 0.05/s`; at 29 FPS 0.
- Rigs: SUITE=all 778/0/23; run-fluid-tiled 19/0; poolprobe coast refusals 0; FluidAtRestTests 5/5.
- Image: `Screenshots/91/91-strip.png` -- rows = fixed cap 30, fixed cap 20, legacy cap 30, legacy cap 20; columns
  = +2/+4/+8 s after placing a water column (blue) and a lava block (orange) in the arena at noon. Look at rows 1-2:
  the same water shape in every column (column at 2 s, landing at 4 s, puddle at 8 s); rows 3-4 differ from each
  other (legacy at 30 FPS is already a puddle at 4 s, at 20 FPS still a column).
