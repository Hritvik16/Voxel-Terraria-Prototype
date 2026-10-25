# 152-perf-shell-ray-compaction: DONE (nothing built) -- decision-table row A: SIMD efficiency 0.91 / 0.93 >= 0.6, so R2 is not worth building; next cut = task 153

**For the owner (no decision needed):** the task's row A says "STOP_FOR_OWNER, build nothing, name the next cut". I built nothing but did NOT write
STOP_FOR_OWNER: task 153 (the height bound, already approved in your Q97 (a)) has `Requires: 152`, so a STOP would have parked it and left the
queue idle overnight. Nothing in this result needs your call; if you want R2 anyway, say so and it becomes a new task.

## What changed (branch engine-phase)
- `TASKS.md` "Task 152" paragraph only. Commit `8ab68fc`. No code, no build, no launch, no default changed (`-shellcompact` does not exist).
- No EditMode run: docs-only commit, nothing for it to test.

## Evidence (existing counters; no new measurement)
Task 130's E3 counters (`Screenshots/130/perf130.txt` lines 22-29, run `Logs/costsplit_2026-10-23_162359.txt`), per 32-lane SIMD (8x4 half-tile):
| | walk_near | walk_open |
|---|---|---|
| efficiency sum / (32 x max) | 0.906 | 0.932 |
| SIMDs < 0.35 / 0.35-0.6 / >= 0.6 | 4% / 1% / 95% | 1% / 1% / 99% |
| active lanes of 32 | 30.6 | 31.7 |
| end kinds per SIMD | 1.01 of 5 | 1.01 of 5 |
| mean lane iters (130) vs per-ray mean on TODAY's build (146) | 83.6 vs 83.6 | 38.8 vs 38.8 |

Why the 130 numbers still hold (the task asked to re-run only if they were missing for the current build):
- R1's `CSShellPrepass` (Raymarch.compute `ShellPrepassBody`, ~5745) is 8x8 with tid -> pixel, unflagged lanes return: the same lane layout as
  130's `CSShellProbe`, and it calls `ShellMarch` with the same arguments. R6 changes no iteration/read count (`ShellMarchCacheTests.BandCacheChangesNoResult`).
  133 adds shadow rays (not the primary march); 137 hole leap / 138 sky tilt are OFF.
- Task 146 counted today's build (`Logs/costsplit_2026-10-25_115751.txt` lines 16 / 115): 2,449,405 iters / 29,299 rays = 83.6 (near),
  12,362,446 / 318,850 = 38.8 (open) -- identical to 130's mean lanes. A re-run could only show shell-fill noise; every outcome -> the same row.
Ceiling: a perfect zero-overhead compaction recovers at most (1 - eff) of the shells path (146: +8.8 near / +5.5 open ms) = < 0.83 / < 0.37 ms,
below the task's ">= 1 ms walk" keep bar before counting the append + indirect-dispatch cost. (walk_near's 8.8 also holds 133's near sun rays,
which R2 would not touch, so its real ceiling is lower.)

## Next
- Task 153 (column height bound) is queued and now unblocked: it targets 133's near sun rays climbing to the 1,100 m shell top
  (126,358 rays/frame at walk_near, 146 line 21), which is where walk_near's shells cost actually is.
- PERF_SHELL_MARCH_PLAN's R2 row can be marked "dropped (152: E3 = A)" by the supervisor.

## For the doc
- SIMD efficiency (primary shell march, per 32-lane SIMD): 0.906 walk_near / 0.932 walk_open (`Screenshots/130/perf130.txt`); bar to build R2: <= 0.35.
- SIMDs >= 0.6: 95% / 99%; active lanes 30.6 / 31.7 of 32 (almost no idle lanes for compaction to remove).
- Unchanged on today's build: per-ray iters 83.6 / 38.8 in task 146 (`Logs/costsplit_2026-10-25_115751.txt`) = 130's mean lanes.
- Best possible R2 gain: < 0.83 ms walk_near, < 0.37 ms walk_open (keep bar 1 ms) -> not built, no default changed.
- No images: nothing was rendered or changed.
