// Assets/TerrainGen/Routes.cs
//
// ROUTES BETWEEN THE EASY AREAS (map v4, surface rebuild B8, plan rule 9; owner
// Q3 (a), Q4 (a), Q5 (a), 2026-10-02). Owner's words: routes must NOT look
// constructed -- it should feel like there are several options; natural-looking
// routes with a landmark in sight the player may be trying to reach. B7 (towns)
// is not done, so routes join EASY AREAS only.
//
//   1. Cost (on the first record: WorldMap.BuildV4 builds it, runs this, then
//      builds the record again on the eased map, keeping these easy areas):
//      each 8 m cell costs BAND_COST[band] x (1 + base danger) -- Walk 1,
//      Scramble 2, Climb 6, Sheer 30. Sheer is allowed (a route never fails),
//      only expensive. The S-level stamps and the geyser pools (voxel-only
//      steps) cost Sheer, so a route goes round them. A lake
//      costs as Sheer (swimming), a river cell RIVER_MUL x its ground (a ford,
//      not a towpath), a signature feature's footprint FEATURE_MUL x (a route
//      goes past a landmark, not over it). Sea is closed. A step costs the two
//      cells' mean x the 5/7 chamfer length: integers only, ties by cell index.
//   2. Search: one multi-source Dijkstra per PAIR of easy areas over the 8 m
//      map (fast enough for the build gate, so no 16 m grid), from every cell
//      of one area to the first cell of the other; a third area is not
//      crossed, so a link A-C never runs through B. Q5 (a), "the next waypoint
//      in sight": a cell from which neither end area can be seen (Viewshed,
//      2 m eye) costs SIGHT_UNSEEN / SIGHT_SEEN more, so routes keep to the
//      open valleys and slopes that look at where they go.
//   3. Links (Q3 (a)): Kruskal's spanning tree by path cost, then every area
//      with fewer than two links takes its cheapest further link, each priced
//      up by the share of it that runs within OVERLAP_R of a route already
//      taken -- a second way out that leaves by the first one's corridor is
//      not a second way.
//   4. Centre line: the cell path smoothed (SMOOTH passes of a 5-tap filter,
//      ends fixed), so it bends with the ground instead of the grid's 0/45/90.
//   5. Easing (Q4 (a)), after the hydrology, on dry cells only:
//        profile    along the centre line the ground is held under PASS_SLOPE:
//                   the lowest PASS_SLOPE-Lipschitz curve under it cuts the
//                   crests, at most CUT_MAX (20 m) -- read on the line AND on
//                   the cells within the core beside it; only where that cap
//                   leaves a step is the step's foot raised until the line is
//                   under PASS_SLOPE again -- a talus ramp (each cell's fill
//                   is capped at FILL_MAX, 25 m; the cones use the uncapped
//                   line, so two of them never disagree);
//        sideways   every cell near the line is held inside a FLARED CONE
//                   round the eased line height: PASS_SLOPE out to CORE x the
//                   half width, then a slope that grows by FLARE, so a pass is
//                   a saddle and a hillside crossing a soft bench, each dying
//                   out by itself where the land is no steeper than the cone
//                   (cut <= CUT_MAX, fill <= FILL_MAX);
//        roughness  the map's detail amplitude eased to ROUTE_ROUGH in the
//                   corridor's middle, fading out from a noise-moved start to
//                   ROUGH_EDGE past the half width.
//      The corridor's half width wanders HALF_W x (1 +- WIDTH_WOBBLE) along the
//      route (60 m wide on average). Never touched: lake and river cells, easy
//      cells and a one-cell collar round them, signature-feature footprints;
//      a dry cell within two cells of water stays WATER_MARGIN above that
//      water (the voxel water column reads the 4 nearest cells' water) and
//      every cut cell stays LAND_MIN above sea. The edge wobbles by a noise;
//      the core does not.
//   6. Passes: centre-line crests standing PASS_PROMINENCE above the lowest
//      ground before a higher point on both sides; with the destination area
//      and the features near the route they are the waypoints (Q5 (a); the
//      record lists them so placement can add cairns or ruins later).
//   7. Mark (WorldRecord.Build, after Danger.Mark): every cell within the half
//      width of a centre line gets FLAG_ROUTE and its base danger one tier
//      lower (never below 0); the land beside keeps its full danger.
//
// Determinism: integer path costs and keys; +, -, *, /, sqrt, min/max and 2-D
// noise on the map's floats (TerrainSpec.cs contract). The routes are a pure
// function of the map, rebuilt (never saved) like the rest of the record.
using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace TerrainGen
{
    public sealed class Route
    {
        public int mapVersion;
        public int areaA, areaB;          // indices in the record's EasyAreaList (both builds share it)
        public int2 areaCentreA, areaCentreB;   // those areas' seeded centres, voxels
        public bool tree;                 // a spanning-tree link (else an extra link)
        public long cost;                 // path cost (step units)
        public float overlap;             // share of the path within OVERLAP_R of an earlier route
        public int[] cells;               // the search's cell path, A to B
        public float2[] line;             // the smoothed centre line, voxels, A to B
        public float[] halfW;             // corridor half width per line point, voxels
        public float[] cut;               // pass cut per line point, voxels
        public float[] fill;              // scree fill per line point (only under a capped cut), voxels
        public byte[] bandBefore;         // the first record's band per line point (before easing)
        public int[] passes;              // line indices of the passes, A to B
        public float widthM;              // mean corridor width, metres
        public float lengthM, straightM;  // centre-line length; straight distance end to end
        public int2 endA, endB;           // voxels
        public int[] corridor;            // flagged cells (FLAG_ROUTE), this route's own
    }

    public static class Routes
    {
        /// Band costs (plan rule 9): Walk 1, Scramble 2, Climb 6, Sheer 30.
        public static readonly int[] BAND_COST = { 1, 2, 6, 30 };
        public const int RIVER_MUL = 4;
        public const int FEATURE_MUL = 3;
        public const int ORTHO = 5, DIAG = 7;           // chamfer step lengths
        public const float HALF_W = 300f;               // voxels: 30 m, a 60 m corridor
        public const float WIDTH_WOBBLE = 0.3f;
        public const float CUT_MAX = 200f;              // voxels: a pass is lowered 20 m at most
        public const float FILL_MAX = 250f;             // voxels: a scree ramp under a capped cut, 25 m at most
        public const float PASS_SLOPE = 0.4f;           // tan ~22 deg along the line, under Scramble's 30
        /// The flared cone: within CORE x the half width of the centre line the
        /// ground may stand at most PASS_SLOPE per unit of distance off the eased
        /// centre-line height; past it the allowed slope grows by FLARE per voxel
        /// (0.4 -> 1.2, ~50 deg, over 30 m), so the easing dies out by itself
        /// where the land is no steeper than that -- a saddle or a bench with
        /// no edge of its own. Cells farther than CORE + FLARE_REACH are not looked at.
        public const float CORE = 0.5f;
        public const float FLARE = 0.8f / 300f;
        public const float FLARE_REACH = 220f;
        public const float ROUTE_ROUGH = 0.25f;         // detail amplitude inside (an easy area's is 0.2)
        public const float ROUGH_EDGE = 80f;            // voxels the roughness easing fades over past the half width
        public const float WATER_MARGIN = 5f;           // voxels over nearby water a cut cell keeps
        public const float LAND_MIN = 20f;              // voxels over the sea a cut cell keeps
        public const float PASS_PROMINENCE = 100f;      // voxels: a crest 10 m over both sides is a pass
        public const float OVERLAP_R = 600f;            // voxels: 60 m
        public const int SMOOTH = 3;
        /// Q5 (a): a cell that sees neither end area (from a 2 m eye to its
        /// ground + 2 m, within SIGHT_RANGE cells of SIGHT_TARGETS of its cells)
        /// costs SIGHT_UNSEEN / SIGHT_SEEN = 1.5x, so a route keeps its goal in
        /// view where the land lets it.
        public const int SIGHT_TARGETS = 6, SIGHT_RANGE = 300;
        public const int SIGHT_SEEN = 2, SIGHT_UNSEEN = 3;
        public const float EYE = 20f;
        /// Voxels round a geyser pool's centre a route keeps out of (its radius
        /// is at most 110: the 2.5 m rim, plus one 8 m cell).
        public const float POOL_KEEP_OUT = 190f;

        /// Voxels round an S-level stamp's site a route keeps out of: the
        /// stamp's reach in Stamps.Column plus one 8 m cell; 0 = a map-level
        /// stamp (the crater's bowl is in the map, so in the cost already).
        /// A fossil reaches past its skull (200): ribs out to 1,000 along +dir,
        /// +-240 across (Stamps.Column), so its disc is centred 400 along the
        /// axis (B10: a re-routed seed 42 link ran under the ribs 64 m from
        /// the skull, which the old 280 round the site missed).
        public static float StampKeepOut(byte kind) =>
            kind == Stamps.CaveMouth ? 150f : kind == Stamps.Fissure ? 680f : kind == Stamps.Arch ? 330f
          : kind == Stamps.Hoop ? 280f : kind == Stamps.StoneRing ? 360f : kind == Stamps.CrystalCluster ? 300f
          : kind == Stamps.Fossil ? 730f : 0f;

        /// The centre of StampKeepOut's disc: the site, or a fossil's rib cage.
        public static float2 StampKeepOutCentre(in Stamps.Stamp s) =>
            s.kind == Stamps.Fossil ? s.pos + s.dir * 400f : s.pos;

        /// Diagnostics of the last Build.
        public static string LastBuild = "";
        /// Test aid: when set, Build keeps every map cell's height before the
        /// easing and its cone bounds (DiagH0/Lo/Hi; NaN = not looked at,
        /// DiagSkip = not easable). Off in the game.
        public static bool Diagnose;
        public static float[] DiagH0, DiagLo, DiagHi; public static bool[] DiagSkip;

        /// Finds the links on the first record, eases the map (Height, Rough)
        /// in place and returns the routes. Null when the record has fewer than
        /// two easy areas.
        public static Route[] Build(WorldMap map, WorldRecord rec)
        {
            var areas = rec.EasyAreaList;
            if (areas == null || areas.Length < 2) { LastBuild = "fewer than two easy areas"; return null; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var spec = map.Spec;
            int n = spec.mapCells, N = n * n, A = areas.Length;
            float cv = spec.mapCellVoxels;

            // ---- 1. cell costs; area membership ----
            var cost = new int[N];
            var area = new byte[N];
            for (int i = 0; i < N; i++)
            {
                var c = rec.Data[i];
                if (map.Height[i] <= 0f || c.IsSea) continue;   // closed
                int k = BAND_COST[c.MoveBand] * (1 + c.Danger);
                byte wc = map.WaterClass[i];
                if (wc == Hydrology.LAKE) k = BAND_COST[Danger.Sheer] * (1 + c.Danger);
                else if (wc == Hydrology.RIVER) k *= RIVER_MUL;
                if (c.IsFeature) k *= FEATURE_MUL;
                cost[i] = k;
            }
            for (int a = 0; a < A; a++) foreach (int i in areas[a].cells) area[i] = (byte)(a + 1);
            // the S-level stamps (arches, rings, clusters, fossils, mouths) and
            // the geyser pools are voxel-only steps the map does not see: a
            // route costs Sheer there, so it goes round them
            var o2 = Offsets.For(in spec);
            void KeepOut(float2 c, float r)
            {
                int R = (int)math.ceil(r / cv), cx = (int)(c.x / cv), cz = (int)(c.y / cv);
                for (int z = math.max(0, cz - R); z <= math.min(n - 1, cz + R); z++)
                for (int x = math.max(0, cx - R); x <= math.min(n - 1, cx + R); x++)
                {
                    int i = z * n + x;
                    if (cost[i] == 0 || math.distance(c, new float2((x + 0.5f) * cv, (z + 0.5f) * cv)) > r) continue;
                    cost[i] = math.max(cost[i], BAND_COST[Danger.Sheer] * (1 + rec.Data[i].Danger));
                }
            }
            int sc = spec.worldSizeVoxels >> Stamps.CELL_SHIFT;
            for (int gz = 0; gz <= sc; gz++)
            for (int gx = 0; gx <= sc; gx++)
                if (Stamps.CellStamp(in o2, gx, gz, out var st) && StampKeepOut(st.kind) > 0f) KeepOut(StampKeepOutCentre(in st), StampKeepOut(st.kind));
            for (int j = 0; j < 9; j++) KeepOut(Stamps.PoolCentre(in spec, in o2, j), POOL_KEEP_OUT);

            // ---- 2. what sees each area; one search per pair ----
            var surf = new float[N];
            for (int i = 0; i < N; i++)
            {
                float w = map.Water[i];
                surf[i] = math.max(math.max(map.Height[i], 0f), w != Hydrology.NO_WATER ? w : 0f);
            }
            var sees = new byte[N];
            for (int a = 0; a < A; a++)
            {
                var cells = areas[a].cells;
                int step = math.max(1, cells.Length / SIGHT_TARGETS);
                for (int t = 0; t < SIGHT_TARGETS && t * step < cells.Length; t++)
                    Viewshed(surf, n, cells[t * step], SIGHT_RANGE, sees, (byte)(1 << a));
            }
            var linkCost = new long[A, A];
            var linkPath = new int[A, A][];
            var dist = new int[N]; var parent = new int[N];
            var heap = new Heap(1 << 16);
            for (int a = 0; a < A; a++)
            for (int b = a + 1; b < A; b++)
            {
                for (int i = 0; i < N; i++) { dist[i] = int.MaxValue; parent[i] = -1; }
                heap.Clear();
                foreach (int i in areas[a].cells) { dist[i] = 0; heap.Push(i); }
                int end = -1, both = (1 << a) | (1 << b);
                while (heap.Count > 0)
                {
                    long key = heap.Pop();
                    int i = (int)(key & 0xFFFFF), d = (int)(key >> 20);
                    if (d != dist[i]) continue;
                    int ar = area[i] - 1;
                    if (ar == b) { end = i; break; }
                    if (ar >= 0 && ar != a) continue;   // never cross another area
                    int x = i & (n - 1), z = i >> Log2(n);
                    int ci = cost[i] * ((sees[i] & both) != 0 ? SIGHT_SEEN : SIGHT_UNSEEN);
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int xx = x + dx, zz = z + dz;
                        if (xx < 0 || zz < 0 || xx >= n || zz >= n) continue;
                        int j = zz * n + xx;
                        if (cost[j] == 0 || area[j] == a + 1) continue;
                        int cj = cost[j] * ((sees[j] & both) != 0 ? SIGHT_SEEN : SIGHT_UNSEEN);
                        int nd = d + (ci + cj) * (dx != 0 && dz != 0 ? DIAG : ORTHO);
                        if (nd < dist[j]) { dist[j] = nd; parent[j] = i; heap.Push(((long)nd << 20) | (uint)j); }
                    }
                }
                if (end < 0) { linkCost[a, b] = linkCost[b, a] = long.MaxValue; continue; }
                var p = new List<int>();
                for (int i = end; i >= 0; i = parent[i]) p.Add(i);
                p.Reverse();   // A's cell first
                linkCost[a, b] = linkCost[b, a] = dist[end];
                linkPath[a, b] = p.ToArray();
            }
            long searchMs = sw.ElapsedMilliseconds;

            // ---- 3. spanning tree + extra links ----
            var pairs = new List<(long c, int a, int b)>();
            for (int a = 0; a < A; a++) for (int b = a + 1; b < A; b++) if (linkCost[a, b] != long.MaxValue) pairs.Add((linkCost[a, b], a, b));
            pairs.Sort((p, q) => p.c != q.c ? p.c.CompareTo(q.c) : p.a != q.a ? p.a.CompareTo(q.a) : p.b.CompareTo(q.b));
            var comp = new int[A]; for (int a = 0; a < A; a++) comp[a] = a;
            int Find(int a) { while (comp[a] != a) a = comp[a] = comp[comp[a]]; return a; }
            var chosen = new List<(int a, int b, bool tree, float overlap)>();
            var linked = new bool[A, A];
            var deg = new int[A];
            var near = new bool[N];
            void Stamp(int[] path)
            {
                int r = (int)math.ceil(OVERLAP_R / cv);
                foreach (int i in path)
                {
                    int x = i & (n - 1), z = i / n;
                    for (int zz = math.max(0, z - r); zz <= math.min(n - 1, z + r); zz++)
                    for (int xx = math.max(0, x - r); xx <= math.min(n - 1, x + r); xx++)
                        if ((xx - x) * (xx - x) + (zz - z) * (zz - z) <= r * r) near[zz * n + xx] = true;
                }
            }
            float Overlap(int[] path)
            {
                int c = 0; foreach (int i in path) if (near[i]) c++;
                return path.Length > 0 ? (float)c / path.Length : 0f;
            }
            foreach (var p in pairs)
            {
                int ra = Find(p.a), rb = Find(p.b);
                if (ra == rb) continue;
                comp[ra] = rb;
                chosen.Add((p.a, p.b, true, Overlap(linkPath[p.a, p.b])));
                linked[p.a, p.b] = linked[p.b, p.a] = true; deg[p.a]++; deg[p.b]++;
                Stamp(linkPath[p.a, p.b]);
            }
            for (int a = 0; a < A; a++)
            {
                while (deg[a] < 2)
                {
                    int best = -1; double bestC = double.MaxValue; float bestO = 0f;
                    for (int b = 0; b < A; b++)
                    {
                        if (b == a || linked[a, b] || linkCost[a, b] == long.MaxValue) continue;
                        var path = a < b ? linkPath[a, b] : linkPath[b, a];
                        float o = Overlap(path);
                        double c = linkCost[a, b] * (1.0 + 2.0 * o);
                        if (c < bestC) { bestC = c; best = b; bestO = o; }
                    }
                    if (best < 0) break;
                    int lo = math.min(a, best), hi = math.max(a, best);
                    chosen.Add((lo, hi, false, bestO));
                    linked[a, best] = linked[best, a] = true; deg[a]++; deg[best]++;
                    Stamp(linkPath[lo, hi]);
                }
            }

            // ---- 4 + 5. centre lines, then the easing ----
            var routes = new Route[chosen.Count];
            var coneLo = new float[N]; var coneHi = new float[N]; var roughW = new float[N];
            var touched = new List<int>();
            var seen = new bool[N]; var mark = new bool[N];
            for (int r = 0; r < chosen.Count; r++)
            {
                var (a, b, tree, ov) = chosen[r];
                int[] path = linkPath[a, b];
                var rt = new Route
                {
                    mapVersion = spec.mapVersion, areaA = a, areaB = b, tree = tree, overlap = ov,
                    areaCentreA = areas[a].centre, areaCentreB = areas[b].centre,
                    cost = linkCost[a, b], cells = path,
                };
                rt.line = Smooth(path, n, cv);
                int L = rt.line.Length;
                rt.endA = (int2)rt.line[0]; rt.endB = (int2)rt.line[L - 1];
                rt.halfW = new float[L]; rt.cut = new float[L]; rt.bandBefore = new byte[L];
                var prof = new float[L];
                for (int k = 0; k < L; k++)
                {
                    float2 p = rt.line[k];
                    rt.halfW[k] = HALF_W * (1f + WIDTH_WOBBLE * math.clamp(1.6f * Noise.Fbm(p / 2500f + o2.valley + 37f, 2), -1f, 1f));
                    int ci = CellOf(p, n, cv);
                    rt.bandBefore[k] = (byte)rec.Data[ci].MoveBand;
                    prof[k] = Bilinear(map.Height, n, cv, p);
                }
                // the lowest PASS_SLOPE-Lipschitz profile under the ground: crests cut
                var u = (float[])prof.Clone();
                var seg = new float[L];
                for (int k = 1; k < L; k++) seg[k] = math.distance(rt.line[k], rt.line[k - 1]);
                for (int k = 1; k < L; k++) u[k] = math.min(u[k], u[k - 1] + PASS_SLOPE * seg[k]);
                for (int k = L - 2; k >= 0; k--) u[k] = math.min(u[k], u[k + 1] + PASS_SLOPE * seg[k + 1]);
                // where the cut would pass CUT_MAX the step stays; the lowest
                // PASS_SLOPE-Lipschitz profile over THAT fills its foot (a scree
                // ramp, at most FILL_MAX). Without a capped cut nothing is filled.
                // The cap is read on the dry cells within the core as well as on
                // the line: a cell beside it that a 20 m cut leaves standing
                // high lifts the line to within the cone of it (the bilinear
                // profile alone misses a cliff that runs beside the line).
                for (int k = 0; k < L; k++)
                {
                    float lo = prof[k] - CUT_MAX, core = CORE * rt.halfW[k];
                    float2 p = rt.line[k];
                    int R = (int)math.ceil(core / cv), cx = (int)(p.x / cv), cz = (int)(p.y / cv);
                    for (int z = math.max(0, cz - R); z <= math.min(n - 1, cz + R); z++)
                    for (int x = math.max(0, cx - R); x <= math.min(n - 1, cx + R); x++)
                    {
                        int i = z * n + x;
                        if (!Easable(map, rec, i)) continue;
                        float d = math.distance(p, new float2((x + 0.5f) * cv, (z + 0.5f) * cv));
                        if (d <= core) lo = math.max(lo, map.Height[i] - CUT_MAX - PASS_SLOPE * d);
                    }
                    u[k] = math.max(u[k], lo);
                }
                for (int k = 1; k < L; k++) u[k] = math.max(u[k], u[k - 1] - PASS_SLOPE * seg[k]);
                for (int k = L - 2; k >= 0; k--) u[k] = math.max(u[k], u[k + 1] - PASS_SLOPE * seg[k + 1]);
                rt.fill = new float[L];
                float len = 0f;
                for (int k = 0; k < L; k++)
                {
                    rt.cut[k] = math.clamp(prof[k] - u[k], 0f, CUT_MAX);
                    rt.fill[k] = math.max(0f, u[k] - prof[k]);   // the cells' FILL_MAX caps it
                    len += seg[k];
                }
                rt.lengthM = len / 10f;
                rt.straightM = math.distance(rt.line[0], rt.line[L - 1]) / 10f;
                float wsum = 0f; foreach (float w in rt.halfW) wsum += w;
                rt.widthM = 2f * wsum / L / 10f;
                // per cell: the flared cone's bounds, the strongest roughness easing, the corridor
                var corr = new List<int>();
                for (int k = 0; k < L; k++)
                {
                    float2 p = rt.line[k];
                    float hw = rt.halfW[k], core = CORE * hw, T = u[k];   // the uncapped profile: the cones agree
                    float reach = math.max(core + FLARE_REACH, hw + ROUGH_EDGE);
                    int R = (int)math.ceil(reach / cv) + 1;
                    int cx = (int)(p.x / cv), cz = (int)(p.y / cv);
                    for (int z = math.max(0, cz - R); z <= math.min(n - 1, cz + R); z++)
                    for (int x = math.max(0, cx - R); x <= math.min(n - 1, cx + R); x++)
                    {
                        int i = z * n + x;
                        float2 q = new float2((x + 0.5f) * cv, (z + 0.5f) * cv);
                        // the edge wanders a little (no ruled line); the core does not
                        float d0 = math.distance(p, q);
                        float d = d0 + 40f * Noise.Fbm(q / 700f + o2.valley - 23f, 2) * math.saturate((d0 - core) / core);
                        if (d > reach) continue;
                        if (!seen[i]) { seen[i] = true; touched.Add(i); coneLo[i] = float.MinValue; coneHi[i] = float.MaxValue; }
                        float dd = math.max(0f, d), fx = math.max(0f, dd - core);
                        float F = PASS_SLOPE * dd + 0.5f * FLARE * fx * fx;
                        coneLo[i] = math.max(coneLo[i], T - F); coneHi[i] = math.min(coneHi[i], T + F);
                        // full inside, from a noise-moved start out to the edge
                        float start = (0.45f + 0.3f * Noise.Fbm(q / 900f + o2.detail + 51f, 2)) * hw;
                        roughW[i] = math.max(roughW[i], 1f - Noise.SmoothStep(start, hw + ROUGH_EDGE, d));
                        if (d <= hw && !mark[i]) { mark[i] = true; corr.Add(i); }
                    }
                }
                // mark is per route: cleared so the next route lists its own cells
                foreach (int i in corr) mark[i] = false;
                corr.Sort();
                rt.corridor = corr.ToArray();
                routes[r] = rt;
            }
            // apply, guarded
            int cutCells = 0, fillCells = 0; float maxCut = 0f, maxFill = 0f;
            if (Diagnose)
            {
                DiagH0 = (float[])map.Height.Clone(); DiagLo = new float[N]; DiagHi = new float[N]; DiagSkip = new bool[N];
                for (int i = 0; i < N; i++) { DiagLo[i] = DiagHi[i] = float.NaN; DiagSkip[i] = !Easable(map, rec, i); }
                foreach (int i in touched) { DiagLo[i] = coneLo[i]; DiagHi[i] = coneHi[i]; }
            }
            foreach (int i in touched)
            {
                if (!Easable(map, rec, i)) continue;
                if (roughW[i] > 0f)
                    map.Rough[i] = math.lerp(map.Rough[i], math.min(map.Rough[i], ROUTE_ROUGH), roughW[i]);
                float h0 = map.Height[i];
                float delta = math.clamp(math.clamp(h0, coneLo[i], coneHi[i]) - h0, -CUT_MAX, FILL_MAX);
                if (delta > 0f) { map.Height[i] = h0 + delta; fillCells++; maxFill = math.max(maxFill, delta); }
                if (delta >= 0f) continue;
                int x = i & (n - 1), z = i / n;
                float floor = LAND_MIN;
                for (int zz = math.max(0, z - 2); zz <= math.min(n - 1, z + 2); zz++)
                for (int xx = math.max(0, x - 2); xx <= math.min(n - 1, x + 2); xx++)
                {
                    float w = map.Water[zz * n + xx];
                    if (w != Hydrology.NO_WATER) floor = math.max(floor, w + WATER_MARGIN);
                }
                float nh = math.max(h0 + delta, math.min(h0, floor));
                if (nh < map.Height[i]) { cutCells++; maxCut = math.max(maxCut, map.Height[i] - nh); }
                map.Height[i] = nh;
            }
            // ---- 6. passes on the eased profile ----
            foreach (var rt in routes)
            {
                int L = rt.line.Length;
                var prof = new float[L];
                for (int k = 0; k < L; k++) prof[k] = Bilinear(map.Height, n, cv, rt.line[k]);
                rt.passes = Passes(prof);
            }
            var sb = new System.Text.StringBuilder();
            sb.Append($"areas {A}, links {chosen.Count} ({pairs.Count} reachable pairs); search {searchMs} ms, all {sw.ElapsedMilliseconds} ms; cut cells {cutCells}, deepest cut {maxCut / 10f:F1} m; fill cells {fillCells}, deepest fill {maxFill / 10f:F1} m; ");
            foreach (var rt in routes)
                sb.Append($"[{rt.areaA}-{rt.areaB}{(rt.tree ? "" : " extra")} {rt.lengthM:F0} m / {rt.straightM:F0} m, overlap {rt.overlap * 100f:F0}%, passes {rt.passes.Length}] ");
            LastBuild = sb.ToString();
            return routes;
        }

        /// The record's corridor: FLAG_ROUTE and base danger one tier lower
        /// (WorldRecord.Build, after Danger.Mark). A cell in two corridors drops once.
        public static void Mark(WorldMap map, WorldRecord rec)
        {
            var routes = map.Routes;
            if (routes == null) return;
            foreach (var rt in routes)
            foreach (int i in rt.corridor)
            {
                if ((rec.Data[i].flags & WorldRecord.FLAG_ROUTE) != 0) continue;
                rec.Data[i].flags |= WorldRecord.FLAG_ROUTE;
                if (rec.Data[i].IsSea) continue;
                byte hz = rec.Data[i].hazard;
                int d = hz & 7;
                rec.Data[i].hazard = (byte)((hz & ~7) | math.max(0, d - 1));
            }
            rec.RouteList = routes;
        }

        /// Marks `bit` on every cell within `range` cells whose 2 m eye sees cell
        /// t's ground + 2 m (the map surface: ground, lake, river or sea). Rays
        /// to every cell of the square's rim, integer steps (no trig); a cell
        /// is seen when the line from t to its eye clears the steepest ground
        /// passed so far on that ray.
        public static void Viewshed(float[] surf, int n, int t, int range, byte[] mask, byte bit)
        {
            int tx = t & (n - 1), tz = t / n;
            float ht = surf[t] + EYE;
            mask[t] |= bit;
            for (int side = 0; side < 4; side++)
            for (int e = -range; e < range; e++)
            {
                int dx = side == 0 ? range : side == 1 ? -range : e;
                int dz = side == 2 ? range : side == 3 ? -range : e;
                if (side >= 2) dx = side == 2 ? -e : e;
                float len = math.sqrt((float)(dx * dx + dz * dz)) / range;   // cells per step
                float maxSlope = float.MinValue;
                for (int s = 1; s <= range; s++)
                {
                    int x = tx + Div(dx * s, range), z = tz + Div(dz * s, range);
                    if (x < 0 || z < 0 || x >= n || z >= n) break;
                    int i = z * n + x;
                    float r = s * len;
                    if ((surf[i] + EYE - ht) / r >= maxSlope) mask[i] |= bit;
                    maxSlope = math.max(maxSlope, (surf[i] - ht) / r);
                }
            }
        }

        // rounded integer division
        private static int Div(int a, int b) => a >= 0 ? (a + b / 2) / b : -((-a + b / 2) / b);

        /// Line indices of the crests that stand PASS_PROMINENCE over the lowest
        /// ground before a higher point on both sides (ends excluded).
        public static int[] Passes(float[] prof)
        {
            var list = new List<int>();
            int L = prof.Length;
            for (int k = 1; k < L - 1; k++)
            {
                if (prof[k] < prof[k - 1] || prof[k] < prof[k + 1]) continue;
                if (prof[k] == prof[k - 1] && list.Count > 0 && list[list.Count - 1] == k - 1) continue;
                float lo = prof[k]; bool ok = true;
                for (int j = k - 1; j >= 0 && prof[j] <= prof[k]; j--) lo = math.min(lo, prof[j]);
                if (prof[k] - lo < PASS_PROMINENCE) ok = false;
                lo = prof[k];
                for (int j = k + 1; j < L && prof[j] <= prof[k]; j++) lo = math.min(lo, prof[j]);
                if (prof[k] - lo < PASS_PROMINENCE) ok = false;
                if (ok) list.Add(k);
            }
            return list.ToArray();
        }

        /// Cell centres, then SMOOTH passes of a [1 2 3 2 1]/9 filter with the ends fixed.
        public static float2[] Smooth(int[] path, int n, float cv)
        {
            int L = path.Length;
            var a = new float2[L];
            for (int k = 0; k < L; k++) a[k] = new float2(((path[k] & (n - 1)) + 0.5f) * cv, ((path[k] / n) + 0.5f) * cv);
            var b = new float2[L];
            for (int s = 0; s < SMOOTH; s++)
            {
                for (int k = 0; k < L; k++)
                {
                    if (k < 2 || k > L - 3) { b[k] = a[k]; continue; }
                    b[k] = (a[k - 2] + 2f * a[k - 1] + 3f * a[k] + 2f * a[k + 1] + a[k + 2]) / 9f;
                }
                var t = a; a = b; b = t;
            }
            return a;
        }

        /// The cells the easing may change: dry land off the easy areas, a
        /// one-cell collar round them (an easy cell's slope reads its four
        /// neighbours: easing one would change the area) and the signature
        /// features' footprints.
        private static bool Easable(WorldMap map, WorldRecord rec, int i)
        {
            var c = rec.Data[i];
            if (c.IsSea || c.IsEasy || c.IsFeature || map.WaterClass[i] != Hydrology.DRY || map.Height[i] <= 0f) return false;
            int n = rec.Cells, x = i & (n - 1), z = i / n;
            return !((x > 0 && rec.Data[i - 1].IsEasy) || (x < n - 1 && rec.Data[i + 1].IsEasy)
                  || (z > 0 && rec.Data[i - n].IsEasy) || (z < n - 1 && rec.Data[i + n].IsEasy));
        }

        public static int CellOf(float2 p, int n, float cv) =>
            math.clamp((int)(p.y / cv), 0, n - 1) * n + math.clamp((int)(p.x / cv), 0, n - 1);

        public static float Bilinear(float[] h, int n, float cv, float2 p)
        {
            float gx = p.x / cv - 0.5f, gz = p.y / cv - 0.5f;
            int x0 = math.clamp((int)math.floor(gx), 0, n - 2), z0 = math.clamp((int)math.floor(gz), 0, n - 2);
            float fx = math.saturate(gx - x0), fz = math.saturate(gz - z0);
            float a = math.lerp(h[z0 * n + x0], h[z0 * n + x0 + 1], fx);
            float b = math.lerp(h[(z0 + 1) * n + x0], h[(z0 + 1) * n + x0 + 1], fx);
            return math.lerp(a, b, fz);
        }

        private static int Log2(int v) { int r = 0; while ((1 << r) < v) r++; return r; }

        /// Binary min-heap of (cost << 20 | cell) keys; ties break by cell index.
        private sealed class Heap
        {
            private long[] _a; public int Count;
            public Heap(int cap) { _a = new long[cap]; }
            public void Clear() => Count = 0;
            public void Push(long v)
            {
                if (Count == _a.Length) Array.Resize(ref _a, _a.Length * 2);
                int i = Count++;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (_a[p] <= v) break;
                    _a[i] = _a[p]; i = p;
                }
                _a[i] = v;
            }
            public long Pop()
            {
                long top = _a[0], v = _a[--Count];
                int i = 0;
                while (true)
                {
                    int c = 2 * i + 1;
                    if (c >= Count) break;
                    if (c + 1 < Count && _a[c + 1] < _a[c]) c++;
                    if (v <= _a[c]) break;
                    _a[i] = _a[c]; i = c;
                }
                if (Count > 0) _a[i] = v;
                return top;
            }
        }
    }
}
