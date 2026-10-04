# 23-coarse-first-gen-plan

Requires: 20-worker-contention-probe

Approval: owner, 2026-10-04 ~00:00, Queue decisions doc Q14, answer (a). The approved
option was "Queue a planning-only task after 20 (no code, no builds): how coarse-first
fits chunk content, edits, saving and the GPU mirror; the expected gain; the risks.
The owner approves the plan before any code." Queued by the supervisor.

PLANNING ONLY: no source changes, no builds, no Unity runs. Read code, logs and docs.

## Why
KNOWN_ISSUES "STREAMING REQUIREMENT" (task 16b): the worst case needs 2,285 chunks/s.
Even ideal workers (isolated 49-65 ms per chunk x 7 threads, ~121 /s) are 19x short.
16b's fix 2 is the only lever on its list that plausibly closes that: chunks in the
outer ring (about 64-128 m) are drawn at cascade tier 2 (0.4 m, 1/64 of the voxels),
so generate them coarse first and refine to full detail later, or only when needed.
Task 20's result (worker contention) and task 22's (detail radius at speed) set the
baseline this plan starts from. Read their HANDOFFs in `tools/overnight/done/`.

## Write `PLAN_COARSE_FIRST_GEN.md` (repo root), under ~250 lines
1. **What changes.** Which chunks start coarse, at which tier, and when (or whether)
   they refine. Ordering against nearest-first plus heading generation.
2. **The contracts it touches.** Cite the section of ARCHITECTURE_v8.6.md or the
   amendment for each, and say "frozen" where a frozen API is involved (CLAUDE.md hard
   invariants):
   - CPU sole writer (`ChunkStore.SetVoxel`).
   - `IWorldQuery.GetVoxel` on a coarse chunk.
   - Edits (`IEditService.SetVoxel`) on a coarse chunk.
   - D.1 deltas and persistence: is the base still the deterministic full chunk?
   - The GPU mirror and cascade tiers: are tiers written from coarse data?
   - Occupancy mask and air mips (8.7 / 8.8: read the implementations, no docs exist).
   - Fluids, lighting, player and entity collision near the edge of full detail.
   Any change to a frozen contract is flagged as a REDESIGN needing the owner.
3. **Gain estimate**, from measured numbers only:
   - The generator's own stage costs from existing StreamTrace TSVs.
   - What fraction of per-chunk cost scales with voxel count. Say what is a guess.
   - The resulting capacity against 2,285 /s.
4. **Risks**, including visible pop when a chunk refines, and memory against the
   3 GB cap (already exceeded: KNOWN_ISSUES).
5. **Steps**: one change per task, each with its measurement (`MODE=stream`
   section 7) and its stop point, in the existing task-file shape.
6. **Alternatives** considered and why not (for example, a cheaper full generator).

The supervisor turns the plan into an owner approval question. End your HANDOFF
with the 3-5 decisions the owner must make, each as a multiple-choice line with
your recommendation first.

## Do not
Write code, change defaults, build, or run Unity.
