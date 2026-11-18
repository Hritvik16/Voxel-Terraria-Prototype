STOP_FOR_OWNER -- the budget check works, but no baseline can be accepted yet. Two reasons:
(1) The NIGHT launch peaks at **~6.0 GB** of process memory (6,025 and 6,055 MB; task 229's night 6,037). The hard limit is 3,000 MB, and this was not reported before. The day launch stays at 2,568 MB. INFERRED cause: the `tf_on` arm's synchronous `TorchFloodPool.Rebuild` (28 torches). 233's night ran on the same build without `tf_on` and peaked at 2,538 MB. Play uses the incremental `Tick`, so it is NOT known whether play spikes too.
**Question:** (a) queue a bug task to find the +3.5 GB transient in Rebuild/Flush, and check whether play's Tick does it too (recommended; every check fails until then), or (b) accept the night peak as capture-only and count only the day peak for the hard limit.
(2) The run was NOT CLEAN: the owner used the Mac (19:33-20:26, then from 21:19) and paused at 22:04. So `--rebaseline` was refused, as designed. Rerun the check when the Mac is idle, after (1) is decided.

# 230-perf-budget-check-script: script written and failure path proven. First real run NOT CLEAN; night peak 6.0 GB over the hard limit.

## What changed (no Assets code, no API/layout change)
- `tools/overnight/run-budget-check.sh` (new). Holds the lock. Steps: build unless SKIP_BUILD=1 → warm-up → DAY cost split (walk_near, arms from `PERF_BUDGET.json` steps) → FOREST (FULL only, secondary) → NIGHT (`-costpaths torch`, arms tf_on and tl_none; the torch scene's FULL is the OLD point lights). Input watch: a dirty DAY or NIGHT launch reruns once, and still dirty = NOT CLEAN; input during forest_eye is reported only. It prints ONE table and `BUDGET PASS|WARN|FAIL|NOT CLEAN` (exit 0/0/1/3), and takes `--rebaseline "<who>"` and `--compare DAY NIGHT [FOREST]`.
- `tools/overnight/budget_compare.py` (the comparison) and `tools/overnight/hididle.c` (212's input probe).
- `run-cost-split.sh`: `DEV_KERNELS=0` (play's player) and `BUILD_ARGS` hooks. The defaults are unchanged.
- `PERF_BUDGET_GUARD.md`: the owner's pick (b), N = 5, an "as built" section and the open 6 GB note. `PERF_BUDGET.json`: the forest_eye pose is pinned, plus the launch flags. The baseline is STILL the provisional one.
- `tools/overnight/BUDGET_COUNT` = 7 (225-229, 232, 233; 233 HEAVY). PREAMBLE rule 13 (the supervisor's counter and check rule). TASKS.md paragraph.
- Fixed after the run (my bug): the script's EXIT trap replaced unity-lock's release trap, which left a stale lock (I removed it; it was mine, dead pid).

## Evidence (warm: warm-up `Logs/tg2world_20261117_192617.txt`; run `Logs/budget/2026-11-17_192530/`)
- Table: `Logs/budget/2026-11-17_192530/check.txt` (vs the provisional baseline). Clean part = the day rerun's walk_near (timed about 20:46-20:56; no input from 20:26 until 21:19:43, which fell in forest_eye). p50 ms:
  walk_near FULL 67.4 (p95 117.1, 88.8 % > 33 ms, FULL range 5.5) | ceiling 22.9
  steps: sky fill 11.5 (share 2.5) | sun 20.9 (share 5.0) | flood day -0.2 (229's skip works) | shells 6.0 (share 4.0)
  night torch_camp (first launch, input only): tf_on 59.0, tl_none 49.6, so the flood step is +9.4 ms (share 1.0). The rerun (paused) is unusable: FULL range 21.6.
  forest_eye FULL 99.9 / 103.7 (both launches had input during it; secondary). Memory: day peak 2,568 MB, GPU refusals 0 at every day path, GPU pool peak 245,704, CPU pool peak 494,879.
- Failure path: a copy with this run as its baseline (hard memory limit lifted to 7,000 to isolate the ratchet rows) gives `BUDGET PASS` exit 0. The same copy x 0.8 gives `BUDGET FAIL` exit 1 (walk p50 +13.5 > 5.0, p95, sun step +4.2 > 3.0, night play, memory) (`Logs/overnight/230/failtest_same.txt`, `failtest_strict.txt`).
- Time: 165 min with both reruns. The day launch took 73 min: 13 min walk_near and 60 min timing all 5 arms at forest_eye at about 100 ms frames. After the run I moved forest_eye to a FULL-only launch: estimated total about 35-40 min per clean check. This split is NOT yet run for real (owner pause), but the compare side is tested on these logs.
- EditMode suite NOT run: nothing under Assets changed (shell/python/md only), and the owner's pause holds the Unity lock queue.

## Left / next
- Owner: the 6 GB question above. Then a clean `run-budget-check.sh --rebaseline "owner Q-AO b"` (SKIP_BUILD=1 is fine until code changes; the build at 16:56 = HEAD with `-startsun 0` pinned).
- Note: 233's walk_near FULL of about 35 ms is not comparable with this check's 67 ms (233 used -shellwait 30 and 12 s timing, and these numbers repeat 229's 60-88 ms at shellwait 60).
- Not mine, not committed: `Assets/Scenes/Playground.unity`, `Logs/TerrainGen/*`, the 230 queue file's edited Requires line.

## For the doc
- The guard exists: `tools/overnight/run-budget-check.sh` prints one table and BUDGET PASS / WARN / FAIL / NOT CLEAN. Proven: same baseline → PASS (exit 0), baseline 20 % stricter → FAIL (exit 1).
- First real run (`Logs/budget/2026-11-17_192530/check.txt`): walk_near at play settings 67.4 ms p50 (target 33.3), ceiling 22.9 ms. Lighting steps: sky fill 11.5 ms (share 2.5), sun 20.9 ms (share 5.0), shells 6.0 (share 4.0), torch flood by day -0.2 ms (≈0 since 229), torch flood at night +9.4 ms (share 1.0).
- Memory: day 2,568 MB, but the night torch scene peaks at 6,055 MB, over the 3 GB limit. New finding, owner question.
- Not accepted as the baseline: the owner used the Mac during the run (NOT CLEAN). Rerun when idle.
- No images: a tooling task. The cost split's stills are in `~/Library/Application Support/.../UpscalerClips/2026-11-17_204403/stills/` if wanted.
