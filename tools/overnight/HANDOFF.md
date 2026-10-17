# 94 fluid gravity fall (8.16): DONE. Every fluid now falls at g up to 12.15 m/s, the same for all; viscosity only gates sideways moves

## What changed (commit 92e8fde, engine-phase)
- `AMENDMENT_8_16_FLUID_FALL.md` (DESIGN CHANGE, owner-approved 2026-10-15), added to CLAUDE.md's amendment order + a task-94 note.
- `FluidFall.cs` (new): ONE integer table, Table[k] = whole voxels fallen after k ticks (the exact g t^2/2, capped, floored). The cap is 12 m/s
  rounded to whole voxels per tick: 27 voxels/tick = **12.15 m/s** at 4.5 Hz, reached after 1.24 s / 7.5 m. Never slower than the old 1 voxel/tick.
- `FluidReferenceCPU` + `FluidCA.compute` CSIntent: a fluid (not steam, not sand) with air below SKIPS the viscosity gate and makes ONE ordinary
  claim Step(k) voxels down. It stops above the first cell that was not air at tick start (= landing, k = 0). A missing tile stops it without a landing.
  Steam below = an ungated swap. CSSweep resets k on a lost claim. **State: k in GPU `stateFlags` bits [15:8]** (spare bits in the slot's existing
  uint lane; still 24 B). The CPU oracle keeps it in a side array, because A.5's byte layout has no spare byte. No struct, D.1 or API change.
- Conservation: the fall is a normal §7.3 claim (air-only, plain store, destination-driven commit, one bid per slot), so no new collision rule
  was needed. Sand (`IsFallingSolid`) and steam are unchanged: the fall branch is fluid-only, and sand's falls stay 1 voxel/tick.
- `FluidGpuSimulation`: `GravityFall`, `FallTickSeconds` (Playground: the scheduler's period), `NoteFallAhead`. A fast-fall op pulls in the tile
  its next tick reaches (AwakeOnly); without it a fall stalls at every 32-voxel tile face. `FluidOpListReadback.FastFallOpsApplied`.
- Playground `-fluidfall 0` = v8.6 rule. Capture `-fallstrip` (`PlaygroundCapture.FallStrip.cs`).
- Tests: new `FluidGravityFallTests` (8). Five old tests pin the v8.6 FALL with `GravityFall = false`: GravityOnly_SingleDrop, MineAFallingDrop,
  Viscosity_LavaEvery6/Honey30, Viscosity_DoesNotAge, Lava_QuietWindow. Also the 8x8 tie-break experiment: its 30-drop pour now settles to one
  flat layer for every salt, but its rig-scale control still differs under 8.16. ReactionPairTests water-on-soil: I dropped the "no water touches
  soil at rest" line. It held only for the old pour's path; a drop now sleeps right after a lateral move, the sand test's documented caveat.

## Evidence
- Own filter run 53/53 (FluidGravityFall, FluidConservation, FluidAtRest, FluidSlowViscositySettle, FluidTieBreakVariance, FluidSlopedTerrain).
  **Fast suite PASS 830 FAIL 0 SKIP 14** (`Logs/overnight/94-fast.out`).
- CPU fall, every fluid the same (water, lava, honey, oil, acid, sludge, quicksand), within 1 voxel of the integrated fall at every tick to 2 s:
  +0.44 s 0.90 m (exact 0.97), +1.11 s 6.00 m (6.06), +2.00 s 16.70 m (16.78). Never passes a 1-voxel slab at 28 offsets. Cap holds (27/tick).
- GPU (EditMode is CPU-only, so this is the GPU check): one strip launch, `Logs/tg2world_20261017_050009.txt`, `Screenshots/94/94-fallstrip.txt`.
  AFTER: water, lava and honey fronts are IDENTICAL at every shot (0.9 / 3.8 / 7.9 m at +0.5 / +1 / +2 s, landed by +2 s). They match the
  table, with one tick of wake latency. BEFORE: water 0.2 / 0.3 / 0.8 / 1.7 m, lava 0 / 0 / 0.1 / 0.2 m, honey 0 at +0.5 / +1 / +2 / +4 s.
- `run-fluid-tiled.sh` **PASS 19 FAIL 0** (`Screenshots/94/94-fluidtiled.txt`). That rig has NO mass-ledger assertion; its checks are residency,
  motion and wakes. Exact mass is proven on the CPU oracle (conservation asserted every tick in all the tests above). The GPU apply is all-or-nothing per move op.

## Gameplay consequences (CLAUDE.md memory rule)
- More motion and more pool use: FluidTiled scatter voxel writes 93,744 -> 167,711, peak tiles 553 -> 618 (+12%, cap 1,024).
  Stale ops 6,934 -> 16,717 (dropped whole: lost motion, not mass). Strip: 12 fall-ahead tiles, 0 refused.
- Looks: at 4.5 Hz a terminal-speed fall jumps 2.7 m per tick, and a released block falls as spaced SHEETS (each layer starts one tick
  after the one below). Both get smoother with a higher tick rate, so this bears on 97. The arena's resident height capped the strip drop at 8 m.

## Next
- Supervisor: re-ask the owner about 97 (adaptive drain to raise 4.5 Hz); the sheet/jump look above is the new argument for it.
- Not done (not asked): updating ARCHITECTURE A.5/Appendix B itself; the amendment records the bit use.

## For the doc
- Fall speed after +0.5 / +1 / +2 s, before -> after: water 0.45 -> 4.9 / 9.8 / 12.15 m/s; lava 0.075 -> same; honey 0.015 -> same
  (every fluid identical; cap 12.15 m/s = 27 voxels/tick at 4.5 Hz). Distance fallen at 2 s: water 0.9 m, lava 0.1 m, honey 0 -> 16.7 m for all
  (`FluidGravityFallTests`, TestResults_filter.xml).
- GPU strip fronts at +1 s: water 0.3 / lava 0 / honey 0 m -> 3.8 m each (`Screenshots/94/94-fallstrip.txt`).
- Conservation: exact on the CPU oracle every tick (8 new tests plus 45 existing); FluidTiled rig PASS 19/0; fast suite PASS 830 FAIL 0.
- Pool: FluidTiled peak tiles 553 -> 618 (cap 1,024); strip 12 fall-ahead tiles, 0 refused.
- `Screenshots/94/94-strip-crop.png`: top row is BEFORE, bottom row AFTER; columns +0 / +0.5 / +1 / +2 / +4 s. In each frame the left block is
  honey (brown), the middle lava (orange), the right water (blue), released 8 m up. Top: honey never moves, lava sags at +4 s, water sinks ~1.7 m.
  Bottom: all three shed sheets that fall at the same rate, most is on the ground by +2 s, and at +4 s three piles sit on the ground.
- `Screenshots/94/94-strip.png`: the same 10 frames uncropped, to show the site. The blocks are small dots at mid-frame, 32 m away.
