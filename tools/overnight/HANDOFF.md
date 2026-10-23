STOP_FOR_OWNER -- task 115 is a diagnosis; the fix is the next task. Q at the end (recommendation first).

# 115-perf-stutter-isolation: DONE. The walk's slow tail is slow VIEWS (GPU), not hitches. Spikes are only the p99 story.

## What changed (commits 7e25102 + this one)
- `-spikelog` (OFF by default; `FrameGapProbe.cs` + `PlaygroundCapture.cs` Sustained): a per-frame CSV of what ran. `Phase4Bootstrapper`
  now times `Shells.Update` (`FrameGapProbe.LastShellsMs`). `tools/spike-report.py`, `tools/spike-attribution.py`. TASKS.md: Task 115.
- No default, look or engine behaviour change. Fast suite `PASS 888 FAIL 0 SKIP 14` (`Logs/overnight/115-tests.out`).

## Evidence: warm steady state, AC power, noon, play settings (FSR 3 at 540 rows, High, shells ON), 3-min `MODE=walk` each
Warm-ups: `Logs/tg2world_20261022_183057.txt` (walks 1-2 back to back), `..._185033.txt` (arm, then walk 3). The 17:00 warm-up hit an
owner pause and then the Mac went on battery, so I threw it out and re-warmed. Logs: walk 1 `..._183613`, walk 2 `..._184103`,
arm `..._185531`, walk 3 `..._190024`. CSVs + reports: `Screenshots/115/` (`spike_report_all.txt`, `spike_attribution.txt`).
- Whole run (rig line) ON 1/2/3: p50 41.9 / 40.7 / 41.1 ms, p95 72.7 / 73.6 / 74.3, p99 90.0 / 101.6 / 105.8, > 33 ms 77.7 / 71.6 / 73.7% (= task 110).
- **Where the time goes:** a <= 50 ms frame is 34.5 ms; 28.3 ms of it is preUpdate (the GPU wait), our own CPU work ~6 ms.
- **Slow views:** 88-93% of frames sit in a stretch whose median (+-15 frames) is > 33 ms. Averaged over 5 frames, 90% are still > 33 ms
  and 20% are > 50 ms. Split the circle into 24 sectors and the median frame runs from 29 to 75 ms, the SAME way on both walks
  (r = 0.964; the slowest are sectors 14-15 at 57-75 ms). In the slow stretches FTM's (inflated) GPU time is 130-138 vs 101 on normal
  frames. So the view decides the frame time.
- **Spikes** (> 50 ms AND > local median + 15 ms): 525 / 672 / 683 per walk (13-16% of frames). They cost 7.8 / 11.2 / 10.7% of
  wall clock. With every spike set to its local median, > 33 ms only goes 74.1->73.1 / 67.1->65.6 / 69.1->68.5%, but p95 goes
  75->66 / 79->61 / 78->61 and p99 goes 96->78 / 106->76 / 106->73 ms.
- **Spike causes, ranked** (exclusive, first match; ON walks):
  1. Pacing pairs, 55-60%: a long frame followed by a short one, net cost ~0 (CPU blips while the GPU queue is full).
     28-33% of spikes are fluid-tick frames (a tick every 5 frames): +8.5 ms, then -8.8 ms next frame, net -0.7 / +0.1 ms.
  2. Main-thread streaming, 19-25% (1.7-3.2% of wall clock). These are real cost (net +4.5 / +6.9 ms per "streamer > 5 ms" frame).
     In streamer > 8 ms frames the drain is 6-7 ms and the slide 2.8 ms. About 90 chunk-crossing frames per walk each evict 250-275 chunks
     (max ~1,600): slide 5.5-8.5 ms, frame 58-64 ms. Worst single case: 142 ms evicting 1,157 chunks.
  3. Unexplained preUpdate waits with little own work, 16-22%. These are most of the > 100 ms frames (pre ~100 ms, own work 7-11 ms).
     A shell upload happened in 53 of 54 / 30 of 34 of those frames (vs ~50% of frames overall). Shell uploads scale with size:
     none -5 ms excess, > 300 KB +7.5 / +11.5 ms (net +4.0 / +5.7). This is a correlation, not proven.
  4. GC: 1-3% of spikes (the probe's own allocations add some).
- **Confirm arm: `-fluidhz 0.5`** (a tick every ~2 s instead of every 5 frames). Spikes 550 (ON 525-683), > 33 ms 74.7% (ON 71.6-77.7),
  p50 41.8, p99 89.9 (ON 90.0-105.8). **This rules out the fluid tick as the spike cause**: the pacing pairs just move to other frames.
  > 100 ms frames were 19 (ON 34-63): fewer, but from n = 1, so unconfirmed. Simulation note: this was a measurement arm only; fluid at
  0.5 Hz flows 13x slower and was never meant to ship.
## Proposed fix (ONE): spread the streamer's chunk-crossing eviction over frames
- What: when the camera crosses a chunk, `EvictOutOfRange` (in slide) evicts a whole ~256-chunk slab in one frame. Queue them and
  evict <= ~32 per frame, the same way task 26 already defers save-capped evictions. Internal only: no frozen API, no memory layout change.
- Estimated effect: removes ~90 hitches per 3 min of +15-20 ms (the 58-64 ms frames and the 142 ms one). p99 -3 to -8 ms (est.),
  > 33 ms about -0.3 points, p50 0. The completion drain (6-7 ms) is the bigger CPU share, but capping it delays chunk arrival while walking.
- Risk: low-medium. Slots free later (pool pressure), and stray chunks must stay outside the mirror box (task 21/26/32; tests exist). Look: none.
- **Plain message:** no spike fix gets near the 5% line. Even removing every spike leaves 66-73% of frames > 33 ms. That line needs
  GPU cuts (PERF_30FPS_PLAN rows 1-4 = queued tasks 116 sky leap, 117 Medium, 118 shadow clamp). Spike fixes buy p95/p99 and smoothness.
## Left open
- The > 100 ms preUpdate waits are not isolated. The suspects are the shell upload (a GPU write hazard?) and the fluid GPU burst. Isolating
  them needs an upload-batching arm, which is a new flag and so a task for the owner to approve.
## Owner question
Q71. Spike work: (a) eviction slicing as one task AFTER 116-118 (recommend); (b) do it now, before 116; (c) skip spikes, GPU only.

## For the doc
| walk (3 min, warm, AC; `spike_report_all.txt`, post-walk rows trimmed) | <= 33 ms | 33-50 | 50-100 | > 100 | p50 / p95 / p99 ms | spikes | spikes' share of wall clock |
|---|---|---|---|---|---|---|---|
| ON 1 | 25.9% | 42.5% | 30.7% | 0.8% | 42.6 / 75.1 / 95.5 | 525 | 7.8% |
| ON 2 | 32.9% | 36.2% | 29.5% | 1.5% | 41.0 / 79.2 / 106.2 | 672 | 11.2% |
| ON 3 | 30.9% | 38.8% | 29.0% | 1.3% | 41.1 / 77.6 / 106.2 | 683 | 10.7% |
| arm `-fluidhz 0.5` | 27.0% | 41.1% | 31.4% | 0.5% | 41.9 / 76.2 / 93.0 | 550 | 7.7% |
- Spikes removed (counterfactual): > 33 ms 74 -> 73% / 67 -> 66% / 69 -> 69%; p99 96 -> 78 / 106 -> 76 / 106 -> 73 ms.
- Same sectors slow on both walks (r = 0.964, medians 29-75 ms); GPU-bound (wait 28 of 34.5 ms, own CPU ~6 ms). No image: nothing visible changed.
