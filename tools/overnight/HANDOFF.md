# 93b2d (owner Q56 (a)): DONE -- shell fill cost 35-52 -> ~9 ms per shell-1/2 chunk on a worker, byte-identical; walk target (>= 90%) still NOT met

## What changed (commit 3c7aedb, engine-phase)
- `TerrainGenChunkSource.ShellFill.cs`: the fill job runs once per PHASE (setup, flood, cand, sub, fill, flat), each timed into
  `ShellChunk.FillMs`; `ShellStreamer.CountersReport` prints `f1..f7` lines (fill ms per FILLED chunk by phase).
- THE ONE SPEED CHANGE: the two `TouchesSeed` scans (flood seeds, mouth candidates) start each column at `ScanLo` instead of ly 0.
  Below `ScanLo` no seed can touch a voxel: the column's open height - 1, the side neighbours' open heights, halo/top-layer seeds, op boxes.
  The same voxels pass, so the marks and the per-column candidate lists are the same. `-shellfillfast 0` / `ShellFillFast = false` = 52aad5e's scans.
- Not changed: the fill rule, the 1/4 mouth threshold, `FIRST_FILL_SHELL`, the Q47 (a) order, worker count, pools, memory layout. Shells off: untouched code.

## Evidence
- Phase split BEFORE (worker, A1 whole launch, ms per filled chunk, setup+flood+cand+sub+fill+flat):
  s1 0.9+3.7+**40.8**+1.9+4.3+0.6 = 52.1; s2 0.6+2.6+**30.3**+3.1+3.2+0.9 = 40.8; s3 0.7+3.3+**30.3**+4.4+3.3+5.2 = 47.3.
  The cost was the mouth CANDIDATE SCAN, not the sub-sampling (93b2 had guessed the sub-sampling). Editor split: `Logs/Shells/fill93b2d_phases.txt`.
- Byte identity (`ShellFillTests.Speed.cs`):
  - `Fill_BytesAsGolden_FastAndReference` (Slow): 85 chunks, shells 1-7 (walk start, entrances, cliff sites), on both paths, hash as GOLDEN = 52aad5e's code. 0 differ.
  - `FastPath_WithOps_SameBytesAsReference`: a far crater plus a buried carve, shells 1-3.
  - `ShellFillTests` all pass: 3,677,460 outside rays 0 differ; mouths 0 lost (`Logs/Shells/fill83_tests.txt`).
- Fast suite **PASS 802 FAIL 0 SKIP 14** (`Logs/overnight/93b2d-fast.out`).
- Walk (3 min, 5 m/s, `-shells 1 -shellworkers 3 -walkshellwait 120 -renderpreset quality`), one build.
  Warm-up `Logs/tg2world_20261016_183103.txt` (discarded). Then A1 B1 B2 A2: `Logs/tg2world_20261016_{183836,184518,185200,191249}.txt`.
  Copies of each arm are in `Screenshots/93b2d/{A1,B1,B2,A2}/walk.txt`.
  Conditions: all arms ON BATTERY (54 -> 30%, low power mode off). A2 started after a 14 min idle gap, so it ran cooler, which favours A.
  | | A1 / A2 (old) | B1 / B2 (fast) |
  |---|---|---|
  | fill ms/filled chunk, whole launch s1 / s2 / s3 | 52.1 / 40.8 / 47.3 ; 45.4 / 35.2 / 36.7 | 9.3 / 8.8 / 14.1 ; 9.5 / 9.3 / 15.1 |
  | same at the start pose s1 / s2 | 30.5 / 35.6 ; 25.7 / 27.6 | 4.9 / 7.7 ; 5.4 / 8.0 |
  | start pose complete s1 / s2 (s) | 42 / 92 ; 33 / 70 | 23 / 61 ; 24 / 65 |
  | walk's end held s1 / s2 / s3 / s4 | 938 / 390 / 602 / 135 ; 1001 / 471 / 654 / 312 | 1216 / 701 / 650 / 338 ; 1303 / 677 / 654 / 257 |
  | shell chunks generated in the launch | 12,943 ; 14,414 | 16,028 ; 15,712 |
  | FULL refusals | 0 | 0 |
- Target >= 90% at the walk's end is NOT met: s1 ~58% (2,170), s2 ~32% (2,170), s3 ~39% (1,663).
  - Shell 3 gets NO worker time during the walk in any arm (generated at the start pose = generated at the end).
  - Shell 1 still takes most of the workers: ~10,700 generated, ~8,700 evicted as its box moves, ~46 ms each (column 29 + voxel 12 + rest 5, averaged with air chunks).
- Peak footprint 2,525-2,565 MB in every arm.
- B2's player hung at quit after writing its report (0% CPU). I killed it; the report was complete. The cause is not isolated (93b2 saw a quit race too).

## Next (for the supervisor)
- The fill is no longer the limit. The next named speed change is the shell-1 COLUMN PASS while walking: 29 ms per chunk over the launch vs 11 at the start pose, because reuse is rarer as the box moves. That is the largest block of worker time now (~310 of ~900 worker-s per launch).
  After it, shell 3's voxel pass (104-113 ms per chunk).
- 93b2's estimate stands: >= 90% at 5 m/s needs ~2x more throughput. Rationing shell 1 or adding a 4th worker (Q56 (b)/(c)) remain owner options.

## For the doc
Task 93b2d: the shell cave fill is ~5x cheaper on a worker with the same bytes. The cost was the scan for mouth candidates, not the mouth sub-sampling.
- Phase split before (shell 1, ms/filled chunk): setup 0.9, flood 3.7, **candidate scan 40.8**, sub-sample 1.9, fill 4.3, flatten 0.6 (`Screenshots/93b2d/A1/walk.txt`).
- Fill per filled chunk: s1 **52 / 45 -> 9.3 / 9.5 ms**; s2 **41 / 35 -> 8.8 / 9.3**; s3 47 / 37 -> 14 / 15 (A1/A2 -> B1/B2).
- Walk's end held: s1 938-1,001 -> 1,216-1,303; s2 390-471 -> 677-701; s3 ~650 both (target 90% not met). Start pose s1 complete 33-42 -> 23-24 s.
- 85 chunks of shells 1-7 hash as 52aad5e's; fast suite PASS 802 FAIL 0.
Image: `Screenshots/93b2d/walk_end_before_after.jpg`.
- Left = A1 (old fill), right = B1 (fast fill). Top row = the walk's end view; bottom row = shell tint (blue = tier 0, green = shell 1, red = shell 4, grey = far band).
- The picture is the same, with no holes.
- After, a red shell-4 patch covers the right hillside (A1 held 135 shell-4 chunks, B1 338), and a thin green shell-1 strip runs along the far ridge edge. The plain is far band (grey) in both.
- The light is dusk-blue because the walk mode pins the clock at 17:35; there is no midday option in this mode.
