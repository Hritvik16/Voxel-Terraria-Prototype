# 74-engine-10-coarse-first

Requires: 73-rivers-waterfalls-plan

Approval: owner, 2026-10-11 ~08:40 in chat: Q43 "Approve all (a)" -- Amendment 8.14 as drafted (Q7-Q11 all (a);
earlier Q1-Q6 answers already recorded in the draft). `ENGINE_PHASE_PLAN.md` approved 2026-10-09 (Q36). Queued by
the supervisor 2026-10-11. Branch `engine-phase`.
(`Requires:` 73 only to keep one task at a time in order; 74 does not depend on 73's content. If 73 blocks for the
owner, run 74 anyway.)

## Change
1. **First commit: adopt the amendment.** Rename `AMENDMENT_8_14_LOD_SHELLS_DRAFT.md` -> `AMENDMENT_8_14_LOD_SHELLS.md`,
   mark it ADOPTED 2026-10-11 with the owner's answers (Q1-Q11) in its header, and add 8.14 to CLAUDE.md's
   "Ground truth, in order" list after 8.12. No code in this commit.
2. **Engine step 10, coarse-first generation**, exactly as `ENGINE_PHASE_PLAN.md` row 10 and the amendment's
   contract rows describe: generate a chunk directly at 0.2-12.8 m voxels from the world map on the one Burst path
   (step 1). Nothing renders from it yet (that is step 11); this step is the generator + tests.
- CPU stays the sole terrain writer; no change to D.1, §4.4, `IWorldQuery.GetVoxel`, `IEditService.SetVoxel`.
  Bitwise integer coordinate math; power-of-two sizes.

## Acceptance
- Row 10's own acceptance: solid/air agreement with the downsampled 0.1 m chunk >= the threshold the amendment
  sets (if it sets none, pick and justify one in HANDOFF), as an EditMode test over several seeds/landforms.
- Side-by-side renders (coarse vs downsampled 0.1 m) for 2-3 places, captioned.
- `SUITE=all` green (this touches `Assets/TerrainGen/`-adjacent generation). No frame-time measurement.
- HANDOFF `## For the doc`: agreement numbers per level, generation ms per coarse chunk from the test (not a rig), images.
