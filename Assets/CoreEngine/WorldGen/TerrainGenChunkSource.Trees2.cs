// Assets/CoreEngine/WorldGen/TerrainGenChunkSource.Trees2.cs
//
// Task 210 (owner 2026-11-14 Q-AD (a)): `-trees2 1` = TerrainGen.Trees2's WORLD
// space-colonization trees (header there). DEFAULT OFF: without the flag
// spec.trees2On stays 0, no library is built and every hook below returns at its
// first line -- the world is today's trees byte for byte (TreeGalleryTests.TodayTrees_Pinned,
// the tree tests).
//
// THE LIBRARY is built ONCE per process (refcounted by the sources that use it):
// every prototype (species x variant) is grown by TreeGallery.Build on its own
// WORKER thread -- each writes only its own result object, the single-writer rule
// holds -- then the main thread packs the results into unmanaged memory (2 bits a
// voxel) and publishes them through a SharedStatic, so the Burst chunk jobs read
// them without new job fields (as task 200's gallery). Nothing is written to the
// library after that; a source's Dispose releases its reference.
//
// HOOKS (all in TerrainGenChunkSource.Trees.cs's tree path, LOD 0, shells <= Trees.MAX_SHELL, point):
//   GatherTrees   after Trees.Gather: Trees2.Gather appends the library trees touching the
//                 footprint (Tree entries, shape SHAPE_LIB) and Suppress192 empties 192's
//                 trees inside a library tree's keep radius; crowns raise the column top
//   OverlayTrees  OverlayLib first (wood of every instance, then leaves; AIR only), then 192's prims
//   PointTree     the same voxel (FarQuery)
using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace VoxelEngine.WorldGen
{
    public static class Trees2Shared
    {
        private sealed class Key { }
        public static readonly SharedStatic<TerrainGen.TreeLib> Lib = SharedStatic<TerrainGen.TreeLib>.GetOrCreate<Key>();
    }

    /// The process's prototype library (task 210).
    public static unsafe class Trees2Library
    {
        private static readonly object _lock = new object();
        private static int _refs;
        public static long Bytes { get; private set; }
        public static double BuildMs { get; private set; }
        public static string Summary { get; private set; } = "";
        /// Per species: prototypes built and their mean bricks (tests, reports).
        public static readonly double[] MeanBricks = new double[TerrainGen.Trees2.SP_COUNT];
        public static TerrainGen.Trees2.ProtoBuild[] Built { get; private set; }
        /// Task 243: the crown-core mode the published library was built with (TerrainSpec.crownCore).
        public static byte CoreMode { get; private set; }

        /// Task 243: `core` = spec.crownCore. One library per process: a request in another mode
        /// REBUILDS it for every holder (tests and reports switch between Generate calls; a player
        /// has one mode). Not thread-safe against running chunk jobs.
        public static void Acquire(byte core = 0)
        {
            lock (_lock)
            {
                _refs++;
                if (_refs > 1)
                {
                    if (core != CoreMode) { Free(); Build(core); }
                    return;
                }
                Build(core);
            }
        }

        /// Task 243: rebuild the library in `core` mode for its current holders (no reference taken).
        public static void SetCoreMode(byte core)
        {
            lock (_lock)
            {
                if (_refs == 0 || core == CoreMode) return;
                Free(); Build(core);
            }
        }

        public static void Release()
        {
            lock (_lock)
            {
                if (_refs == 0 || --_refs > 0) return;
                Free();
            }
        }

        private static void Build(byte core)
        {
            CoreMode = core;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var jobs = new List<int2>();   // (species, variant), the costly giants first (load balance only)
            int[] order = { TerrainGen.TreeGallery.SP_BANYAN, TerrainGen.TreeGallery.SP_REDWOOD, TerrainGen.TreeGallery.SP_OAK,
                            TerrainGen.TreeGallery.SP_SPRUCE, TerrainGen.TreeGallery.SP_ACACIA, TerrainGen.TreeGallery.SP_BIRCH };
            foreach (int sp in order)
                for (int v = 0; v < TerrainGen.Trees2.VARIANTS[sp]; v++) jobs.Add(new int2(sp, v));
            var res = new TerrainGen.Trees2.ProtoBuild[jobs.Count];
            int next = -1, threads = math.clamp(Environment.ProcessorCount - 1, 1, 6);
            var workers = new Thread[threads];
            for (int w = 0; w < threads; w++)
            {
                workers[w] = new Thread(() =>
                {
                    for (int i = Interlocked.Increment(ref next); i < jobs.Count; i = Interlocked.Increment(ref next))
                    {
                        try { res[i] = TerrainGen.Trees2.BuildProto(jobs[i].x, jobs[i].y, core); }
                        catch (Exception e) { res[i] = null; UnityEngine.Debug.LogError($"[Trees2] prototype {jobs[i]} failed: {e.Message}"); }
                    }
                }) { IsBackground = true, Name = "Trees2 library " + w };
                workers[w].Start();
            }
            foreach (var t in workers) t.Join();

            // pack (main thread): species in TreeGallery order, variants in seed order
            var lib = new TerrainGen.TreeLib();
            var keep = new List<TerrainGen.Trees2.ProtoBuild>();
            for (int sp = 0; sp < TerrainGen.Trees2.SP_COUNT; sp++)
            {
                lib.first[sp] = keep.Count;
                double sum = 0; int cnt = 0;
                for (int i = 0; i < jobs.Count; i++)
                {
                    if (jobs[i].x != sp) continue;
                    var pb = res[i];
                    if (pb == null || !pb.ok) continue;
                    keep.Add(pb); sum += pb.placeBricks; cnt++;   // task 243: placement reads the pre-fill bricks
                }
                // jobs hold each species' variants in seed order, so the kept ones stay in that order
                lib.variants[sp] = cnt;
                MeanBricks[sp] = cnt > 0 ? sum / cnt : 0;
            }
            lib.count = keep.Count;
            lib.protos = (TerrainGen.TreeProto*)UnsafeUtility.Malloc(sizeof(TerrainGen.TreeProto) * math.max(1, keep.Count), 16, Allocator.Persistent);
            long bytes = 0;
            for (int i = 0; i < keep.Count; i++)
            {
                var pb = keep[i];
                var p = new TerrainGen.TreeProto
                {
                    lo = pb.lo, nb = pb.nb, vlo = pb.vlo, vhi = pb.vhi, m1 = pb.m1, m2 = pb.m2, m3 = pb.m3,
                    species = (byte)pb.species, tier = (byte)TerrainGen.Trees2.Tier(pb.species), giant = (byte)(TerrainGen.Trees2.Tier(pb.species) == 0 ? 1 : 0),
                    bricks = pb.bricks, reach = pb.reach, keep = pb.keep, leafBottom = pb.leafBottom, leafReach = pb.leafReach, footR = pb.footR, top = pb.top,
                    feet = pb.feet.Length,
                };
                p.index = (int*)UnsafeUtility.Malloc(4L * pb.index.Length, 4, Allocator.Persistent);
                fixed (int* src = pb.index) UnsafeUtility.MemCpy(p.index, src, 4L * pb.index.Length);
                p.codes = (byte*)UnsafeUtility.Malloc(math.max(1, pb.codes.Length), 16, Allocator.Persistent);
                if (pb.codes.Length > 0) fixed (byte* src = pb.codes) UnsafeUtility.MemCpy(p.codes, src, pb.codes.Length);
                p.foot = (int4*)UnsafeUtility.Malloc(16L * math.max(1, pb.feet.Length), 16, Allocator.Persistent);
                for (int f = 0; f < pb.feet.Length; f++) p.foot[f] = pb.feet[f];
                bytes += 4L * pb.index.Length + pb.codes.Length + 16L * pb.feet.Length + sizeof(TerrainGen.TreeProto);
                lib.protos[i] = p;
            }
            TerrainGen.Trees2.Probabilities(ref lib, MeanBricks);
            lib.ready = 1;
            Trees2Shared.Lib.Data = lib;
            Bytes = bytes; BuildMs = sw.Elapsed.TotalMilliseconds;
            Built = keep.ToArray();

            var sb = new System.Text.StringBuilder();
            sb.Append($"{keep.Count} prototypes, {bytes / 1048576.0:F1} MB, built in {BuildMs:F0} ms on {threads} worker threads");
            if (core != 0)
            {   // task 243: what the crown-core fill added
                long filled = 0, dropped = 0; int bb = 0, ba = 0;
                foreach (var pb in keep) { filled += pb.coreStats.filled; dropped += pb.coreStats.dropped; bb += pb.coreStats.bricksBefore; ba += pb.coreStats.bricksAfter; }
                sb.Append($", crown cores ON (-crowncore {core}): +{filled:N0} leaf voxels, {dropped:N0} unconnected dropped, bricks {bb:N0} -> {ba:N0}");
            }
            sb.Append(" |");
            for (int sp = 0; sp < TerrainGen.Trees2.SP_COUNT; sp++)
            {
                double slow = 0; foreach (var pb in keep) if (pb.species == sp) slow = math.max(slow, pb.ms);
                sb.Append($" {TerrainGen.TreeGallery.SpeciesName[sp]} {lib.variants[sp]} x {MeanBricks[sp]:F0} bricks (slowest {slow:F0} ms)");
            }
            sb.Append(" | p per 12.8 m cell:");
            for (int b = 0; b < TerrainGen.GenBiome.Count; b++)
                for (int sp = 0; sp < TerrainGen.Trees2.SP_COUNT; sp++)
                    if (lib.pq[b * 8 + sp] > 0) sb.Append($" b{b}/{TerrainGen.TreeGallery.SpeciesName[sp]} {lib.pq[b * 8 + sp] / 65536.0:F3}");
            sb.Append(" | dense (213) p per 12.8 m / 6.4 m cell:");
            for (int b = 0; b < TerrainGen.GenBiome.Count; b++)
                for (int sp = 0; sp < TerrainGen.Trees2.SP_COUNT; sp++)
                {
                    if (lib.pqD[b * 8 + sp] > 0) sb.Append($" b{b}/{TerrainGen.TreeGallery.SpeciesName[sp]} {lib.pqD[b * 8 + sp] / 65536.0:F3}");
                    if (lib.pqF[b * 8 + sp] > 0) sb.Append($" b{b}/{TerrainGen.TreeGallery.SpeciesName[sp]}(6.4) {lib.pqF[b * 8 + sp] / 65536.0:F3}");
                }
            Summary = sb.ToString();
            UnityEngine.Debug.Log("[Trees2] library: " + Summary);
        }

        private static void Free()
        {
            var lib = Trees2Shared.Lib.Data;
            Trees2Shared.Lib.Data = default;
            for (int i = 0; i < lib.count; i++)
            {
                UnsafeUtility.Free(lib.protos[i].index, Allocator.Persistent);
                UnsafeUtility.Free(lib.protos[i].codes, Allocator.Persistent);
                UnsafeUtility.Free(lib.protos[i].foot, Allocator.Persistent);
            }
            if (lib.protos != null) UnsafeUtility.Free(lib.protos, Allocator.Persistent);
            Bytes = 0; Built = null;
        }
    }

    public sealed partial class TerrainGenChunkSource
    {
        private bool _trees2Owner;

        /// -trees2 (task 210; DEFAULT ON since task 214, owner Q-AJ (a)): the world space-colonization
        /// trees. `-trees2 0` = today's (pre-210) trees byte for byte. Last match wins. Needs the trees
        /// on (`-trees 0` turns both off).
        public static bool Trees2Arg() => Trees2Mode() != 0;

        /// The spec's trees2On from the flags: 1 task 213's denser forest (the default since task 214),
        /// 2 task 210's placement (`-trees2dense 0`), 0 today's trees (`-trees2 0`). Last match wins per flag.
        public static byte Trees2Mode()
        {
            if (Trees2TestOverride >= 0) return (byte)Trees2TestOverride;
            string[] a = Environment.GetCommandLineArgs();
            bool on = true, dense = true;   // task 214: ON unless -trees2 0
            for (int i = 0; i + 1 < a.Length; i++)
            {
                if (a[i] == "-trees2" && int.TryParse(a[i + 1], out int p)) on = p != 0;
                if (a[i] == "-trees2dense" && int.TryParse(a[i + 1], out int d)) dense = d != 0;
            }
            return on ? (dense ? (byte)1 : (byte)2) : (byte)0;
        }

        /// Tests: -1 = read the flags; 0 off / 1 dense (213) / 2 task 210's placement, for the sources built from now on. Not thread-safe.
        public static int Trees2TestOverride = -1;

        /// Constructor step (after the tree plane): build / share the library when the spec asks for it.
        private void InitTrees2()
        {
            if (Spec.trees2On == 0) return;
            Trees2Library.Acquire(Spec.crownCore);
            _trees2Owner = true;
            UnityEngine.Debug.Log($"[Trees2] world trees ON (default; -trees2 0 = today's trees), crown cores {(Spec.crownCore != 0 ? $"ON (-crowncore {Spec.crownCore})" : "OFF (default)")}: {Trees2Library.Summary}");
        }

        private void DisposeTrees2()
        {
            if (!_trees2Owner) return;
            _trees2Owner = false;
            Trees2Library.Release();
        }

        /// Tests and reports: spec.trees2On for the chunks generated from now on (the library is
        /// built on the first ON). Not thread-safe: call between Generate calls.
        public void SetTrees2(bool on) => SetTrees2Mode(on ? (byte)1 : (byte)0);

        /// Tests and reports (task 213): 0 off, 1 the denser forest, 2 task 210's placement.
        public void SetTrees2Mode(byte mode)
        {
            bool on = mode != 0;
            if (on && !_trees2Owner) { Trees2Library.Acquire(Spec.crownCore); _trees2Owner = true; }
            var t = Spec; t.trees2On = mode; Spec = t;
            _columnTop.Clear(); _specStamp++;
        }

        /// Task 243: -crowncore 1 = solid crown cores (TerrainGen.CrownCore: the enclosed interior), 2 = the closed
        /// crown (gaps narrower than ~2 clumps count as crown); a look change, DEFAULT OFF (0).
        /// Last match wins. Only with the world trees (map v4).
        public static byte CrownCoreArg()
        {
            string[] a = Environment.GetCommandLineArgs();
            byte v = 0;
            for (int i = 0; i + 1 < a.Length; i++)
                if (a[i] == "-crowncore" && int.TryParse(a[i + 1], out int p)) v = (byte)math.clamp(p, 0, 2);
            return v;
        }

        /// Tests and reports (task 243): crown cores on / off for the chunks generated from now on
        /// (rebuilds the process's library in that mode). Not thread-safe: call between Generate calls.
        public void SetCrownCore(bool on) => SetCrownCore(on ? (byte)1 : (byte)0);

        /// Tests and reports (task 243): crown-core mode 0 / 1 / 2 for the chunks generated from now on.
        public void SetCrownCore(byte mode)
        {
            var t = Spec; t.crownCore = mode; Spec = t;
            if (_trees2Owner) Trees2Library.SetCoreMode(t.crownCore);
            _columnTop.Clear(); _specStamp++;
        }

        /// Tests and reports: the library trees whose box touches fine columns [x0, x1] x [z0, z1],
        /// gathered exactly as the chunk jobs do (Burst, the same map view). Empty when off.
        public TerrainGen.Tree[] LibTreesIn(int x0, int z0, int x1, int z1)
        {
            if (!Spec.Trees2) return new TerrainGen.Tree[0];
            var outT = new NativeArray<TerrainGen.Tree>(math.max(1, TerrainGen.Trees2.MaxTrees(x0, z0, x1, z1, Spec.Trees2Dense)), Allocator.TempJob);
            var cnt = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                new LibGatherJob
                {
                    h = _h, r = _r, w = _w, cls = _cls, flow = _flow, routeMask = _route, shoreField = _shore, pocketPlane = _pocket, landSigPlane = _landSig,
                    townPlane = _town, treePlane = _treePlane, oreCellPlane = _oreCell, spec = Spec, o = O, box = new int4(x0, z0, x1, z1),
                    outTrees = outT, count = cnt,
                }.Run();
                var res = new TerrainGen.Tree[cnt[0]];
                for (int i = 0; i < res.Length; i++) res[i] = outT[i];
                return res;
            }
            finally { outT.Dispose(); cnt.Dispose(); }
        }

        /// The species name of a library tree (Tree.prim0 = its prototype).
        public static unsafe string LibSpeciesName(in TerrainGen.Tree t)
            => TerrainGen.TreeGallery.SpeciesName[Trees2Shared.Lib.Data.protos[t.prim0].species];

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
        private struct LibGatherJob : IJob
        {
            [ReadOnly] public NativeArray<float> h, r, w;
            [ReadOnly] public NativeArray<byte> cls, routeMask, landSigPlane, treePlane;
            [ReadOnly] public NativeArray<ushort> flow, shoreField;
            [ReadOnly] public NativeArray<int> pocketPlane, townPlane;
            [ReadOnly] public NativeArray<uint> oreCellPlane;
            public TerrainGen.TerrainSpec spec; public TerrainGen.Offsets o; public int4 box;
            public NativeArray<TerrainGen.Tree> outTrees;
            public NativeArray<int> count;

            public unsafe void Execute()
            {
                var map = new TerrainGen.MapView
                {
                    height = (float*)h.GetUnsafeReadOnlyPtr(), rough = (float*)r.GetUnsafeReadOnlyPtr(),
                    water = (float*)w.GetUnsafeReadOnlyPtr(), waterClass = (byte*)cls.GetUnsafeReadOnlyPtr(),
                    flowDir = flow.Length > 1 ? (ushort*)flow.GetUnsafeReadOnlyPtr() : null,
                    route = routeMask.Length > 1 ? (byte*)routeMask.GetUnsafeReadOnlyPtr() : null,
                    shore = shoreField.Length > 1 ? (ushort*)shoreField.GetUnsafeReadOnlyPtr() : null,
                    pocket = pocketPlane.Length > 1 ? (int*)pocketPlane.GetUnsafeReadOnlyPtr() : null,
                    stream = pocketPlane.Length > 1 ? TerrainGen.PocketStreams.Section((int*)pocketPlane.GetUnsafeReadOnlyPtr(), pocketPlane.Length, spec.mapCells, spec.mapCellVoxels) : null,   // task 209b
                    landSig = landSigPlane.Length > 1 ? (byte*)landSigPlane.GetUnsafeReadOnlyPtr() : null,
                    town = townPlane.Length > 1 ? (int*)townPlane.GetUnsafeReadOnlyPtr() : null,
                    trees = treePlane.Length > 1 ? (byte*)treePlane.GetUnsafeReadOnlyPtr() : null,
                    oreCell = oreCellPlane.Length > 1 ? (uint*)oreCellPlane.GetUnsafeReadOnlyPtr() : null,
                    cells = spec.mapCells, cellVoxels = spec.mapCellVoxels,
                };
                int top = 0;
                count[0] = GatherLib(in spec, in map, in o, box.x, box.y, box.z, box.w, (TerrainGen.Tree*)outTrees.GetUnsafePtr(), outTrees.Length, 0, null, ref top);
            }
        }

        /// Library trees a LOD-0 chunk column can gather.
        /// (Task 213: sized for the dense mode, which is >= task 210's.)
        internal static readonly int ChunkMaxLibTrees = TerrainGen.Trees2.MaxTrees(0, 0, EngineConfig.CHUNK_EDGE_VOXELS - 1, EngineConfig.CHUNK_EDGE_VOXELS - 1, true);

        /// GatherTrees' library step (Burst): appends the library trees after 192's nt trees and empties
        /// 192's trees inside their keep radius. Returns the library trees written.
        internal static unsafe int GatherLib(in TerrainGen.TerrainSpec spec, in TerrainGen.MapView map, in TerrainGen.Offsets o,
                                             int x0, int z0, int x1, int z1, TerrainGen.Tree* trees, int treeCap, int nt,
                                             TerrainGen.TreePrim* prims, ref int top)
        {
            if (!spec.Trees2) return 0;
            ref var lib = ref Trees2Shared.Lib.Data;
            if (lib.ready == 0) return 0;
            bool dense = spec.Trees2Dense;   // task 213
            int cap = TerrainGen.Trees2.MaxCandidates(x0, z0, x1, z1, dense);
            int cellCap = TerrainGen.Trees2.MaxCells(x0, z0, x1, z1, dense);
            // Task 218: heap scratch, freed before returning. Allocator.Temp is given back only when the whole
            // job ends, and FarQuery's PointJob (one IJobParallelFor) calls this once per chunk column of every
            // batch: ~23 KB a call piled up to 9-10 GB at jungle_high (task 216's watchdog kills).
            var bp = (TerrainGen.LibInst*)UnsafeUtility.Malloc((long)UnsafeUtility.SizeOf<TerrainGen.LibInst>() * math.max(1, cap),
                                                               UnsafeUtility.AlignOf<TerrainGen.LibInst>(), Allocator.Persistent);
            var cp = (int*)UnsafeUtility.Malloc(4L * math.max(1, cellCap), 4, Allocator.Persistent);
            var grid = default(TerrainGen.Trees2.LibGrid);
            int nl = TerrainGen.Trees2.Gather(in spec, in map, in o, in lib, x0, z0, x1, z1, bp, cap, out int nBuf,
                                              cp, cellCap, ref grid, trees + nt, treeCap - nt, ref top);
            TerrainGen.Trees2.Suppress192(bp, nBuf, in grid, trees, nt, prims);
            UnsafeUtility.Free(cp, Allocator.Persistent);
            UnsafeUtility.Free(bp, Allocator.Persistent);
            return nl;
        }

        /// OverlayTrees' library step: coarse voxel i = fine origin + (i << shift) + half; an AIR
        /// voxel takes the first instance's WOOD containing it, then the first's leaves. Uniform air
        /// bricks are expanded on their first write; touched bricks are re-checked for uniformity.
        internal static unsafe void OverlayLib(NativeArray<TerrainGen.Tree> trees, int nTrees, int3 origin, int shift, int half,
                                               NativeArray<byte> toEngine, NativeArray<byte> mats, NativeArray<byte> brickMat, NativeArray<bool> brickUniform)
        {
            ref var lib = ref Trees2Shared.Lib.Data;
            if (lib.ready == 0 || shift > TerrainGen.Trees.MAX_SHELL) return;
            bool anyLib = false;
            for (int t = 0; t < nTrees && !anyLib; t++) anyLib = (trees[t].shape & TerrainGen.Trees2.SHAPE_LIB) != 0;
            if (!anyLib) return;
            const int E = 128;
            byte air = toEngine[TerrainGen.GenMat.Air];
            int step = 1 << shift;
            var touched = new NativeArray<bool>(EngineConfig.BRICKS_PER_CHUNK, Allocator.Temp);
            bool any = false;
            for (int pass = 0; pass < 2; pass++)
            for (int t = 0; t < nTrees; t++)
            {
                var tr = trees[t];
                if ((tr.shape & TerrainGen.Trees2.SHAPE_LIB) == 0) continue;
                ref var P = ref lib.protos[tr.prim0];
                int rot = tr.shape & 3, want = pass == 0 ? 1 : 2;
                int3 i0 = math.max((tr.lo - origin - half + (step - 1)) >> shift, 0);
                int3 i1 = math.min((tr.hi - origin - half) >> shift, E - 1);
                if (math.any(i0 > i1)) continue;
                for (int by = i0.y >> 3; by <= i1.y >> 3; by++)
                for (int bz = i0.z >> 3; bz <= i1.z >> 3; bz++)
                for (int bx = i0.x >> 3; bx <= i1.x >> 3; bx++)
                {
                    int bi = (bz << 8) | (by << 4) | bx;
                    if (brickUniform[bi] && !touched[bi] && brickMat[bi] != air) continue;   // uniform solid / fluid: no air
                    int3 b0 = new int3(bx, by, bz) << 3;
                    int3 v0 = math.max(b0, i0), v1 = math.min(b0 + 7, i1);
                    if (shift <= 2)
                    {   // skip a coarse brick whose samples meet no prototype brick of this pass's class
                        int3 pa = TerrainGen.Trees2.ToProto(origin + (v0 << shift) + half - tr.root, rot);
                        int3 pb = TerrainGen.Trees2.ToProto(origin + (v1 << shift) + half - tr.root, rot);
                        int3 q0 = (math.min(pa, pb) - P.lo) >> 3, q1 = (math.max(pa, pb) - P.lo) >> 3;
                        q0 = math.max(q0, 0); q1 = math.min(q1, P.nb - 1);
                        bool hit = false;
                        for (int qy = q0.y; qy <= q1.y && !hit; qy++)
                        for (int qz = q0.z; qz <= q1.z && !hit; qz++)
                        for (int qx = q0.x; qx <= q1.x && !hit; qx++)
                        {
                            int e = P.index[(qy * P.nb.z + qz) * P.nb.x + qx];
                            hit = e >= 0 && (e & want) != 0;
                        }
                        if (!hit) continue;
                    }
                    for (int y = v0.y; y <= v1.y; y++)
                    for (int z = v0.z; z <= v1.z; z++)
                    for (int x = v0.x; x <= v1.x; x++)
                    {
                        int3 f = origin + (new int3(x, y, z) << shift) + half;
                        int c = TerrainGen.Trees2.Code(in P, TerrainGen.Trees2.ToProto(f - tr.root, rot));
                        if (c == 0 || (c == 2) != (pass == 1)) continue;
                        if (!touched[bi])
                        {
                            if (brickUniform[bi])   // uniform air: expand before the first write
                                for (int vy = 0; vy < 8; vy++)
                                for (int vz = 0; vz < 8; vz++)
                                {
                                    int row = (((by << 3) + vy) << 14) | (((bz << 3) + vz) << 7) | (bx << 3);
                                    for (int vx = 0; vx < 8; vx++) mats[row + vx] = air;
                                }
                            touched[bi] = true; any = true;
                        }
                        int idx = (y << 14) | (z << 7) | x;
                        if (mats[idx] == air) mats[idx] = toEngine[TerrainGen.Trees2.CodeMat(in P, c)];
                    }
                }
            }
            if (any)
                for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++)
                {
                    if (!touched[bi]) continue;
                    int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
                    byte first = mats[((by << 3) << 14) | ((bz << 3) << 7) | (bx << 3)];
                    bool uni = true;
                    for (int vy = 0; vy < 8 && uni; vy++)
                    for (int vz = 0; vz < 8 && uni; vz++)
                    {
                        int row = (((by << 3) + vy) << 14) | (((bz << 3) + vz) << 7) | (bx << 3);
                        for (int vx = 0; vx < 8; vx++) if (mats[row + vx] != first) { uni = false; break; }
                    }
                    brickUniform[bi] = uni;
                    brickMat[bi] = first;
                }
            touched.Dispose();
        }
    }
}
