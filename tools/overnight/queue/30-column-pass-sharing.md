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

## Check (supervisor edit, owner 2026-10-08 -- replaces the warm ABBA streaming measurement)
The output is byte-identical, so any speed-up is kept whatever its size; the only result that
would change what we do is "slower". So NO warm-up, NO `MODE=stream` ABBA arms:
- `SUITE=all` (TerrainGen/WorldGen code), 0 FAIL, with the new both-ways byte-compare test.
- ONE isolated timing check (EditMode or a single short launch, cold is fine): generate the same
  set of column layers both ways, report ms per column and ColumnJob runs per column, plus the
  bounded cache's peak bytes. Revert if sharing is slower.
- `MODE=poses` once: byte-identical shots vs the previous poses run (bit-identical terrain).

## ROADMAP.md (owner 2026-10-08) overrides part of Q19
When you record the 2026-10-04 owner decisions in `PLAN_COARSE_FIRST_GEN.md`, mark Q19 3a
("water beyond ~64 m stops ticking under the flag") as SUPERSEDED by ROADMAP.md E7 (active fluid
should keep simulating wherever it is). Add one line at the top of that plan: "Order and scope:
ROADMAP.md (E2/E3) overrides this plan where they differ."

## Do not
Start coarse-first step 1, change worker count or thread priority, or change generator output.
