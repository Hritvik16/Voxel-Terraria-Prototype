# 180b-worldgen-material-rules-prototype

Model: opus
Requires: 179-worldgen-c5-rivers-meander-carve

Approval: owner, 2026-11-06 (chat, "yes" to a flag-off prototype so the owner can SEE it). The owner's direction (memory `simple-material-rules`):
the layered rock tables of 178/178c are too complicated and the world looks off. Wanted instead: a short rule list per biome over simple local
inputs; BIOME PAINTS EVERYTHING (skin, subsoil AND stone) -- the inputs only choose WHICH of the biome's materials goes WHERE; interaction of the
rules gives believable, emergent terrain; deterministic from the seed. NO 3-D noise (too slow). The world is "one wacky island with a realistic
twist": universal rules are physical and realistic; the per-biome palettes are where the wackiness lives.
Read: `Assets/TerrainGen/Cover.cs` (header: the slot rules ALREADY pick a ground-cover slot from shape; this task extends that idea DOWNWARD),
`DetailField.cs` (Strata/LayerRock/LayerMaterial/MixBrick, soil depth), `ShapeField.cs`, `Rivers.cs` (179), `WORLD_GEN_LOOK_PLAN.md`,
`DESIGN_LARGE_WORLD_AND_TERRAIN_OVERHAUL.md` section 2, KNOWN_ISSUES on the far band / shells, and the memory rule: rules read generator data,
never a seed constant, and are proven on an adversarial case.

## Design (the owner approved this shape; do not redesign it, flag a real conflict instead)
1. MATERIAL = f(column values, depth). Per-column values are computed ONCE per column from the map: slope, curvature (crest / dip, already in
   Cover), height over sea, sun-facing side (from the slope direction: shaded sides keep snow / moss, sunny sides show dry rock), distance to water
   AND height above that water's surface (a 3 m bank is not wet at the top: mud reaches ~1 m above the waterline), and ONE slow 2-D noise per
   biome. Per voxel only DEPTH BELOW THE COLUMN'S ORIGINAL SURFACE is new. No 3-D noise, no per-voxel search.
2. Each biome has ONE palette row: skin (depth < ~0.3 m), soil, subsoil, stone, deep stone, plus its slot entries (flat / steep / high / hollow /
   shore, as in Cover) and its own patch variant. Biome owns every output. Remove the dependence on LAYER_ROCKS, the partner-brick mix and the
   rock-biome A/B for the new path; they stay for the old path.
3. Scree / talus: flat ground with steep ground just upslope (max slope within ~20 m above) gets the biome's rubble entry. Bare rock on steep crests.
   Soil collects in dips. Snow line by height and shade. Beach = low and near the sea. Oasis / swamp / mud collar = flat or hollow + near fresh water.
4. Fluids: water uses the biome's wet-bank (hollow) entry; every OTHER fluid kind (lava, oil, acid, sludge, honey, quicksand, steam) owns its shore
   material (a small per-fluid table: e.g. lava -> basalt crust), NOT biome x fluid. Distance comes from a coarse map-baked field (one pass per map).
5. Priority (first match wins) must be WRITTEN in the file header and tested on CONFLICT PAIRS: steep + wet, high + shore, hollow + crest,
   scree + route corridor, river bank across a biome border (blend the bank material with the same smooth border warp as the rock biomes, so a mud
   bank never turns into a sand bank in one step).
6. EDITS: material comes from the ORIGINAL generated surface and the ORIGINAL water map; NEVER from live voxels or live fluid. A dug hole shows
   subsoil, then stone, then deep stone; a dammed river keeps its bed and banks; placed blocks are their own material. Chunk regeneration stays a pure
   function of seed + edit log. The CPU stays the sole terrain writer. Overhangs / arches come only from stamps: stamps carry their own materials
   (state this in the header). Runtime grass regrowth / wet-bank conversion is OUT of scope (gameplay phase); only write down in TASKS.md that it
   would be one deterministic tick re-applying the cover rule to exposed dirt.
7. Far band and shells use the SAME rules, band-limited exactly as Cover already is (no octave shorter than two footprints), so near and far agree.
   Ground cover / strata colours in the far band must follow the new path when the flag is on.

## Do
- Flag: `-matrules 1`, default OFF (spec field, read by `TerrainGenChunkSource` and `FarField` like `-rivershape`). OFF = bit-identical to today.
- Implement the rules above for ALL six biomes (forest, desert, snow, jungle, savanna, badlands). Badlands may keep its bands ONLY if they fall out of
  a depth rule in its palette, not from a hardcoded table (owner: no stripes until said otherwise -> default: no stripes).
- Ore is NOT part of this task (181 is separate); leave a note in the file header on where an ore rule would hook in (depth, biome, noise threshold).
- Tests (new file, e.g. `MaterialRuleTests`): determinism; an edit never changes the material beneath (dig a column, re-generate, compare);
  the conflict pairs above; 4 seeds + adversarial cases (a river crossing a biome border, an oasis, a tall cliff, a canyon floor, a lake outlet);
  near == far on the slot choice at texel <= 22; no new needles / holes (`StampTests.NoNeedles_OnTheMap`). Whole suite (generator change).
- A per-biome table of what the rules produced (share of each material by depth band and slot), so the owner can read the rules as numbers.
- Rough cost note from the task's own runs (chunk gen ms per chunk, ON vs OFF); no timing arms.
- Document in TASKS.md (feature note, flag, tests). Do NOT touch CLAUDE.md.

## Shots (full size, ON | OFF side by side, with a flip viewer like `Screenshots/178c/viewer.html`; midday, sun behind the camera)
- A tall cliff face in forest, desert and snow (shows the stone now painted by biome, the old stripes gone).
- A river bank (mud collar) and a river crossing a biome border.
- A beach and an oasis or swamp pose.
- A dug pit (cut-away) showing skin, soil, subsoil, stone from the original surface.
- A scree apron at a cliff foot.
One caption each naming what to look at.

## End
STOP_FOR_OWNER with the pictures: "keep the new material rules as the default?" Done otherwise. HANDOFF with the ON|OFF pictures and the per-biome table.
