# 168b-perf-30fps-ceiling-check: DONE. The average is reachable, but the tail is not reachable by shading cuts. Decision-table row 2: spikes are the problem -> owner question below.

**Answer:** walk p50 <= 30 is already met at 432 rows on AC (FULL walk 23.7 ms). <= 5 % of frames > 33 ms is NOT reachable by any shading lever.
Even with the sky probe, sun and shells all OFF, the moving walk keeps 12 % of frames > 33 ms (p95 42). The moving tail comes from main-thread streaming
(and the fluid tick for the > 100 ms frames), not from GPU shading. Static views meet the tail line only with sky probe + sun both off, and that changes the picture.

## Owner question (decision table, row 2: ceiling p50 <= 30 but walk p95 > 33)
(a) Spike work: time-slice the streamer's main-thread work + upload pacing (cut E, widened: the data points at streamer CPU time, not only `SetData` syncs).
(b) Lower the target to p95 <= 45. The FULL walk is already at 45.1 (borderline).
Recommendation: (a), but judge it on the walk's > 33 % share, not p50.

## What changed (`e685b77`, capture-only, no change to play)
- `PlaygroundCapture.CostSplit.cs`: stacked arms `skysun_off`, `sunshells_off`, `ceiling` (FSR 3 kept). Every run line and table row now prints p95 and the share of frames > 33 ms.
- `PlaygroundCapture.cs` `Sustained()`: honours `-noskyprobe` and `-nosim`; the header prints `sky probe ON|OFF`. TASKS.md paragraph added.

## Evidence (warm steady state, AC throughout, 432 rows pinned, tg2 noon, play flags)
Warm-up `Logs/tg2world_20261102_094324.txt` (discarded). Then back to back: walk FULL, walk CEIL, cost split, walk CEIL, walk FULL (ABBA; script `Logs/overnight/168b_measure.sh`).
Cost split `Logs/costsplit_2026-11-02_095908.txt`: 7 FULL runs per view, range 1.2 / 1.6 ms. Walks (MODE=playwalk, 3 min): `Logs/tg2world_20261102_094834/_095315/_101810/_102253.txt`.

| arm | walk_near p50 / p95 / %>33 | walk_open | the walk (moving, streaming live) |
|---|---|---|---|
| FULL | 37.2 / 65.5 / 60.4 % | 20.8 / 30.3 / 1.2 % | 23.9, 23.4 / 45.2, 44.9 / 19.0, 17.3 % |
| skyprobe_off | 31.1 / 55.5 / 45.3 % | 19.3 / 31.3 / 4.4 % | -- |
| sun_off | 24.2 / 41.7 / 19.1 % | 18.2 / 24.5 / 0.0 % | -- |
| shells_off | 29.9 / 58.4 / 38.7 % | 17.8 / 26.7 / 0.9 % | -- |
| skysun_off | 20.5 / 31.4 / 1.4 % | 17.5 / 22.0 / 0.2 % | -- |
| sunshells_off | 22.9 / 36.2 / 11.1 % | 16.3 / 24.9 / 0.1 % | -- |
| ceiling (sky+sun+shells) | 17.7 / 29.6 / 0.3 % | 13.8 / 23.1 / 0.1 % | 21.1, 20.9 / 41.9, 42.8 / 11.9, 12.8 % |

- **The walk tail.** Turning off all three levers moves the walk's p95 only 45 -> 42 and its > 33 ms share 18 -> 12 %.
  - Spikelog ceiling walk (`Logs/tg2world_20261102_102802.txt`, `tools/spike-attribution.py`): 72 % of spikes are main-thread streaming (streamer/evict >= 8 ms). The fluid tick adds +14 ms on its frame (15 % of frames). There were 31 frames > 100 ms.
  - Same walk with `-nosim` (`Logs/tg2world_20261102_103551.txt`): > 100 ms frames 31 -> 1, > 50 ms 6.6 -> 3.0 %, but > 33 ms stays at 12 % (p95 43).
  - The 33-50 ms band follows streamer > 5 ms (25 % of frames, +10.4 ms that frame, -5.7 the next). These are co-occurrence lifts, not proof.
  - Spikelog walks run ~1-4 points worse on > 33 than plain ones (16.4 % vs 12.3 %). Overhead or variance; the ABBA plain walks are the numbers above.
- **Static views:** the near arc meets the tail line only at `skysun_off` or `ceiling`. The open arc already meets it at FULL (p95 30.3, 1.2 %).
- **Exact vs picture-changing:** every arm above changes the picture. Sun off = no sun shadows. Sky probe off = no sky-ambient darkening in overhangs/caves.
  Shells off = no shell detail past the 128 m window (far band instead). The exact levers aimed at the same costs are C1 / C2 (sun) and G (sky probe), est. 2-7 ms
  near (`PERF_EXACT_CUTS.md` section 2). The streaming tail's lever is E / streamer time-slicing (exact at rest; chunks land later while moving).
- Caveats: noon only (sun 78 deg). At walk_near the shells were not full (s3 missing 477, s4-s7 missing), so shells' full cost may be higher. Fast suite PASS 985 FAIL 0 SKIP 16 (`Logs/overnight/168b_fast.out`).
- Stills looked at (`.../UpscalerClips/2026-11-02_095908/stills/`): walk_near FULL vs skysun_off, and walk_open FULL vs ceiling. At noon each pair looks almost the same, because the sun is high and the near wall faces away from it.

## Next
- The supervisor asks the owner (a) vs (b) above. If (a): a design + build task for streamer main-thread time-slicing, folded into E, judged on a playwalk's > 33 % share.
- Not queued (measure-only): the same ceiling at a low sun (evening), where sun/sky matter more.

## For the doc
- 30 fps ceiling, warm AC, 432 rows (`Logs/costsplit_2026-11-02_095908.txt`, playwalks `Logs/tg2world_20261102_0948/0953/1018/1022*.txt`):
  - FULL walk p50 23.7 ms, p95 45, 18 % of frames > 33 ms. CEILING walk (sky probe + sun + shells off): 21.0 / 42.4 / 12.3 %.
  - walk_near FULL 37.2 / 65.5 / 60 % -> skysun_off 20.5 / 31.4 / 1.4 % -> ceiling 17.7 / 29.6 / 0.3 %. walk_open FULL is already 20.8 / 30.3 / 1.2 %.
  - Ceiling walk without fluid: > 100 ms frames 31 -> 1, > 33 ms still 12 %, which follows main-thread streaming.
- **Is 30 fps reachable?** The average yes (already). The <= 5 % > 33 ms tail is not reachable by shading cuts, even if we accept the picture loss.
  It needs streaming/fluid main-thread pacing work (E, widened), or a target of p95 <= 45, which FULL just meets.
- Image: `.../UpscalerClips/2026-11-02_095908/stills/walk_near_full.png` vs `walk_near_skysun_off.png`: the same canyon wall at noon. Look at the shaded faces
  on the left: they are nearly identical, although the right one drops 17 ms. At noon the trade is nearly invisible here; at low sun it would not be.
