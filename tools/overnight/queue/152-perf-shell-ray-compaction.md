# 152-perf-shell-ray-compaction

Model: opus

Approval: owner, 2026-10-25, doc Q97 answer (a): "queue R2 ray compaction, then the shell height bound, one task each, exact cuts first".
A PERFORMANCE task, ONE change. Plan: `PERF_SHELL_MARCH_PLAN.md` (R2 row line ~110; E3 divergence check lines ~84-120), `PERF_30FPS_PLAN.md`,
`Logs/costsplit_2026-10-25_115751.txt` (task 146: shells path 8.8 near / 5.5 open ms; target walk p50 -14.7 ms).

## Decision table (written before building)
R2 is exact (no layout change) but only worth building if the march diverges. FIRST read the existing E3 numbers (task 130/93b4: efficiency
at walk_open / walk_near, air-brick-step share 43-75%) in PERF_SHELL_MARCH_PLAN / TASKS.md. If they are missing for the current build
(R1/R6 changed the kernel), run the existing divergence counter once (do not write a new one unless none exists).
- efficiency >= 0.6 at both poses: R2 not worth it: STOP_FOR_OWNER with the numbers, build nothing, name the next cut (height bound is task 153).
- <= 0.35 at walk_open (or low at both): build R2.
- in between: build R2 behind the flag and judge by the cost-split below; keep only if >= 1 ms walk.
Generality rule (owner 2026-10-24): exact and data-derived, an adversarial pixel-identity case, no constants tuned to current terrain.

## Do
1. Implement R2 as the plan specifies (R1's pre-pass appends flagged pixels to a list, sorted into sky / likely-hit buckets from last frame,
   `DispatchIndirect`), flag `-shellcompact 1` (ON only if the proof and the gain below hold, else OFF), the old path intact as the A arm.
2. Proof: raw stills byte-identical (0 differing px) at walk_near / walk_open noon + sun 20 + cliff2 + one edit-op adversarial pose, flag on vs off.
3. Cost: warm, one cost-split / ABBA of on vs off at walk_near and walk_open (window means). One performance change only.
4. Fast suite once at the end.

## Deliverable
HANDOFF: decision-table row taken, pixel diffs, ms per pose, default state, log paths, For the doc. TASKS.md paragraph.
