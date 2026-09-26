// Assets/TerrainGen/Tests/V0DenseBrickTests.cs
//
// V0 of the V/H phase (design doc §11; owner go-ahead 2026-09-25: "V0 first,
// check-in before V1"). THE MEMORY QUESTION: how many 8^3 bricks of a TerrainGen
// chunk are DENSE (hold more than one material) and so need a 512-byte body?
// Measured per chunk class, seed 42, under each candidate rule:
//
//   R0  today: per-voxel geology everywhere
//   R1  brick geology below 20 m (TerrainSpec.brickGeologyDepth = 200): one
//       band decision per brick, at its centre
//   R2  a shorter CPU ring (8 layers, +-51 m) instead of 16 -- no change to the
//       bricks, only to how many underground chunks the ring holds
//
// Chunk classes (128^3 voxels, 4,096 bricks): SURFACE (the chunk holding the
// top), SHALLOW (two chunks below it), CAVERN (the cavern layer), UNDERWORLD
// (the lava sea's chamber). Four land sites each. Evaluated under Burst, like
// the chunk generator. Projection: a 32 x 32 ring with the camera on the
// surface holds, per column, the surface chunk plus (layers/2 - 1) shallow
// ones; air above is uniform. Report: Logs/TerrainGen/v0_dense_bricks.txt.
// Asserts only that the report ran (a measurement, not yet a gate).
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class V0DenseBrickTests
    {
        private const uint SEED = 42;
        private const int CHUNK = 128;
        private static TerrainSpec _spec;
        private static Offsets _o;
        private static WorldMap _map;

        [OneTimeSetUp]
        public void BuildOnce()
        {
            if (_map != null) return;
            _spec = TerrainSpec.SizeClass2(SEED);
            _o = Offsets.For(SEED);
            _map = WorldMap.Build(_spec);
        }

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
        private struct DenseJob : IJob
        {
            [ReadOnly] public NativeArray<float> h, r, w;
            public TerrainSpec spec; public Offsets o;
            public int3 origin;
            public NativeArray<ColumnSample> colS;
            public NativeArray<float> colStep;
            public NativeArray<DetailField.ColumnDetail> colCd;
            public NativeArray<int> stats;   // 0 dense bricks, 1 air-only bricks, 2 solid uniform bricks

            public unsafe void Execute()
            {
                var map = new MapView { height = (float*)h.GetUnsafeReadOnlyPtr(), rough = (float*)r.GetUnsafeReadOnlyPtr(),
                                        water = (float*)w.GetUnsafeReadOnlyPtr(), cells = spec.mapCells, cellVoxels = spec.mapCellVoxels };
                for (int k = 0; k < CHUNK; k++)
                for (int i = 0; i < CHUNK; i++)
                {
                    int x = origin.x + i, z = origin.z + k, ci = k * CHUNK + i;
                    var c = ShapeField.Sample(in spec, in map, in o, x + 0.5f, z + 0.5f, 1);
                    colS[ci] = c;
                    colStep[ci] = ShapeField.MaxStep(in spec, in map, in o, x + 0.5f, z + 0.5f, 1, c.height);
                    colCd[ci] = DetailField.PrepareColumn(in o, x, z);
                }
                for (int bz = 0; bz < CHUNK; bz += 8)
                for (int by = 0; by < CHUNK; by += 8)
                for (int bx = 0; bx < CHUNK; bx += 8)
                {
                    var cr = DetailField.CullBrick(in spec, in o, origin.x + bx, origin.y + by, origin.z + bz);
                    byte first = 0; bool dense = false;
                    for (int dz = 0; dz < 8 && !dense; dz++)
                    for (int dx = 0; dx < 8 && !dense; dx++)
                    {
                        int ci = (bz + dz) * CHUNK + bx + dx;
                        var c = colS[ci]; var cd = colCd[ci]; float st = colStep[ci];
                        for (int dy = 0; dy < 8; dy++)
                        {
                            byte m = DetailField.Material(in spec, in o, in c, in cd, st, origin.x + bx + dx, origin.y + by + dy, origin.z + bz + dz, in cr);
                            if (dz == 0 && dx == 0 && dy == 0) first = m;
                            else if (m != first) { dense = true; break; }
                        }
                    }
                    if (dense) stats[0]++;
                    else if (first == GenMat.Air) stats[1]++;
                    else stats[2]++;
                }
            }
        }

        private static int Dense(in TerrainSpec spec, int3 origin)
        {
            using var hA = new NativeArray<float>(_map.Height, Allocator.TempJob);
            using var rA = new NativeArray<float>(_map.Rough, Allocator.TempJob);
            using var wA = new NativeArray<float>(_map.Water, Allocator.TempJob);
            using var colS = new NativeArray<ColumnSample>(CHUNK * CHUNK, Allocator.TempJob);
            using var colStep = new NativeArray<float>(CHUNK * CHUNK, Allocator.TempJob);
            using var colCd = new NativeArray<DetailField.ColumnDetail>(CHUNK * CHUNK, Allocator.TempJob);
            using var stats = new NativeArray<int>(3, Allocator.TempJob);
            new DenseJob { h = hA, r = rA, w = wA, spec = spec, o = _o, origin = origin, colS = colS, colStep = colStep, colCd = colCd, stats = stats }.Run();
            return stats[0];
        }

        /// Four land sites: one per quadrant, 30-45% of the island radius out,
        /// the first column along the bearing that stands 10 m above the sea.
        private static List<int3> Sites()
        {
            var list = new List<int3>();
            float c = _spec.worldSizeVoxels * 0.5f;
            var view = _map.View;
            float2[] dirs = { new float2(0.8f, 0.6f), new float2(-0.6f, 0.8f), new float2(-0.8f, -0.6f), new float2(0.6f, -0.8f) };
            foreach (var d in dirs)
            for (float f = 0.30f; f < 0.80f; f += 0.05f)
            {
                float2 p = new float2(c, c) + d * (_spec.islandRadius * f);
                var col = ShapeField.Sample(in _spec, in view, in _o, p.x, p.y, 1);
                if (col.height < _spec.seaLevelY + 100 || col.water > col.height) continue;
                list.Add(new int3((int)p.x & ~(CHUNK - 1), col.height & ~(CHUNK - 1), (int)p.y & ~(CHUNK - 1)));
                break;
            }
            return list;
        }

        [Test]
        public void Report_V0_DenseBricksPerChunkClass()
        {
            var sites = Sites();
            Assert.GreaterOrEqual(sites.Count, 3, "fewer than 3 land sites found");
            var r1 = _spec; r1.brickGeologyDepth = 200;
            string[] cls = { "SURFACE", "SHALLOW (2 chunks down)", "CAVERN layer", "UNDERWORLD" };
            var mean = new double[4, 2];
            var lines = new List<string> { "V0 -- dense bricks per 128^3 chunk (of 4,096), seed 42, Burst", "" ,
                                           $"{"class",-26}{"site",-22}{"R0 today",10}{"R1 brick geology",18}" };
            foreach (var s in sites)
            {
                int3[] origins =
                {
                    s,
                    s - new int3(0, 2 * CHUNK, 0),
                    new int3(s.x, (_spec.CavernTopY - 600) & ~(CHUNK - 1), s.z),
                    new int3(s.x, (_spec.worldBottomY + 250) & ~(CHUNK - 1), s.z),
                };
                for (int k = 0; k < 4; k++)
                {
                    int a = Dense(in _spec, origins[k]), b = Dense(in r1, origins[k]);
                    mean[k, 0] += a / (double)sites.Count; mean[k, 1] += b / (double)sites.Count;
                    lines.Add($"{cls[k],-26}{origins[k].ToString(),-22}{a,10}{b,18}");
                }
            }
            lines.Add("");
            lines.Add($"{"mean",-48}{"R0",10}{"R1",18}");
            for (int k = 0; k < 4; k++) lines.Add($"{cls[k],-48}{mean[k, 0],10:F0}{mean[k, 1],18:F0}");
            lines.Add("");
            // Ring projection: 32 x 32 columns; per column the surface chunk and
            // (layers/2 - 1) shallow chunks below it (air above is uniform).
            const double BODY = 512.0, MB = 1024.0 * 1024.0;
            lines.Add("PROJECTED RING (32 x 32 columns, camera on the surface; 512 B per dense brick body):");
            foreach (int layers in new[] { 16, 8 })
            {
                int below = layers / 2 - 1;
                double r0 = 1024 * (mean[0, 0] + below * mean[1, 0]), rr1 = 1024 * (mean[0, 1] + below * mean[1, 1]);
                lines.Add($"  {layers,2} layers ({(layers == 16 ? "today's WINDOW_CHUNKS_Y" : "R2, +-51 m")}): R0 {r0 / 1e6:F2} M bricks = {r0 * BODY / MB:F0} MB   R1 {rr1 / 1e6:F2} M = {rr1 * BODY / MB:F0} MB");
            }
            lines.Add("  reference: today's whole window peaks at 336,731 dense bricks (164 MB) in a 366 MB shared pool");
            lines.Add("  a camera DEEP underground (cavern layer) fills the ring with CAVERN-class chunks instead: " +
                      $"R0 {1024 * (0.5 * 16) * mean[2, 0] * BODY / MB:F0} MB, R1 {1024 * (0.5 * 16) * mean[2, 1] * BODY / MB:F0} MB for half the 16-layer ring");
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen");
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "v0_dense_bricks.txt"), lines);
            Debug.Log("[V0]\n" + string.Join("\n", lines));
        }
    }
}
