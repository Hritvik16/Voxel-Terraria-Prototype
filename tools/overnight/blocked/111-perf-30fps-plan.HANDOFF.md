STOP_FOR_OWNER -- task 111 (30 fps plan): `PERF_30FPS_PLAN.md` is written. Please pick the cuts below (recommendation first).

- **Q1 Internal rows.** (a) **FSR 3 dyn 432-540, target ~30 ms** (rows drop only on heavy views; stills at 432 vs 540 shown first);
  (b) dyn 360-540 (most headroom, softest); (c) keep the 540 minimum: 30 fps then needs shells OFF or sun shadows OFF.
- **Q2 Quality preset on the M1.** (a) **Medium**: shells sized for the 540 rows play actually renders; far band past 3.3 km (was 6.6);
  drawn 0.1 m area 65 m (simulation stays 128 m, 8.15); -171 MB; (b) keep High; (c) Low (only with Q1 (b)).
- **Q3 Shells.** (a) **keep ON and cut their march** (exact column sky leap, then Medium); (b) shells OFF in play (-13 ms measured, same
  look at noon, loses 3D range past the window; reverses Q63 (a)).
- **Q4 Sun shadows.** (a) **128 m clamp, then 1-in-8 temporal reuse, one task each, judged on golden-hour stills**; (b) half-res now
  (-0.5-1.3 ms, blockier edges); (c) no shadow cut.
- **Q5 Upscaler.** (a) **keep FSR 3; STP (-3 ms) is the fallback**; (b) STP now; (c) FSR 1 now (-5 ms, shimmers).
- **Q6 Order.** (a) **as in the plan §3** (exact cut, preset, rows, shadows, spikes); (b) rows first.
- **Q7 Shell fill while walking.** (a) **park until 30 fps is met** (filled shells add cost back); (b) now.
- Going ahead with (a), reversible: **Q8** summary build to a worker job (108's spike); **Q9** measurement fixes (`-renderrows` honoured
  by the cost split, p95 in the walk report, a golden-hour walk arm).

# 111-perf-30fps-plan: DONE (planning only; STOP_FOR_OWNER for the cuts)

## What changed
- New `PERF_30FPS_PLAN.md`: the gap, a ranked table of 10 candidate cuts, a one-change-per-task order with a running sum, approvals, questions.
  No product code, no tests, no launches (planning task). Commit: see `git log -1` (this file + the plan only).

## What the plan says (all from existing logs; est. = arithmetic, not an arm)
- Gap: walking p50 41.0 -> 30 ms (-11), frames > 33 ms 72-78% -> 5% (needs p95 <= 33; p99 is 99 ms); still: walk_near 60.7 -> 30 (-30.7),
  walk_open 43.0 -> 30 (-13.0). Source: `Logs/costsplit_2026-10-22_{122517,130301}.txt`, `Logs/tg2world_20261022_{133936,134432}.txt`.
- New findings while reading code/logs: (1) FSR 3 "dyn" never leaves 540 rows today (steps down above 18 ms smoothed; floor 540), so it
  holds nothing at these frame times. (2) Play runs preset High (shells sized for 720 rows) while rendering 540: march spent on detail below
  1.5 px. (3) On the walk, shells 3-7 hold nothing, so the moving number understates the cost once the shells fill. (4) 110 ran at noon only.
- Honest sum: walk p50 <= 30 reachable at step 3 (est. ~28 ms) only with rows below 540. The 5% line is NOT reached with confidence:
  cliff-facing views est. ~37 ms after steps 1-4, ~30-34 only with a 360-405 row floor, plus spike fixes of unknown size.

## Evidence looked at (images not committed)
- `Screenshots/93b/sheet_walk_open.png` -- top-left (shells OFF) vs top-right (shells ON): ridge, orange mountain, snow identical.
- `Screenshots/93b/sheet_walk_near.png` -- any panel: sky only in the top-right strip (~6% px), which carries most of the 12.8 ms shell march.
- `Screenshots/110/walk_open_full.png` vs `Screenshots/93b3/split/walk_open_fshadow_off.png` -- far mountains at right: far shadows ON/OFF
  identical at noon.
- `Screenshots/110/walk_near_full.png` -- cliff fills ~90% of the frame, HUD 27 fps: the view the 5% line is about.

## Next step
The owner answers Q1-Q7. Then the supervisor queues step 1 (shell column sky leap = `later/102` made concrete) with the approval in its task file.
Each step: warm-up + one cost-split launch + walk x4 ABBA, then stop.

## For the doc
- Gap: walking p50 41.0 ms -> 30 (-11 ms); frames > 33 ms 72-78% -> <= 5%; still: walk_near 60.7 -> 30 ms, walk_open 43.0 -> 30 ms.
- Top 6 of the ranked table (ms near / open; **est.** unless measured):
  1. Shell column sky leap (exact, 0 px): est. 3-8 / 1-2.
  2. Quality preset Medium (shells sized for the 540 rows rendered): est. 2-4 / 4-6, -171 MB; far band past 3.3 km.
  3. Rows below 540 (FSR 3 dyn floor 432, target 30 ms): est. 11-17 / 8-12 (405: 13-21 / 9-14; 360: 17-27 / 12-18). Softer picture.
  4. Shells OFF in play: measured 13.4 / 13.6; same look at noon poses; reverses Q63 (a).
  5. Sun shadows cheaper (128 m clamp / 1-in-8 reuse): est. 2-4 / 0-1 and 3-6 / 1-2 (sun OFF measured 15.7 / 6.0; half-res 0.5-1.3).
  6. Cheaper upscaler: STP measured -2.7 / -3.1, FSR 1 -4.9 / -5.4 (task 51); FSR 3 pass 5-6 (110).
  far_off (15-18) overlaps the shell march and is not a separate cut.
- Images: `Screenshots/93b/sheet_walk_open.png` (top row, OFF vs ON: shells cost no look at this pose);
  `Screenshots/93b/sheet_walk_near.png` (top-right sky strip, ~6% of pixels, carries the shell march cost).
