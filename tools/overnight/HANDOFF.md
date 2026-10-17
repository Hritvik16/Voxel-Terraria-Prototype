# 97-fluid-option-b-adaptive-drain: DONE. Option B is in. The fixed fluid tick rose from 4.5 to 6.5 Hz with no shortfall at 30 or 20 FPS (arena pour).

**Owner, please note (frame cost, not blocking):** option B moves a whole op batch, up to 4,096 ops, onto ONE frame's main
thread. In a heavy pour (9,261 water voxels) at 20 FPS the apply's p99 went from 5.3 to 12.0 ms and its max from 5.9 to
21.9 ms. That spike lands about 6 times a second while the pour runs. In the light arena pour the p99 stays at 5.5-6.4 ms
(5.2-6.1 before). This is the risk that FLUID_TICK_DIAGNOSIS section 3 named. The apply timer measured it; no frame-time
arm was run (rule 10). If it shows in play, `-drainwhole 2048` (or 0) is the knob.

## What changed (commit `d536179`, branch engine-phase)
- `OpDrainCursor.BudgetFor(batchOps, perFrame, wholeBatchMax)`: a batch of <= wholeBatchMax ops gets the whole batch as its
  budget. A bigger batch keeps 1024/frame, with the same slices as before. The rule looks at the batch's total size.
- `FluidOpListReadback.WholeBatchMaxOps` is per instance and 0 (off) by default. Playground sets `WholeBatchMaxOpsDefault` = 4096
  (`-drainwhole N`, 0 = the old drain). The stand-alone rigs keep pure slicing. New counters: `WholeBatchesApplied`, and
  `ApplyMs*` (mean, 0.1 ms histogram p99, max).
- `FluidTickScheduler.DefaultRateHz` is now **6.5** (was 4.5), so lava runs at 1.08 Hz. Playground's fall table follows the period.
  `FluidFall.DefaultTickSeconds` stays 1/4.5 for the rigs and the CPU-twin tests (comment says why).
- Fluid probe: a `DRAIN (task 97)` line at every report point, giving the period split (latency + drain + gap), ops/batch, carried or whole, and apply ms.
  `-fluidbox N` sets the arena water box half-width (10 = heavy pour). `-stripnoshots` turns off captures and adds a +40 s point.
- Tests: `AdaptiveDrainTests` (6: a small batch drains in 1 frame, a big batch is sliced exactly as before, off = old, exactly-once order,
  the period model, B needed for R at 30 FPS). `FluidTickSchedulerTests`' fake pipeline went from 5 to 4 busy frames (5 frames could not carry R > 6).
- Docs: ENGINE_PHASE_PLAN row 5 note, KNOWN_ISSUES (fluid entry), CLAUDE.md index line.

## Evidence
- `SUITE=all`: **PASS 888 FAIL 0 SKIP 25** (`Logs/overnight/97-suite.out`). The filtered run while iterating was 19/19.
- Rate arms ran at warm steady state. Warm-up: `Logs/tg2world_20261017_073606.txt`. The arms ran back to back from 07:44 to 08:50 with
  no owner pause. The Mac went from AC to battery (95-100%) at about 07:56, outside my control; 91's arms also ran on battery.
  Full table: `Screenshots/97/rates.txt`.
  | arm (arena, 40 s, no shots) | FPS | ticks/s | SHORTFALL |
  |---|---|---|---|
  | before 4.5 Hz, cap 30 / cap 20 | 29.6 / 19.9 | 4.50 / 4.50 | 0 / 0 |
  | after 6.5 Hz + B, cap 30 / cap 20 (x2) | 29.5 / 19.8 | 6.50 / 6.49, 6.49 | 0 / 0, 0 |
  | 6.5 Hz with B OFF, cap 20 | 19.8 | 6.39 | 0.10/s |
  | heavy pour 4.5 -> 6.5+B, cap 20 | 19.8 | 4.17 -> 5.82 | 0.32 -> 0.70/s |
  | heavy pour 4.5 -> 6.5+B, cap 30 | 29.8 | 4.45 -> 6.17 | 0.05 -> 0.32/s |
- Why 6.5 and not more: readback latency now measures 2.0 frames in every arm (the 09-24 diagnosis had 3.6-3.8). In the arena,
  a tick takes 2 + 1 + 1 = 4 frames, of which 3 count towards the period (latency + drain - 1 + gap = 2 + 1 - 1 + 1), so the
  capacity at 19.8 FPS is ~6.6 ticks/s. R = 7 cannot be delivered there.
  At 6.0 Hz, one run lost 5 ticks during an FPS dip in its 20-40 s window (0 in 0-20 s), so I count that as noise, not a rate limit.
- Pipeline capacity (legacy, tick whenever CanIssue): heavy pour at cap 20 went from 3.85 to 5.29 ticks/s with B, at cap 30 from 6.35 to 8.29.
  Light pour at cap 20 went from 6.23 to 6.39: B only matters when a tick carries more than 1024 ops.
- Finding: each strip screenshot is a ~0.3-0.5 s frame and drops one due tick at 6.5 Hz (3 per launch). 91's 0.05/s at 20 FPS
  was probably the same artefact. That is why the rate arms use `-stripnoshots` and the strip launches are separate.
- Screenshots I looked at: `Screenshots/97/97-strip.png` (below) and the raw +4 s 6.5 Hz shot (`FarBand/2026-10-17_084434_fluidprobe_arena/strip_4s.png`):
  the arena floor at noon with the water mound and the slumping lava, nothing broken.

## Left / next
- Not done: a frame-time A/B of B (not asked; rule 10). Option A (gap 0, ~8 Hz) still needs the conservation-ledger proof first.
- Heavy pours keep falling short at 20 FPS (5.8 of 6.5). A cheaper apply (it costs ~2-5 us per op) would help both the shortfall and the spike.

## For the doc
- Fixed fluid rate went from **4.5 to 6.5 ticks/s** (+44%; lava 0.75 to 1.08 Hz). Arena pour: 6.50 ticks/s at 30 FPS and 6.49 at 20 FPS.
- SHORTFALL at 20 FPS: **0 over 40 s** in 2 runs, before and after (`Screenshots/97/rates.txt`, logs `Logs/tg2world_20261017_081418.txt`, `_084733.txt`).
- Heavy 9,261-voxel pour at 20 FPS: 4.17 -> 5.82 ticks/s (shortfall 0.70/s). Its apply spike p99 5.3 -> 12.0 ms, max 5.9 -> 21.9 ms.
- Option B alone, same 6.5 Hz at 20 FPS: B off 6.39 ticks/s (0.10/s short) -> B on 6.49 (0 short). Heavy-pour capacity at 20 FPS 3.85 -> 5.29.
- Image: `Screenshots/97/97-strip.png`. Rows: before (4.5 Hz, old drain, cap 30), after (6.5 Hz + B, cap 30), after (6.5 Hz + B, cap 20).
  Columns: +2/+4/+8 s after placing a water column (blue) and a lava block (orange) in the arena at noon. Look at the +4 s column:
  the after rows show a lower, wider water mound than before's cone, and at +8 s a wider puddle, so water moves faster. Rows 2 and 3
  match each other, so the speed still does not depend on the frame rate.
