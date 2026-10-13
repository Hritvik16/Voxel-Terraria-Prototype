// Assets/CoreEngine/WorldGen/TerrainGenChunkSource.ShellFill.cs
//
// Engine step 11d (task 83; owner Q44 (a), 2026-10-12; Amendment 8.14 row 13's
// fill note): SHELL CAVE FILL. Below the surface, shells >= 3 (FIRST_FILL_SHELL,
// 275 m+ on High, the same shell index on every preset) are NOT equal to the
// generator. After generation and the far edit ops, GenerateShellChunk runs
// this pass on the coarse grid:
//   OPEN       a pass voxel (air, a fluid, the river lip) is OPEN when its
//              sample lies above h - V (h = the column's ground, V = one shell
//              voxel: the "fill zone" is everything at least one voxel below
//              the top voxel), when it lies in an edit op's box (+-1 voxel: a
//              far crater or tunnel stays open across a chunk face), when it
//              sits on a chunk face whose neighbour across the face is above
//              that neighbour column's h - V (the halo, one ShapeField sample
//              per face column), or when it is 6-connected inside the chunk
//              to an open voxel. Everything else in the fill zone is ENCLOSED.
//   MOUTHS     (cave entrances are points of interest, owner 2026-10-12) a
//              solid fill-zone voxel touching a seed-open voxel (outside air,
//              a halo or op seed) is sub-sampled 4 x 4 x 4 at the fine level
//              (DetailField.Material at the cell centres); it becomes AIR when
//              at least 1/4 of its UNDERGROUND sub-samples (fine y <= that
//              fine column's h, outside an arch / hoop / ledge opening: those
//              are not caves) are air or fluid, and at least 4 are. A mouth
//              half a shell voxel wide and high crossing the voxel covers
//              exactly 16 of 64 sub-samples, so it shows; the point sample of
//              row 13 can miss it. A cheap per-column prefilter (tunnel line
//              value and band, or a 3D stamp within reach; generous bounds;
//              ShellFillTests checks it against no prefilter) skips columns
//              no cave can reach. Air
//              behind an opened mouth is then reached by the flood.
//   FILL       enclosed AIR becomes the solid below it (else above it, else
//              the chunk's first solid); fluids are never filled.
//   FLATTEN    a brick that is all solid, mixed, and has no pass voxel in its
//              6-neighbourhood (a face neighbour outside the chunk counts as
//              pass when it is above its column's h - V) becomes uniform with
//              its majority material: no 512-byte body. A chunk left with
//              only uniform solid bricks and no pass voxel across its faces
//              becomes ONE material: no 16 KB directory page either.
// What a ray from outside can see is unchanged: filled voxels never touch an
// open voxel, flattened bricks touch none, and nothing at or above the top
// voxel is written (row 13's sample and top-voxel rules stay exact there).
// The one exception is a widened mouth (MOUTHS). ShellFillTests: raycasts
// before/after, mouths kept, dense bricks.
//
// THREADING: runs inside GenerateShellChunk on the caller's thread and writes
// only the ShellChunk the caller owns (single-writer, CLAUDE.md).
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngine.Memory;

namespace VoxelEngine.WorldGen
{
    public sealed partial class TerrainGenChunkSource
    {
        /// The first shell the fill runs on (owner Q44 (a): k >= 3).
        public const int FIRST_FILL_SHELL = 3;
        public static bool ShellFills(int shell) => shell >= FIRST_FILL_SHELL;

        /// Task 83's switches, read at each call. All on by default; tests turn
        /// them off for the before/after comparisons.
        public bool ShellFill = true;
        public bool ShellFillMouths = true;
        public bool ShellFillFlatten = true;
        public bool ShellFillMouthPrefilter = true;
        /// DIAGNOSTIC ONLY, off: also fill ENCLOSED fluids (the underworld's lava
        /// sea, sealed pockets). The task's rule is "fluids are never filled";
        /// this switch only measures what that rule costs (ShellNeed report).
        public bool ShellFillFluids = false;

        /// Fill diagnostics since construction, summed by every thread:
        /// [0] chunks filled + 65536 x chunks made wholly uniform, [1] enclosed air voxels filled, [2] mouth voxels
        /// opened, [3] bricks flattened, [4] dense bricks before the pass,
        /// [5] after, [6] mouth candidates sub-sampled, [7] columns sub-sampled.
        public readonly long[] FillStats = new long[8];

        /// Tests and reports: the fill over a chunk generated with ShellFill off
        /// (row 13's sample, no ops), whatever ShellFill says. Same bytes as
        /// GenerateShellChunk with the fill on.
        public void FillShell(ShellChunk output)
        {
            if (!ShellFills(output.Shell) || output.IsUniformAir) return;
            FillShellChunk(output, (1 << (output.Shell - 1)) - 1, null, true);
        }

        /// The pass over a generated (and edited) shell chunk. `half` is the
        /// sample offset generation used.
        private void FillShellChunk(ShellChunk output, int half, VoxelEngine.Streaming.EditOpSnapshot ops, bool force = false)
        {
            if ((!ShellFill && !force) || !ShellFills(output.Shell) || output.IsUniformAir) return;
            int k = output.Shell;
            int3 origin = output.Coord << (CHUNK_SHIFT + k);
            var boxes = new NativeList<int3>(Allocator.TempJob);
            var stats = new NativeArray<int>(8, Allocator.TempJob);
            var pass = new NativeArray<byte>(256, Allocator.TempJob);
            var open = new NativeArray<ulong>(128 * 128 * 128 / 64, Allocator.TempJob);
            var lyOpen = new NativeArray<int>(128 * 128, Allocator.TempJob);
            var halo = new NativeArray<int>(4 * 128, Allocator.TempJob);
            try
            {
                if (ops != null)
                    for (int i = 0; i < ops.Count; i++)
                    {
                        var op = ops.Ops[i];
                        if (!VoxelEngine.Streaming.EditOpApply.TouchesChunk(op, output.Coord, k)) continue;
                        VoxelEngine.Streaming.EditOpApply.Bounds(op, out int3 lo, out int3 hi);
                        int3 l0 = math.max(((lo - origin - half) >> k) - 1, 0);
                        int3 l1 = math.min(((hi - origin - half) >> k) + 1, 127);
                        if (math.any(l1 < l0)) continue;
                        boxes.Add(l0); boxes.Add(l1);
                    }
                new ShellFillJob
                {
                    h = _h, r = _r, w = _w, cls = _cls, flow = _flow, toEngine = _toEngine, spec = Spec, o = O,
                    origin = origin, shift = k, half = half,
                    colS = output.colS, colCd = output.colCd,
                    mats = output.Mats, brickMat = output.BrickMat, brickUniform = output.BrickUniform,
                    boxes = boxes.AsArray(), mouths = ShellFillMouths, prefilter = ShellFillMouthPrefilter,
                    flatten = ShellFillFlatten, fillFluids = ShellFillFluids, stats = stats,
                    _pass = pass, _open = open, _lyOpen = lyOpen, _halo = halo,
                }.Run();
                for (int i = 0; i < 8; i++) System.Threading.Interlocked.Add(ref FillStats[i], stats[i]);
            }
            finally { boxes.Dispose(); stats.Dispose(); pass.Dispose(); open.Dispose(); lyOpen.Dispose(); halo.Dispose(); }
        }

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
        private struct ShellFillJob : IJob
        {
            [ReadOnly] public NativeArray<float> h, r, w;
            [ReadOnly] public NativeArray<byte> cls;
            [ReadOnly] public NativeArray<ushort> flow;
            [ReadOnly] public NativeArray<byte> toEngine;
            public TerrainGen.TerrainSpec spec; public TerrainGen.Offsets o;
            public int3 origin;
            public int shift, half;
            [ReadOnly] public NativeArray<TerrainGen.ColumnSample> colS;
            [ReadOnly] public NativeArray<TerrainGen.DetailField.ColumnDetail> colCd;
            public NativeArray<byte> mats;
            public NativeArray<byte> brickMat;
            public NativeArray<bool> brickUniform;
            [ReadOnly] public NativeArray<int3> boxes;   // (lo, hi) pairs, coarse local, inclusive
            public bool mouths, prefilter, flatten, fillFluids;
            public NativeArray<int> stats;

            private const int E = 128;
            private const int YS = 14, ZS = 7;   // Index(): (y << 14) | (z << 7) | x

            // Prefilter bounds (MOUTHS). TerrainGen's tunnel line is |Fbm(p / 2200, 3)| / w with
            // w in [0.7, 1.3] x 0.012; Fbm's gradient is <= 1.74 x snoise's (<= 3.5 taken) / 2200
            // per voxel. A sub-column at most 0.71 V from the coarse column can be in a passage
            // only if the coarse value is below (0.0156 + 0.71 V G) / 0.0084.
            private const float LINE_W_MAX = 0.0156f, LINE_W_MIN = 0.0084f, LINE_GRAD = 1.74f * 3.5f / 2200f;

            // Scratch the caller allocates (TempJob) and disposes; zeroed.
            public NativeArray<byte> _pass;
            public NativeArray<ulong> _open;
            public NativeArray<int> _lyOpen;    // per column: first ly whose sample is above h - V (unclamped)
            public NativeArray<int> _halo;      // 4 faces x 128: the same for the column across the face

            public unsafe void Execute()
            {
                int V = 1 << shift;
                _pass[Materials.Air] = 1;
                for (int g = 0; g <= TerrainGen.GenMat.RiverLip; g++) _pass[toEngine[g]] = 1;
                var map = new TerrainGen.MapView
                {
                    height = (float*)h.GetUnsafeReadOnlyPtr(), rough = (float*)r.GetUnsafeReadOnlyPtr(),
                    water = (float*)w.GetUnsafeReadOnlyPtr(), waterClass = (byte*)cls.GetUnsafeReadOnlyPtr(),
                    flowDir = flow.Length > 1 ? (ushort*)flow.GetUnsafeReadOnlyPtr() : null,
                    cells = spec.mapCells, cellVoxels = spec.mapCellVoxels,
                };

                int denseBefore = 0;
                for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++) if (!brickUniform[i]) denseBefore++;
                stats[4] += denseBefore;

                for (int ci = 0; ci < E * E; ci++) _lyOpen[ci] = LyOpen(colS[ci].height, V);
                for (int i = 0; i < E; i++)
                {
                    int s = (i << shift) + half;
                    _halo[i]         = LyOpen(HaloHeight(in map, origin.x - V + half, origin.z + s), V);
                    _halo[E + i]     = LyOpen(HaloHeight(in map, origin.x + (E << shift) + half, origin.z + s), V);
                    _halo[2 * E + i] = LyOpen(HaloHeight(in map, origin.x + s, origin.z - V + half), V);
                    _halo[3 * E + i] = LyOpen(HaloHeight(in map, origin.x + s, origin.z + (E << shift) + half), V);
                }

                // 1. Flood from the open seeds through pass voxels of the fill zone.
                var stack = new NativeList<int>(4096, Allocator.Temp);
                for (int ly = 0; ly < E; ly++)
                for (int lz = 0; lz < E; lz++)
                for (int lx = 0; lx < E; lx++)
                {
                    int ci = (lz << ZS) | lx;
                    if (ly >= _lyOpen[ci]) continue;
                    int v = (ly << YS) | ci;
                    if (_pass[mats[v]] == 0) continue;
                    if (TouchesSeed(lx, ly, lz)) { Mark(v); stack.Add(v); }
                }
                Flood(stack);

                var dirty = new NativeArray<byte>(EngineConfig.BRICKS_PER_CHUNK, Allocator.Temp);

                // 2. Mouths: solid fill-zone voxels touching a seed-open voxel.
                if (mouths)
                {
                    var head = new NativeArray<int>(E * E, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    for (int i = 0; i < E * E; i++) head[i] = -1;
                    var cand = new NativeList<int>(1024, Allocator.Temp);
                    var next = new NativeList<int>(1024, Allocator.Temp);
                    for (int ly = 0; ly < E; ly++)
                    for (int lz = 0; lz < E; lz++)
                    for (int lx = 0; lx < E; lx++)
                    {
                        int ci = (lz << ZS) | lx;
                        if (ly >= _lyOpen[ci]) continue;
                        int v = (ly << YS) | ci;
                        if (_pass[mats[v]] != 0 || !TouchesSeed(lx, ly, lz)) continue;
                        next.Add(head[ci]); head[ci] = cand.Length; cand.Add(v);
                    }
                    var subS = new NativeArray<TerrainGen.ColumnSample>(16, Allocator.Temp);
                    var subD = new NativeArray<TerrainGen.DetailField.ColumnDetail>(16, Allocator.Temp);
                    var widened = new NativeList<int>(256, Allocator.Temp);
                    for (int ci = 0; ci < E * E; ci++)
                    {
                        if (head[ci] < 0) continue;
                        int lx = ci & (E - 1), lz = ci >> ZS;
                        bool loaded = false;
                        for (int c = head[ci]; c >= 0; c = next[c])
                        {
                            int v = cand[c], ly = v >> YS;
                            if (prefilter && !MayHoldMouth(ci, ly, V)) continue;
                            if (!loaded)
                            {
                                for (int s = 0; s < 16; s++)
                                {
                                    int sx = origin.x + (lx << shift) + ((2 * (s & 3) + 1) << (shift - 3));
                                    int sz = origin.z + (lz << shift) + ((2 * (s >> 2) + 1) << (shift - 3));
                                    subS[s] = TerrainGen.ShapeField.Sample(in spec, in map, in o, sx + 0.5f, sz + 0.5f, 1);
                                    subD[s] = TerrainGen.DetailField.PrepareColumn(in spec, in o, sx, sz);
                                }
                                loaded = true; stats[7]++;
                            }
                            stats[6]++;
                            int und = 0, pas = 0;
                            for (int s = 0; s < 16; s++)
                            {
                                int sx = origin.x + (lx << shift) + ((2 * (s & 3) + 1) << (shift - 3));
                                int sz = origin.z + (lz << shift) + ((2 * (s >> 2) + 1) << (shift - 3));
                                var c0 = subS[s]; var d0 = subD[s];
                                for (int j = 0; j < 4; j++)
                                {
                                    int fy = origin.y + (ly << shift) + ((2 * j + 1) << (shift - 3));
                                    if (fy > c0.height) continue;   // outside air: not a mouth
                                    if ((fy >= c0.open1Lo && fy < c0.open1Hi) || (fy >= c0.open2Lo && fy < c0.open2Hi) ||
                                        (fy >= c0.open3Lo && fy < c0.open3Hi)) continue;   // an arch / hoop / ledge gap: not a cave
                                    und++;
                                    if (TerrainGen.DetailField.Material(in spec, in o, in c0, in d0, 0f, sx, fy, sz) < TerrainGen.GenMat.RiverLip) pas++;
                                }
                            }
                            if (pas >= 4 && pas * 4 >= und) widened.Add(v);
                        }
                    }
                    for (int i = 0; i < widened.Length; i++)
                    {
                        int v = widened[i];
                        mats[v] = Materials.Air; Mark(v); stack.Add(v);
                        dirty[BrickOf(v)] = 1;
                    }
                    stats[2] += widened.Length;
                    Flood(stack);
                    head.Dispose(); cand.Dispose(); next.Dispose(); subS.Dispose(); subD.Dispose(); widened.Dispose();
                }

                // 3. Fill enclosed air: the solid below (rising, so a cave fills from its floor),
                // else the solid above (falling, over what is left), else the chunk's first solid.
                int filled = 0; byte fallback = 0;
                var pending = new NativeList<int>(256, Allocator.Temp);
                for (int ly = 0; ly < E; ly++)
                for (int lz = 0; lz < E; lz++)
                for (int lx = 0; lx < E; lx++)
                {
                    int ci = (lz << ZS) | lx, v = (ly << YS) | ci;
                    byte m = mats[v];
                    if (fallback == 0 && _pass[m] == 0) fallback = m;
                    if ((m != Materials.Air && !(fillFluids && _pass[m] != 0)) || ly >= _lyOpen[ci] || IsOpen(v)) continue;
                    byte below = ly > 0 ? mats[v - (1 << YS)] : Materials.Air;
                    if (_pass[below] == 0) { mats[v] = below; filled++; dirty[BrickOf(v)] = 1; }
                    else pending.Add(v);
                }
                for (int i = pending.Length - 1; i >= 0; i--)   // highest first
                {
                    int v = pending[i];
                    byte above = (v >> YS) < E - 1 ? mats[v + (1 << YS)] : Materials.Air;
                    byte m = _pass[above] == 0 ? above : fallback;
                    if (m == Materials.Air) continue;   // no solid anywhere in the chunk: leave it
                    mats[v] = m; filled++; dirty[BrickOf(v)] = 1;
                }
                pending.Dispose();
                stats[1] += filled;
                stack.Dispose();

                for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++) if (dirty[bi] != 0) Recompute(bi);

                // 4. Flatten interior mixed bricks.
                if (flatten)
                {
                    var hist = new NativeArray<int>(256, Allocator.Temp);
                    for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++)
                    {
                        if (brickUniform[bi]) continue;
                        int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
                        if (!Interior(bx, by, bz)) continue;
                        for (int i = 0; i < 256; i++) hist[i] = 0;
                        for (int y = 0; y < 8; y++) for (int z = 0; z < 8; z++) for (int x = 0; x < 8; x++)
                            hist[mats[(((by << 3) + y) << YS) | (((bz << 3) + z) << ZS) | ((bx << 3) + x)]]++;
                        int best = 0;
                        for (int i = 1; i < 256; i++) if (hist[i] > hist[best]) best = i;
                        byte m = (byte)best;
                        for (int y = 0; y < 8; y++) for (int z = 0; z < 8; z++) for (int x = 0; x < 8; x++)
                            mats[(((by << 3) + y) << YS) | (((bz << 3) + z) << ZS) | ((bx << 3) + x)] = m;
                        brickUniform[bi] = true; brickMat[bi] = m;
                        stats[3]++;
                    }
                    hist.Dispose();

                    // A chunk with no pass voxel in it or across its faces is all
                    // interior: one material, so it costs no page either.
                    if (InteriorChunk())
                    {
                        var hb = new NativeArray<int>(256, Allocator.Temp);
                        for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++) hb[brickMat[bi]]++;
                        int best = 0;
                        for (int i = 1; i < 256; i++) if (hb[i] > hb[best]) best = i;
                        byte m = (byte)best;
                        for (int v = 0; v < E * E * E; v++) mats[v] = m;
                        for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++) brickMat[bi] = m;
                        hb.Dispose();
                        stats[0] += 1 << 16;   // chunks made uniform (high half)
                    }
                }

                int denseAfter = 0;
                for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++) if (!brickUniform[i]) denseAfter++;
                stats[5] += denseAfter;
                stats[0]++;
                dirty.Dispose();
            }

            /// First ly whose sample (origin.y + (ly << k) + half) is above ground - V.
            private int LyOpen(int ground, int V) => ((ground - V - origin.y - half) >> shift) + 1;

            private int HaloHeight(in TerrainGen.MapView map, int x, int z)
                => TerrainGen.ShapeField.Sample(in spec, in map, in o, x + 0.5f, z + 0.5f, 1).height;

            private static int BrickOf(int v) => (((v >> YS) >> 3) << 4) | ((((v >> ZS) & (E - 1)) >> 3) << 8) | ((v & (E - 1)) >> 3);

            private void Mark(int v) => _open[v >> 6] |= 1ul << (v & 63);
            private bool IsOpen(int v) => (_open[v >> 6] & (1ul << (v & 63))) != 0;

            /// Open without the flood: in an op box, or across a chunk face whose
            /// neighbour is above its column's h - V. (Above the fill zone is
            /// open by height and never flooded.)
            private bool SeedOpen(int lx, int ly, int lz)
            {
                if ((lx == 0 && ly >= _halo[lz]) || (lx == E - 1 && ly >= _halo[E + lz]) ||
                    (lz == 0 && ly >= _halo[2 * E + lx]) || (lz == E - 1 && ly >= _halo[3 * E + lx]) ||
                    (ly == E - 1 && E >= _lyOpen[(lz << ZS) | lx]))
                    return true;
                for (int i = 0; i < boxes.Length; i += 2)
                {
                    int3 a = boxes[i], b = boxes[i + 1];
                    if (lx >= a.x && lx <= b.x && ly >= a.y && ly <= b.y && lz >= a.z && lz <= b.z) return true;
                }
                return false;
            }

            /// The voxel is a seed itself, or a 6-neighbour inside the chunk is a
            /// pass voxel that is open by height or a seed.
            private bool TouchesSeed(int lx, int ly, int lz)
            {
                return SeedNeighbour(lx - 1, ly, lz) || SeedNeighbour(lx + 1, ly, lz) ||
                       SeedNeighbour(lx, ly - 1, lz) || SeedNeighbour(lx, ly + 1, lz) ||
                       SeedNeighbour(lx, ly, lz - 1) || SeedNeighbour(lx, ly, lz + 1) ||
                       SeedOpen(lx, ly, lz);
            }

            private bool SeedNeighbour(int lx, int ly, int lz)
            {
                if ((uint)lx >= E || (uint)ly >= E || (uint)lz >= E) return false;
                int ci = (lz << ZS) | lx;
                if (_pass[mats[(ly << YS) | ci]] == 0) return false;
                return ly >= _lyOpen[ci] || SeedOpen(lx, ly, lz);
            }

            private void Flood(NativeList<int> stack)
            {
                while (stack.Length > 0)
                {
                    int v = stack[stack.Length - 1];
                    stack.RemoveAt(stack.Length - 1);
                    int lx = v & (E - 1), lz = (v >> ZS) & (E - 1), ly = v >> YS;
                    if (lx > 0) Visit(v - 1, lx - 1, ly, lz, stack);
                    if (lx < E - 1) Visit(v + 1, lx + 1, ly, lz, stack);
                    if (lz > 0) Visit(v - (1 << ZS), lx, ly, lz - 1, stack);
                    if (lz < E - 1) Visit(v + (1 << ZS), lx, ly, lz + 1, stack);
                    if (ly > 0) Visit(v - (1 << YS), lx, ly - 1, lz, stack);
                    if (ly < E - 1) Visit(v + (1 << YS), lx, ly + 1, lz, stack);
                }
            }

            private void Visit(int v, int lx, int ly, int lz, NativeList<int> stack)
            {
                if (ly >= _lyOpen[(lz << ZS) | lx] || IsOpen(v) || _pass[mats[v]] == 0) return;
                Mark(v); stack.Add(v);
            }

            /// The prefilter: can a cave (a tunnel passage or a 3D stamp: cave
            /// mouth, fissure, skull cave, vents) reach this coarse voxel's
            /// footprint? (Generous bounds; a false "yes" only costs time.)
            private bool MayHoldMouth(int ci, int ly, int V)
            {
                var cd = colCd[ci]; var c = colS[ci];
                if (c.stamp3D) return true;
                // A stamp's footprint (<= 1,100 voxels) never leaves its cell (Stamps' header).
                float2 p = new float2(origin.x + ((ci & (E - 1)) << shift) + half + 0.5f, origin.z + ((ci >> ZS) << shift) + half + 0.5f);
                if (TerrainGen.Stamps.StampAt(in o, p, out var st) && math.distance(p, st.pos) < 1100f + 2f * V) return true;
                float tMax = (LINE_W_MAX + 0.71f * V * LINE_GRAD) / LINE_W_MIN;
                int yLo = origin.y + (ly << shift), yHi = yLo + V - 1;
                float pad = 4f + V * 0.25f;
                if (cd.tun0 < tMax) return true;   // follows the ground, which varies across a cliff
                if (cd.tun1 < tMax && yHi >= cd.tunY1 - cd.tunH1 - pad && yLo <= cd.tunY1 + cd.tunH1 + pad) return true;
                if (cd.tun2 < tMax && yHi >= cd.tunY2 - cd.tunH2 - pad && yLo <= cd.tunY2 + cd.tunH2 + pad) return true;
                float y3 = c.regional - cd.tunY3;
                if (cd.tun3 < tMax && yHi >= y3 - cd.tunH3 - pad - V && yLo <= y3 + cd.tunH3 + pad + V) return true;
                return false;
            }

            private bool Interior(int bx, int by, int bz)
            {
                int x0 = bx << 3, y0 = by << 3, z0 = bz << 3;
                for (int y = 0; y < 8; y++) for (int z = 0; z < 8; z++) for (int x = 0; x < 8; x++)
                    if (_pass[mats[((y0 + y) << YS) | ((z0 + z) << ZS) | (x0 + x)]] != 0) return false;
                for (int a = 0; a < 8; a++)
                for (int b = 0; b < 8; b++)
                    if (OutsidePass(x0 - 1, y0 + a, z0 + b) || OutsidePass(x0 + 8, y0 + a, z0 + b) ||
                        OutsidePass(x0 + a, y0 - 1, z0 + b) || OutsidePass(x0 + a, y0 + 8, z0 + b) ||
                        OutsidePass(x0 + a, y0 + b, z0 - 1) || OutsidePass(x0 + a, y0 + b, z0 + 8))
                        return false;
                return true;
            }

            /// Every brick uniform and solid, and no face neighbour outside the
            /// chunk above its column's h - V.
            private bool InteriorChunk()
            {
                for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++)
                    if (!brickUniform[bi] || _pass[brickMat[bi]] != 0) return false;
                for (int a = 0; a < E; a++)
                {
                    if (_halo[a] <= E - 1 || _halo[E + a] <= E - 1 || _halo[2 * E + a] <= E - 1 || _halo[3 * E + a] <= E - 1) return false;
                    for (int b = 0; b < E; b++) if (_lyOpen[(a << ZS) | b] <= E) return false;   // the top face's neighbour
                }
                return true;
            }

            /// Is voxel (lx, ly, lz) -- possibly one outside the chunk -- a pass
            /// voxel? Outside: a face neighbour above its column's h - V counts.
            private bool OutsidePass(int lx, int ly, int lz)
            {
                if (lx < 0) return ly >= _halo[lz];
                if (lx >= E) return ly >= _halo[E + lz];
                if (lz < 0) return ly >= _halo[2 * E + lx];
                if (lz >= E) return ly >= _halo[3 * E + lx];
                if (ly < 0 || ly >= E) return ly >= _lyOpen[(lz << ZS) | lx];
                return _pass[mats[(ly << YS) | (lz << ZS) | lx]] != 0;
            }

            private void Recompute(int bi)
            {
                int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
                byte first = mats[((by << 3) << YS) | ((bz << 3) << ZS) | (bx << 3)];
                bool uni = true;
                for (int y = 0; y < 8 && uni; y++)
                for (int z = 0; z < 8 && uni; z++)
                for (int x = 0; x < 8; x++)
                    if (mats[(((by << 3) + y) << YS) | (((bz << 3) + z) << ZS) | ((bx << 3) + x)] != first) { uni = false; break; }
                brickUniform[bi] = uni; brickMat[bi] = first;
            }
        }
    }
}
