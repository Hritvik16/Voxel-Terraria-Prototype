STOP_FOR_OWNER -- 93b2: walking fill throughput FIXED (2.8x), but shells 1-2 stop at their BRICK caps (~55% / ~28% of the band).
Question (Q53): how should shells 1-2 hold their whole band?
 (a) RECOMMENDED: extend task 83's cave fill to shells 1-2 (today shells >= 3, owner Q44 (a)). No extra memory. Unfilled shells 1-2
     are 67% mixed chunks (caves below the surface); filled shell 3 is 26%. That should bring shell 1 under its cap; shell 2 likely
     still short (~75%). Cost: the fill adds ~50 ms per chunk on the workers (shell 3's figure).
 (b) Raise the shell 1-2 brick pools: ~+250 MB (s1 +71, s2 +179). More than D1 freed (193 MB); peak ~2,740 -> ~2,990 MB of 3,072.
 (c) Accept it: shells 1-2 hold what fits at mountain spots, the far band draws the rest; go on to 93b3.

# 93b2-shells-fill-while-walking: cause found (2 causes), throughput half fixed; >= 90% target NOT met (caps)

## Cause (step 1, counted per shell over a walk; `ShellStreamer.Counters.cs`)
- Not churn at the hole edge, not drops on arrival (10-20 per launch), not recentre losses: hypotheses 2-3 ruled out.
- (4) THROUGHPUT: ~90% of a shell chunk's time is its COLUMN PASS (shell 1 at the start: column 90-119 ms, voxel 8-11 ms).
  It depends on (shell, x, z) only, yet ran once per LAYER: 13 times per shell-1 column. That is task 30's tier-0 bug, in the shells.
  Walking at 5 m/s, shell 1 alone needs ~33 chunks/s (5,893 generated in the 3 min walk after the fix). Before: ~9.5/s,
  so it fell 3-5x behind the box; shell 2 got no worker at all during the walk.
- (1) CAPS, the second limit: shells 1-2 go FULL on BRICKS (pages are half free), at 1,130-1,338 of 2,168 chunks (s1) and 510-610 of ~2,000 (s2).
## Fix (commit c265106; one change, same bytes, design kept)
- `GenerateShellChunk` COLUMN REUSE: the scratch keeps its column (key: source, spec stamp, shell, x, z, sample offset) and skips the job.
- `ShellStreamer` COLUMN AFFINITY: a returning scratch first takes the NEAREST missing layer of its own column (tier 0's column batch,
  one layer per hand-over). The global pick passes over columns another scratch owns. Across columns the order is still Q47 (a)'s.
  Scratches go from workers + 2 to 2 x workers + 2 (+3 x ~4.4 MB). `-shellcolreuse 0` = the old behaviour (diagnostic A/B).
- No frozen API, layout or memory-layout change. With shells off nothing is called.
- Capture: per-shell counters at the walk's start and end (`CountersReport`), held per 30 s window, an `-walkshellwait S`
  start-pose wait (the walk clock starts after it), and a `99_end_tint` still.
## Evidence
- `ShellColumnReuseTests` (3): reuse byte-identical (shell 1, and filled shell 3); spec change drops the column; ~1 pass per column;
  walking twin (Slow; modelled 3 workers, column 150 / voxel 15 ms, 5 m/s for 15 s): ON 240 -> 1,211 held (FULL), OFF 240 -> 458
  (`Screenshots/93b2/walk_twin.txt`). Shell tests filter PASS 24 (order, storage, redraw, summaries, hand-over); fast suite
  **PASS 763 FAIL 0 SKIP 14** (`Logs/overnight/93b2-fast.out`).
- Launches (same build, AC, counts only, 120 s start-pose wait + 3 min walk; flags in `Logs/overnight/93b2-{before2,after}.out`):
  before = `-shellcolreuse 0` `Screenshots/93b2/before/walk.txt`, after `Screenshots/93b2/after/walk.txt`.
  First run (HEAD + counters; its walk was only 55 s, timer bug since fixed) `Screenshots/93b2/first_run_55s_walk.txt`: same pattern.
- Peak footprint 2,742 -> 2,739 MB. No frame times quoted (rule 10).
- Not changed: shell 1 alone takes ~77% of the 3 workers while walking, so shell 3 still drains (1,225 -> 639). A 4th shell
  worker or the fill question above decides that; 93b3 / the 93b re-run measures it.
## Next step
Owner answers Q53. Then 93b3 (far band after the shells), then re-run 93b.

## For the doc
Task 93b2: why the shells empty while walking. Each shell chunk re-ran a column pass that depends only on x and z,
for every one of its 13 layers. Now it runs once per column. Shells 1-2 still stop at their brick caps (owner Q53).
| 5 m/s walk, 3 shell workers | before | after |
|---|---|---|
| shell chunks generated (whole launch) | 3,598 | 9,948 |
| shell 1 column pass reused | 0% | 86% |
| start pose: shell 1 FULL after | 46 s | < 10 s |
| walk's end held s1 / s2 / s3 / s4 | 207 / 125 / 146 / 0 | 1,049 / 351 / 639 / 141 |
| band s1 / s2 / s3 / s4 | 2,170 / 2,170 / 1,663 / 987 | same |
Logs `Screenshots/93b2/{before,after}/walk.txt`.
Images:
- `Screenshots/93b2/walk_end_before_after.jpg`: top row, the walk's end view, before (left) and after (right): the same
  picture (near mountain, hill, far plain). Bottom row, the shell tint: green = shell 1, blue = tier 0, grey = far band.
  After, a green strip of shell 1 covers the valley floor left of the hill (~5x the green pixels). The hill and the plain are
  grey (far band) in both, because shells 2-3 did not fill there (caps / workers).
