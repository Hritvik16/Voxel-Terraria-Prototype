STOP_FOR_OWNER -- 93b third run (after 99) NOT MEASURED: the Mac has been on BATTERY since 10:34 and never went back to AC.
Question: please plug the Mac in, then re-queue 93b unchanged (`tools/overnight/blocked/93b-*` back to `queue/`).
Nothing needs deciding. The driver is ready, and the player is already built with task 99's final code.
If the Mac must stay on battery: say so, and the run goes ahead on battery with that noted.
Not recommended: task 08 found cost arms on battery were wrong.

# 93b-shells-perf-breakdown (third run, Q61 (a)): BLOCKED on power. No arm ran; no numbers.

## What happened
- 10:43: the task started. `pmset -g batt`: "Battery Power", 43%, discharging.
  - `pmset -g log` shows AC -> battery at 10:34:55, which is after task 99 finished its measurement (10:24, on AC).
  - The task file says AC only for every launch.
- The player build was STALE.
  - `Builds/Playground.app` was built at 10:09.
  - Task 99 edited `Raymarch.compute`, `RaymarchFeature.cs` and `PlaygroundCapture.CostSplit.cs` at 10:32, before its commit `2b63964`.
  - So I rebuilt it: `BUILD_ONLY=1 ./run-cost-split.sh`, "build ok" at 11:01, no shader or CS errors. This was a build only, not a measurement, so battery did not matter.
  - The next run can use `SKIP_BUILD=1`, as the driver does.
- Driver: `Screenshots/93b/rerun2/93b-rerun2-driver.sh` (copy of `Logs/overnight/93b-rerun2-driver.sh`).
  - It runs the same arms as the 17 Oct re-run: warm-up, then cost-split OFF ON ON OFF, then walk OFF ON ON OFF. Sky exit is ON (99's default) and the mask is OFF.
  - It waits for AC before the warm-up, and it aborts with "POWER LOST" if AC is gone before any later arm.
- It waited from 11:01 to 13:00 and never saw AC (battery 39% -> 33%). I killed it.
  - It was still in its wait loop, so it had taken no Unity lock and launched no player.
  - `Logs/overnight/93b-rerun2.out` is empty.
- No product code changed. No tests were run, because no code changed.

## Next step
- On AC: `nohup Logs/overnight/93b-rerun2-driver.sh > Logs/overnight/93b-rerun2.out 2>&1 &` (about 75 min). Then:
  - Write `Screenshots/93b/rerun2/perf93b_rerun2.txt`, with the columns "first run" -> "re-run 17 Oct" -> "now".
  - Make the sheets from the cost-split stills (`<data>/UpscalerClips/<stamp>/stills/walk_*_{full,shells_off}_clean.png`, `walk_*_shelltint.png`).
  - Apply the task's three rules for 93c.
- What to expect, not a result: task 99 saved 4.7 / 2.6 ms p50 at walk_near / walk_open.
  - The 17 Oct gap was +12.7 / +13.9 ms. Taking 99's saving off that would still leave about +8 / +11 ms, which is rule 3 (93c held).
  - Only the run can say this. No number here is measured.

## For the doc
Task 93b (third run): not measured. The Mac was on battery for the whole session (10:34 -> 13:00, 43% -> 33%), and the task needs AC.
- Numbers: none. The last measured gap is still the 17 Oct re-run's: shells ON +12.7 / +13.9 ms p50 steady (`Screenshots/93b/rerun/perf93b_rerun.txt`).
- Done: the player was rebuilt with task 99's final code (11:01). The driver is ready (`Screenshots/93b/rerun2/93b-rerun2-driver.sh`).
- Images: none. Nothing was launched.
