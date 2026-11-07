// Assets/TerrainGen/FluidPockets.cs
//
// TASK 183 -- FLUID POCKETS AT REST (placement plan step 4, owner-approved
// 2026-11-06 incl. batch 2; behind `-fluidpockets 1` = TerrainSpec.fluidPocketsOn,
// DEFAULT OFF; map v4 only). OFF keeps every byte as before. The fluid
// simulation is UNCHANGED: pockets are generated terrain, like the lakes and the
// caldera's lava, and only move once something disturbs them.
//
// TWO KINDS, both decided by generator data (the world record per 8 m cell, the
// column sample, depth), never by a place or a seed constant -- the seed only
// feeds the integer hashes:
//
//  1. SURFACE POCKETS (new). One candidate per 51.2 m PLANE CELL (512 voxels,
//     power of two: a column finds its site by >> CELL_SHIFT). The site is a
//     small elliptical basin dug into flat dry ground, its fluid at a flat level
//     L below the surrounding ground. Rules, first match wins:
//       volcanic land (Regions.Volcanic)            lava (1 in 4 acid)  -- the ashlands' pools
//       badlands biome                              oil                 -- the tar seep
//       desert biome                                quicksand           -- the sinking basin
//       jungle, a valley or wetland                 sludge              -- the bog
//       jungle, other land                          honey               -- the hive pools
//       a VALLEY (ground >= VALLEY below its +-120 m regional mean) or wetland,
//       in forest / savanna / snow                  water
//     Every cell the basin and its rim touch must be dry land (not sea, >= 32 m
//     from any lake or river cell), Walk band, not a route corridor, not a
//     signature feature, not the start zone (either start rule's easy area +
//     START_REACH round its column). Samples of the base terrain on the rim must
//     be dry, stamp-free, river-free, above the sea and within FLAT_SPAN of each
//     other; L = their lowest - 1.
//     AT REST BY CONSTRUCTION (ShapeField.Sample calls Apply on every path --
//     LOD 0, shells, the point query, the far band -- so they all agree):
//       footprint column  height := L - depth(col) (a smooth bowl, >= 1 voxel of
//                         fluid at the edge); fluid fills (height, L]
//       rim column        (not in the footprint, one of its 8 neighbours is):
//                         height := max(height, L + 1) -- a berm only where the
//                         base ground dips below L + 1, so every fluid voxel's
//                         side neighbour is fluid or solid
//       both              LINED with a material the fluid does not react with
//                         (Lining): a thin bed -- every solid voxel that can share
//                         a face with a fluid voxel (down to the lowest footprint
//                         neighbour's floor; the rim up to L + 1) plus one voxel
//                         under the column's own floor. No fluid voxel
//                         touches a reactive face (acid eats rock, lava glasses
//                         sand / melts snow, water muds loam -- MaterialRules);
//                         caves carve only below L - D - CAP_MARGIN (riverFloor,
//                         the river's own mechanism); stamp openings / ledges off.
//     That is "no carving beyond the pocket basin": the only removed ground is
//     the bowl; the berm and the lining are additions / repaints.
//
//  2. DEEP POCKETS (DetailField's sealed ellipsoids, unchanged in shape and
//     place). With the flag on their fluid follows rules too (DeepFluid):
//     volcanic land above them -> lava (magma chambers); the deeper half of the
//     cavern layer -> lava; the cavern layer: jungle sludge, desert / badlands
//     acid, snow water, forest / savanna a mix (the poisonous cavern lake); the
//     stone layers: badlands oil (oil under rock caps), desert oil / water,
//     jungle honey / water, snow water, forest / savanna water / some oil. The
//     inner wall is lined (DeepLining) so an acid pocket does not eat its own
//     wall -- the one deep pocket that was NOT at rest before.
//
// STEAM is not generated. It would be "natural" only where lava meets water,
// and there the reaction makes it (water + lava -> steam + cooled stone). A
// generated steam pocket at rest would need its own GenMat and a far-band
// mapping for no gameplay gain; skipped (task 183 says so).
//
// INTEGER footprint tests (an axis-aligned ellipse with integer radii), so the
// seal is exact on every path. The site plane itself is built once per world on
// the main thread from the base terrain (Sample with no plane) and stored, so no
// path re-derives L.
using Unity.Mathematics;

namespace TerrainGen
{
    public static unsafe class FluidPockets
    {
        public const int CELL_SHIFT = 9;                 // 512 voxels = 51.2 m plane cells
        public const int CELL = 1 << CELL_SHIFT;
        public const int STRIDE = 4;                     // ints per plane cell
        public const int SITE_MARGIN = 128;              // centre this far inside its cell: basin + rim stay inside
        public const int FLAT_SPAN = 24;                 // voxels: rim samples' spread (2.4 m)
        public const int VALLEY = 40;                    // voxels below the regional mean: a valley
        public const int CAP_MARGIN = 8;                 // caves stay this far under the deepest lining
        public const int SEA_CLEAR = 20;                 // basin floor this far above the sea
        public const int FRESH_CLEAR = 2;                // record freshWater steps (16 m) from any lake / river cell
        public const int START_REACH = OreRules.START_REACH;   // 128 m, the NEAR band (8.15)

        public const int CTX_SEA = 15;                   // deep context of a sea cell (no landform rule)

        // packed site word [3]: bits 0-7 fluid GenMat, 8-15 rx, 16-23 rz, 24-30 depth D
        // word [0]: bits 0-23 centre x, bits 24-27 deep context (archetype + 1, CTX_SEA under the sea; 0 = none)

        /// Plane cells per edge.
        public static int PlaneCells(in TerrainSpec spec) => (spec.mapCells * spec.mapCellVoxels + CELL - 1) >> CELL_SHIFT;

        /// The fluid a column's still level holds. waterKind 0 = water, 1 = lava
        /// (the caldera, every map since v2); any other value IS the GenMat fluid
        /// code (task 183's pockets).
        public static byte FluidOf(byte waterKind) => waterKind == 0 ? GenMat.Water : waterKind == 1 ? GenMat.Lava : waterKind;

        /// What lines a SURFACE pocket: a material its fluid never reacts with,
        /// that does not fall, and that reads as the pocket's bed.
        public static byte Lining(byte fluid)
        {
            switch (fluid)
            {
                case GenMat.Water: return GenMat.Mud;          // water + loam / red earth -> mud: the bed already is
                case GenMat.Acid: return GenMat.Sinter;        // acid-proof mineral crust (MaterialRules: no Dissolves)
                default: return GroundRules.FluidShore(fluid); // lava magma crust, oil shale, sludge mud, honey ochre band, quicksand packed sand
            }
        }

        /// A DEEP pocket's inner wall: 0 = the rock itself (water reacts with no rock).
        public static byte DeepLining(byte fluid) => fluid == GenMat.Water ? (byte)0 : Lining(fluid);

        // ------------------------------------------------------------------
        // The per-column step (ShapeField.Sample, last before the cover rules)
        // ------------------------------------------------------------------

        public static bool InFoot(int dx, int dz, int rx, int rz)
        {
            long rx2 = (long)rx * rx, rz2 = (long)rz * rz;
            return (long)dx * dx * rz2 + (long)dz * dz * rx2 < rx2 * rz2;
        }

        /// Fluid depth at a footprint column (1 at the rim .. D at the centre).
        public static int DepthAt(int dx, int dz, int rx, int rz, int d)
        {
            long rx2 = (long)rx * rx, rz2 = (long)rz * rz, den = rx2 * rz2;
            long num = (long)dx * dx * rz2 + (long)dz * dz * rx2;
            return 1 + (int)(((long)(d - 1) * (den - num)) / den);
        }

        public static void Apply(in TerrainSpec spec, in MapView map, float x, float z, ref ColumnSample c)
        {
            if (map.pocket == null || !spec.Pockets) return;
            int ix = (int)math.floor(x), iz = (int)math.floor(z);
            int n = PlaneCells(in spec);
            int pcx = ix >> CELL_SHIFT, pcz = iz >> CELL_SHIFT;
            if ((uint)pcx >= (uint)n || (uint)pcz >= (uint)n) return;
            int* e = map.pocket + (pcz * n + pcx) * STRIDE;
            c.pkCtx = (byte)((e[0] >> 24) & 15);
            uint s = (uint)e[3];
            if (s == 0u) return;
            int cx = e[0] & 0xFFFFFF, cz = e[1], L = e[2];
            byte fluid = (byte)(s & 255u);
            int rx = (int)((s >> 8) & 255u), rz = (int)((s >> 16) & 255u), d = (int)((s >> 24) & 127u);
            int dx = ix - cx, dz = iz - cz;
            if (dx < -rx - 1 || dx > rx + 1 || dz < -rz - 1 || dz > rz + 1) return;
            // The lining: every solid voxel with a face on a fluid voxel, and one more under
            // the bed. Fluid fills (floor, L] of each footprint column, so a column's solid
            // voxels down to its lowest footprint neighbour's floor + 1 can touch fluid.
            int lowNb = int.MaxValue;
            for (int oz = -1; oz <= 1; oz++)
            for (int ox = -1; ox <= 1; ox++)
                if ((ox != 0 || oz != 0) && InFoot(dx + ox, dz + oz, rx, rz))
                    lowNb = math.min(lowNb, L - DepthAt(dx + ox, dz + oz, rx, rz, d));
            if (InFoot(dx, dz, rx, rz))
            {
                int floor = L - DepthAt(dx, dz, rx, rz, d);
                c.height = floor;
                c.water = L;
                c.waterKind = fluid == GenMat.Water ? (byte)0 : fluid;
                c.pkLineLo = math.min(floor - 1, lowNb);
                c.pkLineHi = floor;
            }
            else
            {
                if (lowNb == int.MaxValue) return;   // no footprint neighbour: not the rim
                c.height = math.max(c.height, L + 1);
                if (c.water <= c.height) c.water = ColumnSample.NO_WATER_Y;
                c.pkLineLo = lowNb;
                c.pkLineHi = L + 1;
            }
            c.pkLining = Lining(fluid);
            int cap = L - d - CAP_MARGIN;
            c.riverFloor = c.riverFloor == 0 ? cap : math.min(c.riverFloor, cap);
            c.open1Lo = c.open1Hi = c.open2Lo = c.open2Hi = c.open3Lo = c.open3Hi = 0;
            c.stamp3D = false; c.carvesTop = false; c.lipLo = 0;
        }

        // ------------------------------------------------------------------
        // Deep pockets (DetailField's sealed ellipsoids) under the rules
        // ------------------------------------------------------------------

        /// DetailField.Pocket with the rule fluid and its lining: the fluid
        /// (below the level) / Air (above) inside, the lining in the inner wall
        /// (d^2 in [1, POCKET_LINING)) when the fluid has one, 255 elsewhere;
        /// inShell = inside the no-carve shell. ctx = archetype + 1 of the column's
        /// plane cell (the pocket lies inside that cell: both grids are 512).
        public static byte DeepPocket(in TerrainSpec spec, in Offsets o, byte ctx, int x, int y, int z, out bool inShell)
        {
            inShell = false;
            int cx = x >> DetailField.POCKET_CELL_SHIFT, cy = y >> DetailField.POCKET_CELL_SHIFT, cz = z >> DetailField.POCKET_CELL_SHIFT;
            if (!DetailField.TryGetPocket(in spec, in o, cx, cy, cz, out DetailField.PocketInfo pk)) return 255;
            float3 q = (new float3(x, y, z) + 0.5f - pk.centre) / pk.radii;
            float d2 = q.x * q.x + q.y * q.y + q.z * q.z;
            if (d2 >= 1.35f * 1.35f) return 255;
            byte fluid = DeepFluid(in spec, in o, ctx, cx, cy, cz, pk.centre);
            if (d2 >= 1f)
            {
                inShell = true;
                if (d2 < GroundRules.POCKET_LINING) { byte l = DeepLining(fluid); if (l != 0) return l; }
                return 255;
            }
            return (y + 0.5f > pk.level) ? GenMat.Air : fluid;
        }

        public static byte DeepFluid(in TerrainSpec spec, in Offsets o, byte ctx, int cx, int cy, int cz, float3 centre)
        {
            int arch = ctx - 1;
            if (arch == Regions.Volcanic) return GenMat.Lava;                 // magma chambers under volcanic land
            float r = Noise.Hash01(cx, cy, cz, o.seed + 183u);
            byte biome = ShapeField.Biome(in o, centre.xz);
            if (centre.y < spec.CavernTopY)
            {
                float t = (spec.CavernTopY - centre.y) / math.max(1f, spec.CavernTopY - spec.UnderworldTopY);
                if (t > 0.55f) return GenMat.Lava;                              // the deep half: nearer the underworld
                switch (biome)
                {
                    case GenBiome.Jungle: return GenMat.Sludge;
                    case GenBiome.Desert: case GenBiome.Badlands: return GenMat.Acid;
                    case GenBiome.Snow: return GenMat.Water;
                    default: return r < 0.35f ? GenMat.Acid : (r < 0.6f ? GenMat.Sludge : GenMat.Water);
                }
            }
            switch (biome)
            {
                case GenBiome.Badlands: return GenMat.Oil;
                case GenBiome.Desert: return r < 0.6f ? GenMat.Oil : GenMat.Water;
                case GenBiome.Jungle: return r < 0.4f ? GenMat.Honey : GenMat.Water;
                case GenBiome.Snow: return GenMat.Water;
                default: return r < 0.15f ? GenMat.Oil : GenMat.Water;
            }
        }

        // ------------------------------------------------------------------
        // The site plane (built once per world, main thread)
        // ------------------------------------------------------------------

        public struct Site
        {
            public int cx, cz, level, rx, rz, depth;
            public byte fluid;
            public byte archetype, biome;
            public bool valley;
        }

        public static Site Decode(int[] plane, int i)
        {
            int b = i * STRIDE; uint s = (uint)plane[b + 3];
            return new Site
            {
                cx = plane[b] & 0xFFFFFF, cz = plane[b + 1], level = plane[b + 2],
                fluid = (byte)(s & 255u), rx = (int)((s >> 8) & 255u), rz = (int)((s >> 16) & 255u), depth = (int)((s >> 24) & 127u),
                archetype = (byte)(((plane[b] >> 24) & 15) - 1),
            };
        }

        /// Rule fluid of a candidate (0 = none), its radius range and depth range (voxels).
        public static byte RuleFluid(in RecordCell rc, bool valley, float r, float pick, out int rMin, out int rMax, out int dMin, out int dMax)
        {
            rMin = rMax = dMin = dMax = 0;
            byte f = 0; float chance = 0f;
            byte arch = rc.Archetype, biome = rc.Biome;
            if (arch == Regions.Volcanic) { f = pick < 0.25f ? GenMat.Acid : GenMat.Lava; chance = 0.5f; }
            else if (biome == GenBiome.Badlands) { f = GenMat.Oil; chance = 0.3f; }
            else if (biome == GenBiome.Desert) { f = GenMat.Quicksand; chance = 0.2f; }
            else if (biome == GenBiome.Jungle && (valley || arch == Regions.Wetland)) { f = GenMat.Sludge; chance = 0.35f; }
            else if (biome == GenBiome.Jungle) { f = GenMat.Honey; chance = 0.15f; }
            else if (valley || arch == Regions.Wetland) { f = GenMat.Water; chance = 0.35f; }
            if (f == 0 || r >= chance) return 0;
            switch (f)
            {
                case GenMat.Lava: rMin = 24; rMax = 48; dMin = 10; dMax = 16; break;
                case GenMat.Acid: rMin = 14; rMax = 28; dMin = 6; dMax = 10; break;
                case GenMat.Oil: rMin = 18; rMax = 40; dMin = 4; dMax = 8; break;
                case GenMat.Quicksand: rMin = 20; rMax = 40; dMin = 10; dMax = 18; break;
                case GenMat.Sludge: rMin = 18; rMax = 36; dMin = 6; dMax = 12; break;
                case GenMat.Honey: rMin = 14; rMax = 28; dMin = 6; dMax = 10; break;
                default: rMin = 24; rMax = 52; dMin = 10; dMax = 20; break;   // water
            }
            return f;
        }

        /// Per map cell: inside the start zone (both start rules' easy areas + every
        /// cell within START_REACH of either start column), as task 181's plane.
        public static bool[] StartZone(WorldMap map)
        {
            var spec = map.Spec; var rec = map.Record;
            int mc = spec.mapCells, cv = spec.mapCellVoxels;
            var zone = new bool[mc * mc];
            if (rec == null) return zone;
            for (int mild = 0; mild < 2; mild++)
            {
                if (!PlayStart.Find(map, out var pick, mild == 1)) continue;
                if (pick.area >= 0 && pick.area < rec.EasyAreaList.Length && rec.EasyAreaList[pick.area].cells != null)
                    foreach (int ci in rec.EasyAreaList[pick.area].cells) zone[ci] = true;
                int rr = START_REACH / cv + 2;
                int pcx = pick.column.x / cv, pcz = pick.column.y / cv;
                for (int dz = -rr; dz <= rr; dz++)
                for (int dx = -rr; dx <= rr; dx++)
                {
                    int cx = pcx + dx, cz = pcz + dz;
                    if (cx < 0 || cz < 0 || cx >= mc || cz >= mc) continue;
                    long qx = math.clamp(pick.column.x, cx * cv, cx * cv + cv - 1) - pick.column.x;
                    long qz = math.clamp(pick.column.y, cz * cv, cz * cv + cv - 1) - pick.column.y;
                    if (qx * qx + qz * qz <= (long)START_REACH * START_REACH) zone[cz * mc + cx] = true;
                }
            }
            return zone;
        }

        public struct PlaneStats { public int candidates, ruled, rejectedCells, rejectedSamples, rejectedFlat, sites; }

        /// The plane: STRIDE ints per plane cell, row-major. Without a record
        /// (map v3) every entry is 0 (no pockets, no deep context).
        public static int[] BuildPlane(WorldMap map, out PlaneStats st)
        {
            st = default;
            var spec = map.Spec;
            int n = PlaneCells(in spec);
            var plane = new int[n * n * STRIDE];
            var rec = map.Record;
            if (rec == null) return plane;
            var o = Offsets.For(in spec);
            var view = map.View;
            view.pocket = null;   // the BASE terrain
            int mc = spec.mapCells, cv = spec.mapCellVoxels, world = mc * cv;

            var zone = StartZone(map);

            for (int pz = 0; pz < n; pz++)
            for (int px = 0; px < n; px++)
            {
                int b = (pz * n + px) * STRIDE;
                // deep context: the record at the plane cell's centre
                int ccx = math.min((px << CELL_SHIFT) + CELL / 2, world - 1), ccz = math.min((pz << CELL_SHIFT) + CELL / 2, world - 1);
                var rcc = rec.AtCell(ccx / cv, ccz / cv);
                plane[b] = (rcc.Archetype < Regions.KINDS ? rcc.Archetype + 1 : CTX_SEA) << 24;

                // the surface candidate
                int sx = (px << CELL_SHIFT) + SITE_MARGIN + (int)(Noise.Hash01(px, 1831, pz, spec.seed) * (CELL - 2 * SITE_MARGIN));
                int sz = (pz << CELL_SHIFT) + SITE_MARGIN + (int)(Noise.Hash01(px, 1832, pz, spec.seed) * (CELL - 2 * SITE_MARGIN));
                if (sx >= world - SITE_MARGIN || sz >= world - SITE_MARGIN) continue;
                st.candidates++;
                var rc = rec.AtCell(sx / cv, sz / cv);
                if (rc.IsSea) continue;
                var c0 = ShapeField.Sample(in spec, in view, in o, sx + 0.5f, sz + 0.5f, 1, false);
                bool valley = c0.regional - c0.height >= VALLEY;
                byte fluid = RuleFluid(in rc, valley, Noise.Hash01(px, 1833, pz, spec.seed), Noise.Hash01(px, 1834, pz, spec.seed),
                                       out int rMin, out int rMax, out int dMin, out int dMax);
                if (fluid == 0) continue;
                st.ruled++;
                int rx = rMin + (int)(Noise.Hash01(px, 1835, pz, spec.seed) * (rMax - rMin + 1));
                int rz = math.max(rMin * 3 / 4, (int)(rx * (0.6f + 0.4f * Noise.Hash01(px, 1836, pz, spec.seed))));
                if (Noise.Hash01(px, 1837, pz, spec.seed) < 0.5f) { int t = rx; rx = rz; rz = t; }
                int dep = dMin + (int)(Noise.Hash01(px, 1838, pz, spec.seed) * (dMax - dMin + 1));

                // every record cell under the basin + rim (+ 1 cell)
                bool ok = true;
                int x0 = (sx - rx - 2) / cv - 1, x1 = (sx + rx + 2) / cv + 1, z0 = (sz - rz - 2) / cv - 1, z1 = (sz + rz + 2) / cv + 1;
                for (int cz = z0; cz <= z1 && ok; cz++)
                for (int cx = x0; cx <= x1 && ok; cx++)
                {
                    if (cx < 0 || cz < 0 || cx >= mc || cz >= mc) { ok = false; break; }
                    var q = rec.AtCell(cx, cz);
                    if (q.IsSea || q.freshWater < FRESH_CLEAR || q.IsRoute || q.IsFeature || q.MoveBand != 0 || zone[cz * mc + cx]) ok = false;
                }
                if (!ok) { st.rejectedCells++; continue; }

                // base terrain samples: centre, 16 on the rim ellipse, 8 halfway
                int lo = c0.height, hi = c0.height;
                for (int k = 0; k < 24 && ok; k++)
                {
                    float a = (k < 16 ? k / 16f : (k - 16) / 8f + 1f / 16f) * 2f * math.PI;
                    float sc = k < 16 ? 1f : 0.5f;
                    float qx = sx + 0.5f + math.cos(a) * (rx + 2) * sc, qz = sz + 0.5f + math.sin(a) * (rz + 2) * sc;
                    var c = ShapeField.Sample(in spec, in view, in o, qx, qz, 1, false);
                    if (c.water != ColumnSample.NO_WATER_Y || c.lipLo != 0 || c.stampMat != 0 || c.stamp3D || c.carvesTop ||
                        c.open1Hi > c.open1Lo || c.open2Hi > c.open2Lo || c.open3Hi > c.open3Lo || c.riverFloor != 0)
                        ok = false;
                    lo = math.min(lo, c.height); hi = math.max(hi, c.height);
                }
                if (!ok) { st.rejectedSamples++; continue; }
                int level = lo - 1;
                if (hi - lo > FLAT_SPAN || level - dep <= spec.seaLevelY + SEA_CLEAR) { st.rejectedFlat++; continue; }

                plane[b] |= sx & 0xFFFFFF;
                plane[b + 1] = sz;
                plane[b + 2] = level;
                plane[b + 3] = (int)((uint)fluid | ((uint)rx << 8) | ((uint)rz << 16) | ((uint)dep << 24));
                st.sites++;
            }
            return plane;
        }
    }
}
