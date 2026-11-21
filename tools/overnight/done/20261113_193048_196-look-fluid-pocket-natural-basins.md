# 196-look-fluid-pocket-natural-basins

Model: opus

Approval: OWNER, 2026-11-13, Queue decisions doc Q-X answer (a): "fluid pockets stay OFF until a small task
softens their walls into natural basins". This is that task. `-fluidpockets` stays default OFF.

## The problem (task 191)
`Screenshots/191/on42_surface_lava.png`: a round lava pool with SHEER CYLINDER WALLS in a snow conifer forest --
looks cut, not natural. 191's verdict: "at rest and in the right place", only the shape is wrong.

## Do
- Find how the pocket carve is shaped today (`TerrainGenChunkSource.Pockets.cs` and what it calls) and say why
  the walls come out vertical.
- Shape the basin from data, not tuning (owner rule: no terrain-specific engineering): e.g. a bowl whose wall
  slope follows the material's angle of repose / the existing slope rules, a rim that blends into the local
  ground height, depth from the pocket's own size. Same rule for every fluid and biome; no per-pose constants.
- Keep what 191 found right: the fluid sits at rest (no spill on load), placement unchanged, deterministic
  (near chunk, FarQuery points and shells agree byte for byte, like 192's `Chunks_Deterministic_Border...`).
- Adversarial cases: a pocket on a slope (the downhill rim must still hold the fluid), a pocket next to a cliff,
  a pocket in sand. Tests where the carve is testable on the CPU side.
- `-fluidpockets 0` unchanged byte for byte. Generator code -> `SUITE=all` once at the end. TASKS.md paragraph.
- No timing arms.

## Pictures (one launch, 3-5 poses, midday, sun behind camera)
191's `surface_lava` pose and 1-2 other surface pockets (water and lava), before vs after, full size, `viewer.html`.
Captions say what the walls/rim look like.

## End
Done with HANDOFF + pictures; the owner then decides whether pockets go ON.
