// Assets/TerrainGen/FluidPockets.cs
//
// TASK 183 -- FLUID POCKETS AT REST (placement plan step 4, owner-approved
// 2026-11-06 incl. batch 2; TerrainSpec.fluidPocketsOn, set by the engine's chunk
// source unless `-fluidpockets 0` (DEFAULT ON there since task 201; a bare
// TerrainSpec stays OFF); map v4 only). OFF keeps every byte as before. The fluid
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
// TASK 196 -- NATURAL BASINS (owner Q-X (a), 2026-11-13). 183's carve left a SHEER
// WALL on the high side: L sits 1 under the LOWEST rim sample, and the ground up
// to FLAT_SPAN (2.4 m) higher was cut straight down at the footprint's edge
// (191's surface_lava: a cylinder in a snowy slope). Now the shape follows the
// slope rule the ground already has -- Cover.STEEP_DEG (30 deg, = Danger's
// scramble limit: the steepest ground that keeps its soil and that a walker can
// still climb out of), as the integer slope BANK_Q8 / 256:
//   rim  (the 8-neighbours of the footprint, as before): height := L + 1 -- the
//        fluid's shore is flush, one voxel over the fluid; LINED as before.
//   bank (columns at ring t = smallest t with InFoot(dx, dz, rx + t, rz + t)):
//        the ground is moved toward L + 1 by at most UP - drop(t) (cut) or
//        DN - drop(t) (fill, the downhill levee), drop(t) = (t - 1) * BANK_Q8 >> 8.
//        UP / DN = the site's exact rim excess over / under L + 1 (every rim
//        column of the BASE terrain, measured once in BuildPlane), so the rim
//        lands exactly on L + 1 and the change tapers to 0 at ring BankBand(UP, DN):
//        no step anywhere, the bank is the base ground's slope + at most 30 deg.
//        A cliff further out keeps its shape (lowered by the taper only).
//   bowl depth := min(D, 1 + rmin * BANK_Q8 / 512): the paraboloid's steepest
//        under-fluid wall (2 (D - 1) / rmin, at the edge) stays under the same slope.
// The band must stay inside the plane cell and the record cells BuildPlane checked
// (dry, no route / feature / town / start zone); where the measured excess needs a
// wider band the excess is capped (PlaneStats.bankCapped) and that rim keeps a
// small step -- still >= L + 1, so still sealed. Placement is unchanged (same
// candidates, same rules, same L); only the shape and the stored depth differ.
// Plane words (task 196): [1] bits 24-31 UP, [2] bits 24-31 DN (0 / 0 = 183's shape).
//
// TASK 203 -- NATURAL OUTLINES (owner 2026-11-13, Q-AA "more like ponds", W1 (a): every
// fluid). The footprint is no longer one ellipse but a smooth blob: the sum of 3-4
// overlapping LOBES (scaled copies of the site's ellipse) above a threshold,
//   G(dx, dz; t) = sum_i max(0, 1 - rho_i(t)),  footprint_t = { G > TAU },
// rho_i(t) = the lobe's normalized distance^2 with its radii grown by t (Q16 integer).
// (TAU 0.05; ponds ~55 % of the ellipse's area.) Lobe 0 sits on the centre; satellite 1
// points to the LOWEST of the 16 rim samples the
// planner already takes (the ground's own downhill: the pond reaches toward the low
// ground, its shore draws back from the high side, so less is cut); satellites 2-3 at
// hashed angles from it. Each lobe lies inside the site's ellipse (normalized offset +
// radius <= 1) and inside its box (|o| + a <= rx), so the pond and every bank ring t stay
// where the planner looked and where BuildPlane's room allows -- the containment proof of
// 183 / 196 carries over. Sizes / angles come from an integer hash of the site's own
// centre (the seed placed it) -- no terrain constant, same rule for every fluid / biome.
//   footprint  depth := 1 + min(D - 1, k * BANK_Q8 >> 8), k = how far the lobes shrink
//              (radii - k) before the column leaves: the bed shelves at the bank slope
//              (30 deg) from the shore down to D (FluidPocketTests.Outline_Rules: 1 voxel per
//              column between neighbours, 2 on 0.015 % of pairs where two lobes meet).
//   lining     every footprint / rim column is lined over [L - D - 1, floor or L + 1]:
//              every fluid voxel lies in (L - D, L], so this covers every solid face that
//              can touch fluid without a neighbour's depth (a mud lens under the shallows).
//   rim / bank as 196 (8-neighbours of the footprint flush at L + 1; ring t = smallest t
//              with the column in footprint_t; UP / DN measured on the new rim).
// G only grows with t, so the rings nest (Ring's binary search holds). Plane bits: word
// [0] bits 28-31 = the low direction (0-15), word [3] bit 31 = outline on. `-pocketoutline 0`
// leaves both 0 = 196's ellipse byte for byte.
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
        /// Task 196: the bank / bowl slope, tan(Cover.STEEP_DEG = 30 deg) * 256 rounded DOWN
        /// (0.574: 29.9 deg, never over the rule). FluidPocketTests pins it to Cover.STEEP_DEG.
        public const int BANK_Q8 = 147;
        public const int LOW24 = 0xFFFFFF;

        // packed site word [3]: bits 0-7 fluid GenMat, 8-15 rx, 16-23 rz, 24-30 depth D
        // word [0]: bits 0-23 centre x, bits 24-27 deep context (archetype + 1, CTX_SEA under the sea; 0 = none)
        // word [1]: bits 0-23 centre z, bits 24-31 bank UP (task 196); word [2]: bits 0-23 level L, 24-31 bank DN
        // task 203: word [0] bits 28-31 the lowest rim direction (k * 22.5 deg); word [3] bit 31 = natural outline
        public const int TAU_Q16 = 3277;                 // task 203: footprint threshold of the lobe field (0.05)
        public const int MAX_LOBES = 4;

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

        /// Task 196: how far the bank may move ground at ring t (>= 1) for a site excess e.
        public static int BankDrop(int t) => ((t - 1) * BANK_Q8) >> 8;

        /// Task 196: the first ring where the bank no longer moves anything (1 = none).
        public static int BankBand(int up, int dn)
        {
            int e = math.max(up, dn);
            return e <= 0 ? 1 : 1 + (e * 256 + BANK_Q8 - 1) / BANK_Q8;
        }

        /// Task 196: the bank's height for base ground `h` at ring t: moved toward L + 1 by
        /// at most (up or dn) - BankDrop(t), never past L + 1.
        public static int BankHeight(int h, int level, int up, int dn, int t)
        {
            int top = level + 1, drop = BankDrop(t);
            if (h > top) { int a = up - drop; return a <= 0 ? h : h - math.min(h - top, a); }
            if (h < top) { int a = dn - drop; return a <= 0 ? h : h + math.min(top - h, a); }
            return h;
        }

        /// Task 196: the bowl's depth -- its steepest under-fluid wall (the paraboloid's
        /// edge, 2 (D - 1) / rmin) at most the bank slope.
        public static int BowlDepth(int d, int rx, int rz) => math.min(d, 1 + ((math.min(rx, rz) * BANK_Q8) >> 9));

        /// Task 196: the ring of a column outside the footprint (smallest t in [1, band) with
        /// the column inside the ellipse (rx + t, rz + t)); `band` when there is none.
        public static int Ring(int dx, int dz, int rx, int rz, int band)
        {
            if (band <= 1 || !InFoot(dx, dz, rx + band - 1, rz + band - 1)) return band;
            int lo = 1, hi = band - 1;   // InFoot is monotone in t: the ellipses nest
            while (lo < hi) { int m = (lo + hi) >> 1; if (InFoot(dx, dz, rx + m, rz + m)) hi = m; else lo = m + 1; }
            return lo;
        }

        // ------------------------------------------------------------------
        // Task 203: the natural outline (lobes)
        // ------------------------------------------------------------------

        /// Up to MAX_LOBES axis-aligned lobes: centre offsets (ox, oz) and radii (a, b), voxels.
        public struct Lobes { public int n; public int4 ox, oz, a, b; }

        static uint Mix(uint h) { h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16; return h; }

        /// cos(k * 22.5 deg) * 256, k mod 16 (the planner's 16 rim-sample directions).
        public static int CosQ8(int k)
        {
            k &= 15;
            int q = k <= 4 ? k : k <= 8 ? 8 - k : k <= 12 ? k - 8 : 16 - k;
            int v = q == 0 ? 256 : q == 1 ? 237 : q == 2 ? 181 : q == 3 ? 98 : 0;
            return k > 4 && k < 12 ? -v : v;
        }
        public static int SinQ8(int k) => CosQ8(k - 4);

        /// The lobes of a site (integer only, the same on every path). lowDir = the
        /// direction of the lowest rim sample; the hash takes the site's own centre.
        public static Lobes Shape(int cx, int cz, int rx, int rz, int lowDir)
        {
            uint h = Mix((uint)cx * 0x9E3779B1u ^ Mix((uint)cz * 0x85EBCA77u ^ ((uint)rx << 8) ^ (uint)rz ^ 0xC0DE203u));
            uint h2 = Mix(h ^ 0x68E31DA4u);
            var l = new Lobes { n = 3 + (int)((h >> 5) & 1) };
            int m = 160 + (int)(h & 31) * 5 / 4;                           // main lobe 0.62 .. 0.77 of the ellipse
            l.a[0] = math.max(2, (rx * m) >> 8); l.b[0] = math.max(2, (rz * m) >> 8);
            for (int j = 1; j < l.n; j++)
            {
                // satellite 1 toward the low ground, 2 and 3 on its flanks (67-135 deg either side):
                // the side opposite the low direction keeps only the main lobe -- the shore draws back there
                int dir = j == 1 ? lowDir : j == 2 ? lowDir + 3 + (int)((h >> 6) & 3) : lowDir - 3 - (int)((h >> 8) & 3);
                uint hj = h2 >> ((j - 1) * 10);
                int u = j == 1 ? 102 + (int)(hj & 31) : 77 + (int)(hj & 63);  // offset: 0.40 .. 0.52 (low lobe), 0.30 .. 0.55
                int sc = 256 - u - 8 - (j == 1 ? (int)((hj >> 6) & 15) : 3 * (int)((hj >> 6) & 15));   // offset + radius <= 1 - 8/256
                int ox = rx * u * CosQ8(dir) / 65536, oz = rz * u * SinQ8(dir) / 65536;
                l.ox[j] = ox; l.oz[j] = oz;
                l.a[j] = math.max(2, math.min((rx * sc) >> 8, rx - math.abs(ox)));
                l.b[j] = math.max(2, math.min((rz * sc) >> 8, rz - math.abs(oz)));
            }
            return l;
        }

        public static Lobes LobesOf(in Site s) => Shape(s.cx, s.cz, s.rx, s.rz, s.lowDir);

        /// G (Q16) at (dx, dz) with every lobe's radii grown by t (t < 0 shrinks; a lobe
        /// with a radius <= 0 is gone). Non-decreasing in t.
        public static int Field(in Lobes l, int dx, int dz, int t)
        {
            int g = 0;
            for (int i = 0; i < l.n; i++)
            {
                long A = l.a[i] + t, B = l.b[i] + t;
                if (A <= 0 || B <= 0) continue;
                long x = dx - l.ox[i], z = dz - l.oz[i];
                long A2 = A * A, B2 = B * B, den = A2 * B2, num = x * x * B2 + z * z * A2;
                if (num >= den) continue;
                g += (int)(65536L - (num << 16) / den);
            }
            return g;
        }

        public static bool InLobes(in Lobes l, int dx, int dz, int t) => Field(in l, dx, dz, t) > TAU_Q16;

        /// Fluid depth of a footprint column: shelving at BANK_Q8 per shrink step from 1 at the shore to d.
        public static int LobeDepth(in Lobes l, int dx, int dz, int d)
        {
            int lo = 0, hi = ((d - 1) * 256 + BANK_Q8 - 1) / BANK_Q8;
            while (lo < hi) { int m = (lo + hi + 1) >> 1; if (InLobes(in l, dx, dz, -m)) lo = m; else hi = m - 1; }
            return 1 + math.min(d - 1, (lo * BANK_Q8) >> 8);
        }

        /// Ring of a column outside the footprint (smallest t in [1, band) inside footprint_t); `band` if none.
        public static int LobeRing(in Lobes l, int dx, int dz, int band)
        {
            if (band <= 1 || !InLobes(in l, dx, dz, band - 1)) return band;
            int lo = 1, hi = band - 1;
            while (lo < hi) { int m = (lo + hi) >> 1; if (InLobes(in l, dx, dz, m)) hi = m; else lo = m + 1; }
            return lo;
        }

        /// The footprint test of a decoded site (task 203's lobes, or the ellipse when the outline is off).
        public static bool InSite(in Site s, int dx, int dz)
        {
            if (!s.outline) return InFoot(dx, dz, s.rx, s.rz);
            var l = LobesOf(in s);
            return InLobes(in l, dx, dz, 0);
        }

        /// The fluid depth a footprint column of a decoded site holds.
        public static int SiteDepth(in Site s, int dx, int dz)
        {
            if (!s.outline) return DepthAt(dx, dz, s.rx, s.rz, s.depth);
            var l = LobesOf(in s);
            return LobeDepth(in l, dx, dz, s.depth);
        }

        /// Task 203's column step (Apply with the outline bit set).
        static void ApplyOutline(int dx, int dz, int cx, int cz, int rx, int rz, int L, int d, int up, int dn, int band,
                                 int lowDir, byte fluid, ref ColumnSample c)
        {
            var l = Shape(cx, cz, rx, rz, lowDir);
            int lineLo = L - d - 1;
            if (InLobes(in l, dx, dz, 0))
            {
                int floor = L - LobeDepth(in l, dx, dz, d);
                c.height = floor;
                c.water = L;
                c.waterKind = fluid == GenMat.Water ? (byte)0 : fluid;
                c.pkLineLo = lineLo;
                c.pkLineHi = floor;
            }
            else
            {
                bool rim = false;
                if (dx >= -rx - 1 && dx <= rx + 1 && dz >= -rz - 1 && dz <= rz + 1)
                    for (int oz = -1; oz <= 1 && !rim; oz++)
                    for (int ox = -1; ox <= 1; ox++)
                        if ((ox != 0 || oz != 0) && InLobes(in l, dx + ox, dz + oz, 0)) { rim = true; break; }
                if (rim)
                {
                    c.height = math.max(BankHeight(c.height, L, up, dn, 1), L + 1);
                    if (c.water <= c.height) c.water = ColumnSample.NO_WATER_Y;
                    c.pkLineLo = lineLo;
                    c.pkLineHi = L + 1;
                }
                else
                {
                    int t = LobeRing(in l, dx, dz, band);
                    if (t >= band) return;
                    int h = BankHeight(c.height, L, up, dn, t);
                    if (h == c.height) return;
                    c.height = h;
                    if (c.water <= c.height) c.water = ColumnSample.NO_WATER_Y;
                    c.open1Lo = c.open1Hi = c.open2Lo = c.open2Hi = c.open3Lo = c.open3Hi = 0;
                    c.stamp3D = false; c.carvesTop = false; c.lipLo = 0;
                    return;
                }
            }
            c.pkLining = Lining(fluid);
            int cap = L - d - CAP_MARGIN;
            c.riverFloor = c.riverFloor == 0 ? cap : math.min(c.riverFloor, cap);
            c.open1Lo = c.open1Hi = c.open2Lo = c.open2Hi = c.open3Lo = c.open3Hi = 0;
            c.stamp3D = false; c.carvesTop = false; c.lipLo = 0;
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
            int cx = e[0] & LOW24, cz = e[1] & LOW24, L = e[2] & LOW24;
            int up = (int)((uint)e[1] >> 24), dn = (int)((uint)e[2] >> 24);
            int band = BankBand(up, dn);
            byte fluid = (byte)(s & 255u);
            int rx = (int)((s >> 8) & 255u), rz = (int)((s >> 16) & 255u), d = (int)((s >> 24) & 127u);
            int dx = ix - cx, dz = iz - cz;
            int ext = math.max(1, band);
            if (dx < -rx - ext || dx > rx + ext || dz < -rz - ext || dz > rz + ext) return;
            if ((s >> 31) != 0u) { ApplyOutline(dx, dz, cx, cz, rx, rz, L, d, up, dn, band, (int)((uint)e[0] >> 28), fluid, ref c); return; }   // task 203
            // The lining: every solid voxel with a face on a fluid voxel, and one more under
            // the bed. Fluid fills (floor, L] of each footprint column, so a column's solid
            // voxels down to its lowest footprint neighbour's floor + 1 can touch fluid.
            int lowNb = int.MaxValue;
            if (dx >= -rx - 1 && dx <= rx + 1 && dz >= -rz - 1 && dz <= rz + 1)
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
            else if (lowNb != int.MaxValue)
            {
                // the rim: flush at L + 1 (task 196; a capped excess leaves it higher, never lower)
                c.height = math.max(BankHeight(c.height, L, up, dn, 1), L + 1);
                if (c.water <= c.height) c.water = ColumnSample.NO_WATER_Y;
                c.pkLineLo = lowNb;
                c.pkLineHi = L + 1;
            }
            else
            {
                // task 196: the bank -- no fluid touches it, so no lining and no cave cap
                int t = Ring(dx, dz, rx, rz, band);
                if (t >= band) return;
                int h = BankHeight(c.height, L, up, dn, t);
                if (h == c.height) return;
                c.height = h;
                if (c.water <= c.height) c.water = ColumnSample.NO_WATER_Y;
                c.open1Lo = c.open1Hi = c.open2Lo = c.open2Hi = c.open3Lo = c.open3Hi = 0;
                c.stamp3D = false; c.carvesTop = false; c.lipLo = 0;
                return;
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
            public int up, dn;   // task 196: the bank's cut / fill at the rim (0 / 0 = 183's shape)
            public int lowDir;   // task 203: direction (k * 22.5 deg) of the lowest rim sample
            public bool outline; // task 203: the natural outline (lobes); false = the ellipse
            public byte fluid;
            public byte archetype, biome;
            public bool valley;
        }

        public static Site Decode(int[] plane, int i)
        {
            int b = i * STRIDE; uint s = (uint)plane[b + 3];
            return new Site
            {
                cx = plane[b] & LOW24, cz = plane[b + 1] & LOW24, level = plane[b + 2] & LOW24,
                up = (int)((uint)plane[b + 1] >> 24), dn = (int)((uint)plane[b + 2] >> 24),
                fluid = (byte)(s & 255u), rx = (int)((s >> 8) & 255u), rz = (int)((s >> 16) & 255u), depth = (int)((s >> 24) & 127u),
                archetype = (byte)(((plane[b] >> 24) & 15) - 1),
                lowDir = (int)((uint)plane[b] >> 28), outline = (s >> 31) != 0u,
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
            if (spec.Towns) { map.EnsureSites(); Towns.MarkCells(zone, mc, cv, rec.SiteList); }   // task 190: no pocket in a town
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

        public struct PlaneStats
        {
            public int candidates, ruled, rejectedCells, rejectedSamples, rejectedFlat, sites;
            public int banked, bankCapped, maxUp, maxDn, depthCut;   // task 196: sites with a bank, capped, the largest UP / DN, bowls made shallower
            public int outlined;   // task 203: sites with the natural outline (0 under -pocketoutline 0)
        }

        /// Task 196: the bank of a site -- the exact excess of the BASE terrain's rim
        /// columns (every 8-neighbour of the footprint) over / under L + 1, each capped so
        /// the band ends within `room` columns of the footprint (the plane cell and the
        /// checked record cells). capped = a cap bit.
        public static void MeasureBank(in TerrainSpec spec, in MapView baseView, in Offsets o, int cx, int cz, int level,
                                       int rx, int rz, int room, out int up, out int dn, out bool capped)
        {
            var s = new Site { cx = cx, cz = cz, level = level, rx = rx, rz = rz };
            MeasureBank(in spec, in baseView, in o, in s, room, out up, out dn, out capped);
        }

        /// Task 203: the same on a site's own footprint (InSite: the lobes when s.outline).
        public static void MeasureBank(in TerrainSpec spec, in MapView baseView, in Offsets o, in Site s,
                                       int room, out int up, out int dn, out bool capped)
        {
            int cx = s.cx, cz = s.cz, rx = s.rx, rz = s.rz;
            int top = s.level + 1, hi = int.MinValue, lo = int.MaxValue;
            // the footprint over the box once (dx in [-rx - 2, rx + 2]), then the 8-neighbour rim by lookup
            int W = 2 * rx + 5, H = 2 * rz + 5;
            var foot = new bool[W * H];
            var lb = LobesOf(in s);
            for (int j = 0; j < H; j++)
            for (int i = 0; i < W; i++)
                foot[j * W + i] = s.outline ? InLobes(in lb, i - rx - 2, j - rz - 2, 0) : InFoot(i - rx - 2, j - rz - 2, rx, rz);
            for (int dz = -rz - 1; dz <= rz + 1; dz++)
            for (int dx = -rx - 1; dx <= rx + 1; dx++)
            {
                int k0 = (dz + rz + 2) * W + dx + rx + 2;
                if (foot[k0]) continue;
                bool rim = false;
                for (int oz = -1; oz <= 1 && !rim; oz++)
                for (int ox = -1; ox <= 1; ox++)
                    if ((ox != 0 || oz != 0) && foot[k0 + oz * W + ox]) { rim = true; break; }
                if (!rim) continue;
                int h = ShapeField.Sample(in spec, in baseView, in o, cx + dx + 0.5f, cz + dz + 0.5f, 1, false).height;
                hi = math.max(hi, h); lo = math.min(lo, h);
            }
            up = math.max(0, hi - top); dn = math.max(0, top - lo);
            int emax = math.clamp(((room - 1) * BANK_Q8) >> 8, 0, 255);
            capped = up > emax || dn > emax;
            up = math.min(up, emax); dn = math.min(dn, emax);
        }

        /// Task 203: the direction (0-15, k * 22.5 deg) of the lowest of the 16 rim samples the
        /// planner takes on the ellipse (rx + 2, rz + 2) (ties: the first). BuildPlane gets the
        /// same value from its own sample loop; tests use this for forced sites.
        public static int RimLowDir(in TerrainSpec spec, in MapView baseView, in Offsets o, int sx, int sz, int rx, int rz)
        {
            int best = 0, bestH = int.MaxValue;
            for (int k = 0; k < 16; k++)
            {
                float a = k / 16f * 2f * math.PI;
                int h = ShapeField.Sample(in spec, in baseView, in o, sx + 0.5f + math.cos(a) * (rx + 2), sz + 0.5f + math.sin(a) * (rz + 2), 1, false).height;
                if (h < bestH) { bestH = h; best = k; }
            }
            return best;
        }

        /// TESTS ONLY (task 203): >= 0 overrides `-pocketoutline` for BuildPlane (0 = the ellipse). -1 = the argument.
        public static int OutlineTestOverride = -1;

        /// `-pocketoutline 0` (task 203, for before / after pictures): 196's ellipse exactly.
        /// Absent or 1 = the natural outline. Last match wins.
        public static bool OutlineArg()
        {
            string[] a = System.Environment.GetCommandLineArgs();
            bool on = true;
            for (int i = 0; i + 1 < a.Length; i++)
                if (a[i] == "-pocketoutline" && int.TryParse(a[i + 1], out int v)) on = v != 0;
            return on;
        }

        /// `-pocketbasin 0` (task 196, for before / after pictures): the plane gets 183's
        /// shape back exactly (no bank, the drawn depth). Absent or 1 = the natural basin. Last match wins.
        public static bool BasinsArg()
        {
            string[] a = System.Environment.GetCommandLineArgs();
            bool on = true;
            for (int i = 0; i + 1 < a.Length; i++)
                if (a[i] == "-pocketbasin" && int.TryParse(a[i + 1], out int v)) on = v != 0;
            return on;
        }

        /// The four plane words of a site (ctx: word [0]'s deep-context bits, already shifted).
        public static void Pack(in Site s, int ctxBits, int[] w)
        {
            w[0] = (ctxBits & 0x0F000000) | ((s.lowDir & 15) << 28) | (s.cx & LOW24);   // task 203: bits 28-31 the low direction
            w[1] = (s.cz & LOW24) | (s.up << 24);
            w[2] = (s.level & LOW24) | (s.dn << 24);
            // task 203: NO conditional inside this expression. IL2CPP (the player) types the value it
            // carries across such a branch by its FIRST operand, the byte `fluid`, and truncated
            // rx / rz / depth to 0 in the player only (Mono and Burst were right; found by the player
            // plane fingerprint). Statements, not an expression branch.
            uint w3 = (uint)s.fluid | ((uint)s.rx << 8) | ((uint)s.rz << 16) | ((uint)s.depth << 24);
            if (s.outline) w3 |= 1u << 31;
            w[3] = (int)w3;
        }

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
            bool basins = BasinsArg();   // task 196
            bool outline = OutlineTestOverride >= 0 ? OutlineTestOverride != 0 : OutlineArg();   // task 203

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
                int lo = c0.height, hi = c0.height, lowDir = 0, lowH = int.MaxValue;
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
                    if (k < 16 && c.height < lowH) { lowH = c.height; lowDir = k; }   // task 203: the lowest rim direction
                }
                if (!ok) { st.rejectedSamples++; continue; }
                int level = lo - 1;
                if (hi - lo > FLAT_SPAN || level - dep <= spec.seaLevelY + SEA_CLEAR) { st.rejectedFlat++; continue; }

                // task 196: the natural basin (placement above is unchanged)
                int bowl = basins ? BowlDepth(dep, rx, rz) : dep;
                if (bowl < dep) st.depthCut++;
                int cellX = px << CELL_SHIFT, cellZ = pz << CELL_SHIFT;
                int roomX = math.min(math.min(sx - math.max(cellX, x0 * cv), math.min(cellX + CELL, (x1 + 1) * cv) - 1 - sx), world - 1 - sx) - rx;
                int roomZ = math.min(math.min(sz - math.max(cellZ, z0 * cv), math.min(cellZ + CELL, (z1 + 1) * cv) - 1 - sz), world - 1 - sz) - rz;
                int up = 0, dn = 0; bool capped = false;
                var probe = new Site { cx = sx, cz = sz, level = level, rx = rx, rz = rz, lowDir = outline ? lowDir : 0, outline = outline };
                if (basins) MeasureBank(in spec, in view, in o, in probe, math.min(roomX, roomZ), out up, out dn, out capped);
                if (up > 0 || dn > 0) st.banked++;
                if (capped) st.bankCapped++;
                st.maxUp = math.max(st.maxUp, up); st.maxDn = math.max(st.maxDn, dn);

                var site = new Site { cx = sx, cz = sz, level = level, rx = rx, rz = rz, depth = bowl, up = up, dn = dn, fluid = fluid,
                                      lowDir = probe.lowDir, outline = outline };
                var w = new int[4];
                Pack(in site, plane[b], w);
                for (int k = 0; k < 4; k++) plane[b + k] = w[k];
                st.sites++;
                if (outline) st.outlined++;
            }
            return plane;
        }
    }
}
