STOP_FOR_OWNER -- Amendment 8.14 draft is written (`AMENDMENT_8_14_LOD_SHELLS_DRAFT.md`). It needs the owner's answers before step 10. "All (a)" approves it.
1. Q1. Shell sizing: (a) 720 rows (~+150 MB, +15% generation vs 540, est.); (b) 540; (c) 720 near (shells 0-2), 540 beyond.
2. Q2. Hand-over: (a) where the finer voxel reaches 1 px (the coarser arrives at 2 px; the 2026-09-25 strict test looked no better and cost +6 ms); (b) strict ≤ 1 px (~2x terrain memory, over 3 GB at 720); (c) strict for shell 0 only (+300 MB).
3. Q3. Op log vs D.1: (a) by time order (op count at save, kept in the new op file; D.1 frozen); (b) ops then D.1 (a later far op is lost in dug bricks); (c) a new D.1 version (frozen-format redesign).
4. Q4. Awake fluid outside the smaller window: (a) stays resident inside the 32-chunk CPU ring, no new §4.4 state; (b) a new §4.4 "patch" state now (redesign); (c) don't shrink near awake fluid.
5. Q5. Coarse-first pass line: (a) centre sample, ≥ 97% solid/air agreement in surface bricks + ≥ 99% column heights within 1 coarse voxel; (b) corner sample (Q19 4a); (c) 90%.
6. Q6. Far band: (a) keep it beyond the last shell, retire at step 23 if shells cover the horizon; (b) retire at step 11; (c) keep it for good.

# 60-engine-9-amendment-8-14-draft: DONE (planning only). Draft committed; owner decision needed.
## What changed (branch `engine-phase`)
- 5ad05a4: `AMENDMENT_8_14_LOD_SHELLS_DRAFT.md` (new, the only file). No code, no Unity, no builds, no rigs.
## Evidence / sources (nothing measured; every number has a source or "(est.)")
- Memory baseline: task 55's ledger (`Logs/overnight/55_pool_ledger.out`): terrain owners 1,322 MB; pool peaks tier0 379,527, t1 76,150, t2 16,045. Task 53 peaks: poses 82,641 / 16,437; phys_footprint poses 3,077, walk 2,869 MB.
- Frame time: task 57 (`git show 572f5f8:tools/overnight/HANDOFF.md`): 49.4 / 30.4 ms; tiers 0.3 / 2.9; far band 1.8 / 3.8.
- Generation capacity 23-37 chunks/s warm (task 28, plan E3); column cost split from `PLAN_COARSE_FIRST_GEN.md` §3.1.
- Rule-B evidence: `LODConfig.cs:50-58` (2026-09-25: strict bounds no better, 11.7 vs 11.4/255, +6 ms).
- Geometry for the shell tables: formula in the draft §2 (shells, pow2 rings, hollow, horizon 6.6 km, world 8 km × 1.1 km).
## Findings worth knowing
- The plan's "720 costs 1.78x" is ~1.3x once rings snap to chunks and the horizon cuts the last shells (est.).
- The plan's wording "each shell starts where its voxel is ≤ 1 px" (rule B) and its "full detail 52-70 m" (rule A) disagree. Q2 settles it. Rule B at 720 would put the process back over 3 GB.
- Today's GPU mirror directory is 128 MB GPU + 128 MB managed (32×32×8 chunk slots × 16 KB, `TerrainClipmap.cs:226-241`). A 16³ mirror box halves it. Rule B's 32³ shell 0 would make it 1 GB, so rule B also forces a §3.7 layout change.
- Shells are NOT a frame-rate win (est. −1.6 to +0.4 ms near, −0.8 to +2.7 open). The ~19 / ~8 ms main march is untouched.
- Full detail keeps up to ~7-11 m/s at 720 sizing (est.); above that the coarser shell shows (Q2 (a) of the plan).
## Not done / next
- Nothing to build until the owner answers. Then step 10 (coarse-first generation) uses Q5's line; step 11 builds shells.
- Plan step 8 (one shadow-cost change) is still open from task 59's STOP.

## For the doc
**For the owner** (from the draft):
- What changes: today's 3 voxel tiers (voxels only to ~128 m, then the flat far band) become 8 nested cube shells, 0.1 m to 12.8 m voxels, out to 6.6 km, vertically too.
- What it buys: real 3D terrain to the horizon. The 0.1 m area shrinks from 128 m to ~80 m, saving ~470 MB (est.). A late chunk shows a coarser shell, not a hole.
- What it does not buy: frame rate. The main march (~19 / ~8 ms, task 57) stays; shells cost about the same as tiers + far band (−1.6 to +2.7 ms, est.).
- Costs: steps 10-14 as planned. Full detail keeps up only to ~7-11 m/s (est.); faster, the coarser shell shows briefly.
- Frozen and still frozen: GetVoxel, SetVoxel, the D.1 bytes, the §4.4 table. New beside them: an edit-op log file.
**Numbers:** terrain memory 1,322 → ~855 MB (est., `Logs/overnight/55_pool_ledger.out` as today); poses peak 3,077 → ~2,340 MB (est., task 53 + 55 + this); 0.1 m window radius 128 → 82 m; voxel at hand-over ≤ 2 px at 720 rows, ≤ 1.5 px at 540; upload at 5 m/s 0.23 MB/frame vs the 3 MiB cap.
**Questions:** Q1-Q6 exactly as at the top of this file.
**Images:** none. This is a planning task, and nothing visible changed.
