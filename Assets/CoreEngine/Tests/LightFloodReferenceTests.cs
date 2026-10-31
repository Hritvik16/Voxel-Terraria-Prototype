// ==========================================
// LightFloodReferenceTests.cs -- task 157 (TORCH_LIGHT_PLAN.md step L1, owner 2026-10-30 "a to all")
//
// The torch light-flood CPU oracle (LightFloodReference), the plan's tests 1-6.
// Every shape is BUILT through the edit-op API (EditOps.Record of Box ops, as
// a player's build would be) or, for test 6's chunk re-admission, written back
// voxel by voxel through ChunkStore.SetVoxel; never taken from a generator
// (the 2026-10-24 generality rule). The adversarial cases are the proof:
//   (1) sealed walls give EXACTLY 0 on the far side: an axis 0.1 m wall, a
//       staircase, the edge-touching diagonal (x == z), the 3D diagonal plane
//       x + y + z == K, a closed 0.1 m box round the torch; each also built
//       AFTER the torch is lit (the incremental closing path)
//   (2) open air: flood distance within 8% of Euclid for every lit voxel at 12 m
//   (3) an L-bend: lit round the corner, at the PATH length (within 8% of the
//       geodesic round the inner edge), never brighter than straight-line
//   (4) fuzz: random torch / block / dig / thin-wall sequences, incremental ==
//       from-scratch byte for byte after every op
//   (5) removing every torch frees every brick slot and chunk table
//   (6) evict / edit / re-admit chunks == from-scratch after every step
// REPORTS=1 (Slow) writes Screenshots/157/torchflood157.txt (memory, split
// cells, worker ms) and the slice PNGs.
// ==========================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Streaming;

public class LightFloodReferenceTests
{
    private BrickDataPool _pool;
    private ChunkHandleAllocator _alloc;
    private ChunkStore _store;
    private EditOps _ops;
    private LightFloodReference _light;

    [TearDown]
    public void Teardown()
    {
        _light?.Dispose(); _light = null;
        _pool?.Dispose(); _pool = null;
    }

    /// A world of `chunks` (all resident), every chunk uniform `fill`.
    private void NewWorld(int3 chunks, byte fill = 0, int poolBricks = 80000)
    {
        _light?.Dispose();
        _pool?.Dispose();
        _pool = new BrickDataPool(poolBricks);
        _alloc = new ChunkHandleAllocator(512);
        _store = new ChunkStore(_pool, _alloc);
        for (int cz = 0; cz < chunks.z; cz++)
        for (int cy = 0; cy < chunks.y; cy++)
        for (int cx = 0; cx < chunks.x; cx++)
            _store.InsertChunk(new Chunk { coord = new int3(cx, cy, cz), isUniform = true, uniformMaterial = fill });
        var edits = new EditService();
        edits.AttachWorld(_store, _store, _store, null);
        _ops = new EditOps(EditOpLog.InMemory(157u), edits, _store);
        _light = new LightFloodReference(_store);
    }

    /// The inclusive box lo..hi as Box edit ops (SkyVisReferenceTests' cover).
    private void Fill(int3 lo, int3 hi, byte m)
    {
        int3 ext = hi - lo + 1;
        int e = math.cmin(ext);
        if ((e & 1) == 0) e--;
        int s = (e - 1) / 2;
        foreach (int x in Starts(lo.x, hi.x, e))
        foreach (int y in Starts(lo.y, hi.y, e))
        foreach (int z in Starts(lo.z, hi.z, e))
            _ops.Record(new EditOp(EditOpKind.Box, new int3(x, y, z) + s, (ushort)s, m));
    }

    private static IEnumerable<int> Starts(int lo, int hi, int e)
    {
        int a = lo;
        for (; a + e - 1 < hi; a += e) yield return a;
        yield return hi - e + 1;
    }

    private void Put(int3 v, byte m) => _ops.Record(new EditOp(EditOpKind.Box, v, 0, m));

    private static List<int3> Cells(int3 lo, int3 hi)
    {
        var l = new List<int3>();
        for (int z = lo.z; z <= hi.z; z++)
        for (int y = lo.y; y <= hi.y; y++)
        for (int x = lo.x; x <= hi.x; x++) l.Add(new int3(x, y, z));
        return l;
    }

    private void PlaceTorch(int3 v, uint seed)
    {
        Put(v, Materials.Torch);
        _light.OnVoxelChanged(v);
        _light.AddTorch(v, seed);
    }

    private void AssertSameAsScratch(string what)
    {
        using (var s = LightFloodReference.FromScratch(_store, _light.Torches))
        {
            string d = _light.Diff(s);
            if (d != null) Assert.Fail($"{what}: incremental != from-scratch: {d}");
        }
    }

    private static readonly uint White12 = LightFloodReference.Pack(515, 515, 515);

    // ---------------------------------------------------------------------
    // The metric itself
    // ---------------------------------------------------------------------

    [Test]
    public void Metric_MoveTableAndConstants()
    {
        Assert.AreEqual(515, LightFloodReference.ReachLevel(12f), "12 m reach in level units");
        int faces = 0, edges = 0, corners = 0;
        for (int m = 0; m < 26; m++)
        {
            int n = math.csum(math.abs(LightFloodReference.MoveDelta[m]));
            int fp = LightFloodReference.MoveFootprint[m].Length;
            if (n == 1) { faces++; Assert.AreEqual(0, fp); Assert.AreEqual(4, LightFloodReference.MoveWeight[m]); }
            if (n == 2) { edges++; Assert.AreEqual(2, fp); Assert.AreEqual(6, LightFloodReference.MoveWeight[m]); }
            if (n == 3) { corners++; Assert.AreEqual(6, fp); Assert.AreEqual(7, LightFloodReference.MoveWeight[m]); }
        }
        Assert.AreEqual(new int3(6, 12, 8), new int3(faces, edges, corners));
        // colour: the brightest channel reaches the full 12 m, a dimmer one less
        uint s = LightFloodReference.SeedForMaterial(Materials.Torch);
        Assert.AreEqual(515, LightFloodReference.R(s));
        Assert.Less(LightFloodReference.B(s), LightFloodReference.G(s));
        Assert.Greater(LightFloodReference.B(s), 0);
    }

    // ---------------------------------------------------------------------
    // (1) Sealed walls: exactly 0 behind them
    // ---------------------------------------------------------------------

    private static readonly int3 RoomLo = new int3(20, 20, 20), RoomHi = new int3(79, 49, 79);

    /// A 6 x 3 x 6 m room inside rock (one chunk, the rest unloaded).
    private void BuildRoom()
    {
        NewWorld(new int3(1, 1, 1), Materials.Stone);
        Fill(RoomLo, RoomHi, 0);
    }

    private void AssertFarSideDark(Func<int3, bool> far, Func<int3, bool> wall, int3 torch, string what)
    {
        int farCells = 0, nearLit = 0;
        for (int z = RoomLo.z; z <= RoomHi.z; z++)
        for (int y = RoomLo.y; y <= RoomHi.y; y++)
        for (int x = RoomLo.x; x <= RoomHi.x; x++)
        {
            var v = new int3(x, y, z);
            if (wall(v) || v.Equals(torch)) continue;
            uint l = _light.Get(v);
            if (far(v))
            {
                farCells++;
                if (l != 0) Assert.Fail($"{what}: far-side voxel {v} is lit {LightFloodReference.Str(l)}");
            }
            else if (l != 0) nearLit++;
        }
        Assert.Greater(farCells, 1000, $"{what}: the far side exists");
        Assert.Greater(nearLit, 500, $"{what}: the near side is lit");
    }

    private void SealedCase(Func<int3, bool> wall, Func<int3, bool> far, int3 torch, string what)
    {
        var wallCells = new List<int3>();
        foreach (int3 v in Cells(RoomLo, RoomHi)) if (wall(v)) wallCells.Add(v);

        // built first, then the torch (the SSSP path)
        BuildRoom();
        foreach (int3 v in wallCells) Put(v, Materials.Stone);
        PlaceTorch(torch, White12);
        AssertFarSideDark(far, wall, torch, what + " (wall first)");

        // the torch first, then the wall (the incremental closing path)
        BuildRoom();
        PlaceTorch(torch, White12);
        foreach (int3 v in wallCells) Put(v, Materials.Stone);
        _light.OnVoxelsChanged(wallCells);
        AssertFarSideDark(far, wall, torch, what + " (wall after the torch)");
        AssertSameAsScratch(what);
    }

    [Test]
    public void Sealed_AxisWall_0p1m()
    {
        SealedCase(v => v.x == 50, v => v.x > 50, new int3(30, 30, 50), "axis wall x = 50");
    }

    [Test]
    public void Sealed_Staircase()
    {
        // face-connected staircase: x - z in {0, 1}
        SealedCase(v => v.x - v.z == 0 || v.x - v.z == 1, v => v.x - v.z < 0, new int3(60, 30, 30), "staircase");
    }

    [Test]
    public void Sealed_EdgeTouchingDiagonal()
    {
        // voxels touch only along their y edges: no face contact anywhere
        SealedCase(v => v.x == v.z, v => v.x < v.z, new int3(60, 30, 30), "diagonal x == z");
    }

    [Test]
    public void Sealed_DiagonalPlane3D()
    {
        // x + y + z == 150: every voxel touches its neighbours by edges only
        SealedCase(v => v.x + v.y + v.z == 150, v => v.x + v.y + v.z > 150, new int3(30, 30, 30), "plane x + y + z = 150");
    }

    [Test]
    public void Sealed_ClosedBoxAroundTorch()
    {
        int3 t = new int3(50, 35, 50);
        Func<int3, bool> shell = v => math.cmax(math.abs(v - t)) == 5;
        SealedCase(shell, v => math.cmax(math.abs(v - t)) > 5, t, "0.1 m box round the torch");
    }

    [Test]
    public void Sealed_DoorwayOpensAndClosesExactly()
    {
        // a wall with a 1 x 2 m doorway: open -> the far side lit; sealed -> exactly 0 again
        BuildRoom();
        var wall = Cells(new int3(50, RoomLo.y, RoomLo.z), new int3(50, RoomHi.y, RoomHi.z));
        foreach (int3 v in wall) Put(v, Materials.Stone);
        PlaceTorch(new int3(30, 30, 50), White12);
        var door = Cells(new int3(50, 20, 45), new int3(50, 39, 54));
        foreach (int3 v in door) Put(v, 0);
        _light.OnVoxelsChanged(door);
        Assert.Greater(_light.Get(new int3(60, 30, 50)), 0u, "lit through the doorway");
        AssertSameAsScratch("doorway open");
        foreach (int3 v in door) Put(v, Materials.Stone);
        _light.OnVoxelsChanged(door);
        AssertFarSideDark(v => v.x > 50, v => v.x == 50, new int3(30, 30, 50), "doorway sealed again");
        AssertSameAsScratch("doorway sealed");
    }

    // ---------------------------------------------------------------------
    // (2) Open air: within 8% of Euclid
    // ---------------------------------------------------------------------

    [Test]
    public void OpenAir_FalloffWithin8PercentOfEuclid()
    {
        NewWorld(new int3(3, 3, 3));
        int3 t = new int3(192, 192, 192);
        uint seed = LightFloodReference.SeedForMaterial(Materials.Torch);
        PlaceTorch(t, seed);
        int top = LightFloodReference.MaxChannel(seed);
        double lo = 9, hi = 0, maxLitEuclidM = 0;
        long lit = 0;
        foreach (var (b, d) in _light.LitBricks())
            for (int i = 0; i < 512; i++)
            {
                if (d[i] == 0) continue;
                lit++;
                int3 v = (b << 3) + new int3(i & 7, (i >> 3) & 7, i >> 6);
                double eM = math.length((float3)(v - t)) * 0.1;
                double fM = LightFloodReference.UnitsToMetres(top - LightFloodReference.MaxChannel(d[i]));
                double r = fM / eM;
                lo = math.min(lo, r); hi = math.max(hi, r);
                maxLitEuclidM = math.max(maxLitEuclidM, eM);
            }
        TestContext.WriteLine($"open air 12 m: {lit:N0} lit voxels, flood/Euclid {lo:F4} .. {hi:F4}, furthest lit {maxLitEuclidM:F2} m");
        Assert.GreaterOrEqual(lo, 0.92, "flood distance never more than 8% short of Euclid");
        Assert.LessOrEqual(hi, 1.08, "flood distance never more than 8% beyond Euclid");
        // the reach: everything within 11 m is lit, nothing beyond 12.9 m
        Assert.LessOrEqual(maxLitEuclidM, 12.9);
        for (int k = 0; k < 26; k++)
        {
            float3 dir = math.normalize((float3)LightFloodReference.MoveDelta[k]);
            int3 v = t + (int3)math.round(dir * 110f);
            Assert.Greater(_light.Get(v), 0u, $"11 m along {LightFloodReference.MoveDelta[k]} is lit");
        }
    }

    // ---------------------------------------------------------------------
    // (3) An L-bend: path-length falloff round the corner
    // ---------------------------------------------------------------------

    // leg A along +x (x 20..90, z 20..29), leg B along +z (x 81..90, z 20..110); 2 m tall (y 20..39)
    private void BuildLBend(int3 torch)
    {
        NewWorld(new int3(1, 1, 1), Materials.Stone);
        Fill(new int3(20, 20, 20), new int3(90, 39, 29), 0);
        Fill(new int3(81, 20, 20), new int3(90, 39, 110), 0);
        PlaceTorch(torch, White12);
    }

    [Test]
    public void LBend_LitRoundTheCornerAtPathLength()
    {
        int3 t = new int3(20, 30, 24);
        BuildLBend(t);
        int top = 515;
        var tc = new double3(t.x + 0.5, t.y + 0.5, t.z + 0.5);
        var corner = new double2(81, 30);                    // the inner vertical edge (voxel boundaries)
        double h1 = math.length(new double2(tc.x, tc.z) - corner);
        int checkedCells = 0;
        double lo = 9, hi = 0;
        for (int z = 40; z <= 110; z++)
        for (int y = 20; y <= 39; y++)
        for (int x = 81; x <= 90; x++)
        {
            var v = new int3(x, y, z);
            uint l = _light.Get(v);
            var pc = new double3(x + 0.5, y + 0.5, z + 0.5);
            double h2 = math.length(new double2(pc.x, pc.z) - corner);
            double geo = math.sqrt((h1 + h2) * (h1 + h2) + (pc.y - tc.y) * (pc.y - tc.y));   // voxels
            double eu = math.length(pc - tc);
            double geoM = geo * 0.1;
            if (l == 0)
            {
                // unlit only beyond the reach along the path (6.8% chamfer slack)
                Assert.Greater(geoM, 12.0 / 1.08, $"{v} unlit at path {geoM:F2} m");
                continue;
            }
            double fM = LightFloodReference.UnitsToMetres(top - LightFloodReference.MaxChannel(l));
            double r = fM / geoM;
            lo = math.min(lo, r); hi = math.max(hi, r);
            Assert.Greater(fM, eu * 0.1 * 1.0, $"{v}: round the bend is darker than straight-line distance");
            checkedCells++;
        }
        TestContext.WriteLine($"L-bend: {checkedCells} lit voxels round the corner, flood/geodesic {lo:F3} .. {hi:F3}");
        Assert.Greater(checkedCells, 3000, "leg B is lit round the corner");
        Assert.GreaterOrEqual(lo, 0.92); Assert.LessOrEqual(hi, 1.08);
        // darkens monotonically along leg B's centre line
        int prev = int.MaxValue;
        for (int z = 30; z <= 100; z += 5)
        {
            int l = LightFloodReference.MaxChannel(_light.Get(new int3(85, 30, z)));
            Assert.Less(l, prev, $"leg B centre z {z}");
            prev = l;
            if (l == 0) break;
        }
    }

    // ---------------------------------------------------------------------
    // (4) + (5) Fuzz: incremental == from-scratch, then every slot freed
    // ---------------------------------------------------------------------

    private static readonly byte[] TorchMats = { Materials.Torch, Materials.TorchBlue, Materials.TorchGreen, Materials.TorchPurple };

    private void Fuzz(int ops, int seed)
    {
        NewWorld(new int3(1, 1, 1), Materials.Stone);
        var rng = new System.Random(seed);
        int3 lo = new int3(40, 40, 40), hi = new int3(87, 87, 87);
        Fill(lo, hi, 0);
        // a few starting walls so the region is not one open box
        Fill(new int3(60, 40, 40), new int3(60, 87, 87), Materials.Stone);
        Fill(new int3(40, 60, 40), new int3(87, 60, 87), Materials.Stone);
        var torches = new List<int3>();
        int3 Rand(int3 a, int3 b) => new int3(rng.Next(a.x, b.x + 1), rng.Next(a.y, b.y + 1), rng.Next(a.z, b.z + 1));
        bool HitsTorch(List<int3> cells) { foreach (int3 c in cells) if (torches.Contains(c)) return true; return false; }
        var counts = new int[6];

        for (int i = 0; i < ops; i++)
        {
            int kind = rng.Next(100);
            string what;
            if (kind < 22 || torches.Count == 0)
            {
                int3 v = Rand(lo, hi);
                if (torches.Contains(v)) continue;
                byte m = TorchMats[rng.Next(TorchMats.Length)];
                PlaceTorch(v, LightFloodReference.SeedForMaterial(m, 3f));
                torches.Add(v);
                what = $"place torch {v}"; counts[0]++;
            }
            else if (kind < 35)
            {
                int3 v = torches[rng.Next(torches.Count)];
                Assert.IsTrue(_light.RemoveTorch(v));
                torches.Remove(v);
                Put(v, rng.Next(2) == 0 ? (byte)0 : Materials.Stone);
                _light.OnVoxelChanged(v);
                what = $"remove torch {v}"; counts[1]++;
            }
            else if (kind < 80)
            {
                bool solid = kind < 57;
                int s = rng.Next(0, solid ? 2 : 3);
                int3 c = Rand(lo, hi);
                var cells = Cells(c - s, c + s);
                if (HitsTorch(cells)) continue;
                _ops.Record(new EditOp(EditOpKind.Box, c, (ushort)s, solid ? Materials.Stone : (byte)0));
                _light.OnVoxelsChanged(cells);
                what = $"{(solid ? "block" : "dig")} {c} s{s}"; counts[solid ? 2 : 3]++;
            }
            else
            {
                // a 0.1 m wall segment: axis slab or edge-touching diagonal, placed or cut
                bool solid = rng.Next(2) == 0;
                var cells = new List<int3>();
                int3 c = Rand(lo, hi);
                int len = rng.Next(4, 16);
                if (rng.Next(2) == 0)
                {
                    int axis = rng.Next(3);
                    foreach (int3 v in Cells(c, c + len)) { int3 w = v; w[axis] = c[axis]; if (!cells.Contains(w)) cells.Add(w); }
                }
                else
                    for (int k = 0; k < len; k++) for (int y = 0; y < len; y++) cells.Add(c + new int3(k, y, k));
                cells.RemoveAll(v => math.any(v < lo) || math.any(v > hi));
                if (cells.Count == 0 || HitsTorch(cells)) continue;
                foreach (int3 v in cells) Put(v, solid ? Materials.Stone : (byte)0);
                _light.OnVoxelsChanged(cells);
                what = $"thin {(solid ? "wall" : "cut")} {c} x{cells.Count}"; counts[4]++;
            }
            AssertSameAsScratch($"op {i} ({what})");
        }
        TestContext.WriteLine($"fuzz seed {seed}: torches placed {counts[0]}, removed {counts[1]}, blocks {counts[2]}, digs {counts[3]}, thin {counts[4]}; " +
                              $"{torches.Count} torches left, {_light.UsedBrickSlots} brick slots");
        Assert.Greater(_light.UsedBrickSlots, 0);

        // (5) remove every torch: every slot back, every chunk table gone
        foreach (int3 v in torches) Assert.IsTrue(_light.RemoveTorch(v));
        Assert.AreEqual(0, _light.UsedBrickSlots, "every brick slot freed");
        Assert.AreEqual(0, _light.ChunkTables, "every chunk table freed");
        Assert.AreEqual(0, _light.TorchCount);
    }

    [Test]
    public void Fuzz_IncrementalEqualsScratch_ThenAllSlotsFreed() => Fuzz(80, 157);

    [Test, Category("Slow")]
    public void Fuzz_Long() { Fuzz(300, 1571); Fuzz(300, 1572); }

    [Test]
    public void RemoveEveryTorch_FreesEverySlot()
    {
        BuildRoom();
        var ts = new List<int3>();
        for (int k = 0; k < 6; k++) { var v = new int3(25 + 10 * k, 22 + 4 * k, 30 + 7 * k); PlaceTorch(v, LightFloodReference.SeedForMaterial(TorchMats[k & 3], 4f)); ts.Add(v); }
        Assert.Greater(_light.UsedBrickSlots, 0);
        foreach (int3 v in ts) _light.RemoveTorch(v);
        Assert.AreEqual(0, _light.UsedBrickSlots);
        Assert.AreEqual(0, _light.ChunkTables);
    }

    // ---------------------------------------------------------------------
    // (6) Evict / edit / re-admit == from-scratch
    // ---------------------------------------------------------------------

    // 3 x 1 x 3 chunks. Ground below y 20; a wall along x = 128 (the chunk seam) with a
    // doorway; a wall along z = 250 near the next seam; torches on both sides of the seams.
    private static readonly (int3 lo, int3 hi, byte m)[] Seam = {
        (new int3(0, 0, 0), new int3(383, 19, 383), Materials.Stone),
        (new int3(128, 20, 60), new int3(128, 40, 300), Materials.Stone),
        (new int3(100, 20, 250), new int3(300, 45, 250), Materials.Stone),
    };
    private static readonly int3[] DoorLo = { new int3(128, 20, 120) }, DoorHi = { new int3(128, 34, 129) };

    private void WriteChunk(int3 chunk, Dictionary<int3, byte> overrides)
    {
        int3 cl = chunk << 7, ch = cl + 127;
        foreach (var (lo, hi, m) in Seam)
        {
            int3 a = math.max(lo, cl), b = math.min(hi, ch);
            if (math.any(a > b)) continue;
            for (int z = a.z; z <= b.z; z++) for (int y = a.y; y <= b.y; y++) for (int x = a.x; x <= b.x; x++)
                _store.SetVoxel(new int3(x, y, z), m);
        }
        for (int k = 0; k < DoorLo.Length; k++)
            foreach (int3 v in Cells(DoorLo[k], DoorHi[k])) if (math.all((v >> 7) == chunk)) _store.SetVoxel(v, 0);
        foreach (var kv in overrides) if (math.all((kv.Key >> 7) == chunk)) _store.SetVoxel(kv.Key, kv.Value);
    }

    [Test]
    public void Streaming_EvictEditReadmit_EqualsScratch()
    {
        NewWorld(new int3(3, 1, 3));
        var overrides = new Dictionary<int3, byte>();
        for (int cz = 0; cz < 3; cz++) for (int cx = 0; cx < 3; cx++) WriteChunk(new int3(cx, 0, cz), overrides);

        var ts = new[] {
            new int3(124, 25, 124), new int3(133, 30, 130), new int3(126, 35, 200), new int3(140, 22, 255),
            new int3(250, 30, 131), new int3(129, 26, 245), new int3(60, 28, 124), new int3(255, 40, 255),
        };
        for (int k = 0; k < ts.Length; k++)
        {
            overrides[ts[k]] = TorchMats[k & 3];
            _store.SetVoxel(ts[k], TorchMats[k & 3]);
            _light.OnVoxelChanged(ts[k]);
            _light.AddTorch(ts[k], LightFloodReference.SeedForMaterial(TorchMats[k & 3], 4f));
        }
        AssertSameAsScratch("all resident");

        void Evict(int3 c) { _store.EvictChunk(c); _light.OnChunkEvicted(c); AssertSameAsScratch($"evict {c}"); }
        void Admit(int3 c)
        {
            _store.InsertChunk(new Chunk { coord = c, isUniform = true, uniformMaterial = 0 });
            WriteChunk(c, overrides);
            _light.OnChunkAdmitted(c);
            AssertSameAsScratch($"admit {c}");
        }

        Evict(new int3(1, 0, 1));
        Evict(new int3(0, 0, 0));
        // an edit while (1,0,1) is away: widen the doorway on the resident side of the seam
        var dig = Cells(new int3(127, 20, 118), new int3(127, 36, 131));
        foreach (int3 v in dig) { _store.SetVoxel(v, 0); overrides[v] = 0; }
        _light.OnVoxelsChanged(dig);
        AssertSameAsScratch("edit while evicted");
        Admit(new int3(1, 0, 1));
        Evict(new int3(1, 0, 0));
        Admit(new int3(0, 0, 0));
        Admit(new int3(1, 0, 0));
        Assert.Greater(_light.UsedBrickSlots, 0);
    }

    // =====================================================================
    // REPORT (REPORTS=1): memory, split cells, worker ms, slice PNGs
    // =====================================================================

    // task 138's site (SkyVisReferenceTests.BuildSite138, mouth +x)
    private static readonly int3 C138 = new int3(256, 200, 256);

    private void BuildSite138()
    {
        NewWorld(new int3(4, 4, 4));
        int3 C = C138, a = new int3(1, 0, 0);
        Fill(new int3(96, 180, 96), new int3(480, 199, 416), Materials.Stone);
        Fill(new int3(176, 100, 176), new int3(336, 199, 336), Materials.Stone);
        _ops.Record(new EditOp(EditOpKind.Box, C + new int3(0, 45, 0), 75, Materials.Stone));
        for (int k = 0; k < 3; k++)
            _ops.Record(new EditOp(EditOpKind.Box, C + a * (75 - 20 - 41 * k) + new int3(0, 25, 0), 20, 0));
        _ops.Record(new EditOp(EditOpKind.Box, C + new int3(0, -60, 0), 25, 0));
    }

    // 138's tunnel: torch by the -v (+z) wall 10.2 m in from the mouth (x 331), 1.5 m up
    private static readonly int3 TunnelTorch = new int3(229, 220, 276);
    // 138's sealed room (x, z 231..281, y 115..165): torch in a corner, 1.5 m up
    private static readonly int3 RoomTorch = new int3(232, 130, 232);

    /// Flat ground, top at y 203 (mid-brick: one mixed brick layer, as any non-aligned height gives).
    /// Built as generation would: chunk y 0 uniform stone; in chunk y 1 the bricks below 200 are
    /// uniform stone handles and the voxels 200..203 are written through SetVoxel.
    private void BuildGround(int3 chunks)
    {
        NewWorld(chunks, 0, 200000);
        for (int cz = 0; cz < chunks.z; cz++)
        for (int cx = 0; cx < chunks.x; cx++)
        {
            _store.EvictChunk(new int3(cx, 0, cz));
            _store.InsertChunk(new Chunk { coord = new int3(cx, 0, cz), isUniform = true, uniformMaterial = Materials.Stone });
            var c1 = new int3(cx, 1, cz);
            _store.EvictChunk(c1);
            var ch = new Chunk { coord = c1, isUniform = false, bricks = _alloc.Alloc() };
            for (int i = 0; i < 4096; i++) ch.bricks[i].data = ((i >> 4) & 15) < 9 ? Materials.Stone : 0u;   // brick y 16..24 = voxels 128..199
            _store.InsertChunk(ch);
            int3 o = c1 << 7;
            for (int z = 0; z < 128; z++) for (int y = 200; y <= 203; y++) for (int x = 0; x < 128; x++)
                _store.SetVoxel(new int3(o.x + x, y, o.z + z), Materials.Stone);
        }
    }

    /// A torch on a 0.1 m stone post, 1.5 m above the ground.
    private int3 PostTorch(int x, int z)
    {
        Fill(new int3(x, 204, z), new int3(x, 218, z), Materials.Stone);
        var t = new int3(x, 219, z);
        Put(t, Materials.Torch);
        return t;
    }

    private struct Mem
    {
        public int air, mixed, chunks, cells, split, splitLeak;
        public double MbPlan => (mixed * 2048.0 + air * 32.0 + chunks * 16384.0) / (1024 * 1024);
        public double MbCoarse => (mixed * 256.0 + split * 32.0 + air * 32.0 + chunks * 16384.0) / (1024 * 1024);
        public double MbOracle => ((mixed + air) * 2048.0 + chunks * 16384.0) / (1024 * 1024);
    }

    /// Terrain class of a world brick: 0 all solid, 1 all air, 2 mixed (`scratch` = open flags if 2).
    private int BrickClass(int3 wb, byte[] scratch)
    {
        Chunk ch = _store.GetChunk(wb >> 4);
        if (ch == null) return 0;
        if (ch.isUniform) return ch.uniformMaterial == 0 ? 1 : 0;
        uint h = ch.bricks[CoordMath.LocalBrickIndex(wb & 15)].data;
        if ((h & 0x80000000u) == 0) return (h & 0xFFu) == 0 ? 1 : 0;
        int air = 0, pool = (int)(h & 0x3FFFFFFFu);
        var raw = _store.BrickPool.RawData;
        for (int i = 0; i < 512; i++)
        {
            bool open = raw[pool * 512 + i] == 0;
            scratch[i] = open ? (byte)1 : (byte)0;
            if (open) air++;
        }
        return air == 0 ? 0 : air == 512 ? 1 : 2;
    }

    /// Split 0.2 m cells of a mixed brick: a 2x2x2 cell whose air voxels form >= 2
    /// face-connected groups inside the cell. `light` (optional) counts "leaky" ones:
    /// one group lit and another dark.
    private static void SplitCells(byte[] open, uint[] light, ref int cells, ref int split, ref int leak)
    {
        for (int c = 0; c < 64; c++)
        {
            int3 co = new int3(c & 3, (c >> 2) & 3, c >> 4) * 2;
            int[] idx = new int[8];
            for (int k = 0; k < 8; k++) { int3 l = co + new int3(k & 1, (k >> 1) & 1, k >> 2); idx[k] = l.x | (l.y << 3) | (l.z << 6); }
            int[] grp = new int[8];
            int groups = 0;
            for (int k = 0; k < 8; k++) grp[k] = -1;
            for (int k = 0; k < 8; k++)
            {
                if (open[idx[k]] == 0 || grp[k] >= 0) continue;
                var stack = new Stack<int>(); stack.Push(k); grp[k] = groups;
                while (stack.Count > 0)
                {
                    int u = stack.Pop();
                    for (int bit = 1; bit <= 4; bit <<= 1)
                    {
                        int w = u ^ bit;           // face neighbour inside the cell
                        if (open[idx[w]] != 0 && grp[w] < 0) { grp[w] = groups; stack.Push(w); }
                    }
                }
                groups++;
            }
            if (groups == 0) continue;
            cells++;
            if (groups < 2) continue;
            split++;
            if (light == null) continue;
            bool anyLit = false, anyDark = false;
            for (int g = 0; g < groups; g++)
            {
                bool lit = false;
                for (int k = 0; k < 8; k++) if (grp[k] == g && light[idx[k]] != 0) lit = true;
                if (lit) anyLit = true; else anyDark = true;
            }
            if (anyLit && anyDark) leak++;
        }
    }

    /// Memory of the oracle's current lit set.
    private Mem MemOfOracle()
    {
        var m = new Mem();
        var chunks = new HashSet<int3>();
        var scratch = new byte[512];
        foreach (var (b, d) in _light.LitBricks())
        {
            chunks.Add(b >> 4);
            int k = BrickClass(b, scratch);
            if (k == 1) m.air++;
            else { m.mixed++; SplitCells(scratch, d, ref m.cells, ref m.split, ref m.splitLeak); }
        }
        m.chunks = chunks.Count;
        return m;
    }

    /// Memory of the union of many torches' lit bricks (no per-voxel storage; for the 200-torch base).
    private Mem MemOfUnion(List<int3> torches, uint seed, out double floodMsTotal)
    {
        var lit = new HashSet<int3>();
        floodMsTotal = 0;
        int top = LightFloodReference.MaxChannel(seed);
        foreach (int3 t in torches)
        {
            _light.Flood(t, top, out int3 lo, out int3 dims);
            floodMsTotal += _light.LastFloodMs;
            _light.ForEachTouchedBrick(lo, dims, b => lit.Add(b));
        }
        var m = new Mem();
        var chunks = new HashSet<int3>();
        var scratch = new byte[512];
        foreach (int3 b in lit)
        {
            chunks.Add(b >> 4);
            int k = BrickClass(b, scratch);
            if (k == 1) m.air++;
            else if (k == 2) { m.mixed++; SplitCells(scratch, null, ref m.cells, ref m.split, ref m.splitLeak); }
        }
        m.chunks = chunks.Count;
        return m;
    }

    private static double Median(List<double> l) { l.Sort(); return l[l.Count / 2]; }

    private string TimeTorch(int3 t, uint seed, out Mem mem)
    {
        // first add (Burst compile warm), then 5 timed re-adds
        _light.AddTorch(t, seed);
        mem = MemOfOracle();
        var g = new List<double>(); var f = new List<double>(); var mg = new List<double>(); var rm = new List<double>();
        for (int k = 0; k < 3; k++)
        {
            var sw = Stopwatch.StartNew();
            _light.RemoveTorch(t);
            rm.Add(sw.Elapsed.TotalMilliseconds);
            _light.AddTorch(t, seed);
            g.Add(_light.LastGatherMs); f.Add(_light.LastFloodMs); mg.Add(_light.LastMergeMs);
        }
        return $"{_light.LastReached,9:N0} reached ({100.0 * mem.mixed * 512 / math.max(1, _light.LastReached):F0}% of that = voxels of lit mixed bricks) | flood job {Median(f),6:F1} ms | gather {Median(g),5:F1} | merge (managed) {Median(mg),6:F1} | " +
               $"incremental remove (managed) {Median(rm),7:F1} ms";
    }

    private static string MemRow(string name, Mem m, int torches) =>
        $"{name,-34} {m.air,8:N0} {m.mixed,7:N0} {m.chunks,6} | {m.MbPlan,7:F2} {m.MbCoarse,7:F2} {m.MbOracle,8:F2} | " +
        $"{(m.cells > 0 ? 100.0 * m.split / m.cells : 0),6:F2}% ({m.split:N0}/{m.cells:N0}) leak {m.splitLeak} | per torch {m.MbPlan / torches:F2} MB";

    [Test, Category("Slow"), Timeout(1800000)]
    public void Report_MemoryAndTimingAndSlices()
    {
        if (System.Environment.GetEnvironmentVariable("REPORTS") != "1") Assert.Ignore("report only: set REPORTS=1");
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Screenshots", "157");
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder();
        uint seed = LightFloodReference.SeedForMaterial(Materials.Torch);
        int top = LightFloodReference.MaxChannel(seed);
        sb.AppendLine("TASK 157 -- torch light flood oracle (TORCH_LIGHT_PLAN.md L1). REPORTS=1 EditMode, this Mac (M1 Air), Burst flood job.");
        sb.AppendLine($"reach 12 m = {top} units (chamfer 4/6/7 at {LightFloodReference.UNITS_PER_VOXEL:F4} units per 0.1 m), torch seed {LightFloodReference.Str(seed)} (material Torch)");
        sb.AppendLine($"Burst enabled: {Unity.Burst.BurstCompiler.IsEnabled}; jobs compiled with safety checks OFF (as in a player build).");
        sb.AppendLine("ms are a CPU microbenchmark in the Editor (cold-ish chip, one run, median of 3 re-adds): magnitude only, NOT a frame-time claim.");
        sb.AppendLine();
        sb.AppendLine("MEMORY (per side). plan = 0.1 m mixed bricks 2 KB + air bricks 32 B (2^3 cells at 0.4 m) + 16 KB slot table per lit chunk;");
        sb.AppendLine("  0.2m = mixed bricks at 0.2 m (256 B) + 32 B per split cell (its 8 voxels at 0.1 m) + the same air / tables;");
        sb.AppendLine("  oracle = this CPU oracle's exact per-voxel storage (2 KB per lit brick).");
        sb.AppendLine("  split = 0.2 m cells (with air) of lit mixed bricks whose air voxels form >= 2 face-connected groups inside the cell;");
        sb.AppendLine("  leak = split cells with one group lit and another dark (what a single 0.2 m value would get wrong).");
        sb.AppendLine($"{"scene",-34} {"air br",8} {"mixed",7} {"chunks",6} | {"MB plan",7} {"MB 0.2m",7} {"MB oracle",8} | split cells at 0.2 m");

        var timing = new StringBuilder();

        // ---- 138's tunnel and sealed room ----
        BuildSite138();
        Put(TunnelTorch, Materials.Torch);
        timing.AppendLine($"tunnel 138 (10.2 m in)   {TimeTorch(TunnelTorch, seed, out Mem mTunnel)}");
        sb.AppendLine(MemRow("tunnel 138, torch 10.2 m in", mTunnel, 1));
        Slice("slice157_tunnel138", TunnelTorch.y, new int3(180, 0, 200), new int3(420, 0, 320), 3, seed, dir);
        _light.RemoveTorch(TunnelTorch);

        Put(RoomTorch, Materials.Torch);
        timing.AppendLine($"sealed room 138 (corner) {TimeTorch(RoomTorch, seed, out Mem mRoom)}");
        sb.AppendLine(MemRow("sealed room 138, torch in a corner", mRoom, 1));
        Slice("slice157_sealedroom", RoomTorch.y, new int3(225, 0, 225), new int3(287, 0, 287), 8, seed, dir);
        _light.RemoveTorch(RoomTorch);

        // ---- L-bend and the diagonal 0.1 m wall (slices) ----
        BuildLBend(new int3(20, 30, 24));
        sb.AppendLine(MemRow("L-bend corridor 1 x 2 m", MemOfOracle(), 1));
        Slice("slice157_lbend", 30, new int3(10, 0, 10), new int3(100, 0, 120), 6, White12, dir);

        BuildRoom();
        foreach (int3 v in Cells(RoomLo, RoomHi)) if (v.x == v.z) Put(v, Materials.Stone);
        PlaceTorch(new int3(60, 30, 30), White12);
        sb.AppendLine(MemRow("6 m room split by a diagonal 0.1 m wall", MemOfOracle(), 1));
        Slice("slice157_diagonalwall", 30, new int3(15, 0, 15), new int3(84, 0, 84), 6, White12, dir);

        // ---- outdoor: one torch on a post; 32 and 200 torches over open ground ----
        BuildGround(new int3(4, 3, 4));
        int3 ot = PostTorch(256, 256);
        timing.AppendLine($"outdoor, post 1.5 m      {TimeTorch(ot, seed, out Mem mOut)}");
        sb.AppendLine(MemRow("outdoor torch on a 1.5 m post", mOut, 1));
        Slice("slice157_outdoor", 204, new int3(116, 0, 116), new int3(396, 0, 396), 2, seed, dir);
        _light.RemoveTorch(ot);

        var grid32 = new List<int3>();
        for (int i = 0; i < 8; i++) for (int j = 0; j < 4; j++) grid32.Add(PostTorch(160 + i * 28, 200 + j * 37));   // ~20 x 11 m
        Mem m32 = MemOfUnion(grid32, seed, out double ms32);
        sb.AppendLine(MemRow("32 torches, open ground ~20 x 11 m", m32, 32));

        BuildGround(new int3(9, 3, 9));
        var grid200 = new List<int3>();
        for (int i = 0; i < 20; i++) for (int j = 0; j < 10; j++) grid200.Add(PostTorch(176 + i * 42, 176 + j * 89));   // 80 x 80 m
        Mem m200 = MemOfUnion(grid200, seed, out double ms200);
        sb.AppendLine(MemRow("200 torches, open ground 80 x 80 m", m200, 200));
        sb.AppendLine();
        sb.AppendLine($"200-torch base: plan storage {m200.MbPlan:F1} MB vs the 48 MB cap per side -> {(m200.MbPlan > 48 ? "OVER" : "under")}; " +
                      $"0.2 m + escape {m200.MbCoarse:F1} MB.");
        sb.AppendLine();
        sb.AppendLine("WORKER TIME PER FLOOD (one torch; the flood job is the Burst core L3 runs on a worker):");
        sb.Append(timing);
        sb.AppendLine($"32 torches: flood jobs {ms32:F0} ms total ({ms32 / 32:F1} per torch); 200 torches: {ms200:F0} ms total ({ms200 / 200:F1} per torch)");

        File.WriteAllText(Path.Combine(dir, "torchflood157.txt"), sb.ToString());
        TestContext.WriteLine(sb.ToString());
    }

    /// Top-down slice at height y: x right, z down. Solid grey, unlit air black, light =
    /// the fraction of the reach left per channel (level / seed max), dark contour every 1 m
    /// of flood distance, torches white.
    private void Slice(string name, int y, int3 lo, int3 hi, int scale, uint seed, string dir)
    {
        int top = LightFloodReference.MaxChannel(seed);
        int w = hi.x - lo.x + 1, h = hi.z - lo.z + 1;
        double unitsPerM = LightFloodReference.UNITS_PER_VOXEL * 10.0;
        var px = new UnityEngine.Color32[w * scale * h * scale];
        int Band(uint l) => l == 0 ? -1 : (int)math.floor((top - LightFloodReference.MaxChannel(l)) / unitsPerM);
        for (int z = 0; z < h; z++)
        for (int x = 0; x < w; x++)
        {
            var v = new int3(lo.x + x, y, lo.z + z);
            UnityEngine.Color32 c;
            if (_light.Torches.ContainsKey(v)) c = new UnityEngine.Color32(255, 255, 255, 255);
            else if (!_light.Open(v)) c = new UnityEngine.Color32(70, 70, 82, 255);
            else
            {
                uint l = _light.Get(v);
                float f(int lv) => math.saturate(lv / (float)top);
                float r = f(LightFloodReference.R(l)), g = f(LightFloodReference.G(l)), b = f(LightFloodReference.B(l));
                int band = Band(l);
                bool contour = band >= 0 && ((x + 1 < w && Band(_light.Get(v + new int3(1, 0, 0))) != band && _light.Open(v + new int3(1, 0, 0))) ||
                                             (z + 1 < h && Band(_light.Get(v + new int3(0, 0, 1))) != band && _light.Open(v + new int3(0, 0, 1))));
                float k = contour ? 0.55f : 1f;
                c = new UnityEngine.Color32((byte)(255 * r * k), (byte)(255 * g * k), (byte)(255 * b * k), 255);
            }
            for (int sz = 0; sz < scale; sz++)
            for (int sx = 0; sx < scale; sx++)
            {
                int row = (h - 1 - z) * scale + (scale - 1 - sz);     // z grows downward in the picture
                px[row * w * scale + x * scale + sx] = c;
            }
        }
        // flip so +z is DOWN (Texture2D row 0 is the bottom)
        var tex = new UnityEngine.Texture2D(w * scale, h * scale, UnityEngine.TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), UnityEngine.ImageConversion.EncodeToPNG(tex));
        UnityEngine.Object.DestroyImmediate(tex);
    }
}
