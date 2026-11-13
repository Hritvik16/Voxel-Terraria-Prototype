# 192-look-trees-unique-open-crowns: DONE. Trees are now unique and their crowns are open: a skeleton (trunk, limbs, twigs) with leaf clumps and sky between them. Still behind `-trees 1` (default OFF). The owner picks a leaf fill: 30, 45 (proposed default) or 60.

## What to compare
Open `Screenshots/192/viewer.html`: Space cycles TODAY (180) / fill 30 / 45 / 60, the arrows change pose, S shows the contact sheets, Z shows full size.

## What changed (commit 36a9425, plus a commit for the logs and this handoff)
- `Assets/TerrainGen/Trees.cs`: the "3 families x jitter" design is gone. `Pick` draws every number of a tree from its cell hash, inside ranges that its biome and cover slot set:
  - age: saplings / mature / old;
  - size, crown base, crown radius, limb and twig counts, rise, droop, flatness;
  - lean, out over the foot's downhill side;
  - leaf shade: one per tree, chosen from the existing leaf GenMats (no new materials).
- `EmitLook` builds each tree:
  - forest: broadleaf (with conifers mixed in on high and steep ground);
  - jungle: emergents with buttresses, plus palms in hollows;
  - savanna: flat acacias, plus bottle-trunked giants;
  - snow: a range from spruce to pine (lobed whorls with a bark limb under each);
  - desert: cacti with 0-3 arms, or barrels.
- `MAX_PRIMS` 14 -> 28. Flags (TerrainSpec): `-leaffill N` (`treeLeafFill`) and `-treestyle 0` (`treeStyle180` = task 180's trees, byte for byte, diagnostic).
- Tests:
  - new `TreeTests.Look_FillSkyUniqueness_Sheets` (Slow) and `Poses_UnderCrown` (Explicit);
  - `Cost_OnVsOff` runs both looks;
  - `TreePoolTests` pose arms: OFF / ON-180 / ON-30 / ON-45 / ON-60 / ADV-x60;
  - the tree logs and sheets now go to `Logs/TerrainGen/trees192` and `Screenshots/192`.
- TASKS.md: task 192 paragraph.

## Evidence
- **Whole suite PASS 1060 FAIL 0 SKIP 28** (`Logs/overnight/192/all.out`).
- `FourSeeds_Rules_Sheets` passes on the new default: 4 seeds, 0 rejected (nothing floating, every trunk on the ground, crown < 2 % in rock, every voxel 6-connected) (`trees192/rules.txt`).
- The look test also checks every fill arm on seeds 42 and 7: 0 rejected.
- `Chunks_Deterministic_Border_PointAndShellsAgree` is green: the near chunk, FarQuery points and shells 1-3 agree byte for byte (`trees192/chunks.txt`).
- `-trees 0` is untouched: no trees are gathered.

| seed 42, 30 trees/biome (`trees192/look.txt`) | 180 today | fill 30 | fill 45 | fill 60 |
|---|---|---|---|---|
| leaf fill, forest / jungle / savanna / snow | 75 / 64 / 60 / 81 % | 34 / 32 / 32 / 32 % | 48 / 45 / 47 / 45 % | 59 / 57 / 61 / 52 % |
| sky through the crown, forest / savanna | 11 / 12 % | 19 / 30 % | 13 / 18 % | 12 / 14 % |
| "twin" IoU vs most similar of 12 neighbours, forest / snow (1 = clones) | 0.76 / 0.89 | 0.55 / 0.54 | 0.61 / 0.59 | 0.66 / 0.61 |
| leaf voxels per forest tree | 132 k | 32 k | 50 k | 70 k |

**Shell pools** (`trees192/pools_{forest,jungle}.txt`; bodies as % of PoolCap, worst shell):

| pose | 180 | fill 30 | fill 45 | fill 60 | adversarial at 60 |
|---|---|---|---|---|---|
| forest | 53 % | 53 % | 53 % | 53 % | 86 % |
| jungle | 68 % | 70 % | 71 % | 71 % | 92 % |

- Every arm fits, with 0 refusals in every launch.
- Full vs compact bricks (shell 1, from bricks - bodies): the forest compact share goes from ~60 % (180) to ~45 % (new), because twigs inside clumps make 3-value bricks. The net cost is +4.8 k bodies (forest s1) and +7.6 k (jungle s1).

**LOD 0** (`trees192/cost.txt`): new trees add fewer dense bricks in every biome. Forest +9,727 -> +8,009, jungle +2,448 -> +1,601. Generation time is the same (+12 % vs +13 %).

**Memory, jungle-only launches** (`Screenshots/192/lookreview_jungle_*.txt`): near-pool refusals 5,270 (180) vs 3,446 / 3,910 / 4,876; process peak 2,591 vs 2,511 / 2,514 / 2,589 MB. In the 6-pose launches the order differed and the peaks were 2,418 / 2,591 / 2,474 / 2,582 MB (noise). All are under 3,000.

## Pictures I looked at
- `forest_180.png` vs `forest_30.png`: today is a wall of solid blobs with black underneath. Fill 30 shows separate trees with visible trunks and limbs, clumped crowns, and sunlit ground with dappled shade.
- `forest_60.png`: the crowns close up and get darker; the trunks still show.
- `snow_45.png`: layered spruces of varied height and width, gaps between the whorls.
- `savanna_45.png`: flat-topped acacias in pads plus a bottle-trunked giant (front left); no two alike.
- `jungle_high_45.png` vs `jungle_high_180.png`: clumpy crowns in three greens with lit ground between them, vs today's flat umbrellas.
- **Rectangular orange/white boxes appear in BOTH** jungle_high shots. These are 191's near-pool "pits"; they existed before this task and are not caused by it.
- `under_crown_30.png`: looking up, sky shows between the clumps, a limb is visible, and the leaf undersides are black (that is task 193's lighting).
- Mean luminance, 180 -> 45: forest 57 -> 67, snow 92 -> 109, under the crown 11 -> 30.
- `jungle_30/45/60.png` are BLACK because 180's jungle eye sits inside a taller new crown. They are retaken as `jungle_high_*` (same target, eye 20 m higher).

## Not done / notes
- Crown width is a little narrower than 180's flat jungle umbrellas.
- No new leaf colours yet: one or two per biome via `palette_defs.py` would add variety. That needs 5 table edits, so it is a separate small task if wanted.
- Launches ran ~20 min each, over the 10-min budget: 6 poses, plus load time on a warm Mac.

## Next step
The owner picks the fill: (a) 30, (b) 45 (recommended), or (c) 60. Then task 193 (shade lighting).

## For the doc
- Leaf fill per forest crown: **75 % (today) -> 34 / 48 / 59 %** at `-leaffill 30 / 45 / 60` (`Logs/TerrainGen/trees192/look.txt`).
- Neighbour likeness ("twin" silhouette IoU): forest **0.76 -> 0.61**, snow **0.89 -> 0.59** at fill 45 (lower = more unique).
- Leaf voxels per forest tree: **132 k -> 50 k** at fill 45. Sky through a savanna crown: **12 % -> 18 %**.
- Worst shell pool with trees: jungle **68 % -> 71 %** of cap, adversarial 92 %, 0 refusals. LOD-0 dense bricks added by trees in the forest: **9,727 -> 8,009**.
- Whole suite PASS 1060 FAIL 0.

Images (open `Screenshots/192/viewer.html`; Space flips 180 / 30 / 45 / 60):
- `Screenshots/192/forest_180.png` vs `forest_30.png`: solid blobs over black ground become separate trees with trunks, limbs, clumps and sunlit gaps.
- `Screenshots/192/look_42_forest.png`: the same 12 neighbouring trees. Rows 1-2 are today (clone poplars and blobs); rows 3-8 are the new trees at fill 30 / 45 / 60, each tree different.
- `Screenshots/192/under_crown_45.png`: looking up under a forest tree, sky between the leaf clumps (the black undersides are for task 193).
