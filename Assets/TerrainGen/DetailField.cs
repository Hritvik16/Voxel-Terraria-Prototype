// Assets/TerrainGen/DetailField.cs
//
// D(x, y, z) -- THE DETAIL FIELD (§4.1). 3D, run only for resident near chunks,
// on top of S. The far band never calls it.
//
//   above the column   air, or sea water up to sea level (static, at rest)
//   surface / soil     per biome; steep faces expose the strata (cliffs read
//                      as geology, §2.6); sand and powder snow only where the
//                      top is STABLE AT REST (no neighbour > 1 voxel lower)
//   strata             5 per biome, horizontal geology cut by the terrain,
//                      boundaries are noisy surfaces with a regional tilt
//   caverns            below sea - 150 m: CavernRock / Deepstone bands
//   underworld         below sea - 400 m: ash, obsidian, magma crust; an open
//                      lava sea at rest
//   caves              spaghetti tunnels anywhere > 6 m deep; cheese caverns in
//                      the cavern layer; the underworld's open volume
//   fluid pockets      sealed ellipsoids, generated AT REST: fluid below a flat
//                      level, air above; a shell no cave may breach (§2.7)
//
// THE LOD-0 CONTRACT (S's header): nothing here carves within CARVE_MIN_DEPTH of
// the column top, so the top solid voxel is always S's height. Surface cave
// mouths and fissures (§2.5) will break that for their own columns; the oracle
// then excludes exactly those columns, by construction of the stamp.
using Unity.Mathematics;

namespace TerrainGen
{
    public static class DetailField
    {
        public const int CARVE_MIN_DEPTH = 60;        // 6 m
        public const int SURFACE_THICKNESS = 3;       // 0.3 m of grass/sand/snow
        public const int POCKET_CELL_SHIFT = 9;       // one pocket candidate per 51.2 m cube
        public const int POCKET_CELL = 1 << POCKET_CELL_SHIFT;
        public const int POCKET_MAX_TOP_BELOW_SEA = 1000; // pockets sit >= 100 m below sea:
                                                          // below the deepest ocean floor (90 m)

        /// Everything D needs that varies per COLUMN, not per voxel: the 2D
        /// noise terms of the strata, soil, cavern and underworld bands. A chunk
        /// generator computes it once per column (PrepareColumn) and calls the
        /// overload below for each voxel; the result is identical by construction.
        public struct ColumnDetail
        {
            public float strataOffset;    // added to y before the strata band lookup
            public float cavernOffset;
            public float underOffset;
            public int soilDepth;
        }

        public static ColumnDetail PrepareColumn(in Offsets o, int x, int z)
        {
            float2 p = new float2(x, z);
            // Regional tilt (dip) and a wavy boundary surface: the band index is
            // a function of a displaced HEIGHT, not of displaced coordinates.
            float2 dip = new float2(noise.snoise(p / 40000f + o.dip), noise.snoise(p / 40000f + o.dip + 50f)) * 0.05f;
            var cd = new ColumnDetail();
            cd.strataOffset = dip.x * x + dip.y * z + 30f * noise.snoise(p / 2600f + o.strata);
            cd.cavernOffset = 25f * noise.snoise(p / 3000f + o.strata + 90f);
            cd.underOffset = 20f * noise.snoise(p / 1800f + o.strata - 70f);
            float n = noise.snoise(p / 300f + o.soil) * 0.5f + 0.5f;
            cd.soilDepth = 12 + (int)math.floor(n * 12f);
            return cd;
        }

        /// Convenience form: prepares the column itself (slow in a loop).
        /// `stepAtTop` is ShapeField.MaxStep at spacing 1 for this column (the
        /// falling-solid stability test).
        public static byte Material(in TerrainSpec spec, in Offsets o, in ColumnSample c, float stepAtTop,
                                    int x, int y, int z)
        {
            var cd = PrepareColumn(in o, x, z);
            return Material(in spec, in o, in c, in cd, stepAtTop, x, y, z);
        }

        public static byte Material(in TerrainSpec spec, in Offsets o, in ColumnSample c, in ColumnDetail cd,
                                    float stepAtTop, int x, int y, int z)
        {
            int h = c.height;
            if (y > h) return y <= spec.seaLevelY ? GenMat.Water : GenMat.Air;
            if (y <= spec.worldBottomY) return GenMat.Bedrock;

            int depth = h - y;
            if (depth > CARVE_MIN_DEPTH)
            {
                byte pocket = Pocket(in spec, in o, x, y, z, out bool inShell);
                if (pocket != 255) return pocket;
                if (!inShell)
                {
                    byte carved = Carve(in spec, in o, x, y, z);
                    if (carved != 255) return carved;
                }
            }

            if (y < spec.UnderworldTopY) return UnderworldRock(y + cd.underOffset);
            if (y < spec.CavernTopY) return CavernBands(y + cd.cavernOffset);

            bool steep = stepAtTop > 1.5f;
            if (!steep)
            {
                if (depth < SURFACE_THICKNESS) return Surface(in spec, in c, stepAtTop);
                if (depth < cd.soilDepth) return Soil(c.biome);
            }
            return StrataBand(c.biome, y + cd.strataOffset);
        }

        /// The material of the column's top voxel -- what the far band colours a
        /// texel with. Same rules as Material at depth 0.
        public static byte TopMaterial(in TerrainSpec spec, in ColumnSample c, float stepAtTop, in Offsets o, int x, int z)
        {
            if (c.height < spec.seaLevelY) return GenMat.Water;
            if (stepAtTop > 1.5f) return Strata(in o, c.biome, x, c.height, z);
            return Surface(in spec, in c, stepAtTop);
        }

        private static byte Surface(in TerrainSpec spec, in ColumnSample c, float stepAtTop)
        {
            bool stable = stepAtTop <= 1f;   // falling solids only where stable at rest
            // Beaches: the last couple of metres above the waterline.
            if (c.height <= spec.seaLevelY + 22 && c.height >= spec.seaLevelY - 30 && c.biome != GenBiome.Snow)
                return stable ? GenMat.BeachSand : GenMat.PackedSand;
            switch (c.biome)
            {
                case GenBiome.Desert: return stable ? GenMat.Sand : GenMat.PackedSand;
                case GenBiome.Snow: return stable ? GenMat.Snow : GenMat.PackedIce;
                case GenBiome.Jungle: return GenMat.JungleGrass;
                default: return GenMat.Grass;
            }
        }

        private static byte Soil(byte biome)
        {
            switch (biome)
            {
                case GenBiome.Desert: return GenMat.PackedSand;
                case GenBiome.Snow: return GenMat.PackedIce;
                case GenBiome.Jungle: return GenMat.Mud;
                default: return GenMat.Loam;
            }
        }

        // Band thicknesses (voxels) of one strata cycle, top band first. Adjacent
        // bands alternate light/dark in every biome's palette (§2.6).
        private static int BandThickness(int i)
        {
            switch (i)
            {
                case 0: return 55;
                case 1: return 22;
                case 2: return 40;
                case 3: return 18;
                default: return 30;
            }
        }
        private const int STRATA_PERIOD = 165;

        public static byte Strata(in Offsets o, byte biome, int x, int y, int z)
        {
            var cd = PrepareColumn(in o, x, z);
            return StrataBand(biome, y + cd.strataOffset);
        }

        private static byte StrataBand(byte biome, float g)
        {
            int gi = (int)math.floor(g);
            int m = ((gi % STRATA_PERIOD) + STRATA_PERIOD) % STRATA_PERIOD;
            int band = 0;
            while (band < 4 && m >= BandThickness(band)) { m -= BandThickness(band); band++; }
            switch (biome)
            {
                case GenBiome.Desert: return (byte)(GenMat.CreamSandstone + band);
                case GenBiome.Snow: return (byte)(GenMat.BlueSlate + band);
                case GenBiome.Jungle: return (byte)(GenMat.KarstLimestone + band);
                default: return (byte)(GenMat.Limestone + band);
            }
        }

        private static byte CavernBands(float g)
        {
            return (((int)math.floor(g) >> 6) & 1) == 0 ? GenMat.CavernRock : GenMat.Deepstone;
        }

        private static byte UnderworldRock(float g)
        {
            int band = ((int)math.floor(g) >> 5) % 3;
            return band == 0 ? GenMat.Ash : (band == 1 ? GenMat.Obsidian : GenMat.MagmaCrust);
        }

        // ------------------------------------------------------------------
        // Caves. Returns 255 when the voxel is not carved.
        // ------------------------------------------------------------------
        private static byte Carve(in TerrainSpec spec, in Offsets o, int x, int y, int z)
        {
            float3 p = new float3(x, y, z);

            // Underworld: a huge open volume over a lava sea at rest.
            if (y < spec.UnderworldTopY)
            {
                float u = noise.snoise(p / 2400f + o.under) + 0.35f * noise.snoise(p / 700f + o.under + 40f);
                int floorY = spec.worldBottomY + 300;
                if (y < spec.UnderworldTopY - 250 && u > -0.15f)
                    return y <= floorY ? GenMat.Lava : GenMat.Air;
            }
            // Cavern layer: cheese caverns.
            else if (y < spec.CavernTopY)
            {
                float cav = noise.snoise(p / 1500f + o.cavern) + 0.3f * noise.snoise(p / 450f + o.cavern + 20f);
                if (cav > 0.62f) return GenMat.Air;
            }
            // Spaghetti tunnels everywhere below 6 m: the intersection of two
            // noise zero-sets is a tube.
            float a = noise.snoise(p / 440f + o.caveA);
            float b = noise.snoise(p / 440f + o.caveB);
            if (a * a + b * b < 0.0035f) return GenMat.Air;
            return 255;
        }

        // ------------------------------------------------------------------
        // Sealed fluid pockets. One candidate per POCKET_CELL cube, centred in
        // [170, 342] with radii <= 90 (shell <= 121.5), so the pocket and its shell never leave the cell --
        // which is what lets a voxel test only its own cell.
        // Returns 255 outside every pocket; inShell = inside the no-carve shell.
        // ------------------------------------------------------------------
        public struct PocketInfo
        {
            public float3 centre, radii;
            public float level;      // flat fluid surface (absolute Y): fluid below, air above
            public byte fluid;       // GenMat fluid code
        }

        /// The pocket of pocket cell (cx, cy, cz), if it has one. Deterministic
        /// from the seed; the pocket and its no-carve shell lie inside the cell.
        public static bool TryGetPocket(in TerrainSpec spec, in Offsets o, int cx, int cy, int cz, out PocketInfo info)
        {
            info = default;
            float3 centre = new float3(cx, cy, cz) * POCKET_CELL + new float3(
                170f + 172f * Noise.Hash01(cx, cy, cz, o.seed + 1u),
                170f + 172f * Noise.Hash01(cx, cy, cz, o.seed + 2u),
                170f + 172f * Noise.Hash01(cx, cy, cz, o.seed + 3u));
            float3 radii = new float3(
                45f + 45f * Noise.Hash01(cx, cy, cz, o.seed + 4u),
                25f + 35f * Noise.Hash01(cx, cy, cz, o.seed + 5u),
                45f + 45f * Noise.Hash01(cx, cy, cz, o.seed + 6u));
            int topLimit = spec.seaLevelY - POCKET_MAX_TOP_BELOW_SEA;
            if (centre.y + radii.y * 1.35f > topLimit) return false;              // too shallow
            if (centre.y - radii.y * 1.35f < spec.UnderworldTopY) return false;   // the underworld has its lava sea
            bool cavern = centre.y < spec.CavernTopY;
            if (Noise.Hash01(cx, cy, cz, o.seed) >= (cavern ? 0.45f : 0.35f)) return false;
            float fill = 0.35f + 0.55f * Noise.Hash01(cx, cy, cz, o.seed + 7u);
            info.centre = centre;
            info.radii = radii;
            info.level = centre.y - radii.y + 2f * radii.y * fill;
            info.fluid = PocketFluid(in o, cavern, cx, cy, cz, centre);
            return true;
        }

        /// Returns 255 outside every pocket; inShell = inside the no-carve shell.
        public static byte Pocket(in TerrainSpec spec, in Offsets o, int x, int y, int z, out bool inShell)
        {
            inShell = false;
            // Power-of-two cell, arithmetic shift == floor division (CLAUDE.md's
            // bitwise coordinate rule).
            int cx = x >> POCKET_CELL_SHIFT, cy = y >> POCKET_CELL_SHIFT, cz = z >> POCKET_CELL_SHIFT;
            if (!TryGetPocket(in spec, in o, cx, cy, cz, out PocketInfo pk)) return 255;
            float3 q = (new float3(x, y, z) + 0.5f - pk.centre) / pk.radii;
            float d2 = q.x * q.x + q.y * q.y + q.z * q.z;
            if (d2 >= 1.35f * 1.35f) return 255;
            if (d2 >= 1f) { inShell = true; return 255; }                          // solid shell: falls through to rock
            return (y + 0.5f > pk.level) ? GenMat.Air : pk.fluid;                 // FLAT level: at rest
        }

        private static byte PocketFluid(in Offsets o, bool cavern, int cx, int cy, int cz, float3 centre)
        {
            float r = Noise.Hash01(cx, cy, cz, o.seed + 8u);
            if (cavern)
            {
                if (r < 0.30f) return GenMat.Acid;
                if (r < 0.60f) return GenMat.Sludge;
                if (r < 0.80f) return GenMat.Water;
                return GenMat.Lava;
            }
            byte biome = ShapeField.Biome(in o, centre.xz);
            if (biome == GenBiome.Desert) return r < 0.55f ? GenMat.Oil : (r < 0.8f ? GenMat.Quicksand : GenMat.Water);
            if (biome == GenBiome.Jungle) return r < 0.35f ? GenMat.Honey : GenMat.Water;
            return r < 0.15f ? GenMat.Oil : GenMat.Water;
        }

    }
}
