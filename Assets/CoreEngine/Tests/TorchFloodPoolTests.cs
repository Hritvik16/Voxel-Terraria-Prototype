// ==========================================
// TorchFloodPoolTests.cs -- task 158 (TORCH_LIGHT_PLAN.md step L2)
//
// The GPU light pool's CPU side: TorchFloodPool.Encode (task 157's oracle ->
// 0.2 m mixed cells + per-voxel escapes + 0.4 m air bricks) and SampleLevels,
// the C# twin of TorchFlood.hlsl's FloodLevels (the player's GPU probe checks
// GPU == this twin; these tests check the twin against the oracle):
//   - sealed walls stay EXACTLY dark through the encoding: axis wall, the
//     edge-touching diagonal (escapes), the 3D diagonal plane, a closed box
//   - mixed cells: never darker than the oracle, at most one cell's path brighter
//   - air bricks (open ground): within AIR_TOL levels of the oracle
//   - no torch -> nothing stored; the split-cell table
//   - Raymarch.compute: every flood line is under #ifdef VE_TORCH_FLOOD, so the
//     OFF kernels compile from the pre-task source
// Shapes are built through the edit-op API, as in LightFloodReferenceTests.
// ==========================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public class TorchFloodPoolTests
{
    /// Mixed 0.2 m cell = the max over its connected air: a voxel is at most the
    /// longest in-cell path (7 face moves) below it.
    private const int MIXED_TOL = 7 * LightFloodReference.W_FACE;
    /// Air brick: trilinear between 0.4 m cells (task 165: each the MAX of its 2^3 centre voxels; measured max in the open-air case + margin).
    private const float AIR_TOL = 24f;

    private BrickDataPool _pool;
    private ChunkStore _store;
    private EditOps _ops;
    private LightFloodReference _light;

    [TearDown]
    public void Teardown()
    {
        _light?.Dispose(); _light = null;
        _pool?.Dispose(); _pool = null;
    }

    private void NewWorld(int3 chunks, byte fill) => NewWorld(chunks, c => fill);

    private void NewWorld(int3 chunks, Func<int3, byte> fillFor)
    {
        _light?.Dispose();
        _pool?.Dispose();
        _pool = new BrickDataPool(80000);
        _store = new ChunkStore(_pool, new ChunkHandleAllocator(512));
        for (int cz = 0; cz < chunks.z; cz++)
        for (int cy = 0; cy < chunks.y; cy++)
        for (int cx = 0; cx < chunks.x; cx++)
            _store.InsertChunk(new Chunk { coord = new int3(cx, cy, cz), isUniform = true, uniformMaterial = fillFor(new int3(cx, cy, cz)) });
        var edits = new EditService();
        edits.AttachWorld(_store, _store, _store, null);
        _ops = new EditOps(EditOpLog.InMemory(158u), edits, _store);
        _light = new LightFloodReference(_store);
    }

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

    private static readonly int3 RoomLo = new int3(20, 20, 20), RoomHi = new int3(79, 49, 79);

    private void Room(Func<int3, bool> wall, int3 torch)
    {
        NewWorld(new int3(1, 1, 1), Materials.Stone);
        Fill(RoomLo, RoomHi, 0);
        for (int z = RoomLo.z; z <= RoomHi.z; z++)
        for (int y = RoomLo.y; y <= RoomHi.y; y++)
        for (int x = RoomLo.x; x <= RoomHi.x; x++)
            if (wall(new int3(x, y, z))) Put(new int3(x, y, z), Materials.Stone);
        Put(torch, Materials.Torch);
        _light.AddTorch(torch, LightFloodReference.SeedForMaterial(Materials.Torch));
    }

    private struct Stats { public int mixed, air, airLit; public float maxAirErr, meanAirErr; public int maxMixedOver; }

    /// Every open voxel of the box: the encoded sample vs the oracle.
    private Stats Compare(TorchFloodPool.Encoded e, int3 lo, int3 hi, Func<int3, bool> mustBeDark, string what)
    {
        var st = new Stats();
        double airSum = 0;
        for (int z = lo.z; z <= hi.z; z++)
        for (int y = lo.y; y <= hi.y; y++)
        for (int x = lo.x; x <= hi.x; x++)
        {
            var v = new int3(x, y, z);
            if (!_light.Open(v)) continue;
            uint o = _light.Get(v);
            float3 ov = new float3(LightFloodReference.R(o), LightFloodReference.G(o), LightFloodReference.B(o));
            float3 s = TorchFloodPool.SampleLevels(e, v);
            if (mustBeDark != null && mustBeDark(v) && math.any(s != 0f))
                Assert.Fail($"{what}: sealed-side voxel {v} samples {s} (oracle {LightFloodReference.Str(o)})");
            bool airBrick = IsAirBrick(e, v);
            if (airBrick)
            {
                st.air++;
                float err = math.cmax(math.abs(s - ov));
                if (o != 0) { st.airLit++; airSum += err; }
                st.maxAirErr = math.max(st.maxAirErr, err);
            }
            else
            {
                st.mixed++;
                // nearest cell, value = max over the cell's connected air: never darker, at most MIXED_TOL brighter
                if (math.any(s < ov)) Assert.Fail($"{what}: mixed-cell voxel {v} samples {s} < oracle {ov}");
                int over = (int)math.cmax(s - ov);
                if (over > MIXED_TOL) Assert.Fail($"{what}: mixed-cell voxel {v} samples {s}, oracle {ov} (> {MIXED_TOL} over)");
                st.maxMixedOver = math.max(st.maxMixedOver, over);
            }
        }
        st.meanAirErr = st.airLit > 0 ? (float)(airSum / st.airLit) : 0f;
        Assert.LessOrEqual(st.maxAirErr, AIR_TOL, $"{what}: air-brick error");
        UnityEngine.Debug.Log($"[158] {what}: chunks {e.chunks} air bricks {e.airBricks} mixed {e.mixedBricks} escapes {e.escapes} " +
                              $"GPU {e.Bytes / 1048576.0:F2} MB | voxels mixed {st.mixed} (max over {st.maxMixedOver}) air {st.air} " +
                              $"(max err {st.maxAirErr:F1}, mean lit {st.meanAirErr:F2})");
        return st;
    }

    private static bool IsAirBrick(TorchFloodPool.Encoded e, int3 v)
    {
        if (!e.Any) return false;
        int3 c = (v >> 7) - e.origin;
        if (math.any(c < 0) || math.any(c >= e.dims)) return false;
        uint t = e.dir[(c.z * e.dims.y + c.y) * e.dims.x + c.x];
        if (t == 0) return false;
        int3 b = (v >> 3) & 15;
        uint en = e.tables[(t - 1) * 4096 + (uint)((b.z << 8) | (b.y << 4) | b.x)];
        return (en >> 30) == TorchFloodPool.KIND_AIR;
    }

    [Test]
    public void Groups_SplitCellTable()
    {
        // through Encode: a lone diagonal voxel pair is reported as split
        var g = typeof(TorchFloodPool).GetField("Groups", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var t = (byte[])g.GetValue(null);
        Assert.AreEqual(0, t[0]);
        Assert.AreEqual(1, t[255]);
        Assert.AreEqual(1, t[0b00000011], "x neighbours");
        Assert.AreEqual(2, t[0b00000110], "(1,0,0) and (0,1,0): edge-touching only");
        Assert.AreEqual(2, t[0b10000001], "opposite corners");
        Assert.AreEqual(1, t[0b00001011], "L of three");
    }

    [Test]
    public void NoTorch_NothingStored()
    {
        NewWorld(new int3(1, 1, 1), Materials.Stone);
        Fill(RoomLo, RoomHi, 0);
        var e = TorchFloodPool.Encode(_light, _store);
        Assert.IsFalse(e.Any);
        Assert.AreEqual(0f, math.cmax(TorchFloodPool.SampleLevels(e, new int3(40, 30, 40))));
    }

    [Test]
    public void Room_SampleMatchesOracle()
    {
        Room(v => false, new int3(25, 35, 25));
        var e = TorchFloodPool.Encode(_light, _store);
        var st = Compare(e, RoomLo, RoomHi, null, "room");
        Assert.Greater(st.mixed, 1000);
        Assert.AreEqual(0, e.escapes, "an empty room splits no 0.2 m cell");
    }

    [Test]
    public void Sealed_AxisWall_StaysDark()
    {
        Room(v => v.x == 50, new int3(30, 30, 50));
        var e = TorchFloodPool.Encode(_light, _store);
        Compare(e, RoomLo, RoomHi, v => v.x > 50, "axis wall x = 50");
        Assert.AreEqual(0, e.escapes, "an axis wall never splits a cell");
    }

    [Test]
    public void Sealed_EdgeTouchingDiagonal_EscapesKeepFarSideDark()
    {
        Room(v => v.x == v.z, new int3(60, 30, 30));
        var e = TorchFloodPool.Encode(_light, _store);
        Compare(e, RoomLo, RoomHi, v => v.x < v.z, "diagonal x == z");
        Assert.Greater(e.escapes, 100, "the diagonal splits 0.2 m cells");
    }

    [Test]
    public void Sealed_DiagonalPlane3D_StaysDark()
    {
        Room(v => v.x + v.y + v.z == 150, new int3(30, 30, 30));
        var e = TorchFloodPool.Encode(_light, _store);
        Compare(e, RoomLo, RoomHi, v => v.x + v.y + v.z > 150, "plane x + y + z = 150");
    }

    [Test]
    public void Sealed_ClosedBox_StaysDark()
    {
        int3 t = new int3(50, 35, 50);
        Room(v => math.cmax(math.abs(v - t)) == 5, t);
        var e = TorchFloodPool.Encode(_light, _store);
        Compare(e, RoomLo, RoomHi, v => math.cmax(math.abs(v - t)) > 5, "0.1 m box round the torch");
    }

    [Test]
    public void OpenGround_AirBricksWithinTolerance()
    {
        // 3 x 3 x 3 chunks: stone below y 128 (a chunk and brick boundary, so the air above the
        // floor sits in AIR bricks: the trilinear case), air above; a torch on a 1.5 m post
        NewWorld(new int3(3, 3, 3), c => c.y == 0 ? Materials.Stone : (byte)0);
        var t = new int3(192, 143, 192);
        for (int y = 128; y < 143; y++) Put(new int3(192, y, 192), Materials.Stone);
        Put(t, Materials.Torch);
        _light.AddTorch(t, LightFloodReference.SeedForMaterial(Materials.Torch));
        var e = TorchFloodPool.Encode(_light, _store);
        var st = Compare(e, new int3(64, 128, 64), new int3(320, 170, 320), null, "open ground, torch on a post");
        Assert.Greater(e.airBricks, 1000);
        Assert.Greater(st.airLit, 100000);
    }

    // ---- task 164: the direction terms (-tfdir), through TorchFloodPool.LightTwin ----

    private static readonly int3[] Normals =
        { new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 1, 0), new int3(0, -1, 0), new int3(0, 0, 1), new int3(0, 0, -1) };

    /// The default level -> light map (TorchFloodPool.Params / Params2) with direction term `mode`.
    private static float4 MapP => new float4(2.0f, 0.10f, LightFloodReference.DEFAULT_REACH_M, (float)(LightFloodReference.UNITS_PER_VOXEL * 10.0));
    private static float4 MapP2(int mode) =>
        new float4(LightFloodReference.ReachLevel(LightFloodReference.DEFAULT_REACH_M), LightFloodReference.STOPS_AT_REACH, 0.3f, mode);

    [Test]
    public void Direction_EveryMode_SealedBoxStaysExactlyDark()
    {
        int3 t = new int3(50, 35, 50);
        Room(v => math.cmax(math.abs(v - t)) == 5, t);
        var e = TorchFloodPool.Encode(_light, _store);
        float4 p = MapP;
        var torches = new List<float3> { ((float3)t + 0.5f) * 0.1f };
        int lit = 0;
        for (int mode = 0; mode <= 3; mode++)
        {
            float4 p2 = MapP2(mode);
            for (int z = RoomLo.z; z <= RoomHi.z; z++)
            for (int y = RoomLo.y; y <= RoomHi.y; y++)
            for (int x = RoomLo.x; x <= RoomHi.x; x++)
            {
                var v = new int3(x, y, z);
                if (!_light.Open(v)) continue;
                bool outside = math.cmax(math.abs(v - t)) > 5;
                foreach (var n in Normals)
                {
                    float3 l = TorchFloodPool.LightTwin(e, v, n, p, p2, torches);
                    if (outside && math.any(l != 0f)) Assert.Fail($"mode {mode}: sealed voxel {v} n {n} gets {l}");
                    if (!outside && math.any(l > 0f)) lit++;
                }
            }
        }
        Assert.Greater(lit, 0, "the box's inside is lit");
    }

    /// The facet metric: along a floor line beside a torch on a post, the direction factor
    /// dir = light(n = +y) / light(n = 0) between neighbouring voxels. 158's term jumps
    /// (1 -> 0.5 -> 0.25 ndl at the chamfer's region borders); mode 2 follows the torch's
    /// Lambert cosine, which moves < 0.03 per 0.1 m at a 1.5 m post; flat never moves.
    [Test]
    public void Direction_FloorBesideTorch_FacetSteps()
    {
        NewWorld(new int3(3, 3, 3), c => c.y == 0 ? Materials.Stone : (byte)0);
        var t = new int3(192, 143, 192);
        for (int y = 128; y < 143; y++) Put(new int3(192, y, 192), Materials.Stone);
        Put(t, Materials.Torch);
        _light.AddTorch(t, LightFloodReference.SeedForMaterial(Materials.Torch));
        var e = TorchFloodPool.Encode(_light, _store);
        float4 p = MapP;
        var torches = new List<float3> { ((float3)t + 0.5f) * 0.1f };
        var maxStep = new float[4];
        var report = new System.Text.StringBuilder();
        foreach (int zOff in new[] { 0, 7, 23 })
        for (int mode = 0; mode <= 3; mode++)
        {
            float4 p2 = MapP2(mode);
            float prev = -1f;
            for (int x = 195; x <= 192 + 80; x++)
            {
                var v = new int3(x, 128, 192 + zOff);
                float flat = math.cmax(TorchFloodPool.LightTwin(e, v, int3.zero, p, p2, torches));
                if (flat <= 1e-4f) { prev = -1f; continue; }
                float dir = math.cmax(TorchFloodPool.LightTwin(e, v, new int3(0, 1, 0), p, p2, torches)) / flat;
                if (prev >= 0f) maxStep[mode] = math.max(maxStep[mode], math.abs(dir - prev));
                prev = dir;
            }
        }
        for (int mode = 0; mode <= 3; mode++) report.Append($" mode {mode} max step {maxStep[mode]:F3};");
        UnityEngine.Debug.Log($"[164] floor beside a 1.5 m post torch, dir factor between 0.1 m neighbours:{report}");
        Assert.Greater(maxStep[0], 0.2f, "158's term has its facet steps here (the case this test guards)");
        Assert.Less(maxStep[2], 0.05f, "mode 2 is smooth along the floor");
        Assert.AreEqual(0f, maxStep[3], 1e-5f, "flat has no direction");
    }

    [Test]
    public void Raymarch_FloodCodeOnlyUnderIfdef()
    {
        string src = File.ReadAllText("Assets/CoreEngine/Rendering/Raymarch.compute");
        var outside = new System.Text.StringBuilder();
        int skip = 0;
        foreach (string line in src.Split('\n'))
        {
            string l = line.Trim();
            if (skip > 0)
            {
                if (l.StartsWith("#if")) skip++;
                else if (l.StartsWith("#endif")) skip--;
                continue;
            }
            if (l == "#ifdef VE_TORCH_FLOOD") { skip = 1; continue; }
            if (l.StartsWith("#pragma kernel") || l.StartsWith("//")) continue;
            outside.AppendLine(l);
        }
        var m = Regex.Match(outside.ToString(), @"Flood\w*\(|_Flood\w+|TorchFlood");
        Assert.IsFalse(m.Success, $"flood code outside #ifdef VE_TORCH_FLOOD: '{m.Value}'");
        StringAssert.Contains("#pragma kernel CSMainFlood VE_TORCH_FLOOD", src);
        StringAssert.Contains("#pragma kernel CSMainFarShadeFlood VE_FAR_SHADE VE_TORCH_FLOOD", src);
    }
}
