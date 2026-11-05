# 177-worldgen-c2c3-biome-cover-slots

Model: opus

Approval: owner, 2026-11-05, chat: "instead of saying grass or snow, can't we use materials from that biome? For example, if we wanted to bring
in a fire biome later, maybe the higher you go we might want it to turn into lava. The point is it might not always be snow." This merges
WORLD_GEN_LOOK_PLAN C2 (ground cover from shape) and C3 (high ground) into ONE general mechanism. WORLD GEN, look stage.
Read `WORLD_GEN_LOOK_PLAN.md` C2/C3, `tools/overnight/blocked/173-plan-world-gen-look.HANDOFF.md`, memories no-terrain-specific-engineering
and game-design-intent (2026-11-05 entries), KNOWN_ISSUES "FarField.GenToEngine is the chunk material table: any new GenMat needs a mapping".

## Design (owner's)
- Each biome owns a COVER PALETTE with slots: `flat`, `steep` (slopes, ridges, faces' top skin), `hollow` (dips, beside fresh water), `high`
  (high ground). Optionally a patch-variant per slot. The rules NEVER name a material ("grass", "snow"); they only choose the slot.
- Slot choice from the column's shape data that exists for every seed: slope, curvature (hollow / ridge), height above the local valley,
  freshwater distance, and for `high` an altitude that is a FRACTION of the world's own height range (scales with world size; no constant
  tuned to seed 42's 600 m peak). Drawn in coherent patches of metres, not per voxel (kills the camo speckle).
- Adding a biome = filling its palette row (data), no new rule code. Prove this: a test-only "fire" biome whose `high` slot is lava (or any
  existing hot material) appears correctly without touching the rules. Do NOT ship a fire biome.
- This replaces the 2026-10-01 "no snow lines" reading: height now picks the `high` SLOT; what fills it is the biome's choice.
- Biomes stay independent of topography (the biome map itself does not change here).

## Do
1. Implement the palette + slot rules in TerrainGen. Propose a first palette for the 6 biomes (e.g. snow: high = snow; forest: high = bare
   rock/scree; desert: high = pale rock; ...). Every material must map in `FarField.GenToEngine`; far band and shells must show the same cover.
2. Accept on 4 seeds: per-biome slot shares (no slot absent where its shape exists; no biome > ~85 % one slot), speckle metric down vs today.
3. Shots: 173's weak poses (`start_view`, `pair_desert_alpine-range`, `pair_forest_river-plains`, `feature_great-peak-0`, `range_200up`)
   before | after, full size, plus the island map coloured by slot.
4. WHOLE suite (`SUITE=all`), TerrainGen rule. Rough cost note from runs you already do; no timing arms.

## End
STOP_FOR_OWNER with a full-size viewer and the palette table (biome x slot -> material, with a swatch) as "change any cell?".
