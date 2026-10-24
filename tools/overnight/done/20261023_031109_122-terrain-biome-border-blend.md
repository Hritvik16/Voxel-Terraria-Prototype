# 122-terrain-biome-border-blend

Model: opus
Requires: 121-look-sky-and-haze

Approval: owner, 2026-10-22 (chat, task 114 Q6 (a)): blend biome borders, after the renderer look fixes (119-121).
TERRAIN-GENERATOR task (`Assets/TerrainGen/`): `SUITE=all` before committing. No timing run. Map v4 stays the default; the change
must keep generator determinism (same seed -> same world) and the biome x landform independence (owner, 2026-10-01).

## Change (114 defect #5)
Biomes meet along hard, nearly straight lines and each patch is one flat colour (one surface material per biome cell; the far band stores
one material per column). Add a dithered transition band at biome borders (materials of both sides mixed, width a few tens of metres,
noise-shaped so the line is not straight) and mild per-biome tint variation. The far band must show the blend too.
Map renders (biome map before/after) and in-game stills of 2-3 borders (114's `161606_low/high_fsr3_540.png` view). Report the
EditMode map metrics that changed. HANDOFF `## For the doc` with images.
