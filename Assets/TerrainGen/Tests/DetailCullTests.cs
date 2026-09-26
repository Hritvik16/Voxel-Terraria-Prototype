// Assets/TerrainGen/Tests/DetailCullTests.cs
//
// D's brick culling (DetailField.CullBrick), design doc §9 step 2:
//   GradientBound   |grad snoise| measured over 400k points sits below
//                   SNOISE_GRAD_BOUND with a 1.25x margin -- the one fact the
//                   exactness argument rests on
//   Exactness       culled == unculled, voxel for voxel, over cave-dense
//                   regions in every layer (tunnels, caverns, underworld, a pocket)
//   Report          a CAVE-DENSE 128^3 chunk (the densest of 16 cavern-layer
//                   candidates): D time culled vs unculled under Burst, one
//                   thread, and its dense-brick count (§5.3's memory risk).
//                   Written to Logs/TerrainGen/detail_cull.txt.
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
    public class DetailCullTests
    {
        private const uint SEED = 42;
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
        private struct RegionJob : IJob
        {
            [ReadOnly] public NativeArray<float> h, r;
            public TerrainSpec spec; public Offsets o;
            public int x0, y0, z0, nx, ny, nz;
            public bool cull;
            public NativeArray<ColumnSample> colS;
            public NativeArray<float> colStep;
            public NativeArray<DetailField.ColumnDetail> colCd;
            [WriteOnly] public NativeArray<byte> outM;
            public NativeArray<int> stats;   // 0 bricks, 1 noPocket, 2 noTunnel, 3 noCheese, 4 underClosed, 5 dense bricks
            public NativeArray<int> managedFlag;

            public unsafe void Execute()
            {
                bool managed = false; MarkManaged(ref managed); managedFlag[0] = managed ? 1 : 0;
                var map = new MapView { height = (float*)h.GetUnsafeReadOnlyPtr(), rough = (float*)r.GetUnsafeReadOnlyPtr(),
                                        cells = spec.mapCells, cellVoxels = spec.mapCellVoxels };
                for (int k = 0; k < nz; k++)
                for (int i = 0; i < nx; i++)
                {
                    int x = x0 + i, z = z0 + k, ci = k * nx + i;
                    var c = ShapeField.Sample(in spec, in map, in o, x + 0.5f, z + 0.5f, 1);
                    colS[ci] = c;
                    colStep[ci] = ShapeField.MaxStep(in spec, in map, in o, x + 0.5f, z + 0.5f, 1, c.height);
                    colCd[ci] = DetailField.PrepareColumn(in spec, in o, x, z);
                }
                for (int bz = 0; bz < nz; bz += 8)
                for (int by = 0; by < ny; by += 8)
                for (int bx = 0; bx < nx; bx += 8)
                {
                    var cr = cull ? DetailField.CullBrick(in spec, in o, x0 + bx, y0 + by, z0 + bz) : DetailField.BrickCull.None;
                    stats[0]++;
                    if (cr.noPocket) stats[1]++;
                    if (cr.noTunnel) stats[2]++;
                    if (cr.noCheese) stats[3]++;
                    if (cr.underClosed) stats[4]++;
                    byte first = 0; bool dense = false;
                    for (int dz = 0; dz < 8; dz++)
                    for (int dx = 0; dx < 8; dx++)
                    {
                        int ci = (bz + dz) * nx + bx + dx;
                        var c = colS[ci]; var cd = colCd[ci]; float st = colStep[ci];
                        for (int dy = 0; dy < 8; dy++)
                        {
                            int x = x0 + bx + dx, y = y0 + by + dy, z = z0 + bz + dz;
                            byte m = DetailField.Material(in spec, in o, in c, in cd, st, x, y, z, in cr);
                            outM[((bz + dz) * nx + bx + dx) * ny + by + dy] = m;
                            if (dz == 0 && dx == 0 && dy == 0) first = m; else if (m != first) dense = true;
                        }
                    }
                    if (dense) stats[5]++;
                }
            }
        }

        [BurstDiscard] private static void MarkManaged(ref bool b) { b = true; }

        private struct Run { public byte[] mats; public int[] stats; public double ms; public bool burst; }

        private static Run RunRegion(int3 origin, int3 size, bool cull, int reps = 1)
        {
            Assert.AreEqual(0, origin.x & 7); Assert.AreEqual(0, origin.y & 7); Assert.AreEqual(0, origin.z & 7);
            using var hA = new NativeArray<float>(_map.Height, Allocator.TempJob);
            using var rA = new NativeArray<float>(_map.Rough, Allocator.TempJob);
            int cols = size.x * size.z;
            using var colS = new NativeArray<ColumnSample>(cols, Allocator.TempJob);
            using var colStep = new NativeArray<float>(cols, Allocator.TempJob);
            using var colCd = new NativeArray<DetailField.ColumnDetail>(cols, Allocator.TempJob);
            using var outM = new NativeArray<byte>(cols * size.y, Allocator.TempJob);
            var stats = new NativeArray<int>(6, Allocator.TempJob);
            using var flag = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                var job = new RegionJob { h = hA, r = rA, spec = _spec, o = _o, x0 = origin.x, y0 = origin.y, z0 = origin.z,
                                          nx = size.x, ny = size.y, nz = size.z, cull = cull,
                                          colS = colS, colStep = colStep, colCd = colCd, outM = outM, stats = stats, managedFlag = flag };
                job.Run();   // warm + result
                var st = stats.ToArray();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < reps; i++) { for (int k = 0; k < 6; k++) stats[k] = 0; job.Run(); }
                return new Run { mats = outM.ToArray(), stats = st, ms = sw.Elapsed.TotalMilliseconds / reps, burst = flag[0] == 0 };
            }
            finally { stats.Dispose(); }
        }

        private static int3 Centre => new int3(_spec.worldSizeVoxels / 2 & ~127, 0, _spec.worldSizeVoxels / 2 & ~127);

        [Test]
        public void GradientBound_HoldsWithMargin()
        {
            var rng = new Unity.Mathematics.Random(0xC0FFEEu);
            float maxG = 0f;
            const float hs = 1e-3f;
            for (int i = 0; i < 400000; i++)
            {
                float3 p = rng.NextFloat3(-500f, 500f);
                float gx = (noise.snoise(p + new float3(hs, 0, 0)) - noise.snoise(p - new float3(hs, 0, 0))) / (2 * hs);
                float gy = (noise.snoise(p + new float3(0, hs, 0)) - noise.snoise(p - new float3(0, hs, 0))) / (2 * hs);
                float gz = (noise.snoise(p + new float3(0, 0, hs)) - noise.snoise(p - new float3(0, 0, hs))) / (2 * hs);
                maxG = math.max(maxG, math.length(new float3(gx, gy, gz)));
            }
            Debug.Log($"[DetailCull] measured max |grad snoise| = {maxG:F3}, bound {DetailField.SNOISE_GRAD_BOUND}");
            Write("detail_cull_gradient.txt", new List<string> { $"measured max |grad snoise| over 400k points: {maxG:F4}; bound used: {DetailField.SNOISE_GRAD_BOUND}; margin {DetailField.SNOISE_GRAD_BOUND / maxG:F2}x" });
            Assert.Less(maxG * 1.25f, DetailField.SNOISE_GRAD_BOUND, "the cull's gradient bound is not safe");
        }

        [Test]
        public void Culling_IsExact_InEveryLayer()
        {
            var c = Centre;
            var regions = new List<(string, int3)>
            {
                ("tunnels", new int3(c.x, (_spec.seaLevelY - 600) & ~7, c.z)),
                ("caverns", new int3(c.x + 512, (_spec.CavernTopY - 400) & ~7, c.z)),
                ("underworld", new int3(c.x - 512, (_spec.worldBottomY + 360) & ~7, c.z + 256)),
            };
            // A real pocket: scan cells near the centre for one.
            bool gotPocket = false;
            for (int cz = -6; cz <= 6 && !gotPocket; cz++)
            for (int cx = -6; cx <= 6 && !gotPocket; cx++)
            for (int cy = _spec.UnderworldTopY >> DetailField.POCKET_CELL_SHIFT; cy <= _spec.seaLevelY >> DetailField.POCKET_CELL_SHIFT && !gotPocket; cy++)
            {
                int pcx = (c.x >> DetailField.POCKET_CELL_SHIFT) + cx, pcz = (c.z >> DetailField.POCKET_CELL_SHIFT) + cz;
                if (!DetailField.TryGetPocket(in _spec, in _o, pcx, cy, pcz, out var pk)) continue;
                regions.Add(("pocket", new int3((int)pk.centre.x - 32, (int)pk.centre.y - 32, (int)pk.centre.z - 32) & ~7));
                gotPocket = true;
            }
            Assert.IsTrue(gotPocket, "no pocket found near the centre to test");
            var lines = new List<string>();
            foreach (var (name, o) in regions)
            {
                var size = new int3(64, 64, 64);
                var a = RunRegion(o, size, false);
                var b = RunRegion(o, size, true);
                int diff = 0, firstI = -1, carved = 0;
                for (int i = 0; i < a.mats.Length; i++)
                {
                    if (a.mats[i] != b.mats[i]) { diff++; if (firstI < 0) firstI = i; }
                    if (a.mats[i] == GenMat.Air || GenMat.IsFluid(a.mats[i])) carved++;
                }
                int n = b.stats[0];
                string line = $"{name,-11} origin {o}: {a.mats.Length:N0} voxels, {diff} differ; open/fluid {100.0 * carved / a.mats.Length:F1}%; " +
                              $"bricks culled: pocket {100.0 * b.stats[1] / n:F0}% tunnel {100.0 * b.stats[2] / n:F0}% " +
                              $"cheese {100.0 * b.stats[3] / n:F0}% underworld {100.0 * b.stats[4] / n:F0}%";
                lines.Add(line);
                Debug.Log("[DetailCull] " + line);
                Assert.AreEqual(0, diff, $"{name}: culled D differs from D at flat index {firstI}");
            }
            Write("detail_cull_exact.txt", lines);
        }

        [Test]
        public void Report_CaveDenseChunkCost()
        {
            var c = Centre;
            // The densest-caved of 16 cavern-layer chunks, by a coarse count.
            int3 best = default; int bestOpen = -1;
            int y0 = (_spec.CavernTopY - 1200) & ~127;
            for (int k = 0; k < 16; k++)
            {
                var o = new int3(c.x + (k % 4 - 2) * 1024, y0, c.z + (k / 4 - 2) * 1024);
                var r = RunRegion(o, new int3(32, 32, 32), false);
                int open = 0; foreach (byte m in r.mats) if (m == GenMat.Air || GenMat.IsFluid(m)) open++;
                if (open > bestOpen) { bestOpen = open; best = o; }
            }
            var size = new int3(128, 128, 128);
            var un = RunRegion(best, size, false, reps: 1);
            var cu = RunRegion(best, size, true, reps: 2);
            int diff = 0, open2 = 0;
            for (int i = 0; i < un.mats.Length; i++) { if (un.mats[i] != cu.mats[i]) diff++; if (un.mats[i] == GenMat.Air || GenMat.IsFluid(un.mats[i])) open2++; }
            // A near-surface chunk for comparison (mostly air above, strata below).
            var surfOrigin = new int3(c.x, 0, c.z);
            var s0 = ShapeField.Sample(in _spec, _map.View, in _o, c.x + 64.5f, c.z + 64.5f, 1);
            surfOrigin.y = (s0.height - 64) & ~127;
            var su = RunRegion(surfOrigin, size, false, reps: 1);
            var sc = RunRegion(surfOrigin, size, true, reps: 2);
            int bricks = cu.stats[0];
            var lines = new List<string>
            {
                $"CAVE-DENSE CHUNK (densest of 16 cavern-layer candidates) at {best}: {100.0 * open2 / un.mats.Length:F1}% open or fluid",
                $"  D unculled {un.ms:F0} ms, culled {cu.ms:F0} ms per 128^3 chunk, one thread, {(cu.burst ? "BURST" : "MANAGED")} " +
                $"({1e6 * un.ms / un.mats.Length:F1} -> {1e6 * cu.ms / cu.mats.Length:F1} ns/voxel, {un.ms / math.max(1e-3, cu.ms):F1}x); {diff} voxels differ",
                $"  bricks culled: pocket {100.0 * cu.stats[1] / bricks:F0}%  tunnel {100.0 * cu.stats[2] / bricks:F0}%  cheese {100.0 * cu.stats[3] / bricks:F0}%",
                $"  DENSE bricks (not one material): {cu.stats[5]} of {bricks} ({100.0 * cu.stats[5] / bricks:F1}%) -- design doc §5.3's memory risk; " +
                $"today's surface-only peak is 336,731 dense bricks for the whole window",
                $"NEAR-SURFACE CHUNK at {surfOrigin}: D unculled {su.ms:F0} ms, culled {sc.ms:F0} ms; dense bricks {sc.stats[5]} of {sc.stats[0]}",
            };
            Debug.Log("[DetailCull]\n" + string.Join("\n", lines));
            Write("detail_cull.txt", lines);
            Assert.AreEqual(0, diff, "culled D differs on the cave-dense chunk");
        }

        private static void Write(string name, List<string> lines)
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen");
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, name), lines);
        }
    }
}
