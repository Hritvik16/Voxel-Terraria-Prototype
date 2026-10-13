# 77-engine-11a-shell-storage

Approval: `ENGINE_PHASE_PLAN.md` (owner, Q36, 2026-10-09) step 11; Amendment 8.14 adopted 2026-10-11 (owner "Approve
all (a)", task 74 commit 9a859f1). Task 74's two questions: supervisor default **(a) and (a)** under the
recommended-option rule (owner may object; recorded in the Queue decisions doc), and Q1 (a) also follows the owner's
2026-10-11 rule "they should look the same regardless of what distance they're at". Queued by the supervisor
2026-10-11. Branch `engine-phase`.

## Change
1. **Task 74's answers, first commit:** amend `AMENDMENT_8_14_LOD_SHELLS.md` row 13 and `GenerateShellChunk`:
   - Q1 (a): a coarse voxel that is its column's top solid voxel takes the material of the fine voxel at the
     column's ground height, so distant land keeps its 0.3 m grass/sand/snow colour. Keep the test exact.
   - Q2 (a): sample fine index 2^(k-1)-1 per axis instead of 2^(k-1) (matches the cascades' tie-to-solid vote
     better: pooled shell 1 voxels 97.17 -> 98.49%, heights 98.97 -> 99.58%).
   Re-run 74's agreement report; top-voxel material vs vote must rise well above 74's 96/77/61/56% (shells 1-4).
2. **Engine step 11a: shell storage and upload**, exactly as `ENGINE_PHASE_PLAN.md` row 11 (part a) and the
   amendment's contract rows describe: runtime `ShellLayout.From(preset)` (High = the M1, pinned by every rig),
   pools/directories allocated at start-up, shells filled from `GenerateShellChunk` on the CPU and uploaded within
   the upload budget. Nothing new has to render yet (11b does raymarch + seams); if a debug view of shell occupancy
   is cheap, add it for screenshots.
- CPU sole terrain writer (GPU only stores shells); no D.1 / §4.4 / frozen API change; power-of-two ring sizes,
  bitwise integer coordinate math (the §6.2 phantom-terrain rule).
- If 11a does not fit one task, do the first useful half, commit, and say what remains.

## Acceptance
- Row 11's validator tests for storage/upload (stale slots 0, `ClipmapValidator`-style check per shell), memory per
  preset from the ledger vs the amendment's estimates (High ~855 MB terrain total).
- Fast EditMode green; `SUITE=all` (generator code touched). `run-phase7-rig.sh` and the acceptance rig's Gates C/D
  unchanged in result (rigs pin High; shells may be allocated but must not change what renders before 11b).
- No frame-time measurement. HANDOFF `## For the doc`: 74's re-run agreement numbers, memory per shell, any images.
