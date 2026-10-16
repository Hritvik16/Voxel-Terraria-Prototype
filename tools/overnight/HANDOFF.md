# 93e1-gameplay-rx1-adjacency-reactions: DONE -- four adjacency pairs in MaterialRules' table, CPU + GPU agree, 4 play shots

## What changed (commit 700560a, branch engine-phase)
- **Id count first (plan §6 risk):** engine ids 23-127 were all free (105 ids). Two new palette entries: `Materials.CooledStone = 23`
  and `Materials.Glass = 24`. Mud already exists (tg2 GenMat 23 = engine 151). No memory-layout change, no frozen API touched.
- `MaterialRules`: the RX1 pairs (each stored both ways by the new `Pair` helper):
  - water + lava -> **steam** (water cell) + **cooled stone** (lava cell). Before: air + obsidian.
  - water + loam (148) / red earth (152) -> the soil becomes **mud** (151), the water is kept.
  - lava + sand (4) / tg2 sand (140) / beach sand (142) -> the sand becomes **glass**, the lava is kept.
  - lava + snow (7) / tg2 snow (141) / packed ice (150) -> the snow becomes **water**, the lava is kept. The melt water then quenches the lava (pair 1).
- `FluidCA.compute` `CSReact`: a reaction op whose product is MOBILE now carries the wake bit. Without it, steam and melt water would sit without a slot on the GPU.
  The CPU reference already wakes these cells in CommitPass. Air/solid products carry no bit, as before, so acid and the old pairs behave the same.
- `FluidGpuSimulation.BuildReactionTables` is now a static function, so a test can check the GPU's 256x256 table. The upload is unchanged.
- Palette: CPU tables and both shader functions (`MaterialAlbedo`, `MaterialAlbedoLook`). Names added in `Items` and `Playground`.
- Old fluid rigs (FluidChaos, LateGameSiege, Phase5b/5c) now count cooled stone where they counted obsidian.
  Phase5c's floating count skips gases, because steam rises. None of these rigs was run.
- `-reactiondemo` (`Playground.ReactionDemo.cs`): four marble trays ahead of the play start, one per pair, with a 2 m viewing pillar. Noon, sun behind the camera.
  Each tray's materials are counted from ChunkStore after the GPU ops are applied. Vitals are off during the demo.

## Evidence
- `ReactionPairTests` (6, new): one test per pair on `FluidReferenceCPU`, with conservation checked every tick. Each also checks the GPU table gives the same products in both scan orders.
  Two more tests: the GPU table equals `TryGetReaction` for all 65,536 pairs, and the shader's wake rule.
  `FluidConservationTests`' two water+lava tests were updated to the new products (ledger: 1 consumed, was 2).
- Fast suite **PASS 785 FAIL 0 SKIP 14** (wall 4m04s). Nothing under `Assets/TerrainGen/` changed, so SUITE=all was not needed.
- GPU path in play (`Screenshots/93e1/reactiondemo_log.txt`); every count balances:
  - water + lava: 352 cooled stone = 352 steam; water 800 -> 448.
  - water + loam: mud 374 (= loam lost); water 144 kept.
  - sand + lava: glass 241 (= sand lost); lava 256 kept.
  - snow + lava: 134 snow melted -> 134 steam + 134 cooled stone; lava 256 -> 122.
- No frame-time measurement (feature task).

## Picks (owner may change)
1. "Stone" from water + lava is a NEW id, **cooled stone** (dark cool grey, 0.30). It is not legacy Stone (2), so product counts never mix with generated stone. Acid eats it like stone.
2. **Glass** is pale sea-green, OPAQUE (real transparency would be renderer work) and ACID-PROOF. It is now the acid container (obsidian was before).
3. Water + soil: the **water is kept** (no draining lakes). "dirt" = tg2 **loam + red earth**. Grass, hardpan and clay are not included.
4. Sand + lava and snow + lava: the **lava is kept**. A lava flow glasses or melts only what it touches. Melt water then quenches the lava (steam + crust).
5. Legacy Sand/Snow are included alongside the tg2 ones.

## Known limits (not fixed; RX1 is table rows only)
- **A slot can fall asleep before reacting.** The CA ages a slot on a LATERAL move, so lava can doze off on sand it just slid onto, without reacting until something wakes it.
  The CPU test found this, and the sand test does not assert "no contact at rest".
- **Three-way contact can differ between CPU and GPU.** Example: lava touching both water and sand in one tick. The CPU reserves the lava cell once; the GPU may apply both ops.
  Each side's result still conserves mass, and §7.8 only requires steady-state invariants.
- **Steam looks rough.** It draws as scattered floating grey cubes, some with black shadowed faces. That is today's steam look, not changed here.
- The `-reactiondemo` shots include the HUD, as `-vitalsdemo`'s do.

## Next step
The plan's run order: 5 (68) next. RX4 (life/growth) reads mud from RX1.

## For the doc
RX1 adjacency reactions are done: 4 pairs in MaterialRules' table; the CPU reference and the GPU CA agree; 2 new materials, `cooled stone` = 23 and `glass` = 24 (mud already existed, 151). Picks are listed above.
- water + lava, GPU in play: 352 cooled stone + 352 steam from 352 water (`Screenshots/93e1/reactiondemo_log.txt`).
- snow + lava: 134 melted -> 134 steam + 134 cooled stone (melt, then quench, exact).
- sand + lava: 241 glass; water + loam: 374 mud with all 144 water kept.
- Tests: ReactionPairTests 6 new; fast suite PASS 785 FAIL 0.
- `Screenshots/93e1/1_water_lava.png` -- centre tray: the orange lava floor; in the middle, the poured water (blue, dark cooled-stone crust under it); the grey steam cubes rise above.
- `Screenshots/93e1/2_water_loam.png` -- centre tray: the orange-brown loam floor; the dark purple-brown patch in the middle is mud under the shallow see-through water.
- `Screenshots/93e1/3_sand_lava.png` -- centre tray: the lava block's far half on the sand; the near half was cleared to show the pale green glass square beneath it.
- `Screenshots/93e1/4_snow_lava.png` -- centre tray: a lava blob on a snow floor; the dark ring round it is cooled stone where the melt water quenched it; the grey cubes above are steam.
