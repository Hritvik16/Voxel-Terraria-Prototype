# 179-worldgen-c5-rivers-meander-carve

Model: opus

Approval: owner, 2026-11-06, "Questions for you" (river question): (a) yes, queue C5. Spec: `WORLD_GEN_LOOK_PLAN.md` C5 and its finding 3
("Rivers: canals, not rivers"). Read both, plus `Assets/TerrainGen/Rivers.cs` (D8 flow, `FLOW_SIZE`) and KNOWN_ISSUES entries on rivers/fluid.

## Do (shape only; the fluid simulation is NOT changed)
- Smooth the D8 centre line into a curve; add meanders whose wavelength scales with the river's own size (`Rivers.FLOW_SIZE`);
  width grows downstream.
- Carve banks INTO the ground instead of raising levee walls (finding 3: `pair_snow_river-plains` looks like an aqueduct).
- The water surface follows the bed's gradient in small steps (no boxy tiles, no segment rising out of the ground like a bent pipe).
- General: driven by the flow graph of ANY seed (memory rule no-terrain-specific-engineering). Prove it on 4 seeds plus these adversarial
  cases: a river crossing a canyon floor, a lake outlet, a river crossing a biome border. Do not tune to seed 42's poses.
- Keep: `StampTests.NoNeedles_OnTheMap`, route corridors still pass (178b: relief skips routes), rivers are "never at rest" (brief).
- This should turn the known red route test (seed 31337's river ford bank) green; if it does, update the KNOWN_ISSUES and CLAUDE.md-index
  line text in the repo (KNOWN_ISSUES only; leave CLAUDE.md to the owner). If it stays red, explain exactly why.
- WHOLE suite (generator change). Re-pin goldens that move. Rough cost note from the task's own runs; no timing arms.
- Before | after shots, full size, of: `pair_snow_river-plains`, `river`, `pair_desert_canyonlands`, `feature_mesa-4`, `pair_badlands_high-plateau`.
  One caption each saying what to look at.

## End
Done (no owner stop) unless a new red appears. HANDOFF with the before|after pictures for the doc.

## Supervisor note (2026-11-06 15:40)
178c is committed (1d57b8f); it only awaits the owner's keep/revert choice on the cliff look (flags `-rockmix`, `-cliffs`), which does not touch rivers. Start now.
