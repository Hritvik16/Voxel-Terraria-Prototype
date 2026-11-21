// Assets/CoreEngine/Tests/TownChunkTests.cs
//
// Task 190 (`-towns 1`): the towns in the engine's generators
// (TerrainGenChunkSource.Towns.cs). Seed 42, the first town's hall:
//   - every chunk under the hall: ON twice = same bytes; ON vs OFF differ only in
//     columns inside a site disc or its ring (the flatten + the buildings);
//   - every building voxel of those chunks = Towns.VoxelAt (the kit's part, mapped
//     through FarField.GenToEngine); FarQuery's point path = the chunk bytes;
//   - shell 0 = Generate; shell 1 (stride 2): every sample inside a building box
//     = the 0.1 m voxel at the sample; shells 2-3: a sample whose 0.1 m voxel is a
//     solid building part, and every cell whose stride-2 sub-grid holds one, shows
//     a building material of that sub-grid, and the hall's wall plan keeps a solid voxel in every column;
//   - dense (non-uniform) bricks of the town's shell chunks ON vs OFF (the pool
//     cost, task 188's lesson) -> Logs/TerrainGen/towns190/chunks.txt.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using TerrainGen;
using Unity.Collections;
using Unity.Mathematics;
using VoxelEngine;
using VoxelEngine.Memory;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public unsafe class TownChunkTests
{
    private static byte[] Gen(TerrainGenChunkSource src, int3 coord)
    {
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK + 8);
        try
        {
            var chunk = new Chunk();
            src.Generate(coord, chunk, new ChunkHandleAllocator(2), pool, null);
            var v = new byte[128 * 128 * 128];
            if (chunk.isUniform) { for (int i = 0; i < v.Length; i++) v[i] = chunk.uniformMaterial; return v; }
            var raw = pool.RawData;
            for (int y = 0; y < 128; y++)
            for (int z = 0; z < 128; z++)
            for (int x = 0; x < 128; x++)
            {
                uint d = chunk.bricks[((z >> 3) << 8) | ((y >> 3) << 4) | (x >> 3)].data;
                v[TerrainGenChunkSource.Index(x, y, z)] = (d & 0x80000000u) == 0 ? (byte)d
                    : raw[(int)(d & 0x7FFFFFFFu) * 512 + (((z & 7) << 6) | ((y & 7) << 3) | (x & 7))];
            }
            return v;
        }
        finally { pool.Dispose(); }
    }

    private sealed class Empty : IDisposable
    {
        private readonly BrickDataPool _pool = new BrickDataPool(16);
        public readonly ChunkStore store;
        public Empty() { store = new ChunkStore(_pool, new ChunkHandleAllocator(1)); }
        public void Dispose() => _pool.Dispose();
    }

    private static int Zone(int[] plane, int x, int z)
    {
        for (int k = 0; k < plane[0]; k++)
        {
            int e = plane[2] + k * Towns.SITE_INTS;
            long dx = x - plane[e], dz = z - plane[e + 1], d2 = dx * dx + dz * dz, rb = plane[e + 2] + plane[e + 3];
            if (d2 <= rb * rb) return 1;
        }
        return 0;
    }

    private static byte Want(int* tp, int3 v, out bool part) { part = Towns.VoxelAt(tp, v, out byte g); return FarField.GenToEngine(g); }

    [Test]
    public void Hall_ChunksPointShellsAgree_PoolCost()
    {
        var log = new StringBuilder("Task 190 towns in the engine (seed 42, the first town's hall)\n");
        var src = new TerrainGenChunkSource(42u);
        try
        {
            src.SetTowns(true);
            var map = src.Map;
            var plane = map.TownPlane;
            Assert.IsNotNull(plane);
            int hallIdx = Array.FindIndex(map.Buildings, b => b.shape == BuildingKit.HALL);
            Assert.GreaterOrEqual(hallIdx, 0, "no hall placed");
            var hall = map.Buildings[hallIdx];
            var fp = BuildingKit.Footprint(in hall);
            log.AppendLine($"hall {hallIdx} site #{hall.id} rot {hall.rot} biome {hall.biome}: box {fp.min} .. {fp.max}; {map.TownStats.Line()}");
            fixed (int* tp = plane)
            {

                // ---- LOD 0 ----
                int3 c0 = fp.min >> 7, c1 = (fp.max - 1) >> 7;
                var on = new Dictionary<int3, byte[]>();
                int partVox = 0, diffs = 0;
                for (int cz = c0.z; cz <= c1.z; cz++)
                for (int cy = c0.y; cy <= c1.y; cy++)
                for (int cx = c0.x; cx <= c1.x; cx++)
                {
                    var cc = new int3(cx, cy, cz);
                    var a = Gen(src, cc);
                    var b = Gen(src, cc);
                    Assert.IsTrue(a.SequenceEqual(b), $"chunk {cc}: two ON runs differ");
                    src.SetTowns(false);
                    var off = Gen(src, cc);
                    src.SetTowns(true);
                    int3 o = cc << 7;
                    for (int i = 0; i < a.Length; i++)
                    {
                        int3 v = o + new int3(i & 127, i >> 14, (i >> 7) & 127);
                        byte w = Want(tp, v, out bool part);
                        if (part) { partVox++; if (a[i] != w) Assert.AreEqual(w, a[i], $"voxel {v}: chunk {a[i]} != the kit's part {w}"); }
                        if (a[i] == off[i]) continue;
                        diffs++;
                        if (Zone(plane, v.x, v.z) == 0) Assert.Fail($"voxel {v}: ON differs from OFF outside every disc + ring");
                    }
                    on[cc] = a;
                }
                Assert.Greater(partVox, 10000, "the hall's parts");
                log.AppendLine($"  LOD 0: {on.Count} chunks, {partVox} building-part voxels = Towns.VoxelAt, {diffs} voxels ON != OFF, all inside a disc + ring");

                // ---- the point path ----
                var rng = new Unity.Mathematics.Random(190u);
                var pts = new List<int3>(); var want = new List<byte>();
                foreach (var kv in on)
                {
                    int3 o = kv.Key << 7;
                    for (int k = 0; k < 6000; k++)
                    {
                        int3 v = k < 3000 ? rng.NextInt3(math.max(fp.min, o), math.min(fp.max, o + 128)) : o + rng.NextInt3(new int3(0), new int3(128));
                        if (math.any(v < o) || math.any(v >= o + 128)) continue;
                        int3 l = v - o;
                        pts.Add(v); want.Add(kv.Value[TerrainGenChunkSource.Index(l.x, l.y, l.z)]);
                    }
                }
                using (var empty = new Empty())
                using (var fq = new FarQuery(empty.store, empty.store, src, EditOpLog.InMemory(42u), null, 42u))
                {
                    var p = new NativeArray<int3>(pts.ToArray(), Allocator.TempJob);
                    var res = new NativeArray<byte>(pts.Count, Allocator.TempJob);
                    try
                    {
                        fq.GetVoxels(p, res);
                        for (int i = 0; i < pts.Count; i++)
                            if (want[i] != res[i]) Assert.AreEqual(want[i], res[i], $"point {pts[i]}: FarQuery {res[i]} != chunk {want[i]}");
                    }
                    finally { p.Dispose(); res.Dispose(); }
                }
                log.AppendLine($"  point path: {pts.Count} points equal (half inside the hall's box)");

                // ---- shells ----
                using var sc = new TerrainGenChunkSource.ShellChunk();
                foreach (var kv in on)
                {
                    src.GenerateShellChunk(0, kv.Key, sc);
                    for (int i = 0; i < kv.Value.Length; i++)
                    {
                        byte g = sc.Get(i & 127, i >> 14, (i >> 7) & 127);
                        if (g != kv.Value[i]) Assert.Fail($"shell 0 chunk {kv.Key} voxel {i}: {g} != Generate {kv.Value[i]}");
                    }
                }
                src.ShellFill = false;   // the generator's voxels (the fill may flatten a buried mixed brick)
                int3 mid = (fp.min + fp.max) >> 1;
                for (int k = 1; k <= 3; k++)
                {
                    int3 scc = mid >> (7 + k);
                    src.GenerateShellChunk(k, scc, sc);
                    int3 origin = scc << (7 + k);
                    int half = (1 << (k - 1)) - 1, cnt = 1 << (k - 1);
                    int compared = 0, solidCols = 0, planCols = 0;
                    int3 i0 = math.max((fp.min - origin) >> k, 0), i1 = math.min((fp.max - 1 - origin) >> k, 127);
                    for (int z = i0.z; z <= i1.z; z++)
                    for (int x = i0.x; x <= i1.x; x++)
                    {
                        bool colSolid = false, inPlan = false;
                        for (int y = i0.y; y <= i1.y; y++)
                        {
                            int3 s = origin + (new int3(x, y, z) << k) + half;
                            byte got = sc.Get(x, y, z);
                            byte w = Want(tp, s, out bool part);
                            bool solid = part && w != Materials.Air;
                            if (solid) inPlan = true;
                            if (k == 1)
                            {
                                if (part) { compared++; if (got != w) Assert.AreEqual(w, got, $"shell 1 sample {s}: {got} != the 0.1 m voxel {w}"); }
                                if (part && got != Materials.Air) colSolid = true;
                                continue;
                            }
                            // sub-grid materials of this cell
                            var mats = new HashSet<byte>();
                            int3 s0 = s - half + (half & 1);
                            for (int jy = 0; jy < cnt; jy++) for (int jz = 0; jz < cnt; jz++) for (int jx = 0; jx < cnt; jx++)
                            {
                                byte m = Want(tp, s0 + new int3(jx, jy, jz) * 2, out bool pp);
                                if (pp && m != Materials.Air) mats.Add(m);
                            }
                            if (mats.Count > 0) inPlan = true;
                            if (mats.Contains(got)) colSolid = true;
                            if (mats.Count == 0) { if (solid) Assert.Fail($"shell {k} sample {s}: solid part off the sub-grid"); continue; }
                            compared++;   // every cell holding a solid part shows one of them
                            if (!mats.Contains(got)) Assert.Fail($"shell {k} sample {s}: {got} is not a building material of its cell ({string.Join(",", mats)})");
                        }
                        if (inPlan) { planCols++; if (colSolid) solidCols++; }
                    }
                    Assert.Greater(compared, 0, $"shell {k} compared nothing");
                    Assert.AreEqual(planCols, solidCols, $"shell {k}: {planCols - solidCols} hall columns show no building voxel (holes)");
                    int denseOn = 0, denseOff = 0;
                    for (int b = 0; b < EngineConfig.BRICKS_PER_CHUNK; b++) if (!sc.BrickUniform[b]) denseOn++;
                    src.SetTowns(false);
                    src.GenerateShellChunk(k, scc, sc);
                    for (int b = 0; b < EngineConfig.BRICKS_PER_CHUNK; b++) if (!sc.BrickUniform[b]) denseOff++;
                    src.SetTowns(true);
                    log.AppendLine($"  shell {k}: {compared} building samples compared, {solidCols}/{planCols} hall columns solid; dense bricks of the chunk ON {denseOn} vs OFF {denseOff}");
                }
                src.ShellFill = true;
            }
        }
        finally { src.Dispose(); }
        Directory.CreateDirectory(Path.Combine("Logs", "TerrainGen", "towns190"));
        File.WriteAllText(Path.Combine("Logs", "TerrainGen", "towns190", "chunks.txt"), log.ToString());
        UnityEngine.Debug.Log(log.ToString());
    }
}
