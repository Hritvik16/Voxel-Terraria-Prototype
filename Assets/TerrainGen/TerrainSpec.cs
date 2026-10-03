// Assets/TerrainGen/TerrainSpec.cs
//
// TERRAINGEN -- the standalone terrain generator (DESIGN_LARGE_WORLD_AND_
// TERRAIN_OVERHAUL.md §4.1 / §2, Step 2).
//
// PORTABILITY CONTRACT. This assembly references Unity.Mathematics and nothing
// else: no UnityEngine type, no engine type (ChunkStore, Materials, Biomes...).
// It is meant to be copied into the other two projects unchanged. A project
// consumes it through three calls -- WorldMap.Build, ShapeField.Sample,
// DetailField.Material -- and maps GenMat codes to its own material ids.
// TerrainGenPortabilityTests asserts the reference list.
//
// DETERMINISM CONTRACT. Every float operation in S and D is +, -, *, /, sqrt,
// floor, min/max/abs or Unity.Mathematics noise (itself built from those). No
// sin/cos/exp/pow: their Mono and Burst implementations are not guaranteed to
// agree in the last bit, and a world that generates differently under Burst
// than under Mono is §0.3's undiagnosable class. Burst jobs must use
// FloatMode.Strict; TerrainGenDeterminismTests asserts byte identity.
//
// UNITS. Voxels (0.1 m) throughout, world Y absolute. Nothing here knows about
// chunks, bricks or windows.
namespace TerrainGen
{
    /// Every number that shapes a world, in one blittable struct. sizeClass 2
    /// is the owner's §2 world: 8 km across, a ~6 km island, peaks ~600 m,
    /// 500 m of depth below sea level.
    public struct TerrainSpec
    {
        public uint seed;
        public int worldSizeVoxels;      // square world edge
        public int mapCells;             // world-map cells per edge (power of two)
        public int mapCellVoxels;        // voxels per map cell edge
        public int seaLevelY;            // absolute voxel Y of the sea surface
        public float islandRadius;       // voxels, the envelope's nominal coast
        public float peakHeight;         // voxels above sea, the dominant summit
        public float rangeHeight;        // voxels, typical mountain-range relief
        public float lowlandHeight;      // voxels, typical lowland relief
        public float oceanDepth;         // voxels below sea, the deep floor
        public int cavernTopBelowSea;    // voxels below sea where caverns begin
        public int underworldTopBelowSea;// voxels below sea where the underworld begins
        public int worldBottomY;         // absolute voxel Y of the world floor
        /// V0 (design doc §11), OPT-IN: deeper than this many voxels below the
        /// column top, the geology (strata, cavern and underworld bands) is
        /// decided once per 8^3 brick, at its centre, so deep bricks are uniform
        /// unless a cave, a pocket or a biome border crosses them. 0 (the
        /// default) = per-voxel geology everywhere, as before.
        public int brickGeologyDepth;
        /// Which world-map recipe: 2 = noise ranges + bolted-on landforms (to
        /// 2026-09-25), 3 = regions + erosion (TerrainGen v3, design doc §13).
        /// 0 is read as 2, so a spec built field by field keeps the old map.
        public int mapVersion;
        /// Map v4 (surface rebuild B1, owner 2026-10-03): multiplies the two
        /// climate-field wavelengths that drive ShapeField.Biome, so biomes get
        /// larger without touching landform. 0 = the version's default (x3 on
        /// v4). IGNORED below v4: v3 is x1 and byte-identical. Read it through
        /// BiomeScale, never directly.
        public float biomeScale;

        /// The owner's world (§2.2). Additive: sizeClass 0/1 live in the
        /// engine's own generator and are never touched by this assembly.
        public static TerrainSpec SizeClass2(uint seed) => new TerrainSpec
        {
            seed = seed,
            worldSizeVoxels = 80000,         // 8 km
            mapCells = 1024,
            mapCellVoxels = 80,              // 8 m cells; 1024 * 80 = 81,920 >= 80,000
            seaLevelY = 5120,                // 512 m of world below the sea surface
            islandRadius = 24000f,           // with the coast noise, ~6 km across (TerrainGen targets test)
            peakHeight = 6000f,              // 600 m dominant summit
            rangeHeight = 3200f,             // ranges to ~150-400 m with the base
            lowlandHeight = 420f,            // rolling lowlands, tens of metres
            oceanDepth = 900f,               // 90 m deep ocean floor
            cavernTopBelowSea = 1500,        // 150 m
            underworldTopBelowSea = 4000,    // 400 m
            worldBottomY = 120,              // 12 m of bedrock floor under the underworld
            mapVersion = 3,                  // regions + erosion (§13)
        };

        /// sizeClass 2 at an explicit map version (4 = B1: v3 + the world
        /// record + larger biomes). 0 or 3 is exactly SizeClass2(seed).
        public static TerrainSpec SizeClass2(uint seed, int mapVersion)
        {
            var s = SizeClass2(seed);
            if (mapVersion > 0) s.mapVersion = mapVersion;
            return s;
        }

        public const float V4_BIOME_SCALE = 3f;

        /// The biome wavelength multiplier actually in force (see biomeScale).
        public float BiomeScale => mapVersion >= 4 ? (biomeScale > 0f ? biomeScale : V4_BIOME_SCALE) : 1f;

        public int CavernTopY => seaLevelY - cavernTopBelowSea;
        public int UnderworldTopY => seaLevelY - underworldTopBelowSea;
    }
}
