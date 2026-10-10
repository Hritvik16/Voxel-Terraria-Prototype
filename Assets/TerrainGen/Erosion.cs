// Assets/TerrainGen/Erosion.cs
//
// TERRAINGEN v3, STAGE T2 -- EROSION (design doc §13). What makes land read as
// land: rivers cut branching valleys, ridges are what is left between them,
// hard rock stands up as cliffs and benches, loose slopes slump to their angle
// of repose. Runs ONCE per world, at map build, on a coarse grid (the caller
// passes 512^2 at 16 m for the sizeClass 2 island).
//
// Per iteration (v3.1: uplift + erosion to balance, see Run):
//   0. UPLIFT toward the target shape.
//   1. DRAINAGE. A priority flood from the sea (Barnes et al. 2014) every
//      FLOOD_EVERY iterations: cells visited lowest-first route water THROUGH
//      closed basins (over their spill point) and give the processing order.
//      Receivers are the steepest descent on the water surface.
//   2. AREA. Multiple-flow drainage area (split by slope) in reverse order.
//   3. STREAM POWER, implicit (Braun & Willett 2013), n = 1, m = 0.5:
//        h_i <- (h_i + F h_r) / (1 + F),   F = E_i sqrt(A_i) / d_ir
//      in visit order, so every receiver is already updated. Unconditionally
//      stable at any F; big rivers (large A) cut fast, ridges (A = 1) slowly.
//      A cell BELOW its receiver (the floor of a closed basin) is left alone:
//      that is a lake, and Hydrology fills it later.
//   4. TALUS. Any 8-neighbour slope steeper than the cell's stable slope sheds
//      a quarter of the excess downhill (mass-conserving).
//
// Deterministic: fixed visit order (ties broken by index), +, -, *, /, sqrt
// only. No 3-D noise, no randomness.
using Unity.Mathematics;

namespace TerrainGen
{
    public static class Erosion
    {
        private const float DIAG = 1.41421356f;

        public struct Stats { public int iterations; public float maxCut; public int outlets; }

        /// Rate at which land rises toward its target shape each iteration.
        public const float UPLIFT = 0.035f;
        /// Iterations between full priority floods (basin routing).
        public const int FLOOD_EVERY = 10;

        /// UPLIFT + EROSION to balance (v3.1). `target` is each cell's intended
        /// height (voxels rel. sea); the land starts at a fifth of it and rises
        /// toward it at UPLIFT per iteration while rivers cut, so every slope
        /// ends up dissected by its own valley network (a carve-down of a
        /// finished shape leaves the hillsides as smooth noise -- v3.0). Flow
        /// splits between all downhill neighbours in proportion to slope
        /// (multiple-flow area), which removes the straight parallel streaks
        /// single-direction routing draws. `erod` = E per cell, `talus` the
        /// stable rise-over-run, `layered` the hard/soft-layer archetypes.
        /// Cells with target <= 0 are sea: fixed base level. Returns the final
        /// heights in `target`'s place.
        /// `start` is the fraction of the target the land starts at (v3: 0.2,
        /// so it rises 80% of its height while rivers cut); a higher start is
        /// LESS uplift during the pass (v4, B3: a light valley pass).
        /// Burst (engine plan step 1): buffers are MapArena pointers; the
        /// scratch is released on the way out.
        public static unsafe Stats Run(MapArena* A, int n, float* target, float* erod, float* talus, bool* layered, float cellVoxels, int iterations, float start = 0.2f)
        {
            int* DX = stackalloc int[] { -1, 0, 1, -1, 1, -1, 0, 1 };
            int* DZ = stackalloc int[] { -1, -1, -1, 0, 0, 1, 1, 1 };
            int mark = A->Mark();
            int N = n * n;
            float* h = A->Alloc<float>(N);
            for (int i = 0; i < N; i++) h[i] = target[i] <= 0f ? target[i] : target[i] * start;
            int* order = A->Alloc<int>(N);
            int* parent = A->Alloc<int>(N);
            int* recv = A->Alloc<int>(N);
            float* area = A->Alloc<float>(N);
            float* fill = A->Alloc<float>(N);
            bool* visited = A->Alloc<bool>(N);
            var heap = new Heap(A, N);
            var st = new Stats { iterations = iterations };
            int count = 0;
            for (int it = 0; it < iterations; it++)
            {
                // ---- uplift toward the target ----
                for (int i = 0; i < N; i++)
                    if (target[i] > 0f && h[i] < target[i]) h[i] += UPLIFT * (target[i] - h[i]);

                // ---- basin routing: a priority flood every FLOOD_EVERY iterations ----
                if (it % FLOOD_EVERY == 0)
                {
                    count = 0;
                    heap.Clear();
                    for (int i = 0; i < N; i++) { visited[i] = false; parent[i] = i; }
                    for (int z = 0; z < n; z++)
                    for (int x = 0; x < n; x++)
                    {
                        int i = z * n + x;
                        bool edge = x == 0 || z == 0 || x == n - 1 || z == n - 1;
                        if (h[i] <= 0f || edge) { visited[i] = true; fill[i] = h[i]; heap.Push(h[i], i); }
                    }
                    st.outlets = heap.Count;
                    while (heap.Count > 0)
                    {
                        heap.Pop(out float fh, out int i);
                        order[count++] = i;
                        int x = i % n, z = i / n;
                        for (int k = 0; k < 8; k++)
                        {
                            int xx = x + DX[k], zz = z + DZ[k];
                            if (xx < 0 || zz < 0 || xx >= n || zz >= n) continue;
                            int j = zz * n + xx;
                            if (visited[j]) continue;
                            visited[j] = true;
                            parent[j] = i;
                            fill[j] = math.max(h[j], fh + 1e-3f);
                            heap.Push(fill[j], j);
                        }
                    }
                }
                // The water surface between floods: land, or a basin's standing level.
                for (int i = 0; i < N; i++) fill[i] = math.max(fill[i], h[i]);

                // ---- receivers: steepest descent on the water surface ----
                for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int i = z * n + x;
                    recv[i] = parent[i];
                    if (h[i] <= 0f) { recv[i] = i; continue; }
                    float best = 0f;
                    for (int k = 0; k < 8; k++)
                    {
                        int xx = x + DX[k], zz = z + DZ[k];
                        if (xx < 0 || zz < 0 || xx >= n || zz >= n) continue;
                        int j = zz * n + xx;
                        float s = (fill[i] - fill[j]) * ((k == 0 || k == 2 || k == 5 || k == 7) ? 0.70710678f : 1f);
                        if (s > best) { best = s; recv[i] = j; }
                    }
                }
                // ---- multiple-flow drainage area, top of the flood order first ----
                for (int i = 0; i < N; i++) area[i] = 1f;
                for (int c = count - 1; c >= 0; c--)
                {
                    int i = order[c];
                    if (h[i] <= 0f) continue;
                    int x = i % n, z = i / n;
                    float sum = 0f;
                    for (int k = 0; k < 8; k++)
                    {
                        int xx = x + DX[k], zz = z + DZ[k];
                        if (xx < 0 || zz < 0 || xx >= n || zz >= n) continue;
                        float s = fill[i] - fill[zz * n + xx];
                        if (s > 0f) sum += s * ((k == 0 || k == 2 || k == 5 || k == 7) ? 0.70710678f : 1f);
                    }
                    if (sum <= 0f) { if (parent[i] != i) area[parent[i]] += area[i]; continue; }
                    for (int k = 0; k < 8; k++)
                    {
                        int xx = x + DX[k], zz = z + DZ[k];
                        if (xx < 0 || zz < 0 || xx >= n || zz >= n) continue;
                        int j = zz * n + xx;
                        float s = fill[i] - fill[j];
                        if (s > 0f) area[j] += area[i] * s * ((k == 0 || k == 2 || k == 5 || k == 7) ? 0.70710678f : 1f) / sum;
                    }
                }
                // ---- implicit stream power along the steepest receiver ----
                for (int c = 0; c < count; c++)
                {
                    int i = order[c], r = recv[i];
                    if (r == i || h[i] <= 0f) continue;
                    if (h[i] <= h[r]) continue;                                  // basin floor: a lake later
                    int dxr = math.abs(i % n - r % n), dzr = math.abs(i / n - r / n);
                    float d = (dxr + dzr == 2) ? DIAG : 1f;
                    float e = erod[i];
                    if (layered[i] && Regions.HardLayer(h[i])) e *= 0.08f;
                    float F = e * math.sqrt(area[i]) / d;
                    h[i] = (h[i] + F * h[r]) / (1f + F);
                }
                // ---- talus ----
                for (int z = 1; z < n - 1; z++)
                for (int x = 1; x < n - 1; x++)
                {
                    int i = z * n + x;
                    if (h[i] <= 0f) continue;
                    float tal = talus[i] * cellVoxels;
                    if (layered[i] && Regions.HardLayer(h[i])) tal *= 3f;            // cap rock stands as a cliff
                    for (int k = 0; k < 8; k++)
                    {
                        int j = (z + DZ[k]) * n + x + DX[k];
                        float lim = (k == 0 || k == 2 || k == 5 || k == 7) ? tal * DIAG : tal;
                        float diff = h[i] - h[j];
                        if (diff > lim)
                        {
                            float move = (diff - lim) * 0.25f;
                            h[i] -= move; h[j] += move;
                        }
                    }
                }
            }
            for (int i = 0; i < N; i++) { st.maxCut = math.max(st.maxCut, target[i] - h[i]); target[i] = h[i]; }
            A->Release(mark);
            return st;
        }

        /// Binary min-heap of (height, index), ties by index: a fixed order.
        private unsafe struct Heap
        {
            private float* _k; private int* _v; private int _n;
            public Heap(MapArena* A, int cap) { _k = A->Alloc<float>(cap + 1); _v = A->Alloc<int>(cap + 1); _n = 0; }
            public int Count => _n;
            public void Clear() => _n = 0;
            private bool Less(int a, int b) => _k[a] < _k[b] || (_k[a] == _k[b] && _v[a] < _v[b]);
            public void Push(float k, int v)
            {
                int i = ++_n; _k[i] = k; _v[i] = v;
                while (i > 1 && Less(i, i >> 1)) { Swap(i, i >> 1); i >>= 1; }
            }
            public void Pop(out float k, out int v)
            {
                k = _k[1]; v = _v[1];
                _k[1] = _k[_n]; _v[1] = _v[_n]; _n--;
                int i = 1;
                while (true)
                {
                    int l = i << 1, r = l + 1, m = i;
                    if (l <= _n && Less(l, m)) m = l;
                    if (r <= _n && Less(r, m)) m = r;
                    if (m == i) break;
                    Swap(i, m); i = m;
                }
            }
            private void Swap(int a, int b) { float tk = _k[a]; _k[a] = _k[b]; _k[b] = tk; int tv = _v[a]; _v[a] = _v[b]; _v[b] = tv; }
        }
    }
}
