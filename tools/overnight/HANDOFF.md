# 168d-perf-e-streamer-timeslice: DONE, flag stays OFF (decision row 2: gain within noise). The budget is exact at rest (test), but in play the admission drain is not the tail.

**Main finding (it changes 168b's conclusion):** 168b took its spike log from the CEILING walk. That walk passes `-shells 0`, and with shells off,
cascade tiers 1-2 get allocated (task 93d). Every admission then pays `SubmitPrecomputed`, about 1.05 ms per chunk, and that cost was the
"72 % main-thread streaming". In play (shells ON) an admission costs about 0.1-0.25 ms (16 admits = 1.8 ms median). Removing the WHOLE drain
would cut the share of frames > 33 ms by only about 3 points; removing ALL of Update would cut it by 5-6 points. 57 % of the > 33 ms frames
have no CPU section >= 5 ms (the GPU / preUpdate wait). In play the tail is mostly GPU + fluid-tick pacing, not streamer CPU.

## Design (step 1-2)
- Spike ranking, 168b's ceiling walk (`Logs/tg2world_20261102_102802.txt`, shells OFF): the largest phase >= 5 ms on a > 33 ms frame is drain 60.7 %,
  none (GPU/pre) 19.6 %, fluid tick issued 9.8 %, streamer mixed 6.9 %, upload 1.4 %, slide/evict 0.9 %, sweep 0.6 %. Drain cost: ~1.05 ms per admitted chunk.
- Ranking in play (today's A arms, shells ON): none (GPU/pre) 57 %, fluid tick issued 24-27 %, drain 7.5-8.1 %, streamer mixed 7-8.5 %, slide 0.7 %.
  `spike-attribution.py`: fluid tick pacing 30-34 %, other pacing 27-29 %, unexplained preUpdate wait 20-22 %, main-thread streaming 13-21 %, GC 2 %.
- Built: `-admitbudget <ms>` (`StreamManager.AdmitBudgetMs`, default 0 = OFF). `Update`'s `DrainCompletions` stops once the budget is spent, after at
  least 1 admission. The check runs before the dequeue, so the FIFO order is unchanged. Startup / `WaitForIdle` drains are unbudgeted.
  The queue is bounded by the scratch pool (2 x workers). Everything stays on the main thread: no locks, no state-machine or API change.
  Evict was already sliced (task 123, `-evictslice`). It is 0.7-0.9 % of the > 33 ms frames, so I left it alone.
- New `ADMIT (task 168d)` line in every sustained/playwalk report: admitted/s, completion->admit wait mean/p50/p95/max, budget stops, deficit.

## Evidence (warm steady state, AC, 432 rows pinned, play flags, `-spikelog` in both arms; script `Logs/overnight/168d_measure.sh`)
Warm-up (discarded) `Logs/tg2world_20261102_152217.txt`, then ABBA playwalks, 3 min each:

| arm | log | p50 | p95 | > 33 ms | admitted/s | wait mean / p95 | budget stops |
|---|---|---|---|---|---|---|---|
| A1 OFF | `_153238` | 31.0 | 58.1 | 44.3 % | 154 | 20.6 / 49 ms | 0 |
| B1 4 ms | `_153721` | 29.2 | 55.0 | 36.9 % | 98 | 22.1 / 52 ms | 224 |
| B2 4 ms | `_154216` | 30.7 | 57.0 | 43.4 % | 115 | 22.5 / 53 ms | 301 |
| A2 OFF | `_154709` | 30.2 | 55.4 | 39.9 % | 106 | 22.1 / 50 ms | 0 |

- A mean 42.1 % / p95 56.7 -> B 40.1 % / 56.0. That is -2 points, smaller than either arm's own spread (A 4.4, B 6.5 points). Row 2: keep behind the flag.
- Time-to-visible: completion->admit wait +1 ms mean, +3 ms p95. Admitted/s swings 98-154 within each arm too (A2 106), so it is not the budget.
- **Caveat: the Mac was busy.** Firefox (~90 % CPU), Preview, and ReportCrashService / ReportMemoryException (60-85 % CPU) ran all the way through,
  and the battery was charging 56 -> 69 %. Every arm is about 7 ms slower than 168b's FULL walks (p50 ~30 vs 23.9). A vs B is still fair (ABBA,
  same conditions); absolute numbers are not comparable to 168b. No "owner paused" note.
- Exactness: `AdmitBudgetTests` PASS. A ~0 ms budget admits 1 chunk/frame, with 21 budget stops. Driven to rest with Update only, the 25 chunks are
  identical to the unbudgeted run (resident set, strided voxels, dense/uniform layout). Filter run `Logs/overnight/168d_filter.out` PASS 6.
  Fast suite PASS 991 FAIL 0 SKIP 16 (`Logs/overnight/168d_fast.out`).
- Still looked at: `~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Sustained/2026-11-02_153904_tg2playwalk_20261102_153721/99_end_pose.png`.
  Noon mountain slope on the left, a green plateau and the sea behind it. Normal; no holes, nothing floating.

## What changed (`4e40a8e`)
`StreamManager.cs` (budget, admit-wait stats, `doneTs` on each completion), `Phase4Bootstrapper.cs` (`-admitbudget`), `PlaygroundCapture.cs` (ADMIT line),
`AdmitBudgetTests.cs`, TASKS.md paragraph.

## Next (supervisor / owner)
- The <= 5 % > 33 ms target is not reached (40-42 % today on a busy Mac; 168b had 17-19 % on a quiet one). Streamer CPU is not the lever in play.
- The next biggest identifiable source in play is the fluid tick frame (24-27 % of > 33 ms frames; 30-34 % of spikes as "fluid tick issued, next frame short").
  A fluid main-thread time-slice would need its own approved task. Ask the owner first, because the attribution says it is partly pacing (net ~0).
- The rest is GPU-bound frames (57 %). That is shading / view cost, which 168b's cost split covers (sky probe + sun at walk_near).
- Not queued (measure-only): re-run 168b's spike attribution on a FULL (shells ON) walk on a quiet Mac, to replace the ceiling-walk numbers in the doc.

## For the doc
- Spike sources in play (shells ON), > 33 ms frames by their largest section: GPU/preUpdate wait 57 %, fluid tick 24-27 %, admission drain 7.5-8 %,
  streamer mixed 7-8.5 %, evict 0.7 %. 168b's "72 % streaming" was measured with shells OFF, where an admission costs 1.05 ms instead of ~0.15 ms.
- > 33 ms share A (today) 44.3 / 39.9 % -> B (4 ms admission budget) 36.9 / 43.4 %; p95 58.1 / 55.4 -> 55.0 / 57.0 ms. Within noise (`Logs/tg2world_20261102_1532..1547*.txt`).
- Time-to-visible cost: +1 ms mean, +3 ms p95 completion->admit wait. <= 5 % NOT reached; flag `-admitbudget` stays OFF.
- Image: `.../Sustained/2026-11-02_153904_tg2playwalk_20261102_153721/99_end_pose.png`: the walk's end pose with the budget ON. Mountain slope on the left,
  green plateau and sea at the right. It looks normal, so the budget changes nothing visible.
