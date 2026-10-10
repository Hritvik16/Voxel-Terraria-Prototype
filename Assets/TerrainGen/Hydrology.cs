// Assets/TerrainGen/Hydrology.cs
//
// MAP STAGE v2 (§2.4, design doc §9 step 3): the non-local features a per-column
// generator can never produce. A pass over the world map's height grid, run once
// at world creation, deterministic from the seed (every tie breaks by cell index):
//
//   1. PRIORITY FLOOD from the ocean (Barnes et al. 2014), twice:
//        plain  -> every depression's SPILL LEVEL: lakes are flat, filled
//                  exactly to the height where their basin overflows
//        eps    -> a drainage surface with no sinks at all, and each cell's
//                  flood PARENT, which is a downhill path that ends in the sea
//   2. FLOW ACCUMULATION along the parents, in reverse flood order
//   3. RIVERS where the catchment exceeds RIVER_MIN_CELLS; every river drains to
//      the sea by construction (the parent chain ends at an ocean seed)
//   4. LIGHT EROSION: channels carved ~ log2(catchment), blurred into valleys,
//      then the plain flood re-run so lake levels match the carved terrain
//   5. WATER LEVEL per cell: lake -> spill level; river -> bed + depth. Still
//      water for now (§2.7): lakes are at rest; river water on a slope is not
//      (it will drain downhill once the CA wakes it) -- the channel is what the
//      map carries "from day one so flow can switch on later".
//
// Coordinates are map CELLS; heights are voxels relative to sea level (the map's
// own convention). NO_WATER marks a dry cell.
using System;
using Unity.Mathematics;

namespace TerrainGen
{
    public static class Hydrology
    {
        public const float NO_WATER = -1e9f;
        public const float FLOOD_EPS = 0.02f;         // voxels per cell on flats
        public const int RIVER_MIN_CELLS = 800;      // ~0.05 km^2 catchment (8 m cells)
        /// Per-cell water class (WorldMap.WaterClass).
        public const byte DRY = 0, LAKE = 1, RIVER = 2;
        public const float LAKE_MIN_DEPTH = 12f;     // voxels (1.2 m): shallower is a puddle, left dry.
                                                     // Was 0.3 m: the first map v2 PNG was speckled
                                                     // with hundreds of puddle "lakes" on every range.

        public struct Stats
        {
            public int landCells, riverCells, riverMouths, lakeCells, lakes;
            public float maxCarve, maxCatchmentKm2;
            public double ms;
        }


        /// Runs the pass IN PLACE on `height` (erosion) and `rough` (smoother beds),
        /// and fills `water` (level or NO_WATER) and `flow` (catchment, cells).
        /// `minLakeDepth` (optional, voxels per cell): a lake survives only if its
        /// deepest point is at least this deep there (DrainShallowLakes) -- v3
        /// regions drain small hollows in sand and plains and keep a wetland's
        /// ponds. Null = map v2's behaviour exactly.
        /// Burst (engine plan step 1): MapArena pointers, scratch released on the
        /// way out. `ms` is left 0 here (no clock under Burst); the managed
        /// caller fills it with the whole map build's time.
        public static unsafe Stats Run(MapArena* A, int n, float* height, float* rough, float* water, float* flow, byte* cls, float* minLakeDepth = null)
        {
            int mark = A->Mark();
            var st = new Stats();
            int count = n * n;
            int* parent = A->Alloc<int>(count);
            int* order = A->Alloc<int>(count);
            float* eps = A->Alloc<float>(count);

            // ---- 1. eps flood: drainage parents + order ----
            Flood(A, n, height, FLOOD_EPS, eps, parent, order);

            // ---- 2. accumulation (reverse flood order = upstream first) ----
            for (int i = 0; i < count; i++) flow[i] = height[i] > 0f ? 1f : 0f;
            for (int k = count - 1; k >= 0; k--)
            {
                int c = order[k];
                int p = parent[c];
                if (p >= 0 && height[c] > 0f) flow[p] += flow[c];
            }

            // ---- 3 + 4. rivers and light erosion ----
            float* carve = A->Alloc<float>(count);
            for (int i = 0; i < count; i++)
            {
                if (height[i] <= 0f) continue;
                st.landCells++;
                if (flow[i] < RIVER_MIN_CELLS) continue;
                float c = 15f + 12f * math.log2(flow[i] / RIVER_MIN_CELLS);
                carve[i] = math.min(80f, c);
            }
            // Two 3x3 box blurs: a channel becomes a valley ~3 cells (24 m) wide.
            float* tmp = A->Alloc<float>(count);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float s = 0f; int m = 0;
                    for (int j = -1; j <= 1; j++)
                    for (int i = -1; i <= 1; i++)
                    {
                        int xx = x + i, zz = z + j;
                        if (xx < 0 || zz < 0 || xx >= n || zz >= n) continue;
                        s += carve[zz * n + xx]; m++;
                    }
                    tmp[z * n + x] = s / m;
                }
                for (int i = 0; i < count; i++) carve[i] = tmp[i];
            }
            for (int i = 0; i < count; i++)
            {
                if (height[i] <= 0f || carve[i] <= 0f) continue;
                // Land stays land: a carved channel bottoms out just above the sea.
                float nh = math.max(2f, height[i] - carve[i]);
                st.maxCarve = math.max(st.maxCarve, height[i] - nh);
                height[i] = nh;
                rough[i] *= math.lerp(1f, 0.3f, math.saturate(carve[i] / 20f));   // smooth beds
            }

            // ---- 1 again, plain: spill levels on the carved terrain ----
            float* level = A->Alloc<float>(count);
            int* parent2 = A->Alloc<int>(count);
            int* order2 = A->Alloc<int>(count);
            Flood(A, n, height, 0f, level, parent2, order2);

            // ---- 5. water level ----
            for (int i = 0; i < count; i++)
            {
                water[i] = NO_WATER;
                cls[i] = DRY;
                if (height[i] <= 0f) continue;
                if (level[i] - height[i] >= LAKE_MIN_DEPTH) { water[i] = level[i]; cls[i] = LAKE; st.lakeCells++; continue; }
                if (flow[i] >= RIVER_MIN_CELLS)
                {
                    st.riverCells++;
                    cls[i] = RIVER;
                    water[i] = height[i] + 8f + 2f * math.log2(flow[i] / RIVER_MIN_CELLS);
                    int p = parent[i];
                    if (p >= 0 && height[p] <= 0f) st.riverMouths++;
                }
            }
            if (minLakeDepth != null) DrainShallowLakes(A, n, height, water, flow, cls, parent, minLakeDepth, ref st);
            st.lakes = CountLakes(A, n, water, cls);
            for (int i = 0; i < count; i++) st.maxCatchmentKm2 = math.max(st.maxCatchmentKm2, flow[i]);
            st.maxCatchmentKm2 *= 64f / 1e6f;   // 8 m cells
            A->Release(mark);
            return st;
        }

        /// Priority flood from every ocean cell (height <= 0) and every border
        /// cell. out[i] = max(height[i], out[parent] + eps); parent[i] = the cell
        /// that opened i (-1 for a seed); order = pop order (non-decreasing out).
        public static unsafe void Flood(MapArena* A, int n, float* height, float eps, float* outLevel, int* parent, int* order)
        {
            int* DX = stackalloc int[] { -1, 0, 1, -1, 1, -1, 0, 1 };
            int* DZ = stackalloc int[] { -1, -1, -1, 0, 0, 1, 1, 1 };
            int mark = A->Mark();
            int count = n * n;
            bool* closed = A->Alloc<bool>(count);
            var heap = new MinHeap(A, count);
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                bool seed = height[i] <= 0f || x == 0 || z == 0 || x == n - 1 || z == n - 1;
                parent[i] = -1;
                if (!seed) continue;
                outLevel[i] = height[i];
                closed[i] = true;
                heap.Push(outLevel[i], i);
            }
            int k = 0;
            while (heap.Count > 0)
            {
                heap.Pop(out float lv, out int c);
                order[k++] = c;
                int cx = c % n, cz = c / n;
                for (int d = 0; d < 8; d++)
                {
                    int x = cx + DX[d], z = cz + DZ[d];
                    if (x < 0 || z < 0 || x >= n || z >= n) continue;
                    int nb = z * n + x;
                    if (closed[nb]) continue;
                    closed[nb] = true;
                    outLevel[nb] = math.max(height[nb], lv + eps);
                    parent[nb] = c;
                    heap.Push(outLevel[nb], nb);
                }
            }
            A->Release(mark);
        }

        /// Managed entry for tests (HydrologyTests): the same code, run here.
        public static unsafe void Flood(int n, float[] height, float eps, float[] outLevel, int[] parent, int[] order)
        {
            var a = MapArena.Create();
            try { fixed (float* h = height, o = outLevel) fixed (int* p = parent, r = order) Flood(&a, n, h, eps, o, p, r); }
            finally { a.Dispose(); }
        }

        /// v3: a WHOLE lake stays or goes. Its deepest point must reach the
        /// minimum depth of the region it lies in (minLakeDepth at that cell);
        /// otherwise every cell of it is drained -- to river where the flow is a
        /// river's, dry elsewhere. Per-cell thresholds left dry shelves below a
        /// lake's own water line (HydrologyTests' containment check caught it).
        private static unsafe void DrainShallowLakes(MapArena* A, int n, float* height, float* water, float* flow, byte* cls, int* parent,
                                                     float* minLakeDepth, ref Stats st)
        {
            int* DX = stackalloc int[] { -1, 0, 1, -1, 1, -1, 0, 1 };
            int* DZ = stackalloc int[] { -1, -1, -1, 0, 0, 1, 1, 1 };
            int mark = A->Mark();
            int count = n * n;
            bool* seen = A->Alloc<bool>(count);
            int* stack = A->Alloc<int>(count);
            int* members = A->Alloc<int>(count); int nm = 0;
            for (int i = 0; i < count; i++)
            {
                if (seen[i] || cls[i] != LAKE) continue;
                nm = 0;
                float deepest = -1f; int deepCell = i;
                int sp = 0; stack[sp++] = i; seen[i] = true;
                while (sp > 0)
                {
                    int c = stack[--sp];
                    members[nm++] = c;
                    float dpt = water[c] - height[c];
                    if (dpt > deepest) { deepest = dpt; deepCell = c; }
                    int cx = c % n, cz = c / n;
                    for (int d = 0; d < 8; d++)
                    {
                        int x = cx + DX[d], z = cz + DZ[d];
                        if (x < 0 || z < 0 || x >= n || z >= n) continue;
                        int nb = z * n + x;
                        if (seen[nb] || cls[nb] != LAKE || water[nb] != water[i]) continue;
                        seen[nb] = true; stack[sp++] = nb;
                    }
                }
                if (deepest >= minLakeDepth[deepCell]) continue;
                for (int q = 0; q < nm; q++)
                {
                    int c = members[q];
                    st.lakeCells--;
                    water[c] = NO_WATER; cls[c] = DRY;
                    if (flow[c] >= RIVER_MIN_CELLS)
                    {
                        st.riverCells++;
                        cls[c] = RIVER;
                        water[c] = height[c] + 8f + 2f * math.log2(flow[c] / RIVER_MIN_CELLS);
                    }
                }
            }
            A->Release(mark);
        }

        private static unsafe int CountLakes(MapArena* A, int n, float* water, byte* cls)
        {
            int* DX = stackalloc int[] { -1, 0, 1, -1, 1, -1, 0, 1 };
            int* DZ = stackalloc int[] { -1, -1, -1, 0, 0, 1, 1, 1 };
            int mark = A->Mark();
            int count = n * n, lakes = 0;
            bool* seen = A->Alloc<bool>(count);
            int* stack = A->Alloc<int>(count);
            for (int i = 0; i < count; i++)
            {
                if (seen[i] || cls[i] != LAKE) continue;
                lakes++;
                int sp = 0; stack[sp++] = i; seen[i] = true;
                while (sp > 0)
                {
                    int c = stack[--sp];
                    int cx = c % n, cz = c / n;
                    for (int d = 0; d < 8; d++)
                    {
                        int x = cx + DX[d], z = cz + DZ[d];
                        if (x < 0 || z < 0 || x >= n || z >= n) continue;
                        int nb = z * n + x;
                        if (seen[nb] || cls[nb] != LAKE || water[nb] != water[i]) continue;
                        seen[nb] = true; stack[sp++] = nb;
                    }
                }
            }
            A->Release(mark);
            return lakes;
        }

        /// A lake cell (flat, filled to spill). River cells are NOT lakes even
        /// though their water stands above the bed: they are classed apart.
        public static bool IsLake(int i, byte[] cls) => cls[i] == LAKE;

        /// Binary min-heap on (key, index); ties break by index, so the flood's
        /// pop order -- and everything downstream of it -- is deterministic.
        private unsafe struct MinHeap
        {
            private float* _k;
            private int* _v;
            public int Count;
            public MinHeap(MapArena* A, int cap) { _k = A->Alloc<float>(cap); _v = A->Alloc<int>(cap); Count = 0; }
            private bool Less(int a, int b) => _k[a] < _k[b] || (_k[a] == _k[b] && _v[a] < _v[b]);
            private void Swap(int a, int b)
            {
                float tk = _k[a]; _k[a] = _k[b]; _k[b] = tk;
                int tv = _v[a]; _v[a] = _v[b]; _v[b] = tv;
            }
            public void Push(float key, int v)
            {
                int i = Count++;
                _k[i] = key; _v[i] = v;
                while (i > 0) { int p = (i - 1) >> 1; if (!Less(i, p)) break; Swap(i, p); i = p; }
            }
            public void Pop(out float key, out int v)
            {
                key = _k[0]; v = _v[0];
                Count--;
                if (Count == 0) return;
                _k[0] = _k[Count]; _v[0] = _v[Count];
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < Count && Less(l, m)) m = l;
                    if (r < Count && Less(r, m)) m = r;
                    if (m == i) break;
                    Swap(i, m); i = m;
                }
            }
        }
    }
}
