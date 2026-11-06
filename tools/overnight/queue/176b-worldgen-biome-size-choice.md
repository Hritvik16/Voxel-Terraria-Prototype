# 176b-worldgen-biome-size-choice

Model: opus

Approval: owner, 2026-11-05, chat on 176: "Show me sizes" -- 176's layout (climate wave 0.75 x island radius) fixed the straight belts but
makes biomes change every ~375 m E-W (B10 ~1,050 m); the owner asked earlier for LARGER biomes (B1 / Q5) and wants bold, distinct
regions ("One Piece-like with a realistic twist", memory game-design-intent). MAP-ONLY task: no game launch, no defaults changed.
Read `tools/overnight/blocked/176-worldgen-c1-biome-layout.HANDOFF.md`, `Screenshots/176/c1_layout*.txt`, `tools/biome176.py`, ShapeField `Climate3`.

## Do
1. Render the REAL generator's biome map (the EditMode `BiomeLayoutDump` path or an equivalent that runs ShapeField itself, not only the
   Python port) for 4 seeds (42, 7, 2026, 31337) at 4 region sizes spanning ~500 m to ~1,200+ m mean E-W change (e.g. climate wave
   1.0 / 1.5 / 2.0 / 3.0 x radius; choose octave weights so edges stay organic). Keep: no domain warp (owner rule), 176's patch borders.
2. Per size and seed report: mean E-W change distance, longest straight border, regions per biome (a biome in only ONE region is allowed
   now -- report it, do not gate on it), land share per biome, biomes absent from a world (report).
3. Sheet per seed: before (B10 belts) | 176 (0.75) | the 4 sizes, same colours as 176's maps, captioned; plus one full-size viewer.
4. Recommend ONE size, reasoning against "larger biomes" + "bold distinct regions" + "no straight belts". No code default change; the
   chosen value is applied in a follow-up (one constant + re-pinned goldens + whole suite).

## Known, do not fix
The Slow route test red since 176 (river ford bank, KNOWN_ISSUES) stays red until C5; owner decision.

## End
STOP_FOR_OWNER: "Which size?" with the sheets.
