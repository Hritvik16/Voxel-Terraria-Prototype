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
        /// Task 86 (owner Q45 (a)): DetailField.PrepareColumn evaluates the cave
        /// noises (tunnels, tunnel depths, caverns, the underworld chamber) on an
        /// absolute grid of one node per 2^s x 2^s columns and interpolates them
        /// bilinearly. 0 = per column (the pre-86 bytes; a spec built field by
        /// field). SizeClass2 sets COARSE_COLUMN_SHIFT; -coarsecols 0 on a player
        /// turns it off (TerrainGenChunkSource.CoarseColumnArg).
        public int coarseColumnShift;
        public const int COARSE_COLUMN_SHIFT = 3;   // one node per 8 x 8 columns (one per brick column)
        /// Task 88: 1 = DetailField.PrepareColumn(.., ref NodeCache) recomputes the
        /// cavern candidates per column instead of once per cavern cell (the A arm
        /// of its timing and byte tests; same bytes either way). 0 = cached.
        public byte cavernCellCacheOff;
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
        /// Map v4 (surface rebuild B2, plan rule 2; owner Q9 2026-10-03: "needs
        /// testing", so a TUNABLE): the share of LAND whose region is of the
        /// gentle landform class (Regions.ClassOf). Broken and steep split the
        /// rest 3:2. 0 = the version's default (0.50 on v4). IGNORED below v4.
        /// Read it through GentleShare, never directly.
        public float gentleShare;
        /// Map v4 (surface rebuild B10, owner Q10 (a)): which biome field. 6 =
        /// forest, desert, snow, jungle, savanna, badlands on a one-octave
        /// climate (ShapeField.Biome); 4 = B1-B8's four-biome field, kept so
        /// the change is reversible and B8's maps can be redrawn. 0 = the
        /// version's default (6 on v4). IGNORED below v4. Read it through
        /// BiomeSet, never directly.
        public int biomeSet;
        /// Task 122 (VISUAL_REVIEW_114 #5, owner Q6 (a), 2026-10-22): 1 = no biome
        /// BORDER BLEND (ShapeField.SurfaceBiome), the pre-122 surface. 0 = the
        /// version's default: ON on v4, IGNORED below v4 (v3 byte-identical).
        /// Read it through BiomeBlend, never directly. `-biomeblend 0` sets it.
        public byte biomeBlendOff;
        /// Task 176 (C1, owner 2026-11-05 Q2 (a) "vary climate"): 1 = B10's
        /// one-octave climate at x3.25 (pre-176: across the island it is nearly a
        /// ramp, so the biomes run as straight belts). 0 = the version's default:
        /// on the six-biome v4 field the climate's wavelength is a fraction of the
        /// island radius, with two low octaves, normalised over the island
        /// (ShapeField.Biome6). IGNORED below v4 and on biomeSet 4. Read it
        /// through BiomeVary, never directly. `-biomevary 0` sets it.
        public byte biomeVaryOff;
        /// Task 177 (C2 + C3, owner 2026-11-05 "use materials from that biome"):
        /// 1 = the pre-177 surface (one cover per biome, the beach rule, rock
        /// wherever a voxel's top step exceeds 1.5). 0 = the version's default:
        /// on v4 the cover is a SLOT chosen from the ground's shape, filled from
        /// the biome's palette row (Cover.cs). IGNORED below v4. Read it through
        /// CoverSlots, never directly. `-coverslots 0` sets it.
        public byte coverSlotsOff;
        /// Task 178 (C4, owner 2026-11-05 "C4 cliffs approved"): 1 = the pre-178
        /// cliffs (one rock per biome down to sea - 50 m, badlands' 3.2 m bands,
        /// smooth faces). 0 = the version's default: on v4 the stone zone is a few
        /// THICK rock layers per biome by height (DetailField.LayerRock), and faces
        /// get relief from the map slope (ShapeField.FaceRelief: fluting, ledges,
        /// scree at the foot). IGNORED below v4. Read it through Cliffs, never
        /// directly. `-cliffs 0` sets it.
        public byte cliffsOff;
        /// Task 178c: 1 = the cliff layers without the per-brick rock mix (each
        /// layer one rock, DetailField.MixBrick off). 0 = the default. Only with
        /// Cliffs. Read it through RockMix. `-rockmix 0` sets it; tests whose
        /// shells run without the fill pass use it (TerrainGenChunkSource.SetRockMix).
        public byte rockMixOff;
        /// Task 179 (C5, owner 2026-11-06 "queue C5"): 1 = the pre-179 rivers (the
        /// D8 centre line drawn straight cell to cell, water at bed + depth held by
        /// raised banks). 0 = the version's default: on v4 the course is a smooth
        /// curve with meanders, the water stands UNDER its floodplain and the banks
        /// are carved into the ground (Rivers.cs). IGNORED below v4. Read it through
        /// RiverShape, never directly. `-rivershape 0` sets it. Changes the MAP
        /// (Rivers.Profile's levels) as well as the columns.
        public byte riverShapeOff;
        /// Task 180 (placement, first test): 1 = generated voxel trees (Trees.cs),
        /// OPT-IN: 0 (the default) = none. Needs a world record (v4) and a MapView
        /// carrying the tree plane. Read it through Trees, never directly. The engine's
        /// chunk source sets it unless `-trees 0` (task 195a); a bare spec stays 0.
        /// Columns only: the map, the record and the far band ignore it.
        /// Task 188 (TESTS ONLY, no flag): 2 + b = the ADVERSARIAL cover -- every
        /// tree cell that passes the physical rules (land, dry, no stamp / ledge,
        /// foot, clearance) gets a biome-b tree, density and the plane ignored.
        public byte treesOn;
        /// Task 192 (trees' look; read through Trees.LeafFill, never directly): the
        /// leaf FILL target, percent of a crown's envelope. 0 = Trees.DEFAULT_LEAF_FILL.
        /// `-leaffill 30|45|60` sets it (the owner's three arms). Columns only.
        public byte treeLeafFill;
        /// Task 192 (diagnostic): 1 = task 180's tree families byte for byte (3 shapes
        /// per biome, one solid crown), `-treestyle 0`. 0 (the default) = 192's open
        /// crowns on a branch skeleton. Columns only.
        public byte treeStyle180;
        /// Task 210 (owner 2026-11-14 Q-AD (a)): 1 = the WORLD space-colonization trees
        /// (Trees2.cs: a shared prototype library, a brick budget per area, giants on their
        /// own sparse grid; 192's trees kept only for the forms the library does not grow).
        /// 0 (the default, a bare spec) = today's trees byte for byte. Needs treesOn and
        /// the library the chunk source builds (`-trees2 1`). Read it through Trees2, never directly.
        public byte trees2On;
        /// Task 180b / 186: 0 (THE DEFAULT since task 186, owner 2026-11-07 Q-M (a))
        /// = the simple material rules (GroundRules.cs: one palette row per biome
        /// paints skin, soil, subsoil and stone; a short slot list over local
        /// inputs). 1 = the 177-179 cover + layered rock, byte for byte (the old
        /// path, `-matrules 0`). Columns and the far band's colours only: the map
        /// and the record ignore it. Read it through MatRules, never directly.
        public byte matRulesOff;
        /// Task 181 (placement, resource deposits): 1 = OreRules.cs (harshness-scaled
        /// deposits, wall veins, rich features, the hidden start, latent cobalt and
        /// emberstone twins). 0 = step 3's Ores.cs placement, byte for byte (a bare
        /// spec's value). Needs a record (v4) and the ore cell plane
        /// (ColumnDetail.oreCell), so only the engine's TerrainGenChunkSource sets
        /// it -- ON there by default since task 195 (owner 2026-11-13); `-ores 0`
        /// leaves it 0. Read it through Ores2, never directly.
        public byte oresOn;
        /// Task 183 (placement, fluid pockets at rest): 1 = FluidPockets.cs (surface
        /// basins by rule, the deep pockets' rule fluids and linings). 0 (the
        /// default) = no surface pockets, the deep pockets as before, byte for byte.
        /// Needs a record (v4) and the site plane (MapView.pocket). Read it through
        /// Pockets, never directly. `-fluidpockets 1` sets it.
        public byte fluidPocketsOn;
        /// Task 185 / 187a (WORLD_GEN_LOOK_PLAN C7): 0 (THE DEFAULT since task 187a,
        /// owner 2026-11-12 Q-F (a)) = stamps that sit in the land (Stamps.cs
        /// "TASK 185"): a cave mouth only where its tube stays covered, flat
        /// cap-rock ledges on a rock-layer boundary, the geyser basin's terraces
        /// follow the ground, hoops only on a rise or in rugged rock, and no stamp
        /// opening within SEA_KEEP_OUT of the sea. 1 = the pre-185 stamps, byte for
        /// byte (`-stampfit 0`). Columns only: the map and the record ignore it.
        /// Read it through StampFit, never directly.
        public byte stampFitOff;
        /// Task 187 (surface rebuild B7, flag-off): 1 = plan town / village /
        /// outpost / frontier sites (Sites.cs) into WorldRecord.SiteList and the
        /// record's site bytes (ids >= 16). Changes NO terrain (the flatten rule
        /// is stored, not applied): the map, the columns and every voxel ignore
        /// it. Read it through Sites, never directly. `-sites 1` sets it.
        public byte sitesOn;
        /// Task 188a / 190a (owner 2026-11-12: "just randomly spraying it is weird
        /// ... like blotches"; default flipped by task 190a, Q-V (a)): 0 (THE
        /// DEFAULT since task 190a) = the material rules give every column its
        /// slot's MAIN palette entry -- no random variant patches. 1 = the
        /// pre-190a ~20-25 % VariantNoise spray, byte for byte (`-matvariant 1`).
        /// Rules path only (MatRules); the old -matrules 0 path, the map and the
        /// record ignore it, and trees keep reading the noise (c.cover's variant
        /// bit is unchanged). Read it through MatVariant, never directly.
        public byte matVariantOn;
        /// Task 189 / 190b (WORLD_GEN_LOOK_PLAN C6; default flipped by task 190b,
        /// owner 2026-11-12 Q-W (b)): 0 (THE DEFAULT since task 190b) = landform
        /// signatures at walking scale (LandSig.cs): meso (20-200 m) shapes per
        /// landform, never per biome -- dune ridges with a steep lee side,
        /// canyonland slot canyons and benches, alpine ridgelines and side valleys
        /// along the slope. 1 = the pre-189 columns, byte for byte (`-landsig 0`).
        /// Columns only: the map's arrays and the record ignore it; its weight
        /// plane is derived (WorldMap.EnsureLandSigPlane, which WorldMap.Build runs
        /// whenever LandSig is in force). Read it through LandSig, never directly.
        public byte landSigOff;
        /// Task 190 (placement, flag-off): 1 = TOWNS ON THE SITES (Towns.cs): the
        /// site discs flattened to their pad (ring blended back) and BuildingKit's
        /// huts / houses / halls laid out on them. Needs the site planner (sitesOn;
        /// the engine's chunk source and far band set both unless `-towns 0`, task 195a;
        /// a bare spec stays 0). 0 = every byte as before. The map
        /// and the record's cells ignore it (the plane is derived, WorldMap.EnsureTowns).
        /// Read it through Towns, never directly.
        public byte townsOn;

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
            coarseColumnShift = COARSE_COLUMN_SHIFT,
            mapVersion = 4,                  // v4 = surface rebuild B1-B10 (owner 2026-10-09, Q35 6a); 3 = regions + erosion (§13), via -mapversion 3
        };

        /// sizeClass 2 at an explicit map version (4 = B1: v3 + the world
        /// record + larger biomes). 0 or 4 is exactly SizeClass2(seed); 3 is the
        /// pre-rebuild map (byte-identical, still reachable).
        public static TerrainSpec SizeClass2(uint seed, int mapVersion)
        {
            var s = SizeClass2(seed);
            if (mapVersion > 0) s.mapVersion = mapVersion;
            return s;
        }

        public const float V4_BIOME_SCALE = 3f;
        /// B10: six biomes on a one-octave climate need x3.25 to keep a biome
        /// change walking east-west >= 900 m on seeds 42/7/2026/31337.
        public const float V4_BIOME_SCALE_6 = 3.25f;
        public const int V4_BIOME_SET = 6;

        /// The biome field in force (see biomeSet): 4 below v4.
        public int BiomeSet => mapVersion >= 4 ? (biomeSet > 0 ? biomeSet : V4_BIOME_SET) : 4;

        /// The biome wavelength multiplier actually in force (see biomeScale).
        public float BiomeScale => mapVersion >= 4
            ? (biomeScale > 0f ? biomeScale : (BiomeSet == 6 ? V4_BIOME_SCALE_6 : V4_BIOME_SCALE)) : 1f;

        /// The varied climate in force (see biomeVaryOff): v4's six-biome field only.
        public bool BiomeVary => mapVersion >= 4 && BiomeSet == 6 && biomeVaryOff == 0;

        /// The biome border blend in force (see biomeBlendOff): v4 only.
        public bool BiomeBlend => mapVersion >= 4 && biomeBlendOff == 0;

        /// The shape-chosen cover slots in force (see coverSlotsOff): v4 only.
        public bool CoverSlots => mapVersion >= 4 && coverSlotsOff == 0;

        /// Task 178's cliff geology and face relief in force (see cliffsOff): v4 only.
        public bool Cliffs => mapVersion >= 4 && cliffsOff == 0;

        /// Task 178c's per-brick rock mix inside the cliff layers in force (see rockMixOff).
        public bool RockMix => Cliffs && rockMixOff == 0;

        /// Task 179's river course and incision in force (see riverShapeOff): v4 only.
        public bool RiverShape => mapVersion >= 4 && riverShapeOff == 0;

        /// Task 180's generated trees in force (see treesOn): v4 only, opt-in.
        public bool Trees => mapVersion >= 4 && treesOn != 0;
        /// Task 210's world trees in force (see trees2On): only with Trees.
        public bool Trees2 => Trees && trees2On != 0;

        /// Task 180b's material rules in force (see matRulesOff): v4 only, ON by
        /// default since task 186.
        public bool MatRules => mapVersion >= 4 && matRulesOff == 0;

        /// Task 188a / 190a: the material rules' random variant spray in force
        /// (see matVariantOn): OFF by default since task 190a.
        public bool MatVariant => matVariantOn != 0;

        /// Task 181's rule ores in force (see oresOn): v4 only; the chunk source's default since task 195.
        public bool Ores2 => mapVersion >= 4 && oresOn != 0;

        /// Task 183's fluid pockets in force (see fluidPocketsOn): v4 only, opt-in.
        public bool Pockets => mapVersion >= 4 && fluidPocketsOn != 0;

        /// Task 185's stamp fit in force (see stampFitOff): v4 only, ON by default
        /// since task 187a.
        public bool StampFit => mapVersion >= 4 && stampFitOff == 0;

        /// Task 189's landform signatures in force (see landSigOff): v4 only, ON by
        /// default since task 190b.
        public bool LandSig => mapVersion >= 4 && landSigOff == 0;

        /// Task 187's site planner in force (see sitesOn): v4 only, opt-in.
        public bool Sites => mapVersion >= 4 && sitesOn != 0;

        /// Task 190's towns in force (see townsOn): v4 + the site planner, opt-in.
        public bool Towns => Sites && townsOn != 0;

        public const float V4_GENTLE_SHARE = 0.50f;

        /// The gentle-class land share in force (see gentleShare); 0 below v4,
        /// where the region classes are not assigned to a share at all.
        public float GentleShare => mapVersion >= 4 ? (gentleShare > 0f ? gentleShare : V4_GENTLE_SHARE) : 0f;

        public int CavernTopY => seaLevelY - cavernTopBelowSea;
        public int UnderworldTopY => seaLevelY - underworldTopBelowSea;
    }
}
