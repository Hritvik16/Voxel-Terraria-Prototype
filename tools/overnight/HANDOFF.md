# 64-gameplay-step-1: DONE. Survival dig -> inventory -> build from a hotbar works in play; 17 new tests green.

## What changed (branch `engine-phase`, commits 5ba06e8, 1462477)
- `Assets/ContentModules/Items.cs` (new): item = material id; tg2 grass/jungle grass/savanna grass -> loam/mud/red
  earth; fluids give nothing (P9); torches place 1 voxel; names for every legacy + tg2 material; starter kit 50 torches.
- `Assets/CoreEngine/Gameplay/Inventory.cs` (new): 36 slots (9 hotbar + 27), cap 10,000 voxels, stacks-first add,
  all-or-nothing take, swap/merge, JSON with repair of bad slots.
- `EditService.MineSphereBudgeted(centre, radius, budget, int[] removedByMaterial)`: NEW overload; old one unchanged.
- `Assets/CoreEngine/Gameplay/SurvivalBuild.cs` (new): Dig (credits inventory, reports lost overflow) and Place
  (cube at `at & ~(edge-1)`, fills only Air/fluid cells via `PlacePrefab`, cost = cells written).
- `PlaySettings.gameMode` + `-gamemode survival|creative`; F6 menu row "Game mode" (menu title now SETTINGS).
- `Playground.cs`: Survival hotbar strip, E bag panel, Q size, cyan cube preview, player.json beside the deltas
  (fresh unless `-keepdeltas`/KEEP=1, saved every 30 s + on quit), refusals (encloses you / wake radius / too few),
  F2 help, `-survivaldemo` (scripted screenshots). `PlaygroundFlyCamera` frees the mouse while the bag is open.
- `run-play.sh` key list, `CLAUDE.md` one paragraph. No frozen API, memory layout or TerrainGen change.

## Evidence
- Fast EditMode: `PASS 666 FAIL 0 SKIP 14` (Logs/overnight/64-fast2.out, after the last code change). New
  `SurvivalBuildTests` (17): inventory add/stack/cap/full/take/swap, JSON round-trip + repair, item table (codes
  pinned vs GenMat), overload counts, dig N -> +N (grass -> loam), fluid -> nothing, full bag -> lost, place in air
  costs 64, place over grass costs 48 and dig-back restores 500, refuse short/empty/fluid writes nothing, torch = 1
  voxel, snap = integer floor for negatives, -gamemode precedence, Creative default + brush slots 0-3 (reflection).
- Play run (BUILD=1, `-gamemode survival -survivaldemo`; Logs/overnight/64-demo-player.log): dug 12,239 voxels ->
  credited 12,239, lost 0; wall 24 cubes = 1,493 voxels placed, inventory 12,289 -> 10,796 ("conservation OK").
- No frame-time measurement (owner rule). Memory: the inventory is 36 ints; no engine allocation added.

## Picks (owner may change)
- E/Q act only in WALK mode; in fly they stay the flycam's up/down (fly is a dev mode, P11).
- RMB places one cube per click, repeating every 0.22 s while held (Creative still paints every frame).
- A cube fills Air AND fluid cells (replaces water, as Creative does); solid cells inside it are skipped and free.
- Placement is taken from the selected slot only (not other stacks of the same item).
- Refused if the cube overlaps your body; mobile blocks (sand, beach sand) keep the wake-radius guard.
- Starter torches go in hotbar slot 1; dug blocks fill slots 2+ in dig order.
- Dig rate/radius = the existing Z/X tool tiers (hand/drill/bore); bedrock is diggable (hardness is step 10).
- F6 menu: "Game mode" sits on top, above a GRAPHICS heading.

## Unfinished / notes
- `Builds/Playground.app` was built one cosmetic commit early (1462477 hides the crosshair box under the bag):
  `BUILD=1 ./run-play.sh` once.
- Dev keys (V vents, bombs, G chaos) still use Creative's brush list in Survival (P11), never the inventory.

## Next step
- Task 65 = plan step 2 (movement set A: swim/dive, double jump, dash).

## For the doc
Task 64: gameplay step 1. In `./run-play.sh` you now start in **Survival**: LMB digs and every voxel goes into
your inventory (grass gives loam), RMB places a cube of the held block (cyan box shows where), **Q** cube size
0.1/0.2/0.4/0.8 m, **E** opens the 36-slot bag (click two slots to swap), **1-9**/wheel pick the hotbar slot;
**F6 -> Game mode** (or `-gamemode creative`) brings back the old brushes. KEEP=1 keeps the bag with the world.
- Dig conservation: 12,239 voxels dug -> 12,239 credited, 0 lost (Logs/overnight/64-demo-player.log).
- Build conservation: 24 cubes placed 1,493 voxels -> bag 12,289 -> 10,796 (-1,493).
- Tests: fast suite 666 passing, 0 failing, incl. 17 new (Logs/overnight/64-fast2.out).
- Picks: listed above (E/Q walk-only, 0.22 s place repeat, fluid cells fillable, selected slot only).
- `Screenshots/64/1_hotbar_after_dig.png` -- centre: the black doorway just dug into a sunlit sandstone hillside;
  bottom: the hotbar with torch 50 / sand / packed sand / orange sandstone and their voxel counts.
- `Screenshots/64/2_inventory_panel.png` -- centre panel: the E bag, 27 empty backpack slots over the hotbar row,
  four items, and the totals line under it (orange sandstone 9,461, packed sand 2,415, sand 363, torch 50).
- `Screenshots/64/3_wall_beside_hole.png` -- centre: the flat pale packed-sand wall (6 x 4 cubes of 0.4 m) built
  from dug blocks; left of it the black doorway it came from; status line top-left "placed 64 packed sand".
