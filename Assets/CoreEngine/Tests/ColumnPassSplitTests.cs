// Assets/CoreEngine/Tests/ColumnPassSplitTests.cs
//
// Task 86 step 0 (queue 86-coarse-column-fields; owner Q45 (a)): an isolated
// timing split of the 0.1 m chunk column pass (TerrainGenChunkSource's
// ColumnJob) into its two halves, as task 30 / 82b timed theirs:
//   S  ShapeField.Sample over the 130^2 grid (the chunk's columns + border)
//   P  DetailField.PrepareColumn over the 128^2 columns
// plus, for scale, a whole chunk column (surface band, 7 layers, one shared
// ColumnPass) through Generate. Burst jobs with ColumnJob's own compile flags,
// one thread, ABBA (S P P S). Decision rule (stated in the task file before
// measuring): P >= ~30% of S + P -> coarse-grid the km-scale fields;
// less -> target ShapeField.Sample instead. Report only (REPORTS=1); writes
// Logs/TerrainGen/column_pass_split.txt. Editor Burst: a direction check, not
// a streaming or frame-time claim.
//
// After the change (spec.coarseColumnShift = 3, the default): arms A = 0 (per
// column, the pre-86 code) and B = 3, ABBA, for P alone (as ColumnJob calls it,
// with a NodeCache), for whole 0.1 m chunk columns and for shell chunks 1-5
// (GenerateShellChunk: column pass, voxels, shell fill).
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.WorldGen;

public class ColumnPassSplitTests
{
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
    private struct SampleJob : IJob
    {
        [ReadOnly] public NativeArray<float> h, r, w;
        [ReadOnly] public NativeArray<byte> cls;
        [ReadOnly] public NativeArray<ushort> flow;
        public TerrainGen.TerrainSpec spec; public TerrainGen.Offsets o;
        public int3 origin;
        public NativeArray<int> sink;

        public unsafe void Execute()
        {
            var map = new TerrainGen.MapView
            {
                height = (float*)h.GetUnsafeReadOnlyPtr(), rough = (float*)r.GetUnsafeReadOnlyPtr(),
                water = (float*)w.GetUnsafeReadOnlyPtr(), waterClass = (byte*)cls.GetUnsafeReadOnlyPtr(),
                flowDir = flow.Length > 1 ? (ushort*)flow.GetUnsafeReadOnlyPtr() : null,
                cells = spec.mapCells, cellVoxels = spec.mapCellVoxels,
            };
            int acc = 0;
            for (int k = -1; k <= 128; k++)
            for (int i = -1; i <= 128; i++)
            {
                var c = TerrainGen.ShapeField.Sample(in spec, in map, in o, origin.x + i + 0.5f, origin.z + k + 0.5f, 1);
                acc += c.height ^ c.water;
            }
            sink[0] += acc;
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
    private struct PrepareJob : IJob
    {
        public TerrainGen.TerrainSpec spec; public TerrainGen.Offsets o;
        public int3 origin;
        public NativeArray<int> sink;

        public void Execute()
        {
            int acc = 0;
            var nodes = new TerrainGen.DetailField.NodeCache();
            for (int k = 0; k < 128; k++)
            for (int i = 0; i < 128; i++)
            {
                var cd = TerrainGen.DetailField.PrepareColumn(in spec, in o, origin.x + i, origin.z + k, ref nodes);
                acc += cd.cav0Lo ^ cd.underHi ^ (int)(cd.tun0 * 16f) ^ (int)cd.deepOffset;
            }
            sink[0] += acc;
        }
    }

    [Test, Category("Slow")]
    public void ColumnPass_SampleVsPrepare_TimingReport()
    {
        if (System.Environment.GetEnvironmentVariable("REPORTS") != "1") Assert.Ignore("report only: set REPORTS=1");
        var src = new TerrainGenChunkSource(42u);
        var spec = src.Spec; var o = src.O; var view = src.Map.View;
        var h = new NativeArray<float>(src.Map.Height, Allocator.Persistent);
        var r = new NativeArray<float>(src.Map.Rough, Allocator.Persistent);
        var w = new NativeArray<float>(src.Map.Water, Allocator.Persistent);
        var cls = new NativeArray<byte>(src.Map.WaterClass, Allocator.Persistent);
        var flow = src.Map.FlowDir != null ? new NativeArray<ushort>(src.Map.FlowDir, Allocator.Persistent)
                                           : new NativeArray<ushort>(1, Allocator.Persistent);
        var sink = new NativeArray<int>(1, Allocator.Persistent);
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
        var lines = new List<string>();
        try
        {
            // 8 land chunk columns spread over the world (a fixed scan; the first
            // 8 with ground above the sea), the surface layer's chunk y.
            var cols = new List<int3>();
            int G = 16, step = spec.worldSizeVoxels / G;
            for (int t = 0; t < G * G && cols.Count < 8; t++)
            {
                int idx = (int)((uint)t * 37u % (uint)(G * G));
                int x = (idx % G) * step + step / 2, z = (idx / G) * step + step / 2;
                var c = TerrainGen.ShapeField.Sample(in spec, in view, in o, x + 0.5f, z + 0.5f, 1);
                if (c.height <= spec.seaLevelY + 20) continue;
                cols.Add(new int3(x >> 7, c.height >> 7, z >> 7));
            }

            var spec0 = spec; spec0.coarseColumnShift = 0;
            var spec3 = spec; spec3.coarseColumnShift = TerrainGen.TerrainSpec.COARSE_COLUMN_SHIFT;
            void RunS(int3 cc) => new SampleJob { h = h, r = r, w = w, cls = cls, flow = flow, spec = spec, o = o, origin = cc * 128, sink = sink }.Run();
            void RunP(int3 cc, bool coarse) => new PrepareJob { spec = coarse ? spec3 : spec0, o = o, origin = cc * 128, sink = sink }.Run();
            RunS(cols[0]); RunP(cols[0], false); RunP(cols[0], true);   // compile + warm

            // Arms: 0 = S, 1 = P per column (A), 2 = P coarse (B). S P0 P3 P3 P0 S.
            double[] ms = new double[3]; int[] n = new int[3];
            foreach (int arm in new[] { 0, 1, 2, 2, 1, 0 })
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                foreach (var cc in cols) { if (arm == 0) RunS(cc); else RunP(cc, arm == 2); n[arm]++; }
                ms[arm] += sw.Elapsed.TotalMilliseconds;
            }
            double s = ms[0] / n[0], p = ms[1] / n[1], p3 = ms[2] / n[2];

            // Scale: a whole chunk column, 7 layers round the surface, one shared pass (task 30's path).
            using (var wp = new TerrainGenChunkSource.ColumnPass())
            { pool.Reset(); src.Generate(cols[0], new Chunk(), new ChunkHandleAllocator(2), pool, null, wp); }
            double[] gen = new double[2]; int[] gn = new int[2]; int gl = 0;
            foreach (int arm in new[] { 0, 1, 1, 0 })
            {
                src.SetCoarseColumnShift(arm == 0 ? 0 : TerrainGen.TerrainSpec.COARSE_COLUMN_SHIFT);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                foreach (var cc in cols)
                {
                    using (var pass = new TerrainGenChunkSource.ColumnPass())
                        for (int dy = -4; dy <= 2; dy++)
                        {
                            pool.Reset();
                            src.Generate(new int3(cc.x, cc.y + dy, cc.z), new Chunk(), new ChunkHandleAllocator(2), pool, null, pass);
                            gl++;
                        }
                    gn[arm]++;
                }
                gen[arm] += sw.Elapsed.TotalMilliseconds;
            }
            double perCol = gen[0] / gn[0], perCol3 = gen[1] / gn[1];

            // Shell chunks 1-5 at the surface layer of the first 4 columns, A/B ABBA.
            var shellMs = new double[6, 2]; var shellN = new int[6, 2];
            using (var sc = new TerrainGenChunkSource.ShellChunk())
            {
                for (int k = 1; k <= 5; k++)
                {
                    int sh = 7 + k;
                    src.SetCoarseColumnShift(0);
                    src.GenerateShellChunk(k, new int3((cols[0].x << 7) >> sh, (cols[0].y << 7) >> sh, (cols[0].z << 7) >> sh), sc);   // warm
                    foreach (int arm in new[] { 0, 1, 1, 0 })
                    {
                        src.SetCoarseColumnShift(arm == 0 ? 0 : TerrainGen.TerrainSpec.COARSE_COLUMN_SHIFT);
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        for (int q = 0; q < 4; q++)
                        {
                            var cc = cols[q];
                            src.GenerateShellChunk(k, new int3((cc.x << 7) >> sh, (cc.y << 7) >> sh, (cc.z << 7) >> sh), sc);
                            shellN[k, arm]++;
                        }
                        shellMs[k, arm] += sw.Elapsed.TotalMilliseconds;
                    }
                }
            }
            src.SetCoarseColumnShift(TerrainGen.TerrainSpec.COARSE_COLUMN_SHIFT);

            lines.Add($"Task 86 step 0: column pass split, seed 42 map v{spec.mapVersion}, {cols.Count} land chunk columns, Editor Burst, one thread, ABBA (S P P S)");
            lines.Add("Isolated generation only; not a streaming or frame-time claim.");
            lines.Add("");
            lines.Add($"  S  ShapeField.Sample x 130^2     {s,8:F2} ms per chunk column   ({n[0]} runs)   {s * 1e6 / (130 * 130),7:F0} ns per sample");
            lines.Add($"  P  DetailField.PrepareColumn x 128^2 {p,8:F2} ms per chunk column   ({n[1]} runs)   {p * 1e6 / (128 * 128),7:F0} ns per column");
            lines.Add($"  P share of S + P: {100 * p / (s + p):F1}%   (decision rule: >= ~30% -> coarse-grid the km-scale fields; less -> target Sample)");
            lines.Add("");
            lines.Add($"  whole chunk column, 7 layers (shared pass), Generate: {perCol,8:F2} ms   ({gl / 2} chunks / {gn[0]} columns per arm)");
            lines.Add($"  S + P as a share of that: {100 * (s + p) / perCol:F1}%   (P alone {100 * p / perCol:F1}%, S alone {100 * s / perCol:F1}%)");
            lines.Add("");
            lines.Add($"After task 86: A = per-column cave fields (coarseColumnShift 0, pre-86), B = coarse (shift {TerrainGen.TerrainSpec.COARSE_COLUMN_SHIFT}); ABBA");
            lines.Add($"  P per chunk column           A {p,8:F2} ms   B {p3,8:F2} ms   B/A {p3 / p:F3}   ({1e6 * p3 / (128 * 128):F0} ns per column)");
            lines.Add($"  column pass S + P            A {s + p,8:F2} ms   B {s + p3,8:F2} ms   B/A {(s + p3) / (s + p):F3}");
            lines.Add($"  chunk column, 7 layers       A {perCol,8:F2} ms   B {perCol3,8:F2} ms   B/A {perCol3 / perCol:F3}");
            for (int k = 1; k <= 5; k++)
            {
                double a0 = shellMs[k, 0] / shellN[k, 0], b0 = shellMs[k, 1] / shellN[k, 1];
                lines.Add($"  shell {k} chunk (surface)     A {a0,8:F2} ms   B {b0,8:F2} ms   B/A {b0 / a0:F3}   ({shellN[k, 0]} per arm)");
            }
        }
        finally
        {
            src.SetCoarseColumnShift(TerrainGen.TerrainSpec.COARSE_COLUMN_SHIFT);
            pool.Dispose(); sink.Dispose(); h.Dispose(); r.Dispose(); w.Dispose(); cls.Dispose(); flow.Dispose();
            src.Dispose();
        }
        string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "TerrainGen");
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "column_pass_split.txt"), lines);
        UnityEngine.Debug.Log("[task 86]\n" + string.Join("\n", lines));
    }
    /// Task 88 (report only, REPORTS=1): the cavern cell cache, A = off
    /// (spec.cavernCellCacheOff = 1: candidates per column, the pre-88 work),
    /// B = on; both with the coarse cave grid. ABBA for P alone (as ColumnJob
    /// calls it), whole 0.1 m chunk columns (7 layers, shared pass) and shell
    /// chunks 1-7. Same land columns as the test above. Writes
    /// Logs/TerrainGen/cavern_cache_timing.txt. Editor Burst, one thread: a
    /// direction check, not a streaming or frame-time claim.
    [Test, Category("Slow")]
    public void CavernCache_TimingReport()
    {
        if (System.Environment.GetEnvironmentVariable("REPORTS") != "1") Assert.Ignore("report only: set REPORTS=1");
        var src = new TerrainGenChunkSource(42u);
        var spec = src.Spec; var o = src.O; var view = src.Map.View;
        var sink = new NativeArray<int>(1, Allocator.Persistent);
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
        var lines = new List<string>();
        try
        {
            var cols = new List<int3>();
            int G = 16, step = spec.worldSizeVoxels / G;
            for (int t = 0; t < G * G && cols.Count < 8; t++)
            {
                int idx = (int)((uint)t * 37u % (uint)(G * G));
                int x = (idx % G) * step + step / 2, z = (idx / G) * step + step / 2;
                var c = TerrainGen.ShapeField.Sample(in spec, in view, in o, x + 0.5f, z + 0.5f, 1);
                if (c.height <= spec.seaLevelY + 20) continue;
                cols.Add(new int3(x >> 7, c.height >> 7, z >> 7));
            }
            var specOff = spec; specOff.cavernCellCacheOff = 1;
            void RunP(int3 cc, bool on) => new PrepareJob { spec = on ? spec : specOff, o = o, origin = cc * 128, sink = sink }.Run();
            RunP(cols[0], false); RunP(cols[0], true);   // compile + warm
            double[] pm = new double[2]; int[] pn = new int[2];
            foreach (int arm in new[] { 0, 1, 1, 0, 0, 1, 1, 0 })
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                foreach (var cc in cols) { RunP(cc, arm == 1); pn[arm]++; }
                pm[arm] += sw.Elapsed.TotalMilliseconds;
            }

            using (var wp = new TerrainGenChunkSource.ColumnPass())
            { pool.Reset(); src.Generate(cols[0], new Chunk(), new ChunkHandleAllocator(2), pool, null, wp); }
            double[] gen = new double[2]; int[] gn = new int[2];
            foreach (int arm in new[] { 0, 1, 1, 0 })
            {
                src.SetCavernCellCache(arm == 1);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                foreach (var cc in cols)
                {
                    using (var pass = new TerrainGenChunkSource.ColumnPass())
                        for (int dy = -4; dy <= 2; dy++)
                        {
                            pool.Reset();
                            src.Generate(new int3(cc.x, cc.y + dy, cc.z), new Chunk(), new ChunkHandleAllocator(2), pool, null, pass);
                        }
                    gn[arm]++;
                }
                gen[arm] += sw.Elapsed.TotalMilliseconds;
            }

            int maxK = TerrainGenChunkSource.MAX_SHELL;
            var shellMs = new double[maxK + 1, 2]; var shellN = new int[maxK + 1, 2];
            using (var sc = new TerrainGenChunkSource.ShellChunk())
            {
                for (int k = 1; k <= maxK; k++)
                {
                    int sh = 7 + k;
                    src.SetCavernCellCache(false);
                    src.GenerateShellChunk(k, new int3((cols[0].x << 7) >> sh, (cols[0].y << 7) >> sh, (cols[0].z << 7) >> sh), sc);   // warm
                    foreach (int arm in new[] { 0, 1, 1, 0 })
                    {
                        src.SetCavernCellCache(arm == 1);
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        for (int q = 0; q < 4; q++)
                        {
                            var cc = cols[q];
                            src.GenerateShellChunk(k, new int3((cc.x << 7) >> sh, (cc.y << 7) >> sh, (cc.z << 7) >> sh), sc);
                            shellN[k, arm]++;
                        }
                        shellMs[k, arm] += sw.Elapsed.TotalMilliseconds;
                    }
                }
            }
            src.SetCavernCellCache(true);

            double pa = pm[0] / pn[0], pb = pm[1] / pn[1], ga = gen[0] / gn[0], gb = gen[1] / gn[1];
            lines.Add($"Task 88: cavern cell cache, seed 42 map v{spec.mapVersion}, {cols.Count} land chunk columns, Editor Burst, one thread, ABBA");
            lines.Add("A = cache off (candidates per column, pre-88 work), B = on; both coarse cave grid. Isolated generation only; not a streaming or frame-time claim.");
            lines.Add($"  P per chunk column           A {pa,8:F2} ms   B {pb,8:F2} ms   B/A {pb / pa:F3}   ns per column A {1e6 * pa / (128 * 128):F0}  B {1e6 * pb / (128 * 128):F0}   ({pn[0]} runs per arm)");
            lines.Add($"  chunk column, 7 layers       A {ga,8:F2} ms   B {gb,8:F2} ms   B/A {gb / ga:F3}   ({gn[0]} columns per arm)");
            for (int k = 1; k <= maxK; k++)
            {
                double a0 = shellMs[k, 0] / shellN[k, 0], b0 = shellMs[k, 1] / shellN[k, 1];
                lines.Add($"  shell {k} chunk (surface)     A {a0,8:F2} ms   B {b0,8:F2} ms   B/A {b0 / a0:F3}   ({shellN[k, 0]} per arm)");
            }
        }
        finally
        {
            src.SetCavernCellCache(true);
            pool.Dispose(); sink.Dispose(); src.Dispose();
        }
        string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "TerrainGen");
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "cavern_cache_timing.txt"), lines);
        UnityEngine.Debug.Log("[task 88]\n" + string.Join("\n", lines));
    }
}
