STOP_FOR_OWNER -- 93b: shells ON are 10-15 ms SLOWER at steady state (rule 3), and they never fill while walking.
Question (Q50): what happens to 93c (shells default in play)?
 (a) RECOMMENDED: hold 93c; queue one task "shells: walking churn + far-after-shell cost": find why shell 1 is generated
     and dropped while walking, and split the far band's +11 ms after the shells (primary vs sun-shadow rays, start t). Re-run 93b after it.
 (b) switch anyway for the visuals. But while walking the shells are nearly empty, so the visual gain doesn't show up in play either.
 (c) leave shells off in play until D1-D4 (93d-93g) land, then re-measure.

# 93b-shells-perf-breakdown: DONE (measured) -- ON +15.2 / +10.5 ms p50 steady; moving ON -2.9 ms but with EMPTY shells

## What changed (commit becf7f1, capture-only, no defaults)
- `PlaygroundCapture.ShellsPerf.cs` (new): cost split + `-shells 1` waits `-shellwait` s (default 120) at the pinned pose, logs
  stored/missing/in-flight per shell and the `-shellsdebug` tint still; `-sustained` prints whole-run p50/p99/>33 ms share, shell
  counts at the walk's start/end, the time each shell completed, and the streamer counters (requested/generated/over-budget).
- `PlaygroundCapture.CostSplit.cs`: arms `shells_off` (DrawShells false: tiers 1-2 draw, workers keep running), `fshade_off`.
- KNOWN_ISSUES entry + CLAUDE.md index line. Report: `Screenshots/93b/perf93b.txt` (all numbers, log paths).

## Evidence (warm steady state; warm-up `Logs/tg2world_20261015_205445.txt`; ABBA back to back, AC, no owner pause)
Setting: fsr3 540, quality preset, far band + far shadows, `-farshade 1 -seethrough 1 -riverlook 1`, noon; ON adds `-shells 1 -shellworkers 3`.
| arm | p50 ms | p99 ms | >33 ms | peak MB | Metal MB |
|---|---|---|---|---|---|
| steady walk_near OFF / ON (2 launches each) | 53.6, 51.2 / 66.5, 68.6 | 91-119 / 120-132 | - | - | - |
| steady walk_open OFF / ON | 36.2, 35.5 / 45.4, 47.2 | 50-69 / 87-88 | - | - | - |
| moving walk OFF / ON | 41.8, 43.3 / 40.4, 38.9 | 99, 100 / 92, 90 | 70, 76% / 69, 65% | 2,542, 2,564 / 2,943, 2,936 | 1,164, 1,210 / 1,526, 1,528 |
- Steady delta: +15.2 ms (near, noise 2.4) and +10.5 ms (open, noise 0.7). ON with a 420 s fill wait: 66.1 / 45.9, the same
  (`Logs/costsplit_2026-10-15_220254.txt`; it ended on battery). Logs: `Logs/costsplit_2026-10-15_{210051,210747,212140,213552}.txt`,
  `Logs/tg2world_20261015_{214220,214718,215208,215700}.txt`.
- Where the cost is (paired arms in the ON launches): turning shells off saves 8.6-16.4 ms, so ON with shells off = OFF and
  the 3 shell workers cost ~0. Turning the far band off saves 13.6-19.2 ms with shells ON vs 2.0-5.7 ms with them OFF.
  So: the shell march costs ~+3 ms more than tiers 1-2, and the far band after the shells (`tFar = tShellEnd`, task 85) costs
  ~+11 ms more than after the tiers. Why that march costs more is NOT isolated. gpu* rises 34-39%, so the extra cost is on the GPU.
- Shells 1-2 hit their page cap (FULL) at both walk poses with 621 / 1,140 chunks unstored (shell 3 too at walk_open).
- Moving: shell 1 held 104-149 chunks at the walk's start and 76 at the end; shells 2-7 held none; nothing completed. A counter run
  (`Logs/tg2world_20261015_222142.txt`, on battery, counts only): 2,347 chunks generated in 3 min, 170 held, 1 over-budget frame
  in 4,912, so the upload cap is NOT the cause. Likely cause, not verified: nearest-first order keeps the workers on the chunks
  just outside shell 1's hole, and the eye walks into them. So the moving ON arm measured "tiers 1-2 not drawn", not the shells.
- Memory ON: +387 MB peak (2,940 of 3,072), Metal +340 MB, with tiers 1-2 still allocated (193 MB; 93d frees them).
- Fast suite `PASS 745 FAIL 0 SKIP 14` (`Logs/overnight/93b-fast.out`).
- Looked at: `Screenshots/93b/sheet_walk_open.png`, `sheet_walk_near.png`. OFF and ON look the same at both poses. Pixel samples
  of the tint still: the nearby mountain is tier 0 (blue), the ridge top right is shells 1-2 (green/yellow), the sea horizon is
  the far band (grey).

## Not done
- `tiny_gate` arm not run. The OFF bracket after the 420 s launch was stopped: the Mac switched to battery at 22:18.
- Rule 2's `later/` task "shell march cost" was not written, because rule 3 applies (the owner decides first).

## For the doc
Task 93b: shells ON vs today's play, same settings (fsr3 540, quality, far band + far shading), warm, ABBA.
- Steady (streaming pinned): p50 52.4 -> 67.6 ms (walk_near, +15.2), 35.9 -> 46.3 ms (walk_open, +10.5); OFF-vs-OFF noise 0.7-2.4 ms (`Screenshots/93b/perf93b.txt`)
- Cost: shell march +3 ms over tiers 1-2; far band drawn after the shells +11 ms (far off saves 13-19 ms ON vs 2-6 OFF)
- Moving (5 m/s walk): 42.6 -> 39.7 ms p50, BUT shell 1 held only ~76-208 chunks and shells 2-7 none (2,347 generated in 3 min)
- Memory: peak 2,553 -> 2,940 MB (cap 3,072), Metal 1,187 -> 1,527 MB
- Rule: ON > 3 ms slower -> 93c held (STOP_FOR_OWNER, Q50)
Images:
- `Screenshots/93b/sheet_walk_open.png`: top left OFF, top right ON: the same view across a terraced mountain; nothing visibly
  better or worse. Bottom right, the shell tint: the mountain is tier 0 (blue cast), the ridge top right is shells 1-2 (green/yellow),
  the sea and horizon on the left are the far band (grey).
- `Screenshots/93b/sheet_walk_near.png`: a canyon wall at 10-30 m; OFF and ON are the same. The tint is blue all over (tier 0).
