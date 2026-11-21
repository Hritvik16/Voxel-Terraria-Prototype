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
//
// TASK 179 (WORLD_GEN_LOOK_PLAN C5, finding 3 "rivers: canals, not rivers";
// spec.RiverShape, `-rivershape 0` = everything above unchanged):
//   * INCISED LEVELS (Profile): a river cell's water stands FREEBOARD under its
//     own map height (its floodplain), not DEPTH over it; taken upstream first
//     as the lowest of the cell and everything upstream of it, so a hump in the
//     bed is cut through instead of the dip behind it being filled (seed 42 had
//     949 of 3354 cells raised over their bed: the aqueducts and the "bent pipe").
//     The downstream pass (lake caps, ends, mouths, falls) is unchanged.
//   * THE COURSE (GatherShaped): each river cell has a NODE, its centre moved by
//     a meander displacement: a 2-D vector of length MEANDER_AMP half widths
//     whose direction turns with a seeded noise of wavelength MEANDER_WAVE half
//     widths -- both scale with the river's own size (FLOW_SIZE). The node keeps
//     to its valley floor: the displacement shrinks to 0 where the map ground
//     there rises or drops away from the cell's (a canyon wall, a lake, the sea),
//     and a mouth node stays on its D8 line. Displacement + half width <= one
//     cell (MEANDER_REACH), so the water stays inside the renderer's flow-map
//     reach and Gather's 9 x 9 window. The course is the quadratic B-spline of
//     the nodes: round each node X with upstream cell Y and downstream Z, a
//     Bezier from mid(Y, X) over X to mid(X, Z), cut into CURVE_SUBS straight
//     subsegments; level, width, depth and floodplain ride the same Bezier, so the
//     surface still steps one voxel at a time along the curve. A head (no river
//     upstream) adds Y -> mid(Y, X); a mouth ends mid(Y, X) -> the lake / sea cell.
//   * CARVED BANKS (Column): beside a channel, ground above the water is cut to
//     a BANK_CARVE slope rising from the water's edge; the cut is at most the
//     CUT_MIN or the river's incision (floodplain - level) + CUT_EXTRA, at
//     most CUT_LIMIT, tapering linearly to nothing at CARVE_REACH, so a valley
//     or canyon wall keeps its shape (0.2-0.6 added to its slope at most). The old bank RAISE stays for ground below the water (now rare).
// None of this is tuned to a place: every term comes from the flow graph, the
// map height and the seed.
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

        // ---- task 179 (C5): incision, course, carved banks ----
        public const float FREEBOARD_MIN = 4f;        // voxels the water stands under its floodplain at the threshold
        public const float FREEBOARD_PER_LOG2 = 0.5f;
        public const float FREEBOARD_MAX = 10f;
        public const float SIDE_DROP = 20f;           // voxels: the most a lower side cell deepens a level
        public const float MEANDER_WAVE = 24f;        // meander wavelength, half widths (~12 channel widths)
        public const float MEANDER_WAVE_MIN = 6f;     // ... but at least this many cells: the nodes are one cell apart
        public const float MEANDER_AMP = 2.5f;        // node displacement, half widths
        public const float MEANDER_TURN = 0.75f;      // turns of the displacement per unit of noise
        public const float MEANDER_REACH = 80f;       // voxels: displacement + half width <= one cell
        public const float MEANDER_RISE_FREE = 4f;    // voxels the ground under a node may rise before its displacement shrinks
        public const float MEANDER_RISE_SPAN = 16f;   // ... and is gone RISE_FREE + RISE_SPAN up
        public const float MEANDER_DROP_FREE = 2f;    // the same for ground lower than the cell's (water would stand over it)
        public const float MEANDER_DROP_SPAN = 6f;
        public const int CURVE_SUBS = 3;              // straight subsegments per Bezier
        public const float BANK_CARVE = 0.5f;         // voxels up per voxel from the water's edge (27 deg, = SHAPED_SHELF)
        public const float SHAPED_EDGE = 1.5f;        // the shaped section: voxels of water at the edge (>= 1: the edge column is wet)
        public const float SHAPED_SHELF = 0.5f;       // ... and deeper per voxel inward, so bank and bed are one slope
                                                      // (DEPTH_EDGE's 3-voxel drop at the edge made a 1 m route sample Sheer)
        public const float CUT_MIN = 20f;             // voxels a bank may be cut at the edge at least (the detail octaves stand
                                                      // up to ~2 m over the map height; the incision alone left 1 m walls)
        public const float CUT_EXTRA = 8f;            // ... or the river's incision + this, when more (a level 3 m under its
                                                      // floodplain left 1 m walls on seed 42's routes with 2 m)
        public const float CUT_LIMIT = 60f;           // ... never more; the cut tapers linearly to 0 at CARVE_REACH
        public const float CARVE_REACH = 100f;        // voxels past the edge a bank can be cut (= BANK_REACH)
        public static float Freeboard(int size) => math.min(FREEBOARD_MAX, FREEBOARD_MIN + FREEBOARD_PER_LOG2 * (size / 16f));

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
            => Profile(n, height, water, flow, cls, parent, order, flowDir, cap, false, ref st);

        /// `incise` (task 179, spec.RiverShape): levels FREEBOARD under the bed
        /// instead of DEPTH over it, upstream first (see the header).
        public static unsafe void Profile(int n, float* height, float* water, float* flow, byte* cls,
                                          int* parent, int* order, ushort* flowDir, float* cap, bool incise, ref Hydrology.Stats st)
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
            if (incise)
            {
                // Task 179: each river cell's target is FREEBOARD under its own
                // height, then the lowest of it and everything upstream (reverse pop
                // order = children before parents), so levels never rise downstream
                // without being raised. Kept in `water` (Hydrology's bed + depth for
                // river cells, which only this method reads).
                for (int i = 0; i < count; i++)
                {
                    if (cls[i] != Hydrology.RIVER) continue;
                    // Also under the LOWER of the two cells across the flow (dry or
                    // river land): the drainage parents predate the erosion blur and
                    // the routes, so a river cell can sit above a side neighbour, and
                    // its water stood over the ground there (task 179: 5-7 % of banks
                    // perched >= 0.5 m). At most SIDE_DROP deeper: a slot, not a gorge.
                    float h = height[i], side = h;
                    int p = parent[i];
                    if (p >= 0)
                    {
                        int x = i % n, z = i / n, dx = p % n - x, dz = p / n - z;
                        for (int sgn = -1; sgn <= 1; sgn += 2)
                        {
                            int sx = x - sgn * dz, sz = z + sgn * dx;
                            if (sx < 0 || sz < 0 || sx >= n || sz >= n) continue;
                            int k = sz * n + sx;
                            if (height[k] > 0f && cls[k] != Hydrology.LAKE) side = math.min(side, height[k]);
                        }
                    }
                    water[i] = math.min(h - Freeboard(SizeOf(flow[i])) - math.min(SIDE_DROP, h - side), cap[i]);
                }
                // A lake's OUTLET (the river cell its spill cell drains to) keeps the
                // pre-179 level, the lake's (capped at bed + depth): incised, it
                // stood under the lake's rim, which buried the river's first cells
                // (task 179: dry channel sections next to lakes).
                for (int i = 0; i < count; i++)
                {
                    int p = parent[i];
                    if (cls[i] != Hydrology.LAKE || p < 0 || cls[p] != Hydrology.RIVER) continue;
                    water[p] = math.max(water[p], math.min(height[p] + Depth(SizeOf(flow[p])), cap[p]));
                }
                for (int k = count - 1; k >= 0; k--)
                {
                    int c = order[k];
                    if (cls[c] != Hydrology.RIVER) continue;
                    int p = parent[c];
                    if (p >= 0 && cls[p] == Hydrology.RIVER && water[c] < water[p]) water[p] = water[c];
                }
            }
            // Levels, downstream first.
            for (int k = 0; k < count; k++)
            {
                int c = order[k];
                flowDir[c] = 0;
                if (cls[c] != Hydrology.RIVER) continue;
                int size = SizeOf(flow[c]);
                float target = incise ? water[c] : math.min(height[c] + Depth(size), cap[c]);
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
            public float fa, fb;     // floodplain: the map height under the node, >= level (task 179's shaped course only)
        }

        /// Room for GatherShaped (9 x 9 cells, each a head piece + CURVE_SUBS,
        /// culled to the query's reach); Gather's 7 x 7 needs 49.
        public const int MAX_SEGS = 160;
        private const int WIN = 4, MEMO = 2 * (WIN + 2) + 1;   // node memo: offsets -6..6 (a piece reads its cell's grandparent)

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

        // ---- task 179: the shaped course ----
        private struct Nd { public float2 p; public float l, w, d, f; }

        private static Nd Mid(in Nd a, in Nd b) => new Nd { p = 0.5f * (a.p + b.p), l = 0.5f * (a.l + b.l), w = 0.5f * (a.w + b.w), d = 0.5f * (a.d + b.d), f = 0.5f * (a.f + b.f) };

        private static Nd Bez(in Nd a, in Nd c, in Nd b, float t)
        {
            float u = 1f - t, wa = u * u, wc = 2f * t * u, wb = t * t;
            return new Nd { p = wa * a.p + wc * c.p + wb * b.p, l = wa * a.l + wc * c.l + wb * b.l, w = wa * a.w + wc * c.w + wb * b.w,
                            d = wa * a.d + wc * c.d + wb * b.d, f = wa * a.f + wc * c.f + wb * b.f };
        }

        /// The seed's offset into the meander noise.
        public static float2 SeedOffset(uint seed)
        {
            uint h = math.hash(new uint2(seed, 0x51A7C0u));
            return new float2(h & 1023u, (h >> 10) & 1023u) + 0.37f;
        }

        /// Bilinear map height between cell centres (the valley test of a node).
        private static unsafe float HeightAt(in MapView map, float2 q)
        {
            int n = map.cells;
            float2 g = q / map.cellVoxels - 0.5f;
            int x0 = (int)math.floor(g.x), z0 = (int)math.floor(g.y);
            float fx = g.x - x0, fz = g.y - z0;
            int xa = math.clamp(x0, 0, n - 1), xb = math.clamp(x0 + 1, 0, n - 1);
            int za = math.clamp(z0, 0, n - 1), zb = math.clamp(z0 + 1, 0, n - 1);
            float h0 = math.lerp(map.height[za * n + xa], map.height[za * n + xb], fx);
            float h1 = math.lerp(map.height[zb * n + xa], map.height[zb * n + xb], fx);
            return math.lerp(h0, h1, fz);
        }

        /// A cell's NODE (see the header): its centre, moved SIDEWAYS (across the
        /// flow two cells on, which smooths D8's 45 deg alternation) by the meander
        /// displacement when it is a river cell with a river downstream.
        public static unsafe float2 NodePos(in MapView map, float2 off, int cx, int cz)
        {
            float cv = map.cellVoxels;
            int n = map.cells, k = cz * n + cx;
            float2 ctr = new float2((cx + 0.5f) * cv, (cz + 0.5f) * cv);
            if (map.waterClass[k] != Hydrology.RIVER) return ctr;
            int f = map.flowDir[k];
            if ((f & FLOW_HAS) == 0) return ctr;
            int d = f & FLOW_DIR_MASK, px = cx + DX(d), pz = cz + DZ(d);
            if (px < 0 || pz < 0 || px >= n || pz >= n || map.waterClass[pz * n + px] != Hydrology.RIVER) return ctr;   // a mouth
            float w = HalfWidth(f >> FLOW_SIZE_SHIFT);
            float amp = math.min(MEANDER_AMP * w, MEANDER_REACH - w);
            if (amp <= 0f) return ctr;
            float2 ahead = new float2(px - cx, pz - cz);
            int fp = map.flowDir[pz * n + px];
            if ((fp & FLOW_HAS) != 0) ahead += new float2(DX(fp & FLOW_DIR_MASK), DZ(fp & FLOW_DIR_MASK));
            float2 side = math.normalizesafe(new float2(-ahead.y, ahead.x));
            float th = 2f * math.PI * MEANDER_TURN * noise.snoise(ctr / math.max(MEANDER_WAVE * w, MEANDER_WAVE_MIN * cv) + off);
            float2 v = amp * math.sin(th) * side;
            float2 q = ctr + v;
            int qx = math.clamp((int)math.floor(q.x / cv), 0, n - 1), qz = math.clamp((int)math.floor(q.y / cv), 0, n - 1);
            int qk = qz * n + qx;
            if (map.waterClass[qk] == Hydrology.LAKE || map.height[qk] <= 0f) return ctr;
            float dh = HeightAt(in map, q) - map.height[k];
            float kf = math.min(math.saturate(1f - (dh - MEANDER_RISE_FREE) / MEANDER_RISE_SPAN),
                                math.saturate(1f + (dh + MEANDER_DROP_FREE) / MEANDER_DROP_SPAN));
            return ctr + kf * v;
        }

        private static unsafe float2 NodeMemo(in MapView map, float2 off, int cx, int cz, int ox, int oz, float2* memo, byte* have)
        {
            int mx = cx - ox + WIN + 2, mz = cz - oz + WIN + 2;
            if (memo == null || mx < 0 || mz < 0 || mx >= MEMO || mz >= MEMO) return NodePos(in map, off, cx, cz);
            int i = mz * MEMO + mx;
            if (have[i] == 0) { memo[i] = NodePos(in map, off, cx, cz); have[i] = 1; }
            return memo[i];
        }

        /// The node values of cell (cx, cz): a river cell's own; an END (lake or
        /// sea cell) takes `up`'s width and depth and its own level, no incision.
        /// The floodplain is the map height under the (moved) node.
        private static unsafe Nd NodeOf(in MapView map, float2 off, int cx, int cz, int ox, int oz, float2* memo, byte* have, in Nd up)
        {
            int n = map.cells, k = cz * n + cx;
            var r = new Nd { p = NodeMemo(in map, off, cx, cz, ox, oz, memo, have) };
            if (map.waterClass[k] == Hydrology.RIVER)
            {
                int size = map.flowDir[k] >> FLOW_SIZE_SHIFT;
                r.l = map.water[k]; r.w = HalfWidth(size); r.d = Depth(size); r.f = math.max(HeightAt(in map, r.p), r.l);
            }
            else { r.l = map.height[k] <= 0f ? 0f : map.water[k]; r.w = up.w; r.d = up.d; r.f = r.l; }
            return r;
        }

        private static unsafe void Emit(Seg* segs, ref int m, in Nd a, in Nd b, float2 p, float reach)
        {
            if (m >= MAX_SEGS) return;
            if (reach >= 0f)
            {
                float2 ab = b.p - a.p;
                float l2 = math.dot(ab, ab);
                float t = l2 > 0f ? math.saturate(math.dot(p - a.p, ab) / l2) : 0f;
                if (math.length(p - (a.p + t * ab)) > math.max(a.w, b.w) + reach) return;
            }
            segs[m++] = new Seg { a = a.p, b = b.p, la = a.l, lb = b.l, wa = a.w, wb = b.w, da = a.d, db = b.d, fa = a.f, fb = b.f };
        }

        /// The course pieces river cell Y = (cx, cz) owns (see the header), culled
        /// to `reach` past their half width from p (reach < 0: all of them).
        private static unsafe void Pieces(in MapView map, float2 off, int cx, int cz, int ox, int oz, float2* memo, byte* have,
                                          float2 p, float reach, Seg* segs, ref int m)
        {
            int n = map.cells, k = cz * n + cx;
            int f = map.flowDir[k];
            var none = new Nd();
            Nd y = NodeOf(in map, off, cx, cz, ox, oz, memo, have, in none);
            if ((f & FLOW_HAS) == 0) { Emit(segs, ref m, in y, in y, p, reach); return; }
            int d = f & FLOW_DIR_MASK, xx = cx + DX(d), xz = cz + DZ(d), kx = xz * n + xx;
            Nd x = NodeOf(in map, off, xx, xz, ox, oz, memo, have, in y);
            Nd m1 = Mid(in y, in x);
            bool head = true;
            for (int q = 0; q < 8 && head; q++)
            {
                int qx = cx + DX(q), qz = cz + DZ(q);
                if (qx < 0 || qz < 0 || qx >= n || qz >= n) continue;
                int kq = qz * n + qx, fq = map.flowDir[kq];
                if (map.waterClass[kq] == Hydrology.RIVER && (fq & FLOW_HAS) != 0 && qx + DX(fq & FLOW_DIR_MASK) == cx && qz + DZ(fq & FLOW_DIR_MASK) == cz) head = false;
            }
            if (head) Emit(segs, ref m, in y, in m1, p, reach);
            int fx = map.waterClass[kx] == Hydrology.RIVER ? map.flowDir[kx] : 0;
            if ((fx & FLOW_HAS) == 0) { Emit(segs, ref m, in m1, in x, p, reach); return; }
            int dz = fx & FLOW_DIR_MASK, zx = xx + DX(dz), zz = xz + DZ(dz);
            Nd z = NodeOf(in map, off, zx, zz, ox, oz, memo, have, in x);
            Nd m2 = Mid(in x, in z);
            Nd prev = m1;
            for (int i = 1; i <= CURVE_SUBS; i++)
            {
                Nd cur = i == CURVE_SUBS ? m2 : Bez(in m1, in x, in m2, i / (float)CURVE_SUBS);
                Emit(segs, ref m, in prev, in cur, p, reach);
                prev = cur;
            }
        }

        /// Task 179's Gather: the shaped course's subsegments within `reach` (past
        /// their half width) of p, from the river cells of the 9 x 9 window round
        /// it. A piece lies within 0.71 cell + MEANDER_REACH of its cell's parent,
        /// so every piece within 2.2 cells - W of p is found (reach <= ~140 voxels).
        public static unsafe int GatherShaped(in MapView map, float2 off, float2 p, float reach, Seg* segs)
        {
            if (map.flowDir == null) return 0;
            float cv = map.cellVoxels;
            int n = map.cells;
            int ix = (int)math.floor(p.x / cv), iz = (int)math.floor(p.y / cv), m = 0;
            float2* memo = stackalloc float2[MEMO * MEMO];
            byte* have = stackalloc byte[MEMO * MEMO];
            for (int i = 0; i < MEMO * MEMO; i++) have[i] = 0;
            for (int j = -WIN; j <= WIN; j++)
            for (int i = -WIN; i <= WIN; i++)
            {
                int cx = ix + i, cz = iz + j;
                if (cx < 0 || cz < 0 || cx >= n || cz >= n) continue;
                if (map.waterClass[cz * n + cx] != Hydrology.RIVER) continue;
                Pieces(in map, off, cx, cz, ix, iz, memo, have, p, reach, segs, ref m);
            }
            return m;
        }

        /// Tests: the course pieces river cell (cx, cz) owns, in order (shaped: a
        /// head piece, then the curve round its parent; else Gather's straight
        /// segment to the parent).
        public static unsafe int CellCourse(in MapView map, uint seed, bool shaped, int cx, int cz, Seg* segs)
        {
            int n = map.cells, k = cz * n + cx, m = 0;
            if (map.flowDir == null || map.waterClass[k] != Hydrology.RIVER) return 0;
            if (shaped) { Pieces(in map, SeedOffset(seed), cx, cz, cx, cz, null, null, float2.zero, -1f, segs, ref m); return m; }
            float cv = map.cellVoxels;
            int f = map.flowDir[k];
            var s = new Seg { a = new float2((cx + 0.5f) * cv, (cz + 0.5f) * cv), la = map.water[k], wa = HalfWidth(f >> FLOW_SIZE_SHIFT), da = Depth(f >> FLOW_SIZE_SHIFT) };
            s.b = s.a; s.lb = s.la; s.wb = s.wa; s.db = s.da;
            if ((f & FLOW_HAS) != 0)
            {
                int d = f & FLOW_DIR_MASK, px = cx + DX(d), pz = cz + DZ(d);
                s.b = new float2((px + 0.5f) * cv, (pz + 0.5f) * cv);
            }
            segs[m++] = s;
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
        public static unsafe float LevelAt(Seg* segs, int m, float2 p, float extra, out float bedDepth, out float maxDepth, out float bank, bool nearest = false)
            => LevelAt(segs, m, p, extra, out bedDepth, out maxDepth, out bank, out _, out _, out _, nearest);

        /// Task 179: also the NEAREST channel's level `nearL`, its edge distance
        /// `nearE` (BANK_REACH when none is nearer) and its incision `nearI`
        /// (floodplain - level, >= 0) when none covers p.
        public static unsafe float LevelAt(Seg* segs, int m, float2 p, float extra, out float bedDepth, out float maxDepth, out float bank,
                                           out float nearL, out float nearE, out float nearI, bool nearest = false)
        {
            float bestD = float.MaxValue;
            float lv = Hydrology.NO_WATER; bedDepth = 0f; maxDepth = 0f; bank = Hydrology.NO_WATER;
            nearL = Hydrology.NO_WATER; nearI = 0f;
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
                    // Task 179 (`nearest`, the shaped course): the channel whose
                    // centre line is nearest, so a bend's inside takes its own
                    // piece's level, not the highest piece reaching over it.
                    if (nearest ? d < bestD : L > lv)
                    {
                        lv = L; bestD = d;
                        float shelf = nearest ? SHAPED_EDGE + SHAPED_SHELF * (w - d) : DEPTH_EDGE + SHELF * (w - d);
                        maxDepth = math.min(MAX_DEPTH, shelf);
                        bedDepth = math.min(math.lerp(s.da, s.db, t), maxDepth);
                    }
                }
                else if (d - w < nearestEdge)
                {
                    nearestEdge = d - w; bank = L - nearestEdge * BANK_SLOPE;
                    nearL = L; nearI = math.max(0f, math.lerp(s.fa, s.fb, t) - L);
                }
            }
            nearE = nearestEdge;
            if (lv > Hydrology.NO_WATER * 0.5f) { bank = Hydrology.NO_WATER; nearL = Hydrology.NO_WATER; nearE = BANK_REACH; }
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
            bool shaped = spec.RiverShape;
            Seg* segs = stackalloc Seg[MAX_SEGS];
            float extra = texelVoxels > 1 ? texelVoxels * 0.5f : 0f;
            float2 p = new float2(x, z);
            // Task 179: every query below is within this of a channel's edge
            // (the bank / carve reach, KEEP_OUT, the LIP rule's 8 neighbours).
            float reach = extra + math.max(math.max(BANK_REACH, CARVE_REACH), KEEP_OUT + extra) + 2f;
            int m = shaped ? GatherShaped(in map, SeedOffset(spec.seed), p, reach, segs) : Gather(in map, x, z, segs);
            if (m == 0) return;
            nearRiver = Near(segs, m, p, KEEP_OUT + extra, out float nearLevel);
            // No cave above the deepest a river nearby can be (MAX_DEPTH under its
            // level): a tunnel breaking out of a valley wall must not open beside
            // or under its water. DetailField reads riverFloor.
            if (nearRiver) c.riverFloor = spec.seaLevelY + (int)math.floor(nearLevel) - MAX_DEPTH - 2;
            float L = LevelAt(segs, m, p, extra, out float depth, out float maxDepth, out float bank, out float nearL, out float nearE, out float nearI, shaped);
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
                if (shaped && nearE < CARVE_REACH)
                {
                    // Task 179: CARVED BANK -- ground above the water is cut to a
                    // BANK_CARVE slope from the edge; the cut is at most CUT_MIN or
                    // the incision + CUT_EXTRA (<= CUT_LIMIT), tapering linearly to
                    // nothing at CARVE_REACH; never below a lake's rim.
                    int capY = sea + (int)math.floor(nearL + BANK_CARVE * nearE);
                    float cut = math.min(CUT_LIMIT, math.max(CUT_MIN, nearI + CUT_EXTRA)) * math.saturate(1f - nearE / CARVE_REACH);
                    int to = math.max(capY, c.height - (int)math.floor(cut));
                    if (rimY != int.MinValue) to = math.max(to, rimY);
                    if (to < c.height) c.height = to;
                }
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
                float Ln = LevelAt(segs, m, p + new float2(dx, dz), 0f, out _, out _, out _, shaped);
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
