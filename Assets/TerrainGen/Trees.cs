// Assets/TerrainGen/Trees.cs
//
// Task 180 (placement, owner 2026-11-06: "trees are voxels, so we can interact
// with them"; batch-2 answers: Terraria-sized 8-20 m, a procedural FAMILY per
// biome, numbers from an integer hash of the position). A FIRST, REVERSIBLE TEST
// behind `-trees 1` (TerrainSpec.treesOn; default off).
//
// WHERE TREES LIVE. A tree is a pure function of (seed, its 6.4 m tree cell):
// Build() decides from data the generator already has -- the per-map-cell
// density plane (TreeTable.DensityPlane: the world record's landform archetype x
// movement band, zero on sea, route-corridor and signature-feature cells), the
// trunk column's own ShapeField sample at LOD-0 footprint (biome = surfBiome,
// the cover slot + patch variant, water, stamps) and a few height-only samples
// (footprint slope, canopy clearance). No seed constant, no terrain-specific
// number: one rule per biome SLOT (DENSITY, SLOT_MUL). Every consumer gathers the
// trees of the cells round its footprint and asks Contains() per voxel:
//   LOD 0       ColumnJob gathers, VoxelJob overlays (TerrainGenChunkSource)
//   shells 1-5  the same at each coarse voxel's sample point (exact: the shell
//               voxel IS the 0.1 m voxel at that point), shells > MAX_SHELL none
//   point query PointVoxel (FarQuery) -- the chunk's byte
//   far band    none: it is visible only past the last shell (>= 4.4 km on
//               High), where a 12.8 m texel is wider than any tree.
// The CPU stays the sole terrain writer: trees are GENERATED voxels, written by
// the chunk pass like every other generated voxel (CLAUDE.md invariants).
//
// RULE AT A VOXEL. Trees overwrite AIR only (never rock, soil, water): a tree is
// never inside rock and never in water. Precedence is fixed so every consumer
// agrees: first every TRUNK primitive (trees in cell order z then x), then every
// LEAF primitive; the first one containing the voxel wins.
//
// CHOPPING (designed for, not built): trunk and leaf voxels have their own
// materials (GenMat.IsTreeTrunk / IsTreeLeaf), and Build() rebuilds the whole
// tree from its cell, so "cut any trunk voxel -> drop the tree's wood, remove it
// top-down" needs only the cell of the cut voxel's nearest root.
//
// COORDINATES: the tree grid is integer (>> CELL_SHIFT); primitives are stored
// RELATIVE to the tree root (int3), so the float shape tests only ever see small
// local offsets.
using Unity.Mathematics;

namespace TerrainGen
{
    /// One solid primitive of a tree, relative to its root.
    public struct TreePrim
    {
        public int3 root;       // the tree's root: trunk column (x, z), its ground Y
        public int3 lo, hi;     // inclusive AABB, absolute fine voxels (edge noise included)
        public float3 a, b;     // CAPSULE ends a, b | BLOB centre a, radii b | CONE base centre a, b = (radius, height, -)
        public float r0, r1;    // CAPSULE radii at a and b | BLOB / CONE: r0 = edge-noise amplitude (fraction)
        public uint noise;      // edge-noise seed
        public byte kind, mat, leaf;
    }

    /// A tree: its primitives [prim0, prim0 + primCount) in the gather buffer.
    public struct Tree
    {
        public int3 root, lo, hi;
        public int prim0, primCount;
        public int cellX, cellZ;
        public byte biome, shape;
    }

    public static unsafe class Trees
    {
        // ---- the tree grid ----
        public const int CELL_SHIFT = 6, CELL = 1 << CELL_SHIFT;   // 6.4 m cells, at most one tree each
        public const int JITTER_MARGIN = 8, JITTER_SPAN = 48;       // root at [8, 56) inside its cell
        /// No tree voxel lies further than this from its root column (x or z); Build rejects one that would.
        public const int REACH = 63;
        /// ... nor more than this above its root's ground (MaxContentY's STAMP_RISE_BOUND covers it).
        public const int MAX_HEIGHT = 240;
        /// Trunks reach this far below the lowest ground round them (they only fill air).
        public const int ROOT_DEPTH = 6;
        /// Task 192: a skeleton tree (trunk + buttress + 5 limbs + 10 twigs + 10 leaf
        /// clumps + a top clump) takes at most this many (task 180's families: 14).
        public const int MAX_PRIMS = 28;
        /// Shells above this draw no trees (3.2 m voxels; beyond 2.2 km on High).
        public const int MAX_SHELL = 5;

        public const byte CAPSULE = 0, BLOB = 1, CONE = 2;
        private const uint SALT = 0x7EE5EEDu;

        // ---- density: ONE rule per biome slot (no seed constant) ----
        /// Trees per tree cell (per mille) by GenBiome: forest, desert (cactus), snow, jungle, savanna, badlands.
        private static readonly short[] DENSITY = { 800, 60, 300, 950, 110, 0 };
        private const int DENSITY_MAX = 950;
        /// Cover slot multiplier (1/16): flat, steep, hollow, high, shore.
        private static readonly byte[] SLOT_MUL = { 16, 6, 10, 8, 0 };
        /// A cover PATCH VARIANT reads as a clearing: x 10/16.
        private const int VARIANT_MUL = 10;

        /// Task 210: Build's cover multiplier (1/256): the slot's SLOT_MUL x 16, a patch variant's x VARIANT_MUL.
        public static int SlotMul(byte cover)
        {
            int slot = Cover.Slot(cover);
            int mul = slot < SLOT_MUL.Length ? SLOT_MUL[slot] : 0;
            return Cover.IsVariant(cover) ? mul * VARIANT_MUL : mul * 16;
        }

        /// Per-mille density of biome `b` (0 for unknown / badlands).
        public static int Density(byte b) => b < GenBiome.Count ? DENSITY[b] : 0;

        // ---- task 192: the look (leaf fill, forms) ----
        /// The leaf FILL target (percent of the crown envelope) when the spec leaves it 0.
        public const int DEFAULT_LEAF_FILL = 45;
        public static int LeafFill(in TerrainSpec spec) => spec.treeLeafFill == 0 ? DEFAULT_LEAF_FILL : math.clamp((int)spec.treeLeafFill, 10, 90);
        /// Tree.shape under 192's style: the FORM (task 180's style: the family 0-2).
        public const byte FORM_BROAD = 0, FORM_CONIFER = 1, FORM_PALM = 2, FORM_CACTUS = 3, FORM_COUNT = 4;

        // ---- materials ----
        public static byte Wood(byte biome)
        {
            switch (biome)
            {
                case GenBiome.Jungle: return GenMat.JungleWood;
                case GenBiome.Savanna: return GenMat.AcaciaWood;
                case GenBiome.Snow: return GenMat.PineWood;
                case GenBiome.Desert: return GenMat.Cactus;
                default: return GenMat.ForestWood;
            }
        }
        public static byte Leaf(byte biome)
        {
            switch (biome)
            {
                case GenBiome.Jungle: return GenMat.JungleLeaves;
                case GenBiome.Savanna: return GenMat.SavannaLeaves;
                case GenBiome.Snow: return GenMat.PineNeedles;
                default: return GenMat.ForestLeaves;
            }
        }

        // ---- the gather a chunk / shell / point runs ----

        /// Tree cells whose tree can touch fine columns [x0, x1] x [z0, z1].
        public static int4 CellRange(int x0, int z0, int x1, int z1)
            => new int4((x0 - REACH) >> CELL_SHIFT, (z0 - REACH) >> CELL_SHIFT, (x1 + REACH) >> CELL_SHIFT, (z1 + REACH) >> CELL_SHIFT);

        /// Upper bound on the trees Gather can return for that footprint.
        public static int MaxTrees(int x0, int z0, int x1, int z1)
        {
            int4 r = CellRange(x0, z0, x1, z1);
            return (r.z - r.x + 1) * (r.w - r.y + 1);
        }

        /// Every tree touching fine columns [x0, x1] x [z0, z1], in cell order
        /// (z, then x), its primitives appended to `prims`. Returns the tree
        /// count; `top` = max(top, highest tree voxel). Capacities must be at
        /// least MaxTrees and MaxTrees * MAX_PRIMS.
        public static int Gather(in TerrainSpec spec, in MapView map, in Offsets o, int x0, int z0, int x1, int z1,
                                 Tree* trees, int treeCap, TreePrim* prims, int primCap, out int primCount, ref int top)
        {
            primCount = 0;
            if (!spec.Trees || map.trees == null) return 0;
            int4 r = CellRange(x0, z0, x1, z1);
            int n = 0;
            for (int cz = r.y; cz <= r.w; cz++)
            for (int cx = r.x; cx <= r.z; cx++)
            {
                if (n >= treeCap || primCount + MAX_PRIMS > primCap) return n;
                int start = primCount;
                if (!Build(in spec, in map, in o, cx, cz, prims, ref primCount, out Tree t)) continue;
                if (t.hi.x < x0 || t.lo.x > x1 || t.hi.z < z0 || t.lo.z > z1) { primCount = start; continue; }
                trees[n++] = t;
                top = math.max(top, t.hi.y);
            }
            return n;
        }

        /// The tree material of fine voxel v from gathered primitives (trunks
        /// first, then leaves; the first containing one wins), or Air.
        public static byte MaterialAt(TreePrim* prims, int count, int3 v)
        {
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < count; i++)
                {
                    if (prims[i].leaf != pass) continue;
                    if (math.any(v < prims[i].lo) || math.any(v > prims[i].hi)) continue;
                    if (Contains(in prims[i], v)) return prims[i].mat;
                }
            return GenMat.Air;
        }

        // ---- one cell's tree ----

        /// The tree of tree cell (cx, cz), or false. Appends its primitives at
        /// prims[n..] (n advanced; restored when there is no tree).
        public static bool Build(in TerrainSpec spec, in MapView map, in Offsets o, int cx, int cz,
                                 TreePrim* prims, ref int n, out Tree t)
        {
            t = default;
            if (map.trees == null) return false;
            uint seed = spec.seed ^ SALT;
            uint h0 = Ores.Hash(seed, cx, 0, cz);
            int tx = (cx << CELL_SHIFT) + JITTER_MARGIN + Span(h0 & 0xFFFFu, JITTER_SPAN);
            int tz = (cz << CELL_SHIFT) + JITTER_MARGIN + Span(h0 >> 16, JITTER_SPAN);
            if (tx < 0 || tz < 0 || tx >= spec.worldSizeVoxels || tz >= spec.worldSizeVoxels) return false;
            int forced = spec.treesOn - 2;   // task 188: >= 0 = the test-only ADVERSARIAL cover (TerrainSpec.treesOn)
            int plane = map.trees[Ores.CellIndex(tx, tz, spec.mapCells, spec.mapCellVoxels)];
            if (plane == 0 && forced < 0) return false;
            int roll = (int)(((ulong)Ores.Hash(seed, cx, 1, cz) * 1000u * 256u) >> 32);   // [0, 256000): per mille x 256
            // the cheap reject before any sample: density can never exceed DENSITY_MAX x plane / 16
            if (roll >= DENSITY_MAX * plane * 16 && forced < 0) return false;

            var c = ShapeField.Sample(in spec, in map, in o, tx + 0.5f, tz + 0.5f, 1);
            byte biome = c.surfBiome;
            int slot = Cover.Slot(c.cover);
            int mul = slot < SLOT_MUL.Length ? SLOT_MUL[slot] : 0;
            if (Cover.IsVariant(c.cover)) mul = mul * VARIANT_MUL;
            else mul *= 16;
            if (spec.Trees2 && forced < 0)
            {   // task 210: the library grows this biome's broadleaf / conifer trees; 192 keeps the other forms
                int k2 = Trees2.Keep192(biome);
                if (k2 == 0) return false;
                mul = mul * k2 >> 4;
            }
            // P = Density/1000 x plane/16 x mul/256; roll/256000 < P  <=>  roll x 16 < Density x plane x mul (integers)
            if (forced >= 0) biome = (byte)forced;   // every cell that passes the physical rules below gets this family
            else if ((long)roll * 16 >= (long)Density(biome) * plane * mul) return false;
            if (c.height <= spec.seaLevelY) return false;
            if (c.water != ColumnSample.NO_WATER_Y && c.water >= c.height) return false;
            if (c.stampMat != 0 || c.stamp3D || c.carvesTop || c.lipLo != 0
                || c.open1Hi > c.open1Lo || c.open2Hi > c.open2Lo || c.open3Hi > c.open3Lo) return false;

            var root = new int3(tx, c.height, tz);
            int start = n;
            var rng = new Rng { seed = seed, cx = cx, cz = cz, k = 16 };
            bool style180 = spec.treeStyle180 != 0;
            byte shape;
            float trunkR;
            var P = default(Look);
            if (style180)
            {   // task 180's families, byte for byte (-treestyle 0)
                shape = (byte)Span(Ores.Hash(seed, cx, 2, cz) & 0xFFFFu, 3);
                Emit(biome, shape, root, ref rng, prims, ref n);
                if (n == start) return false;
                trunkR = prims[start].kind == CAPSULE ? math.max(prims[start].r0, prims[start].r1) : prims[start].b.x;
            }
            else
            {   // task 192: this tree's own numbers, drawn inside its biome x slot ranges
                P = Pick(biome, slot, LeafFill(in spec), ref rng);
                if (P.form == 255) return false;
                if (spec.Trees2 && forced < 0 && biome == GenBiome.Savanna && !P.baobab) return false;   // task 210: acacias come from the library
                shape = P.form;
                trunkR = P.footR;
            }

            // FOOTPRINT: no tree on a sheer foot (> 45 deg across the trunk + 2 voxels).
            int d = (int)math.ceil(trunkR) + 2;
            int hE = HeightAt(in spec, in map, in o, tx + d, tz), hW = HeightAt(in spec, in map, in o, tx - d, tz);
            int hN = HeightAt(in spec, in map, in o, tx, tz + d), hS = HeightAt(in spec, in map, in o, tx, tz - d);
            int dmax = math.max(math.max(math.abs(hE - c.height), math.abs(hW - c.height)),
                                math.max(math.abs(hN - c.height), math.abs(hS - c.height)));
            if (dmax > d) { n = start; return false; }
            int minH = math.min(math.min(math.min(hE, hW), math.min(hN, hS)), c.height);
            if (!style180)
            {   // the skeleton, leaning out over the downhill side (the foot's own gradient)
                EmitLook(in P, root, new float2(hW - hE, hS - hN), 2 * d, ref rng, prims, ref n);
                if (n == start) return false;
            }
            // ROOTS: prim 0 is always the trunk; it reaches ROOT_DEPTH under the lowest ground round it.
            prims[start].a.y = minH - ROOT_DEPTH - root.y;
            Bounds(ref prims[start]);

            // CANOPY CLEARANCE: the ground at 0.7 x the leaves' reach must stay under the leaves' bottom.
            int leafReach = 0, leafBottom = int.MaxValue;
            int3 lo = prims[start].lo, hi = prims[start].hi;
            for (int i = start; i < n; i++)
            {
                lo = math.min(lo, prims[i].lo); hi = math.max(hi, prims[i].hi);
                if (prims[i].leaf == 0) continue;
                leafBottom = math.min(leafBottom, prims[i].lo.y);
                leafReach = math.max(leafReach, math.cmax(math.abs(new int4(prims[i].lo.xz - root.xz, prims[i].hi.xz - root.xz))));
            }
            if (leafBottom != int.MaxValue)
            {
                int rc = (leafReach * 7) >> 3;
                int g = math.max(math.max(HeightAt(in spec, in map, in o, tx + rc, tz), HeightAt(in spec, in map, in o, tx - rc, tz)),
                                 math.max(HeightAt(in spec, in map, in o, tx, tz + rc), HeightAt(in spec, in map, in o, tx, tz - rc)));
                if (g >= leafBottom - 2) { n = start; return false; }
            }
            if (math.cmax(math.abs(new int4(lo.xz - root.xz, hi.xz - root.xz))) > REACH || hi.y - root.y > MAX_HEIGHT)
            { n = start; return false; }

            t = new Tree { root = root, lo = lo, hi = hi, prim0 = start, primCount = n - start, cellX = cx, cellZ = cz, biome = biome, shape = shape };
            return true;
        }

        private static int HeightAt(in TerrainSpec spec, in MapView map, in Offsets o, int x, int z)
            => ShapeField.Sample(in spec, in map, in o, x + 0.5f, z + 0.5f, 1, false).height;

        /// floor(u16 x span / 65536): an integer uniform pick in [0, span) from 16 hash bits.
        private static int Span(uint u16, int span) => (int)((u16 * (uint)span) >> 16);

        /// A per-tree stream of uniform floats in [0, 1) from the cell's integer hash.
        private struct Rng
        {
            public uint seed; public int cx, cz, k;
            public float Next() => (Ores.Hash(seed, cx, k++, cz) & 0xFFFFFFu) / 16777216f;
            public float Range(float a, float b) => a + (b - a) * Next();
            public float Angle() => Next() * 6.2831853f;
        }

        // ---- the families: 3 shapes per biome, every number from the cell's hash ----

        private static void Emit(byte biome, byte shape, int3 root, ref Rng r, TreePrim* p, ref int n)
        {
            byte W = Wood(biome), L = Leaf(biome);
            float th = r.Angle();
            float2 dir = new float2(math.cos(th), math.sin(th));
            switch (biome)
            {
                case GenBiome.Forest:
                    if (shape == 0)
                    {   // OAK: stout trunk, a round crown and three branch clumps
                        float H = r.Range(70, 110), rt = r.Range(3.5f, 5f);
                        float2 lean = dir * r.Range(0, 6);
                        Capsule(p, ref n, root, new float3(0, 0, 0), new float3(lean.x, H, lean.y), rt, rt * 0.6f, W);
                        Blob(p, ref n, root, new float3(lean.x, H + 8, lean.y), new float3(r.Range(30, 42), r.Range(22, 30), r.Range(30, 42)), 0.15f, L, ref r);
                        for (int i = 0; i < 3; i++)
                        {
                            float a = th + i * 2.0944f + r.Range(-0.4f, 0.4f);
                            float2 d = new float2(math.cos(a), math.sin(a));
                            float3 s = new float3(lean.x * 0.6f, H * 0.6f, lean.y * 0.6f);
                            float len = r.Range(16, 24);
                            float3 e = s + new float3(d.x * len, r.Range(20, 30), d.y * len);
                            Capsule(p, ref n, root, s, e, 2.2f, 1.5f, W);
                            Blob(p, ref n, root, e, new float3(r.Range(16, 22), r.Range(12, 16), r.Range(16, 22)), 0.18f, L, ref r);
                        }
                    }
                    else if (shape == 1)
                    {   // POPLAR: tall and slim, a tall narrow crown
                        float H = r.Range(110, 150), rt = r.Range(2.5f, 3.5f);
                        Capsule(p, ref n, root, new float3(0, 0, 0), new float3(0, H + 30, 0), rt, rt * 0.5f, W);
                        Blob(p, ref n, root, new float3(0, H + 10, 0), new float3(r.Range(17, 23), r.Range(42, 56), r.Range(17, 23)), 0.15f, L, ref r);
                        Blob(p, ref n, root, new float3(dir.x * 9, H - 22, dir.y * 9), new float3(14, r.Range(20, 26), 14), 0.18f, L, ref r);
                        Blob(p, ref n, root, new float3(-dir.x * 9, H - 10, -dir.y * 9), new float3(13, r.Range(18, 24), 13), 0.18f, L, ref r);
                    }
                    else
                    {   // BUSHY: a short trunk forking in two, three clumps
                        float H = r.Range(50, 70), rt = r.Range(4f, 5.5f);
                        Capsule(p, ref n, root, new float3(0, 0, 0), new float3(0, H * 0.7f, 0), rt, rt * 0.8f, W);
                        for (int i = 0; i < 2; i++)
                        {
                            float a = th + i * 3.14159f + r.Range(-0.5f, 0.5f);
                            float2 d = new float2(math.cos(a), math.sin(a));
                            float len = r.Range(14, 22);
                            float3 s = new float3(0, H * 0.7f, 0), e = new float3(d.x * len, H * 0.7f + r.Range(24, 34), d.y * len);
                            Capsule(p, ref n, root, s, e, 3f, 2f, W);
                            Blob(p, ref n, root, e, new float3(r.Range(22, 28), r.Range(17, 22), r.Range(22, 28)), 0.18f, L, ref r);
                        }
                        Blob(p, ref n, root, new float3(0, H + 16, 0), new float3(r.Range(20, 25), r.Range(16, 20), r.Range(20, 25)), 0.18f, L, ref r);
                    }
                    break;

                case GenBiome.Jungle:
                    if (shape == 0)
                    {   // EMERGENT: very tall, buttressed, a flat umbrella crown
                        float H = r.Range(140, 180), rt = r.Range(4.5f, 6f);
                        Capsule(p, ref n, root, new float3(0, 0, 0), new float3(0, H, 0), rt, 3f, W);
                        Capsule(p, ref n, root, new float3(0, 0, 0), new float3(0, 18, 0), rt + r.Range(4, 6), rt, W);   // buttress
                        Blob(p, ref n, root, new float3(0, H + 4, 0), new float3(r.Range(40, 50), r.Range(12, 16), r.Range(40, 50)), 0.12f, L, ref r);
                        for (int i = 0; i < 3; i++)
                        {
                            float a = th + i * 2.0944f + r.Range(-0.4f, 0.4f);
                            float2 d = new float2(math.cos(a), math.sin(a));
                            float3 s = new float3(0, H * 0.8f, 0), e = new float3(d.x * 20, H * 0.8f + 16, d.y * 20);
                            Capsule(p, ref n, root, s, e, 2.5f, 1.8f, W);
                            Blob(p, ref n, root, e, new float3(r.Range(17, 22), r.Range(8, 10), r.Range(17, 22)), 0.15f, L, ref r);
                        }
                    }
                    else if (shape == 1)
                    {   // PALM: a curving trunk (three segments), a crown of drooping fronds
                        float H = r.Range(90, 130), bend = r.Range(10, 22);
                        float3 p0 = new float3(0, 0, 0);
                        float3 p1 = new float3(dir.x * bend * 0.11f, H * 0.34f, dir.y * bend * 0.11f);
                        float3 p2 = new float3(dir.x * bend * 0.44f, H * 0.67f, dir.y * bend * 0.44f);
                        float3 p3 = new float3(dir.x * bend, H, dir.y * bend);
                        Capsule(p, ref n, root, p0, p1, 3.2f, 3f, W);
                        Capsule(p, ref n, root, p1, p2, 3f, 2.7f, W);
                        Capsule(p, ref n, root, p2, p3, 2.7f, 2.4f, W);
                        Blob(p, ref n, root, p3 + new float3(0, 2, 0), new float3(9, 7, 9), 0.1f, L, ref r);
                        for (int i = 0; i < 5; i++)
                        {   // a frond: a long flat leaf arching out and down from the crown
                            float a = th + i * 1.2566f + r.Range(-0.25f, 0.25f);
                            float2 d = new float2(math.cos(a), math.sin(a));
                            float out_ = r.Range(15, 20);
                            Blob(p, ref n, root, p3 + new float3(d.x * out_ * 0.5f, 0, d.y * out_ * 0.5f), new float3(12, 4.5f, 12), 0.1f, L, ref r);
                            Blob(p, ref n, root, p3 + new float3(d.x * out_, -r.Range(3, 4.5f), d.y * out_), new float3(10, 4.5f, 10), 0.1f, L, ref r);
                        }
                    }
                    else
                    {   // DENSE: a broad round crown with two side lobes
                        float H = r.Range(85, 110), rt = r.Range(4f, 5f);
                        Capsule(p, ref n, root, new float3(0, 0, 0), new float3(0, H, 0), rt, rt * 0.7f, W);
                        Blob(p, ref n, root, new float3(0, H + 10, 0), new float3(r.Range(34, 42), r.Range(28, 34), r.Range(34, 42)), 0.15f, L, ref r);
                        Blob(p, ref n, root, new float3(dir.x * 20, H - 10, dir.y * 20), new float3(22, 18, 22), 0.18f, L, ref r);
                        Blob(p, ref n, root, new float3(-dir.x * 20, H - 4, -dir.y * 20), new float3(20, 16, 20), 0.18f, L, ref r);
                    }
                    break;

                case GenBiome.Savanna:
                    if (shape == 0)
                    {   // UMBRELLA ACACIA: a short trunk forking, one wide flat crown
                        float H = r.Range(55, 75), rt = r.Range(3.2f, 4.2f);
                        Capsule(p, ref n, root, new float3(0, 0, 0), new float3(0, H * 0.6f, 0), rt, rt * 0.85f, W);
                        int forks = 2 + (r.Next() < 0.5f ? 1 : 0);
                        for (int i = 0; i < forks; i++)
                        {
                            float a = th + i * 6.2832f / forks + r.Range(-0.3f, 0.3f);
                            float2 d = new float2(math.cos(a), math.sin(a));
                            float len = r.Range(16, 24);
                            float3 e = new float3(d.x * len, H, d.y * len);
                            Capsule(p, ref n, root, new float3(0, H * 0.6f, 0), e, 2.5f, 1.8f, W);
                            Blob(p, ref n, root, e + new float3(0, 4, 0), new float3(16, 7, 16), 0.12f, L, ref r);
                        }
                        Blob(p, ref n, root, new float3(0, H + 12, 0), new float3(r.Range(42, 50), r.Range(9, 12), r.Range(42, 50)), 0.12f, L, ref r);
                    }
                    else if (shape == 1)
                    {   // BAOBAB: a fat bottle trunk, a small crown of stubby branches
                        float H = r.Range(55, 75), rt = r.Range(9f, 12f);
                        Capsule(p, ref n, root, new float3(0, 0, 0), new float3(0, H, 0), rt, rt * 0.6f, W);
                        for (int i = 0; i < 5; i++)
                        {
                            float a = th + i * 1.2566f + r.Range(-0.3f, 0.3f);
                            float2 d = new float2(math.cos(a), math.sin(a));
                            float len = r.Range(10, 16);
                            float3 e = new float3(d.x * len, H + r.Range(14, 20), d.y * len);
                            Capsule(p, ref n, root, new float3(0, H - 4, 0), e, 2.6f, 1.6f, W);
                            Blob(p, ref n, root, e, new float3(r.Range(9, 12), r.Range(7, 9), r.Range(9, 12)), 0.2f, L, ref r);
                        }
                    }
                    else
                    {   // TALL ACACIA: two flat tiers on a leaning trunk
                        float H = r.Range(80, 100), rt = r.Range(3f, 4f);
                        float3 top = new float3(dir.x * 12, H, dir.y * 12);
                        Capsule(p, ref n, root, new float3(0, 0, 0), top, rt, rt * 0.6f, W);
                        Blob(p, ref n, root, top + new float3(0, 8, 0), new float3(r.Range(32, 38), r.Range(7, 9), r.Range(32, 38)), 0.12f, L, ref r);
                        float3 s = new float3(dir.x * 7, H * 0.6f, dir.y * 7), e = new float3(-dir.x * 14, H * 0.8f, -dir.y * 14);
                        Capsule(p, ref n, root, s, e, 2.2f, 1.6f, W);
                        Blob(p, ref n, root, e + new float3(0, 6, 0), new float3(r.Range(22, 28), r.Range(6, 8), r.Range(22, 28)), 0.12f, L, ref r);
                    }
                    break;

                case GenBiome.Snow:
                    if (shape == 0)
                    {   // SPRUCE: four stacked cones down to a fifth of the height
                        float H = r.Range(110, 160), rt = r.Range(3f, 4f), R0 = r.Range(28, 36);
                        Capsule(p, ref n, root, new float3(0, 0, 0), new float3(0, H, 0), rt, 1.5f, W);
                        for (int i = 0; i < 4; i++)
                            Cone(p, ref n, root, new float3(0, H * (0.18f + 0.2f * i), 0), R0 * (1f - 0.18f * i), H * 0.42f * (1f - 0.1f * i), 0.1f, L, ref r);
                    }
                    else if (shape == 1)
                    {   // TALL PINE: a long bare trunk, three narrow tiers at the top
                        float H = r.Range(140, 180), R0 = r.Range(22, 28);
                        Capsule(p, ref n, root, new float3(0, 0, 0), new float3(0, H, 0), 3f, 1.5f, W);
                        for (int i = 0; i < 3; i++)
                            Cone(p, ref n, root, new float3(0, H * (0.5f + 0.15f * i), 0), R0 * (1f - 0.22f * i), H * 0.3f, 0.1f, L, ref r);
                    }
                    else
                    {   // FIR: one broad cone almost to the ground, a second tier inside it
                        float H = r.Range(80, 110), R0 = r.Range(28, 36);
                        Capsule(p, ref n, root, new float3(0, 0, 0), new float3(0, H, 0), 3.5f, 1.5f, W);
                        Cone(p, ref n, root, new float3(0, H * 0.08f, 0), R0, H * 0.95f, 0.1f, L, ref r);
                        Cone(p, ref n, root, new float3(0, H * 0.45f, 0), R0 * 0.75f, H * 0.6f, 0.1f, L, ref r);
                    }
                    break;

                case GenBiome.Desert:
                    if (shape == 0)
                    {   // SAGUARO: a column with one or two elbow arms
                        float H = r.Range(40, 65), rt = r.Range(4f, 5.5f);
                        Capsule(p, ref n, root, new float3(0, 0, 0), new float3(0, H, 0), rt, rt, W);
                        int arms = 1 + (r.Next() < 0.5f ? 1 : 0);
                        for (int i = 0; i < arms; i++)
                        {
                            float2 d = i == 0 ? dir : -dir;
                            float y = H * r.Range(0.3f, 0.5f), len = r.Range(9, 13);
                            float3 s = new float3(0, y, 0), e = new float3(d.x * len, y + 3, d.y * len);
                            Capsule(p, ref n, root, s, e, rt * 0.75f, rt * 0.75f, W);
                            Capsule(p, ref n, root, e, e + new float3(0, r.Range(12, 22), 0), rt * 0.75f, rt * 0.7f, W);
                        }
                    }
                    else if (shape == 1)
                    {   // COLUMN
                        float H = r.Range(28, 48), rt = r.Range(4.5f, 6f);
                        Capsule(p, ref n, root, new float3(0, 0, 0), new float3(0, H, 0), rt, rt * 0.9f, W);
                    }
                    else
                    {   // BARREL: a squat ribbed ball (prim 0 stays a capsule: the root)
                        float rx = r.Range(7, 10), ry = r.Range(9, 12);
                        Capsule(p, ref n, root, new float3(0, 0, 0), new float3(0, 4, 0), rx * 0.8f, rx * 0.8f, W);
                        Blob(p, ref n, root, new float3(0, ry * 0.7f, 0), new float3(rx, ry, rx), 0.08f, W, ref r);
                        p[n - 1].leaf = 0;
                    }
                    break;

                default:
                    break;   // badlands: none (its density is 0 anyway)
            }
        }

        // ==================================================================
        // Task 192 (owner 2026-11-12: "very generic and not unique ... the density
        // of the leaves is too much"): no families. Every tree draws its OWN numbers
        // from the cell's hash INSIDE ranges its biome and cover slot set (so no two
        // trees match while a biome still reads as one kind of woodland), then grows
        // a SKELETON -- trunk -> limbs -> twigs -- and puts its leaves as CLUMPS on
        // the twig ends (conifers: lobed whorls round the trunk), sized so the
        // leaves fill LeafFill % of the crown envelope; the rest is sky. One leaf
        // material per tree (a shade picked per tree), so a canopy brick holds
        // <= 2 values and task 188's compact shell bricks keep working.
        // ==================================================================

        /// One tree's own numbers (fine voxels, relative to its root).
        private struct Look
        {
            public byte form, wood, leaf;
            public float H;            // crown top over the root's ground (cactus / barrel: body height)
            public float rt, footR;    // trunk base radius; the foot test's radius
            public float cb;           // crown base: lowest limb / lowest tier
            public float wr;           // broad: crown radius / tree height; conifer: base radius / crown height
            public float leader;       // broad: trunk top inside the crown (0 = forks at the base, 0.6 = a leader)
            public float rise, flat, droop;
            public int limbs, twigs;   // palm: limbs = fronds; cactus: limbs = arms
            public bool buttress, barrel;
            public bool baobab;        // task 210: savanna's bottle-trunked giant (Trees2 keeps it)
            public float lean, leanSlope;  // lean (fraction of H) on flat ground; extra per unit of foot slope
            public float fill;         // the leaf fill target, 0-1
            public float spacing, curve;   // conifer: tier spacing, radius profile exponent; palm: curve = bend
        }

        private static float SlotScale(int slot) => slot == Cover.Steep || slot == Cover.High ? 0.85f : slot == Cover.Hollow ? 1.1f : 1f;
        private static int Int(ref Rng r, int lo, int hi) => math.min(hi, lo + (int)(r.Next() * (hi - lo + 1)));

        /// This tree's numbers (form 255 = none). The draw ORDER is fixed per biome.
        private static Look Pick(byte biome, int slot, int fillPct, ref Rng r)
        {
            var P = new Look { form = 255, fill = fillPct * 0.01f };
            // AGE: saplings, mature trees, a few old ones (size, girth, limb count)
            float u = r.Next();
            int age = u < 0.15f ? 0 : u < 0.82f ? 1 : 2;
            float s = (age == 0 ? r.Range(0.42f, 0.6f) : age == 1 ? r.Range(0.85f, 1.05f) : r.Range(1.05f, 1.15f)) * SlotScale(slot);
            float girth = age == 2 ? 1.25f : 1f;
            float formRoll = r.Next(), leafRoll = r.Next();
            switch (biome)
            {
                case GenBiome.Forest:
                {   // broad deciduous; conifers mix in on high and steep ground
                    float conifer = slot == Cover.High ? 0.5f : slot == Cover.Steep ? 0.2f : 0.05f;
                    if (formRoll < conifer) { Conifer(ref P, ref r, s * 0.8f, girth); break; }
                    P.form = FORM_BROAD; P.wood = GenMat.ForestWood;
                    P.leaf = leafRoll < 0.6f ? GenMat.ForestLeaves : leafRoll < 0.85f ? GenMat.JungleLeaves : GenMat.SavannaLeaves;
                    P.H = r.Range(100f, 160f) * s;
                    P.cb = P.H * r.Range(0.2f, 0.42f);
                    P.wr = r.Range(0.24f, 0.4f);
                    P.leader = r.Range(0.1f, 0.6f);
                    P.limbs = math.min(5, (age == 0 ? Int(ref r, 2, 3) : Int(ref r, 3, 5)) + (age == 2 ? 1 : 0));
                    P.twigs = age == 0 ? Int(ref r, 1, 2) : Int(ref r, 2, 3);
                    P.rise = r.Range(0.35f, 0.75f); P.flat = r.Range(0.65f, 0.9f); P.droop = r.Range(0f, 0.3f);
                    P.rt = math.max(1.6f, P.H * r.Range(0.033f, 0.048f) * girth);
                    P.lean = r.Range(0f, 0.05f); P.leanSlope = 0.12f;
                    break;
                }
                case GenBiome.Jungle:
                {   // tall emergents with wide high crowns, palms in the hollows
                    float palm = slot == Cover.Hollow ? 0.35f : slot == Cover.Flat ? 0.15f : slot == Cover.Steep ? 0.1f : 0.05f;
                    if (formRoll < palm)
                    {
                        P.form = FORM_PALM; P.wood = GenMat.JungleWood; P.leaf = leafRoll < 0.75f ? GenMat.JungleLeaves : GenMat.ForestLeaves;
                        P.H = r.Range(80f, 140f) * s;
                        P.rt = 3.2f * math.clamp(s, 0.6f, 1.1f);
                        P.curve = r.Range(6f, 24f) * s;
                        P.limbs = Int(ref r, 5, 6);
                        P.droop = r.Range(0.3f, 0.55f);
                        P.lean = r.Range(0f, 0.02f); P.leanSlope = 0.05f;
                        break;
                    }
                    P.form = FORM_BROAD; P.wood = GenMat.JungleWood; P.leaf = leafRoll < 0.7f ? GenMat.JungleLeaves : GenMat.ForestLeaves;
                    float h0 = r.Range(110f, 200f);
                    P.H = h0 * s;
                    P.cb = P.H * math.clamp(math.lerp(0.45f, 0.72f, (h0 - 110f) / 90f) + r.Range(-0.05f, 0.05f), 0.35f, 0.8f);
                    P.wr = r.Range(0.26f, 0.36f);
                    P.leader = r.Range(0f, 0.25f);
                    P.limbs = Int(ref r, 3, 5); P.twigs = age == 0 ? Int(ref r, 1, 2) : Int(ref r, 2, 3);
                    P.rise = r.Range(0.2f, 0.5f); P.flat = r.Range(0.45f, 0.7f); P.droop = r.Range(0.1f, 0.35f);
                    P.rt = math.max(1.8f, P.H * r.Range(0.03f, 0.042f) * girth);
                    P.buttress = age != 0 && (h0 > 150f || age == 2);
                    P.lean = r.Range(0f, 0.03f); P.leanSlope = 0.08f;
                    break;
                }
                case GenBiome.Savanna:
                {   // flat-topped acacias; now and then a bottle-trunked giant
                    P.form = FORM_BROAD; P.wood = GenMat.AcaciaWood; P.leaf = leafRoll < 0.7f ? GenMat.SavannaLeaves : GenMat.ForestLeaves;
                    if (formRoll < 0.12f)
                    {
                        P.baobab = true;
                        P.H = r.Range(60f, 85f) * s;
                        P.rt = P.H * r.Range(0.12f, 0.17f);
                        P.cb = P.H * r.Range(0.6f, 0.75f);
                        P.wr = r.Range(0.3f, 0.45f); P.leader = 0f;
                        P.limbs = Int(ref r, 4, 5); P.twigs = 1;
                        P.rise = r.Range(0.5f, 0.8f); P.flat = 0.6f; P.droop = 0f;
                        P.lean = 0f; P.leanSlope = 0.03f;
                        break;
                    }
                    P.H = r.Range(60f, 95f) * s;
                    P.cb = P.H * r.Range(0.5f, 0.68f);
                    P.wr = r.Range(0.45f, 0.65f);
                    P.leader = r.Range(0f, 0.1f);
                    P.limbs = Int(ref r, 2, 4); P.twigs = age == 0 ? Int(ref r, 1, 2) : Int(ref r, 2, 3);
                    P.rise = r.Range(0.45f, 0.75f); P.flat = r.Range(0.3f, 0.45f); P.droop = r.Range(0f, 0.1f);
                    P.rt = math.max(1.6f, P.H * r.Range(0.045f, 0.06f) * girth);
                    P.lean = r.Range(0f, 0.06f); P.leanSlope = 0.12f;
                    break;
                }
                case GenBiome.Snow:
                    Conifer(ref P, ref r, s, girth);
                    break;
                case GenBiome.Desert:
                {
                    P.form = FORM_CACTUS; P.wood = GenMat.Cactus;
                    float cs = math.clamp(s, 0.6f, 1.1f);
                    if (formRoll < 0.22f)
                    {
                        P.barrel = true;
                        P.rt = r.Range(6f, 10f) * cs; P.H = r.Range(8f, 13f) * cs;
                        P.footR = P.rt * 0.8f;
                        return P;
                    }
                    P.H = r.Range(28f, 70f) * s;
                    P.rt = r.Range(3.8f, 6f) * math.clamp(s, 0.7f, 1.1f);
                    P.limbs = math.clamp((int)(r.Next() * 1.6f * (P.H - 25f) / 14f), 0, 3);
                    break;
                }
                default:
                    return P;   // badlands: none (its density is 0 anyway)
            }
            if (P.form == FORM_BROAD) P.twigs = math.min(P.twigs, 10 / P.limbs);
            P.H = math.min(P.H, 200f);
            P.footR = P.rt;
            return P;
        }

        /// A conifer: tiers of lobed whorls round a straight trunk; q = 0 spruce-like
        /// (crown to the ground, broad), q = 1 pine-like (long bare trunk, narrow top).
        private static void Conifer(ref Look P, ref Rng r, float s, float girth)
        {
            P.form = FORM_CONIFER; P.wood = GenMat.PineWood; P.leaf = GenMat.PineNeedles;
            float q = r.Next();
            P.H = r.Range(100f, 180f) * s;
            P.cb = P.H * math.clamp(math.lerp(0.06f, 0.42f, q) + r.Range(-0.03f, 0.03f), 0.03f, 0.5f);
            P.wr = math.lerp(0.28f, 0.2f, q) * r.Range(0.85f, 1.15f);
            P.spacing = r.Range(13f, 20f) * math.clamp(s, 0.6f, 1.1f);
            P.curve = r.Range(0.8f, 1.15f);
            P.rt = math.max(1.6f, P.H * r.Range(0.025f, 0.035f) * girth);
            P.lean = r.Range(0f, 0.015f); P.leanSlope = 0.03f;
        }

        private static float3 Axis(float2 lean, float yRef, float y) => new float3(lean.x * y / yRef, y, lean.y * y / yRef);

        /// Grow tree P at root: `down` = the foot's downhill vector (height difference
        /// over `span` voxels), so trees on slopes lean out over the drop.
        private static void EmitLook(in Look P, int3 root, float2 down, int span, ref Rng r, TreePrim* p, ref int n)
        {
            float th = r.Angle();
            float dl = math.length(down);
            float2 dir = dl > 0.5f ? down / dl : new float2(math.cos(th), math.sin(th));
            float slope = math.min(dl / math.max(span, 1), 1.5f);
            float2 lean = dir * math.min(P.H * (P.lean + slope * P.leanSlope), 10f);
            byte W = P.wood, Lf = P.leaf;
            switch (P.form)
            {
                case FORM_BROAD:
                {
                    float ch = math.max(P.H - P.cb, 8f);
                    float Rv = ch * 0.5f, Cy = P.cb + Rv;
                    float Rh = math.clamp(P.H * P.wr, 6f, 48f - math.length(lean));
                    float yf = P.cb + ch * P.leader;
                    float3 top = Axis(lean, Cy, yf);
                    Capsule(p, ref n, root, new float3(0, 0, 0), top, P.rt, math.max(P.rt * 0.5f, 1.2f), W);   // prim 0: the trunk
                    if (P.buttress) Capsule(p, ref n, root, new float3(0, 0, 0), new float3(0, math.min(P.rt * 2.5f, 18f), 0), P.rt + math.min(P.rt * 0.9f, 5.5f), P.rt, W);
                    int Lm = P.limbs, T = P.twigs;
                    bool topClump = P.leader >= 0.3f;
                    int N = Lm * T + (topClump ? 1 : 0);
                    // CLUMP SIZE from the fill target. Clumps overlap more as they grow, so the measured
                    // fill (leaf voxels / the ellipsoid inscribed in the leaves' box) saturates near 55 %:
                    // tf = the clump volume asked of the crown ellipsoid (Rh, Rv, Rh) for a measured fill
                    // of P.fill (task 192's calibration on seeds 42 / 7: 0.30 -> 0.30, 0.45 -> 0.52, 0.60 -> 0.90;
                    // 1.5 = the densest clump crown, about 66 %). Sparse crowns put their clumps on the upper shell (a dome,
                    // the interior open); denser ones also fill the inside and the underside.
                    float tf = math.min(0.3f * math.exp(3.66f * (P.fill - 0.3f)), 1.5f), tf1 = math.min(tf, 1f);
                    float rc = math.pow(tf * Rh * Rh * Rv / (N * P.flat * 1.6f), 1f / 3f);
                    rc = math.clamp(rc, 4f, math.min(Rh, Rv / P.flat) * 0.8f);
                    float Rhs = math.max(Rh - rc, 2f), Rvs = math.max(Rv - rc * P.flat, 1f);
                    float uLo = 0.95f - 0.45f * tf1, vLo = -0.1f - 0.6f * tf1;
                    float rl0 = math.max(P.rt * 0.55f * math.rsqrt(Lm / 3f), 1.4f), rl1 = math.max(rl0 * 0.6f, 1.1f), rw = math.max(rl1 * 0.75f, 1f);
                    float phi0 = r.Angle(), sector = 6.2831853f / Lm;
                    for (int i = 0; i < Lm; i++)
                    {
                        float phi = phi0 + i * sector + r.Range(-0.3f, 0.3f) * sector;
                        float2 d = new float2(math.cos(phi), math.sin(phi));
                        float ys = topClump ? P.cb + (yf - P.cb) * (Lm == 1 ? 0f : (float)i / (Lm - 1)) * 0.85f : yf;
                        ys = math.clamp(ys + r.Range(-2f, 2f), P.cb * 0.9f, yf);
                        float3 S = Axis(lean, Cy, ys);
                        float v = math.clamp(math.lerp(-0.35f, 0.45f, P.rise) + r.Range(-0.15f, 0.15f), -0.6f, 0.7f);
                        float rho = Rhs * math.sqrt(1f - v * v) * (T == 1 ? r.Range(0.6f, 0.85f) : r.Range(0.3f, 0.55f));
                        var E = new float3(lean.x + d.x * rho, Cy + Rvs * v, lean.y + d.y * rho);
                        E.y = math.max(E.y, S.y + 2f);
                        Capsule(p, ref n, root, S, E, rl0, rl1, W);
                        if (T == 1)
                        {
                            float k1 = r.Range(0.85f, 1.15f);
                            Blob(p, ref n, root, E, new float3(rc * k1, rc * k1 * P.flat, rc * k1), 0.3f, Lf, ref r);
                            continue;
                        }
                        float spread = sector * r.Range(0.5f, 0.85f);
                        for (int j = 0; j < T; j++)
                        {
                            float psi = phi + spread * ((float)j / (T - 1) - 0.5f) + r.Range(-0.15f, 0.15f);
                            float vv = math.clamp(r.Range(vLo, 0.75f) - P.droop, -0.8f, 0.9f);
                            float uu = r.Range(uLo, 1f) * math.sqrt(1f - vv * vv);
                            var Q = new float3(lean.x + math.cos(psi) * Rhs * uu, Cy + Rvs * vv, lean.y + math.sin(psi) * Rhs * uu);
                            Capsule(p, ref n, root, E, Q, rl1, rw, W);
                            float k = r.Range(0.8f, 1.15f);
                            Blob(p, ref n, root, Q, new float3(rc * k, rc * k * P.flat, rc * k), 0.3f, Lf, ref r);
                        }
                    }
                    if (topClump) Blob(p, ref n, root, top + new float3(0, rc * P.flat * 0.4f, 0), new float3(rc, rc * P.flat, rc), 0.3f, Lf, ref r);
                    break;
                }
                case FORM_CONIFER:
                {
                    float ch = math.max(P.H - P.cb, 12f);
                    float R0 = math.min(ch * P.wr, 46f);
                    var top = new float3(lean.x, P.H * 0.96f, lean.y);
                    Capsule(p, ref n, root, new float3(0, 0, 0), top, P.rt, 1.2f, W);   // prim 0: the trunk
                    float topLen = math.clamp(ch * 0.14f, 6f, 20f);
                    int N = math.clamp((int)((ch - topLen) / P.spacing), 3, 8);
                    float sp = (ch - topLen) / N;
                    // TIER HEIGHT from the fill target: a stack of N cones of height g x spacing under a
                    // straight-sided envelope cone fills about g / 3 of it
                    float g = math.clamp(3f * P.fill, 0.75f, 2.2f);
                    float phi0 = r.Angle(), Rlast = R0;
                    for (int k = 0; k < N; k++)
                    {
                        float y = P.cb + k * sp + r.Range(-0.15f, 0.15f) * sp;
                        float t = math.saturate((y - P.cb) / ch);
                        float R = math.max(R0 * math.pow(1f - t, P.curve) * r.Range(0.85f, 1.1f), 4f);
                        float h = math.clamp(g * sp, 3f, R * 1.8f);
                        float3 c = Axis(lean, P.H, y);
                        Cone(p, ref n, root, c, R, h, 0.3f, Lf, ref r);
                        // a limb under the whorl: bark shows between the tiers
                        float phi = phi0 + k * 2.39996f;
                        Capsule(p, ref n, root, c + new float3(0, 1f, 0),
                                c + new float3(math.cos(phi) * R * 0.85f, r.Range(0.5f, 2.5f), math.sin(phi) * R * 0.85f),
                                math.max(1.4f, P.rt * 0.35f), 1f, W);
                        Rlast = R;
                    }
                    Cone(p, ref n, root, Axis(lean, P.H, P.H - topLen), math.min(Rlast * 0.6f, 7f), topLen + 4f, 0.15f, Lf, ref r);
                    break;
                }
                case FORM_PALM:
                {
                    float2 bd = math.normalizesafe(lean, dir);
                    float bend = P.curve + math.length(lean);
                    float3 p0 = new float3(0, 0, 0);
                    float3 p1 = new float3(bd.x * bend * 0.11f, P.H * 0.34f, bd.y * bend * 0.11f);
                    float3 p2 = new float3(bd.x * bend * 0.44f, P.H * 0.67f, bd.y * bend * 0.44f);
                    float3 p3 = new float3(bd.x * bend, P.H, bd.y * bend);
                    float tr = P.rt;
                    Capsule(p, ref n, root, p0, p1, tr, tr * 0.94f, W);   // prim 0: the trunk's foot
                    Capsule(p, ref n, root, p1, p2, tr * 0.94f, tr * 0.84f, W);
                    Capsule(p, ref n, root, p2, p3, tr * 0.84f, tr * 0.75f, W);
                    float sz = tr / 3.2f;
                    Blob(p, ref n, root, p3 + new float3(0, 1.5f, 0), new float3(5f, 4f, 5f) * sz, 0.1f, Lf, ref r);
                    // FRONDS: chains of four flat leaf pads arching out and down, sized from the frond's
                    // length (successive pads overlap: no detached crumbs); the fill scales their width
                    float fs = math.pow(P.fill / 0.45f, 1f / 3f);
                    float phi0 = r.Angle();
                    for (int f = 0; f < P.limbs; f++)
                    {
                        float psi = phi0 + f * 6.2831853f / P.limbs + r.Range(-0.25f, 0.25f);
                        float2 d = new float2(math.cos(psi), math.sin(psi));
                        float len = r.Range(26f, 38f) * sz;
                        float dist = 4f * sz;
                        for (int k = 0; k < 4; k++)
                        {
                            float rad = len * (0.2f - 0.035f * k) * fs;
                            float t = dist / len;
                            var c = p3 + new float3(d.x * dist, 1.5f + len * (0.25f * t - P.droop * t * t), d.y * dist);
                            Blob(p, ref n, root, c, new float3(rad, rad * 0.42f, rad), 0.12f, Lf, ref r);
                            dist += 0.6f * (rad + len * (0.2f - 0.035f * (k + 1)) * fs);
                        }
                    }
                    break;
                }
                case FORM_CACTUS:
                {
                    if (P.barrel)
                    {   // a squat ribbed ball (prim 0 stays a capsule: the root)
                        Capsule(p, ref n, root, new float3(0, 0, 0), new float3(0, 4, 0), P.rt * 0.8f, P.rt * 0.8f, W);
                        Blob(p, ref n, root, new float3(0, P.H * 0.7f, 0), new float3(P.rt, P.H, P.rt), 0.08f, W, ref r);
                        p[n - 1].leaf = 0;
                        break;
                    }
                    Capsule(p, ref n, root, new float3(0, 0, 0), new float3(0, P.H, 0), P.rt, P.rt * r.Range(0.8f, 1f), W);
                    float phi0 = r.Angle();
                    for (int a = 0; a < P.limbs; a++)
                    {
                        float phi = phi0 + a * 6.2831853f / P.limbs + r.Range(-0.5f, 0.5f);
                        float2 d = new float2(math.cos(phi), math.sin(phi));
                        float y = P.H * r.Range(0.3f, 0.6f), len = r.Range(7f, 13f);
                        float up = math.min(r.Range(10f, 24f), P.H * 1.05f - y - 3f);
                        var e = new float3(d.x * len, y + 3f, d.y * len);
                        Capsule(p, ref n, root, new float3(0, y, 0), e, P.rt * 0.72f, P.rt * 0.72f, W);
                        if (up > 2f) Capsule(p, ref n, root, e, e + new float3(0, up, 0), P.rt * 0.72f, P.rt * 0.65f, W);
                    }
                    break;
                }
            }
        }

        private static void Capsule(TreePrim* p, ref int n, int3 root, float3 a, float3 b, float r0, float r1, byte mat)
        {
            p[n] = new TreePrim { root = root, a = a, b = b, r0 = r0, r1 = r1, kind = CAPSULE, mat = mat, leaf = 0 };
            Bounds(ref p[n]);
            n++;
        }

        private static void Blob(TreePrim* p, ref int n, int3 root, float3 c, float3 radii, float amp, byte mat, ref Rng r)
        {
            p[n] = new TreePrim { root = root, a = c, b = radii, r0 = amp, kind = BLOB, mat = mat, leaf = 1,
                                  noise = Ores.Hash(r.seed, r.cx, r.k++, r.cz) };
            Bounds(ref p[n]);
            n++;
        }

        private static void Cone(TreePrim* p, ref int n, int3 root, float3 baseC, float radius, float height, float amp, byte mat, ref Rng r)
        {
            p[n] = new TreePrim { root = root, a = baseC, b = new float3(radius, height, 0), r0 = amp, kind = CONE, mat = mat, leaf = 1,
                                  noise = Ores.Hash(r.seed, r.cx, r.k++, r.cz) };
            Bounds(ref p[n]);
            n++;
        }

        /// The primitive's inclusive integer AABB (absolute), edge noise included.
        public static void Bounds(ref TreePrim p)
        {
            float3 lo, hi;
            if (p.kind == CAPSULE)
            {
                float rr = math.max(p.r0, p.r1);
                lo = math.min(p.a, p.b) - rr; hi = math.max(p.a, p.b) + rr;
            }
            else if (p.kind == BLOB)
            {
                float3 e = p.b * (1f + p.r0);
                lo = p.a - e; hi = p.a + e;
            }
            else
            {
                float e = p.b.x * (1f + p.r0);
                lo = new float3(p.a.x - e, p.a.y, p.a.z - e); hi = new float3(p.a.x + e, p.a.y + p.b.y, p.a.z + e);
            }
            p.lo = p.root + (int3)math.floor(lo) - 1;
            p.hi = p.root + (int3)math.ceil(hi) + 1;
        }

        // ---- the shape test ----

        /// Whether fine voxel v lies inside primitive p.
        public static bool Contains(in TreePrim p, int3 v)
        {
            int3 l = v - p.root;
            float3 q = l;
            if (p.kind == CAPSULE)
            {
                float3 ab = p.b - p.a;
                float t = math.clamp(math.dot(q - p.a, ab) / math.max(math.dot(ab, ab), 1e-6f), 0f, 1f);
                float r = math.lerp(p.r0, p.r1, t);
                float3 d = q - (p.a + ab * t);
                return math.dot(d, d) <= r * r;
            }
            if (p.kind == BLOB)
            {
                float3 nq = (q - p.a) / p.b;
                float d2 = math.dot(nq, nq);
                float kmax = 1f + p.r0;
                if (d2 > kmax * kmax) return false;
                float kmin = 1f - p.r0;
                if (d2 <= kmin * kmin) return true;
                // the noise is read on the SURFACE point in this direction, so the edge radius
                // depends on the direction only: the crown is star-shaped, no detached crumbs
                float3 s = p.a + nq * (p.b / math.sqrt(d2));
                float k = 1f + p.r0 * EdgeNoise(s, p.noise);
                return d2 <= k * k;
            }
            {
                float y = q.y - p.a.y;
                if (y < 0f || y > p.b.y) return false;
                float R = p.b.x * (1f - y / p.b.y);
                float dx = q.x - p.a.x, dz = q.z - p.a.z, d2 = dx * dx + dz * dz;
                float rmax = R * (1f + p.r0);
                if (d2 > rmax * rmax) return false;
                float rmin = R * (1f - p.r0);
                if (d2 <= rmin * rmin) return true;
                // noise on the slice's rim in this direction (star-shaped round the axis, as BLOB)
                float inv = R / math.sqrt(d2);
                float3 s = new float3(p.a.x + dx * inv, q.y, p.a.z + dz * inv);
                float rk = R * (1f + p.r0 * EdgeNoise(s, p.noise));
                return d2 <= rk * rk;
            }
        }

        /// Whether EVERY point of the box [lo, hi] (fine voxels) is inside p for any
        /// edge noise (the inner shape is convex: its 8 corners suffice). Capsules: false.
        public static bool ContainsBox(in TreePrim p, int3 lo, int3 hi)
        {
            if (p.kind == CAPSULE) return false;
            for (int i = 0; i < 8; i++)
            {
                int3 v = new int3((i & 1) != 0 ? hi.x : lo.x, (i & 2) != 0 ? hi.y : lo.y, (i & 4) != 0 ? hi.z : lo.z);
                float3 q = v - p.root;
                if (p.kind == BLOB)
                {
                    float3 nq = (q - p.a) / p.b;
                    float kmin = 1f - p.r0;
                    if (math.dot(nq, nq) > kmin * kmin) return false;
                }
                else
                {
                    float y = q.y - p.a.y;
                    if (y < 0f || y > p.b.y) return false;
                    float rmin = p.b.x * (1f - y / p.b.y) * (1f - p.r0);
                    float dx = q.x - p.a.x, dz = q.z - p.a.z;
                    if (dx * dx + dz * dz > rmin * rmin) return false;
                }
            }
            return true;
        }

        /// Value noise in [-1, 1] on an 8-voxel lattice, smoothstep-blended (continuous
        /// in s, a point relative to the root): the leafy lumps on a crown's edge.
        public static float EdgeNoise(float3 s, uint seed)
        {
            float3 s8 = s * 0.125f, fl = math.floor(s8);
            int3 c = (int3)fl;
            float3 f = s8 - fl;
            f = f * f * (3f - 2f * f);
            float n000 = Lat(c, seed), n100 = Lat(c + new int3(1, 0, 0), seed);
            float n010 = Lat(c + new int3(0, 1, 0), seed), n110 = Lat(c + new int3(1, 1, 0), seed);
            float n001 = Lat(c + new int3(0, 0, 1), seed), n101 = Lat(c + new int3(1, 0, 1), seed);
            float n011 = Lat(c + new int3(0, 1, 1), seed), n111 = Lat(c + new int3(1, 1, 1), seed);
            float x00 = math.lerp(n000, n100, f.x), x10 = math.lerp(n010, n110, f.x);
            float x01 = math.lerp(n001, n101, f.x), x11 = math.lerp(n011, n111, f.x);
            return math.lerp(math.lerp(x00, x10, f.y), math.lerp(x01, x11, f.y), f.z);
        }

        private static float Lat(int3 c, uint seed) => (Ores.Hash(seed, c.x, c.y, c.z) & 0xFFFFu) * (2f / 65535f) - 1f;
    }

    /// The per-map-cell tree density plane a chunk generator reads (beside the
    /// ore plane). Managed (CPU, built once with the world); kept out of Trees so
    /// Burst never sees a managed static. One byte per cell: the landform's
    /// multiplier x the movement band's (1/16ths), 0 = no tree.
    public static class TreeTable
    {
        /// Regions archetype 0-9 (1/16): alpine, highlands, canyonlands, volcanic,
        /// river plains, breaks, dunes, sea cliffs, plateau, wetland.
        private static readonly byte[] LANDFORM = { 10, 16, 6, 3, 18, 6, 3, 12, 12, 20 };
        /// Danger movement band (1/16): walk, scramble, climb, sheer.
        private static readonly byte[] BAND = { 16, 14, 6, 0 };

        public static byte CellValue(in RecordCell rc)
        {
            if (rc.IsSea || rc.IsRoute || rc.IsFeature) return 0;
            int a = rc.Archetype, b = rc.MoveBand;
            if (a >= LANDFORM.Length || b >= BAND.Length) return 0;
            return (byte)((LANDFORM[a] * BAND[b] + 8) >> 4);
        }

        /// One value per map cell (row-major, cells x cells); all zeros (no trees)
        /// when there is no record (map v3).
        public static byte[] DensityPlane(WorldRecord record, int cells)
        {
            var plane = new byte[cells * cells];
            if (record == null) return plane;
            for (int z = 0; z < cells; z++)
            for (int x = 0; x < cells; x++)
            {
                var rc = record.AtCell(x, z);
                plane[z * cells + x] = CellValue(in rc);
            }
            return plane;
        }
    }
}
