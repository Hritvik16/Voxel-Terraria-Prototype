Owner, still pending from earlier tasks: approve 26 (eviction retry) and 22 (coast bar) by deleting `OWNER-APPROVES-26` / `OWNER-APPROVES-22` from their Requires lines. Task 23 needs a plan decision (below). It blocks nothing in the queue.

# 23-coarse-first-gen-plan: DONE (plan only). Coarse-first is worth ~1.5-2x at the worst case, not 10x. An exact alternative found while planning (column-pass sharing, ~1.66x) should go first. The two stack to ~3x. Neither closes the gap alone.

## What changed (no code, no builds, no Unity runs)
- New `PLAN_COARSE_FIRST_GEN.md` (240 lines): what changes, a contracts table with REDESIGN flags, the gain estimate, risks, steps 0-5 and alternatives.
- `KNOWN_ISSUES.md`: one sub-bullet under STREAMING REQUIREMENT ("Where a chunk's cost goes"). Commit 4fecdf6.

## Evidence
All from existing data: the per-chunk TSVs of task 20's 8 walk launches, plus `Logs/TerrainGen/{cost,v2_chunk_cost,detail_cull}.txt`. The TSVs are in `~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/StreamProbe/2026-10-04_*_streamwalk_*/streamprobe_chunks.tsv`.

**Premise corrected.** The 64-128 m ring is drawn at tier 1 (0.2 m, 1/8 of the voxels), not tier 2 (`LODConfig.cs:64`; `SelectLODTier` uses Euclidean distance). So the coarse level must be tier 1. A tier-2-first level saves little: 84% of the flux is inside 128 m.

**Stage split, newly measured.** Air-fast-path chunks run only the column pass. Pairing them with non-uniform chunks in the same (cx,cz) column gives:
- column pass = 0.60-0.63 of `Generate` (p25-p75 0.52-0.75), at both 1 and 7 workers;
- voxel pass about 0.40;
- downsample about 0.21 x gen on top.
- The Editor's single-thread figure agrees: 24 of 41-46 ms.

**Coarse chunk cost (estimate).**
- Exact point-sample: 0.30 of a full chunk (3.4x cheaper).
- Approximate MaxStep: 0.18 (5.4x cheaper).
- The split inside the column pass (`PrepareColumn` ~1.1 us/column) is inferred by subtraction and is flagged as a guess in the plan.

**Full-detail flux (derived from geometry).** The R_f = 76.8 m sphere still takes 870 /s of the 2,285 /s worst case (673 /s at 64 m, 416 /s at 44.8 m). The band is only 89.6 m tall, so the sphere covers most of it.

**Result.**
| option | gain | short of 2,285 /s with ideal workers |
|---|---|---|
| coarse-first (exact, R_f 76.8) | 1.47x | 12.8x |
| coarse-first (approx, R_f 76.8) | 1.77x | 10.7x |
| column sharing | 1.66x | 11.4x |
| both, R_f 76.8 | 2.96x | 6.4x |
| both, R_f 44.8 (detail radius at speed) | 4.6x | 4.1x |
| same, dive capped | -- | 2.5x |

With measured contention (33 /s), multiply each shortfall by ~3.7.

**Column-pass repeat, measured.** ColumnJob ran 5.0 / 4.8 times per chunk column at 7 workers (6,388 runs over 1,275 columns) and 3.6 at 1 worker.

**Main thread gets worse.** A coarse admit still writes the cascade (~1.0 ms of today's 1.22 ms admit), so the worst case is ~56 ms/frame vs 46.6 today. 16b's fix 3 becomes a prerequisite.

## Contracts (detail in the plan's section 2)
- **Unchanged:** `GetVoxel`, `SetVoxel`, `Generate`, D.1, the §4.4 table and the tier-0 uploader. A coarse chunk never enters `ChunkStore`; it is render-only, like the far band.
- **REDESIGN, owner:**
  - §6.4 + 8.9: "LOD0 0-128 m = physics boundary" and "tiers populated by majority vote". Physics, edits and CPU queries shrink to ~77 m, and tiers hold point-sampled data until refine.
  - The §7.4 fluid wake radius must drop from 128 m to ~64 m (PHASE_5C §9.4 mass-loss precedent).
- **Prerequisite:** downgrade (the memory step) inherits task 21's save-cap bug, so task 26 must land first.

## Unfinished / next
- No follow-up queued: every step changes performance, so each needs owner approval.
- Next: the supervisor turns the decisions below into an approval question. If 1(a) is chosen, the next task is step 0 (column sharing; `SUITE=all`; section 7 capacity).

## Decisions for the owner (recommendation first)
1. Order: (a) column-pass sharing first (exact, no contract change), then coarse-first step 1 / (b) coarse-first only / (c) shelve coarse-first and move the worst case first (16b item 4).
2. The §6.4/8.9 redesign (full detail and physics only within ~77 m; point-sampled tiers until refine): (a) accept behind `-coarsefirst`, OFF by default, and judge the pop from pictures / (b) accept only if step 1 shows < X% differing tier-1 voxels / (c) reject.
3. Fluid wake radius under the flag: (a) shrink to ~64 m (distant water stops ticking) / (b) keep 128 m and always refine water chunks (loses much of the gain near coasts) / (c) decide after step 3a.
4. Coarse sampling: (a) exact point-sample (3.4x cheaper, bit-exact test) / (b) approximate MaxStep (5.4x, no exactness test).
5. Downgrade to bound memory: (a) as step 5, after task 26, with the pool cap re-derived from PeakUsed / (b) never (simpler, no memory gain) / (c) now.
