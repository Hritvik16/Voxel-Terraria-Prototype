# 105-gameplay-step-10-tool-tiers: DONE -- Survival digs with a held PICK; harder rock digs slower; ores need a tier (copper pick REFUSED at iron)

## What changed (commit 5a8ce20)
- `Assets/ContentModules/Tools.cs` (NEW, a content table, guardrail 8): hardness 0-5 for every material id, a required tier (only
  ores, meteor ore, bedrock), 6 picks. Cost per voxel = 1 + max(0, hardness - tier + 1) work units of the pick's rate; 0 = too hard.
- `EditService.MineSphereCosted` (ADDED; nothing existing changed): nearest-first sphere dig that pays per voxel, skips too-hard
  voxels (counted), stops at a voxel it cannot afford yet. `SetCubes` gained an overload with a `keep` mask (the old one forwards).
- `SurvivalBuild`: `PickTables`, `DigWithPick` (aimed voxel too hard -> refused, nothing written); `DigStroke`/`DigPull`/`Cost`/
  `DiggableCells` take the optional keep mask (strokes stay instant, only the refusal applies).
- `Inventory.pick` (player.json field; an older file -> 0 = stone pick; out of range clamps). Format version unchanged.
- `Playground`: Survival LMB = `SurvivalPickDig`; Z/X in Survival switch the pick (status says what it digs); dig box shows
  "pick · tier N" and TOO HARD; F2 help line + panel 512 -> 530 px. Creative and every rig keep hand/drill/bore (untouched).
- `Playground.ToolDemo.cs` (NEW, `EXTRA="-tooldemo"`). `GAMEPLAY_PHASE_PLAN.md` (step 10 Done row, pick P30), `CLAUDE.md` line.

## Evidence
- `ToolTierTests` 10/10: the rate table (pinned literally), the ladder, only ores/bedrock refused, refusal writes nothing,
  gold pick digs iron at cost 2, mixed sphere leaves the ore, work conserved over frames, strokes keep too-hard cells + preview
  matches, player.json round trip. Fast suite `PASS 881 FAIL 0 SKIP 14` (`Logs/overnight/105-fast.out`). No `Assets/TerrainGen/` change.
- No frame-time measurement (rule 10); memory: two 256-byte tables. One launch, `BUILD=1 EXTRA="-tooldemo" ./run-play.sh` (~6 min): log `Logs/overnight/105-player.log`, summary `Screenshots/105/105.txt`.
  Iron deposit found 1 m from the play start, 91.7 m below sea (119 m under the top), viewing room carved in front of it.
  Copper pick: refused, 0 removed, iron 331/331 left. Gold pick: 552 voxels (331 iron + 221 slate) in 9.8 s, iron left 0, 331 in the hotbar.

## Picks (owner may change) -- also P30 in the plan
1. **Ladder: each pick digs the NEXT ore.** stone (starter) -> copper; copper -> gold, NOT iron; gold -> iron; iron -> cobalt;
   cobalt -> emberstone + meteor ore; bedrock never. Why gold before iron: the acceptance wants copper refused at iron, and crafting
   must still climb (iron cannot need a pick made of iron). Gold is the softer metal and lies deeper (player passes iron to reach gold). Alt: Terraria's copper->iron, but no refusal.
2. **Rock is never refused, only slowed** (granite/basalt/obsidian x2-x4 slower with the starter). Only ores/bedrock refuse.
3. **Aimed at a too-hard voxel = nothing dug** (Terraria-like); aimed at rock beside it = the rock goes, the ore stays.
4. **P11 wins over step 10's first text:** Z/X dev tiers do NOT retire in Survival; they switch picks until crafting (step 11).
5. **The pick is gear, not a slot item** (`Inventory.pick`); step 11 decides how picks become items/recipes.
6. Starter = old Survival "drill" on soil (40 vox/s, r2); rates 40/60/90/130/180/250, radius 2,2,2,2,3,3.
7. Dig strokes and face pulls stay instant (BLD1/BLD2 as before); they only skip too-hard cells.

## Left / next
- Demos older than 105 (`-oredemo`, `-survivaldemo`) use the old fixed-budget `SurvivalDig`, which ignores the pick (on purpose; noted in code).
- Next: step 11 (crafting v1: pick recipes per ore tier) per the queue.

## For the doc
**Result:** Survival now digs with a pick. Harder rock digs slower, and each ore needs a good enough pick. A copper pick is
refused at iron ("TOO HARD: iron ore needs a gold pick (tier 3) -- you hold a copper pick (tier 2)"); a gold pick digs the same vein.
**In `./run-play.sh`:** you start with a stone pick; LMB digs; **Z/X switch picks** (a dev key until crafting); F2 lists the ladder.

| pick (tier) | vox/s on soil | limestone | granite | obsidian | ores it digs |
| --- | --- | --- | --- | --- | --- |
| stone (1), starter | 40 | 20 | 13 | 10 | copper |
| copper (2) | 60 | 60 | 30 | 20 | + gold (not iron) |
| gold (3) | 90 | 90 | 90 | 45 | + iron |
| iron (4) | 130 | 130 | 130 | 130 | + cobalt |
| cobalt (5) | 180 | 180 | 180 | 180 | + emberstone, meteor ore |
| emberstone (6) | 250 | 250 | 250 | 250 | everything but bedrock |

- Iron vein, copper pick: 0 of 331 iron voxels dug (refused); gold pick: 331 of 331 in 9.8 s (`Screenshots/105/105.txt`).
- Tests: `ToolTierTests` 10 new; fast suite 881 pass, 0 fail.

Images (underground, torch-lit, 92 m below sea; there is no sun down there):
- `Screenshots/105/1_iron_vein_copper_pick_refused.png` -- top-left status line in red "TOO HARD: iron ore needs a gold pick";
  bottom-left dig box "TOO HARD, copper pick · tier 2"; the orange voxels left of the crosshair are the iron vein, untouched.
- `Screenshots/105/2_gold_pick_digging.png` -- same spot with the gold pick: dark holes eaten into the wall with orange iron still at
  their edges; hotbar slot 3 = iron ore 167, status "+3 iron ore (now 167) gold pick".
- `Screenshots/105/3_gold_pick_vein_dug.png` -- the vein gone: a dark hole at the crosshair; hotbar iron ore 331, slate 221.
