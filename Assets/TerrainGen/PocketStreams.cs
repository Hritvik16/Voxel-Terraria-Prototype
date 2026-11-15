// Assets/TerrainGen/PocketStreams.cs
//
// TASK 209b -- STILL STREAMS FOR THE PONDS (owner 2026-11-14, Q-AG (a): "still stream but looks
// like it's flowing ... Pond overflow streams is exactly what I was thinking, just to make the
// world and ponds feel more natural"; plan = task 204's option (A)). `-pocketstreams 0` = the
// task 209 world byte for byte (no section appended, the column step never runs).
//
// A surface pond (FluidPockets' site plane) gets narrow carved channels built the way Rivers.cs
// builds rivers, AT REST: no fluid-simulation cost (0 tiles, 0 tick until something touches them).
// Every fluid but quicksand (a sinking basin has no flow). Every rule below is a function of the
// map (8 m cells: height, catchment Flow, water class), the world record and the pond itself:
//
//   INLET  the pond's catchment, upstream: from the pond's cell, the higher 8-neighbour that
//          DRAINS INTO the current cell (its steepest descent, D8, is the current cell) with
//          the largest Flow (ties: the lower index). It stops at a cell that fails the keep-outs
//          (sea, lake / river, route corridor, signature feature, not the Walk band, the start
//          zone / towns, another pond's box), a rise of more than RISE_STOP per cell (the spring
//          at a cliff foot) or MaxCells (6-16 cells = 48-128 m, by pond size; 204's cap).
//          No cell drains in: a ONE-cell spring from the highest valid neighbour.
//   SURPLUS a pond whose catchment can feed more than it holds overflows (OUTLET); one that
//          cannot only SEEPS. The rule is the water balance at its simplest: OUTLET iff the
//          catchment area (Flow x one cell) is at least RUNOFF_RATIO x the pond's own area.
//   OUTLET downstream by D8 from the pond's cell to the next RIVER (a water stream joins it at
//          the river's own level, Rivers.LevelAt at the join point), or it ends (a lake, the
//          sea, a keep-out, no lower cell, MaxCells): the channel just ends there, sealed by
//          the LIP rule (the water sinks into the ground). Non-water fluids never join water.
//   SEEP   a damp patch of the pond's lining material (FluidPockets.Lining: mud for water,
//          magma crust for lava, ...) from the low rim one cell downhill, tapering.
//
// THE CHANNEL is Rivers' shaped section at stream scale: half width HW_MIN..HW_MAX (0.6-1.2 m
// wide) by the catchment (log2(Flow) of RIVER_MIN_CELLS), edge SHAPED_EDGE + SHAPED_SHELF per
// voxel in, capped at DEPTH_CAP (0.25-0.35 m of fluid). The course is Rivers' quadratic B-spline
// over the cells' centres (the pond's centre first / last), bent by Rivers' meander rule at the
// stream's scale (2.5 half widths sideways, one bend per 24 half widths), resampled every STEP voxels
// into NODES. A node's level is FREEBOARD under the pond-shaped ground at the node, taken as the
// running minimum downstream (a level never rises downstream), never under the pond's level L
// upstream of it (inlet) and never over L below it (outlet); nodes within the pond or the flat
// reach FLAT_OUT past its shore stand AT L, so the stream meets the pond flush (no step, no lip
// at the shore). Banks: Rivers' carve (BANK_CARVE slope, cut at most CUT_LIMIT, tapering to 0 at
// CARVE_REACH) and bank raise (BANK_SLOPE), at stream scale. LIP rule as Rivers': a column lower
// than a neighbour's stream surface is raised to it; a wet one becomes a LIP (GenMat.RiverLip,
// drawn as water) for water, the lining for every other fluid (lining-coloured lips, 0 new ids).
// AT REST BY CONSTRUCTION (FluidAtRest's rule: every fluid voxel has fluid or solid beside it
// and under it), the same way the rivers are:
//   * a stream column never carves under another body's seal: a pond rim (FluidPockets.Apply's
//     L + 1) unless the column becomes stream fluid at L; a river's hold (Rivers.Column's holdY)
//     unless the column's stream level is at least it; a lake rim never (as Rivers);
//   * a column that already holds water (the pond, a river, a lake, the sea) is never carved
//     or filled -- the stream's surface beside it is at most its own (flush stretches);
//   * no fluid touches a reactive face: the channel and its first ring are LINED (as the pond)
//     over [level - LINE_DOWN, level + LINE_UP], lips of water excepted (RiverLip does not react);
//   * stamps open no air within KEEP_OUT of a channel, caves stay CAP_MARGIN under its bed.
// Only at texel 1 (what the voxels are: LOD 0, shells, the point query). The far band (texel >= 8)
// does not draw a 1 m stream (it would be sub-texel); a pond keeps its far column unchanged.
//
// STORAGE (rule 11): built once per world on the main thread with the site plane, and APPENDED to
// it (WorldMap.PocketPlane), so every generator path that already carries the plane carries the
// streams (MapView.stream points into it; null = no streams). Layout (ints, at the plane's end):
//   [0] MAGIC  [1] G (index cells per edge, 2^GRID_SHIFT voxels)  [2] record count  [3] section length
//   [4 .. 4 + G*G]     start of each index cell's list (G*G + 1 prefix entries, into the id list)
//   id list            record offsets (from the section start)
//   records            [0] fluid | lining << 8 | kind << 16, [1] node count, [2..5] box x0 z0 x1 z1
//                      (nodes + their half width + REACH), then 3 ints a node:
//                      x | (half width * 8) << 24, z | (depth * 8) << 24, level (absolute Y).
// Seed 42: ~130 ponds, ~1-2 k nodes: tens of KB (HANDOFF 209b has the measured size).
using Unity.Mathematics;

namespace TerrainGen
{
    public static unsafe class PocketStreams
    {
        public const int MAGIC = 0x5354524D;            // "STRM"
        public const int GRID_SHIFT = 11;                // 204.8 m index cells
        public const int HEAD = 4;
        public const int REC_HEAD = 6, NODE_INTS = 3;
        public const int KIND_STREAM = 0, KIND_SEEP = 1;

        // ---- the channel (voxels) ----
        public const float HW_MIN = 3f;                  // 0.6 m wide at a head
        public const float HW_MAX = 6f;                  // 1.2 m wide at the river threshold's catchment
        public const float DEPTH_CAP = 3.5f;             // the deepest a stream is (0.35 m)
        public const int FREEBOARD = 2;                  // the water stands this far under the node's ground
        public const int STEP = 12;                      // voxels between nodes (a quarter of the shortest meander wave)
        public const float MEANDER_AMP = Rivers.MEANDER_AMP;     // sideways displacement, half widths (2.5)
        public const float MEANDER_WAVE = Rivers.MEANDER_WAVE;   // one turn of the bend per this many half widths (24)
        public const float MEANDER_TURN = Rivers.MEANDER_TURN;   // turns of the phase per unit of noise (0.75)
        public const float FLAT_OUT = 4f;                // voxels past the pond's shore the stream stays AT L (+ its half width)
        public const int CARVE_REACH = 24;               // voxels past the edge a bank is cut / raised
        public const float CUT_MIN = 6f, CUT_EXTRA = 4f, CUT_LIMIT = 12f;
        public const float BANK_CARVE = Rivers.BANK_CARVE, BANK_SLOPE = Rivers.BANK_SLOPE;
        public const int KEEP_OUT = 8;                   // voxels past the edge: no stamp opening
        public const int LINE_DOWN = 6, LINE_UP = 2;     // the lining band round the level
        public const int CAP_MARGIN = FluidPockets.CAP_MARGIN;
        public const float REACH = CARVE_REACH + 2f;     // a column looks this far past a channel's edge
        public const int MAX_SEGS = 48;

        // ---- the network (map cells) ----
        public const float RISE_STOP = 20f;              // voxels per cell (2 m / 8 m): a spring at the cliff foot
        public const int RUNOFF_RATIO = 16;              // catchment area / pond area to overflow
        public const float SEEP_LEN = 80f;               // one cell downhill
        public const float SEEP_HW0 = 6f, SEEP_HW1 = 2f;

        public static float HalfWidth(float flow)
            => HW_MIN + (HW_MAX - HW_MIN) * math.saturate(math.log2(math.max(1f, flow)) / math.log2(Hydrology.RIVER_MIN_CELLS));
        public static float DepthOf(float hw) => math.min(DEPTH_CAP, 1f + 0.5f * hw);
        public static int MaxCells(int rx, int rz) => math.clamp((rx + rz) / 6, 6, 16);

        /// The section inside a plane array of `len` ints, or null (no streams: -pocketstreams 0).
        public static int* Section(int* plane, int len, int mapCells, int cellVoxels)
        {
            if (plane == null) return null;
            int n = (mapCells * cellVoxels + FluidPockets.CELL - 1) >> FluidPockets.CELL_SHIFT;
            int b = n * n * FluidPockets.STRIDE;
            if (len <= b + HEAD || plane[b] != MAGIC) return null;
            return plane + b;
        }

        // ------------------------------------------------------------------
        // Column step (ShapeField.Sample, right after FluidPockets.Apply)
        // ------------------------------------------------------------------

        private struct Hit { public float level, depth, maxDepth, nearL, nearE; public int seg; }

        /// Rivers.LevelAt's nearest-centre rule (task 179's shaped course) over the stream
        /// segments, with the index of the segment it took (inside) or the nearest (outside).
        private static float Level(Rivers.Seg* segs, int m, float2 p, out Hit h)
        {
            h = default; h.seg = -1; h.nearE = REACH; h.nearL = Hydrology.NO_WATER;
            float bestD = float.MaxValue, lv = Hydrology.NO_WATER;
            int near = -1;
            for (int k = 0; k < m; k++)
            {
                Rivers.Seg s = segs[k];
                float2 ab = s.b - s.a;
                float l2 = math.dot(ab, ab);
                float t = l2 > 0f ? math.saturate(math.dot(p - s.a, ab) / l2) : 0f;
                float d = math.length(p - (s.a + t * ab));
                float w = math.lerp(s.wa, s.wb, t);
                float L = math.lerp(s.la, s.lb, t);
                if (d < w)
                {
                    if (d < bestD)
                    {
                        bestD = d; lv = L; h.seg = k;
                        float shelf = Rivers.SHAPED_EDGE + Rivers.SHAPED_SHELF * (w - d);
                        h.maxDepth = math.min(DEPTH_CAP, shelf);
                        h.depth = math.min(math.lerp(s.da, s.db, t), h.maxDepth);
                    }
                }
                else if (d - w < h.nearE) { h.nearE = d - w; h.nearL = L; near = k; }
            }
            h.level = lv;
            if (lv > Hydrology.NO_WATER * 0.5f) { h.nearL = Hydrology.NO_WATER; h.nearE = REACH; }
            else h.seg = near;
            return lv;
        }

        /// The stream step of a column (see the header). `rimY`: LakeRim's rim (int.MinValue = none);
        /// `riverHoldY`: Rivers.Column's hold (the highest river surface over the column or its 8
        /// neighbours; int.MinValue = none).
        public static void Column(in TerrainSpec spec, in MapView map, float x, float z, int texelVoxels, int rimY, int riverHoldY,
                                  ref ColumnSample c)
        {
            int* s = map.stream;
            if (s == null || texelVoxels > 1) return;
            int ix = (int)math.floor(x), iz = (int)math.floor(z);
            int G = s[1], gx = ix >> GRID_SHIFT, gz = iz >> GRID_SHIFT;
            if ((uint)gx >= (uint)G || (uint)gz >= (uint)G) return;
            int lo = s[HEAD + gz * G + gx], hi = s[HEAD + gz * G + gx + 1];
            if (lo == hi) return;
            int ids = HEAD + G * G + 1;
            Rivers.Seg* segs = stackalloc Rivers.Seg[MAX_SEGS];
            int* segRec = stackalloc int[MAX_SEGS];
            int m = 0;
            float2 p = new float2(x, z);
            // seeps: paint only
            float seepBest = float.MaxValue; int seepRec = -1;
            for (int k = lo; k < hi; k++)
            {
                int r = s[ids + k];
                int* rec = s + r;
                if (ix < rec[2] || iz < rec[3] || ix > rec[4] || iz > rec[5]) continue;
                int kind = (rec[0] >> 16) & 255, cnt = rec[1];
                int* nd = rec + REC_HEAD;
                for (int j = 0; j + 1 < cnt; j++)
                {
                    int* a = nd + j * NODE_INTS, b = a + NODE_INTS;
                    float ax = a[0] & 0xFFFFFF, az = a[1] & 0xFFFFFF, bx = b[0] & 0xFFFFFF, bz = b[1] & 0xFFFFFF;
                    float wa = ((uint)a[0] >> 24) * 0.125f, wb = ((uint)b[0] >> 24) * 0.125f;
                    float pad = math.max(wa, wb) + (kind == KIND_STREAM ? REACH + 1f : 1f);
                    if (x < math.min(ax, bx) - pad || x > math.max(ax, bx) + pad || z < math.min(az, bz) - pad || z > math.max(az, bz) + pad) continue;
                    if (kind == KIND_SEEP)
                    {
                        float2 A = new float2(ax, az), ab = new float2(bx, bz) - A;
                        float l2 = math.dot(ab, ab);
                        float t = l2 > 0f ? math.saturate(math.dot(p - A, ab) / l2) : 0f;
                        float e = math.length(p - (A + t * ab)) - math.lerp(wa, wb, t);
                        if (e < 0f && e < seepBest) { seepBest = e; seepRec = r; }
                        continue;
                    }
                    if (m >= MAX_SEGS) break;
                    segs[m] = new Rivers.Seg
                    {
                        a = new float2(ax, az), b = new float2(bx, bz), la = a[2], lb = b[2], wa = wa, wb = wb,
                        da = ((uint)a[1] >> 24) * 0.125f, db = ((uint)b[1] >> 24) * 0.125f,
                    };
                    segRec[m++] = r;
                }
            }
            if (m == 0)
            {
                if (seepRec >= 0) Seep(s + seepRec, ref c);
                return;
            }

            // the seals the stream must keep (read before anything changes)
            bool wet = c.water != ColumnSample.NO_WATER_Y && c.water > c.height;   // a pond, river, lake or the sea holds this column
            int pondKeep = !wet && c.pkLining != 0 ? c.pkLineHi : int.MinValue;   // a pond rim: L + 1
            int keep = math.max(math.max(pondKeep, riverHoldY), rimY);

            float Lf = Level(segs, m, p, out Hit h);
            int rec0 = h.seg >= 0 ? segRec[h.seg] : -1;
            byte fluid = rec0 >= 0 ? (byte)(s[rec0] & 255) : (byte)0;
            byte lining = rec0 >= 0 ? (byte)((s[rec0] >> 8) & 255) : (byte)0;
            bool inChannel = false, touched = false;
            int ly = int.MinValue;
            if (Lf > Hydrology.NO_WATER * 0.5f)
            {
                ly = (int)math.floor(Lf);
                touched = true;
                if (!wet)
                {
                    int bed = (int)math.floor(Lf - h.depth);
                    bool open = ly >= riverHoldY && (pondKeep == int.MinValue || ly >= pondKeep - 1);   // the column may hold the stream (no int.MinValue - 1)
                    int to = open ? math.max(bed, rimY) : math.max(bed, keep);
                    if (c.height > to) c.height = to;
                    int floorY = ly - (int)math.floor(h.maxDepth);
                    if (open && c.height < floorY)
                    {
                        if (c.rimGround == 0) c.rimGround = c.height;
                        c.height = floorY;
                    }
                    if (open && c.height < ly)
                    {
                        c.water = ly; c.waterKind = fluid == GenMat.Water ? (byte)0 : fluid;
                        inChannel = true;
                    }
                }
                else inChannel = true;
            }
            else if (h.seg >= 0 && h.nearE < CARVE_REACH && !wet && c.height >= spec.seaLevelY)
            {
                touched = true;
                // Rivers' CARVED BANK at stream scale, never under a seal
                int capY = (int)math.floor(h.nearL + BANK_CARVE * h.nearE);
                float cut = math.min(CUT_LIMIT, math.max(CUT_MIN, FREEBOARD + CUT_EXTRA)) * math.saturate(1f - h.nearE / CARVE_REACH);
                int to = math.max(math.max(capY, c.height - (int)math.floor(cut)), keep);
                if (to < c.height) c.height = to;
                // ... and Rivers' bank RAISE (ground under the level beside the channel)
                int by = (int)math.floor(h.nearL - h.nearE * BANK_SLOPE);
                if (c.height < by)
                {
                    if (c.rimGround == 0) c.rimGround = c.height;
                    c.height = by;
                }
                if (c.water != ColumnSample.NO_WATER_Y && c.water <= c.height) c.water = ColumnSample.NO_WATER_Y;
            }
            // the LIP rule (Rivers.Column's): a column lower than a neighbour's stream surface is raised to it
            int rn = int.MinValue;
            for (int k = 0; k < 8; k++)
            {
                int dx = k == 0 || k == 3 || k == 5 ? -1 : (k == 1 || k == 6 ? 0 : 1);
                int dz = k < 3 ? -1 : (k < 5 ? 0 : 1);
                float Ln = Level(segs, m, p + new float2(dx, dz), out Hit hn);
                if (Ln > Hydrology.NO_WATER * 0.5f)
                {
                    int y = (int)math.floor(Ln);
                    if (y > rn) { rn = y; if (rec0 < 0 || ly == int.MinValue) { rec0 = segRec[hn.seg]; fluid = (byte)(s[rec0] & 255); lining = (byte)((s[rec0] >> 8) & 255); } }
                }
            }
            int lipLo = 0;
            if (rn != int.MinValue)
            {
                touched = true;
                int top = math.max(math.max(c.height, spec.seaLevelY), c.water);
                if (rn > top)
                {
                    if (inChannel || c.water > c.height || c.height < spec.seaLevelY) lipLo = c.height + 1;
                    if (c.rimGround == 0) c.rimGround = c.height;
                    c.height = rn;
                    c.water = ColumnSample.NO_WATER_Y;
                    if (lipLo != 0) c.lipLo = lipLo;
                }
            }
            if (!touched) return;
            // keep-outs within KEEP_OUT of a channel, the cave cap, the lining
            float edge = Lf > Hydrology.NO_WATER * 0.5f ? 0f : h.nearE;
            int lvl = Lf > Hydrology.NO_WATER * 0.5f ? ly : (int)math.floor(h.nearL);
            if (rn != int.MinValue) lvl = math.max(lvl, rn);
            if (edge <= KEEP_OUT)
            {
                c.open1Lo = c.open1Hi = c.open2Lo = c.open2Hi = c.open3Lo = c.open3Hi = 0;
                c.carvesTop = false; c.stamp3D = false;
                int cap = lvl - (int)math.ceil(DEPTH_CAP) - CAP_MARGIN;
                c.riverFloor = c.riverFloor == 0 ? cap : math.min(c.riverFloor, cap);
            }
            if (lining != 0 && (edge <= 1.5f || rn != int.MinValue || inChannel))
            {
                int llo = lvl - LINE_DOWN, lhi = lvl + LINE_UP;
                if (lipLo != 0 && fluid == GenMat.Water) lhi = math.min(lhi, lipLo - 1);   // a water lip stays RiverLip (drawn as water)
                if (c.pkLining != 0 && c.pkLining == lining) { c.pkLineLo = math.min(c.pkLineLo, llo); c.pkLineHi = math.max(c.pkLineHi, lhi); }
                else if (c.pkLining == 0) { c.pkLining = lining; c.pkLineLo = llo; c.pkLineHi = lhi; }
            }
        }

        private static void Seep(int* rec, ref ColumnSample c)
        {
            if (c.water != ColumnSample.NO_WATER_Y && c.water > c.height) return;
            byte lining = (byte)((rec[0] >> 8) & 255);
            if (lining == 0) return;
            if (c.pkLining != 0 && c.pkLining != lining) return;
            if (c.pkLining == lining) { c.pkLineLo = math.min(c.pkLineLo, c.height - 1); c.pkLineHi = math.max(c.pkLineHi, c.height); }
            else { c.pkLining = lining; c.pkLineLo = c.height - 1; c.pkLineHi = c.height; }
        }

        // ------------------------------------------------------------------
        // Build (once per world, main thread, from FluidPockets.BuildPlane)
        // ------------------------------------------------------------------

        public struct Stats
        {
            public int inlets, springs, outlets, joins, seeps, none, nodes, bytes, cut;
            public int joinTries, joinNoPoint, joinHigh, joinCut;   // outlets that reached a river cell; no channel point; river over the pond; truncated before it
            public float lengthM;
            public ulong hx, hz, hl, hw, hd;   // FNV of the node fields (player vs Editor, task 209b)
            public double ms;
            public string Line() => $"inlets {inlets} (one-cell springs {springs}), outlets {outlets} (joining a river {joins}), seeps {seeps}, " +
                                    $"river cells reached {joinTries} (no channel point {joinNoPoint}, river over the pond {joinHigh}, cut before it {joinCut}); no stream {none}; {nodes} nodes (fnv x {hx:X4} z {hz:X4} level {hl:X4} width {hw:X4} depth {hd:X4}), {lengthM:F0} m of channel, truncated at a keep-out {cut}; section {bytes} B; {ms:F0} ms";
        }

        private static readonly int[] DXS = { -1, 0, 1, -1, 1, -1, 0, 1 };
        private static readonly int[] DZS = { -1, -1, -1, 0, 0, 1, 1, 1 };

        /// The steepest-descent neighbour of cell (x, z) on the map heights (drop per distance;
        /// ties: the first in DXS order), or -1 (no lower neighbour, or the map's edge).
        private static int D8(float[] H, int n, int x, int z)
        {
            float h = H[z * n + x], best = 0f; int bi = -1;
            for (int k = 0; k < 8; k++)
            {
                int qx = x + DXS[k], qz = z + DZS[k];
                if (qx < 0 || qz < 0 || qx >= n || qz >= n) return -1;
                float d = (h - H[qz * n + qx]) / ((DXS[k] != 0 && DZS[k] != 0) ? 1.41421356f : 1f);
                if (d > best) { best = d; bi = qz * n + qx; }
            }
            return bi;
        }

        private struct Node { public float2 p; public float hw, d; public int level; public float flow; }

        /// The streams of every site in `plane` (sites only), as the section to append.
        public static int[] Build(WorldMap map, int[] plane, bool[] zone, out Stats st)
        {
            st = default;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var spec = map.Spec; var rec = map.Record; var o = Offsets.For(in spec);
            int n = FluidPockets.PlaneCells(in spec), mc = spec.mapCells, cv = spec.mapCellVoxels, world = mc * cv;
            float[] H = map.Height, F = map.Flow; byte[] W = map.WaterClass;
            // each site's box (+ bank band + 2 voxels) per map cell: no stream crosses another pond
            var owner = new int[mc * mc];
            var sites = new System.Collections.Generic.List<int>();
            for (int i = 0; i < n * n; i++)
            {
                if (plane[i * FluidPockets.STRIDE + 3] == 0) continue;
                var s = FluidPockets.Decode(plane, i);
                sites.Add(i);
                int e = FluidPockets.BankBand(s.up, s.dn) + 2;
                int x0 = math.max(0, (s.cx - s.rx - e) / cv), x1 = math.min(mc - 1, (s.cx + s.rx + e) / cv);
                int z0 = math.max(0, (s.cz - s.rz - e) / cv), z1 = math.min(mc - 1, (s.cz + s.rz + e) / cv);
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) owner[z * mc + x] = i + 1;
            }
            var records = new System.Collections.Generic.List<int[]>();
            fixed (int* pp = plane)
            {
                var view = map.View; view.pocket = pp; view.stream = null;   // the ponds, no streams
                var segBuf = new Rivers.Seg[Rivers.MAX_SEGS];
                foreach (int i in sites)
                {
                    var s = FluidPockets.Decode(plane, i);
                    if (s.fluid == GenMat.Quicksand) continue;
                    byte lining = FluidPockets.Lining(s.fluid);
                    int pcx = s.cx / cv, pcz = s.cz / cv;
                    bool CellOk(int ci)
                    {
                        int x = ci % mc, z = ci / mc;
                        if (x <= 0 || z <= 0 || x >= mc - 1 || z >= mc - 1) return false;
                        var q = rec.AtCell(x, z);
                        if (q.IsSea || H[ci] <= 0f || W[ci] != Hydrology.DRY || q.IsRoute || q.IsFeature || q.MoveBand != 0 || zone[ci]) return false;
                        if (q.freshWater < (s.fluid == GenMat.Water ? 1 : FluidPockets.FRESH_CLEAR)) return false;
                        return owner[ci] == 0 || owner[ci] == i + 1;
                    }
                    int maxCells = MaxCells(s.rx, s.rz);
                    int made = 0;

                    // ---- inlet (upstream), in flow order head -> pond ----
                    var up = new System.Collections.Generic.List<int> { pcz * mc + pcx };
                    int cur = up[0];
                    while (up.Count <= maxCells)
                    {
                        int cx = cur % mc, cz = cur / mc, best = -1;
                        for (int k = 0; k < 8; k++)
                        {
                            int qx = cx + DXS[k], qz = cz + DZS[k];
                            if (qx < 1 || qz < 1 || qx >= mc - 1 || qz >= mc - 1) continue;
                            int qi = qz * mc + qx;
                            if (H[qi] <= H[cur] || D8(H, mc, qx, qz) != cur || !CellOk(qi)) continue;
                            if (best < 0 || F[qi] > F[best] || (F[qi] == F[best] && qi < best)) best = qi;
                        }
                        if (best < 0) break;
                        bool diag = best % mc != cx && best / mc != cz;
                        if (H[best] - H[cur] > RISE_STOP * (diag ? 1.41421356f : 1f)) break;
                        up.Add(best); cur = best;
                    }
                    bool spring = false;
                    if (up.Count == 1)
                    {
                        int best = -1;
                        for (int k = 0; k < 8; k++)
                        {
                            int qi = (pcz + DZS[k]) * mc + pcx + DXS[k];
                            bool diag = DXS[k] != 0 && DZS[k] != 0;
                            if (H[qi] <= H[up[0]] || H[qi] - H[up[0]] > RISE_STOP * (diag ? 1.41421356f : 1f) || !CellOk(qi)) continue;
                            if (best < 0 || H[qi] > H[best]) best = qi;
                        }
                        if (best >= 0) { up.Add(best); spring = true; }
                    }
                    if (up.Count > 1)
                    {
                        var ctl = new System.Collections.Generic.List<float2>();
                        for (int k = up.Count - 1; k >= 1; k--) ctl.Add(new float2((up[k] % mc + 0.5f) * cv, (up[k] / mc + 0.5f) * cv));
                        ctl.Add(new float2(s.cx + 0.5f, s.cz + 0.5f));
                        var nodes = Course(ctl, spec.seed, F, mc, cv);
                        if (Levels(in spec, in view, in o, s, nodes, true, int.MinValue, float2.zero, 0f, ref st, out _) && Emit(records, nodes, s.fluid, lining, KIND_STREAM, ref st))
                        { st.inlets++; if (spring) st.springs++; made++; }
                    }

                    // ---- outlet or seep ----
                    double pondArea = math.PI * s.rx * s.rz;
                    bool overflow = (double)F[pcz * mc + pcx] * cv * cv >= RUNOFF_RATIO * pondArea;
                    bool outlet = false;
                    if (overflow)
                    {
                        var dn = new System.Collections.Generic.List<int> { pcz * mc + pcx };
                        cur = dn[0];
                        int joinCell = -1;
                        while (dn.Count <= maxCells)
                        {
                            int nx = D8(H, mc, cur % mc, cur / mc);
                            if (nx < 0) break;
                            if (W[nx] == Hydrology.RIVER && s.fluid == GenMat.Water && !rec.AtCell(nx % mc, nx / mc).IsSea) { joinCell = nx; break; }
                            if (!CellOk(nx)) break;
                            dn.Add(nx); cur = nx;
                        }
                        if (dn.Count > 1 || joinCell >= 0)
                        {
                            var ctl = new System.Collections.Generic.List<float2> { new float2(s.cx + 0.5f, s.cz + 0.5f) };
                            for (int k = 1; k < dn.Count; k++) ctl.Add(new float2((dn[k] % mc + 0.5f) * cv, (dn[k] / mc + 0.5f) * cv));
                            int joinY = int.MinValue; float2 joinP = float2.zero; float joinW = 0f;
                            if (joinCell >= 0) st.joinTries++;
                            bool jp = joinCell >= 0 && JoinPoint(in spec, in view, ctl[ctl.Count - 1], joinCell, mc, cv, segBuf, out joinP, out joinY, out joinW);
                            if (joinCell >= 0 && !jp) st.joinNoPoint++;
                            if (jp && joinY > s.level) st.joinHigh++;
                            if (jp && joinY <= s.level) ctl.Add(joinP);
                            else joinY = int.MinValue;
                            if (ctl.Count > 1)
                            {
                                var nodes = Course(ctl, spec.seed, F, mc, cv);
                                bool okL = Levels(in spec, in view, in o, s, nodes, false, joinY, joinP, joinW, ref st, out bool joined);
                                if (joinY != int.MinValue && !joined) st.joinCut++;
                                if (okL && Emit(records, nodes, s.fluid, lining, KIND_STREAM, ref st))
                                { st.outlets++; if (joined) st.joins++; outlet = true; made++; }
                            }
                        }
                    }
                    if (!outlet)
                    {
                        // the seep: from the low rim, one cell down the low direction, tapering
                        float a = s.lowDir / 16f * 2f * math.PI;
                        float2 dir = new float2(math.cos(a), math.sin(a));
                        int r0 = 0;
                        while (r0 < 255 && FluidPockets.InSite(in s, (int)math.round(dir.x * r0), (int)math.round(dir.y * r0))) r0++;
                        float2 r0v = math.round(dir * math.max(0, r0 - 2));   // whole-voxel offsets: no runtime-dependent rounding
                        float2 c0 = new float2(s.cx + r0v.x, s.cz + r0v.y);
                        float len = SEEP_LEN;
                        for (float t = 8f; t <= SEEP_LEN; t += 8f)
                        {
                            float2 q = c0 + math.round(dir * t) + 0.5f;
                            int qi = math.clamp((int)(q.y / cv), 0, mc - 1) * mc + math.clamp((int)(q.x / cv), 0, mc - 1);
                            var cs = ShapeField.Sample(in spec, in view, in o, q.x, q.y, 1, false);
                            bool inPond = FluidPockets.InSite(in s, (int)math.floor(q.x) - s.cx, (int)math.floor(q.y) - s.cz);
                            if (!inPond && (cs.water != ColumnSample.NO_WATER_Y || !CellOk(qi) || cs.stampMat != 0 || cs.stamp3D)) { len = t - 8f; break; }
                        }
                        if (len >= 16f)
                        {
                            var nodes = new System.Collections.Generic.List<Node>
                            {
                                new Node { p = c0, hw = SEEP_HW0 },
                                new Node { p = c0 + math.round(dir * len), hw = SEEP_HW1 },
                            };
                            if (Emit(records, nodes, s.fluid, lining, KIND_SEEP, ref st)) { st.seeps++; made++; }
                        }
                    }
                    if (made == 0) st.none++;
                }
            }

            // the index (G x G cells of 2^GRID_SHIFT voxels) and the section
            int G = (world + (1 << GRID_SHIFT) - 1) >> GRID_SHIFT;
            var lists = new System.Collections.Generic.List<int>[G * G];
            int recInts = 0;
            var offs = new int[records.Count];
            for (int r = 0; r < records.Count; r++) { offs[r] = recInts; recInts += records[r].Length; }
            int idCount = 0;
            for (int r = 0; r < records.Count; r++)
            {
                int[] R = records[r];
                int gx0 = math.max(0, R[2] >> GRID_SHIFT), gz0 = math.max(0, R[3] >> GRID_SHIFT);
                int gx1 = math.min(G - 1, R[4] >> GRID_SHIFT), gz1 = math.min(G - 1, R[5] >> GRID_SHIFT);
                for (int gz = gz0; gz <= gz1; gz++)
                for (int gx = gx0; gx <= gx1; gx++)
                {
                    ref var l = ref lists[gz * G + gx];
                    if (l == null) l = new System.Collections.Generic.List<int>();
                    l.Add(r); idCount++;
                }
            }
            int idsAt = HEAD + G * G + 1, recAt = idsAt + idCount;
            var sec = new int[recAt + recInts];
            sec[0] = MAGIC; sec[1] = G; sec[2] = records.Count; sec[3] = sec.Length;
            int at = 0;
            for (int g = 0; g < G * G; g++)
            {
                sec[HEAD + g] = at;
                if (lists[g] != null) foreach (int r in lists[g]) sec[idsAt + at++] = recAt + offs[r];
            }
            sec[HEAD + G * G] = at;
            for (int r = 0; r < records.Count; r++) System.Array.Copy(records[r], 0, sec, recAt + offs[r], records[r].Length);
            st.bytes = sec.Length * 4;
            st.ms = sw.Elapsed.TotalMilliseconds;
            return sec;
        }

        /// The course through the control points: Rivers' quadratic B-spline (a head piece to the first
        /// mid point, a Bezier round every interior point, an end piece), then Rivers' MEANDER at the
        /// stream's own scale, continuously along it: a sideways displacement of MEANDER_AMP half widths
        /// whose phase advances one turn per MEANDER_WAVE half widths of length, with a seeded noise on the
        /// phase (irregular bends), faded to 0 over half a wave at both ends (the ends are kept exactly).
        /// Resampled every STEP voxels.
        /// IN DOUBLES, relative to the first control point (exact: every control point is a multiple of
        /// 0.5 voxel): node coordinates are ~30,000 voxels, where a float's step is ~0.002 voxel, and a
        /// one-ulp difference between runtimes (Mono vs IL2CPP fused multiply-adds) flipped round() on
        /// some nodes (the player's plane hash differed from the Editor's; task 209b's first launch).
        private static System.Collections.Generic.List<Node> Course(System.Collections.Generic.List<float2> ctl, uint seed, float[] F, int mc, int cv)
        {
            int nc = ctl.Count;
            double2 org = ctl[0];
            float2 off = Rivers.SeedOffset(seed) + 7.31f + ctl[0] / (4f * MEANDER_WAVE * HW_MAX);
            var c = new double2[nc];
            for (int k = 0; k < nc; k++) c[k] = (double2)ctl[k] - org;
            var poly = new System.Collections.Generic.List<double2> { c[0] };
            void Line(double2 a, double2 b) { for (int q = 1; q <= 4; q++) poly.Add(a + (b - a) * (q / 4.0)); }
            if (nc == 2) Line(c[0], c[1]);
            else
            {
                Line(c[0], 0.5 * (c[0] + c[1]));
                for (int k = 1; k < nc - 1; k++)
                {
                    double2 a = 0.5 * (c[k - 1] + c[k]), b = 0.5 * (c[k] + c[k + 1]), m = c[k];
                    for (int q = 1; q <= 8; q++)
                    {
                        double t = q / 8.0, u = 1.0 - t;
                        poly.Add(u * u * a + 2.0 * t * u * m + t * t * b);
                    }
                }
                Line(0.5 * (c[nc - 2] + c[nc - 1]), c[nc - 1]);
            }
            var fine = Resample(poly, 4.0);
            int nf = fine.Count;
            var arc = new double[nf];
            for (int k = 1; k < nf; k++) arc[k] = arc[k - 1] + math.length(fine[k] - fine[k - 1]);
            double total = arc[nf - 1], phase = 0.0;
            var bent = new System.Collections.Generic.List<double2>(nf);
            for (int k = 0; k < nf; k++)
            {
                double hw = HalfWidth(FlowAt(F, mc, cv, (float2)(fine[k] + org)));
                if (k > 0) phase += 2.0 * math.PI_DBL * (arc[k] - arc[k - 1]) / (MEANDER_WAVE * hw);
                double2 tan = math.normalizesafe(fine[math.min(nf - 1, k + 1)] - fine[math.max(0, k - 1)], new double2(1.0, 0.0));
                double2 side = new double2(-tan.y, tan.x);
                double fadeLen = 0.5 * MEANDER_WAVE * hw;
                double fade = math.smoothstep(0.0, fadeLen, arc[k]) * math.smoothstep(0.0, fadeLen, total - arc[k]);
                double th = phase + 2.0 * math.PI_DBL * MEANDER_TURN * noise.snoise((float2)(fine[k] / (4.0 * MEANDER_WAVE * HW_MAX)) + off);
                bent.Add(fine[k] + fade * MEANDER_AMP * hw * math.sin(th) * side);
            }
            var pts = Resample(bent, STEP);
            var nodes = new System.Collections.Generic.List<Node>(pts.Count);
            foreach (var q in pts)
            {
                double2 w = q + org;
                float2 p = new float2((float)math.round(w.x), (float)math.round(w.y));   // whole voxels (Emit stores ints)
                float fl = FlowAt(F, mc, cv, p), hw = HalfWidth(fl);
                nodes.Add(new Node { p = p, flow = fl, hw = hw, d = DepthOf(hw) });
            }
            return nodes;
        }

        /// Points every `step` along a polyline (by arc length), its first and last point kept exactly.
        private static System.Collections.Generic.List<double2> Resample(System.Collections.Generic.List<double2> poly, double step)
        {
            var o = new System.Collections.Generic.List<double2> { poly[0] };
            double carry = 0.0;
            for (int k = 1; k < poly.Count; k++)
            {
                double2 a = poly[k - 1], b = poly[k];
                double l = math.length(b - a), t0 = 0.0;
                if (l <= 0.0) continue;
                while (carry + (l - t0) >= step)
                {
                    t0 += step - carry; carry = 0.0;
                    o.Add(a + (b - a) * (t0 / l));
                }
                carry += l - t0;
            }
            double2 end = poly[poly.Count - 1];
            if (math.length(o[o.Count - 1] - end) > 0.5)
            {
                if (carry < step * 0.5 && o.Count > 1) o[o.Count - 1] = end;
                else o.Add(end);
            }
            return o;
        }

        private static float FlowAt(float[] F, int mc, int cv, float2 p)
            => F[math.clamp((int)(p.y / cv), 0, mc - 1) * mc + math.clamp((int)(p.x / cv), 0, mc - 1)];

        /// Is the node within the pond or the flat reach past its shore (FLAT_OUT + half width)?
        private static bool NearPond(in FluidPockets.Site s, float2 p, float hw)
        {
            int dx = (int)math.floor(p.x) - s.cx, dz = (int)math.floor(p.y) - s.cz;
            if (FluidPockets.InSite(in s, dx, dz)) return true;
            int r = (int)math.ceil(hw + FLAT_OUT);
            for (int k = 0; k < 16; k++)
            {
                float a = k / 16f * 2f * math.PI;
                if (FluidPockets.InSite(in s, dx + (int)math.round(math.cos(a) * r), dz + (int)math.round(math.sin(a) * r))) return true;
            }
            return false;
        }

        /// The levels of a course (see the header). Inlet: flow order head -> pond, every level >= L;
        /// outlet: pond -> end, every level <= L (and >= joinY when it joins a river). A node that
        /// fails the column keep-outs TRUNCATES the course (inlet: the head moves down past it;
        /// outlet: the course ends before it, and does not join). False = nothing left.
        private static bool Levels(in TerrainSpec spec, in MapView view, in Offsets o, in FluidPockets.Site s,
                                   System.Collections.Generic.List<Node> nodes, bool inlet, int joinY, float2 joinP, float joinW,
                                   ref Stats st, out bool joined)
        {
            joined = false;
            int cnt = nodes.Count;
            var near = new byte[cnt];   // 1 = the pond's flat reach, 2 = the join's flat reach
            var target = new int[cnt];
            int firstBad = -1, lastBad = -1;
            for (int k = 0; k < cnt; k++)
            {
                var nd = nodes[k];
                if (NearPond(in s, nd.p, nd.hw)) { near[k] = 1; continue; }
                if (joinY != int.MinValue && math.length(nd.p - joinP) < joinW + nd.hw + FLAT_OUT) { near[k] = 2; continue; }
                var c = ShapeField.Sample(in spec, in view, in o, nd.p.x + 0.5f, nd.p.y + 0.5f, 1, false);   // the node's column centre
                bool bad = c.water != ColumnSample.NO_WATER_Y || c.lipLo != 0 || c.stampMat != 0 || c.stamp3D || c.carvesTop ||
                           c.open1Hi > c.open1Lo || c.open2Hi > c.open2Lo || c.open3Hi > c.open3Lo || c.pkLining != 0 ||
                           (c.riverFloor != 0 && !(joinY != int.MinValue && s.fluid == GenMat.Water)) || c.height <= spec.seaLevelY + FluidPockets.SEA_CLEAR;
                if (bad) { if (firstBad < 0) firstBad = k; lastBad = k; }
                target[k] = c.height - FREEBOARD;
            }
            int from = 0, to = cnt;   // the kept nodes [from, to)
            if (inlet && lastBad >= 0) { from = lastBad + 1; st.cut++; }
            if (!inlet && firstBad >= 0) { to = firstBad; joinY = int.MinValue; st.cut++; for (int k = 0; k < to; k++) if (near[k] == 2) near[k] = 0; }
            if (!inlet && joinY == int.MinValue)
                for (int k = 0; k < to; k++)
                    if (near[k] == 2) { to = k; break; }
            if (to - from < 2) return false;
            int outside = 0;
            for (int k = from; k < to; k++) if (near[k] != 1) outside++;
            if (outside < 2) return false;
            int L = s.level;
            if (inlet)
            {
                int lv = int.MaxValue;
                for (int k = from; k < to; k++)
                {
                    lv = near[k] == 1 ? L : math.max(L, math.min(lv, target[k]));
                    var nd = nodes[k]; nd.level = lv; nodes[k] = nd;
                }
            }
            else
            {
                int lv = L;
                for (int k = from; k < to; k++)
                {
                    if (near[k] == 1) lv = L;
                    else if (near[k] == 2) lv = joinY;
                    else { lv = math.min(lv, target[k]); if (joinY != int.MinValue) lv = math.max(lv, joinY); }
                    if (near[k] != 1) lv = math.min(lv, L);
                    var nd = nodes[k]; nd.level = lv; nodes[k] = nd;
                }
                joined = joinY != int.MinValue;
            }
            if (to < cnt) nodes.RemoveRange(to, cnt - to);
            if (from > 0) nodes.RemoveRange(0, from);
            return true;
        }

        /// The river join: the nearest point of the shaped course (Rivers.GatherShaped, the pieces round
        /// the river cell) to the last dry cell's centre, the river's level there (LevelAt, the column's own
        /// rule) and its half width. False when the point is not inside a channel.
        private static bool JoinPoint(in TerrainSpec spec, in MapView view, float2 from, int cell, int mc, int cv, Rivers.Seg[] buf,
                                      out float2 p, out int y, out float w)
        {
            p = float2.zero; y = int.MinValue; w = 0f;
            float2 cc = new float2((cell % mc + 0.5f) * cv, (cell / mc + 0.5f) * cv);
            fixed (Rivers.Seg* segs = buf)
            {
                int m = view.flowDir == null ? 0 : (spec.RiverShape ? Rivers.GatherShaped(in view, Rivers.SeedOffset(spec.seed), cc, 2f * cv, segs) : Rivers.Gather(in view, cc.x, cc.y, segs));
                float best = float.MaxValue;
                for (int k = 0; k < m; k++)
                {
                    float2 ab = segs[k].b - segs[k].a;
                    float l2 = math.dot(ab, ab);
                    float t = l2 > 0f ? math.saturate(math.dot(from - segs[k].a, ab) / l2) : 0f;
                    float2 q = segs[k].a + t * ab;
                    float d = math.length(from - q);
                    if (d < best) { best = d; p = q; w = math.lerp(segs[k].wa, segs[k].wb, t); }
                }
                if (best == float.MaxValue) return false;
                p = math.floor(p) + 0.5f;   // a column centre: no runtime-dependent rounding downstream
                float L = Rivers.LevelAt(segs, m, p, 0f, out _, out _, out _, spec.RiverShape);
                if (L <= Hydrology.NO_WATER * 0.5f) return false;
                y = spec.seaLevelY + (int)math.floor(L);
            }
            return true;
        }

        private static bool Emit(System.Collections.Generic.List<int[]> records, System.Collections.Generic.List<Node> nodes, byte fluid, byte lining, int kind, ref Stats st)
        {
            int cnt = nodes.Count;
            if (cnt < 2) return false;
            var R = new int[REC_HEAD + cnt * NODE_INTS];
            R[0] = fluid | (lining << 8) | (kind << 16);
            R[1] = cnt;
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            for (int k = 0; k < cnt; k++)
            {
                var nd = nodes[k];
                int x = (int)math.round(nd.p.x), z = (int)math.round(nd.p.y);
                int hw8 = math.clamp((int)math.round(nd.hw * 8f), 1, 255), d8 = math.clamp((int)math.round(nd.d * 8f), 0, 255);
                R[REC_HEAD + k * NODE_INTS] = (x & 0xFFFFFF) | (hw8 << 24);
                R[REC_HEAD + k * NODE_INTS + 1] = (z & 0xFFFFFF) | (d8 << 24);
                R[REC_HEAD + k * NODE_INTS + 2] = nd.level;
                if (st.hx == 0) { st.hx = st.hz = st.hl = st.hw = st.hd = 1469598103934665603UL; }
                st.hx = (st.hx ^ (uint)x) * 1099511628211UL; st.hz = (st.hz ^ (uint)z) * 1099511628211UL; st.hl = (st.hl ^ (uint)nd.level) * 1099511628211UL;
                st.hw = (st.hw ^ (uint)hw8) * 1099511628211UL; st.hd = (st.hd ^ (uint)d8) * 1099511628211UL;
                float pad = nd.hw + (kind == KIND_STREAM ? REACH + 2f : 2f);
                x0 = math.min(x0, x - pad); z0 = math.min(z0, z - pad); x1 = math.max(x1, x + pad); z1 = math.max(z1, z + pad);
                if (k > 0) st.lengthM += 0.1f * math.length(nd.p - nodes[k - 1].p);
            }
            R[2] = (int)math.floor(x0); R[3] = (int)math.floor(z0); R[4] = (int)math.ceil(x1); R[5] = (int)math.ceil(z1);
            st.nodes += cnt;
            records.Add(R);
            return true;
        }

        // ------------------------------------------------------------------
        // Decoding (tests, reports)
        // ------------------------------------------------------------------
        public struct Rec { public byte fluid, lining; public int kind, count; public int4 box; public int3[] nodes; public float[] hw, depth; }

        /// Every record of a section (tests / reports).
        public static Rec[] Records(int[] plane, int mapCells, int cellVoxels)
        {
            fixed (int* pp = plane)
            {
                int* s = Section(pp, plane.Length, mapCells, cellVoxels);
                if (s == null) return new Rec[0];
                int G = s[1], cntRec = s[2], ids = HEAD + G * G + 1, total = s[HEAD + G * G];
                var seen = new System.Collections.Generic.SortedSet<int>();
                for (int k = 0; k < total; k++) seen.Add(s[ids + k]);
                var list = new System.Collections.Generic.List<Rec>();
                foreach (int r in seen)
                {
                    int* R = s + r; int cnt = R[1];
                    var rc = new Rec { fluid = (byte)(R[0] & 255), lining = (byte)((R[0] >> 8) & 255), kind = (R[0] >> 16) & 255, count = cnt,
                                       box = new int4(R[2], R[3], R[4], R[5]), nodes = new int3[cnt], hw = new float[cnt], depth = new float[cnt] };
                    for (int k = 0; k < cnt; k++)
                    {
                        int* nd = R + REC_HEAD + k * NODE_INTS;
                        rc.nodes[k] = new int3(nd[0] & 0xFFFFFF, nd[2], nd[1] & 0xFFFFFF);
                        rc.hw[k] = ((uint)nd[0] >> 24) * 0.125f; rc.depth[k] = ((uint)nd[1] >> 24) * 0.125f;
                    }
                    list.Add(rc);
                }
                return list.ToArray();
            }
        }
    }
}
