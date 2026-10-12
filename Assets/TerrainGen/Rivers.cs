// Assets/TerrainGen/Rivers.cs
//
// MAP v4 RIVERS AT REST (task 76 = RIVERS_WATERFALLS_PLAN.md R1; owner Q1 (a),
// Q2 (a), Q4 (a), 2026-10-11). Before this, a river was one water level per
// 8 m map cell (Hydrology step 5) and a column took the highest of the four
// cells round it (ShapeField.SampleWater): flat 8 m squares, 1-10 m walls of
// water between them, nothing holding them (BUGS #5, #7).
//
// MAP STAGE (Profile, once per world, after the routes):
//   * every river cell's level = its bed + depth, CAPPED by every lake it drains
//     out of (a river never stands over the lake it leaves) and RAISED to its
//     downstream cell's level (a dip in the bed fills: water never rises
//     downstream). The downstream end is the lake's level or sea level, so a
//     river ends AT the lake or the sea; its last cell stands at that level
//     when within MOUTH_FLAT of it.
//   * FlowDir: 16 bits per cell (Q4 (a)'s flow-direction map; 2 MB at 1024^2 cells, the
//     second byte is the river's size, so a column needs no catchment array): the
//     direction to the drainage parent, a fall flag (the profile drops more
//     than FALL_DROP per cell length: R4's waterfalls go there) and a slope
//     class (foam). The renderer uploads the same bytes (R2).
// COLUMN STAGE (Column, from ShapeField.Sample): a river is a CHANNEL round
// the segments cell centre -> parent centre. Its level is the segment's level
// at the column's projection (linear from cell to parent, so the surface
// steps by ONE voxel every few metres instead of metres every 8 m); its bed is
// carved to a rounded section; ground beside the channel below the level is
// raised into a bank falling at BANK_SLOPE. AT REST without a fluid-engine
// change (plan option A1, until R3's held water): where the level drops by a
// voxel, the first column of the lower step is a LIP -- solid up to the upper
// level, of material GenMat.RiverLip, which the renderer draws as water and R3
// can turn into held water. So no river voxel has air beside it at its own
// level (8 neighbours) and the bed is under it: RiverTests / FluidAtRestTests.
// Fluid is a parameter (Q5): FLUID is what Profile's rivers carry today.
using Unity.Mathematics;

namespace TerrainGen
{
    public static class Rivers
    {
        // ---- FlowDir: 16 bits per cell ----
        public const int FLOW_HAS = 0x80;       // a river cell with a downstream cell
        public const int FLOW_FALL = 0x08;      // drops more than FALL_DROP per cell length (R4)
        public const int FLOW_DIR_MASK = 0x07;  // Hydrology.Flood's neighbour order
        public const int FLOW_SLOPE_SHIFT = 4;  // bits 4-6: slope class, SLOPE_CLASS voxels per cell each
        public const int FLOW_SIZE_SHIFT = 8;   // bits 8-15: river size, 16 x log2(flow / RIVER_MIN_CELLS) (width, depth)
        public const float SLOPE_CLASS = 4f;
        public const float FALL_DROP = 20f;     // voxels (2 m) per cell length: Q3 (a)'s waterfall line
        public const float MOUTH_FLAT = 20f;    // voxels: a last cell at most this far over its lake / the sea stands AT it

        // ---- channel ----
        public const float HALF_W_MIN = 12f;    // voxels: a 2.4 m stream at the river threshold
        public const float HALF_W_PER_LOG2 = 6f;
        public const float HALF_W_MAX = 56f;    // 11 m wide
        public const float DEPTH_EDGE = 3f;     // voxels of water at the channel's edge
        public const float SHELF = 0.6f;        // voxels deeper per voxel in from the edge (31 deg, under
                                                // Danger's 45 deg Sheer): the section is a trapezoid
        public const float BANK_REACH = 100f;   // voxels past the edge a bank can be raised
        public const float BANK_SLOPE = 0.5f;   // voxels down per voxel (27 degrees)
        public const float KEEP_OUT = 24f;      // voxels from a channel's edge: no stamp opening or carve (Stamps)
        public const int MAX_DEPTH = 40;        // voxels: the deepest a river gets (< DetailField.CARVE_MIN_DEPTH)
        /// What generated rivers carry (owner Q5: a parameter; water only today).
        public const byte FLUID = GenMat.Water;

        /// River size from its catchment: 16 x log2(flow / RIVER_MIN_CELLS), 0..255.
        public static int SizeOf(float flow) => (int)math.clamp(math.round(16f * math.log2(math.max(1f, flow / Hydrology.RIVER_MIN_CELLS))), 0f, 255f);
        public static float HalfWidth(int size) => math.min(HALF_W_MAX, HALF_W_MIN + HALF_W_PER_LOG2 * (size / 16f));
        public static float Depth(int size) => 8f + 2f * (size / 16f);

        private static int DX(int d) => d == 0 || d == 3 || d == 5 ? -1 : (d == 1 || d == 6 ? 0 : 1);
        private static int DZ(int d) => d < 3 ? -1 : (d < 5 ? 0 : 1);

        /// The level a segment ends at: the downstream cell's water (river or
        /// lake), sea level for an ocean cell, NO_WATER for anything else.
        private static unsafe float EndLevel(int p, float* height, float* water, byte* cls)
        {
            if (height[p] <= 0f) return 0f;
            if (cls[p] == Hydrology.RIVER || cls[p] == Hydrology.LAKE) return water[p];
            return Hydrology.NO_WATER;
        }

        /// Map stage (see the header). `parent` / `order`: Hydrology.Run's eps
        /// flood (order = pop order, every parent before its children).
        /// `cap`: n x n scratch.
        public static unsafe void Profile(int n, float* height, float* water, float* flow, byte* cls,
                                          int* parent, int* order, ushort* flowDir, float* cap, ref Hydrology.Stats st)
        {
            int count = n * n;
            // Lake caps, upstream first (reverse pop order): a cell's cap is the
            // lowest lake level it drains out of.
            for (int i = 0; i < count; i++) cap[i] = cls[i] == Hydrology.LAKE ? water[i] : float.MaxValue;
            for (int k = count - 1; k >= 0; k--)
            {
                int c = order[k], p = parent[c];
                if (p >= 0 && cap[c] < cap[p]) cap[p] = cap[c];
            }
            st.riverFallCells = st.riverRaisedCells = 0;
            // Levels, downstream first.
            for (int k = 0; k < count; k++)
            {
                int c = order[k];
                flowDir[c] = 0;
                if (cls[c] != Hydrology.RIVER) continue;
                int size = SizeOf(flow[c]);
                float target = math.min(height[c] + Depth(size), cap[c]);
                int p = parent[c];
                float end = p >= 0 ? EndLevel(p, height, water, cls) : Hydrology.NO_WATER;
                float lv = math.max(target, end);
                if (lv > target) st.riverRaisedCells++;
                // The MOUTH: a river's last cell before a lake or the sea stands at
                // that level when it is close to it, so its channel enters the lake
                // flat and the last step is upstream, on land -- not a ribbon of
                // river water standing in the lake (BUGS #5). A higher mouth keeps
                // its step (a fall into the lake: R4).
                if (p >= 0 && cls[p] != Hydrology.RIVER && end > Hydrology.NO_WATER * 0.5f && lv - end <= MOUTH_FLAT) lv = end;
                water[c] = lv;
                flowDir[c] = (ushort)(size << FLOW_SIZE_SHIFT);
                if (p < 0 || end <= Hydrology.NO_WATER * 0.5f) continue;
                int dx = p % n - c % n, dz = p / n - c / n, d = 0;
                for (int q = 0; q < 8; q++) if (DX(q) == dx && DZ(q) == dz) d = q;
                float perCell = (lv - end) / (dx != 0 && dz != 0 ? 1.41421356f : 1f);
                int slope = (int)math.clamp(math.floor(perCell / SLOPE_CLASS), 0f, 7f);
                int b = FLOW_HAS | d | (slope << FLOW_SLOPE_SHIFT) | (size << FLOW_SIZE_SHIFT);
                if (perCell > FALL_DROP) { b |= FLOW_FALL; st.riverFallCells++; }
                flowDir[c] = (ushort)b;
            }
        }

        /// Managed entry (tests): the flow direction of a FlowDir byte as a
        /// cell offset, or zero when it has none.
        public static int2 Dir(ushort f) => (f & FLOW_HAS) == 0 ? int2.zero : new int2(DX(f & FLOW_DIR_MASK), DZ(f & FLOW_DIR_MASK));

        // ------------------------------------------------------------------
        // Column stage
        // ------------------------------------------------------------------
        public struct Seg
        {
            public float2 a, b;      // voxels: cell centre, downstream centre
            public float la, lb;     // levels (voxels rel. sea) at a and b
            public float wa, wb;     // half widths
            public float da, db;     // centre depths
        }

        public const int MAX_SEGS = 49;   // a 7 x 7 window of cells

        /// The river cells' segments within reach of (x, z). Every segment that
        /// passes within HALF_W_MAX + texel/2 + 1 of the point starts in the 7 x 7
        /// window of cells round it (a cell outside is >= 3.5 cells away; a
        /// segment is <= 1.42 cells long; the reach < 1.6 cells).
        public static unsafe int Gather(in MapView map, float x, float z, Seg* segs)
        {
            if (map.flowDir == null) return 0;
            float cv = map.cellVoxels;
            int n = map.cells;
            int ix = (int)math.floor(x / cv), iz = (int)math.floor(z / cv), m = 0;
            for (int j = -3; j <= 3; j++)
            for (int i = -3; i <= 3; i++)
            {
                int cx = ix + i, cz = iz + j;
                if (cx < 0 || cz < 0 || cx >= n || cz >= n) continue;
                int k = cz * n + cx;
                if (map.waterClass[k] != Hydrology.RIVER) continue;
                int f = map.flowDir[k];
                var s = new Seg();
                s.a = new float2((cx + 0.5f) * cv, (cz + 0.5f) * cv);
                s.la = map.water[k];
                s.wa = HalfWidth(f >> FLOW_SIZE_SHIFT);
                s.da = Depth(f >> FLOW_SIZE_SHIFT);
                s.b = s.a; s.lb = s.la; s.wb = s.wa; s.db = s.da;
                if ((f & FLOW_HAS) != 0)
                {
                    int d = f & FLOW_DIR_MASK;
                    int px = cx + DX(d), pz = cz + DZ(d), p = pz * n + px;
                    s.b = new float2((px + 0.5f) * cv, (pz + 0.5f) * cv);
                    s.lb = map.height[p] <= 0f ? 0f : map.water[p];
                    if (map.waterClass[p] == Hydrology.RIVER)
                    {
                        s.wb = HalfWidth(map.flowDir[p] >> FLOW_SIZE_SHIFT);
                        s.db = Depth(map.flowDir[p] >> FLOW_SIZE_SHIFT);
                    }
                }
                segs[m++] = s;
            }
            return m;
        }

        /// The river level (voxels rel. sea, float) over (x, z): the highest of
        /// the segments whose channel (half width + `extra`) covers it, or
        /// NO_WATER. The channel's section is a trapezoid: DEPTH_EDGE at the
        /// edge, SHELF deeper per voxel inward. `bedDepth`: the depth the bed is
        /// carved to (the trapezoid capped at the segment's centre depth);
        /// `maxDepth`: the deepest the water may be (the trapezoid capped at
        /// MAX_DEPTH -- a deeper hole is filled). `bank`: the bank level of the
        /// NEAREST channel (its level less BANK_SLOPE per voxel past its edge,
        /// within BANK_REACH) when none covers p, or NO_WATER. Nearest, not
        /// highest: past the lower end of a steep segment its bank would stand
        /// above the next, lower segment's water (site C's 5 m walls, task 76).
        public static unsafe float LevelAt(Seg* segs, int m, float2 p, float extra, out float bedDepth, out float maxDepth, out float bank)
        {
            float lv = Hydrology.NO_WATER; bedDepth = 0f; maxDepth = 0f; bank = Hydrology.NO_WATER;
            float nearestEdge = BANK_REACH;
            for (int k = 0; k < m; k++)
            {
                Seg s = segs[k];
                float2 ab = s.b - s.a;
                float l2 = math.dot(ab, ab);
                float t = l2 > 0f ? math.saturate(math.dot(p - s.a, ab) / l2) : 0f;
                float d = math.length(p - (s.a + t * ab));
                float w = math.lerp(s.wa, s.wb, t) + extra;
                float L = math.lerp(s.la, s.lb, t);
                if (d < w)
                {
                    if (L > lv)
                    {
                        lv = L;
                        float shelf = DEPTH_EDGE + SHELF * (w - d);
                        maxDepth = math.min(MAX_DEPTH, shelf);
                        bedDepth = math.min(math.lerp(s.da, s.db, t), maxDepth);
                    }
                }
                else if (d - w < nearestEdge) { nearestEdge = d - w; bank = L - nearestEdge * BANK_SLOPE; }
            }
            if (lv > Hydrology.NO_WATER * 0.5f) bank = Hydrology.NO_WATER;
            return lv;
        }

        /// Is any channel's edge closer than `reach` voxels to p? `minLevel`:
        /// the lowest such channel's level there (NO_WATER when none).
        public static unsafe bool Near(Seg* segs, int m, float2 p, float reach, out float minLevel)
        {
            minLevel = float.MaxValue;
            for (int k = 0; k < m; k++)
            {
                Seg s = segs[k];
                float2 ab = s.b - s.a;
                float l2 = math.dot(ab, ab);
                float t = l2 > 0f ? math.saturate(math.dot(p - s.a, ab) / l2) : 0f;
                if (math.length(p - (s.a + t * ab)) < math.lerp(s.wa, s.wb, t) + reach) minLevel = math.min(minLevel, math.lerp(s.la, s.lb, t));
            }
            bool near = minLevel < float.MaxValue;
            if (!near) minLevel = Hydrology.NO_WATER;
            return near;
        }

        /// The river's absolute surface Y over voxel column (x, z) (integer
        /// voxel coordinates; samples at the column centre), or int.MinValue.
        public static unsafe int SurfaceY(in TerrainSpec spec, Seg* segs, int m, int x, int z)
        {
            float L = LevelAt(segs, m, new float2(x + 0.5f, z + 0.5f), 0f, out _, out _, out _);
            return L > Hydrology.NO_WATER * 0.5f ? spec.seaLevelY + (int)math.floor(L) : int.MinValue;
        }

        /// ShapeField.Sample's river step (map v4 with a flow map), after the
        /// lake rim. `rimY`: LakeRim's rim level here (int.MinValue = no lake
        /// near): the channel never carves below it, so no lake loses a wall.
        /// `lakeWater`: the column is a lake's own water (never carved, never
        /// banked; a river higher than the lake still covers it).
        /// `holdY` (out): the highest river surface over this column or its 8
        /// neighbours (int.MinValue = none): ground at or above it holds the
        /// river here (the geyser basin's ring keeps it). `nearRiver` (out): a
        /// channel passes within KEEP_OUT voxels -- no stamp may open air here.
        public static unsafe void Column(in TerrainSpec spec, in MapView map, float x, float z, int texelVoxels,
                                         int rimY, bool lakeWater, ref ColumnSample c, out int holdY, out bool nearRiver)
        {
            holdY = int.MinValue; nearRiver = false;
            Seg* segs = stackalloc Seg[MAX_SEGS];
            int m = Gather(in map, x, z, segs);
            if (m == 0) return;
            float extra = texelVoxels > 1 ? texelVoxels * 0.5f : 0f;
            float2 p = new float2(x, z);
            nearRiver = Near(segs, m, p, KEEP_OUT + extra, out float nearLevel);
            // No cave above the deepest a river nearby can be (MAX_DEPTH under its
            // level): a tunnel breaking out of a valley wall must not open beside
            // or under its water. DetailField reads riverFloor.
            if (nearRiver) c.riverFloor = spec.seaLevelY + (int)math.floor(nearLevel) - MAX_DEPTH - 2;
            float L = LevelAt(segs, m, p, extra, out float depth, out float maxDepth, out float bank);
            if (L > Hydrology.NO_WATER * 0.5f) holdY = spec.seaLevelY + (int)math.floor(L);
            int sea = spec.seaLevelY;
            bool inChannel = false;
            if (L > Hydrology.NO_WATER * 0.5f)
            {
                int ly = sea + (int)math.floor(L);
                int bed = sea + (int)math.floor(L - depth);
                if (!lakeWater)
                {
                    int to = math.max(bed, rimY);
                    if (c.height > to) c.height = to;
                    // Ground deeper than the section allows (a filled dip, a hole)
                    // is filled to it: no underwater cliff at the edge, and
                    // never deeper than MAX_DEPTH, so riverFloor holds.
                    int floorY = ly - (int)math.floor(maxDepth);
                    if (c.height < floorY)
                    {
                        if (c.rimGround == 0) c.rimGround = c.height;
                        c.height = floorY;
                    }
                }
                if (c.height < ly && c.water < ly)
                {
                    c.water = ly; c.waterKind = 0;
                    inChannel = true;
                }
                else if (c.water > c.height) inChannel = true;   // a lake over it, at or above the river
            }
            else if (bank > Hydrology.NO_WATER * 0.5f && !lakeWater && c.height >= sea)
            {
                int by = sea + (int)math.floor(bank);
                if (c.height < by)
                {
                    if (c.rimGround == 0) c.rimGround = c.height;
                    c.height = by;
                }
            }
            if (texelVoxels > 1) return;
            // The LIP rule (texel 1 only, i.e. what the voxels are): a column
            // lower than a neighbour's river surface is raised to it.
            int rn = int.MinValue;
            for (int k = 0; k < 8; k++)
            {
                int dx = k == 0 || k == 3 || k == 5 ? -1 : (k == 1 || k == 6 ? 0 : 1);
                int dz = k < 3 ? -1 : (k < 5 ? 0 : 1);
                float Ln = LevelAt(segs, m, p + new float2(dx, dz), 0f, out _, out _, out _);
                if (Ln > Hydrology.NO_WATER * 0.5f) rn = math.max(rn, sea + (int)math.floor(Ln));
            }
            if (rn == int.MinValue) return;
            holdY = math.max(holdY, rn);
            int top = math.max(math.max(c.height, sea), c.water);
            if (rn <= top) return;
            // A column that held water (the channel's, or a lake's or the sea's
            // beside the river's end) becomes a LIP, drawn as water; a dry one
            // is a bank of its own ground.
            if (inChannel || c.water > c.height || c.height < sea) c.lipLo = c.height + 1;
            if (c.rimGround == 0) c.rimGround = c.height;
            c.height = rn;
            c.water = ColumnSample.NO_WATER_Y;
        }
    }
}
