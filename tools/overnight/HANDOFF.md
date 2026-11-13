# 188a-matrules-no-random-variant: DONE. `-matvariant 0` removes the random variant spray (materials by the rules only). The DEFAULT is unchanged (`-matvariant 1` = today, byte for byte). The owner picks the default from the pictures.

## What changed (commit 1031594)
- `TerrainSpec.matVariantOff`, read through `MatVariant`. `GroundRules.Column`: `variant = noisePatch && spec.MatVariant`. That is the one place the switch happens, so near voxels, `TopMaterial` (far band, minimap) and shells all agree.
- The `-matvariant` arg: `TerrainGenChunkSource.MatVariantArg`, and `FarField`'s own spec, the same way as `-matrules`.
- **Trees are unchanged in both settings.** `c.cover` keeps the noise patch's VARIANT bit, so tree-density patches (`Trees.VARIANT_MUL`) are identical. With `-trees 1` nothing changes for trees.
- The old `-matrules 0` path ignores the flag.
- Where a variant entry was BARE (the high slot in forest / desert / jungle / badlands, snow's steep slot), those patches no longer show bare rock. The bare / sunny / crest rule still makes bare rock.
- Tests: new `MatVariantTests` (TASKS.md 188a paragraph).

## Evidence
- `-matvariant 1` == before. An FNV over height, slot, cover, soil depths, top material and the top 4.8 m of Material (600 columns each, seeds 42 and 7) gives 0x6f6c8910 / 0xeaaad676. Those values were pinned on the pre-change code (`Logs/overnight/188a/pin_before.out`) and match.
- `-matvariant 0`:
  - 0 variant columns (22.4 % with the default, 3,000 land columns).
  - The same rule slot, cover, height, water and biome.
  - The skin is the slot's MAIN entry.
  - Near == far slots at texel 4 / 16.
- Whole suite **PASS 1041 FAIL 0 SKIP 27** (`Logs/overnight/188a/all1.out`); 188 had 1037, and this task adds 4 tests.
- Shots: one build, two launches, 45 s wait, noon (sun 78 deg) (`Logs/overnight/188a/shots.out`, `Logs/overnight/188a_shots.sh`). At 5 of the 6 poses shell 1 is filled or nearly (missing 0-44 per pose); shell 2 is partial and shells 3+ are unfilled.
- One fill difference: at savanna_flat the AFTER launch had 292 shell-1 chunks missing (BEFORE: 26). So the stamp arch at the right edge is drawn as a far-band slab there. That is streaming timing, not this change (heights are proven identical).

## Materials that leave a biome's SURFACE with the spray off (owner decides; no new rules invented)
| biome | lost from the surface | where it came from | still underground? |
|---|---|---|---|
| forest | JungleGrass (12.2 % -> 0) | flat variant | no |
| desert | Mud (0.8 % -> 0) | wet-bank variant (the oasis is Grass) | no |
| jungle | Grass (15.4 % -> 0) | flat variant | no |
| jungle | Gravel (0.3 % -> 0) | scree variant | no |
| savanna | PackedSand (12.8 % -> 0) | flat variant | no |
| savanna | Mud (0.6 % -> 0) | wet-bank variant (the banks are Grass) | no |
| forest | Loam | wet-bank variant | yes: the soil |
| desert | PackedSand (13.7 -> 0.2 %) | flat / scree variant | yes: the soil |
| savanna | RedEarth | scree variant | yes: the soil |
| badlands | OchreBand | scree variant | yes: the subsoil |

Snow loses nothing: PackedIce stays the rule-made ice on shaded steeps and frozen banks (25.7 -> 15.5 %).

## Next step
The owner looks at the viewer and picks the default. If they choose (a), flip it: default `matVariantOff = 1`, and re-pin `MatVariantTests` to the new arm. Optionally they may want rules for the lost materials (e.g. savanna PackedSand on dry flats); that is a design decision.

## For the doc
Share of the surface per material, before -> after (4 seeds, 3,000 land columns each; `Logs/TerrainGen/188a/share188a.txt`):

| biome | main ground | the sprayed material |
|---|---|---|
| forest | Grass 41.3 -> 53.4 % | JungleGrass 12.2 -> 0 % |
| desert | Sand 39.9 -> 52.7 % | PackedSand 13.7 -> 0.2 % |
| snow | Snow 45.1 -> 59.5 % | PackedIce 25.7 -> 15.5 % (the rule-made ice stays) |
| jungle | JungleGrass 47.2 -> 62.1 % | Grass 15.4 -> 0 % |
| savanna | SavannaGrass 44.6 -> 57.4 % | PackedSand 12.8 -> 0 % |
| badlands | BadlandsClay 46.1 -> 60.8 % | Hardpan 18.8 -> 1.6 % (stays as rubble) |

Variant columns: 22.4 % -> 0 %. Heights and slots are unchanged (FNV pin).

Images (full size; open `Screenshots/188a/viewer.html`: Space flips, arrows change pose, Z zooms). Each `pair_*.png` has BEFORE on top and AFTER below:
- `Screenshots/188a/pair_site_frontier.png`: in the foreground of the seed 42 frontier snowfield, the pale-blue ice blotches go and the snow is uniform. The cliff and far ground are unchanged.
- `Screenshots/188a/pair_site_town.png`: on the town plateau, the bright JungleGrass patches in the lower-left grass go. The mountains behind are unchanged.
- `Screenshots/188a/pair_desert_flat.png` and `pair_savanna_flat.png`: the dark PackedSand blotches go from the sand and the golden grass. In savanna, the river's beach sand and the green bank by the water stay (rule-made).
- `Screenshots/188a/pair_forest_bank_a.png` and `pair_forest_bank_b.png`: the brown mud collars along the river stay, and so does the pale ice on the shaded snowy slope (bank_a, upper left). Only the JungleGrass patches in the forest grass (and small ice patches on flat snow, bank_a bottom right) go.
