# 180-placement-voxel-trees-first-test

Model: opus
Requires: 179-worldgen-c5-rivers-meander-carve

Approval: owner, 2026-11-06 (chat + "Placement plan (draft for approval)" doc): placement starts with static objects; TREES ARE VOXELS ("so we can
interact with them"). Order after this: resources (the existing tier chain copper/gold/iron/cobalt/emberstone), then towns' buildings, then fluid pockets.
No NPCs, animation, combat or lairs (owner: those wait for the character creator).
Read `GAME_DESIGN_BRIEF_SNAPSHOT.md` (trees voxel-vs-mesh was "awaiting measurement"), `ARCHITECTURE_v8.6.md` §0 + Appendices A/B, `AMENDMENT_8_14`,
the cover palette notes (`Cover.cs`), and the memory rule: rules read data the generator already has, never a seed constant.

## Do (a small, first, reversible test of placement; behind a flag, default OFF)
- Place 3 tree shapes as voxels (a trunk + canopy built from existing materials; trunk is choppable wood) per the biome x landform record: forest and
  jungle dense, savanna sparse, snow sparse conifer-like, desert none or cactus-like, badlands none. Density from the biome's cover data and slope
  (none on Sheer or on route corridor cells' centre line, none in water). A rule per biome SLOT, not per seed. Keep the CPU the sole writer: trees come
  from generation (the chunk column pass), not by hand-writing the mirror. Respect the single-writer and integer-coordinate rules.
- Decide where it must live so LOD 0, the shells and the far band agree (see how 178b plumbed `routeMask` through every job).
- Prove: determinism (same seed, same trees), 4 seeds + one adversarial case (steep forest slope, tree at a chunk border), no tree inside rock or floating.
- Report the cost, not just the look: resident memory change vs OFF (the 3 GB cap), chunk-generation time change, and how trees look in each shell.
  Take these only from the task's own runs, no extra timing arms.
- Flag `-trees 1` (default 0). Fast suite + whole suite (generator change). Feature note in TASKS.md.
- Shots, full size, ON vs OFF: forest, jungle, savanna, snow, and one far view. A caption each saying what to look at.

## End
STOP_FOR_OWNER: "keep trees on by default?" with the pictures and the cost numbers.
