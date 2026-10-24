# 98-gameplay-start-mild-biome

Requires: 93c1-gameplay-step-4

Approval: owner, 2026-10-16, Queue decisions doc Q53 = (a). Queued by the supervisor. Branch `engine-phase`.

## Why
Task 93c1 starts play on the LARGEST easy area (`TerrainGen.PlayStart`). On seed 42 that is a SNOW plain. Terraria starts
in a forest; the owner wants a gentle first look, with the hard biomes coming later as progression.

## Change (one change, small)
`PlayStart` prefers easy areas whose biome is mild: grass / forest / plains (use the world map's biome per cell; name the
exact biome ids you treat as mild in HANDOFF). Among mild easy areas, keep 93c1's rule (largest, ties -> nearest the island
centre; first easy + Walk + dry cell whose 8 neighbours are too). Fall back to the largest easy area of ANY biome only when
no mild easy area reaches a minimum size (pick a sensible minimum, e.g. the size of a small clearing; state it). Pure
function of the world map, deterministic per seed. No terrain-generator change. Rigs/capture runs and `-playstart 0`
unchanged (`Tg2Spawn`). KEEP=1 still resumes the saved pose.

## Acceptance
- `PlayStartTests` extended: seed 42 picks a mild biome; a synthetic map with no mild easy area falls back to the largest;
  determinism. Report seeds 42 + 3 other seeds: start biome, area size, height (before -> after).
- One `-playshot -playshotquit` launch: the start view on seed 42, before vs after, midday with the sun behind the camera,
  captioned. Fast suite once at the end. CLAUDE.md task-93c1 line updated.
