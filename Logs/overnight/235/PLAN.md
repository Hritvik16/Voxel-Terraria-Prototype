# 235 plan -- worst-case poses in the budget check (written BEFORE any launch)

Owner 2026-11-17 (chat, "I want 1 for now"): judge performance on WORST-CASE poses, not noon.

## Launches (one warm-up, then four cost splits back to back; build = HEAD 06b84cf's player built 23:21 with
DEV_KERNELS=0 by task 236, no source newer -> SKIP_BUILD=1)
| launch | flags (plus play flags, High, 432 rows, shellwait 60) | timed path | arms | est. |
|---|---|---|---|---|
| walk   | -startsun 16 -costtimedpaths walk_near | walk_near (16 deg) | skyfill0 sun_off shells_off ceiling startsun_off (= noon control) | ~14 min |
| forest | -startsun 16 -costfixed forest_eye -costtimedpaths forest_eye | forest_eye (16 deg) | skyfill0 sun_off startsun_off (= noon control) | ~40 min |
| jungle | -startsun 0 -costfixed jungle_eye -costtimedpaths jungle_eye | jungle_eye (noon) | skyfill0 sun_off | ~29 min |
| night  | -costpaths torch | torch_camp (0.5 h, 28 torches) | tf_on tl_none skyfill0 sun_off | ~15 min |
Why this split: forest_eye and jungle_eye keep a streaming deficit (CPU valve / GPU pool full), so every run there waits
NearSettle's 180 s cap (~3.7 min a run, task 230: 60 min for 16 runs). Runs per pose = 1 + 3 x arms, so the deficit poses
get only the arms the table needs. Flood by day is NOT timed: since 229 the FULL run line says `flood skip` = the plain
kernel draws, step 0 by construction (if a day FULL line says `twin`, that is a PROBLEM row). Rest = FULL - sun - sky - flood
(everything else: shells, tier-0 march, FSR, CPU, overlap). Total ~1 h 45 min: OVER the ~40 min the task names; said so in HANDOFF.
Note: walk_near's arc depends on the sun azimuth, so walk_near at 16 deg is a different arc than the noon baseline's (its noon
control = the same arc at noon via startsun_off).

## What each result makes us do
- Worst pose FULL p50 > 33.3 ms (expected everywhere): report the distance and the top 3 steps there; no code change (task rule).
- A launch dirty (owner input / pause during a TIMED path): rerun it once; still dirty -> NOT CLEAN, rebaseline refused,
  HANDOFF says "rerun when idle" (not an owner question).
- Hard limit hit (peak > 3,000 MB, or GPU refusals at walk_near > 0): rebaseline refused -> STOP_FOR_OWNER with the numbers.
- Day FULL line shows the flood twin drawn: PROBLEM row (229's rule broke) -> flag in HANDOFF; no rebaseline.
- Otherwise: --rebaseline "owner 2026-11-17, task 235" writes the v2 (per-pose) baseline; the verdict vs the old v1
  baseline is meaningless (different poses) and is shown as NEW rows.
