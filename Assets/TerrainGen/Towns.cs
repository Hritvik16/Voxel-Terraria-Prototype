// Assets/TerrainGen/Towns.cs
//
// TASK 190 -- TOWNS ON THE SITES (owner 2026-11-12, answer (a): "place the house
// kit on the 187 sites", behind `-towns 1` = TerrainSpec.townsOn, DEFAULT OFF;
// map v4 only; -towns 1 also turns the site planner on). OFF keeps every byte.
//
// Two things, both a pure function of the map + the site plan (Sites.cs), never
// a place or a seed constant:
//
//  1. THE FLATTEN (the rule task 187 stored, now applied). Inside a site's disc
//     (radius R) the ground is set to the pad level (Site.padLevelY: the median
//     integer map height of the disc + sea level). Over a ring of width B past R
//     it is blended back to the natural ground with an integer smoothstep on the
//     squared distance. B = R / 2, shortened so the ring never reaches water (a
//     lake / river / sea cell), a signature feature or another site's ring
//     (BlendWidth). Ring columns that hold still water, a river lip, a stamp
//     (body, opening, 3D carve) or a ledge opening are never moved. Inside the disc + ring caves carve only below
//     CAVE_CAP under the new ground (the river's own mechanism, riverFloor), and
//     inside the disc no stamp or ledge opening, stamp body or river lip stays.
//     ShapeField.Sample calls Apply on EVERY path (LOD 0, shells, shell fill
//     halo, the point query, the far band), so they all agree. Columns outside
//     every disc + ring are untouched: byte for byte today (TownTests).
//
//  2. THE BUILDINGS (BuildingKit's huts / houses / halls). Per site kind a
//     PROGRAM (Program): town = 1 hall + 8 houses + 8 huts, frontier town =
//     1 hall + 4 houses + 4 huts, village = 3 houses + 3 huts, outpost = 2 huts,
//     placed in that order; what does not fit is left out. LAYOUT (Layout): the
//     hall at the centre if it fits there, its door toward the site's route join;
//     every other building on rings round the centre (1 m steps from PLAZA, the
//     open middle; DIRS directions per ring, starting at a hash of the site centre),
//     its door facing the centre (the nearest of the 4 kit rotations). A slot is
//     taken when the rotated box (eaves included) lies EDGE inside the disc,
//     GAP (4 m) clear of every placed box (a lane / door step), every map cell under it
//     is dry land (not sea, lake, river, >= 1 fresh-water step), not a feature,
//     not blocked (stamp disc, start zone) and not a route corridor, and the
//     natural ground under it passes 187's step rule (no 2 m step steeper than
//     Scramble, Sites.StepTooSteep). Floor = pad + 1 (one voxel over the
//     flattened ground), FOUND voxels of foundation into the pad.
//
// THE PLANE (int[], MapView.town; length 1 = none): [0] sites, [1] buildings,
// [2] site offset, [3] building offset; per site SITE_INTS (cx, cz, R, B, pad,
// first building, count, top = highest building voxel or pad); per building
// BLD_INTS (shape | rot << 8 | biome << 16, origin x, y, z, foundation depth,
// box max x, z, y (exclusive)). Buildings never overlap, so a voxel belongs to
// at most one box and every consumer gets the same answer:
//   LOD 0 / point    the exact part (BuildingKit.Role) at the voxel (VoxelAt);
//   shells           CoarseRole: the part at the sample point if it is solid,
//                    else the highest solid part on the stride-2 sub-grid of the
//                    coarse cell (walls, roofs and posts are >= 2 voxels thick,
//                    so a stride-2 grid always hits them: a far house keeps its
//                    walls and roof instead of the sample grid's holes). Shell 1
//                    (stride 2) is exactly the sample point.
//   far band         heights only (the flatten); no buildings, like trees.
// The CPU stays the sole terrain writer: buildings are GENERATED voxels.
//
// Determinism: integer geometry, an integer direction table, integer scores.
// The ground step check is managed float code like 187's ground check.
using System.Collections.Generic;
using Unity.Mathematics;

namespace TerrainGen
{
    public static unsafe class Towns
    {
        public const int HDR = 4, SITE_INTS = 8, BLD_INTS = 8;
        /// Caves carve only this far (6 m) under the flattened ground in a disc + ring.
        public const int CAVE_CAP = 60;
        /// Foundation voxels under the floor (0.6 m into the pad).
        public const int FOUND = 6;
        /// A building box stays 2 m inside its disc.
        public const int EDGE = 20;
        /// 4 m between building boxes (eaves included): a lane, a door step.
        public const int GAP = 40;
        /// The open middle (8 m) where the rings start.
        public const int PLAZA = 80;
        public const int DIRS = 32;

        // round(1024 cos(k 2 pi / 32)), k = 0..8 (the rest by symmetry).
        private static readonly int[] C8 = { 1024, 1004, 946, 851, 724, 569, 392, 200, 0 };
        public static int Cos(int k)
        {
            k &= 31;
            if (k <= 8) return C8[k];
            if (k <= 16) return -C8[16 - k];
            if (k <= 24) return -C8[k - 16];
            return C8[32 - k];
        }
        public static int Sin(int k) => Cos(k - 8);

        public struct Stats
        {
            public int sites, buildings, halls, houses, huts, wanted, blendShort, slotGround;
            public string Line() =>
                $"{sites} sites, {buildings} buildings (hall {halls}, house {houses}, hut {huts}) of {wanted} in the programs; " +
                $"ring shortened at {blendShort} sites; slots refused by the ground {slotGround}";
        }

        /// The building program of a site kind, in placement order.
        public static byte[] Program(byte kind)
        {
            int halls = kind == Site.TOWN || kind == Site.FRONTIER ? 1 : 0;
            int pairs = kind == Site.TOWN ? 8 : kind == Site.FRONTIER ? 4 : kind == Site.VILLAGE ? 3 : 0;
            var p = new List<byte>();
            for (int i = 0; i < halls; i++) p.Add(BuildingKit.HALL);
            for (int i = 0; i < pairs; i++) { p.Add(BuildingKit.HOUSE); p.Add(BuildingKit.HUT); }
            if (kind == Site.OUTPOST) { p.Add(BuildingKit.HUT); p.Add(BuildingKit.HUT); }
            return p.ToArray();
        }

        /// The kit rotation whose front (door) points closest to (dx, dz).
        public static byte FaceRot(int dx, int dz)
        {
            if (math.abs(dx) >= math.abs(dz)) return dx > 0 ? (byte)1 : (byte)3;
            return dz > 0 ? (byte)2 : (byte)0;
        }

        // ------------------------------------------------------------------
        // Build (managed, once per world: WorldMap.EnsureTowns)
        // ------------------------------------------------------------------

        /// The plane for a site plan. `inp` is Sites.FromMap's input (its ground
        /// = the natural terrain: build before the plane is pinned).
        public static int[] Build(SiteInput inp, Site[] sites, out Stats st, out Building[] all, out BuildingFootprint[] prints)
        {
            st = default;
            sites = sites ?? new Site[0];
            var per = new List<Building>[sites.Length];
            var blend = new int[sites.Length];
            for (int k = 0; k < sites.Length; k++)
            {
                blend[k] = BlendWidth(inp, sites, k);
                if (blend[k] < sites[k].radius / 2) st.blendShort++;
                per[k] = Layout(inp, sites[k], ref st);
            }
            int nb = 0;
            foreach (var l in per) nb += l.Count;
            var plane = new int[HDR + sites.Length * SITE_INTS + nb * BLD_INTS];
            plane[0] = sites.Length; plane[1] = nb; plane[2] = HDR; plane[3] = HDR + sites.Length * SITE_INTS;
            all = new Building[nb];
            prints = new BuildingFootprint[nb];
            int b = 0;
            for (int k = 0; k < sites.Length; k++)
            {
                var s = sites[k];
                int e = HDR + k * SITE_INTS;
                int top = s.padLevelY;
                plane[e + 0] = s.centre.x; plane[e + 1] = s.centre.y; plane[e + 2] = s.radius; plane[e + 3] = blend[k];
                plane[e + 4] = s.padLevelY; plane[e + 5] = b; plane[e + 6] = per[k].Count;
                foreach (var bd in per[k])
                {
                    var fp = BuildingKit.Footprint(in bd);
                    int f = plane[3] + b * BLD_INTS;
                    plane[f + 0] = bd.shape | (bd.rot << 8) | (bd.biome << 16);
                    plane[f + 1] = bd.origin.x; plane[f + 2] = bd.origin.y; plane[f + 3] = bd.origin.z; plane[f + 4] = bd.foundDepth;
                    plane[f + 5] = fp.max.x; plane[f + 6] = fp.max.z; plane[f + 7] = fp.max.y;
                    top = math.max(top, fp.max.y - 1);
                    all[b] = bd; prints[b] = fp;
                    b++;
                }
                plane[e + 7] = top;
            }
            st.sites = sites.Length; st.buildings = nb;
            return plane;
        }

        /// The ring width: R / 2, cut so the ring stops short of water (sea,
        /// lake, river), a feature, a blocked cell and half the gap to any other
        /// site's disc (two rings never meet).
        public const int WHY_NONE = 0, WHY_SITE = 1, WHY_SEA = 2, WHY_WATER = 3, WHY_FEATURE = 4, WHY_STAMP = 5, WHY_START = 6;
        public static readonly string[] WhyName = { "-", "site", "sea", "lake/river", "feature", "stamp", "start" };

        public static int BlendWidth(SiteInput inp, Site[] sites, int k) => BlendWidth(inp, sites, k, out _);

        public static int BlendWidth(SiteInput inp, Site[] sites, int k, out int why)
        {
            var s = sites[k];
            int R = s.radius, B = R / 2;
            why = WHY_NONE;
            for (int j = 0; j < sites.Length; j++)
            {
                if (j == k) continue;
                long dx = s.centre.x - sites[j].centre.x, dz = s.centre.y - sites[j].centre.y;
                int d = (int)math.sqrt((double)(dx * dx + dz * dz));
                int b = math.max(0, (d - R - sites[j].radius) / 2);
                if (b < B) { B = b; why = WHY_SITE; }
            }
            int n = inp.n, cv = inp.cellVoxels, reach = R + R / 2 + cv;
            int c0x = math.max(0, (s.centre.x - reach) / cv), c1x = math.min(n - 1, (s.centre.x + reach) / cv);
            int c0z = math.max(0, (s.centre.y - reach) / cv), c1z = math.min(n - 1, (s.centre.y + reach) / cv);
            for (int z = c0z; z <= c1z; z++)
            for (int x = c0x; x <= c1x; x++)
            {
                int i = z * n + x;
                var c = inp.rec[i];
                // (stamp discs and the start zone do NOT cut the ring: Apply leaves every
                // stamp / ledge column alone, and the start column is >= 96 m inside its zone)
                int w = c.IsSea || inp.h[i] <= 0 ? WHY_SEA : inp.waterClass[i] != Hydrology.DRY ? WHY_WATER : c.IsFeature ? WHY_FEATURE : WHY_NONE;
                if (w == WHY_NONE) continue;
                long qx = math.clamp(s.centre.x, x * cv, x * cv + cv - 1) - s.centre.x;
                long qz = math.clamp(s.centre.y, z * cv, z * cv + cv - 1) - s.centre.y;
                int d = (int)math.sqrt((double)(qx * qx + qz * qz));
                int bb = math.max(0, d - R - 1);
                if (bb < B) { B = bb; why = w; }
            }
            return B;
        }

        /// The site's buildings (Program order, first fitting slot each).
        public static List<Building> Layout(SiteInput inp, in Site s, ref Stats st)
        {
            var res = new List<Building>();
            var boxes = new List<int4>();
            var prog = Program(s.kind);
            st.wanted += prog.Length;
            int floorY = s.padLevelY + 1;
            uint hsh = ((uint)s.centre.x * 73856093u) ^ ((uint)s.centre.y * 19349663u) ^ ((uint)s.id * 2654435761u);
            hsh ^= hsh >> 15; hsh *= 2246822519u; hsh ^= hsh >> 13;
            int a0 = (int)(hsh >> 27);   // 0..31: where the rings start (a hash of the centre: towns differ)
            int jx = s.route >= 0 ? s.joinPoint.x - s.centre.x : 0, jz = s.route >= 0 ? s.joinPoint.y - s.centre.y : -1;
            foreach (byte shape in prog)
            {
                bool hall = shape == BuildingKit.HALL;
                bool done = false;
                for (int rho = hall ? 0 : PLAZA; rho <= s.radius && !done; rho += 10)
                {
                    int nd = rho == 0 ? 1 : DIRS;
                    for (int j = 0; j < nd && !done; j++)
                    {
                        int k = (a0 + j) & 31;
                        int bx = s.centre.x + rho * Cos(k) / 1024, bz = s.centre.y + rho * Sin(k) / 1024;
                        byte rot = rho == 0 ? FaceRot(jx, jz) : FaceRot(s.centre.x - bx, s.centre.y - bz);
                        int3 size = BuildingKit.Size(shape, rot);
                        int ox = bx - size.x / 2, oz = bz - size.z / 2;
                        var box = new int4(ox, oz, ox + size.x, oz + size.z);
                        if (!InsideDisc(box, s.centre, s.radius - EDGE) || Overlaps(box, boxes)) continue;
                        if (!SlotGroundOk(inp, box)) { st.slotGround++; continue; }
                        boxes.Add(box);
                        res.Add(new Building
                        {
                            id = s.id, shape = shape, rot = rot, biome = s.biome,
                            origin = new int3(ox, floorY, oz), foundDepth = FOUND,
                        });
                        if (shape == BuildingKit.HALL) st.halls++; else if (shape == BuildingKit.HOUSE) st.houses++; else st.huts++;
                        done = true;
                    }
                }
            }
            return res;
        }

        private static bool InsideDisc(int4 b, int2 c, int r)
        {
            long r2 = (long)r * r;
            for (int q = 0; q < 4; q++)
            {
                long x = ((q & 1) == 0 ? b.x : b.z - 1) - c.x, z = ((q & 2) == 0 ? b.y : b.w - 1) - c.y;
                if (x * x + z * z > r2) return false;
            }
            return true;
        }

        private static bool Overlaps(int4 b, List<int4> boxes)
        {
            foreach (var o in boxes)
                if (b.x < o.z + GAP && o.x < b.z + GAP && b.y < o.w + GAP && o.y < b.w + GAP) return true;
            return false;
        }

        /// Every map cell under the box is dry, unblocked land off the routes and
        /// features; the natural ground passes the step rule on a 2 m grid.
        public static bool SlotGroundOk(SiteInput inp, int4 b)
        {
            int n = inp.n, cv = inp.cellVoxels;
            for (int z = b.y / cv; z <= (b.w - 1) / cv; z++)
            for (int x = b.x / cv; x <= (b.z - 1) / cv; x++)
            {
                if (x < 0 || z < 0 || x >= n || z >= n) return false;
                int i = z * n + x;
                var c = inp.rec[i];
                if (c.IsSea || inp.h[i] <= 0 || inp.waterClass[i] != Hydrology.DRY || c.freshWater < 1) return false;
                if (c.IsFeature || c.IsRoute || inp.blocked[i]) return false;
            }
            if (inp.ground == null) return true;
            int g = Sites.GROUND_STEP;
            int mx = (b.z - 1 - b.x) / g + 1, mz = (b.w - 1 - b.y) / g + 1;
            var h = new int[mx * mz];
            for (int zz = 0; zz < mz; zz++)
            for (int xx = 0; xx < mx; xx++)
            {
                int v = inp.ground(b.x + xx * g, b.y + zz * g);
                h[zz * mx + xx] = v;
                if (xx > 0 && Sites.StepTooSteep(math.abs(v - h[zz * mx + xx - 1]))) return false;
                if (zz > 0 && Sites.StepTooSteep(math.abs(v - h[(zz - 1) * mx + xx]))) return false;
            }
            return true;
        }

        /// Task 190: no generated tree within a site's disc + ring (+ one cell):
        /// zero those cells of TreeTable's density plane.
        public static void ClearCells(byte[] cellPlane, int n, int cv, int[] plane)
        {
            if (plane == null || plane.Length <= 1) return;
            for (int k = 0; k < plane[0]; k++)
            {
                int e = plane[2] + k * SITE_INTS;
                int cx = plane[e], cz = plane[e + 1], rr = plane[e + 2] + math.max(plane[e + 3], cv) + cv;
                for (int z = math.max(0, (cz - rr) / cv); z <= math.min(n - 1, (cz + rr) / cv); z++)
                for (int x = math.max(0, (cx - rr) / cv); x <= math.min(n - 1, (cx + rr) / cv); x++)
                {
                    long qx = math.clamp(cx, x * cv, x * cv + cv - 1) - cx, qz = math.clamp(cz, z * cv, z * cv + cv - 1) - cz;
                    if (qx * qx + qz * qz <= (long)rr * rr) cellPlane[z * n + x] = 0;
                }
            }
        }

        /// The same cells as a mask (FluidPockets' keep-out), from a site plan.
        public static void MarkCells(bool[] zone, int n, int cv, Site[] sites)
        {
            if (sites == null) return;
            foreach (var s in sites)
            {
                int cx = s.centre.x, cz = s.centre.y, rr = s.radius + math.max(s.radius / 2, cv) + cv;
                for (int z = math.max(0, (cz - rr) / cv); z <= math.min(n - 1, (cz + rr) / cv); z++)
                for (int x = math.max(0, (cx - rr) / cv); x <= math.min(n - 1, (cx + rr) / cv); x++)
                {
                    long qx = math.clamp(cx, x * cv, x * cv + cv - 1) - cx, qz = math.clamp(cz, z * cv, z * cv + cv - 1) - cz;
                    if (qx * qx + qz * qz <= (long)rr * rr) zone[z * n + x] = true;
                }
            }
        }

        // ------------------------------------------------------------------
        // Per column / voxel (Burst-safe)
        // ------------------------------------------------------------------

        /// THE FLATTEN at column (x, z); ShapeField.Sample, after the pockets.
        public static void Apply(in TerrainSpec spec, in MapView map, float x, float z, ref ColumnSample c)
        {
            if (map.town == null || !spec.Towns) return;
            int* t = map.town;
            int ns = t[0];
            if (ns <= 0) return;
            int ix = (int)math.floor(x), iz = (int)math.floor(z);
            for (int k = 0; k < ns; k++)
            {
                int* e = t + t[2] + k * SITE_INTS;
                int R = e[2], RB = R + e[3];
                long dx = ix - e[0], dz = iz - e[1];
                if (dx < -RB || dx > RB || dz < -RB || dz > RB) continue;
                long d2 = dx * dx + dz * dz, rb2 = (long)RB * RB, r2 = (long)R * R;
                if (d2 > rb2) continue;
                int pad = e[4];
                if (d2 <= r2)
                {
                    c.height = pad;
                    c.water = ColumnSample.NO_WATER_Y;
                    c.open1Lo = c.open1Hi = c.open2Lo = c.open2Hi = c.open3Lo = c.open3Hi = 0;
                    c.carvesTop = false; c.stamp3D = false; c.stampMat = 0; c.lipLo = 0; c.rimGround = 0;
                }
                else
                {
                    // still water, a river lip, a stamp or a ledge: never moved
                    if (c.water != ColumnSample.NO_WATER_Y || c.lipLo != 0 || c.stampMat != 0 || c.stamp3D || c.carvesTop
                        || c.open1Hi != 0 || c.open2Hi != 0 || c.open3Hi != 0) return;
                    long num = d2 - r2, den = rb2 - r2;
                    long tq = num * 4096 / den;                                    // 0..4096
                    long sm = tq * tq * (3 * 4096 - 2 * tq) / (4096L * 4096L);     // smoothstep, 0..4096
                    long dv = (long)(c.height - pad) * sm;                         // rounded: the ring's edge IS the natural ground
                    c.height = pad + (int)((dv >= 0 ? dv + 2048 : dv - 2048) / 4096);
                }
                int cap = c.height - CAVE_CAP;
                c.riverFloor = c.riverFloor == 0 ? cap : math.min(c.riverFloor, cap);
                return;   // rings never meet (BlendWidth)
            }
        }

        public static Building BuildingAt(int* f) => new Building
        {
            shape = (byte)(f[0] & 255), rot = (byte)((f[0] >> 8) & 3), biome = (byte)((f[0] >> 16) & 255),
            origin = new int3(f[1], f[2], f[3]), foundDepth = f[4],
        };

        /// The building GenMat at world voxel v (LOD 0, the point query); false =
        /// no building part there (keep the terrain). AIR parts give GenMat.Air.
        public static bool VoxelAt(int* t, int3 v, out byte g)
        {
            g = GenMat.Air;
            int ns = t[0];
            for (int k = 0; k < ns; k++)
            {
                int* e = t + t[2] + k * SITE_INTS;
                int R = e[2];
                if (v.x < e[0] - R || v.x > e[0] + R || v.z < e[1] - R || v.z > e[1] + R || v.y > e[7]) continue;
                for (int b = e[5]; b < e[5] + e[6]; b++)
                {
                    int* f = t + t[3] + b * BLD_INTS;
                    if (v.x < f[1] || v.x >= f[5] || v.z < f[3] || v.z >= f[6] || v.y < f[2] - f[4] || v.y >= f[7]) continue;
                    var bd = BuildingAt(f);
                    byte r = BuildingKit.Role(in bd, v);
                    if (r == BuildingKit.NONE) return false;
                    g = BuildingKit.Material(r, bd.biome, bd.shape);
                    return true;
                }
            }
            return false;
        }

        public static bool Solid(byte role) => role != BuildingKit.NONE && role != BuildingKit.AIR;

        /// The part a coarse voxel (sample point p, shell shift, sample offset
        /// half) shows: the sample point's part if solid, else the highest solid
        /// part on the cell's stride-2 sub-grid (y high to low, then z, x), else
        /// the sample point's (AIR or NONE). shift 0 = Role at p.
        public static byte CoarseRole(in Building b, int3 p, int shift, int half)
        {
            byte r0 = BuildingKit.Role(in b, p);
            if (shift <= 0 || Solid(r0)) return r0;
            int cnt = 1 << (shift - 1);
            int3 s0 = p - half + (half & 1);
            for (int j = cnt - 1; j >= 0; j--)
            for (int kz = 0; kz < cnt; kz++)
            for (int kx = 0; kx < cnt; kx++)
            {
                byte r = BuildingKit.Role(in b, s0 + new int3(kx, j, kz) * 2);
                if (Solid(r)) return r;
            }
            return r0;
        }

        /// The highest building voxel of any site whose disc box meets the
        /// footprint [x0, x1] x [z0, z1], or int.MinValue.
        public static int TopOver(int* t, int x0, int z0, int x1, int z1)
        {
            int top = int.MinValue, ns = t[0];
            for (int k = 0; k < ns; k++)
            {
                int* e = t + t[2] + k * SITE_INTS;
                int R = e[2];
                if (e[6] == 0 || e[0] + R < x0 || e[0] - R > x1 || e[1] + R < z0 || e[1] - R > z1) continue;
                top = math.max(top, e[7]);
            }
            return top;
        }
    }
}
