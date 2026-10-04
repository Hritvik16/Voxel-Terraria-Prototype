# 30-column-pass-sharing

Requires: 23-coarse-first-gen-plan

Approval: owner, 2026-10-04, Queue decisions Q18 (a): "column sharing first (exact, no rule
change; full test suite plus the streaming measurement), then coarse-first step 1." This is
step 0 of `PLAN_COARSE_FIRST_GEN.md` (section 5, and section 6 "Column-pass sharing").
Owner also answered Q19 (a) to every part: 2a allow coarse-first behind `-coarsefirst`, off
by default, judged from pictures; 3a water beyond ~64 m stops ticking under the flag;
4a exact point-sample; 5a downgrade later, after task 26. Those are for LATER steps; record
them in the plan file (a short "Owner decisions 2026-10-04" section) in this task's commit.
Queued by the supervisor.

## Why
`TerrainGenChunkSource`'s Burst `ColumnJob` (~0.60 of a chunk's `Generate`) depends only on
the chunk's (x, z) column, but runs once per chunk LAYER: 5.0 / 4.8 times per column at 7
workers (task 23, from task 20's TSVs). Doing it once per column removes ~4/5 of it:
about 1.66x on the measured walk, bit-identical output.

## Do (one change)
- Generate a column's requested band layers from ONE column pass. Prefer batching the
  layers of a column into one worker job (that worker owns the column scratch).
- If a cache is needed for layers requested later (a dive adds layers to existing columns),
  it is a SMALL bounded LRU (each column result is ~2.5 MB; all 441 would be ~1.1 GB, and the
  process is already over the 3 GB cap) owned by ONE thread. No locks in the streaming path
  (CLAUDE.md single-writer: if a lock seems needed, restructure instead). Say the bound and
  its peak memory.
- Output must be bit-identical: chunk voxels, hashes and goldens unchanged. Add an EditMode
  test that generates a column's layers both ways and compares bytes.

## Measure (PREAMBLE rule 9: warm steady state)
- `SUITE=all` (TerrainGen/WorldGen code), 0 FAIL.
- One build, one warm-up launch, then `MODE=stream MOTION=walk` on map v4, before vs after
  interleaved ABBA (the "before" arm = this branch's previous commit, or a diagnostic
  `-columnshare 0` switch if you add one; say which). Report section 7 capacity, the LATE
  line (section 4), ColumnJob runs per column, worker gen p50/p99, peak footprint.
- `MODE=poses` once: byte-identical shots vs the previous poses run (bit-identical terrain).
- Stop after measuring (CLAUDE.md: one performance change, then report).

## Do not
Start coarse-first step 1, change worker count or thread priority, or change generator output.
