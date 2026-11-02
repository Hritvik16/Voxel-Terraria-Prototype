STOP_FOR_OWNER -- task 161 (resume, measurement only): NOT RUN AGAIN. The Mac was on BATTERY for the whole 2 h wait (2026-11-01 21:10-23:10,
95 % -> 77 %, "Now drawing from 'Battery Power'" at all 14 checks, `Logs/overnight/161_acwait2.txt`). The supervisor's 13:12 "AC Power" note no
longer held by 21:10: the charger was unplugged in between.
**Q1 (recommended (a)):** (a) plug the Mac in and LEAVE IT PLUGGED IN, then requeue 161 as-is (commands under "Next", ~1.5 h with warm-up).
(b) Turn `-diet2` ON in play now on the preliminary numbers (picture proven identical, 0 px), and take the arms at the next milestone.
With (b), task 163 still cannot pick D vs C1 (it needs `skyprobe_off` / `reuse*` / `sun_off`), so 163 would also need your pick.
**Note:** 163 `Requires` 161's warm AC arms and will STOP_FOR_OWNER on its own if it runs before 161 does. 164-166 do not depend on 161.

# 161-perf-kernel-diet-2: code + 0 px proof DONE earlier (`24424c2`, `cd565ee`); warm AC arm set NOT RUN (battery, twice); diet 2 stays behind `-diet2 1` (default OFF)

## What changed in this session
- Nothing in code. Only this HANDOFF. `Builds/Playground.app` (2026-10-31 21:08, built by task 162 from `24424c2` + 162's capture-only
  `f83ff32`; Playground.unity 21:06) still matches the sources, so the next try needs no rebuild (`SKIP_BUILD=1`).

## Evidence (carried over from the first 161 session, `tools/overnight/blocked/161-perf-kernel-diet-2.HANDOFF.md`)
- Fast suite `PASS 971 FAIL 0 SKIP 15` (`Logs/overnight/161_fast.out`).
- Exactness: lean vs twin raw no-upscaler stills **0 px** at walk_near, walk_open (8 pairs, controls 0 px; `Logs/costsplit_2026-10-31_171648.txt`,
  `python3 -I tools/diet2_161.py`) and cliff1 A native (`Logs/lookreview_20261031_184726.txt`).
- PRELIMINARY timing, NOT the result (AC but battery 7-8 % charging, no warm-up, 432 rows): FULL (diet 2 ON) vs `diet2_off`:
  walk_near 60.7 vs 68.4 ms p50 (-7.3, FULL spread 2.3), walk_open 22.2 vs 26.9 (-4.8, spread 0.1).
- PRELIMINARY GPU trace (release, unlabelled, `gpu-traces/walknear161_{on,off}.trace`): raymarch compute encoder 30.5 vs 62.0 ms/frame median,
  FSR 3 encoder ~6 ms, render/copies ~3-4 ms. Pre-pass vs main passes cannot be split in a release trace (no debug labels; Unity merges dispatches).

## Decision table -- still cannot be applied
- diet 2 >= 1 ms beyond noise -> lean ON in play: preliminary says yes (-7.3 / -4.8), unconfirmed warm on AC.
- `skyprobe_off` >= 2 ms -> D next, else C1: NOT MEASURED. `reuse1 ~ reuse16 ~ FULL` with `sun_off` large -> C2: NOT MEASURED.

## Next (on AC, back to back; re-check `pmset -g ps` before each step)
1. `MODE=stream MOTION=walk ./run-tg2world.sh` (warm-up, discarded).
2. `SKIP_BUILD=1 EXTRA="-renderrows 432 -diet2 1 -costarms diet2_off,skyprobe_off,sun_off,nearsh_off,fshadow_off,reuse1,reuse16,roof_on,roof_build,shells_off,ups_none,tiny_gate" ./run-cost-split.sh`
   with `Logs/overnight/161_trace_attach.sh <run dir> '^ +3 diet2_off' walknear161_full` beside it (its pattern wait is 500 s: start it once the
   run dir exists). Then apply the table; diet 2 >= 1 ms -> `KernelDiet2` default ON in play + fast suite once; state D or C1 next.

## For the doc
- Nothing new measured this session: the Mac stayed on battery for the full 2 h wait (`Logs/overnight/161_acwait2.txt`).
- Still standing: exactness 0 px (walk_near, walk_open, cliff1 A); preliminary walk_near 68.4 -> 60.7 ms p50, walk_open 26.9 -> 22.2
  (NOT warm, `Logs/costsplit_2026-10-31_171648.txt`).
- No images: nothing visible changed (0 px).
