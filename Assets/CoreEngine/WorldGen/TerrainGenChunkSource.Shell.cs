// Assets/CoreEngine/WorldGen/TerrainGenChunkSource.Shell.cs
//
// Engine step 10, COARSE-FIRST GENERATION (Amendment 8.14 §5 rows 12-13;
// ENGINE_PHASE_PLAN.md row 10): a shell-k chunk generated directly at
// 0.1 * 2^k m voxels from the world map, on the same compiled Burst path as
// Generate (step 1) -- no 0.1 m chunk is built first.
//
// A shell-k chunk is 128^3 of its own voxels, 12.8 * 2^k m on a side; its fine
// (0.1 m) origin is coord << (7 + k). Coarse voxel i samples the fine voxel
// origin + (i << k) + half, half = 2^(k-1) - 1 (0 at k = 0): one fine voxel
// below the centre, which matches the cascades' tie-to-solid vote better than
// the centre (task 74 Q2 (a), 2026-10-11; row 13 as amended).
// THE TOP VOXEL (task 74 Q1 (a)): the coarse voxel whose sample is the
// highest one at or below the column's ground height h (S, the column's top
// solid fine voxel -- Material is air/fluid above it) takes, if it is solid,
// the material of the fine voxel AT h, so a distant surface keeps its 0.3 m
// grass/sand/snow layer instead of the soil or rock a sample inside it hits.
// Every coarse voxel is therefore EXACTLY Generate's voxel at one position
// (row 13, Q5 (a)): the same S at (x + 0.5, z + 0.5), the same spacing-1
// MaxStep (its four neighbours are not on the coarse grid, so they are
// sampled here: (x + 1) + 0.5f is the float ColumnJob's grid uses), the same
// column detail and ore multiplier, and the cull record of the 8^3 FINE brick
// that holds the sample (geology is decided per fine brick). Shell 0 is
// Generate itself, byte for byte (ShellChunkTests).
//
// Render-only product (8.14 row 3): it never enters ChunkStore, is never saved
// and is never queried by IWorldQuery. Nothing reads it yet -- step 11 adds the
// shell pools. Edit ops (row 9, task 81): the overload taking an
// EditOpSnapshot applies them after generation (EditOpApply.ApplyToShell).
//
// CAVE FILL (task 83, owner Q44 (a)): shells >= 3 then fill enclosed
// underground air, open small cave mouths and flatten interior mixed bricks
// (TerrainGenChunkSource.ShellFill.cs); below the surface they are no longer
// the generator's voxels. Shells 0-2 are unchanged.
//
// BRICK SKIP (task 82b): the same proofs as Generate's (its header), over a
// brick's 8 coarse sample heights. The samples' fine bricks differ (k >= 3), so
// the brick-geology offsets are only bounded and the pocket test covers the
// fine-brick box all samples lie in; a skipped brick still fills Mats.
//
// THREADING: like Generate -- the map arrays are shared read-only; each call
// writes only the ShellChunk the caller owns (single-writer, CLAUDE.md).
using System;
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
        /// Shells 0-7: 0.1 m to 12.8 m voxels (8.14 §2a).
        public const int MAX_SHELL = 7;
        /// log2 of the 128-voxel chunk edge (EngineConfig.CHUNK_EDGE_VOXELS).
        private const int CHUNK_SHIFT = 7;

        /// Highest shell-k chunk layer that can hold anything but air.
        public int ShellTopLayer(int shell) => MaxContentY >> (CHUNK_SHIFT + shell);

        /// One shell chunk's scratch and result, owned by one caller (a worker,
        /// later; a test, today). Materials are engine ids, laid out by Index().
        public sealed class ShellChunk : IDisposable
        {
            public int Shell;
            public int3 Coord;
            /// Wholly above every sampled column's ground, water and sea: no
            /// voxel pass ran, Mats is NOT written, every brick is uniform air.
            public bool IsUniformAir;
            /// Edit ops 0..OpsApplied of the save's log are in Mats (task 82: a chunk
            /// that finishes after a newer op lands is caught up before upload).
            public int OpsApplied;
            /// ShellStreamer: this chunk re-makes one the shell already holds (an op redraw).
            public bool Redraw;
            public NativeArray<byte> Mats;          // 128^3, Index(x, y, z)
            public NativeArray<byte> BrickMat;      // 4096, (bz << 8) | (by << 4) | bx
            public NativeArray<bool> BrickUniform;
            internal NativeArray<TerrainGen.ColumnSample> colS;
            internal NativeArray<float> colSt;
            internal NativeArray<TerrainGen.DetailField.ColumnDetail> colCd;
            internal NativeArray<int> top;

            public ShellChunk(Allocator a = Allocator.Persistent)
            {
                const int E = EngineConfig.CHUNK_EDGE_VOXELS;
                Mats = new NativeArray<byte>(E * E * E, a, NativeArrayOptions.UninitializedMemory);
                BrickMat = new NativeArray<byte>(EngineConfig.BRICKS_PER_CHUNK, a);
                BrickUniform = new NativeArray<bool>(EngineConfig.BRICKS_PER_CHUNK, a);
                colS = new NativeArray<TerrainGen.ColumnSample>(E * E, a, NativeArrayOptions.UninitializedMemory);
                colSt = new NativeArray<float>(E * E, a, NativeArrayOptions.UninitializedMemory);
                colCd = new NativeArray<TerrainGen.DetailField.ColumnDetail>(E * E, a, NativeArrayOptions.UninitializedMemory);
                top = new NativeArray<int>(1, a);
            }

            public byte Get(int x, int y, int z) => IsUniformAir ? Materials.Air : Mats[Index(x, y, z)];
            /// The ground height h (fine voxels, S's top solid) of coarse column (x, z)'s sample.
            public int GroundY(int x, int z) => colS[(z << 7) | x].height;

            /// Tests and reports: this chunk becomes a copy of `o` (voxels, bricks
            /// and the column data the fill reads).
            public void CopyFrom(ShellChunk o)
            {
                Shell = o.Shell; Coord = o.Coord; IsUniformAir = o.IsUniformAir; OpsApplied = o.OpsApplied; Redraw = false;
                Mats.CopyFrom(o.Mats); BrickMat.CopyFrom(o.BrickMat); BrickUniform.CopyFrom(o.BrickUniform);
                colS.CopyFrom(o.colS); colSt.CopyFrom(o.colSt); colCd.CopyFrom(o.colCd); top.CopyFrom(o.top);
            }

            /// Bricks that need a 512-byte body.
            public int DenseBricks
            {
                get
                {
                    if (IsUniformAir) return 0;
                    int n = 0;
                    for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++) if (!BrickUniform[i]) n++;
                    return n;
                }
            }

            public void Dispose()
            {
                if (Mats.IsCreated) Mats.Dispose();
                if (BrickMat.IsCreated) BrickMat.Dispose();
                if (BrickUniform.IsCreated) BrickUniform.Dispose();
                if (colS.IsCreated) colS.Dispose();
                if (colSt.IsCreated) colSt.Dispose();
                if (colCd.IsCreated) colCd.Dispose();
                if (top.IsCreated) top.Dispose();
            }
        }

        /// 8.14 row 12's GenerateShellChunk: shell `shell`'s chunk `coord` (in
        /// that shell's own chunk units) into `output`, without edit ops (the
        /// overload below applies them).
        /// `sampleIndex` (diagnostic): which of the 2^k fine voxels per axis a
        /// coarse voxel takes; -1 = row 13's 2^(k-1) - 1. ShellChunkTests
        /// reports the old centre 2^(k-1) beside it; nothing else passes it.
        public void GenerateShellChunk(int shell, int3 coord, ShellChunk output, int sampleIndex = -1)
        {
            int half = GenerateShellChunkRaw(shell, coord, output, sampleIndex);
            FillShellChunk(output, half, null);   // task 83: shells >= 3 only
        }

        /// Generation alone (rows 12-13, exact); returns the sample offset used.
        private int GenerateShellChunkRaw(int shell, int3 coord, ShellChunk output, int sampleIndex)
        {
            if (shell < 0 || shell > MAX_SHELL) throw new ArgumentOutOfRangeException(nameof(shell));
            if (sampleIndex >= (1 << shell)) throw new ArgumentOutOfRangeException(nameof(sampleIndex));
            int3 origin = coord << (CHUNK_SHIFT + shell);
            int half = sampleIndex >= 0 ? sampleIndex : shell == 0 ? 0 : (1 << (shell - 1)) - 1;
            output.Shell = shell; output.Coord = coord; output.IsUniformAir = false; output.OpsApplied = 0;

            new ShellColumnJob
            {
                h = _h, r = _r, w = _w, cls = _cls, flow = _flow, ore = _ore, spec = Spec, o = O,
                origin = origin, shift = shell, half = half,
                colS = output.colS, colSt = output.colSt, colCd = output.colCd, top = output.top,
            }.Run();

            // The air fast path of Generate, at the coarse sample positions: above
            // max(ground, still water, sea) Material returns air (its first block).
            if (origin.y + half > output.top[0])
            {
                output.IsUniformAir = true;
                for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++) { output.BrickUniform[i] = true; output.BrickMat[i] = Materials.Air; }
                return half;
            }

            var stats = new NativeArray<int>(4, Allocator.TempJob);
            try
            {
                new ShellVoxelJob
                {
                    toEngine = _toEngine, spec = Spec, o = O, origin = origin, shift = shell, half = half,
                    colS = output.colS, colSt = output.colSt, colCd = output.colCd,
                    mats = output.Mats, brickMat = output.BrickMat, brickUniform = output.BrickUniform,
                    skip = BrickSkip, stats = stats,
                }.Run();
                AddSkipStats(stats, 4);
            }
            finally { stats.Dispose(); }
            return half;
        }

        /// Row 12 with ops: generation, then the snapshot's ops in seq order at
        /// this shell's voxel size (row 9), then task 83's fill (shells >= 3; the
        /// ops go first, so a far crater or tunnel open to the sky stays open).
        /// Returns the op count applied (the snapshot's), so a caller can tell
        /// which later ops the chunk lacks.
        public int GenerateShellChunk(int shell, int3 coord, ShellChunk output, VoxelEngine.Streaming.EditOpSnapshot ops)
        {
            int half = GenerateShellChunkRaw(shell, coord, output, -1);
            int n = 0;
            if (ops != null && ops.Count > 0)
            {
                VoxelEngine.Streaming.EditOpApply.ApplyToShell(output, ops.Ops, 0, ops.Count);
                output.OpsApplied = n = ops.Count;
            }
            FillShellChunk(output, half, ops);
            return n;
        }

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
        private struct ShellColumnJob : IJob
        {
            [ReadOnly] public NativeArray<float> h, r, w;
            [ReadOnly] public NativeArray<byte> cls;
            [ReadOnly] public NativeArray<ushort> flow;
            [ReadOnly] public NativeArray<byte> ore;
            public TerrainGen.TerrainSpec spec; public TerrainGen.Offsets o;
            public int3 origin;
            public int shift, half;
            [WriteOnly] public NativeArray<TerrainGen.ColumnSample> colS;
            [WriteOnly] public NativeArray<float> colSt;
            [WriteOnly] public NativeArray<TerrainGen.DetailField.ColumnDetail> colCd;
            public NativeArray<int> top;   // [0] = max over sampled columns of max(height, water, sea)

            public unsafe void Execute()
            {
                const int E = 128;
                var map = new TerrainGen.MapView
                {
                    height = (float*)h.GetUnsafeReadOnlyPtr(), rough = (float*)r.GetUnsafeReadOnlyPtr(),
                    water = (float*)w.GetUnsafeReadOnlyPtr(), waterClass = (byte*)cls.GetUnsafeReadOnlyPtr(),
                    flowDir = flow.Length > 1 ? (ushort*)flow.GetUnsafeReadOnlyPtr() : null,   // task 76
                    cells = spec.mapCells, cellVoxels = spec.mapCellVoxels,
                };
                int maxTop = spec.seaLevelY;
                for (int k = 0; k < E; k++)
                for (int i = 0; i < E; i++)
                {
                    int x = origin.x + (i << shift) + half, z = origin.z + (k << shift) + half, ci = k * E + i;
                    var c = TerrainGen.ShapeField.Sample(in spec, in map, in o, x + 0.5f, z + 0.5f, 1);
                    // Spacing-1 MaxStep from the four fine neighbours, at the same
                    // float coordinates as ColumnJob's grid ((x + 1) + 0.5f).
                    int ch = c.height;
                    int a = TerrainGen.ShapeField.Sample(in spec, in map, in o, (x + 1) + 0.5f, z + 0.5f, 1).height;
                    int b = TerrainGen.ShapeField.Sample(in spec, in map, in o, (x - 1) + 0.5f, z + 0.5f, 1).height;
                    int d = TerrainGen.ShapeField.Sample(in spec, in map, in o, x + 0.5f, (z + 1) + 0.5f, 1).height;
                    int e = TerrainGen.ShapeField.Sample(in spec, in map, in o, x + 0.5f, (z - 1) + 0.5f, 1).height;
                    int m = math.max(math.max(math.abs(a - ch), math.abs(b - ch)), math.max(math.abs(d - ch), math.abs(e - ch)));
                    colSt[ci] = m / 1f;
                    var cd = TerrainGen.DetailField.PrepareColumn(in spec, in o, x, z);
                    if (cd.oreMul != 0) cd.oreMul = ore[TerrainGen.Ores.CellIndex(x, z, spec.mapCells, spec.mapCellVoxels)];
                    colCd[ci] = cd;
                    colS[ci] = c;
                    maxTop = math.max(maxTop, math.max(c.height, c.water));
                }
                top[0] = maxTop;
            }
        }

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
        private struct ShellVoxelJob : IJob
        {
            [ReadOnly] public NativeArray<byte> toEngine;
            public TerrainGen.TerrainSpec spec; public TerrainGen.Offsets o;
            public int3 origin;
            public int shift, half;
            [ReadOnly] public NativeArray<TerrainGen.ColumnSample> colS;
            [ReadOnly] public NativeArray<float> colSt;
            [ReadOnly] public NativeArray<TerrainGen.DetailField.ColumnDetail> colCd;
            public NativeArray<byte> mats;
            public NativeArray<byte> brickMat;
            public NativeArray<bool> brickUniform;
            public bool skip;
            public NativeArray<int> stats;   // task 82b: SkipStats' four counts for this chunk

            public void Execute()
            {
                const int E = 128;
                const int FINE_BRICK_MASK = ~(TerrainGen.DetailField.BRICK - 1);
                // Coarse voxels per fine 8^3 brick, per axis: 8, 4, 2, then 1. The
                // cull record is valid for every sample inside one fine brick.
                int s = math.max(1, 8 >> shift);
                var bTop = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                var bMinH = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                var bMaxH = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                if (skip) BrickColumnBounds(in spec, colS, bTop, bMinH, bMaxH);
                stats[3] += EngineConfig.BRICKS_PER_CHUNK;
                int span = 7 << shift;   // fine voxels from a brick's first sample to its last, per axis
                for (int by = 0; by < 16; by++)
                for (int bz = 0; bz < 16; bz++)
                for (int bx = 0; bx < 16; bx++)
                {
                    int bi = (bz << 8) | (by << 4) | bx;
                    if (skip)
                    {
                        int bc = (bz << 4) | bx;
                        int3 f0 = origin + (new int3(bx, by, bz) << (3 + shift)) + half;   // the brick's first sample
                        byte g = 255;
                        if (f0.y > bTop[bc]) g = TerrainGen.GenMat.Air;
                        else if (f0.y + span <= bMinH[bc] || f0.y > bMaxH[bc])
                        {
                            bool noPocket = TerrainGen.DetailField.NoPocketInBox(in spec, in o, f0 & FINE_BRICK_MASK,
                                                                                 ((f0 + span) & FINE_BRICK_MASK) + TerrainGen.DetailField.BRICK);
                            g = ProveBrick(in spec, colS, colSt, colCd, origin, shift, half, bx, bz, f0.y, noPocket, false, 0f, 0f, 0f);
                        }
                        if (g != 255)
                        {
                            byte m = toEngine[g];
                            for (int vy = 0; vy < 8; vy++)
                            for (int vz = 0; vz < 8; vz++)
                            {
                                int row = (((by << 3) + vy) << 14) | (((bz << 3) + vz) << 7) | (bx << 3);
                                for (int vx = 0; vx < 8; vx++) mats[row + vx] = m;
                            }
                            brickUniform[bi] = true; brickMat[bi] = m;
                            stats[SkipKind(g)]++;
                            continue;
                        }
                    }
                    byte first = 0; bool uni = true, any = false;
                    for (int sy = 0; sy < 8; sy += s)
                    for (int sz = 0; sz < 8; sz += s)
                    for (int sx = 0; sx < 8; sx += s)
                    {
                        int fx = origin.x + (((bx << 3) + sx) << shift) + half;
                        int fy = origin.y + (((by << 3) + sy) << shift) + half;
                        int fz = origin.z + (((bz << 3) + sz) << shift) + half;
                        var cr = TerrainGen.DetailField.CullBrick(in spec, in o, fx & FINE_BRICK_MASK, fy & FINE_BRICK_MASK, fz & FINE_BRICK_MASK);
                        for (int vz = sz; vz < sz + s; vz++)
                        for (int vx = sx; vx < sx + s; vx++)
                        {
                            int lx = (bx << 3) + vx, lz = (bz << 3) + vz, ci = lz * E + lx;
                            var c = colS[ci]; var cd = colCd[ci]; float st = colSt[ci];
                            int x = origin.x + (lx << shift) + half, z = origin.z + (lz << shift) + half;
                            for (int vy = sy; vy < sy + s; vy++)
                            {
                                int ly = (by << 3) + vy;
                                byte g = TerrainGen.DetailField.Material(in spec, in o, in c, in cd, st,
                                    x, origin.y + (ly << shift) + half, z, in cr);
                                byte m = toEngine[g];
                                mats[(ly << 14) | (lz << 7) | lx] = m;
                                if (!any) { first = m; any = true; }
                                else if (m != first) uni = false;
                            }
                        }
                    }
                    brickUniform[bi] = uni;
                    brickMat[bi] = first;
                }
                bTop.Dispose(); bMinH.Dispose(); bMaxH.Dispose();

                // The top voxel (task 74 Q1 (a)). Shell 0 samples every fine
                // voxel, so its top voxel IS h already; skip it (== Generate).
                if (shift == 0) return;
                for (int lz = 0; lz < E; lz++)
                for (int lx = 0; lx < E; lx++)
                {
                    int ci = lz * E + lx;
                    var c = colS[ci];
                    int ly = (c.height - origin.y - half) >> shift;   // floor: the highest sample <= h
                    if (ly < 0 || ly >= E) continue;
                    int x = origin.x + (lx << shift) + half, z = origin.z + (lz << shift) + half;
                    int y = origin.y + (ly << shift) + half;
                    var cd = colCd[ci]; float st = colSt[ci];
                    var cs = TerrainGen.DetailField.CullBrick(in spec, in o, x & FINE_BRICK_MASK, y & FINE_BRICK_MASK, z & FINE_BRICK_MASK);
                    if (TerrainGen.DetailField.Material(in spec, in o, in c, in cd, st, x, y, z, in cs) < TerrainGen.GenMat.RiverLip)
                        continue;   // air or a fluid at the sample (an opening): not a solid top
                    var ch = TerrainGen.DetailField.CullBrick(in spec, in o, x & FINE_BRICK_MASK, c.height & FINE_BRICK_MASK, z & FINE_BRICK_MASK);
                    byte m = toEngine[TerrainGen.DetailField.Material(in spec, in o, in c, in cd, st, x, c.height, z, in ch)];
                    mats[(ly << 14) | (lz << 7) | lx] = m;
                    int bi = ((lz >> 3) << 8) | ((ly >> 3) << 4) | (lx >> 3);
                    if (brickUniform[bi] && brickMat[bi] != m) brickUniform[bi] = false;
                }
            }
        }
    }
}
