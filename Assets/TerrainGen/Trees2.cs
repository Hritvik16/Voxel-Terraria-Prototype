// Assets/TerrainGen/Trees2.cs
//
// Task 210 (owner 2026-11-14, Q-AD "a for all"): the WORLD space-colonization
// trees, behind `-trees2 1` (TerrainSpec.trees2On; default OFF = today's trees,
// byte for byte).
//
// WHAT IS SHARED. A tree is not built per world position: task 200's method (a)
// (TreeGallery.Build, M_SPACE) grows a small LIBRARY of prototypes once per
// process (species x variants, fixed seeds 1..V, so variants 1-3 are exactly the
// trees the owner saw in task 200), on worker threads, packed 2 bits per voxel
// (0 none, 1 wood, 2 leaf, 3 a second material) in 8^3 bricks. A world tree is
// an INSTANCE: (prototype, one of 4 quarter turns, a brick-aligned root). Every
// chunk, shell and point query reads the same prototype, so the CPU cost of a
// tree is paid once per prototype, not once per chunk it touches, and the
// library costs a few MB whatever the world holds.
//
// WHERE TREES GO (a pure function of the world seed and a grid cell, like Trees.cs):
//   normal grid  12.8 m cells, at most one tree each (oak, birch, spruce, acacia);
//                species by the biome's BRICK BUDGET (below), density x the tree
//                plane (TreeTable: zero on sea / route / feature / town cells) x the
//                cover slot multiplier (Trees.SlotMul), as today's trees.
//   giant grid   102.4 m cells: a banyan in jungle, a GROVE of 3-5 redwoods in the
//                wettest forest cells (tree plane >= WET_PLANE: river plains /
//                wetland on walkable ground). Rare landmarks at their real size.
//   192's trees  Trees.Build keeps only the forms the library does not grow
//                (Keep192: jungle emergents + palms, savanna baobabs, snow pines at
//                6/16, desert cacti; none in forest) and drops any whose root lies
//                inside a library tree's KEEP radius.
// Suppression is ONE level per tier, so the answer never depends on how far a
// consumer looks: giants (tier 0) > oaks (tier 1) > birch / spruce / acacia (tier 2);
// a tier-t candidate dies inside a surviving lower-tier tree's keep radius, or
// inside a higher-priority same-tier CANDIDATE's (not survivor's) keep radius.
//
// THE BUDGET (owner Q-AD5 (a): "fewer, bigger trees", memory is a hard limit).
// A brick = one 512-byte body on the CPU and on the GPU, so the area budget is in
// BRICKS: BUDGET[biome] = expected tree bricks per 51.2 m x 51.2 m at full plane
// on flat ground, set to today's trees' (Trees.cs) expected bricks there
// (density x 64 tree cells x 266 bricks per tree). Each species takes SHARE/16 of
// it; its probability per normal cell = budget share / (16 cells x the species'
// MEAN prototype bricks, measured from the built library). So the density follows
// the trees: a bigger prototype makes its species rarer, never the area costlier.
//
// RULES AT A SITE (same physical rules as Trees.Build): land above the sea, no
// water over the ground, no stamp / ledge / opening at the root; a 45 deg foot
// across the trunk; canopy clearance (ground at 7/8 of the leaves' reach below
// the leaves); the crown's reach over tree-plane cells (no crown over a road,
// town or the sea); giants: every root foot reaches the ground.
// Trees fill AIR only (Trees.cs's contract). Precedence, the same in every
// consumer: library wood (instances in list order), library leaves, then 192's.
//
// COORDINATES: integer only. Roots are multiples of 8 on every axis (the trunk
// sits 0-7 voxels lower; its roots run 4 m down), so a prototype brick lands on a
// world brick and every instance of a prototype has the same bytes in the bricks
// it does not share with terrain (what a byte-duplicate GPU pool could share).
// Quarter turns about the root's corner map the voxel and brick lattices to themselves.
//
// TASK 213 (owner 2026-11-15 Q-AJ (a): "make the forest denser"; spec.trees2On 1, the
// default meaning of `-trees2 1`; trees2On 2 = task 210's placement byte for byte). 210's
// forest was capped by its grid, not by memory: one tree per 12.8 m cell, 95 % of full-plane
// forest cells already held one. Dense mode keeps every rule above and changes only WHERE:
//   oaks (tier 1) alone on the 12.8 m normal grid;
//   the small trees (tier 2: birch, spruce, acacia) on a FINE 6.4 m grid (root at [8, 56) of
//   its cell, multiples of 8), one tree a fine cell;
//   the forest's brick budget raised (BUDGET_D, still bricks per area: a bigger tree stays rarer;
//   snow and savanna keep 210's budget, only their grid changes).
// Suppression is the same one level per tier, but read through CELL INDEX maps (the caller's
// scratch): every keep radius is <= KEEP_N (96 voxels), so an oak's rivals lie in the 3 x 3
// normal cells round it, a tier-2 tree's in the 5 x 5 fine cells -- O(candidates), not 210's
// O(candidates^2), which a 4x finer grid at shell 5 would have made ~16x dearer.
using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace TerrainGen
{
    /// One library prototype, packed (unmanaged; owned by the chunk source's library).
    public unsafe struct TreeProto
    {
        public int3 lo, nb;          // brick-aligned local origin, bricks per axis
        public int3 vlo, vhi;        // used voxel box, local to the root
        public int* index;           // brick (b.y * nb.z + b.z) * nb.x + b.x -> (slot << 2) | 1 wood | 2 leaf; -1 = empty
        public byte* codes;          // slot * 128: 2 bits per voxel at ((w.y << 6) | (w.z << 3) | w.x)
        public byte m1, m2, m3;      // GenMat of code 1 (wood), 2 (leaf), 3 (other)
        public byte species, tier, giant;
        public int bricks, reach, keep, leafBottom, leafReach, footR, top;
        public int feet;             // giants' root feet (x, z, bottom y, -) local
        public int4* foot;
    }

    /// The library the Burst jobs read (published by the chunk source through a SharedStatic).
    public unsafe struct TreeLib
    {
        public TreeProto* protos;
        public int count;
        public fixed int first[8];      // species -> first prototype
        public fixed int variants[8];   // species -> prototypes (0 = none built)
        public fixed int pq[48];        // biome * 8 + species: probability per normal cell (1/65536) at full plane, flat
        public int pqMax;               // max over biomes of the sum
        public fixed int pqD[48];       // task 213 (dense): normal grid (tier 1 only), per normal cell
        public fixed int pqF[48];       // task 213 (dense): fine 6.4 m grid (tier 2 only), per fine cell
        public int pqDMax, pqFMax;
        public int ready;
    }

    /// One placed tree (a gather's candidate list).
    public struct LibInst
    {
        public int3 root, lo, hi;
        public int proto, keep2, gx, gz;
        public uint prio;
        public byte rot, biome, tier, state;   // state: 0 rejected, 1 candidate, 2 survivor, 3 candidate out of the survivor range
        public byte cand;                       // tier 2: a candidate after the oaks (the same-tier test reads this, never state)
    }

    public static unsafe class Trees2
    {
        public const int N_SHIFT = 7, N_CELL = 1 << N_SHIFT;     // 12.8 m normal cells
        public const int N_MARGIN = 16, N_SPAN = 96;               // root at [16, 112) (multiples of 8)
        public const int F_SHIFT = 6, F_CELL = 1 << F_SHIFT;     // task 213: 6.4 m fine cells (tier 2, dense mode)
        public const int F_MARGIN = 8, F_SPAN = 48;                // root at [8, 56) (multiples of 8)
        public const int G_SHIFT = 10, G_CELL = 1 << G_SHIFT;    // 102.4 m giant cells
        public const int G_MARGIN = 160, G_SPAN = 704;
        /// Bounds the library enforces (a prototype beyond them is not used).
        public const int REACH_N = 160, REACH_G = 240, MAX_HEIGHT = 760;
        public const int KEEP_N = 96, KEEP_G = 200;
        public const int GROVE_R = 200, GROVE_MAX = 5, GROVE_MIN = 90;
        public const int WET_PLANE = 18;   // TreeTable.CellValue: river plains 18, wetland 20 (walk band)
        /// Gather ranges past the footprint (see Gather): normal cells, giant cell centres.
        public const int RANGE_N = REACH_N + 3 * KEEP_N, RANGE_G = RANGE_N + KEEP_G + GROVE_R + 8;   // + 8: a member's root is snapped down to a brick
        public const byte SHAPE_LIB = 0x80;   // Tree.shape bit: a library instance (prim0 = prototype, shape & 3 = turn)
        private const uint SALT_N = 0x7EE52100u, SALT_G = 0x61A27210u, SALT_F = 0x5B1C2130u;

        // ---- the budget (bricks per 51.2 m x 51.2 m, full plane, flat) and species shares (1/16) ----
        // Today's trees there: density (per mille per 6.4 m cell) x 64 cells x 266 bricks (task 200's cost.txt).
        private static readonly int[] BUDGET = { 800 * 64 * 266 / 1000, 0, 300 * 64 * 266 / 1000 * 10 / 16, 0, 110 * 64 * 266 / 1000, 0 };
        //                                       forest                desert  snow (192's pines keep 6/16)   jungle  savanna        badlands
        private static readonly byte[] SHARE_OAK = { 10, 0, 0, 0, 0, 0 }, SHARE_BIRCH = { 6, 0, 0, 0, 0, 0 },
                                       SHARE_SPRUCE = { 0, 0, 16, 0, 0, 0 }, SHARE_ACACIA = { 0, 0, 0, 0, 16, 0 };
        // ---- task 213 (dense mode): the forest's budget and shares; other biomes keep 210's ----
        // Forest: 1.4 x 210's budget, oak 8/16 + birch 8/16. Calibrated by Trees2Tests.Dense_Sweep
        // (Logs/TerrainGen/trees213/sweep.txt): in a 256 m forest window 1.55 x 210's trees for 1.33 x its
        // world tree bricks -- inside the ~30 k of 254 k GPU bodies 210 left free at forest_100m (78.4 k tree
        // bodies there; 2 x the budget was 1.88 x the bricks, over it). Dense_Density_FootprintsAgree pins it.
        public static readonly int[] BUDGET_D = { 800 * 64 * 266 / 1000 * 14 / 10, 0, 300 * 64 * 266 / 1000 * 10 / 16, 0, 110 * 64 * 266 / 1000, 0 };
        public static readonly byte[] SHARE_OAK_D = { 8, 0, 0, 0, 0, 0 }, SHARE_BIRCH_D = { 8, 0, 0, 0, 0, 0 };
        /// Giants per giant cell (1/65536) at full plane: a banyan per jungle cell, a grove per wet forest cell.
        public const int P_BANYAN = 19661, P_GROVE = 22938;   // 0.30, 0.35
        /// Task 192's trees the library leaves (1/16 of their density): forest none (oak + birch), desert all
        /// (cactus), snow 6/16 (the pines beside the spruces), jungle 14/16 (emergents + palms; the 2/16 pay for
        /// the banyans: a full jungle box holds ~160 k tree bodies, 2/16 of it ~20 k, a banyan ~9.4 k minus the
        /// 192 trees in its keep radius -- measured in 210's launches: the jungle box is FULL with today's trees,
        /// so a banyan on top of them raised the refusals at its pose), savanna all but only the baobab form
        /// (Trees.Build drops its acacias), badlands (density 0 anyway).
        public static int Keep192(byte biome) => biome == GenBiome.Forest ? 0 : biome == GenBiome.Snow ? 6 : biome == GenBiome.Jungle ? 14 : 16;

        public const int SP_COUNT = TreeGallery.SP_COUNT;
        /// Variants per species (fixed seeds 1..V): oak, banyan, redwood, birch, spruce, acacia.
        public static readonly int[] VARIANTS = { 6, 3, 3, 6, 6, 6 };
        public static int Tier(int species) => species == TreeGallery.SP_BANYAN || species == TreeGallery.SP_REDWOOD ? 0 : species == TreeGallery.SP_OAK ? 1 : 2;
        public static int Share(int biome, int species)
        {
            switch (species)
            {
                case TreeGallery.SP_OAK: return SHARE_OAK[biome];
                case TreeGallery.SP_BIRCH: return SHARE_BIRCH[biome];
                case TreeGallery.SP_SPRUCE: return SHARE_SPRUCE[biome];
                case TreeGallery.SP_ACACIA: return SHARE_ACACIA[biome];
                default: return 0;
            }
        }
        public static int Budget(int biome) => biome < BUDGET.Length ? BUDGET[biome] : 0;
        /// Task 213 (dense mode): the shares and budget.
        public static int ShareD(int biome, int species)
            => species == TreeGallery.SP_OAK ? SHARE_OAK_D[biome] : species == TreeGallery.SP_BIRCH ? SHARE_BIRCH_D[biome] : Share(biome, species);
        public static int BudgetD(int biome) => biome < BUDGET_D.Length ? BUDGET_D[biome] : 0;

        // =====================================================================
        // Library build (managed, thread-safe: one call per prototype on a worker)
        // =====================================================================
        public sealed class ProtoBuild
        {
            public int species, variant; public uint seed; public bool ok;
            public int3 lo, nb, vlo, vhi;
            public int[] index; public byte[] codes;
            public byte m1, m2, m3;
            public int bricks, reach, keep, leafBottom, leafReach, footR, top, wood, leaf;
            public int4[] feet;
            public double ms;
        }

        public static ProtoBuild BuildProto(int species, int variant)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var pb = new ProtoBuild { species = species, variant = variant, seed = (uint)(variant + 1) };
            var v = TreeGallery.Build(species, TreeGallery.M_SPACE, pb.seed, null, out var st, out var k, world: true);
            pb.m1 = k.sp.wood; pb.m2 = k.sp.leaf;
            pb.lo = v.lo; pb.nb = v.nb;
            pb.index = new int[v.index.Length];
            pb.codes = new byte[128 * v.bricks.Count];
            int3 vlo = int.MaxValue, vhi = int.MinValue;
            int leafBottom = int.MaxValue, leafReach = 0, footR = 0;
            var feet = new Dictionary<int, int4>();
            for (int bi = 0; bi < v.index.Length; bi++)
            {
                int sl = v.index[bi];
                if (sl < 0) { pb.index[bi] = -1; continue; }
                int bx = bi % v.nb.x, bz = (bi / v.nb.x) % v.nb.z, by = bi / (v.nb.x * v.nb.z);
                byte[] b = v.bricks[sl];
                int flags = 0;
                for (int off = 0; off < 512; off++)
                {
                    byte m = b[off];
                    if (m == 0) continue;
                    int code = m == pb.m1 ? 1 : m == pb.m2 ? 2 : 3;
                    if (code == 3) { if (pb.m3 == 0) pb.m3 = m; else if (pb.m3 != m) code = 1; }   // a 4th material falls back to wood
                    pb.codes[sl * 128 + (off >> 2)] |= (byte)(code << ((off & 3) * 2));
                    flags |= code == 2 ? 2 : 1;
                    int3 p = v.lo + new int3(bx, by, bz) * 8 + new int3(off & 7, off >> 6, (off >> 3) & 7);
                    vlo = math.min(vlo, p); vhi = math.max(vhi, p);
                    if (code == 2) { pb.leaf++; leafBottom = math.min(leafBottom, p.y); leafReach = math.max(leafReach, math.max(math.abs(p.x), math.abs(p.z))); }
                    else
                    {
                        pb.wood++;
                        if (p.y == 0) footR = math.max(footR, math.max(math.abs(p.x), math.abs(p.z)));
                        if (p.y <= -8)
                        {   // a root foot: the deepest wood voxel per brick column
                            int key = (p.z >> 3) * 4096 + (p.x >> 3);
                            if (!feet.TryGetValue(key, out var f) || p.y < f.z) feet[key] = new int4(p.x, p.z, p.y, 0);
                        }
                    }
                }
                pb.index[bi] = (sl << 2) | flags;
                pb.bricks++;
            }
            pb.vlo = vlo; pb.vhi = vhi;
            pb.reach = math.max(math.max(-vlo.x, vhi.x), math.max(-vlo.z, vhi.z)) + 1;   // + 1: a quarter turn moves a voxel by one
            pb.top = vhi.y;
            pb.leafBottom = leafBottom == int.MaxValue ? vhi.y : leafBottom;
            pb.leafReach = leafReach;
            pb.footR = math.max(footR, 2);
            bool giant = Tier(species) == 0;
            pb.keep = (leafReach * (giant ? 8 : 6) + 5) / 10;
            pb.keep = math.min(pb.keep, giant ? KEEP_G : KEEP_N);
            var fl = new List<int4>(feet.Values);
            fl.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));   // a fixed order (determinism)
            pb.feet = fl.ToArray();
            pb.ok = pb.bricks > 0 && pb.reach <= (giant ? REACH_G : REACH_N) && pb.top <= MAX_HEIGHT;
            pb.ms = sw.Elapsed.TotalMilliseconds;
            return pb;
        }

        /// The species' probability per normal cell in each biome (1/65536), from the budget and the
        /// prototypes' MEAN bricks; fills lib.pq / lib.pqMax. `meanBricks[s]` = 0 when s has no prototype.
        public static void Probabilities(ref TreeLib lib, double[] meanBricks)
        {
            int pmax = 0;
            for (int b = 0; b < GenBiome.Count; b++)
            {
                double sum = 0; var p = new double[SP_COUNT];
                for (int s = 0; s < SP_COUNT; s++)
                {
                    int sh = Share(b, s);
                    if (sh == 0 || meanBricks[s] <= 0) continue;
                    p[s] = Budget(b) * (sh / 16.0) / (16.0 * meanBricks[s]);   // 16 normal cells per 51.2 m square
                    sum += p[s];
                }
                double scale = sum > 0.95 ? 0.95 / sum : 1.0;   // at most one tree per cell
                int tot = 0;
                for (int s = 0; s < 8; s++)
                {
                    int q = s < SP_COUNT ? (int)math.round(p[s] * scale * 65536.0) : 0;
                    lib.pq[b * 8 + s] = q; tot += q;
                }
                pmax = math.max(pmax, tot);
            }
            lib.pqMax = pmax;
            // task 213 (dense): tier 1 on the normal grid (16 cells per 51.2 m), tier 2 on the fine grid (64)
            int dmax = 0, fmax = 0;
            for (int b = 0; b < GenBiome.Count; b++)
                for (int grid = 0; grid < 2; grid++)
                {
                    double sum = 0, cells = grid == 0 ? 16.0 : 64.0; var p = new double[SP_COUNT];
                    for (int s = 0; s < SP_COUNT; s++)
                    {
                        int sh = ShareD(b, s), t = Tier(s);
                        if (sh == 0 || meanBricks[s] <= 0 || t == 0 || (t == 1) != (grid == 0)) continue;
                        p[s] = BudgetD(b) * (sh / 16.0) / (cells * meanBricks[s]);
                        sum += p[s];
                    }
                    double scale = sum > 0.95 ? 0.95 / sum : 1.0;
                    int tot = 0;
                    for (int s = 0; s < 8; s++)
                    {
                        int q = s < SP_COUNT ? (int)math.round(p[s] * scale * 65536.0) : 0;
                        if (grid == 0) lib.pqD[b * 8 + s] = q; else lib.pqF[b * 8 + s] = q;
                        tot += q;
                    }
                    if (grid == 0) dmax = math.max(dmax, tot); else fmax = math.max(fmax, tot);
                }
            lib.pqDMax = dmax; lib.pqFMax = fmax;
        }

        // =====================================================================
        // Turns and the voxel query
        // =====================================================================
        /// World offset from the root -> prototype local voxel (the inverse of turn `rot`).
        public static int3 ToProto(int3 l, int rot)
        {
            switch (rot & 3)
            {
                case 1: return new int3(l.z, l.y, -1 - l.x);
                case 2: return new int3(-1 - l.x, l.y, -1 - l.z);
                case 3: return new int3(-1 - l.z, l.y, l.x);
                default: return l;
            }
        }

        /// Prototype local voxel -> world offset from the root (turn `rot`).
        public static int3 FromProto(int3 p, int rot)
        {
            switch (rot & 3)
            {
                case 1: return new int3(-1 - p.z, p.y, p.x);
                case 2: return new int3(-1 - p.x, p.y, -1 - p.z);
                case 3: return new int3(p.z, p.y, -1 - p.x);
                default: return p;
            }
        }

        /// The 2-bit code at prototype local voxel p (0 = none).
        public static int Code(in TreeProto t, int3 p)
        {
            int3 q = p - t.lo;
            if (math.any(q < 0) || math.any(q >= t.nb * 8)) return 0;
            int3 b = q >> 3;
            int e = t.index[(b.y * t.nb.z + b.z) * t.nb.x + b.x];
            if (e < 0) return 0;
            int3 w = q & 7;
            int off = (w.y << 6) | (w.z << 3) | w.x;
            return (t.codes[(e >> 2) * 128 + (off >> 2)] >> ((off & 3) * 2)) & 3;
        }

        /// Brick flags (1 wood, 2 leaf; 0 empty / outside) of the prototype brick holding local voxel p.
        public static int BrickFlags(in TreeProto t, int3 p)
        {
            int3 q = p - t.lo;
            if (math.any(q < 0) || math.any(q >= t.nb * 8)) return 0;
            int3 b = q >> 3;
            int e = t.index[(b.y * t.nb.z + b.z) * t.nb.x + b.x];
            return e < 0 ? 0 : e & 3;
        }

        public static byte CodeMat(in TreeProto t, int code) => code == 1 ? t.m1 : code == 2 ? t.m2 : code == 3 ? t.m3 : (byte)0;

        /// The library material of fine voxel v for instance tree `t` (Tree.shape has SHAPE_LIB),
        /// pass 0 = wood (codes 1 and 3) only, pass 1 = leaves only; 0 = none.
        public static byte MatAt(in TreeLib lib, in Tree t, int3 v, int pass)
        {
            if (math.any(v < t.lo) || math.any(v > t.hi)) return 0;
            ref var P = ref lib.protos[t.prim0];
            int c = Code(in P, ToProto(v - t.root, t.shape & 3));
            if (c == 0 || (c == 2) != (pass == 1)) return 0;
            return CodeMat(in P, c);
        }

        /// The library material at v over gathered trees [0, n) (wood of every instance, then leaves), or 0.
        public static byte At(in TreeLib lib, Tree* trees, int n, int3 v)
        {
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < n; i++)
                {
                    if ((trees[i].shape & SHAPE_LIB) == 0) continue;
                    byte m = MatAt(in lib, in trees[i], v, pass);
                    if (m != 0) return m;
                }
            return 0;
        }

        // =====================================================================
        // Placement
        // =====================================================================
        private static int Span(uint u16, int span) => (int)((u16 * (uint)span) >> 16);

        private static bool SiteOk(in TerrainSpec spec, in ColumnSample c)
        {
            if (c.height <= spec.seaLevelY) return false;
            if (c.water != ColumnSample.NO_WATER_Y && c.water >= c.height) return false;
            return !(c.stampMat != 0 || c.stamp3D || c.carvesTop || c.lipLo != 0
                     || c.open1Hi > c.open1Lo || c.open2Hi > c.open2Lo || c.open3Hi > c.open3Lo);
        }

        private static int HeightAt(in TerrainSpec spec, in MapView map, in Offsets o, int x, int z)
            => ShapeField.Sample(in spec, in map, in o, x + 0.5f, z + 0.5f, 1, false).height;

        private static int Plane(in TerrainSpec spec, in MapView map, int x, int z)
        {
            if (x < 0 || z < 0 || x >= spec.worldSizeVoxels || z >= spec.worldSizeVoxels) return 0;
            return map.trees[Ores.CellIndex(x, z, spec.mapCells, spec.mapCellVoxels)];
        }

        /// Prototype pi turned by rot, rooted at O (ground top h, biome b): the site rules; fills t.
        private static bool Site(in TerrainSpec spec, in MapView map, in Offsets o, in TreeLib lib, int pi, int rot,
                                 int3 O, int h, byte biome, out LibInst t)
        {
            t = default;
            ref var P = ref lib.protos[pi];
            // the crown stays over tree-plane cells (no crown over a road, town, feature or the sea)
            int R = P.reach, D = (R * 7) / 10;
            if (Plane(in spec, in map, O.x + R, O.z) == 0 || Plane(in spec, in map, O.x - R, O.z) == 0
                || Plane(in spec, in map, O.x, O.z + R) == 0 || Plane(in spec, in map, O.x, O.z - R) == 0
                || Plane(in spec, in map, O.x + D, O.z + D) == 0 || Plane(in spec, in map, O.x - D, O.z + D) == 0
                || Plane(in spec, in map, O.x + D, O.z - D) == 0 || Plane(in spec, in map, O.x - D, O.z - D) == 0) return false;
            // the foot: no tree on a sheer foot (> 45 deg across the trunk + 2), as Trees.Build
            int d = P.footR + 2;
            int hE = HeightAt(in spec, in map, in o, O.x + d, O.z), hW = HeightAt(in spec, in map, in o, O.x - d, O.z);
            int hN = HeightAt(in spec, in map, in o, O.x, O.z + d), hS = HeightAt(in spec, in map, in o, O.x, O.z - d);
            int dmax = math.max(math.max(math.abs(hE - h), math.abs(hW - h)), math.max(math.abs(hN - h), math.abs(hS - h)));
            if (dmax > d) return false;
            // canopy clearance: the ground at 7/8 of the leaves' reach stays under the leaves' bottom
            int rc = (P.leafReach * 7) >> 3;
            int g = math.max(math.max(HeightAt(in spec, in map, in o, O.x + rc, O.z), HeightAt(in spec, in map, in o, O.x - rc, O.z)),
                             math.max(HeightAt(in spec, in map, in o, O.x, O.z + rc), HeightAt(in spec, in map, in o, O.x, O.z - rc)));
            if (g >= O.y + P.leafBottom - 2) return false;
            // giants: every root foot reaches the ground (a prototype is grown on flat ground)
            if (P.giant != 0)
                for (int f = 0; f < P.feet; f++)
                {
                    int4 ft = P.foot[f];
                    int3 w = FromProto(new int3(ft.x, 0, ft.y), rot);
                    if (HeightAt(in spec, in map, in o, O.x + w.x, O.z + w.z) < O.y + ft.z + 2) return false;
                }
            int3 a = FromProto(P.vlo, rot), b = FromProto(P.vhi, rot);
            t = new LibInst
            {
                root = O, lo = O + math.min(a, b), hi = O + math.max(a, b), proto = pi, keep2 = P.keep * P.keep,
                rot = (byte)rot, biome = biome, tier = P.tier, state = 1,
            };
            return true;
        }

        /// The normal-grid cell's tree candidate (own rules + no surviving giant around it is checked by Gather).
        public static bool NormalCell(in TerrainSpec spec, in MapView map, in Offsets o, in TreeLib lib, int gx, int gz, out LibInst t)
            => GridCell(in spec, in map, in o, in lib, gx, gz, 0, out t);

        /// grid 0 = task 210's normal cell (lib.pq), 1 = dense normal cell (lib.pqD, oaks), 2 = dense fine cell (lib.pqF).
        public static bool GridCell(in TerrainSpec spec, in MapView map, in Offsets o, in TreeLib lib, int gx, int gz, int grid, out LibInst t)
        {
            t = default;
            bool fine = grid == 2;
            uint seed = spec.seed ^ (fine ? SALT_F : SALT_N);
            int shift = fine ? F_SHIFT : N_SHIFT, margin = fine ? F_MARGIN : N_MARGIN, span = fine ? F_SPAN : N_SPAN;
            int pqMax = grid == 0 ? lib.pqMax : grid == 1 ? lib.pqDMax : lib.pqFMax;
            if (pqMax == 0) return false;
            uint h0 = Ores.Hash(seed, gx, 0, gz);
            int rx = (gx << shift) + margin + (Span(h0 & 0xFFFFu, span) & ~7);
            int rz = (gz << shift) + margin + (Span(h0 >> 16, span) & ~7);
            int plane = math.min(16, Plane(in spec, in map, rx, rz));
            if (plane == 0) return false;
            long roll = Ores.Hash(seed, gx, 1, gz) & 0xFFFFFFu;
            if (roll * 16 >= (long)pqMax * plane * 256) return false;   // the cheap reject before any sample
            var c = ShapeField.Sample(in spec, in map, in o, rx + 0.5f, rz + 0.5f, 1);
            byte b = c.surfBiome;
            if (b >= GenBiome.Count || !SiteOk(in spec, in c)) return false;
            long mul = Trees.SlotMul(c.cover);
            long acc = 0; int sp = -1;
            for (int s = 0; s < SP_COUNT; s++)
            {
                int q = grid == 0 ? lib.pq[b * 8 + s] : grid == 1 ? lib.pqD[b * 8 + s] : lib.pqF[b * 8 + s];
                if (q == 0) continue;
                acc += q;
                if (roll * 16 < acc * plane * mul) { sp = s; break; }   // roll / 2^24 < sum P x plane/16 x mul/256
            }
            if (sp < 0 || lib.variants[sp] == 0) return false;
            uint h2 = Ores.Hash(seed, gx, 2, gz);
            int pi = lib.first[sp] + Span(h2 & 0xFFFFu, lib.variants[sp]);
            if (!Site(in spec, in map, in o, in lib, pi, (int)(h2 >> 16) & 3, new int3(rx, c.height & ~7, rz), c.height, b, out t)) return false;
            t.gx = gx; t.gz = gz; t.prio = Ores.Hash(seed, gx, 3, gz);
            return true;
        }

        /// The giant cell's trees (a banyan, or a redwood grove's members) appended at buf[n..].
        public static void GiantCell(in TerrainSpec spec, in MapView map, in Offsets o, in TreeLib lib, int gx, int gz,
                                     LibInst* buf, ref int n, int cap)
        {
            uint seed = spec.seed ^ SALT_G;
            uint h0 = Ores.Hash(seed, gx, 0, gz);
            int cx = (gx << G_SHIFT) + G_MARGIN + (Span(h0 & 0xFFFFu, G_SPAN) & ~7);
            int cz = (gz << G_SHIFT) + G_MARGIN + (Span(h0 >> 16, G_SPAN) & ~7);
            int plane = Plane(in spec, in map, cx, cz);
            if (plane == 0) return;
            long roll = Ores.Hash(seed, gx, 1, gz) & 0xFFFFFFu;
            int pl = math.min(16, plane);
            if (roll * 16 >= (long)math.max(P_BANYAN, P_GROVE) * pl * 256) return;
            var c = ShapeField.Sample(in spec, in map, in o, cx + 0.5f, cz + 0.5f, 1);
            if (c.surfBiome == GenBiome.Jungle && lib.variants[TreeGallery.SP_BANYAN] > 0)
            {
                if (roll * 16 >= (long)P_BANYAN * pl * 256 || !SiteOk(in spec, in c) || n >= cap) return;
                uint h2 = Ores.Hash(seed, gx, 2, gz);
                int pi = lib.first[TreeGallery.SP_BANYAN] + Span(h2 & 0xFFFFu, lib.variants[TreeGallery.SP_BANYAN]);
                if (!Site(in spec, in map, in o, in lib, pi, (int)(h2 >> 16) & 3, new int3(cx, c.height & ~7, cz), c.height, c.surfBiome, out var t)) return;
                t.gx = gx; t.gz = gz; t.prio = h2;
                buf[n++] = t;
                return;
            }
            if (c.surfBiome != GenBiome.Forest || plane < WET_PLANE || lib.variants[TreeGallery.SP_REDWOOD] == 0) return;
            if (roll * 16 >= (long)P_GROVE * pl * 256) return;
            int want = 3 + Span(Ores.Hash(seed, gx, 2, gz) & 0xFFFFu, 3), got = 0, start = n;
            for (int j = 0; j < 3 * want && got < want && n < cap; j++)
            {
                uint hj = Ores.Hash(seed, gx, 16 + j, gz);
                int dx = Span(hj & 0xFFFFu, 2 * GROVE_R) - GROVE_R, dz = Span(hj >> 16, 2 * GROVE_R) - GROVE_R;
                if (dx * dx + dz * dz > GROVE_R * GROVE_R) continue;
                int mx = (cx + dx) & ~7, mz = (cz + dz) & ~7;
                bool close = false;
                for (int k = start; k < n && !close; k++)
                {
                    int ex = buf[k].root.x - mx, ez = buf[k].root.z - mz;
                    close = ex * ex + ez * ez < GROVE_MIN * GROVE_MIN;
                }
                if (close) continue;
                var cm = ShapeField.Sample(in spec, in map, in o, mx + 0.5f, mz + 0.5f, 1);
                if (cm.surfBiome != GenBiome.Forest || !SiteOk(in spec, in cm)) continue;
                uint hv = Ores.Hash(seed, gx, 64 + j, gz);
                int pi = lib.first[TreeGallery.SP_REDWOOD] + Span(hv & 0xFFFFu, lib.variants[TreeGallery.SP_REDWOOD]);
                if (!Site(in spec, in map, in o, in lib, pi, (int)(hv >> 16) & 3, new int3(mx, cm.height & ~7, mz), cm.height, cm.surfBiome, out var t)) continue;
                t.gx = gx; t.gz = gz; t.prio = hv;
                buf[n++] = t; got++;
            }
        }

        // =====================================================================
        // The gather a chunk / shell / point runs
        // =====================================================================
        public static int4 NormalRange(int x0, int z0, int x1, int z1)
            => new int4((x0 - RANGE_N) >> N_SHIFT, (z0 - RANGE_N) >> N_SHIFT, (x1 + RANGE_N) >> N_SHIFT, (z1 + RANGE_N) >> N_SHIFT);
        public static int4 GiantRange(int x0, int z0, int x1, int z1)
            => new int4((x0 - RANGE_G) >> G_SHIFT, (z0 - RANGE_G) >> G_SHIFT, (x1 + RANGE_G) >> G_SHIFT, (z1 + RANGE_G) >> G_SHIFT);

        /// Task 213: the fine cells the dense gather reads (same voxel range as the normal grid).
        public static int4 FineRange(int x0, int z0, int x1, int z1)
            => new int4((x0 - RANGE_N) >> F_SHIFT, (z0 - RANGE_N) >> F_SHIFT, (x1 + RANGE_N) >> F_SHIFT, (z1 + RANGE_N) >> F_SHIFT);
        private static int Cells(int4 r) => (r.z - r.x + 1) * (r.w - r.y + 1);

        /// Candidate buffer Gather needs for the footprint (dense: + the fine cells).
        public static int MaxCandidates(int x0, int z0, int x1, int z1, bool dense = false)
        {
            int4 a = NormalRange(x0, z0, x1, z1), g = GiantRange(x0, z0, x1, z1);
            return Cells(a) + Cells(g) * GROVE_MAX + (dense ? Cells(FineRange(x0, z0, x1, z1)) : 0);
        }

        /// Task 213: the int scratch the dense gather's cell index maps need (0 in 210's mode).
        public static int MaxCells(int x0, int z0, int x1, int z1, bool dense)
            => dense ? Cells(NormalRange(x0, z0, x1, z1)) + Cells(FineRange(x0, z0, x1, z1)) : 0;

        /// Library trees Gather can WRITE for the footprint (the trees whose box can touch it; dense: + fine cells).
        public static int MaxTrees(int x0, int z0, int x1, int z1, bool dense = false)
        {
            int nx = ((x1 + REACH_N) >> N_SHIFT) - ((x0 - REACH_N) >> N_SHIFT) + 1;
            int nz = ((z1 + REACH_N) >> N_SHIFT) - ((z0 - REACH_N) >> N_SHIFT) + 1;
            int R = GROVE_R + REACH_G + 8;
            int gxn = ((x1 + R) >> G_SHIFT) - ((x0 - R) >> G_SHIFT) + 1, gzn = ((z1 + R) >> G_SHIFT) - ((z0 - R) >> G_SHIFT) + 1;
            int fine = 0;
            if (dense)
                fine = (((x1 + REACH_N) >> F_SHIFT) - ((x0 - REACH_N) >> F_SHIFT) + 1) * (((z1 + REACH_N) >> F_SHIFT) - ((z0 - REACH_N) >> F_SHIFT) + 1);
            return nx * nz + gxn * gzn * GROVE_MAX + fine;
        }

        /// Task 213: the dense gather's cell index maps (buf index or -1), for Suppress192's lookups.
        public struct LibGrid
        {
            public int* nIdx, fIdx;
            public int4 nr, fr;
            public int nG;        // giants at buf[0, nG)
            public byte dense;
        }

        private static bool Near(int3 r, int x0, int z0, int x1, int z1, int pad)
            => r.x >= x0 - pad && r.x <= x1 + pad && r.z >= z0 - pad && r.z <= z1 + pad;
        private static int D2(int3 a, int3 b) { int dx = a.x - b.x, dz = a.z - b.z; return dx * dx + dz * dz; }
        private static bool Before(in LibInst a, in LibInst b) => a.prio != b.prio ? a.prio > b.prio : (a.gz != b.gz ? a.gz < b.gz : a.gx < b.gx);

        /// Every library tree whose box touches fine columns [x0, x1] x [z0, z1], appended at
        /// outTrees[0..] in the canonical order (giant cells z then x, members in order; then
        /// normal cells z then x) as Tree entries (shape SHAPE_LIB | turn, prim0 = prototype,
        /// primCount 0). `buf` (>= MaxCandidates) keeps the surviving candidates for Suppress192;
        /// returns the trees written; nBuf = candidates in buf. top = max(top, highest voxel).
        public static int Gather(in TerrainSpec spec, in MapView map, in Offsets o, in TreeLib lib,
                                 int x0, int z0, int x1, int z1, LibInst* buf, int bufCap, out int nBuf,
                                 Tree* outTrees, int outCap, ref int top)
        {
            var grid = default(LibGrid);
            return Gather(in spec, in map, in o, in lib, x0, z0, x1, z1, buf, bufCap, out nBuf, null, 0, ref grid, outTrees, outCap, ref top);
        }

        /// The same; `cells` (>= MaxCells) is the dense mode's index scratch, `grid` describes it for Suppress192.
        public static int Gather(in TerrainSpec spec, in MapView map, in Offsets o, in TreeLib lib,
                                 int x0, int z0, int x1, int z1, LibInst* buf, int bufCap, out int nBuf,
                                 int* cells, int cellCap, ref LibGrid grid,
                                 Tree* outTrees, int outCap, ref int top)
        {
            nBuf = 0;
            grid = default;
            if (!spec.Trees2 || lib.ready == 0 || map.trees == null) return 0;
            if (spec.Trees2Dense && cells != null && cellCap >= MaxCells(x0, z0, x1, z1, true))
                return GatherDense(in spec, in map, in o, in lib, x0, z0, x1, z1, buf, bufCap, out nBuf, cells, ref grid, outTrees, outCap, ref top);
            if (spec.Trees2Dense) return 0;   // a caller without the scratch gets no dense trees (never 210's in their place)
            int n = 0;
            int4 g = GiantRange(x0, z0, x1, z1);
            for (int gz = g.y; gz <= g.w; gz++)
            for (int gx = g.x; gx <= g.z; gx++)
                GiantCell(in spec, in map, in o, in lib, gx, gz, buf, ref n, bufCap);
            int nG = n;
            for (int i = 0; i < nG; i++) buf[i].state = 2;   // giants always survive
            int4 a = NormalRange(x0, z0, x1, z1);
            for (int gz = a.y; gz <= a.w; gz++)
            for (int gx = a.x; gx <= a.z; gx++)
            {
                if (n >= bufCap) break;
                if (!NormalCell(in spec, in map, in o, in lib, gx, gz, out var t)) continue;
                bool hit = false;
                for (int k = 0; k < nG && !hit; k++) hit = D2(t.root, buf[k].root) < buf[k].keep2;
                if (hit) continue;
                buf[n++] = t;
            }
            // tier 1 (oaks): a higher-priority oak CANDIDATE's keep (valid within RANGE_N - KEEP_N)
            for (int i = nG; i < n; i++)
            {
                if (buf[i].tier != 1) continue;
                if (!Near(buf[i].root, x0, z0, x1, z1, REACH_N + 2 * KEEP_N)) { buf[i].state = 0; continue; }
                bool hit = false;
                for (int k = nG; k < n && !hit; k++)
                    hit = k != i && buf[k].tier == 1 && Before(in buf[k], in buf[i]) && D2(buf[i].root, buf[k].root) < buf[k].keep2;
                buf[i].state = hit ? (byte)0 : (byte)2;
            }
            // tier 2: dies inside a surviving oak's keep (candidates, within RANGE_N - 2 KEEP_N) ...
            for (int i = nG; i < n; i++)
            {
                if (buf[i].tier != 2) continue;
                if (!Near(buf[i].root, x0, z0, x1, z1, REACH_N + KEEP_N)) { buf[i].state = 0; continue; }
                bool hit = false;
                for (int k = nG; k < n && !hit; k++)
                    hit = buf[k].tier == 1 && buf[k].state == 2 && D2(buf[i].root, buf[k].root) < buf[k].keep2;
                buf[i].state = hit ? (byte)0 : (byte)1;
                buf[i].cand = buf[i].state;
            }
            // ... and inside a higher-priority tier-2 candidate's keep (within REACH_N)
            for (int i = nG; i < n; i++)
            {
                if (buf[i].tier != 2 || buf[i].state != 1) continue;
                if (!Near(buf[i].root, x0, z0, x1, z1, REACH_N)) { buf[i].state = 3; continue; }   // 3: a candidate only
                bool hit = false;
                for (int k = nG; k < n && !hit; k++)
                    hit = k != i && buf[k].tier == 2 && buf[k].cand != 0 && Before(in buf[k], in buf[i]) && D2(buf[i].root, buf[k].root) < buf[k].keep2;
                buf[i].state = hit ? (byte)0 : (byte)2;
            }
            nBuf = n;
            int w = 0;
            for (int i = 0; i < n && w < outCap; i++)
            {
                ref var t = ref buf[i];
                if (t.state != 2) continue;
                if (t.hi.x < x0 || t.lo.x > x1 || t.hi.z < z0 || t.lo.z > z1) continue;
                outTrees[w++] = new Tree
                {
                    root = t.root, lo = t.lo, hi = t.hi, prim0 = t.proto, primCount = 0,
                    cellX = t.gx, cellZ = t.gz, biome = t.biome, shape = (byte)(SHAPE_LIB | t.rot),
                };
                top = math.max(top, t.hi.y);
            }
            return w;
        }

        private static int Slot(int* idx, int4 r, int gx, int gz)
            => gx < r.x || gx > r.z || gz < r.y || gz > r.w ? -1 : idx[(gz - r.y) * (r.z - r.x + 1) + (gx - r.x)];

        /// Task 213: the dense gather. Same tiers, rules and Near ranges as Gather's 210 path; oaks from
        /// the normal grid, tier 2 from the fine grid, rivals found through the cell index maps
        /// (every keep <= KEEP_N = 96: an oak's rivals in the 3 x 3 normal cells, a tier-2 tree's in 5 x 5 fine).
        private static int GatherDense(in TerrainSpec spec, in MapView map, in Offsets o, in TreeLib lib,
                                       int x0, int z0, int x1, int z1, LibInst* buf, int bufCap, out int nBuf,
                                       int* cells, ref LibGrid grid, Tree* outTrees, int outCap, ref int top)
        {
            int n = 0;
            int4 g = GiantRange(x0, z0, x1, z1);
            for (int gz = g.y; gz <= g.w; gz++)
            for (int gx = g.x; gx <= g.z; gx++)
                GiantCell(in spec, in map, in o, in lib, gx, gz, buf, ref n, bufCap);
            int nG = n;
            for (int i = 0; i < nG; i++) buf[i].state = 2;
            int4 a = NormalRange(x0, z0, x1, z1), f = FineRange(x0, z0, x1, z1);
            int* nIdx = cells, fIdx = cells + Cells(a);
            int nCells = Cells(a) + Cells(f);
            for (int i = 0; i < nCells; i++) cells[i] = -1;
            grid = new LibGrid { nIdx = nIdx, fIdx = fIdx, nr = a, fr = f, nG = nG, dense = 1 };
            int aw = a.z - a.x + 1, fw = f.z - f.x + 1;
            for (int pass = 0; pass < 2; pass++)
            {
                int4 r = pass == 0 ? a : f; int* idx = pass == 0 ? nIdx : fIdx; int w0 = pass == 0 ? aw : fw;
                for (int gz = r.y; gz <= r.w; gz++)
                for (int gx = r.x; gx <= r.z; gx++)
                {
                    if (n >= bufCap) break;
                    if (!GridCell(in spec, in map, in o, in lib, gx, gz, pass + 1, out var t)) continue;
                    bool hit = false;
                    for (int k = 0; k < nG && !hit; k++) hit = D2(t.root, buf[k].root) < buf[k].keep2;
                    if (hit) continue;
                    idx[(gz - r.y) * w0 + (gx - r.x)] = n;
                    buf[n++] = t;
                }
            }
            // tier 1 (oaks, normal grid): a higher-priority oak CANDIDATE's keep
            for (int i = nG; i < n; i++)
            {
                if (buf[i].tier != 1) continue;
                if (!Near(buf[i].root, x0, z0, x1, z1, REACH_N + 2 * KEEP_N)) { buf[i].state = 0; continue; }
                bool hit = false;
                int cx = buf[i].root.x >> N_SHIFT, cz = buf[i].root.z >> N_SHIFT;
                for (int dz = -1; dz <= 1 && !hit; dz++)
                for (int dx = -1; dx <= 1 && !hit; dx++)
                {
                    int k = Slot(nIdx, a, cx + dx, cz + dz);
                    hit = k >= 0 && k != i && buf[k].tier == 1 && Before(in buf[k], in buf[i]) && D2(buf[i].root, buf[k].root) < buf[k].keep2;
                }
                buf[i].state = hit ? (byte)0 : (byte)2;
            }
            // tier 2 (fine grid): dies inside a surviving oak's keep ...
            for (int i = nG; i < n; i++)
            {
                if (buf[i].tier != 2) continue;
                if (!Near(buf[i].root, x0, z0, x1, z1, REACH_N + KEEP_N)) { buf[i].state = 0; buf[i].cand = 0; continue; }
                bool hit = false;
                int cx = buf[i].root.x >> N_SHIFT, cz = buf[i].root.z >> N_SHIFT;
                for (int dz = -1; dz <= 1 && !hit; dz++)
                for (int dx = -1; dx <= 1 && !hit; dx++)
                {
                    int k = Slot(nIdx, a, cx + dx, cz + dz);
                    hit = k >= 0 && buf[k].tier == 1 && buf[k].state == 2 && D2(buf[i].root, buf[k].root) < buf[k].keep2;
                }
                buf[i].state = hit ? (byte)0 : (byte)1;
                buf[i].cand = buf[i].state;
            }
            // ... and inside a higher-priority tier-2 candidate's keep
            for (int i = nG; i < n; i++)
            {
                if (buf[i].tier != 2 || buf[i].state != 1) continue;
                if (!Near(buf[i].root, x0, z0, x1, z1, REACH_N)) { buf[i].state = 3; continue; }
                bool hit = false;
                int cx = buf[i].root.x >> F_SHIFT, cz = buf[i].root.z >> F_SHIFT;
                for (int dz = -2; dz <= 2 && !hit; dz++)
                for (int dx = -2; dx <= 2 && !hit; dx++)
                {
                    int k = Slot(fIdx, f, cx + dx, cz + dz);
                    hit = k >= 0 && k != i && buf[k].tier == 2 && buf[k].cand != 0 && Before(in buf[k], in buf[i]) && D2(buf[i].root, buf[k].root) < buf[k].keep2;
                }
                buf[i].state = hit ? (byte)0 : (byte)2;
            }
            nBuf = n;
            int w = 0;
            for (int i = 0; i < n && w < outCap; i++)
            {
                ref var t = ref buf[i];
                if (t.state != 2) continue;
                if (t.hi.x < x0 || t.lo.x > x1 || t.hi.z < z0 || t.lo.z > z1) continue;
                outTrees[w++] = new Tree
                {
                    root = t.root, lo = t.lo, hi = t.hi, prim0 = t.proto, primCount = 0,
                    cellX = t.gx, cellZ = t.gz, biome = t.biome, shape = (byte)(SHAPE_LIB | t.rot),
                };
                top = math.max(top, t.hi.y);
            }
            return w;
        }

        /// 192's gathered trees [0, nt) whose root lies inside a surviving library tree's keep
        /// radius are dropped: primCount 0 and their primitives emptied (lo > hi), so both
        /// OverlayTrees and MaterialAt skip them. Returns the trees dropped.
        public static int Suppress192(LibInst* buf, int nBuf, Tree* trees, int nt, TreePrim* prims)
        {
            var grid = default(LibGrid);
            return Suppress192(buf, nBuf, in grid, trees, nt, prims);
        }

        /// The same; in dense mode (grid.dense) the survivors are found through the cell index maps
        /// (giants by list, oaks in the 3 x 3 normal cells, tier 2 in the 5 x 5 fine cells round the root).
        public static int Suppress192(LibInst* buf, int nBuf, in LibGrid grid, Tree* trees, int nt, TreePrim* prims)
        {
            int dropped = 0;
            for (int i = 0; i < nt; i++)
            {
                if ((trees[i].shape & SHAPE_LIB) != 0 || trees[i].primCount == 0) continue;
                bool hit = false;
                if (grid.dense == 0)
                    for (int k = 0; k < nBuf && !hit; k++)
                        hit = buf[k].state == 2 && D2(trees[i].root, buf[k].root) < buf[k].keep2;
                else
                {
                    int3 r = trees[i].root;
                    for (int k = 0; k < grid.nG && !hit; k++) hit = D2(r, buf[k].root) < buf[k].keep2;
                    int cx = r.x >> N_SHIFT, cz = r.z >> N_SHIFT;
                    for (int dz = -1; dz <= 1 && !hit; dz++)
                    for (int dx = -1; dx <= 1 && !hit; dx++)
                    {
                        int k = Slot(grid.nIdx, grid.nr, cx + dx, cz + dz);
                        hit = k >= 0 && buf[k].state == 2 && D2(r, buf[k].root) < buf[k].keep2;
                    }
                    cx = r.x >> F_SHIFT; cz = r.z >> F_SHIFT;
                    for (int dz = -2; dz <= 2 && !hit; dz++)
                    for (int dx = -2; dx <= 2 && !hit; dx++)
                    {
                        int k = Slot(grid.fIdx, grid.fr, cx + dx, cz + dz);
                        hit = k >= 0 && buf[k].state == 2 && D2(r, buf[k].root) < buf[k].keep2;
                    }
                }
                if (!hit) continue;
                for (int p = trees[i].prim0; p < trees[i].prim0 + trees[i].primCount; p++) { prims[p].lo = int.MaxValue; prims[p].hi = int.MinValue; }
                trees[i].primCount = 0;
                dropped++;
            }
            return dropped;
        }
    }
}
