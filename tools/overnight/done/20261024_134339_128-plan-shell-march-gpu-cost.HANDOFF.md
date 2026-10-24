STOP_FOR_OWNER -- Q81: approve the experiment order in `PERF_SHELL_MARCH_PLAN.md` §5? (a) recommended: one capture-only probe task (E1-E3), then ONE restructure.

# 128-plan-shell-march-gpu-cost: DONE (plan only). Plan written; nothing built, launched or measured.

## What changed
- New `PERF_SHELL_MARCH_PLAN.md` (repo root, 128 lines): evidence, ranked hypotheses, 3 experiments, 6 restructures, Q81.
- `TASKS.md`: Task 128 paragraph. No product code. No suite run (no code, per the task file).

## Evidence (all from existing logs; nothing new was run)
- Re-read the counter lines of `Logs/costsplit_2026-10-{16_233425,17_023103,17_102453,22_202941,22_235521,23_000248}.txt`.
  Inside a pose, the frame-time deltas follow DISTINCT DIRECTORY READS (~one per chunk a ray enters), not iterations or page reads:
  - 99 sky exit: directory reads -26 / -38 %, frame -4.7 / -2.6 ms.
  - 93b5 mask: iterations -55 / -29 %, directory reads ~0 %, frame +2.8 / +0.6 ms.
  - 116 column leap: iterations -64 / -23 %, page reads -85 / -46 %, directory reads 0 %, frame ~0.
  - 117 Medium: directory reads -11 %, frame -0.8 ms (noise).
- Across poses it does NOT scale: walk_near (30 k rays, 2.48 M iterations, 0.42 M directory reads) costs +16 ms, the same as walk_open
  (322 k rays, 12.41 M, 4.34 M) at +18 ms. So latency is exposed, most likely from low occupancy (H1): if the cost were throughput,
  each walk_near iteration would take ~50 us.
- Pacing ruled out: `vSyncCount 0`; the 7,900 frames of `Screenshots/115/walk{1,2}_spikes.csv` are flat across the 16.7 ms phase
  (310-377 per tenth).
- The play kernel's diagnostic extras are not the cost: 93b3's `fshade_off` (plain `CSMain`, shells ON) saved -0.1 / +1.2 ms.
- Not determinable without a Metal capture: register count, spills, occupancy, per-dispatch GPU ms, cache/TLB hit rates.

## Next
- The owner answers Q81. If (a), queue ONE capture-only task: E1 (`CSShellProbe` split kernel, arm `shellsplit`), E2 (arm `shellsplit2`,
  double march in the probe), E3 (per-8x4 groupshared SIMD-efficiency counters). One build, one warm-up, F X X F at walk_near/walk_open.

## For the doc
Ranked hypotheses (PERF_SHELL_MARCH_PLAN.md §2):
| # | hypothesis | key support / contradiction |
|---|---|---|
| H1 | Low occupancy / register spills: ShellMarch inlined in the 5,600-line kernel | near = open cost with 10x less work (6.5 vs 1.4 ns per iteration frame-wide); heavier leap +5-6 ms (93b5 b) |
| H2 | Dependent memory latency per chunk visit (directory -> page -> body) | directory reads track ms (99 vs 93b5/116/117); not bandwidth (near reads 10x less) |
| H3 | SIMD divergence (trip count, branch union) | walk_open mixes 106-step hits with 20-31-step sky rays; but walk_near's coherent sky patch costs as much |
| H4 | ALU per iteration (ShellByDistance loop, per-lane constant indexing, leap math) | +5-6 ms for a heavier leap; but 4.6x per-iteration spread between poses |
| H5 | Post-hit shading charged to shells | ruled out: walk_near has 26 hits/frame |
| -- | Bandwidth; vsync pacing | ruled out (above) |

Recommended order:
1. E1+E2+E3 in one capture-only build.
2. ONE restructure picked by the result: R1 shell pre-pass kernel (H1) / R5 chunk-state bitmap (H2) / R6 kernel diet (+R2 compaction if divergence).
3. One warm ABBA of it.

Numbers: shells +16 / +18 ms p50 (93b re-run, `Screenshots/93b/rerun/perf93b_rerun.txt`). Directory reads vs ms: see the table in the plan's §1.
Images: none. This was a plan-only task, and nothing visible changed.
