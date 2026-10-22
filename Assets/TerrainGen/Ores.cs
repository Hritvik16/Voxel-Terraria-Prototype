// Assets/TerrainGen/Ores.cs
//
// ORES BY DEPTH AND BIOME x LANDFORM (gameplay step 3, GAMEPLAY_PHASE_PLAN.md
// picks P17-P20; map v4 only, v3 never calls this).
//
//   which ore      one per depth layer (P17), rarer deeper:
//                    layer 0 stone              copper
//                    layer 1 deep stone         iron   (sea - 50 m .. sea - 100 m)
//                    layer 2 deeper deep stone  gold   (sea - 100 m .. the cavern top)
//                    layer 3 cavern rock        cobalt
//                    layer 4 underworld rock    emberstone
//                  The layer is DetailField's own (same wavy boundaries); the
//                  deep-stone split at sea - 100 m rides the stone/deep-stone
//                  boundary's wave.
//   how many       a blob CHANCE per 32^3 cell = the layer's base chance x the
//                  column's biome x landform multiplier (OreTable.PairMul, read
//                  from the world record per 8 m map cell, P18) x 2 for the
//                  biome's favoured ore.
//   deposit shape  at most one deposit per 32^3 cell (3.2 m), in the cell's
//                  2x2x2 inner bricks: one brick (5 in 8), two face-adjacent
//                  bricks (2 in 8) or a 2x2 slab of four (1 in 8). Each chosen
//                  brick holds one ellipsoid centred in it, half-axes 2.5-3.5
//                  voxels, with a ragged rim (the outer ~13% of the radius keeps
//                  half its voxels): 0.5-0.7 m single blobs, deposits up to
//                  ~1.6 m. WHY BRICK-SHAPED (task 66): every 8^3 brick ore
//                  touches stops being uniform and costs a 512-byte body. Free
//                  ellipsoids of 0.3-1.2 m (P19 as written) touched ~3.2 bricks
//                  per blob, ~56 ore voxels per mixed brick; a blob that fills
//                  most of one brick packs ~2x more ore per mixed brick.
//   where          only where DetailField would return layer rock, deeper than
//                  CARVE_MIN_DEPTH (6 m) under the column top: never air, fluid,
//                  soil, surface, stamps or cliff faces (cliff veins are step 12).
//                  Cave walls show it.
//
// INTEGER HASHING ONLY (plan guardrail 4): the placement is a pure function of
// (seed, cell, voxel) in uint arithmetic, so Burst and Mono agree bit for bit
// and co-op stays possible. The host layer (which ore) uses DetailField's float
// layer boundaries, exactly as the rock itself does.
//
// GenMat codes 65-69 (the free run after jungle's set). Append-only.
namespace TerrainGen
{
    public static class Ores
    {
        public const byte Copper = 65, Iron = 66, Gold = 67, Cobalt = 68, Emberstone = 69;
        public const int LAYERS = 5;

        public const int CELL_SHIFT = 5;                 // 32^3 voxel cells
        public const int CELL = 1 << CELL_SHIFT;
        /// Deep stone splits into iron above and gold below this depth under sea.
        public const int GOLD_TOP_BELOW_SEA = 1000;      // 100 m
        /// No ore within this many voxels of the column top (the LOD-0 contract, cliffs).
        public const int MIN_DEPTH = DetailField.CARVE_MIN_DEPTH;
        /// ColumnDetail.oreMul is in 1/16ths: 16 = x1.0. 0 = no ore (map v3).
        public const int MUL_ONE = 16;
        public const byte DEFAULT_MUL = 16;

        public static bool IsOre(byte m) => m >= Copper && m <= Emberstone;

        /// The ore of depth layer k (0 stone .. 4 underworld).
        public static byte OreOf(int layer)
        {
            switch (layer)
            {
                case 0: return Copper;
                case 1: return Iron;
                case 2: return Gold;
                case 3: return Cobalt;
                default: return Emberstone;
            }
        }

        /// Inverse of OreOf; -1 for a non-ore.
        public static int LayerOf(byte ore) => IsOre(ore) ? ore - Copper : -1;

        /// Base deposit chance per 32^3 cell, out of 65,536, at multiplier x1 and
        /// not favoured. A deposit averages ~145 ore voxels in ~1.6 bricks, so the
        /// ore share of the layer's rock is about chance / 65,536 x 0.44%. Set by
        /// the plan's memory rule (mixed bricks may rise <= 5% on a ring-shaped
        /// chunk set, OreTests; x1 here measured +7.8%, so x0.55): copper ~0.02%,
        /// falling to emberstone ~0.005%.
        public static int BaseChance(int layer)
        {
            switch (layer)
            {
                case 0: return 3300;
                case 1: return 2500;
                case 2: return 1650;
                case 3: return 1300;
                default: return 820;
            }
        }

        /// Each biome's favoured ore (its layer), x2 chance (P18).
        public static int FavouredLayer(byte biome)
        {
            switch (biome)
            {
                case GenBiome.Forest: return 0;    // copper
                case GenBiome.Savanna: return 1;   // iron (ironstone country)
                case GenBiome.Desert: return 2;    // gold
                case GenBiome.Badlands: return 2;  // gold
                case GenBiome.Snow: return 3;      // cobalt
                default: return 4;                 // jungle: emberstone
            }
        }

        /// Blob chance per cell (out of 65,536; 65,536 = always), for a column
        /// with pair multiplier `oreMul` (1/16ths) and biome `biome`.
        public static int CellChance(int layer, byte biome, int oreMul)
        {
            long p = (long)BaseChance(layer) * oreMul / MUL_ONE;
            if (FavouredLayer(biome) == layer) p *= 2;
            return p > 65536 ? 65536 : (int)p;
        }

        /// DetailField's rock layer at y (0 stone, 1 deep stone above sea - 100 m,
        /// 2 deep stone below it, 3 cavern rock, 4 underworld) -- LayerRock's
        /// own comparisons, so the ore always matches its host rock.
        public static int Layer(in TerrainSpec spec, float y, float deepOff, float cavernOff, float underOff)
        {
            if (y + underOff < spec.UnderworldTopY) return 4;
            if (y + cavernOff < spec.CavernTopY) return 3;
            if (y + deepOff < spec.seaLevelY - DetailField.STONE_BOTTOM_BELOW_SEA)
                return y + deepOff < spec.seaLevelY - GOLD_TOP_BELOW_SEA ? 2 : 1;
            return 0;
        }

        /// The deposit's inner bricks (bit b = (bx - 1) | (by - 1) << 1 | (bz - 1) << 2):
        /// one brick (5 in 8), two face-adjacent bricks (2 in 8) or a 2x2 slab of
        /// four (1 in 8).
        private static uint DepositMask(uint g)
        {
            int first = (int)(g & 7u);
            uint mask = 1u << first;
            uint size = (g >> 3) & 7u;
            if (size >= 5)
            {
                int a = (int)((((g >> 6) & 15u) * 3u) >> 4);             // axis 0-2
                mask |= 1u << (first ^ (1 << a));
                if (size == 7)
                {
                    int a2 = a + 1 + (int)((g >> 10) & 1u);              // another axis
                    if (a2 >= 3) a2 -= 3;
                    mask |= (1u << (first ^ (1 << a2))) | (1u << (first ^ (1 << a) ^ (1 << a2)));
                }
            }
            return mask;
        }

        /// Engine step 15 (task 82b): false when NO voxel of the 8^3 brick holding
        /// (x, y, z) can be ore at cell chance `chance` (CellChance). At's cell
        /// and deposit tests read only the 32^3 cell and the brick, so they hold
        /// for the whole brick.
        public static bool BrickMayHold(in TerrainSpec spec, int chance, int x, int y, int z)
        {
            if (chance <= 0) return false;
            uint h = Hash(spec.seed, x >> CELL_SHIFT, y >> CELL_SHIFT, z >> CELL_SHIFT);
            if ((int)(h & 0xFFFFu) >= chance) return false;
            int bx = (x >> 3) & 3, by = (y >> 3) & 3, bz = (z >> 3) & 3;
            if (bx == 0 || bx == 3 || by == 0 || by == 3 || bz == 0 || bz == 3) return false;
            int b = (bx - 1) | ((by - 1) << 1) | ((bz - 1) << 2);
            return ((DepositMask(Mix(h ^ 0x6A09E667u)) >> b) & 1u) != 0u;
        }

        /// The ore at voxel (x, y, z) of host layer `layer`, or 0 for none.
        public static byte At(in TerrainSpec spec, int oreMul, byte biome, int layer, int x, int y, int z)
        {
            int chance = CellChance(layer, biome, oreMul);
            if (chance <= 0) return 0;
            uint h = Hash(spec.seed, x >> CELL_SHIFT, y >> CELL_SHIFT, z >> CELL_SHIFT);
            if ((int)(h & 0xFFFFu) >= chance) return 0;
            // Only the cell's 2x2x2 INNER bricks (voxels 8-23 per axis) hold ore.
            int bx = (x >> 3) & 3, by = (y >> 3) & 3, bz = (z >> 3) & 3;
            if (bx == 0 || bx == 3 || by == 0 || by == 3 || bz == 0 || bz == 3) return 0;
            int b = (bx - 1) | ((by - 1) << 1) | ((bz - 1) << 2);
            uint g = Mix(h ^ 0x6A09E667u);
            if (((DepositMask(g) >> b) & 1u) == 0u) return 0;
            // One ellipsoid per brick, centred in it, in DOUBLED brick-local units
            // (voxel v's centre is 2v + 1 - 8, so -7..7): half-axes 2.5-3.5 voxels,
            // never leaving the brick.
            uint q = Mix(g ^ ((uint)b * 0x9E3779B1u));
            int rx = 5 + (int)(((q & 15u) * 3u) >> 4);
            int ry = 5 + (int)((((q >> 4) & 15u) * 3u) >> 4);
            int rz = 5 + (int)((((q >> 8) & 15u) * 3u) >> 4);
            int dx = ((x & 7) << 1) - 7; if (dx > rx || dx < -rx) return 0;
            int dy = ((y & 7) << 1) - 7; if (dy > ry || dy < -ry) return 0;
            int dz = ((z & 7) << 1) - 7; if (dz > rz || dz < -rz) return 0;
            long rx2 = rx * rx, ry2 = ry * ry, rz2 = rz * rz;
            long n = dx * dx * ry2 * rz2 + dy * dy * rx2 * rz2 + dz * dz * rx2 * ry2;
            long r = rx2 * ry2 * rz2;
            if (n > r) return 0;
            // Ragged rim: past 3/4 of the normalised radius squared, half the voxels.
            if (4 * n > 3 * r && (Hash(q, x, y, z) & 1u) == 0u) return 0;
            return OreOf(layer);
        }

        /// OreTable.DensityPlane's entry for voxel column (x, z): WorldRecord.At's cell rule
        /// (clamped to the map).
        public static int CellIndex(int x, int z, int cells, int cellVoxels)
        {
            int cx = x < 0 ? 0 : x / cellVoxels, cz = z < 0 ? 0 : z / cellVoxels;
            if (cx > cells - 1) cx = cells - 1;
            if (cz > cells - 1) cz = cells - 1;
            return cz * cells + cx;
        }

        /// Integer hash of (seed, x, y, z): a multiply-xor mix and a murmur3-style finaliser.
        public static uint Hash(uint seed, int x, int y, int z)
        {
            uint h = seed * 0x9E3779B1u ^ (uint)x * 0x85EBCA77u ^ (uint)y * 0xC2B2AE3Du ^ (uint)z * 0x27D4EB2Fu ^ 0x0E5A11EDu;
            return Mix(h);
        }

        public static uint Mix(uint h)
        {
            h ^= h >> 16; h *= 0x7FEB352Du;
            h ^= h >> 15; h *= 0x846CA68Bu;
            h ^= h >> 16;
            return h;
        }
    }

    /// The biome x landform ore-density table (P18) and the per-map-cell plane a
    /// chunk generator reads. Managed (CPU, built once with the world); kept out
    /// of Ores so Burst never sees a managed static.
    public static class OreTable
    {
        /// Rows: Regions archetype 0-9, then the sea (row 10). Columns: GenBiome
        /// 0-5 (forest, desert, snow, jungle, savanna, badlands). 1/16ths (16 = x1).
        /// Mountains, volcanic ground and eroded breaks are richer; river plains,
        /// dunes and wetlands poorer. = round(landform factor x biome factor / 16),
        /// landform: alpine 24, highlands 18, canyonlands 22, volcanic 26, plains 10,
        /// breaks 24, dunes 12, sea cliffs 18, plateau 20, wetland 8, sea 12;
        /// biome: forest 16, desert 16, snow 18, jungle 14, savanna 16, badlands 20.
        private static readonly byte[] PAIR = {
            //  for  des  snw  jun  sav  bad
                24,  24,  27,  21,  24,  30,   // alpine range
                18,  18,  20,  16,  18,  23,   // rolling highlands
                22,  22,  25,  19,  22,  28,   // canyonlands
                26,  26,  29,  23,  26,  33,   // volcanic
                10,  10,  11,   9,  10,  13,   // river plains
                24,  24,  27,  21,  24,  30,   // breaks
                12,  12,  14,  11,  12,  15,   // dune sea
                18,  18,  20,  16,  18,  23,   // sea cliffs
                20,  20,  23,  18,  20,  25,   // high plateau
                 8,   8,   9,   7,   8,  10,   // wetland basin
                12,  12,  14,  11,  12,  15,   // sea
        };
        public const int ROWS = 11;

        /// The multiplier (1/16ths) for archetype `arch` (Regions.*, or
        /// WorldRecord.SEA) and biome `biome`. Unknown archetype or biome -> x1.
        public static byte PairMul(byte arch, byte biome)
        {
            int row = arch == WorldRecord.SEA ? 10 : arch;
            if (row >= ROWS || biome >= GenBiome.Count) return Ores.DEFAULT_MUL;
            return PAIR[row * GenBiome.Count + biome];
        }

        /// One multiplier per map cell (row-major, cells x cells) from the world
        /// record; all zeros (no ore) when there is no record (map v3).
        public static byte[] DensityPlane(WorldRecord record, int cells)
        {
            var plane = new byte[cells * cells];
            if (record == null) return plane;
            for (int z = 0; z < cells; z++)
            for (int x = 0; x < cells; x++)
            {
                var rc = record.AtCell(x, z);
                plane[z * cells + x] = PairMul(rc.Archetype, rc.Biome);
            }
            return plane;
        }
    }
}
