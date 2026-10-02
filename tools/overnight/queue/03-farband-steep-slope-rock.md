Approved by the owner 2026-10-02 (answer "2c" to the morning report: steep far
slopes should turn to rock, judged by SLOPE, not only by a 1.5 m wall step).

Background: task 02 (`cd28987`) made far-band SIDE faces more than 15 voxels
below the column top use the biome's stone (`FarWallMaterial`, `FAR_WALL_LIP`
in Raymarch.compute). Moderately steep far slopes (e.g. the big mountain in
`eye_level_3_golden`) still show green: at F0 a 30 deg slope steps ~0.5 m per
column, inside the lip, and the TOP faces keep the surface material.

Change (ONE change): decide rock-vs-surface for a far column from the local
slope of the far heightfield, for top AND side faces. First find the rule the
VOXEL terrain uses to put rock instead of grass/snow/sand on steep ground
(TerrainGen / DetailField; grep "slope"), and mirror its threshold so the far
band matches the voxels at the seam. Choose where to apply it -- in the bake
(FarField: write the rock material into the column, if the bake knows the
neighbours) or in the shader (neighbour height reads) -- and say why in the
commit. Prefer the bake if it costs no per-pixel work. Keep `FarWallMaterial`'s
material map; keep grass lips on gentle ground.

Acceptance (all required; otherwise STOP_FOR_OWNER):
- `tools/with-unity-lock.sh ./run-editmode-tests.sh`: 0 FAIL.
- `BUILD=1 MODE=poses ./run-tg2world.sh`: build OK, 0 Shader error.
- Compare against `FarBand/2026-10-02_001850_world` (task 02's run; folder is
  under ~/Library/Application Support/DefaultCompany/<project>/FarBand/):
  eye_level_2_golden, eye_level_3_golden, peak_from_2km_ground_golden,
  summit_view_golden. Look at each. Describe: steep far flanks rock-coloured,
  gentle far ground still green, no visible colour seam where far band meets
  voxels. Count changed / green-dominant pixels as task 02 did.
- `MODE=walk MINUTES=3 ./run-tg2world.sh`: mean of 30 s windows within noise of
  task 02's 11.0 ms (`Logs/tg2world_20261002_003414.txt`).
Commit with the evidence in the message; update KNOWN_ISSUES.md's band entry.
