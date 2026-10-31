159 perf-dyn-floor-432 -- DONE, SHIPPED as the play default: FSR 3 dyn 432-540 rows at a 30 ms target; playwalk p50 -14.7 ms (ABBA, ON BATTERY, see caveat).

## What changed (branch engine-phase; commit: see `git log -1`, "159:")
- `UpscalerSelect.cs`: `UseDynRange(floor)` / `UseDynRangeFromArgs`; floor >= 540 = the old controller exactly (540-720, 16.7 ms, band 15-18);
  floor < 540 = floor-540 at `PlayDynTargetMs` 30 (band 27-33 ms). `DynTargetMs` is now a property. `Describe()` prints range + target.
- `Playground.Start`: PlaySettings (interactive) runs take `PlayDynFloor` 432; all other runs 540 (rigs/captures unchanged); `-dynfloor N` wins.
  Old play behaviour: `-dynfloor 540`. `tools/play-flags.sh` passes `-dynfloor 432` (playwalk follows play); pinned in `PlaySettingsTests`.
- F6 (`SettingsMenu.cs`): "Dynamic 432-540 (now N)", hint names the 30 ms target. `PlaySettings.cs`: comment only (no new field).
- `sustained.tsv`: 7th column `meanRows` (window mean internal rows). LookReview `-lookrowsab N`: walk_open still/walk/turn bursts at 540 and N.
- TASKS.md paragraph; PERF_30FPS_PLAN.md status line (step 3 DONE).

## Evidence
- Fast suite PASS 969 FAIL 0 SKIP 15 (`Logs/overnight/159_tests.out`); filtered PlaySettingsTests 15/15 (`Logs/overnight/159_filter.xml`).
- Measurement: warm-up `Logs/tg2world_20261031_105054.txt` (discarded), then MODE=playwalk ABBA, 5 min each, back to back:
  A old `-dynfloor 540` `Logs/tg2world_20261031_105557.txt`: p50 50.32  p95 77.05  >33 ms 97.7%  rows 540 100%
  B new                `Logs/tg2world_20261031_110242.txt`: p50 33.07  p95 61.21  >33 ms 50.3%  rows 432 81.2 / 468 12.9 / 504 4.6 / 540 1.3%
  B new                `Logs/tg2world_20261031_110933.txt`: p50 32.20  p95 75.27  >33 ms 47.1%  rows 432 88.5 / 468 8.5 / 504 2.5 / 540 0.6%
  A old                `Logs/tg2world_20261031_111632.txt`: p50 44.43  p95 82.09  >33 ms 83.1%  rows 540 100%
  Half-minute window means 0.5-4.5 min (position-matched, from each run's sustained.tsv): A 52.5 / 48.3 ms, B 36.4 / 37.9 ms -> -13.3 ms;
  B is lower in all 18 window pairs. Whole-run p50: A 47.4 vs B 32.6 mean -> -14.7 ms (plan est. walk 7-12).
- **CAVEAT: the Mac was unplugged at ~10:55, during the warm-up; all four arms ran ON BATTERY (56% -> 34%).** Battery numbers are not
  comparable to AC runs (task 08), so the absolute values (A at 44-50 ms vs 146's 44.7 on AC) are not the quotable level. The A/B is interleaved
  under the same condition and the gap (-13 to -15 ms) is 3x the 4 ms bar, so an AC re-run would not change the decision; I did not wait for AC.
  The next perf milestone should quote this on AC.
- The rows sit at 432 on 81-89% of walk frames: the walk is "heavy" almost everywhere. They climbed only on the lightest window
  (2.5 min: 29.7-31.1 ms, mean 451-479 rows). "Stays at 540 on light views" holds for views under ~27 ms, which this walk barely has.
- Motion check, one launch (`Logs/lookreview_20261031_104421.txt`, High preset, walk_open, bursts of 4 consecutive frames after 60 lead frames):
  flicker (max-min over 4 frames, mean 0-255) 540 vs 432: still 0.32 / 0.34, walk 18.52 / 18.26, turn 17.35 / 17.09. In the crops, 432 shows
  no ghost trails and no crawling edges; it is softer, as in task 156's stills. No new shimmer, so per the decision table it ships.
- 30 fps line still NOT met: p95 61-75 ms, 47-50% of frames > 33 ms (the stalls/spikes remain; steps 4-5 of the plan).

## Unfinished / next
- The AC re-quote belongs to the next perf milestone (a measure-only follow-up is not queued, per the rules).
- Controller: 37-41 row steps per 5 min (27-33 ms band, >= 30 frames apart). No hunting was seen in the windows; no tuning proposed.
- Owner-visible consequence: play now renders below the 2026-10-01 960x540 minimum (768x432) most of the time on this Mac (owner's Q100 (a) call).
  `-dynfloor 540` or F6 "540" restores the old look.

## For the doc
- Walk p50 (playwalk, ABBA, battery): 47.4 -> 32.6 ms (**-14.7 ms**); window means 50.4 -> 37.1 ms (-13.3); logs above.
- Frames > 33 ms: 83-98% -> 47-50%. p95: 77-82 -> 61-75 ms (30 fps line p95 <= 33 not yet met).
- Rows share on the walk (new): 432 81-89%, 468 9-13%, 504 3-5%, 540 0.6-1.3%.
- Motion flicker 540 vs 432 (walk_open): walk 18.5 / 18.3, turn 17.4 / 17.1, still 0.32 / 0.34: no new shimmer.
- `Screenshots/159/motion159.png`: rows still / walk / turn; columns 540 frame 3, 432 frame 3, 540 flicker, 432 flicker. Compare the two right
  columns: the flicker patterns are the same (it is real motion, not shimmer), and 432 shows no extra speckle on the far ridge.
- `Screenshots/159/rows156_crops.png`: 3x crops where 540 (left of each pair) and 432 differ most. Look at the grass/sand edge (#1) and the sand
  terraces (#2, #3): at 432 the edges are softer and the grass texture is blurrier. That is the cost of the gain.
