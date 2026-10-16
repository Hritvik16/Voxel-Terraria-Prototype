# 93b2d-shell-fill-worker-cost

Requires: 93b2-shells-fill-while-walking

Approval: OWNER, 2026-10-16 ~15:40, Queue decisions doc Q56 = (a): "keep the fill on shells 1-2 and queue a perf task that
cuts the fill's worker cost to <= ~10 ms per chunk". **This IS a performance task:** PREAMBLE rule 9 (warm steady state,
one warm-up launch, interleaved arms) applies. Branch `engine-phase`.

## Why
Task 93b2 part two (`52aad5e`; `tools/overnight/blocked/93b2-shells-fill-while-walking.HANDOFF.md`) moved task 83's cave
fill down to shells 1-2 (`FIRST_FILL_SHELL` 3 -> 1). Standing still, shells 1-2 now fill their whole band (0 FULL). But
the fill adds worker time: shell 1 +24-25 ms per chunk at the start pose (cool), +44-54 ms over a whole launch; shell 2
+27-29 ms. Editor split for shell 1: generator 91 ms, fill without mouths 107, with mouths 148 ms
(`Logs/Shells/fill83_prefilter.txt`), so the cave-MOUTH pass (>= 1/4 of a voxel's underground 4^3 sub-samples are cave
air) is ~70% of the fill. With 3 shell workers, shell 1 takes ~80% of them and shell 2 starves while walking
(walk's end s1/s2/s3 948/322/644 -> ~768/~141/~440, logs `Logs/tg2world_20261016_{144656,145541}.txt`).
Goal: the fill costs <= ~10 ms per shell-1/shell-2 chunk on a worker.

## Do
Code: `Assets/CoreEngine/WorldGen/TerrainGenChunkSource.ShellFill.cs` (and the mouth sub-sampling it calls).
1. **Isolate first** (CLAUDE.md measurement discipline): split the fill's timer into its phases (e.g. mouth pass /
   enclosed-air flood / fluid fill / flatten) and report per-phase ms per chunk for shells 1, 2 and 3, one short launch.
2. **One change** on the biggest phase. The fill's OUTPUT must stay BYTE-IDENTICAL to `52aad5e`'s (shells 1-7): it is a
   pure speed change. Typical candidates (pick from what step 1 shows, do not chain them): reuse the column pass's cave
   samples instead of re-evaluating the noise per sub-sample (task 86's coarse cave fields make them bilinear in x/z),
   skip sub-sampling voxels whose column proves no cave can reach them (as task 82b's `UniformSamples` does), Burst /
   SIMD the inner loop, or share sub-samples between neighbouring voxels.
3. Measure (warm steady state): fill ms per chunk per shell and walk's-end held per shell, before -> after, same build
   flag or A/B arms, interleaved. If the goal is not met with one change, STOP after measuring and name the next change
   in HANDOFF (the supervisor queues it): do not chain several speed changes in one run.

Do NOT change: the fill's rule (what counts as enclosed, the 1/4 mouth threshold), `FIRST_FILL_SHELL`, the Q47 (a) order
policy, the number of shell workers, pool sizes, memory layout. Shells OFF stays byte-identical. Not allowed:
coarsening the fill's result to save time (owner chose the fill as it is).

## Acceptance
- A byte-identity test: filled chunks of shells 1-7 hash the same before and after (extend `ShellFillTests`; the
  3.68 M outside-ray check still 0 differ, mouths 0 lost).
- The per-phase split before, and fill ms per chunk per shell before -> after (warm steady state, the warm-up's log path).
- The same 3 min 5 m/s `MODE=walk` launch as 93b2 (`-shells 1 -shellworkers 3`, `-walkshellwait`, 120 s start wait):
  held per shell 1-4 at the walk's end and FULL refusals, before -> after. Target from 93b2 stays shells 1-3 >= 90%;
  report how far short if not (93b2 estimated 2-3x the throughput is needed whatever the fill costs).
- Fast suite once at the end. One tint pair at the walk's end (before/after; midday if the mode allows, else say so).
- `KNOWN_ISSUES.md` "SHELLS COST +10-15 ms STEADY, NEVER FILL WHILE WALKING": update the fill-cost line.
- `## For the doc`: the phase split, fill ms per chunk before -> after, walk's-end held per shell, the tint pair.
