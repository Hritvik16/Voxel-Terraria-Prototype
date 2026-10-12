// Assets/TerrainGen/DetailField.cs
//
// D(x, y, z) -- THE DETAIL FIELD (§4.1). 3D, run only for resident near chunks,
// on top of S. The far band never calls it.
//
//   above the column   air, or sea water up to sea level (static, at rest)
//   surface / soil     per biome; steep faces expose the strata (cliffs read
//                      as geology, §2.6); sand and powder snow only where the
//                      top is STABLE AT REST (no neighbour > 1 voxel lower)
//   rock layers        THICK LAYERS BY DEPTH (2026-10-01, owner: "I don't like
//                      the underground bands"; game-design brief, Underground
//                      materials), each biome with its own rock in every layer:
//                        stone        surface down to sea - 50 m (+-8 m wavy)
//                        deep stone   down to sea - 150 m (the cavern top)
//                        cavern rock  down to sea - 400 m (the underworld top)
//                      The thin striped strata (5 bands cycling every 16.5 m)
//                      are gone: they were a default nobody chose and they made
//                      most underground bricks mixed (dense).
//   underworld         below sea - 400 m, SHARED by every biome: ash (top 30 m),
//                      obsidian, magma crust (20 m above the lava); an open
//                      lava sea at rest
//   caves              STRUCTURED (v3 T4, 2026-09-26; owner: no 3-D noise):
//                      three layers of meandering tunnels (the zero-lines of
//                      2-D fields, at depths set by 2-D fields; one layer
//                      follows the terrain 18-30 m down), placed caverns with a
//                      2-D floor and a domed ceiling, the underworld chamber
//                      between a 2-D floor and ceiling with pillars. All of it
//                      is per-COLUMN open intervals (PrepareColumn); a voxel
//                      costs a few comparisons.
//   ores               map v4 (gameplay step 3, Ores.cs): integer-hash blobs
//                      in layer rock deeper than 6 m, one ore per layer
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
            public float deepOffset;      // added to y at the stone / deep-stone boundary (wavy)
            public float cavernOffset;
            public float underOffset;
            public int soilDepth;
            // ---- T4 structured caves, per column ----
            /// Tunnel layer k: |line field| / width (a passage needs < 1), the
            /// passage centre (layer 0: DEPTH below the column top; layers 1-2:
            /// absolute Y) and its half height. 99 = no passage here.
            public float tun0, tun1, tun2, tunY0, tunY1, tunY2, tunH0, tunH1, tunH2;
            /// Layer 3 (T5): DEPTH below the column's REGIONAL ground, so it breaks
            /// out through valley walls and cliffs.
            public float tun3, tunY3, tunH3;
            /// Up to two cavern open intervals [lo, hi), absolute Y (lo == hi: none).
            public int cav0Lo, cav0Hi, cav1Lo, cav1Hi;
            /// The underworld chamber's open interval [lo, hi) (lo == hi: a pillar).
            public int underLo, underHi;
            /// Gameplay step 3: ore density multiplier in 1/16ths (Ores.MUL_ONE = x1),
            /// the column's biome x landform pair (OreTable.PairMul). PrepareColumn sets
            /// x1 on map v4 and 0 (no ore) below; a generator holding the world record
            /// overwrites it from OreTable.DensityPlane.
            public byte oreMul;
        }

        /// The geology terms only (layer boundary offsets, soil): what the
        /// surface and LayerRock need. No caves.
        private static ColumnDetail PrepareGeology(in Offsets o, int x, int z)
        {
            float2 p = new float2(x, z);
            // Each layer boundary is a wavy surface: the offset shifts the
            // boundary's height, column by column.
            var cd = new ColumnDetail();
            cd.deepOffset = 80f * noise.snoise(p / 2600f + o.strata);
            cd.cavernOffset = 25f * noise.snoise(p / 3000f + o.strata + 90f);
            cd.underOffset = 20f * noise.snoise(p / 1800f + o.strata - 70f);
            float n = noise.snoise(p / 300f + o.soil) * 0.5f + 0.5f;
            cd.soilDepth = 12 + (int)math.floor(n * 12f);
            return cd;
        }

        // ---- T4 constants ----
        public const float TUNNEL_SCALE = 2200f;      // voxels: meander wavelength of a passage
        public const float TUNNEL_WIDTH = 0.012f;     // line-field half width (~2-3 m passages)
        public const int CAVERN_CELL_SHIFT = 10;      // one cavern candidate per 102.4 m cell
        public const int CAVERN_CELL = 1 << CAVERN_CELL_SHIFT;
        public const int CAVERN_CELL_Y = 500;
        public const int LAVA_ABOVE_FLOOR = 300;      // the lava sea's level above the world floor

        /// Everything D needs per COLUMN: the geology terms and every cave's
        /// open interval through this column. Computed once per column.
        public static ColumnDetail PrepareColumn(in TerrainSpec spec, in Offsets o, int x, int z)
        {
            var cd = PrepareGeology(in o, x, z);
            cd.oreMul = spec.mapVersion >= 4 ? Ores.DEFAULT_MUL : (byte)0;
            float2 p = new float2(x, z);
            // Tunnels: the zero-line of a 2-D field meanders like a river in plan;
            // a second field gives its depth, a third its size, a fourth where it runs.
            cd.tun0 = TunnelLine(p, o.caveA.xy, o.caveA.z, out float h0);
            cd.tun1 = TunnelLine(p, o.caveB.xy, o.caveB.z, out float h1);
            cd.tun2 = TunnelLine(p, o.cavern.xy, o.cavern.z, out float h2);
            cd.tun3 = TunnelLine(p, o.under.xy + 43f, o.under.z, out float h3);
            cd.tunH0 = h0; cd.tunH1 = h1; cd.tunH2 = h2; cd.tunH3 = h3 * 1.3f;
            cd.tunY3 = 200f + 150f * (Noise.Fbm(p / 3000f + o.under.xy + 47f, 2) * 0.5f + 0.5f);   // below the regional ground
            cd.tunY0 = 180f + 120f * (Noise.Fbm(p / 3500f + o.caveA.xy + 11f, 2) * 0.5f + 0.5f);        // depth below the top
            cd.tunY1 = spec.seaLevelY - 500f - 300f * (Noise.Fbm(p / 4000f + o.caveB.xy + 13f, 2) * 0.5f + 0.5f);
            cd.tunY2 = spec.seaLevelY - 1100f - 300f * (Noise.Fbm(p / 4000f + o.cavern.xy + 17f, 2) * 0.5f + 0.5f);

            // Caverns: placed halls in the cavern layer, one candidate per cell.
            float nR = Noise.Fbm(p / 400f + o.cavern.xy + 23f, 2), nF = Noise.Fbm(p / 200f + o.cavern.xy + 29f, 2);
            int gx = x >> CAVERN_CELL_SHIFT, gz = z >> CAVERN_CELL_SHIFT;
            int band0 = spec.UnderworldTopY + 300;
            int cells = math.max(0, (spec.CavernTopY - 200 - band0) / CAVERN_CELL_Y);
            int found = 0;
            for (int cy = 0; cy < cells && found < 2; cy++)
            for (int j = -1; j <= 1 && found < 2; j++)
            for (int i = -1; i <= 1 && found < 2; i++)
            {
                if (!CavernInterval(in o, gx + i, cy, gz + j, band0, p, nR, nF, out int lo, out int hi)) continue;
                if (found == 0) { cd.cav0Lo = lo; cd.cav0Hi = hi; } else { cd.cav1Lo = lo; cd.cav1Hi = hi; }
                found++;
            }

            // The underworld chamber: floor and ceiling from 2-D fields; pillars
            // where the ceiling comes down to the floor.
            float pil = Noise.Fbm(p / 1800f + o.under.xy + 31f, 3);
            if (pil < 0.42f)
            {
                cd.underLo = spec.worldBottomY + 200 + (int)(150f * (Noise.Fbm(p / 3000f + o.under.xy + 37f, 3) * 0.5f + 0.5f));
                cd.underHi = spec.UnderworldTopY - 250 - (int)(250f * (Noise.Fbm(p / 2200f + o.under.xy + 41f, 3) * 0.5f + 0.5f));
            }
            return cd;
        }

        /// A tunnel layer at p: |line| / width, and the passage half height.
        private static float TunnelLine(float2 p, float2 off, float seedZ, out float halfH)
        {
            halfH = 22f + 14f * (Noise.Fbm(p / 1500f + off + seedZ + 5f, 2) * 0.5f + 0.5f);
            if (Noise.Fbm(p / 6000f + off + seedZ + 3f, 2) < -0.15f) return 99f;          // passages end and begin
            float w = TUNNEL_WIDTH * (0.7f + 0.6f * (Noise.Fbm(p / 2500f + off + seedZ + 7f, 2) * 0.5f + 0.5f));
            return math.abs(Noise.Fbm(p / TUNNEL_SCALE + off + seedZ, 3)) / w;
        }

        /// The open interval of cell (gx, cy, gz)'s cavern through column p.
        private static bool CavernInterval(in Offsets o, int gx, int cy, int gz, int band0, float2 p, float nR, float nF,
                                           out int lo, out int hi)
        {
            lo = hi = 0;
            uint sd = o.seed + 601u;
            if (Noise.Hash01(gx, cy, gz, sd) > 0.45f) return false;
            float2 c = new float2(gx + 0.3f + 0.4f * Noise.Hash01(gx, cy, gz, sd + 1u),
                                  gz + 0.3f + 0.4f * Noise.Hash01(gx, cy, gz, sd + 2u)) * CAVERN_CELL;
            float R = 200f + 220f * Noise.Hash01(gx, cy, gz, sd + 3u);
            float stretch = 1f + 0.6f * Noise.Hash01(gx, cy, gz, sd + 4u);
            float2 u = new float2(Noise.Hash01(gx, cy, gz, sd + 5u) - 0.5f, Noise.Hash01(gx, cy, gz, sd + 6u) - 0.5f);
            float ul = math.sqrt(u.x * u.x + u.y * u.y);
            u = ul > 1e-3f ? u / ul : new float2(1f, 0f);
            float2 q = p - c;
            float along = math.dot(q, u) / stretch, side = q.x * -u.y + q.y * u.x;
            float r2 = (along * along + side * side) / (R * R) * (1f + 0.25f * nR);
            if (r2 >= 1f) return false;
            float cyc = band0 + cy * CAVERN_CELL_Y + CAVERN_CELL_Y * 0.5f + (Noise.Hash01(gx, cy, gz, sd + 7u) - 0.5f) * 100f;
            float floorDepth = 60f + 60f * Noise.Hash01(gx, cy, gz, sd + 8u);
            float ceilH = 100f + 120f * Noise.Hash01(gx, cy, gz, sd + 9u);
            lo = (int)math.floor(cyc - floorDepth * (1f - 0.5f * r2) + 20f * nF);
            hi = (int)math.floor(cyc + ceilH * math.sqrt(1f - r2));
            return hi > lo + 2;
        }

        /// Convenience form: prepares the column itself (slow in a loop).
        /// `stepAtTop` is ShapeField.MaxStep at spacing 1 for this column (the
        /// falling-solid stability test).
        public static byte Material(in TerrainSpec spec, in Offsets o, in ColumnSample c, float stepAtTop,
                                    int x, int y, int z)
        {
            var cd = PrepareColumn(in spec, in o, x, z);
            return Material(in spec, in o, in c, in cd, stepAtTop, x, y, z);
        }

        public static byte Material(in TerrainSpec spec, in Offsets o, in ColumnSample c, in ColumnDetail cd,
                                    float stepAtTop, int x, int y, int z)
            => Material(in spec, in o, in c, in cd, stepAtTop, x, y, z, BrickCull.None);

        // ------------------------------------------------------------------
        // BRICK CULLING (2026-09-25, design doc §9 step 2). D is ~140 ns/voxel
        // underground because every voxel below 6 m evaluates 2-6 3D simplex
        // noises for caves. Those noises are SMOOTH at 440-2400 voxel scales,
        // so over an 8^3 brick (half-diagonal 6.93 voxels) each can change by
        // at most SNOISE_GRAD_BOUND * 6.93 / scale. One evaluation at the brick
        // centre therefore PROVES, for most bricks, that no voxel in the brick
        // can pass a carve test -- and the per-voxel code skips that test.
        //
        // EXACT, NOT APPROXIMATE: a culled Material is byte-identical to the
        // unculled one whenever SNOISE_GRAD_BOUND really bounds |grad snoise|.
        // TerrainGenTests measures the gradient (and asserts the margin) and
        // compares culled vs unculled over cave-dense chunks voxel for voxel.
        // ------------------------------------------------------------------
        public const int BRICK = 8;
        /// Upper bound on |grad noise.snoise(float3)| (per unit input). Measured
        /// max is asserted to sit below this by a 1.25x margin in the tests.
        public const float SNOISE_GRAD_BOUND = 10f;   // measured max 6.985 (1.43x margin)
        private const float BRICK_HALF_DIAG = 6.9282f;      // 4 * sqrt(3)
        private const float CULL_EPS = 1e-4f;               // float rounding of p / scale

        public struct BrickCull
        {
            public bool noPocket;        // no pocket or shell reaches the brick
            public bool noTunnel;        // spaghetti test cannot pass anywhere in it
            public bool noCheese;        // cavern-layer cheese test cannot pass
            public bool underClosed;     // underworld open-volume test cannot pass
            // V2: brick geology (TerrainSpec.brickGeologyDepth) evaluated ONCE
            // per brick, at its centre column -- the rule's own definition, so
            // passing it in is exact. Without it (None) Material prepares it
            // per voxel, which the exactness tests use as the reference.
            public bool hasGeo;
            public float geoDeep, geoCavern, geoUnder;
            public static BrickCull None => default;
        }

        /// The cull record of the 8^3 brick whose MIN corner is (bx, by, bz)
        /// (all multiples of BRICK). Valid for every voxel inside that brick.
        public static BrickCull CullBrick(in TerrainSpec spec, in Offsets o, int bx, int by, int bz)
        {
            var r = new BrickCull();

            // Pockets: a brick is inside exactly one POCKET_CELL (both power-of-two
            // aligned), so one lookup and an AABB test decide it for all 512 voxels.
            int cx = bx >> POCKET_CELL_SHIFT, cy = by >> POCKET_CELL_SHIFT, cz = bz >> POCKET_CELL_SHIFT;
            if (!TryGetPocket(in spec, in o, cx, cy, cz, out PocketInfo pk)) r.noPocket = true;
            else
            {
                float3 ext = pk.radii * 1.35f + 1f;
                float3 lo = new float3(bx, by, bz), hi = lo + BRICK;
                r.noPocket = math.any(hi < pk.centre - ext) || math.any(lo > pk.centre + ext);
            }

            // T4's caves are per-column intervals: nothing left to cull per brick.
            r.noTunnel = true; r.noCheese = true; r.underClosed = true;

            if (spec.brickGeologyDepth > 0)
            {
                var g = PrepareGeology(in o, bx + BRICK / 2, bz + BRICK / 2);
                r.hasGeo = true; r.geoDeep = g.deepOffset; r.geoCavern = g.cavernOffset; r.geoUnder = g.underOffset;
            }
            return r;
        }

        /// Material with a brick's cull record. Byte-identical to the unculled
        /// form (see the block above); `cull` must be CullBrick of the brick
        /// containing (x, y, z).
        public static byte Material(in TerrainSpec spec, in Offsets o, in ColumnSample c, in ColumnDetail cd,
                                    float stepAtTop, int x, int y, int z, in BrickCull cull)
        {
            int h = c.height;
            if (y > h)
            {
                if (y <= spec.seaLevelY) return GenMat.Water;
                if (y <= c.water) return c.waterKind == 1 ? GenMat.Lava : GenMat.Water;
                return GenMat.Air;
            }
            if (y <= spec.worldBottomY) return GenMat.Bedrock;
            if (c.lipLo != 0 && y >= c.lipLo) return GenMat.RiverLip;   // task 76

            // §2.5 stamp openings (arches, hoops, ribs, under the tree's canopy).
            // Never the top voxel (Stamps.cs header), so S stays the top solid.
            // Map v4 (task 37e): an opening below the column's still-water level
            // holds that water -- a hoop or arch standing in a lake has water
            // under it, not an air pocket the lake drains into.
            if ((y >= c.open1Lo && y < c.open1Hi) || (y >= c.open2Lo && y < c.open2Hi) || (y >= c.open3Lo && y < c.open3Hi))
                return spec.mapVersion >= 4 && y <= c.water ? (c.waterKind == 1 ? GenMat.Lava : GenMat.Water) : GenMat.Air;
            if (c.stamp3D)
            {
                byte st = Stamps.Carve3D(in spec, in o, in c, x, y, z);
                if (st != 255) return st;
            }

            // Under a cap-rock LEDGE (ShapeField.Ledge) the ground is the top of
            // the gap's floor, not the shelf: caves, tunnels and the surface /
            // soil rules measure depth from there, or the ground under a ledge
            // counts as 15 m deep and gets carved away (LedgesTest found it).
            int hg = (c.open3Hi > c.open3Lo && y < c.open3Lo) ? c.open3Lo - 1 : h;
            int depth = hg - y;
            // Map v4 (task 37e): a lake rim's fill is not ground to carve; caves
            // go by the ground under it (ColumnSample.rimGround).
            int hc = c.rimGround != 0 ? math.min(hg, c.rimGround) : hg;
            if (hc - y > CARVE_MIN_DEPTH && (c.riverFloor == 0 || y < c.riverFloor))   // task 76: none beside a river
            {
                bool inShell = false;
                if (!cull.noPocket)
                {
                    byte pocket = Pocket(in spec, in o, x, y, z, out inShell);
                    if (pocket != 255) return pocket;
                }
                if (!inShell)
                {
                    // Caverns; giant mushrooms stand inside them.
                    if ((y >= cd.cav0Lo && y < cd.cav0Hi) || (y >= cd.cav1Lo && y < cd.cav1Hi))
                    {
                        byte mush = Stamps.Mushroom(in spec, in o, x, y, z);
                        return mush != 255 ? mush : GenMat.Air;
                    }
                    // The underworld chamber over the lava sea.
                    if (y < spec.UnderworldTopY && y >= cd.underLo && y < cd.underHi)
                        return y <= spec.worldBottomY + LAVA_ABOVE_FLOOR ? GenMat.Lava : GenMat.Air;
                    // Tunnels: an elliptical passage around each layer's centre line.
                    if (InTunnel(cd.tun0, hc - cd.tunY0, cd.tunH0, y) || InTunnel(cd.tun1, cd.tunY1, cd.tunH1, y) ||
                        InTunnel(cd.tun2, cd.tunY2, cd.tunH2, y) || InTunnel(cd.tun3, c.regional - cd.tunY3, cd.tunH3, y))
                        return GenMat.Air;
                }
            }
            if (c.stampMat != 0 && y >= c.stampBodyLo) return c.stampMat;

            // V0's opt-in brick geology (TerrainSpec.brickGeologyDepth): one layer
            // decision per 8^3 brick, from the brick centre's column detail, so a
            // wavy layer boundary steps by whole bricks deep down. This form
            // re-prepares that column per voxel -- fine for measuring; a chunk
            // generator that adopts the rule passes the brick's detail in.
            if (spec.brickGeologyDepth > 0 && depth > spec.brickGeologyDepth)
            {
                int gy = (y & ~(BRICK - 1)) + BRICK / 2;
                float gDeep, gCavern, gUnder;
                if (cull.hasGeo) { gDeep = cull.geoDeep; gCavern = cull.geoCavern; gUnder = cull.geoUnder; }
                else
                {
                    var bd = PrepareGeology(in o, (x & ~(BRICK - 1)) + BRICK / 2, (z & ~(BRICK - 1)) + BRICK / 2);
                    gDeep = bd.deepOffset; gCavern = bd.cavernOffset; gUnder = bd.underOffset;
                }
                // Ores (gameplay step 3, map v4): the host layer is the brick's own.
                if (cd.oreMul != 0 && spec.mapVersion >= 4)
                {
                    byte ore = Ores.At(in spec, cd.oreMul, c.biome, Ores.Layer(in spec, gy, gDeep, gCavern, gUnder), x, y, z);
                    if (ore != 0) return ore;
                }
                return LayerRock(in spec, c.biome, gy, gDeep, gCavern, gUnder);
            }

            bool steep = stepAtTop > 1.5f;
            if (!steep && y >= spec.CavernTopY)
            {
                if (depth < SURFACE_THICKNESS) return Surface(in spec, in c, stepAtTop);
                if (depth < cd.soilDepth) return Soil(c.biome);
            }
            // Ores (gameplay step 3, map v4): only in rock deeper than 6 m (no cliff faces).
            if (cd.oreMul != 0 && depth > Ores.MIN_DEPTH && spec.mapVersion >= 4)
            {
                byte ore = Ores.At(in spec, cd.oreMul, c.biome, Ores.Layer(in spec, y, cd.deepOffset, cd.cavernOffset, cd.underOffset), x, y, z);
                if (ore != 0) return ore;
            }
            return LayerRock(in spec, c.biome, y, cd.deepOffset, cd.cavernOffset, cd.underOffset);
        }

        /// The material of the column's top voxel -- what the far band colours a
        /// texel with. Same rules as Material at depth 0.
        public static byte TopMaterial(in TerrainSpec spec, in ColumnSample c, float stepAtTop, in Offsets o, int x, int z)
        {
            if (c.height < spec.seaLevelY) return GenMat.Water;
            if (c.water > c.height) return c.waterKind == 1 ? GenMat.Lava : GenMat.Water;
            if (c.lipLo != 0 && c.height >= c.lipLo) return GenMat.RiverLip;   // task 76
            if (c.stampMat != 0 && c.height >= c.stampBodyLo) return c.stampMat;
            if (stepAtTop > 1.5f) return Strata(in spec, in o, c.biome, x, c.height, z);
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
                case GenBiome.Savanna: return GenMat.SavannaGrass;
                case GenBiome.Badlands: return GenMat.BadlandsClay;
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
                case GenBiome.Savanna: return GenMat.RedEarth;
                case GenBiome.Badlands: return GenMat.Hardpan;
                default: return GenMat.Loam;
            }
        }

        /// Stone ends this far below sea level (voxels; the wavy deepOffset moves
        /// the boundary +-8 m). Deep stone runs from there to the cavern top.
        public const int STONE_BOTTOM_BELOW_SEA = 500;

        /// The rock at height y in a column (no surface, soil or caves): the
        /// thick depth layers, the biome's own rock in each, the shared
        /// underworld at the bottom. Exposed cliffs show this too.
        public static byte Strata(in TerrainSpec spec, in Offsets o, byte biome, int x, int y, int z)
        {
            var cd = PrepareGeology(in o, x, z);
            return LayerRock(in spec, biome, y, cd.deepOffset, cd.cavernOffset, cd.underOffset);
        }

        private static byte LayerRock(in TerrainSpec spec, byte biome, float y, float deepOff, float cavernOff, float underOff)
        {
            if (y + underOff < spec.UnderworldTopY) return UnderworldRock(in spec, y + underOff);
            if (y + cavernOff < spec.CavernTopY) return BiomeRock(biome, 2);
            if (y + deepOff < spec.seaLevelY - STONE_BOTTOM_BELOW_SEA) return BiomeRock(biome, 1);
            if (biome == GenBiome.Badlands) return BadlandsBand((int)y);
            return BiomeRock(biome, 0);
        }

        /// Badlands' exposed rock (B10): flat bands by height, 3.2 m each
        /// (y >> 5), in an 8-band, 25.6 m cycle: red, red, cream, ochre,
        /// ochre, red, cream, red. Cliffs and the far band's steep texels
        /// (TopMaterial -> Strata) show the stripes.
        public static byte BadlandsBand(int y)
        {
            switch ((y >> 5) & 7)
            {
                case 2: case 6: return GenMat.CreamBand;
                case 3: case 4: return GenMat.OchreBand;
                default: return GenMat.RedBand;
            }
        }

        /// Each biome's rock per layer (0 stone, 1 deep stone, 2 cavern rock),
        /// from its palette-D set: a mid-tone stone near the surface, darker
        /// rock below. The biome's other two rocks are kept for veins and
        /// accents (object placement). Recolouring is a palette change only.
        public static byte BiomeRock(byte biome, int layer)
        {
            switch (biome)
            {
                case GenBiome.Desert: return layer == 0 ? GenMat.OrangeSandstone : (layer == 1 ? GenMat.RedSandstone : GenMat.MaroonMudstone);
                case GenBiome.Snow:   return layer == 0 ? GenMat.SilverSchist : (layer == 1 ? GenMat.BlueSlate : GenMat.Basalt);
                case GenBiome.Jungle: return layer == 0 ? GenMat.KarstLimestone : (layer == 1 ? GenMat.MossRock : GenMat.BlackBasalt);
                case GenBiome.Savanna: return layer == 0 ? GenMat.KopjeGranite : (layer == 1 ? GenMat.Ironstone : GenMat.Gneiss);
                case GenBiome.Badlands: return layer == 0 ? GenMat.RedBand : (layer == 1 ? GenMat.UmberMudstone : GenMat.PlumShale);
                default:              return layer == 0 ? GenMat.Granite : (layer == 1 ? GenMat.Slate : GenMat.Shale);
            }
        }

        /// The shared underworld (every biome): ash in its top 30 m, magma crust
        /// in the 20 m above the lava sea, obsidian between.
        private static byte UnderworldRock(in TerrainSpec spec, float g)
        {
            if (g < spec.worldBottomY + LAVA_ABOVE_FLOOR + 200) return GenMat.MagmaCrust;
            if (g >= spec.UnderworldTopY - 300) return GenMat.Ash;
            return GenMat.Obsidian;
        }

        // ------------------------------------------------------------------
        // Caves (T4): see PrepareColumn. `t` = |line| / width, yc the centre.
        private static bool InTunnel(float t, float yc, float halfH, int y)
        {
            if (t >= 1f) return false;
            float v = (y - yc) / halfH;
            return t * t + v * v < 1f;
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
