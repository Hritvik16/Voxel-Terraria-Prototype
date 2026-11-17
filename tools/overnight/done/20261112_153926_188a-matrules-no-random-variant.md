# 188a-matrules-no-random-variant

Model: opus

Approval: owner, 2026-11-12 (chat): "I'm not a fan of [the variant covering 20-25 % of the ground] ... I like the specific rules that the variant forms in ... just randomly spraying it is weird ... it looks like blotches on the terrain rather than following any rules." Answer (a): REMOVE the random spray, materials by the rules only. Memory simple-material-rules; CLAUDE.md "no terrain-specific engineering".

## What is there today
`GroundRules.Column` (Assets/TerrainGen/GroundRules.cs ~line 434): after `ChooseSlot` picks the slot from the rules, `variant = Cover.VariantNoise(...) > Cover.VARIANT_ABOVE - 0.15 * BiomeNoise` swaps the slot to the palette row's VARIANT entry on ~20-25 % of every slot (flat, steep, hollow, high, shore, rubble, dip), from a 150 m + 45 m 2-D noise that ignores slope / water / shade. Examples the owner saw: PackedIce patches on flat snow (seed 42 frontier, `Screenshots/187/eye_frontier.png`), JungleGrass patches in forest grass (`Screenshots/187/eye_town.png`). Rule-made materials (shaded-steep ice, frozen / mud banks, scree, bare sunny faces) are slot MAIN entries and must stay.

## Do
- Behind a flag `-matvariant 0|1` (TerrainSpec field, same pattern as `matRulesOff`): 0 = no random variant (every column takes its slot's MAIN entry); 1 = today, byte for byte. DEFAULT stays today (1) in this task; the owner picks the default from pictures.
- Check every place the variant bit is read: the material rules path, `c.cover` (trees read the Cover slot variant bit -- with `-trees 1` say what changes), FarField / far band TopMaterial, shells. Near == far must hold in both settings.
- If a palette variant entry is the ONLY place a material appears (so removing the spray would erase it from a biome), list those in the HANDOFF; do not invent new rules for them (the owner decides later).
- Leave the RENDERER's colour jitter / warm-cool tint / per-biome tint alone (different thing; owner did not ask).
- Tests: `-matvariant 1` byte-identical to today (height + material FNV); `-matvariant 0` has zero variant columns; rule slots unchanged. Fast suite; `SUITE=all` once at the end (generator change). TASKS.md paragraph. No timing arms.
- Pictures, OFF vs ON, same pose, midday sun behind camera, full size, captions say what to notice: the seed 42 frontier snowfield (187's pose), the seed 42 town plateau (187's pose), a desert flat, a savanna flat, a forest slope with a river bank (to show the rule-made mud/ice/gravel stay). Plus share of ground per material per biome, before -> after.

## End
Done; default unchanged; the owner picks the default from the pictures. HANDOFF with the table, image paths, and any material that disappears from a biome.
