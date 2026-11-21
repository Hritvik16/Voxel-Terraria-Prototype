// Assets/TerrainGen/OreRules.cs
//
// TASK 181 -- RESOURCE DEPOSITS BY RULE (placement plan, owner-approved
// 2026-11-06 incl. batch 2; TerrainSpec.oresOn, the engine chunk source's
// DEFAULT since task 195 (owner 2026-11-13); `-ores 0` = OFF; map v4 only).
// OFF keeps Ores.cs's step-3 placement byte for byte.
//
// The five-tier ladder is Ores.cs's, unchanged: copper (stone pick), gold
// (copper pick), iron (gold pick), cobalt (iron pick), emberstone (cobalt pick).
// Host layers are Ores.cs's too: copper in stone, iron in deep stone above
// sea - 100 m, gold below it, cobalt in cavern rock, emberstone in the underworld.
//
// FOUR SOURCES (brief "Ores and materials", Decided), every input is generator
// data -- the world record per 8 m map cell, the column sample, the voxel's
// depth. No seed constant anywhere: the seed only feeds the integer hashes.
//
//   1. DEPTH       Ores.BaseChance per layer, rarer deeper (copper 3300 ..
//                  emberstone 820 per 65,536 per 32^3 cell), as step 3.
//   2. BIOME x     Ores' PairMul (landform x biome) and the favoured ore x2, as
//      LANDFORM    step 3, now scaled by the pair's HARSHNESS = Danger's biome
//                  tier + Regions.ClassOf landform class (0-4, Danger.Tiers.Default):
//                    copper, gold  x 16 14 12 10 8 /16  (common on easy, mild land)
//                                  and x 1.25 on easy cells, x 1.25 on mild biomes
//                    iron          x 1 everywhere
//                    cobalt        only harshness >= 3:  x 0 0 0 40 56 /16
//                    emberstone    only harshness 4:     x 0 0 0 0 80 /16
//                  Harshness 4 = jungle / desert / snow on alpine, volcanic or
//                  sea-cliff land; 3 = those on broken land, or badlands on steep.
//   3. WALL VEINS  thin tilted lenses in the face of every cliff and canyon wall:
//                  a column whose spacing-1 step (the LOD-0 MaxStep Material
//                  already takes) exceeds VEIN_STEP 1.5 (56 deg) carries veins
//                  down to (step + VEIN_MARGIN) voxels below its top -- the part
//                  of the column the face exposes, plus 2 m. One vein at most
//                  per 64^3 cell (6.4 m); chance per biome (badlands x3: "ore
//                  veins in their walls"); ore from the biome's vein table (never
//                  a latent ore). None on route-corridor cells, none below the
//                  cell's water floor (sea / the 5x5 cells' still water + 1 m).
//   4. RICH        deposit chance x2 and veins x2 on signature-feature cells.
//
// THE START (owner R1 (b)): the first copper is UNDERGROUND only. On the start
// zone's cells (the play start's easy area, both start rules, plus every cell
// within START_REACH 128 m of either start column): no veins, and a deposit
// needs depth > MIN_DEPTH + step + 1 below its column top, in a column with no
// stamp opening, lip or ledge -- so no neighbouring column's top (|dh| <= step)
// and nothing open in the column can see it. Caves and tunnels still cut it.
//
// LATENT ORES (owner R2 (a)): cobalt and emberstone are generated at world
// creation as a TWIN material per host rock: the twin's colour, hardness, tier
// and drop are the host's until the world stage (world.state, task 101)
// reaches UnlockStage (cobalt 1, emberstone 2); then they are the ore's.
// Nothing is rewritten: the stored byte never changes; content (Tools, Items,
// MaterialPalette, the shader's _WorldStage) resolves it. 9 twins: cobalt in
// each biome's cavern rock (6), emberstone in ash / obsidian / magma crust (3).
// A host with no twin gets no latent ore (deterministic, never happens on the
// shipped hosts).
//
// INTEGER HASHING ONLY, as Ores.cs: placement is a pure function of (seed,
// cell plane, column sample, voxel); Burst and Mono agree bit for bit.
using Unity.Mathematics;

namespace TerrainGen
{
    public static class OreRules
    {
        // ---- the per-map-cell plane (CellPlane), one uint per 8 m cell ----
        public const uint HARSH_MASK = 7u;        // bits 0-2: harshness 0-4
        public const uint START = 1u << 3;        // the start zone (R1 (b))
        public const uint FEATURE = 1u << 4;      // signature feature: rich
        public const uint EASY = 1u << 5;         // FLAG_EASY
        public const uint ROUTE = 1u << 6;        // route corridor: no wall veins
        public const uint VALID = 1u << 7;        // a cell with a record (land or sea); 0 = no rule ore
        public const int FLOOR_SHIFT = 8;         // bits 8-23: lowest y a vein may take (absolute voxels)
        public const uint MILD = 1u << 24;        // Danger biome tier 0 (forest, savanna)

        public const int MAX_HARSH = 4;
        public const int RICH = 2;                       // deposit chance on feature cells
        public const int START_REACH = 1280;             // voxels: 128 m, the NEAR band (8.15)
        public const float VEIN_STEP = 1.5f;             // column step that makes a wall face
        public const int VEIN_MARGIN = 20;               // voxels below the exposed part of the face
        public const int VEIN_CELL_SHIFT = 6;            // 64^3 voxel vein cells
        public const int VEIN_BASE = 9000;               // per 65,536 per vein cell, x1 biome
        public const int VEIN_WATER_CLEAR = 10;          // voxels above the nearby still water
        public const int VEIN_WATER_REACH = 2;           // map cells: the 5x5 water floor

        // ---- latent twins (GenMat codes, append-only; free codes below 96) ----
        public const byte LatentCobaltShale = 26, LatentCobaltMudstone = 27, LatentCobaltBasalt = 28,
                          LatentCobaltBlackBasalt = 29, LatentCobaltGneiss = 38, LatentCobaltPlumShale = 39;
        public const byte LatentEmberAsh = 94, LatentEmberObsidian = 95, LatentEmberMagma = 59;
        public const byte COBALT_STAGE = 1, EMBER_STAGE = 2;

        public static bool IsLatentOre(byte ore) => ore == Ores.Cobalt || ore == Ores.Emberstone;
        public static byte UnlockStage(byte ore) => ore == Ores.Cobalt ? COBALT_STAGE : ore == Ores.Emberstone ? EMBER_STAGE : (byte)0;

        /// The twin of latent `ore` in `host`, or 0 when that host has none.
        public static byte Twin(byte ore, byte host)
        {
            if (ore == Ores.Cobalt)
                switch (host)
                {
                    case GenMat.Shale: return LatentCobaltShale;
                    case GenMat.MaroonMudstone: return LatentCobaltMudstone;
                    case GenMat.Basalt: return LatentCobaltBasalt;
                    case GenMat.BlackBasalt: return LatentCobaltBlackBasalt;
                    case GenMat.Gneiss: return LatentCobaltGneiss;
                    case GenMat.PlumShale: return LatentCobaltPlumShale;
                }
            else if (ore == Ores.Emberstone)
                switch (host)
                {
                    case GenMat.Ash: return LatentEmberAsh;
                    case GenMat.Obsidian: return LatentEmberObsidian;
                    case GenMat.MagmaCrust: return LatentEmberMagma;
                }
            return 0;
        }

        public static bool IsTwin(byte m) => TwinOre(m) != 0;

        /// The ore a twin holds (0 = not a twin).
        public static byte TwinOre(byte m)
        {
            switch (m)
            {
                case LatentCobaltShale: case LatentCobaltMudstone: case LatentCobaltBasalt:
                case LatentCobaltBlackBasalt: case LatentCobaltGneiss: case LatentCobaltPlumShale: return Ores.Cobalt;
                case LatentEmberAsh: case LatentEmberObsidian: case LatentEmberMagma: return Ores.Emberstone;
            }
            return 0;
        }

        /// Any ore the rules place, the twins included.
        public static bool IsRuleOre(byte m) => Ores.IsOre(m) || IsTwin(m);

        // ---- deposits ----

        // copper (0), iron (1), gold (2), cobalt (3), emberstone (4) x harshness 0-4, 1/16ths
        private static int LayerHarsh(int layer, int harsh)
        {
            switch (layer)
            {
                case 0: case 2: return harsh == 0 ? 16 : harsh == 1 ? 14 : harsh == 2 ? 12 : harsh == 3 ? 10 : 8;
                case 1: return 16;
                case 3: return harsh == 3 ? 40 : harsh >= 4 ? 56 : 0;
                default: return harsh >= 4 ? 80 : 0;
            }
        }

        /// Deposit chance per 32^3 cell (out of 65,536) for host layer `layer`.
        public static int CellChance(int layer, byte biome, int oreMul, uint cell)
        {
            if ((cell & VALID) == 0 || oreMul <= 0) return 0;
            int harsh = (int)(cell & HARSH_MASK);
            long p = (long)Ores.BaseChance(layer) * oreMul / Ores.MUL_ONE;
            if (Ores.FavouredLayer(biome) == layer) p *= 2;
            p = p * LayerHarsh(layer, harsh) / 16;
            if (layer == 0 || layer == 2)
            {
                if ((cell & EASY) != 0) p = p * 5 / 4;
                if ((cell & MILD) != 0) p = p * 5 / 4;
            }
            if ((cell & FEATURE) != 0) p *= RICH;
            return p > 65536 ? 65536 : (int)p;
        }

        /// The start zone's deposit rule: deep enough that no neighbouring top and
        /// nothing open in the column can see it.
        public static bool StartHides(in ColumnSample c, float stepAtTop, int depth)
        {
            if (c.open1Hi > c.open1Lo || c.open2Hi > c.open2Lo || c.open3Hi > c.open3Lo) return false;
            if (c.stamp3D || c.stampMat != 0 || c.lipLo != 0) return false;
            return depth > Ores.MIN_DEPTH + (int)math.ceil(stepAtTop) + 1;
        }

        // ---- wall veins ----

        public static int Floor(uint cell) => (int)((cell >> FLOOR_SHIFT) & 0xFFFFu);

        /// The column may carry a vein voxel at depth `depth` (>= 0), height y.
        public static bool VeinColumn(uint cell, float stepAtTop, int depth, int y)
        {
            if ((cell & VALID) == 0 || (cell & (START | ROUTE)) != 0) return false;
            if (!(stepAtTop > VEIN_STEP)) return false;
            return depth <= (int)stepAtTop + VEIN_MARGIN && y >= Floor(cell);
        }

        /// Per-biome vein chance (1/16) and four ore slots (2 hash bits pick one).
        private static int VeinMul(byte biome)
        {
            switch (biome)
            {
                case GenBiome.Desert: return 20;
                case GenBiome.Jungle: return 12;
                case GenBiome.Badlands: return 48;
                default: return 16;   // forest, snow, savanna
            }
        }

        private static byte VeinOre(byte biome, uint k)
        {
            k &= 3u;
            switch (biome)
            {
                case GenBiome.Desert: return k < 2 ? Ores.Gold : Ores.Copper;
                case GenBiome.Snow: return k < 2 ? Ores.Iron : Ores.Copper;
                case GenBiome.Jungle: return k == 0 ? Ores.Gold : Ores.Copper;
                case GenBiome.Savanna: return k == 0 ? Ores.Iron : Ores.Copper;
                case GenBiome.Badlands: return k == 1 ? Ores.Gold : k == 2 ? Ores.Iron : Ores.Copper;
                default: return k == 3 ? Ores.Iron : Ores.Copper;   // forest
            }
        }

        public static int VeinChance(byte biome, uint cell)
        {
            long p = (long)VEIN_BASE * VeinMul(biome) / 16;
            if ((cell & FEATURE) != 0) p *= 2;
            return p > 65536 ? 65536 : (int)p;
        }

        private const uint VEIN_SALT = 0x5EED0B5Eu;   // a hash salt (not a seed): keeps veins off the deposit hash

        /// The vein ore at voxel (x, y, z), or 0. The caller checked VeinColumn.
        public static byte Vein(in TerrainSpec spec, byte biome, uint cell, int x, int y, int z)
        {
            int cx = x >> VEIN_CELL_SHIFT, cy = y >> VEIN_CELL_SHIFT, cz = z >> VEIN_CELL_SHIFT;
            uint h = Ores.Hash(spec.seed ^ VEIN_SALT, cx, cy, cz);
            if ((int)(h & 0xFFFFu) >= VeinChance(biome, cell)) return 0;
            uint g = Ores.Mix(h ^ 0xBB67AE85u);
            // centre in [28, 36) per axis, long radius 18-28, short 8-15 (voxels): inside the cell
            int dx = (x & 63) - (28 + (int)(g & 7u));
            int dy = (y & 63) - (28 + (int)((g >> 3) & 7u));
            int dz = (z & 63) - (28 + (int)((g >> 6) & 7u));
            int nx = (int)((g >> 9) & 15u) - 7, nz = (int)((g >> 13) & 15u) - 7;    // tilt up to ~23 deg
            int t = 2 + (int)((g >> 17) & 1u);                                         // half-thickness 2-3
            long w = (long)dx * nx + (long)dy * 16 + (long)dz * nz;
            long tw = t * 16 + 8;
            if (w > tw || w < -tw) return 0;
            int dir = (int)((g >> 18) & 3u);
            int ux = dir == 0 ? 16 : dir == 1 ? 11 : dir == 2 ? 0 : -11;
            int uz = dir == 0 ? 0 : dir == 1 ? 11 : dir == 2 ? 16 : 11;
            long u = (long)dx * ux + (long)dz * uz, v = (long)dz * ux - (long)dx * uz;
            int ra = 18 + (int)(((g >> 20) & 15u) * 11u >> 4), rb = 8 + (int)((g >> 24) & 7u);
            long a2 = (long)ra * ra, b2 = (long)rb * rb;
            long n = u * u * b2 + v * v * a2, r = 256L * a2 * b2;
            if (n > r) return 0;
            uint q = Ores.Hash(g, x, y, z);
            // ragged: the outer quarter of the lens and the slab's outer voxel keep half
            if ((4 * n > 3 * r || w > tw - 16 || w < 16 - tw) && (q & 1u) == 0u) return 0;
            return VeinOre(biome, g >> 28);
        }

        /// The rule ore at a rock voxel, or 0. `layer` is Ores.Layer at the voxel
        /// (the brick's, deep down); `depth` below the column's ground. A latent
        /// ore comes back as the ORE; the caller turns it into its host's twin.
        public static byte At(in TerrainSpec spec, in ColumnSample c, byte oreMul, uint cell, float stepAtTop,
                              int depth, int layer, int x, int y, int z)
        {
            if ((cell & VALID) == 0) return 0;
            if (depth > Ores.MIN_DEPTH && ((cell & START) == 0 || StartHides(in c, stepAtTop, depth)))
            {
                byte ore = Ores.AtChance(in spec, CellChance(layer, c.biome, oreMul, cell), layer, x, y, z);
                if (ore != 0) return ore;
            }
            if (VeinColumn(cell, stepAtTop, depth, y)) return Vein(in spec, c.biome, cell, x, y, z);
            return 0;
        }

    }

    /// The managed side (built once per world, tests, content): kept out of
    /// OreRules so Burst never sees a managed static (as OreTable / Ores).
    public static class OreRulePlane
    {
        /// The twins as data: (twin, ore, host). Content mirrors it (LatentOres).
        public static readonly byte[] TWINS = {
            OreRules.LatentCobaltShale, Ores.Cobalt, GenMat.Shale,
            OreRules.LatentCobaltMudstone, Ores.Cobalt, GenMat.MaroonMudstone,
            OreRules.LatentCobaltBasalt, Ores.Cobalt, GenMat.Basalt,
            OreRules.LatentCobaltBlackBasalt, Ores.Cobalt, GenMat.BlackBasalt,
            OreRules.LatentCobaltGneiss, Ores.Cobalt, GenMat.Gneiss,
            OreRules.LatentCobaltPlumShale, Ores.Cobalt, GenMat.PlumShale,
            OreRules.LatentEmberAsh, Ores.Emberstone, GenMat.Ash,
            OreRules.LatentEmberObsidian, Ores.Emberstone, GenMat.Obsidian,
            OreRules.LatentEmberMagma, Ores.Emberstone, GenMat.MagmaCrust,
        };

        /// The rock a twin looks like while latent (0 = not a twin).
        public static byte TwinHost(byte m)
        {
            for (int i = 0; i < TWINS.Length; i += 3) if (TWINS[i] == m) return TWINS[i + 2];
            return 0;
        }

        // ---- the plane ----

        /// Harshness of a record cell: Danger's biome tier + the landform class.
        public static int Harshness(in RecordCell rc)
        {
            byte bt = rc.Biome < Danger.Default.biome.Length ? Danger.Default.biome[rc.Biome] : Danger.NONE;
            if (bt == Danger.NONE) bt = 0;
            int lc = rc.Archetype < 10 ? Regions.ClassOf(rc.Archetype) : 0;
            int h = bt + lc;
            return h > OreRules.MAX_HARSH ? OreRules.MAX_HARSH : h;
        }

        /// One uint per map cell (row-major); all zeros (no rule ore) without a
        /// record (map v3). Managed, built once per world.
        public static uint[] CellPlane(WorldMap map, in TerrainSpec spec)
        {
            int n = spec.mapCells;
            var plane = new uint[n * n];
            var rec = map.Record;
            if (rec == null) return plane;
            // the start zone: both start rules' areas and columns
            var zone = new bool[n * n];
            for (int mild = 0; mild < 2; mild++)
            {
                if (!PlayStart.Find(map, out var pick, mild == 1)) continue;
                if (pick.area >= 0 && pick.area < rec.EasyAreaList.Length && rec.EasyAreaList[pick.area].cells != null)
                    foreach (int ci in rec.EasyAreaList[pick.area].cells) zone[ci] = true;
                int r = OreRules.START_REACH / spec.mapCellVoxels + 1;
                int pcx = pick.column.x / spec.mapCellVoxels, pcz = pick.column.y / spec.mapCellVoxels;
                for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int cx = pcx + dx, cz = pcz + dz;
                    if (cx < 0 || cz < 0 || cx >= n || cz >= n) continue;
                    // the cell's nearest point to the start column
                    long qx = math.clamp(pick.column.x, cx * spec.mapCellVoxels, cx * spec.mapCellVoxels + spec.mapCellVoxels - 1) - pick.column.x;
                    long qz = math.clamp(pick.column.y, cz * spec.mapCellVoxels, cz * spec.mapCellVoxels + spec.mapCellVoxels - 1) - pick.column.y;
                    if (qx * qx + qz * qz <= (long)OreRules.START_REACH * OreRules.START_REACH) zone[cz * n + cx] = true;
                }
            }
            float[] water = map.Water;
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                var rc = rec.AtCell(x, z);
                // the sea floor: harshness 0, step 3's sea row of PairMul (no cobalt / emberstone)
                uint v = OreRules.VALID | (rc.IsSea ? 0u : (uint)Harshness(in rc));
                if (zone[z * n + x]) v |= OreRules.START;
                if (rc.IsFeature) v |= OreRules.FEATURE;
                if (rc.IsEasy) v |= OreRules.EASY;
                if (rc.IsRoute) v |= OreRules.ROUTE;
                if (!rc.IsSea && rc.Biome < Danger.Default.biome.Length && Danger.Default.biome[rc.Biome] == 0) v |= OreRules.MILD;
                float wl = 0f;   // rel. sea: never below the sea
                for (int dz = -OreRules.VEIN_WATER_REACH; dz <= OreRules.VEIN_WATER_REACH; dz++)
                for (int dx = -OreRules.VEIN_WATER_REACH; dx <= OreRules.VEIN_WATER_REACH; dx++)
                {
                    int cx = x + dx, cz = z + dz;
                    if (cx < 0 || cz < 0 || cx >= n || cz >= n) continue;
                    float w = water[cz * n + cx];
                    if (w > Hydrology.NO_WATER * 0.5f) wl = math.max(wl, w);
                }
                int floorY = spec.seaLevelY + (int)math.ceil(wl) + OreRules.VEIN_WATER_CLEAR;
                v |= (uint)math.clamp(floorY, 0, 65535) << OreRules.FLOOR_SHIFT;
                plane[z * n + x] = v;
            }
            return plane;
        }
    }
}
