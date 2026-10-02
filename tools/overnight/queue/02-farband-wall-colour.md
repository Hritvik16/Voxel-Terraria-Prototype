Approved by the owner 2026-10-01 (near-cliff "weird mountain" artifacts; fix
the far band issues). One change, measured.

Problem: a far-band column stores ONE surface material (FarField.cs header:
bits 16-23). Its side faces reuse it, so cliffs higher than the voxel band
show vertical streaks of grass/snow colour. Since 2026-10-01 the underground
is thick layers (DetailField.LayerRock): below the soil, every biome's rock
near the surface is its STONE rock (DetailField.BiomeRock(biome, 0)).

Change (shader only, Raymarch.compute, far-band shading near line ~3100,
`else if (farHit)`): when the far hit is a SIDE face (fh.normal.y == 0) and
the hit point is more than ~1.5 m (15 voxels) below the column top
(fh.texel & 0xFFFF), shade with the column's stone rock instead of `fm`:
a small LUT from surface material -> biome stone rock (grass/loam -> granite,
sand/packed sand -> orange sandstone, snow/packed ice -> silver schist,
jungle grass/mud -> karst limestone; a material that is already rock keeps
itself). Use the engine material ids the far band already uses
(FarField.GenToEngine of the GenMat ids). Keep the top 1.5 m as the surface
material so grass lips still show.

Acceptance (all required; otherwise STOP_FOR_OWNER):
- `./run-editmode-tests.sh` (via tools/with-unity-lock.sh) PASS, 0 FAIL.
- Build log has no `Shader error` (run-tg2world.sh BUILD=1 checks it).
- `BUILD=1 MODE=poses ./run-tg2world.sh`: look at eye_level_2_golden,
  eye_level_3_golden, cliff_foot_3_golden and a distant mountain shot
  (summit_view / peak_from_2km_ground). Describe the walls: rock colour, no
  vertical surface-colour streaks; band views unchanged.
- `MODE=walk MINUTES=3 ./run-tg2world.sh`: mean of 30 s windows not worse than
  ~10-15 ms (Performance preset; earlier walks: Logs/tg2world_20261001_18*.txt).
Commit with the evidence in the message; update KNOWN_ISSUES.md's band entry
("Cliffs rising > ~64 m ... next fix") with the result.

Note (2026-10-01 23:40): an earlier attempt was killed before verifying anything.
Its UNVERIFIED shader edit is saved at
`Logs/overnight/02-partial-attempt-raymarch.patch` (not applied). You may
`git apply` it as a starting point, but check its material ids against
FarField.GenToEngine / GenMat first, and it still needs every acceptance step.
