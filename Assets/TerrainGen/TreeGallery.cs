// Assets/TerrainGen/TreeGallery.cs
//
// Task 200 (owner 2026-11-13, trees talk T1-T4): EXAMPLES ONLY. Prototype tree
// GROWING METHODS at 0.1 m, so the owner can see end products before a method is
// chosen. Nothing here is read by the world: Trees.cs (tasks 180 / 192) stays the
// world's trees byte for byte; this file is reached only through the capture flag
// `-treegallery <layout file>` (TerrainGenChunkSource.Gallery.cs, default OFF) and
// the EditMode tests in TreeGalleryTests.
//
// THE CONTRACT IT KEEPS (Trees.cs header): a tree is a pure function of an integer
// seed (here: species, method, seed; in a world: seed + its tree cell), BUILT ONCE
// into a small voxel volume, then QUERIED per voxel; trees fill AIR only. What it
// would change in Trees.cs is in HANDOFF 200 (the volume replaces the primitive
// list; giants need a bigger cell / reach / height bound).
//
// METHODS (all share the species table `Pick`, the trunk seeding, the voxeliser):
//   M_SPACE    space colonization (Runions et al. 2007): attraction points fill the
//              species' crown ENVELOPE (outer shell only where the crown is open);
//              branch nodes grow a step toward the points that see them, points die
//              when a node comes within the kill distance; radii by the pipe model
//              (r^e = sum of the children's r^e). Leaf clumps on the tips.
//   M_LSYS     rule-based recursive branching (an L-system / Weber-Penn style):
//              trunk -> level-1 limbs (137.5 deg phyllotaxis, a down angle, length
//              from the envelope) -> level-2 -> level-3 twigs; clumps on twig ends.
//   M_TEMPLATE a fixed authored limb list per species + per-tree jitter (the
//              "template + variation" baseline; close to today's Trees.cs idea).
// GIANTS: banyan aerial roots drop from the main limbs to the ground; some thicken
// into prop trunks with their own flare. Redwood: a straight tapering leader, a
// fluted buttress flare, branches only in the top ~half, a narrow crown.
//
// UNITS: voxels (0.1 m), local to the root: (0, 0, 0) = the ground's top solid
// voxel at the trunk column; y up. Integer hashes only (Ores.Hash), no Random.
using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace TerrainGen
{
    public static class TreeGallery
    {
        public const int SP_OAK = 0, SP_BANYAN = 1, SP_REDWOOD = 2, SP_BIRCH = 3, SP_SPRUCE = 4, SP_ACACIA = 5, SP_COUNT = 6;
        public const int M_SPACE = 0, M_LSYS = 1, M_TEMPLATE = 2, M_COUNT = 3;
        public static readonly string[] SpeciesName = { "oak", "banyan", "redwood", "birch", "spruce", "acacia" };
        public static readonly string[] MethodName = { "spacecol", "lsystem", "template" };
        /// The person-height marker's material (a 0.4 x 0.3 x 1.8 m post).
        public const byte MARKER_MAT = GenMat.RedSandstone;
        /// Trunks run this far under their root's ground (they fill air only).
        public const int ROOT_DEPTH = 40;
        private const uint SALT = 0x7A11E200u;
        private const float MIN_R = 0.9f;   // a branch's least radius (voxels): 6-connected at any angle

        /// Local column (x, z) -> local Y of its top solid voxel (the root column's = 0).
        public delegate int Ground(int x, int z);
        public static int Flat(int x, int z) => 0;
        /// Wood of a gallery tree (today's trunk GenMats + the birch's pale stand-in).
        public static bool IsTreeWood(byte m) => GenMat.IsTreeTrunk(m) || m == GenMat.MushroomStem;

        public const byte ROLE_BRANCH = 0, ROLE_TRUNK = 1, ROLE_ROOT = 2;
        public struct Node { public float3 p, dir; public int parent, kids; public float r; public byte role; }
        public struct Seg { public float3 a, b; public float ra, rb; }
        public struct Clump { public float3 c, rad; }
        public struct Flare { public float3 b; public float r, amp, h, phase, top; public int flutes; }

        public struct Sp
        {
            public float H, W, cb, rt;          // crown top, crown width, crown base, trunk radius (voxels)
            public int env; public float inner; // envelope; attraction points only outside inner x its radius (open interior)
            public int points; public float step, di, dk;   // space colonization
            public float tropism;               // + up / - down, per growth step
            public bool leader;                 // excurrent: the trunk runs to the top
            public float forkY, limbElev, limbLen; public int forks;   // decurrent: trunk forks at forkY into `forks` limbs
            public float clumpR, clumpFlat, density;   // leaf clump radius (voxels), its y squash, kept fraction inside a clump
            public float flare, flareH; public int flutes;   // trunk base: radius x (1 + flare) at the ground, e-fold height, buttresses
            public int stems;                   // birch: 1-3 stems from one root
            public bool aerial;                 // banyan: aerial roots -> prop trunks
            public float pipe;                  // pipe-model exponent
            public byte wood, leaf;
            public int n1, n2, n3; public float down1, down2, ratio2, ratio3, curve;   // L-system
        }
        public const int ENV_ELLIPSOID = 0, ENV_DOME = 1, ENV_CONE = 2, ENV_OVAL = 3, ENV_LENS = 4, ENV_COLUMN = 5;

        public sealed class Skeleton
        {
            public int species, method; public uint seed; public Sp sp;
            public float lobe1, lobe2, ph1, ph2;
            public readonly List<Node> nodes = new List<Node>();
            public readonly List<Seg> segs = new List<Seg>();
            public readonly List<Clump> clumps = new List<Clump>();
            public readonly List<Flare> flares = new List<Flare>();
            public readonly List<float3> pendingRoots = new List<float3>();   // template: fixed prop-root spots
            public int attraction, iterations, aerialRoots, propRoots;
        }

        public struct Stats
        {
            public int trunk, leaf, segs, clumps, nodes, crumbs, bricks, attraction, iterations, aerial, props;
            public long bytes; public double msGrow, msVox; public int3 size; public int top;   // top: highest voxel over the root's ground
        }

        public struct Rng
        {
            public uint s; public int k;
            public Rng(uint seed) { s = seed; k = 0; }
            public float Next() => (Ores.Hash(s, k++, 0x2C1, 0x71) & 0xFFFFFFu) / 16777216f;
            public float Range(float a, float b) => a + (b - a) * Next();
            public float Angle() => Next() * 6.2831853f;
            public bool Chance(float p) => Next() < p;
            public int Int(int lo, int hi) => math.min(hi, lo + (int)(Next() * (hi - lo + 1)));
        }

        public static uint TreeSeed(int species, int method, uint seed) => Ores.Hash(SALT, species, method, (int)seed);

        // =====================================================================
        // Species table: every number a RANGE, drawn per tree (T3 (a): own settings, varied tree by tree)
        // =====================================================================
        public static Sp Pick(int species, ref Rng r)
        {
            var s = new Sp { pipe = 2.3f, clumpFlat = 0.7f, density = 0.55f, stems = 1, curve = 0.2f, limbLen = 0.5f };
            switch (species)
            {
                case SP_OAK:   // open-grown English oak: broad dome, short trunk forking into big limbs
                    s.H = r.Range(150, 195); s.W = s.H * r.Range(1.0f, 1.25f); s.cb = r.Range(25, 40); s.rt = r.Range(5f, 7f);
                    s.env = ENV_ELLIPSOID; s.inner = 0.5f; s.points = 1100; s.step = 4.5f; s.di = 32; s.dk = 11;
                    s.tropism = 0.1f; s.forkY = s.cb + r.Range(8, 25); s.forks = r.Int(3, 5); s.limbElev = r.Range(38, 55); s.limbLen = 0.42f;
                    s.clumpR = r.Range(6.5f, 8f); s.clumpFlat = 0.7f; s.density = 0.55f;
                    s.flare = 0.45f; s.flareH = 9; s.flutes = 5;
                    s.wood = GenMat.ForestWood; s.leaf = GenMat.ForestLeaves;
                    s.n1 = 5; s.n2 = 6; s.n3 = 5; s.down1 = 60; s.down2 = 50; s.ratio2 = 0.5f; s.ratio3 = 0.45f; s.curve = 0.3f;
                    break;
                case SP_BANYAN:   // giant: wide flat dome, horizontal limbs, aerial roots thickening into prop trunks
                    s.H = r.Range(170, 220); s.W = r.Range(300, 380); s.cb = r.Range(50, 70); s.rt = r.Range(11f, 15f);
                    s.env = ENV_DOME; s.inner = 0.35f; s.points = 2600; s.step = 6f; s.di = 42; s.dk = 14;
                    s.tropism = 0.04f; s.forkY = s.cb - r.Range(0, 10); s.forks = r.Int(5, 7); s.limbElev = r.Range(74, 84); s.limbLen = 0.6f;
                    s.clumpR = r.Range(8f, 10f); s.clumpFlat = 0.6f; s.density = 0.6f;
                    s.flare = 0.8f; s.flareH = 14; s.flutes = 8; s.aerial = true;
                    s.wood = GenMat.JungleWood; s.leaf = GenMat.JungleLeaves;
                    s.n1 = 3; s.n2 = 6; s.n3 = 5; s.down1 = 75; s.down2 = 55; s.ratio2 = 0.45f; s.ratio3 = 0.4f; s.curve = 0.25f;
                    break;
                case SP_REDWOOD:   // giant: straight fluted leader, buttress, branches only in the top ~half, narrow crown
                    s.H = r.Range(580, 720); s.W = r.Range(110, 150); s.cb = s.H * r.Range(0.45f, 0.55f); s.rt = r.Range(17f, 22f);
                    s.env = ENV_COLUMN; s.inner = 0.15f; s.points = 1500; s.step = 6f; s.di = 34; s.dk = 12;
                    s.tropism = -0.12f; s.leader = true; s.limbLen = 0.5f;
                    s.clumpR = r.Range(6.5f, 8f); s.clumpFlat = 0.5f; s.density = 0.6f;
                    s.flare = 0.9f; s.flareH = 26; s.flutes = 7; s.pipe = 2.0f;
                    s.wood = GenMat.PineWood; s.leaf = GenMat.PineNeedles;
                    s.n1 = 46; s.n2 = 4; s.n3 = 0; s.down1 = 100; s.down2 = 50; s.ratio2 = 0.45f; s.curve = 0.15f;
                    break;
                case SP_BIRCH:   // slender pale stems (often 2-3), narrow oval crown, fine drooping twigs, light leaves
                    s.H = r.Range(130, 175); s.W = s.H * r.Range(0.36f, 0.46f); s.cb = s.H * r.Range(0.28f, 0.38f); s.rt = r.Range(1.6f, 2.4f);
                    s.env = ENV_OVAL; s.inner = 0.25f; s.points = 600; s.step = 3.5f; s.di = 22; s.dk = 7.5f;
                    s.tropism = -0.12f; s.leader = true; s.stems = r.Chance(0.5f) ? r.Int(2, 3) : 1;
                    s.clumpR = r.Range(4.5f, 5.5f); s.clumpFlat = 0.75f; s.density = 0.45f;
                    s.flare = 0.25f; s.flareH = 5; s.flutes = 0;
                    s.wood = GenMat.BirchBark; s.leaf = GenMat.Leaves;   // task 210: the real pale bark (task 200 used MushroomStem as a stand-in)
                    s.n1 = 14; s.n2 = 4; s.n3 = 3; s.down1 = 45; s.down2 = 60; s.ratio2 = 0.45f; s.ratio3 = 0.4f; s.curve = 0.3f;
                    break;
                case SP_SPRUCE:   // narrow cone to the ground, drooping whorled branches, dark needles
                    s.H = r.Range(190, 270); s.W = s.H * r.Range(0.28f, 0.36f); s.cb = r.Range(8, 20); s.rt = r.Range(3f, 4.5f);
                    s.env = ENV_CONE; s.inner = 0f; s.points = 2000; s.step = 4f; s.di = 24; s.dk = 7;
                    s.tropism = -0.2f; s.leader = true;
                    s.clumpR = r.Range(5f, 6.5f); s.clumpFlat = 0.5f; s.density = 0.7f;
                    s.flare = 0.35f; s.flareH = 6; s.flutes = 0; s.pipe = 2.0f;
                    s.wood = GenMat.PineWood; s.leaf = GenMat.PineNeedles;
                    s.n1 = 40; s.n2 = 4; s.n3 = 0; s.down1 = 100; s.down2 = 45; s.ratio2 = 0.35f; s.curve = 0.15f;
                    break;
                default:   // SP_ACACIA, umbrella thorn: low fork into ascending limbs, a thin flat-topped crown wider than tall
                    s.H = r.Range(65, 90); s.W = s.H * r.Range(1.6f, 2.1f); s.cb = s.H - r.Range(20, 28); s.rt = r.Range(2.5f, 3.8f);
                    s.env = ENV_LENS; s.inner = 0f; s.points = 800; s.step = 3.5f; s.di = 24; s.dk = 8;
                    s.tropism = 0.05f; s.forkY = r.Range(12, 28); s.forks = r.Int(2, 4); s.limbElev = r.Range(30, 45); s.limbLen = 0.55f;
                    s.clumpR = r.Range(5f, 6.5f); s.clumpFlat = 0.45f; s.density = 0.5f;
                    s.flare = 0.3f; s.flareH = 5; s.flutes = 3;
                    s.wood = GenMat.AcaciaWood; s.leaf = GenMat.SavannaLeaves;
                    s.n1 = 0; s.n2 = 6; s.n3 = 5; s.down1 = 60; s.down2 = 70; s.ratio2 = 0.5f; s.ratio3 = 0.45f; s.curve = 0.2f;
                    break;
            }
            return s;
        }

        /// The crown envelope's horizontal radius at local height y (0 outside [cb, H]).
        public static float EnvR(in Sp s, float y)
        {
            float t = (y - s.cb) / math.max(1f, s.H - s.cb);
            if (t < 0f || t > 1f) return 0f;
            float R = s.W * 0.5f;
            switch (s.env)
            {
                case ENV_ELLIPSOID: return R * math.sqrt(math.max(0f, 1f - (2f * t - 1f) * (2f * t - 1f))) * (t < 0.5f ? 0.85f + 0.3f * t : 1f);
                case ENV_DOME: return R * math.sqrt(math.max(0f, 1f - math.pow(t, 2.2f))) * math.min(1f, 0.55f + 3f * t);
                case ENV_CONE: return R * math.pow(1f - t, 1.05f);
                case ENV_OVAL: return R * math.pow(math.sin(math.PI * math.min(1f, t * 0.9f + 0.05f)), 0.7f);
                case ENV_LENS: return R * math.sqrt(math.max(0f, 1f - math.pow(t, 4f))) * math.min(1f, 0.6f + 1.5f * t);
                default: return R * math.pow(math.min(1f, (1f - t) * 2.4f), 0.75f) * (0.75f + 0.25f * math.sin(t * 9.4f + 1.3f) * math.sin(t * 9.4f + 1.3f));   // COLUMN
            }
        }

        private static float Lobe(Skeleton k, float3 q)
        {
            float th = math.atan2(q.z, q.x);
            return 1f + k.lobe1 * math.sin(2f * th + k.ph1) + k.lobe2 * math.sin(3f * th + k.ph2);
        }

        // =====================================================================
        // Entry points
        // =====================================================================
        /// `world` (task 210, Trees2's library): aerial roots run ROOT_DEPTH into the ground
        /// (not 6 voxels), so a prototype built on flat ground still reaches a gently uneven site.
        public static Skeleton Grow(int species, int method, uint seed, Ground ground, bool world = false)
        {
            uint ts = TreeSeed(species, method, seed);
            var r = new Rng(ts);
            var k = new Skeleton { species = species, method = method, seed = ts };
            var rs = new Rng(Ores.Hash(SALT, species, 99, (int)seed));   // the species draw is method-independent: same tree size per seed
            k.sp = Pick(species, ref rs);
            k.lobe1 = r.Range(0.05f, 0.2f); k.lobe2 = r.Range(0.04f, 0.14f); k.ph1 = r.Angle(); k.ph2 = r.Angle();
            switch (method)
            {
                case M_SPACE: SeedTrunk(k, ref r, true); SpaceColonize(k, ref r); PipeRadii(k); break;
                case M_LSYS: LSystem(k, ref r); break;
                default: Template(k, ref r); break;
            }
            if (k.sp.aerial) AerialRoots(k, ref r, ground, world);
            Finish(k, ref r);
            return k;
        }

        public static Vol Build(int species, int method, uint seed, Ground ground, out Stats st, out Skeleton k, bool world = false)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            k = Grow(species, method, seed, ground ?? Flat, world);
            double g = sw.Elapsed.TotalMilliseconds; sw.Restart();
            var v = Voxelise(k, out st);
            st.msGrow = g; st.msVox = sw.Elapsed.TotalMilliseconds;
            return v;
        }

        // =====================================================================
        // Trunk seeding (shared): root node, stems, leader or fork limbs
        // =====================================================================
        private static int AddNode(Skeleton k, int parent, float3 p, byte role, float r = 0f)
        {
            float3 d = parent >= 0 ? math.normalizesafe(p - k.nodes[parent].p, new float3(0, 1, 0)) : new float3(0, 1, 0);
            k.nodes.Add(new Node { p = p, dir = d, parent = parent, r = r, role = role });
            if (parent >= 0) { var n = k.nodes[parent]; n.kids++; k.nodes[parent] = n; }
            return k.nodes.Count - 1;
        }

        /// A wobbly chain from node `from` to b in `n` steps; radii lerp r0 -> r1. Returns the last node.
        private static int Chain(Skeleton k, int from, float3 b, int n, byte role, float r0, float r1, float wobble, ref Rng r)
        {
            float3 a = k.nodes[from].p;
            int cur = from;
            float3 off = 0f;
            for (int i = 1; i <= n; i++)
            {
                float t = (float)i / n;
                if (i < n) off += new float3(r.Range(-1, 1), 0, r.Range(-1, 1)) * wobble;
                float3 p = math.lerp(a, b, t) + (i < n ? off : 0f);
                cur = AddNode(k, cur, p, role, math.lerp(r0, r1, t));
            }
            return cur;
        }

        /// Root, stem(s), then either a leader to the top or a fork into limbs. sc: limbs
        /// stop inside the crown (space colonization grows the rest).
        private static void SeedTrunk(Skeleton k, ref Rng r, bool sc)
        {
            var s = k.sp;
            int root = AddNode(k, -1, new float3(0, -ROOT_DEPTH, 0), ROLE_TRUNK, s.rt);
            int ground = AddNode(k, root, new float3(0, 0, 0), ROLE_TRUNK, s.rt);
            float lean = r.Range(0f, 0.04f) * s.H; float la = r.Angle();
            for (int st = 0; st < s.stems; st++)
            {
                float a = la + st * 6.2831853f / s.stems;
                float spread = s.stems > 1 ? s.H * r.Range(0.06f, 0.12f) : lean;
                float3 dirXZ = new float3(math.cos(a), 0, math.sin(a));
                if (s.leader)
                {
                    float3 top = dirXZ * spread + new float3(0, s.H * 0.97f, 0);
                    int n = math.max(4, (int)(s.H * 0.97f / (s.step * 2f)));
                    Chain(k, ground, top, n, ROLE_TRUNK, s.rt / math.sqrt(s.stems), MIN_R + 0.3f, s.H * 0.0015f, ref r);
                }
                else
                {
                    float3 fork = dirXZ * lean * 0.5f + new float3(0, s.forkY, 0);
                    int fn = Chain(k, ground, fork, math.max(2, (int)(s.forkY / (s.step * 2f))), ROLE_TRUNK, s.rt, s.rt * 0.85f, 0.4f, ref r);
                    float a0 = r.Angle();
                    for (int f = 0; f < s.forks; f++)
                    {
                        float az = a0 + f * 6.2831853f / s.forks + r.Range(-0.35f, 0.35f);
                        float el = math.radians(s.limbElev + r.Range(-8, 8));
                        float3 d = new float3(math.sin(el) * math.cos(az), math.cos(el), math.sin(el) * math.sin(az));
                        float L = s.W * 0.5f * s.limbLen * r.Range(0.85f, 1.15f) / math.max(0.35f, math.sin(el));
                        if (sc) L *= 0.8f;
                        float3 end = fork + d * L + new float3(0, s.env == ENV_DOME ? r.Range(5, 15) : 0, 0);
                        Chain(k, fn, end, math.max(3, (int)(L / (s.step * 1.5f))), ROLE_TRUNK, s.rt * 0.6f, s.rt * 0.3f, 0.8f, ref r);
                    }
                    if (s.env == ENV_DOME)   // banyan: a central leader rises into the dome too
                        Chain(k, fn, new float3(0, s.H * 0.75f, 0), 6, ROLE_TRUNK, s.rt * 0.5f, s.rt * 0.2f, 1f, ref r);
                }
            }
            // the base flare (buttress) round the root column
            k.flares.Add(new Flare { b = 0f, r = s.rt, amp = s.flare, h = s.flareH, flutes = s.flutes, phase = r.Angle(), top = s.flareH * 4f });
        }

        // =====================================================================
        // (a) Space colonization
        // =====================================================================
        private static long Key(int3 c) => ((long)(c.x & 0x1FFFFF) << 42) | ((long)(c.y & 0x1FFFFF) << 21) | (long)(c.z & 0x1FFFFF);

        private static void SpaceColonize(Skeleton k, ref Rng r)
        {
            var s = k.sp;
            float R = s.W * 0.5f * 1.4f;
            var pts = new List<float3>(s.points);
            for (int tries = 0; pts.Count < s.points && tries < s.points * 60; tries++)
            {
                var q = new float3(r.Range(-R, R), r.Range(s.cb, s.H), r.Range(-R, R));
                float er = EnvR(in s, q.y) * Lobe(k, q);
                float d = math.length(q.xz);
                if (d > er) continue;
                if (d < s.inner * er && q.y < s.H - (s.H - s.cb) * 0.12f) continue;   // open interior (not under the very top)
                pts.Add(q);
            }
            k.attraction = pts.Count;
            int np = pts.Count;
            var alive = new bool[np];
            for (int i = 0; i < np; i++) alive[i] = true;
            float cell = s.di;
            int3 C(float3 p) => (int3)math.floor(p / cell);
            var pgrid = new Dictionary<long, List<int>>();
            for (int i = 0; i < np; i++) { long kk = Key(C(pts[i])); if (!pgrid.TryGetValue(kk, out var l)) pgrid[kk] = l = new List<int>(); l.Add(i); }
            var ngrid = new Dictionary<long, List<int>>();
            void AddN(int j) { long kk = Key(C(k.nodes[j].p)); if (!ngrid.TryGetValue(kk, out var l)) ngrid[kk] = l = new List<int>(); l.Add(j); }
            void Kill(float3 p)
            {
                int3 c = C(p); float dk2 = s.dk * s.dk;
                for (int dz = -1; dz <= 1; dz++) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    if (pgrid.TryGetValue(Key(c + new int3(dx, dy, dz)), out var l))
                        foreach (int i in l) if (alive[i] && math.distancesq(pts[i], p) < dk2) alive[i] = false;
            }
            for (int j = 0; j < k.nodes.Count; j++) { AddN(j); Kill(k.nodes[j].p); }
            var acc = new List<float3>(); var cnt = new List<int>();
            float di2 = s.di * s.di;
            float3 up = new float3(0, 1, 0);
            int it;
            for (it = 0; it < 400; it++)
            {
                while (acc.Count < k.nodes.Count) { acc.Add(0f); cnt.Add(0); }
                for (int j = 0; j < acc.Count; j++) { acc[j] = 0f; cnt[j] = 0; }
                int live = 0;
                for (int i = 0; i < np; i++)
                {
                    if (!alive[i]) continue;
                    live++;
                    float3 p = pts[i]; int3 c = C(p);
                    int best = -1; float bd = di2;
                    for (int dz = -1; dz <= 1; dz++) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                        if (ngrid.TryGetValue(Key(c + new int3(dx, dy, dz)), out var l))
                            foreach (int j in l)
                            {
                                if (k.nodes[j].role == ROLE_ROOT || k.nodes[j].p.y < s.cb * 0.6f) continue;
                                float d2 = math.distancesq(k.nodes[j].p, p);
                                if (d2 < bd) { bd = d2; best = j; }
                            }
                    if (best >= 0) { acc[best] += math.normalizesafe(p - k.nodes[best].p); cnt[best]++; }
                }
                if (live == 0) break;
                int before = k.nodes.Count, added = 0;
                for (int j = 0; j < before; j++)
                {
                    if (cnt[j] == 0) continue;
                    float3 d = acc[j];
                    if (math.lengthsq(d) < 1e-4f) continue;
                    d = math.normalize(math.normalize(d) + up * s.tropism);
                    var nd = k.nodes[j];
                    if (nd.kids >= 4) continue;
                    // the same growth again (an oscillating pair of points): skip
                    bool dup = false;
                    float3 np2 = nd.p + d * s.step;
                    if (ngrid.TryGetValue(Key(C(np2)), out var l2))
                        foreach (int m in l2) if (math.distancesq(k.nodes[m].p, np2) < 0.25f * s.step * s.step) { dup = true; break; }
                    if (dup) continue;
                    int nn = AddNode(k, j, np2, ROLE_BRANCH);
                    AddN(nn); added++;
                }
                for (int j = before; j < k.nodes.Count; j++) Kill(k.nodes[j].p);
                if (added == 0) break;
            }
            k.iterations = it;
        }

        /// Pipe model over the whole graph (children have larger indices than parents),
        /// scaled so the stem at 1.3 m has the species' trunk radius.
        private static void PipeRadii(Skeleton k)
        {
            var s = k.sp; int n = k.nodes.Count;
            var acc = new double[n];
            for (int i = n - 1; i >= 0; i--)
            {
                var nd = k.nodes[i];
                double ri = nd.kids == 0 ? 1.0 : math.pow(acc[i], 1.0 / s.pipe);
                nd.r = (float)ri; k.nodes[i] = nd;
                if (nd.parent >= 0) acc[nd.parent] += math.pow(ri, s.pipe);
            }
            float rRef = k.nodes[1].r / math.sqrt(math.max(1, s.stems));   // node 1 = the trunk at the ground
            float scale = s.rt / math.max(1e-3f, rRef);
            for (int i = 0; i < n; i++)
            {
                var nd = k.nodes[i];
                float t = math.saturate(nd.p.y / s.H);
                float taper = nd.role == ROLE_TRUNK ? 1f - 0.35f * t : 1f;   // a stem tapers a little even between forks
                nd.r = math.max(MIN_R, nd.r * scale * taper);
                k.nodes[i] = nd;
            }
        }

        // =====================================================================
        // (b) L-system / rule-based recursive branching
        // =====================================================================
        private static float3 Perp(float3 d) => math.normalize(math.abs(d.y) < 0.9f ? math.cross(d, new float3(0, 1, 0)) : math.cross(d, new float3(1, 0, 0)));
        private static float3 Rot(float3 v, float3 axis, float ang)
        {
            float c = math.cos(ang), sn = math.sin(ang);
            return v * c + math.cross(axis, v) * sn + axis * math.dot(axis, v) * (1f - c);
        }

        private static void LSystem(Skeleton k, ref Rng r)
        {
            var s = k.sp;
            int root = AddNode(k, -1, new float3(0, -ROOT_DEPTH, 0), ROLE_TRUNK, s.rt);
            int ground = AddNode(k, root, 0f, ROLE_TRUNK, s.rt);
            k.flares.Add(new Flare { b = 0f, r = s.rt, amp = s.flare, h = s.flareH, flutes = s.flutes, phase = r.Angle(), top = s.flareH * 4f });
            float la = r.Angle();
            float phyl = math.radians(137.5f);
            for (int st = 0; st < s.stems; st++)
            {
                float a = la + st * 6.2831853f / s.stems;
                float3 lean = s.stems > 1 ? new float3(math.cos(a), 0, math.sin(a)) * s.H * r.Range(0.06f, 0.12f) : new float3(math.cos(a), 0, math.sin(a)) * s.H * r.Range(0f, 0.03f);
                float rt = s.rt / math.sqrt(s.stems);
                float topY = s.leader ? s.H * 0.97f : s.forkY;
                int nseg = math.max(4, (int)(topY / 10f));
                var chain = new List<int> { ground };
                int cur = ground;
                for (int i = 1; i <= nseg; i++)
                {
                    float t = (float)i / nseg;
                    float3 p = lean * t + new float3(r.Range(-0.5f, 0.5f), topY * t, r.Range(-0.5f, 0.5f));
                    cur = AddNode(k, cur, p, ROLE_TRUNK, math.max(MIN_R, rt * (1f - 0.75f * t * (s.leader ? 1f : 0.3f))));
                    chain.Add(cur);
                }
                // level-1 off the stem: whorls (leader) or laterals under the fork
                float roll = r.Angle();
                int n1 = s.leader ? s.n1 / s.stems : s.n1;
                float y0 = s.cb, y1 = s.leader ? s.H * 0.95f : s.forkY;
                for (int b = 0; b < n1; b++)
                {
                    float f = (b + r.Range(0.2f, 0.8f)) / math.max(1, n1);
                    float y = math.lerp(y0, y1, f);
                    int at = chain[math.clamp((int)(y / topY * nseg), 1, chain.Count - 1)];
                    roll += phyl + r.Range(-0.3f, 0.3f);
                    float3 axis = Rot(Perp(new float3(0, 1, 0)), new float3(0, 1, 0), roll);
                    float down = math.radians(s.down1 + r.Range(-10, 10));
                    float3 d = Rot(new float3(0, 1, 0), axis, down);
                    float reach = math.max(EnvR(in s, y) * Lobe(k, d), s.W * 0.08f);
                    float L = reach / math.max(0.4f, math.sin(down)) * r.Range(0.85f, 1.1f);
                    Limb(k, 1, at, d, L, k.nodes[at].r * 0.55f, ref r);
                }
                if (!s.leader)   // the fork: big ascending limbs into the crown
                {
                    float a0 = r.Angle();
                    for (int f = 0; f < s.forks; f++)
                    {
                        float az = a0 + f * 6.2831853f / s.forks + r.Range(-0.35f, 0.35f);
                        float el = math.radians(s.limbElev + r.Range(-8, 8));
                        float3 d = new float3(math.sin(el) * math.cos(az), math.cos(el), math.sin(el) * math.sin(az));
                        float L = s.W * 0.5f * s.limbLen * 1.5f * r.Range(0.85f, 1.15f) / math.max(0.4f, math.sin(el) + 0.25f);
                        if (s.env == ENV_LENS) L = (s.H - s.forkY) / math.max(0.5f, math.cos(el)) * 0.9f;
                        Limb(k, 1, cur, d, L, rt * 0.62f, ref r);
                    }
                }
            }
        }

        private static void Limb(Skeleton k, int level, int parent, float3 dir, float len, float r0, ref Rng r)
        {
            var s = k.sp;
            float segL = level == 1 ? 9f : level == 2 ? 6f : 4f;
            int nseg = math.max(2, (int)(len / segL));
            float3 d = dir, p = k.nodes[parent].p;
            var chain = new List<int>(nseg);
            int cur = parent;
            float3 up = new float3(0, 1, 0);
            for (int i = 1; i <= nseg; i++)
            {
                float t = (float)i / nseg;
                d = math.normalize(d + up * (s.tropism * 1.2f) + new float3(r.Range(-1, 1), r.Range(-1, 1), r.Range(-1, 1)) * s.curve * 0.5f);
                if (s.env == ENV_LENS || s.env == ENV_DOME)   // flat crowns: limbs level out near the crown top
                {
                    float over = (p.y - (s.H - (s.H - s.cb) * 0.3f)) / math.max(1f, s.H - s.cb);
                    if (over > 0f) d = math.normalize(d - up * over * 2f);
                }
                p += d * (len / nseg);
                cur = AddNode(k, cur, p, ROLE_BRANCH, math.max(MIN_R, math.lerp(r0, r0 * 0.4f, t)));
                chain.Add(cur);
            }
            int nKids = level == 1 ? s.n2 : level == 2 ? s.n3 : 0;
            float ratio = level == 1 ? s.ratio2 : s.ratio3;
            if (nKids == 0 || len * ratio < 6f) { AddClump(k, k.nodes[cur], ref r); return; }
            float roll = r.Angle();
            for (int c = 0; c < nKids; c++)
            {
                float f = 0.25f + 0.75f * (c + r.Range(0.2f, 0.8f)) / nKids;
                int at = chain[math.clamp((int)(f * nseg), 0, chain.Count - 1)];
                float3 pd = k.nodes[at].dir;
                roll += math.radians(137.5f) + r.Range(-0.4f, 0.4f);
                float3 axis = Rot(Perp(pd), pd, roll);
                float down = math.radians(s.down2 + r.Range(-12, 12));
                float3 cd = Rot(pd, axis, down);
                Limb(k, level + 1, at, cd, len * ratio * (1.15f - 0.6f * f) * r.Range(0.8f, 1.15f), math.max(MIN_R, k.nodes[at].r * 0.6f), ref r);
            }
            AddClump(k, k.nodes[cur], ref r);
        }

        // =====================================================================
        // (c) Template + variation: an authored limb list per species, jittered
        // =====================================================================
        private static void Template(Skeleton k, ref Rng r)
        {
            var s = k.sp;
            int root = AddNode(k, -1, new float3(0, -ROOT_DEPTH, 0), ROLE_TRUNK, s.rt);
            int ground = AddNode(k, root, 0f, ROLE_TRUNK, s.rt);
            k.flares.Add(new Flare { b = 0f, r = s.rt, amp = s.flare, h = s.flareH, flutes = s.flutes, phase = r.Angle(), top = s.flareH * 4f });
            float bigR = s.clumpR * 2.2f;
            if (s.leader)   // conifers: whorls of straight limbs, one long clump each
            {
                int top = Chain(k, ground, new float3(0, s.H * 0.97f, 0), 10, ROLE_TRUNK, s.rt, MIN_R + 0.5f, 0.2f, ref r);
                for (float y = s.cb; y < s.H * 0.92f; y += 14f * r.Range(0.9f, 1.1f))
                {
                    float rr = EnvR(in s, y); if (rr < 4f) continue;
                    int at = NearestTrunk(k, y);
                    float a0 = r.Angle();
                    for (int b = 0; b < 5; b++)
                    {
                        float az = a0 + b * 6.2831853f / 5 + r.Range(-0.2f, 0.2f);
                        float3 d = math.normalize(new float3(math.cos(az), -0.25f, math.sin(az)));
                        float3 end = k.nodes[at].p + d * rr;
                        int e = Chain(k, at, end, 2, ROLE_BRANCH, math.max(MIN_R, k.nodes[at].r * 0.3f), MIN_R, 0f, ref r);
                        float3 mid = math.lerp(k.nodes[at].p, end, 0.6f);
                        k.clumps.Add(new Clump { c = mid + new float3(0, 2, 0), rad = new float3(rr * 0.45f, s.clumpR * 0.8f, rr * 0.45f) });
                    }
                }
                return;
            }
            int fork = Chain(k, ground, new float3(0, s.forkY, 0), 3, ROLE_TRUNK, s.rt, s.rt * 0.85f, 0.3f, ref r);
            int K = s.env == ENV_DOME ? 6 : s.env == ENV_LENS ? 4 : s.env == ENV_OVAL ? 4 : 5;
            float elev = s.env == ENV_DOME ? 82f : s.env == ENV_LENS ? 45f : s.env == ENV_OVAL ? 25f : 50f;
            float a00 = r.Angle();
            for (int i = 0; i < K; i++)
            {
                float az = a00 + i * 6.2831853f / K + math.radians(r.Range(-12, 12));
                float el = math.radians(elev + r.Range(-8, 8));
                float3 d = new float3(math.sin(el) * math.cos(az), math.cos(el), math.sin(el) * math.sin(az));
                float L = s.W * 0.5f * 0.62f * r.Range(0.9f, 1.1f) / math.max(0.45f, math.sin(el));
                if (s.env == ENV_LENS) L = (s.H - s.forkY) / math.max(0.5f, math.cos(el)) * 0.8f;
                float3 end = k.nodes[fork].p + d * L;
                int e = Chain(k, fork, end, 3, ROLE_BRANCH, s.rt * 0.5f, s.rt * 0.25f, 0.5f, ref r);
                for (int f = -1; f <= 1; f++)
                {
                    float3 sd = math.normalize(Rot(d, new float3(0, 1, 0), math.radians(35f * f)) + new float3(0, 0.35f, 0));
                    float3 se = end + sd * L * 0.35f;
                    Chain(k, e, se, 2, ROLE_BRANCH, math.max(MIN_R, s.rt * 0.2f), MIN_R, 0.2f, ref r);
                    k.clumps.Add(new Clump { c = se + new float3(0, bigR * 0.3f, 0), rad = new float3(bigR, bigR * s.clumpFlat, bigR) * r.Range(0.85f, 1.15f) });
                }
                if (s.aerial)   // fixed prop roots at 0.5 and 0.9 of each limb
                    for (int pr = 0; pr < 2; pr++)
                    {
                        float3 at = math.lerp(k.nodes[fork].p, end, pr == 0 ? 0.5f : 0.9f);
                        k.pendingRoots.Add(at);
                    }
            }
        }

        private static int NearestTrunk(Skeleton k, float y)
        {
            int best = 1; float bd = float.MaxValue;
            for (int i = 0; i < k.nodes.Count; i++)
                if (k.nodes[i].role == ROLE_TRUNK) { float d = math.abs(k.nodes[i].p.y - y); if (d < bd) { bd = d; best = i; } }
            return best;
        }

        // =====================================================================
        // Banyan aerial roots: drop from the main limbs; some thicken into prop trunks
        // =====================================================================
        private static void AerialRoots(Skeleton k, ref Rng r, Ground ground, bool world)
        {
            var s = k.sp;
            var picks = new List<float3>(k.pendingRoots);
            if (picks.Count == 0)
            {
                var taken = new HashSet<long>();
                for (int i = 2; i < k.nodes.Count && picks.Count < 40; i++)
                {
                    var nd = k.nodes[i];
                    float h = math.length(nd.p.xz);
                    if (nd.r < 1.8f || h < 40f || nd.p.y < 30f || nd.p.y > s.H * 0.8f) continue;
                    long cellKey = Key(new int3((int)math.floor(nd.p.x / 32f), 0, (int)math.floor(nd.p.z / 32f)));
                    if (taken.Contains(cellKey) || !r.Chance(0.55f)) continue;
                    taken.Add(cellKey);
                    picks.Add(nd.p);
                }
            }
            foreach (var top in picks)
            {
                int gx = (int)math.round(top.x), gz = (int)math.round(top.z);
                float gy = ground(gx, gz);
                float drop = top.y - gy;
                if (drop < 10f) continue;
                bool prop = r.Chance(k.method == M_TEMPLATE ? 1f : 0.4f);
                k.aerialRoots++;
                float rBot = prop ? r.Range(3.5f, 6.5f) : r.Range(MIN_R, 1.3f);
                float rTop = prop ? math.max(MIN_R, rBot * 0.55f) : MIN_R;
                float reach = prop || r.Chance(0.5f) ? 1f : r.Range(0.3f, 0.7f);   // some thin ones still hang in the air
                int parent = AddNode(k, -1, top, ROLE_ROOT, rTop);
                int n = math.max(3, (int)(drop * reach / 12f));
                float3 sway = new float3(r.Range(-1, 1), 0, r.Range(-1, 1)) * 0.25f;
                int cur = parent;
                for (int i = 1; i <= n; i++)
                {
                    float t = (float)i / n;
                    float3 p = new float3(top.x, top.y - drop * reach * t, top.z) + sway * math.sin(t * 3.14159f) * 6f;
                    if (reach >= 1f && i == n) p.y = gy - (world ? ROOT_DEPTH : 6f);   // into the ground
                    cur = AddNode(k, cur, p, ROLE_ROOT, math.lerp(rTop, rBot, t * t));
                }
                if (prop)
                {
                    k.propRoots++;
                    k.flares.Add(new Flare { b = new float3(top.x, gy, top.z), r = rBot, amp = 0.7f, h = 6f, flutes = 4, phase = r.Angle(), top = 24f });
                }
            }
        }

        // =====================================================================
        // Segments + leaf clumps from the node graph
        // =====================================================================
        private static void AddClump(Skeleton k, Node tip, ref Rng r)
        {
            var s = k.sp;
            float R = s.clumpR * r.Range(0.8f, 1.2f);
            float3 c = tip.p + tip.dir * R * 0.3f + new float3(0, R * 0.2f, 0);
            k.clumps.Add(new Clump { c = c, rad = new float3(R, R * s.clumpFlat, R) });
        }

        private static void Finish(Skeleton k, ref Rng r)
        {
            int n = k.nodes.Count;
            var maxKid = new float[n];
            for (int i = 0; i < n; i++) { var nd = k.nodes[i]; if (nd.parent >= 0) maxKid[nd.parent] = math.max(maxKid[nd.parent], nd.r); }
            bool sc = k.method == M_SPACE;
            for (int i = 0; i < n; i++)
            {
                var nd = k.nodes[i];
                if (nd.parent < 0) continue;
                var pn = k.nodes[nd.parent];
                float ra = nd.r >= maxKid[nd.parent] - 1e-3f ? pn.r : nd.r;
                if (nd.role == ROLE_ROOT) ra = pn.r;
                k.segs.Add(new Seg { a = pn.p, b = nd.p, ra = ra, rb = nd.r });
                if (sc && nd.kids == 0 && nd.role == ROLE_BRANCH) AddClump(k, nd, ref r);
            }
            if (k.sp.leader && k.method != M_TEMPLATE)   // the leader's tip carries a small crown top, not a bare spike
            {
                for (int i = 0; i < n; i++)
                {
                    var t = k.nodes[i];
                    if (t.role != ROLE_TRUNK || t.p.y < k.sp.H * 0.84f) continue;
                    t.dir = new float3(0, 1, 0); AddClump(k, t, ref r);
                }
            }
        }

        // =====================================================================
        // The voxel volume: 8^3 bricks over the tree's box
        // =====================================================================
        public sealed class Vol
        {
            public readonly int3 lo, nb;          // lo: brick-aligned local voxel; nb: bricks per axis
            public readonly int[] index;          // brick -> slot in `bricks`, -1 = empty
            public readonly List<byte[]> bricks = new List<byte[]>();
            public int3 hiUsed = int.MinValue, loUsed = int.MaxValue;

            public Vol(int3 vlo, int3 vhi)
            {
                lo = (vlo >> 3) << 3;
                nb = ((vhi - lo) >> 3) + 1;
                index = new int[nb.x * nb.y * nb.z];
                for (int i = 0; i < index.Length; i++) index[i] = -1;
            }

            public int BrickIndex(int3 v, out int off)
            {
                int3 l = v - lo; off = 0;
                if (math.any(l < 0) || math.any(l >= nb * 8)) return -1;
                int3 b = l >> 3, w = l & 7;
                off = (w.y << 6) | (w.z << 3) | w.x;
                return (b.y * nb.z + b.z) * nb.x + b.x;
            }

            public byte Get(int3 v)
            {
                int bi = BrickIndex(v, out int off);
                if (bi < 0) return 0;
                int sl = index[bi];
                return sl < 0 ? (byte)0 : bricks[sl][off];
            }

            public bool SetIfEmpty(int3 v, byte m)
            {
                int bi = BrickIndex(v, out int off);
                if (bi < 0) return false;
                int sl = index[bi];
                if (sl < 0) { sl = index[bi] = bricks.Count; bricks.Add(new byte[512]); }
                var b = bricks[sl];
                if (b[off] != 0) return false;
                b[off] = m;
                hiUsed = math.max(hiUsed, v); loUsed = math.min(loUsed, v);
                return true;
            }

            public void Clear(int3 v)
            {
                int bi = BrickIndex(v, out int off);
                if (bi < 0) return;
                int sl = index[bi];
                if (sl >= 0) bricks[sl][off] = 0;
            }

            /// Drop the bricks that ended empty (after the crumb filter); renumber.
            public void Compact()
            {
                var keep = new List<byte[]>();
                for (int i = 0; i < index.Length; i++)
                {
                    int sl = index[i];
                    if (sl < 0) continue;
                    var b = bricks[sl]; bool any = false;
                    for (int j = 0; j < 512; j++) if (b[j] != 0) { any = true; break; }
                    if (!any) { index[i] = -1; continue; }
                    index[i] = keep.Count; keep.Add(b);
                }
                bricks.Clear(); bricks.AddRange(keep);
            }
        }

        public static Vol Voxelise(Skeleton k, out Stats st)
        {
            var s = k.sp;
            st = default;
            float3 lo = float.MaxValue, hi = float.MinValue;
            foreach (var g in k.segs) { float r = math.max(g.ra, g.rb) + 1; lo = math.min(lo, math.min(g.a, g.b) - r); hi = math.max(hi, math.max(g.a, g.b) + r); }
            foreach (var c in k.clumps) { lo = math.min(lo, c.c - c.rad * 1.4f); hi = math.max(hi, c.c + c.rad * 1.4f); }
            foreach (var f in k.flares) { float r = f.r * (1 + f.amp) * 1.5f + 2; lo = math.min(lo, f.b - new float3(r, ROOT_DEPTH, r)); hi = math.max(hi, f.b + new float3(r, f.top, r)); }
            var v = new Vol((int3)math.floor(lo) - 2, (int3)math.ceil(hi) + 2);
            byte wood = s.wood, leaf = s.leaf;
            // 1. wood: every segment (a tapered capsule), then the flares
            foreach (var g in k.segs)
            {
                float r = math.max(g.ra, g.rb);
                int3 a0 = (int3)math.floor(math.min(g.a, g.b) - r - 1), a1 = (int3)math.ceil(math.max(g.a, g.b) + r + 1);
                float3 ab = g.b - g.a; float ll = math.max(math.dot(ab, ab), 1e-6f);
                for (int y = a0.y; y <= a1.y; y++)
                for (int z = a0.z; z <= a1.z; z++)
                for (int x = a0.x; x <= a1.x; x++)
                {
                    float3 q = new float3(x, y, z);
                    float t = math.clamp(math.dot(q - g.a, ab) / ll, 0f, 1f);
                    float rr = math.lerp(g.ra, g.rb, t);
                    if (math.distancesq(q, g.a + ab * t) <= rr * rr) v.SetIfEmpty(new int3(x, y, z), wood);
                }
            }
            foreach (var f in k.flares)
            {
                float rMax = f.r * (1 + f.amp) * 1.45f + 1;
                for (int y = (int)f.b.y - 8; y <= (int)(f.b.y + f.top); y++)
                {
                    float yy = math.max(0f, y - f.b.y);
                    float e = math.exp(-yy / f.h);
                    for (int z = (int)math.floor(f.b.z - rMax); z <= (int)math.ceil(f.b.z + rMax); z++)
                    for (int x = (int)math.floor(f.b.x - rMax); x <= (int)math.ceil(f.b.x + rMax); x++)
                    {
                        float dx = x - f.b.x, dz = z - f.b.z;
                        float th = math.atan2(dz, dx);
                        float flute = f.flutes > 0 ? math.pow(0.5f + 0.5f * math.cos(f.flutes * th + f.phase), 3f) - 0.25f : 0f;
                        float rr = f.r * (1f + f.amp * e) * (1f + 0.9f * f.amp * e * flute);
                        if (dx * dx + dz * dz <= rr * rr) v.SetIfEmpty(new int3(x, y, z), wood);
                    }
                }
            }
            // 2. leaves: lumpy clumps, porous inside (the air gaps), fill AIR only
            uint ps = Ores.Hash(k.seed, 5, 5, 5), ps2 = Ores.Hash(k.seed, 6, 6, 6), es = Ores.Hash(k.seed, 7, 7, 7);
            foreach (var c in k.clumps)
            {
                int3 a0 = (int3)math.floor(c.c - c.rad * 1.35f), a1 = (int3)math.ceil(c.c + c.rad * 1.35f);
                for (int y = a0.y; y <= a1.y; y++)
                for (int z = a0.z; z <= a1.z; z++)
                for (int x = a0.x; x <= a1.x; x++)
                {
                    float3 q = new float3(x, y, z);
                    float3 d = (q - c.c) / c.rad;
                    float s2 = math.dot(d, d);
                    if (s2 > 1.8f) continue;
                    if (s2 > 0.3f && s2 + 0.75f * Trees.EdgeNoise(q * 1.4f, es) > 1f) continue;
                    float hole = (Ores.Hash(ps, x >> 1, y >> 1, z >> 1) & 0xFFFF) / 65535f * 0.7f + (Ores.Hash(ps2, x, y, z) & 0xFF) / 255f * 0.3f;
                    if (hole > s.density) continue;
                    v.SetIfEmpty(new int3(x, y, z), leaf);
                }
            }
            // 3. crumbs: leaf voxels not 6-connected to the wood go (no floating leaves)
            st.crumbs = CrumbFilter(v, wood);
            v.Compact();
            // 4. stats
            for (int i = 0; i < v.bricks.Count; i++)
            {
                var b = v.bricks[i];
                for (int j = 0; j < 512; j++) { if (b[j] == 0) continue; if (b[j] == leaf) st.leaf++; else st.trunk++; }
            }
            st.segs = k.segs.Count; st.clumps = k.clumps.Count; st.nodes = k.nodes.Count; st.bricks = v.bricks.Count;
            st.bytes = (long)v.bricks.Count * 512 + (long)v.index.Length * 4;
            st.attraction = k.attraction; st.iterations = k.iterations; st.aerial = k.aerialRoots; st.props = k.propRoots;
            st.size = v.hiUsed - v.loUsed + 1; st.top = v.hiUsed.y;
            return v;
        }

        private static int CrumbFilter(Vol v, byte wood)
        {
            int nx = v.nb.x * 8, ny = v.nb.y * 8, nz = v.nb.z * 8;
            var seen = new System.Collections.BitArray(nx * ny * nz);
            int Lin(int3 l) => (l.y * nz + l.z) * nx + l.x;
            var stack = new Stack<int3>();
            for (int bi = 0; bi < v.index.Length; bi++)
            {
                int sl = v.index[bi]; if (sl < 0) continue;
                int bx = bi % v.nb.x, bz = (bi / v.nb.x) % v.nb.z, by = bi / (v.nb.x * v.nb.z);
                var b = v.bricks[sl];
                for (int j = 0; j < 512; j++)
                {
                    if (b[j] != wood) continue;
                    int3 l = new int3((bx << 3) | (j & 7), (by << 3) | (j >> 6), (bz << 3) | ((j >> 3) & 7));
                    int li = Lin(l);
                    if (seen[li]) continue;
                    seen[li] = true; stack.Push(l);
                }
            }
            int3[] dirs = { new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 1, 0), new int3(0, -1, 0), new int3(0, 0, 1), new int3(0, 0, -1) };
            while (stack.Count > 0)
            {
                int3 l = stack.Pop();
                foreach (var d in dirs)
                {
                    int3 m = l + d;
                    if (m.x < 0 || m.y < 0 || m.z < 0 || m.x >= nx || m.y >= ny || m.z >= nz) continue;
                    int li = Lin(m);
                    if (seen[li]) continue;
                    if (v.Get(m + v.lo) == 0) continue;
                    seen[li] = true; stack.Push(m);
                }
            }
            int removed = 0;
            for (int bi = 0; bi < v.index.Length; bi++)
            {
                int sl = v.index[bi]; if (sl < 0) continue;
                int bx = bi % v.nb.x, bz = (bi / v.nb.x) % v.nb.z, by = bi / (v.nb.x * v.nb.z);
                var b = v.bricks[sl];
                for (int j = 0; j < 512; j++)
                {
                    if (b[j] == 0) continue;
                    int3 l = new int3((bx << 3) | (j & 7), (by << 3) | (j >> 6), (bz << 3) | ((j >> 3) & 7));
                    if (!seen[Lin(l)]) { b[j] = 0; removed++; }
                }
            }
            return removed;
        }

        /// Today's tree (Trees.Build's primitives) as a volume local to its root, for the
        /// side-by-side ("today at the same framing").
        public static unsafe Vol FromPrims(TreePrim* p, int n, int3 root, out Stats st)
        {
            st = default;
            int3 lo = int.MaxValue, hi = int.MinValue;
            for (int i = 0; i < n; i++) { lo = math.min(lo, p[i].lo); hi = math.max(hi, p[i].hi); }
            var v = new Vol(lo - root, hi - root);
            // Trees' precedence: every trunk primitive, then every leaf one; the first containing wins (= SetIfEmpty in that order)
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < n; i++)
            {
                if (p[i].leaf != pass) continue;
                for (int y = p[i].lo.y; y <= p[i].hi.y; y++)
                for (int z = p[i].lo.z; z <= p[i].hi.z; z++)
                for (int x = p[i].lo.x; x <= p[i].hi.x; x++)
                {
                    var w = new int3(x, y, z);
                    if (Trees.Contains(in p[i], w)) v.SetIfEmpty(w - root, p[i].mat);
                }
            }
            v.Compact();
            for (int i = 0; i < v.bricks.Count; i++)
                foreach (byte b in v.bricks[i]) { if (b == 0) continue; if (GenMat.IsTreeLeaf(b)) st.leaf++; else st.trunk++; }
            st.segs = n; st.bricks = v.bricks.Count; st.bytes = (long)v.bricks.Count * 512 + (long)v.index.Length * 4;
            st.size = v.hiUsed - v.loUsed + 1; st.top = v.hiUsed.y;
            return v;
        }
    }
}
