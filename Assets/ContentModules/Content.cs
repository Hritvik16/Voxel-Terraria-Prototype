// Assets/ContentModules/Content.cs
//
// Phase 3, file 3 of the spec's ordered list (§13 Phase 3): the code-defined
// Materials / Biomes tables per §5.5 — "plain static C# tables … unit-testable,
// debuggable, diffable … the code IS the registry snapshot."
//
// MATERIAL ID CHOICES (documented because two are constrained):
//   Air   = 0  — hard invariant everywhere (shader treats mat==0 as no-hit).
//   Stone = 2  — LEGACY, frozen. Phase 1/2 code, tests, and the shader's old
//                grayscale path all assume Stone==2. Not renumbered.
//   Everything else is new and chosen so the shader's existing 8-color
//   DevMaterialColor palette ((mat-1)%8) gives visually distinct, roughly
//   sensible debug colors: Water=6 -> teal, Grass=3 -> green, Sand=4 -> amber,
//   Snow=7 -> pink (no white in the dev palette; acceptable, it's debug art),
//   Sandstone=8 -> olive, MossyStone=5 -> purple, JungleGrass=9 -> terracotta,
//   Deepstone=10 -> same blue as Stone (deliberate: both "stone family",
//   deep is rarely visible). The dev palette is explicitly not final art.
//
// WORLD-GEN CONSTANTS (all in VOXEL units, 0.1m each, unless stated):
//   SEA_LEVEL_VOXEL_Y = 23 — water occupies world voxels y <= 23, i.e. exactly
//   Y-bricks 0..2 of the cy=0 chunk layer, so the flat water surface aligns to
//   a brick boundary and open-water bricks stay UNIFORM (no air/water straddle
//   bricks — the sticky-note economy of §5.3 step 2 applies to water too).
//
//   DEVIATION NOTE (explicit, per project rules): the spec's coordinate
//   convention (§2, "Origin at island center; sea level Y=0") is not followed
//   here. The Phase-2 world already lives entirely in the cy=0 chunk layer
//   (world voxels Y 0..127) with the island corner near the origin; moving to
//   a sea-level-at-Y=0 world means generating negative-Y chunks, which drags
//   in resident-window/streaming behavior that is Phase 4's job. Phase 3 keeps
//   the established local convention and defines sea level as a named constant
//   instead. Revisit when Phase 4 makes the window actually move.
using System.Text;

public static class Materials
{
    public const byte Air         = 0;
    public const byte Water       = 6;  // MaterialData.flags bit0 IsFluid (A.7)
    public const byte Stone       = 2;  // legacy id — do not renumber
    public const byte Grass       = 3;
    public const byte Sand        = 4;
    public const byte MossyStone  = 5;
    public const byte Snow        = 7;
    public const byte Sandstone   = 8;
    public const byte JungleGrass = 9;
    public const byte Deepstone   = 10;
}

public struct BiomeDefinition
{
    public byte id;
    public string name;
    public byte surfaceMaterial; // top SURFACE_STRATUM_THICKNESS voxels of a column
    public byte bulkMaterial;    // between surface stratum and the deep boundary
    public byte deepMaterial;    // world voxels y < DEEP_STRATUM_TOP_Y

    public BiomeDefinition(byte id, string name, byte surface, byte bulk, byte deep)
    {
        this.id = id; this.name = name;
        surfaceMaterial = surface; bulkMaterial = bulk; deepMaterial = deep;
    }
}

public static class Biomes
{
    public const byte ForestId = 0;
    public const byte DesertId = 1;
    public const byte SnowId   = 2;
    public const byte JungleId = 3;

    // §5.5 starting roster. Bulk material is per-biome data — "why stone?"
    // has a clean answer (§5.3 step 4). Jungle bulk = mossy stone per the
    // spec's own example in §5.3 step 2.
    public static readonly BiomeDefinition[] Table =
    {
        new BiomeDefinition(ForestId, "Forest", Materials.Grass,       Materials.Stone,      Materials.Deepstone),
        new BiomeDefinition(DesertId, "Desert", Materials.Sand,        Materials.Sandstone,  Materials.Deepstone),
        new BiomeDefinition(SnowId,   "Snow",   Materials.Snow,        Materials.Stone,      Materials.Deepstone),
        new BiomeDefinition(JungleId, "Jungle", Materials.JungleGrass, Materials.MossyStone, Materials.Deepstone),
    };

    public static BiomeDefinition Get(byte biomeId) => Table[biomeId];
}

public static class WorldGenConstants
{
    // ---- Vertical layout (voxel units, cy=0 layer spans world voxels 0..127) ----
    public const int SEA_LEVEL_VOXEL_Y        = 23;  // water fills air at y <= 23 (bricks 0..2 exactly)
    public const int DEEP_STRATUM_TOP_Y       = 8;   // y < 8 is the deep stratum (exactly Y-brick 0)
    public const int SURFACE_STRATUM_THICKNESS = 4;  // top 4 solid voxels of a column are surface material
    public const int MIN_TERRAIN_HEIGHT       = 1;
    public const int MAX_TERRAIN_HEIGHT       = 120; // clamp; keeps mountains inside the cy=0 layer

    // ---- Base heightfield (voxel units) ----
    public const float OCEAN_FLOOR_MEAN   = 10f;  // seabed 8..12 with its own low noise
    public const float OCEAN_FLOOR_NOISE  = 2f;
    public const float INLAND_BASE        = 28f;  // island interior base height
    public const float HILL_AMPLITUDE     = 24f;  // fBm hills 0..24 on top of base -> 28..52

    // ---- Noise frequencies (per-voxel; 0.004/voxel == 0.04/m) ----
    public const float HILL_FREQ        = 0.004f;
    public const float COAST_WOBBLE_FREQ = 0.0015f;
    public const float OCEAN_FLOOR_FREQ = 0.02f;
    public const float COAST_WOBBLE_AMP = 160f;   // fBm(X,Z)·k of C.2, voxels

    // ---- Island geometry, derived from world.meta's sizeClass (D.2) ----
    // sizeClass 0 = the 22x22-chunk dev region carried over from Phase 2 for
    // benchmark comparability (see Phase3Bootstrapper header note).
    public static void DeriveIslandGeometry(byte sizeClass,
        out float centerXVoxels, out float centerZVoxels,
        out float coastRadiusVoxels, out float coastFalloffVoxels)
    {
        // Only sizeClass 0 exists in v1-Phase3. Additive later, per D.2.
        float spanVoxels = 22f * 128f; // 2816 voxels = 281.6m
        centerXVoxels = spanVoxels * 0.5f;
        centerZVoxels = spanVoxels * 0.5f;
        coastRadiusVoxels = 1050f;   // 105m — ocean ring fits inside the region
        coastFalloffVoxels = 320f;   // 32m beach/shelf transition
    }

    // contentVersionHash for world.meta (D.2's reserved v1.5 migration slot).
    // FNV-1a over a canonical dump of the tables above, so any content-table
    // edit changes the hash and old world.meta files are detectably stale.
    public static uint ContentVersionHash()
    {
        var sb = new StringBuilder();
        sb.Append("materials:")
          .Append(Materials.Air).Append(',').Append(Materials.Water).Append(',')
          .Append(Materials.Stone).Append(',').Append(Materials.Grass).Append(',')
          .Append(Materials.Sand).Append(',').Append(Materials.MossyStone).Append(',')
          .Append(Materials.Snow).Append(',').Append(Materials.Sandstone).Append(',')
          .Append(Materials.JungleGrass).Append(',').Append(Materials.Deepstone).Append(';');
        foreach (var b in Biomes.Table)
            sb.Append(b.name).Append(':').Append(b.surfaceMaterial).Append(',')
              .Append(b.bulkMaterial).Append(',').Append(b.deepMaterial).Append(';');
        sb.Append("consts:").Append(SEA_LEVEL_VOXEL_Y).Append(',')
          .Append(DEEP_STRATUM_TOP_Y).Append(',').Append(SURFACE_STRATUM_THICKNESS);

        uint hash = 2166136261u;
        foreach (char c in sb.ToString())
        {
            hash ^= (byte)c;
            hash *= 16777619u;
        }
        return hash;
    }
}
