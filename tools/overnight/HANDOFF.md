STOP_FOR_OWNER -- task 177: "Change any palette cell?" Viewer (full size): `Screenshots/177/viewer.html`. Keys: arrows = image, Space = BEFORE|AFTER flip, 1-4 = zoom.
The rules only pick a SLOT. The table below is all the per-biome data (a fire biome = one more row). Each cell is main / patch variant (~20-25 % of the slot). "bare" = no skin, the biome's own rock shows.

| biome | flat | steep | hollow | high | shore |
|---|---|---|---|---|---|
| forest | grass / jungle grass | bare: granite / gravel | gravel / loam | gravel / bare: granite | beach sand / gravel |
| desert | sand / packed sand | bare: orange sandstone / cream sandstone | packed sand / gravel | cream sandstone / bare | beach sand |
| snow | snow / packed ice | packed ice / bare: silver schist | packed ice / snow | snow / packed ice | snow / gravel |
| jungle | jungle grass / grass | bare: karst limestone / moss rock | mud / jungle grass | moss rock / bare | beach sand |
| savanna | savanna grass / red earth | bare: kopje granite / gravel | grass / red earth | bare: kopje granite / gravel | beach sand |
| badlands | badlands clay / hardpan | bare: the bands / hardpan | hardpan / gravel | cream band / bare: bands | beach sand |

Swatches: `Screenshots/177/palette177.png` (rows = biomes, columns = slots, each cell main | variant; hatched = bare rock).
- **Q1. Change any cell?** My suggestions:
  - (a) desert steep variant: cream sandstone -> none. On `great-peak-0` AFTER, the far-left desert peak shows cream blotches on orange; at 1-2 km they read a little like camo.
  - (b) savanna flat variant: red earth -> none or subtler. On `forest_river-plains` AFTER, the red-brown patches are strong.
  - Everything else as proposed (gravel 17, `8e877c`, is new).
- **Q2. Where does "high" start?** (a) **Recommended, shipped:** 2/3 of the height range (the record's top third), 0.6-1.3 % of land. (b) 1/2: 0.9-1.9 %. (c) 1/3: 2.0-3.4 %. High ground keeps its cover up to 45 deg faces.
- **Q3. Snow fleck:** snow settles to packed ice on voxel steps over 1, a white/blue fleck on snowy slopes (`desert_alpine-range` AFTER). (a) **Recommended:** keep it (the falling-solid contract, as for sand). (b) Let snow lie on steps; the engine keeps snow static anyway.

# 177-worldgen-c2c3-biome-cover-slots: cover = shape slot x biome palette; speckle down 7-21x on 4 seeds; whole suite green except the known route red

## What changed (`c89f7de`)
- `Assets/TerrainGen/Cover.cs`:
  - Slot rules: shore, then hollow (lake/river bed), then steep (6 m slope over 30 deg; on high ground only faces over 45 deg), then high (over 2/3 of `spec.peakHeight`), then a crest = steep, then hollow (a dip, or within ~16 m of fresh water), else flat.
  - Every input is shape data that exists for every seed. One 6 m + 25 m patch noise jitters every cut.
  - The cover noises are band-limited to the footprint (chunk 1, shell 2^k, far texel), so far texels do not alias into camo.
- `PALETTE` (6 x 5 x 2) is the only per-biome data. `DetailField`: skin = the cell (sand/snow settled per voxel), soil under it except on steep; rock where BARE or the voxel step > 3 (before: every step > 1.5 = the speckle).
- `-coverslots 0` / `coverSlotsOff` gives the pre-177 surface, bit for bit. v3 is unchanged.
- Tests: ShellFill GOLDEN re-made (67 of 85 chunks changed); `ShellChunkTests`' reference samples the cover at the shell's footprint;
  `OreTests` ring gate held at its absolute bound (8.9 bricks/chunk): the cover cut the base 28 % (5,677 -> 4,093), ore adds +215 both ways (measured).
- `tools/cover177.py`: maps, palette table, before|after pairs, viewer. TASKS.md has the 177 paragraph.

## Evidence
- Whole suite: **PASS 1061 FAIL 1 SKIP 32** (`Logs/overnight/177_all2.out`). The red is the KNOWN route test (river ford bank, until C5).
- `CoverTests` (`Screenshots/177/shares177.txt`): a test-only "fire" row gets magma crust on 206 high columns of seed 42 with no rule change;
  TopMaterial = the row lookup; same slot at footprints <= 1.6 m; every slot on every seed; no biome over 65.5 % one slot (gate 85 %).
- Speckle (isolated top voxels): 1.87 -> 0.09 / 2.00 -> 0.13 / 1.03 -> 0.06 / 0.50 -> 0.07 % (seeds 42 / 7 / 2026 / 31337).
- Island slot shares: flat 53-59, steep 26-32, hollow 7-8, high 0.6-1.2, shore 6-7 % (`map_slot_shares.txt`).
- Shots (173's 5 weak poses, `-wsfixed`): AFTER `Logs/lookreview_20261106_012752.txt`, BEFORE (`-coverslots 0`) `Logs/lookreview_20261106_002547.txt`.
  I looked at all of them. The speckle is gone and the cover follows the ground: grass along the river, rock and ice outcrops on the mountain, badlands stripes kept.
- Rough cost (no timing arms): `Sample` (Mono) 36.2 -> 43.5 us per column (`cost177.txt`); shell chunk 81 -> 88-95 ms in the shot launches.

## Not done / notes
- The far band's `FarWallMaterial` still maps walls by surface material (snow high ground in a forest -> a schist wall; lip mode only, rare).
- Shells of 3.2 m+ show the band-limited cover (12-30 % of columns differ from texel 1, by design). 176c (biome size 2) is next; shares may shift.

## For the doc
- Speckle (isolated top voxels, 40 land windows per seed): 1.87 -> 0.09 %, 2.00 -> 0.13 %, 1.03 -> 0.06 %, 0.50 -> 0.07 % (`Screenshots/177/shares177.txt`).
- Island land by slot: flat 53-59 %, steep 26-32 %, hollow 7-8 %, high 0.6-1.2 %, shore 6-7 %. No biome over 65.5 % in one slot.
- Mixed bricks on OreTests' ring set: 5,677 -> 4,093 (-28 %), a memory hint, not a measurement.
- Column cost: 36.2 -> 43.5 us (Mono); shell chunk 81 -> 88-95 ms in the shot launches (rough).
- Images:
  - `Screenshots/177/shot_start_view_after.png` / `_before.png`: Space flips. Look at the snow mountain top right: grey/white per-voxel noise BEFORE, blue ice with rock outcrops AFTER. Bottom: badlands stripes kept, hardpan patches added.
  - `Screenshots/177/shot_forest_river-plains_after.png` / `_before.png`: look at the river. A green grass band (the hollow slot) runs along it AFTER; BEFORE it is a flat golden lawn.
  - `Screenshots/177/map_seed42.png`: left panel = slots (dark grey steep on ridges and the central massif, blue hollows along rivers, white high spots on the summit). Middle = AFTER cover; right = BEFORE.
