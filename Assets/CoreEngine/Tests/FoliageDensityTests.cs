// Assets/CoreEngine/Tests/FoliageDensityTests.cs
//
// Task 245 (lever F4): the foliage light early-out's per-brick LEAF DENSITY side buffer (FoliageDensity.cs) and the
// shader's use of it (Raymarch.compute, header at FoliageDens).
//   LeafSet_IsTheGeneratorsLeaves          the engine ids counted (183-186, 216) = GenToEngine of GenMat's leaves, no other
//   Tier0_Oracle_BruteForce                WriteTier0's bytes vs a brute-force count of the same bodies, at run offsets,
//                                          adversarial bodies: empty, one leaf voxel, a full leaf brick, a one-voxel-thick leaf
//                                          sheet (64), trunk next to leaves (wood never counts), the neighbours of every leaf id
//   Shell_Oracle_StagedChunk               a chunk's shell bodies staged by ShellBrickPack (full + compact, the real order),
//                                          WriteShell's byte per brick vs a brute-force count of the brick's voxels (and the
//                                          shader's index rule: global body = base + (offset >> 7), sub = (offset & 127) / 17)
//   Shader_Shape                           far-shading kernels only (one Metal slot), every TraceShadowRay call names its
//                                          foliage flag, the ground-bounce leg never takes it; switches default OFF
//   Report_Calibration (REPORTS=1, Slow)   the transmittance floor from the generated forest (seed 42, forest_eye's chunks):
//                                          for each candidate floor, how often a ray the early-out stops would have reached
//                                          the sun -> Logs/TerrainGen/trees245/calibration.txt
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using TerrainGen;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.WorldGen;

public class FoliageDensityTests
{
    const string Shader = "Assets/CoreEngine/Rendering/Raymarch.compute";

    private static int Brute(byte[] body)
    {
        int c = 0;
        foreach (byte m in body) if (FoliageDensity.IsLeaf(m)) c++;
        return c;
    }

    [Test]
    public void LeafSet_IsTheGeneratorsLeaves()
    {
        var want = new bool[256];
        for (int g = 1; g < 256; g++)
            if (GenMat.IsTreeLeaf((byte)g) || g == GenMat.Leaves) want[VoxelEngine.Rendering.FarField.GenToEngine((byte)g)] = true;
        for (int g = 1; g < 256; g++)
        {
            byte e = VoxelEngine.Rendering.FarField.GenToEngine((byte)g);
            if (FoliageDensity.IsLeaf(e)) Assert.IsTrue(want[e], $"gen {g} -> engine {e} counts as leaf but is not a leaf material");
        }
        for (int e = 0; e < 256; e++) Assert.AreEqual(want[e], FoliageDensity.IsLeaf((uint)e), $"engine material {e}");
        Assert.AreEqual(0, FoliageDensity.Encode(0));
        Assert.AreEqual(1, FoliageDensity.Encode(1), "one leaf voxel must count");
        Assert.AreEqual(255, FoliageDensity.Encode(512));
        Assert.AreEqual(32, FoliageDensity.Encode(64));
    }

    private static byte[] Body(System.Func<int, int, int, byte> f)
    {
        var b = new byte[512];
        for (int z = 0; z < 8; z++) for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) b[(z << 6) | (y << 3) | x] = f(x, y, z);
        return b;
    }

    [Test]
    public void Tier0_Oracle_BruteForce()
    {
        const byte OAK = 184, WOOD = 120, STONE = 3;
        var bodies = new List<byte[]>
        {
            Body((x, y, z) => 0),                                              // air (an all-air dense body is impossible, harmless)
            Body((x, y, z) => x == 3 && y == 4 && z == 5 ? OAK : (byte)0),     // sparse: one leaf voxel
            Body((x, y, z) => OAK),                                            // full leaf brick
            Body((x, y, z) => y == 2 ? (byte)183 : (byte)0),                   // one material, a thin (1 voxel) leaf sheet: 64
            Body((x, y, z) => x < 3 ? WOOD : (x == 3 ? OAK : (byte)0)),        // trunk next to leaves: only the 64 leaves
            Body((x, y, z) => (byte)(((x + 8 * y + 64 * z) % 9) switch { 0 => 182, 1 => 183, 2 => 186, 3 => 187, 4 => 215, 5 => 216, 6 => 217, 7 => STONE, _ => 0 })),
        };
        var rng = new Unity.Mathematics.Random(245u);
        for (int i = 0; i < 40; i++)
        {
            float p = rng.NextFloat();
            bodies.Add(Body((x, y, z) => rng.NextFloat() < p ? (byte)(rng.NextBool() ? 185 : rng.NextInt(1, 256)) : (byte)0));
        }
        bool keep = FoliageDensity.Enabled;
        FoliageDensity.Enabled = true;
        try
        {
            FoliageDensity.Release();
            const int CAP = 200;
            FoliageDensity.RegisterTier0(CAP);
            Assert.AreEqual(CAP, FoliageDensity.Tier0Bodies);
            // pack every body into a CPU "pool", write them in runs of 1..7 bodies at shifted GPU slots (src != dst)
            var src = new NativeArray<uint>(bodies.Count * 128 + 128, Allocator.TempJob);
            for (int i = 0; i < bodies.Count; i++)
                for (int v = 0; v < 512; v++) src[(i + 1) * 128 + (v >> 2)] |= (uint)bodies[i][v] << ((v & 3) * 8);
            var dstOf = new int[bodies.Count];
            int at = 0, slot = 37;
            while (at < bodies.Count)
            {
                int n = math.min(1 + (at % 7), bodies.Count - at);
                for (int j = 0; j < n; j++) dstOf[at + j] = slot + j;
                FoliageDensity.WriteTier0(src, (at + 1) * 128, slot * 128, n * 128);
                at += n; slot += n + (at % 3);
            }
            for (int i = 0; i < bodies.Count; i++)
                Assert.AreEqual(FoliageDensity.Encode(Brute(bodies[i])), FoliageDensity.Tier0Byte(dstOf[i]), $"body {i} at slot {dstOf[i]}");
            Assert.AreEqual(1, FoliageDensity.Tier0Byte(dstOf[1]));
            Assert.AreEqual(255, FoliageDensity.Tier0Byte(dstOf[2]));
            Assert.AreEqual(32, FoliageDensity.Tier0Byte(dstOf[3]));
            Assert.AreEqual(32, FoliageDensity.Tier0Byte(dstOf[4]), "wood beside the leaves must not count");
            Assert.AreEqual(0, FoliageDensity.Tier0Byte(0), "an unwritten slot stays 0");
            // a write past the registered bodies is cut, never out of range
            FoliageDensity.WriteTier0(src, 0, (CAP - 1) * 128, 3 * 128);
            src.Dispose();
        }
        finally { FoliageDensity.Release(); FoliageDensity.Enabled = keep; }
    }

    [Test]
    public void Shell_Oracle_StagedChunk()
    {
        // A chunk's voxels (TerrainGenChunkSource.Index order) with dense bricks of every kind; staged as ShellStore does.
        var mats = new NativeArray<byte>(128 * 128 * 128, Allocator.TempJob);
        var uni = new NativeArray<bool>(4096, Allocator.TempJob);
        var rng = new Unity.Mathematics.Random(2450u);
        var kind = new int[4096];
        for (int bi = 0; bi < 4096; bi++)
        {
            int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
            int k = (bi * 7 + 3) % 11;   // 0..2 uniform, else a dense kind
            kind[bi] = k;
            float p = rng.NextFloat();
            for (int z = 0; z < 8; z++) for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                byte m;
                switch (k)
                {
                    case 0: m = 0; break;
                    case 1: m = 3; break;
                    case 2: m = 184; break;
                    case 3: m = rng.NextFloat() < p ? (byte)184 : (byte)0; break;              // compact leaf + air
                    case 4: m = y == 5 ? (byte)216 : (byte)0; break;                             // compact thin sheet
                    case 5: m = x < 4 ? (byte)120 : (x == 4 ? (byte)183 : (byte)0); break;        // full: wood + leaf + air
                    case 6: m = rng.NextFloat() < p ? (byte)120 : (byte)186; break;              // compact wood + leaf (no air)
                    case 7: m = (x + y + z) == 3 ? (byte)185 : (byte)0; break;                   // compact sparse
                    case 8: m = rng.NextFloat() < p ? (byte)rng.NextInt(1, 256) : (byte)0; break; // full random
                    case 9: m = rng.NextFloat() < p ? (byte)0 : (byte)185; break;                 // compact, leaf is value a or b
                    default: m = x == 0 && y == 0 && z == 0 ? (byte)3 : (byte)184; break;        // compact, a = stone
                }
                mats[TerrainGenChunkSource.Index(bx * 8 + x, by * 8 + y, bz * 8 + z)] = m;
            }
            uni[bi] = k <= 2;
        }
        var pair = new NativeArray<uint>(4096, Allocator.TempJob);
        var counts = new NativeArray<int>(2, Allocator.TempJob);
        new ShellBrickPack.ClassifyJob { mats = mats, brickUniform = uni, pair = pair, counts = counts }.Run();
        int nFull = counts[0], nCompact = counts[1];
        Assert.Greater(nFull, 100); Assert.Greater(nCompact, 100);
        var bricks = new NativeArray<int>(4096, Allocator.TempJob);
        int n = 0;
        for (int bi = 0; bi < 4096; bi++) if (!uni[bi] && pair[bi] == ShellBrickPack.NOT_PAIR) bricks[n++] = bi;
        for (int bi = 0; bi < 4096; bi++) if (!uni[bi] && pair[bi] != ShellBrickPack.NOT_PAIR) bricks[n++] = bi;
        int bodies = ShellBrickPack.Bodies(nFull, nCompact);
        var staging = new NativeArray<uint>(bodies * 128, Allocator.TempJob);
        new ShellBrickPack.StageJob { mats = mats, bricks = bricks, pair = pair, nFull = nFull, nCompact = nCompact, staging = staging }.Run();

        bool keep = FoliageDensity.Enabled;
        FoliageDensity.Enabled = true;
        try
        {
            FoliageDensity.Release();
            FoliageDensity.RegisterTier0(10);
            const int BASE = 1000;   // the store's BrickBase in the shared pool
            FoliageDensity.RegisterShells(BASE + 2 * bodies + 10);
            // uploaded in runs (as ShellStore.Continue cuts them), local body i -> global BASE + 2 * i (non-consecutive slots: one per run)
            for (int i = 0; i < bodies; i++) FoliageDensity.WriteShell(staging, i, BASE + 2 * i, 1, nFull, nCompact);
            var host = staging.ToArray();
            int checkedB = 0;
            for (int j = 0; j < nFull + nCompact; j++)
            {
                int bi = bricks[j];
                int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
                int truth = 0;
                for (int z = 0; z < 8; z++) for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                    if (FoliageDensity.IsLeaf(mats[TerrainGenChunkSource.Index(bx * 8 + x, by * 8 + y, bz * 8 + z)])) truth++;
                // the page handle ShellStore writes, decoded as the shader's FoliageShellByte does
                uint h = j < nFull ? (uint)j : ShellBrickPack.COMPACT | (uint)((nFull + (j - nFull) / 7) * 128 + ((j - nFull) % 7) * 17);
                int decoded = 0;
                for (int v = 0; v < 512; v++) if (FoliageDensity.IsLeaf(ShellBrickPack.ShadowVoxel(host, h, v))) decoded++;
                Assert.AreEqual(truth, decoded, $"brick {bi}: ShellBrickPack round trip");
                uint o = h & 0x3FFFFFFFu;
                int localBody = (h & ShellBrickPack.COMPACT) != 0 ? (int)(o >> 7) : (int)o;
                int sub = (h & ShellBrickPack.COMPACT) != 0 ? (int)((o & 127u) / 17u) : 0;
                Assert.AreEqual(FoliageDensity.Encode(truth), FoliageDensity.ShellByte(BASE + 2 * localBody, sub), $"brick {bi} (kind {kind[bi]}, {(j < nFull ? "full" : "compact")})");
                checkedB++;
            }
            Assert.AreEqual(nFull + nCompact, checkedB);
            Assert.AreEqual(0, FoliageDensity.ShellByte(BASE + 1, 0), "a slot between runs stays 0");
            UnityEngine.Debug.Log($"[foliage] shell oracle: {nFull} full + {nCompact} compact bricks in {bodies} bodies; {FoliageDensity.Report()}");
        }
        finally
        {
            FoliageDensity.Release(); FoliageDensity.Enabled = keep;
            mats.Dispose(); uni.Dispose(); pair.Dispose(); counts.Dispose(); bricks.Dispose(); staging.Dispose();
        }
    }

    [Test]
    public void Shader_Shape()
    {
        string src = File.ReadAllText(Shader).Replace("\r\n", "\n");
        StringAssert.Contains("#ifdef VE_FAR_SHADE\n#define FOLIAGE_ON (_FoliageOut != 0)\n#else\n#define FOLIAGE_ON false\n#endif", src);
        int calls = 0;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(src, @"TraceShadowRay\(([^;]*?)\);"))
        {
            calls++;
            string args = m.Groups[1].Value;
            Assert.AreEqual(8, args.Split(',').Length - 1, $"TraceShadowRay call without the foliage flag: {args}");
        }
        Assert.AreEqual(3, calls, "the three TraceShadowRay calls (sun x 2, sky)");
        StringAssert.Contains("wdb, false, false, true, SUN_COARSE_NEVER, leg == legs - 1);", src);
        StringAssert.Contains("else if (shadow && FOLIAGE_ON && sh == k)", src);
        Assert.IsFalse(FoliageDensity.Enabled, "OFF unless -foliageout 1");
        Assert.IsFalse(RaymarchFeature.FoliageOut, "the shader switch is OFF by default");
        string feat = File.ReadAllText("Assets/CoreEngine/Rendering/RaymarchFeature.cs");
        foreach (string k in new[] { "data.kPrimary", "data.kShellPrepass", "data.kSunDefer", "kSky", "kSun", "data.kShellProbe" })
            StringAssert.Contains($"cmd.SetComputeBufferParam(data.compute, {k}, \"FoliageDens\", data.foliage);", feat, $"FoliageDens bound to {k}");
    }

    // ==================================================================
    // Calibration (report only): the transmittance floor from the generated forest, not from a pose.
    [Test, Category("Slow"), Timeout(3600000)]
    public void Report_Calibration()
    {
        if (System.Environment.GetEnvironmentVariable("REPORTS") != "1") Assert.Ignore("report only: set REPORTS=1");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int keepT = TerrainGenChunkSource.Trees2TestOverride;
        TerrainGenChunkSource.Trees2TestOverride = 1;   // 213's dense trees, the default (owner Q-AJ)
        TerrainGenChunkSource srcGen;
        try { srcGen = new TerrainGenChunkSource(42u); srcGen.SetTrees(true); }
        finally { TerrainGenChunkSource.Trees2TestOverride = keepT; }
        using var src = srcGen;
        // forest_eye (3366.4, 4950.0 m): 5 x 5 chunk columns (64 m) round it, chunk layers 40..43 (y 5120..5631: ground + crowns)
        int ccx = 33664 >> 7, ccz = 49500 >> 7, cy0 = 40, cy1 = 43;
        int X0 = (ccx - 2) << 7, Z0 = (ccz - 2) << 7, Y0 = cy0 << 7;
        const int W = 640;
        int H = (cy1 - cy0 + 1) << 7;
        var vol = new byte[W * H * W];
        var dense = new bool[(W >> 3) * (H >> 3) * (W >> 3)];
        var bodyByte = new byte[dense.Length];
        int Vi(int x, int y, int z) => (y * W + z) * W + x;
        int Bi(int x, int y, int z) => ((y >> 3) * (W >> 3) + (z >> 3)) * (W >> 3) + (x >> 3);
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK + 8);
        try
        {
            for (int cz = 0; cz < 5; cz++) for (int cyy = cy0; cyy <= cy1; cyy++) for (int cx = 0; cx < 5; cx++)
            {
                var chunk = new Chunk();
                pool.Dispose(); pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK + 8);
                src.Generate(new int3(ccx - 2 + cx, cyy, ccz - 2 + cz), chunk, new ChunkHandleAllocator(2), pool, null);
                var raw = pool.RawData;
                for (int bi = 0; bi < 4096; bi++)
                {
                    int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
                    uint d = chunk.isUniform ? chunk.uniformMaterial : chunk.bricks[bi].data;
                    bool dn = !chunk.isUniform && (d & 0x80000000u) != 0;
                    int body = (int)(d & 0x3FFFFFFFu) * 512, leaf = 0;
                    int ox = cx * 128 + bx * 8, oy = (cyy - cy0) * 128 + by * 8, oz = cz * 128 + bz * 8;
                    for (int z = 0; z < 8; z++) for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                    {
                        byte m = dn ? raw[body + ((z << 6) | (y << 3) | x)] : (byte)d;
                        vol[Vi(ox + x, oy + y, oz + z)] = m;
                        if (FoliageDensity.IsLeaf(m)) leaf++;
                    }
                    dense[Bi(ox, oy, oz)] = dn;
                    bodyByte[Bi(ox, oy, oz)] = dn ? FoliageDensity.Encode(leaf) : (byte)0;
                }
            }
        }
        finally { pool.Dispose(); }
        long genMs = sw.ElapsedMilliseconds;

        float[] eps = { 1f / 2, 1f / 4, 1f / 8, 1f / 16, 1f / 32, 1f / 64, 1f / 128 };
        int[] elev = { 16, 30, 50, 78 };
        bool[] isTree = new bool[256];
        for (int g = 1; g < 256; g++)
            if (GenMat.IsTreeLeaf((byte)g) || g == GenMat.Leaves || GenMat.IsTreeTrunk((byte)g) || g == GenMat.Bark)
                isTree[VoxelEngine.Rendering.FarField.GenToEngine((byte)g)] = true;
        var log = new StringBuilder();
        log.AppendLine("# task 245: foliage early-out calibration (seed 42, map v4, trees2On 1 = 213 dense, cores OFF; forest_eye's 5 x 5 chunk");
        log.AppendLine($"#   columns x [{X0}, {X0 + W - 1}] z [{Z0}, {Z0 + W - 1}], layers {cy0}..{cy1} = y [{Y0}, {Y0 + H - 1}]). CPU voxel DDA, the shader's rule:");
        log.AppendLine("#   every AIR cell of a DENSE brick multiplies T by (1 - byte / 256), byte = FoliageDensity.Encode(leaf voxels); T < eps = stop (blocked).");
        log.AppendLine("#   Receivers: 'floor' = the air cell over the first non-tree solid from the top (forest floor, under crowns too);");
        log.AppendLine("#   'surface' = the air cell over the first solid from the top (crown tops included). Inner 3 x 3 columns, random x z, a random");
        log.AppendLine("#   azimuth per ray. A ray leaving the box top or sides counts as LIT (side exits overstate the early-out's error: conservative).");
        log.AppendLine("#   cut = rays the early-out stops; wrong = cut rays that reach the sun exactly; P(lit|cut) = wrong / cut; lost = wrong / exactly lit;");
        log.AppendLine("#   dense-cell steps saved = cells in dense bricks the stopped rays no longer march / all dense-brick cells (the GPU's air leaps are not cut).");
        var rng = new Unity.Mathematics.Random(24501u);
        const int ORIGINS = 3000;
        var worst = new double[eps.Length];
        int[] thr = { 32, 64, 96, 128, 192 };   // rule B (the task's first option): a dense brick with byte >= thr (fill >= thr / 256) blocks on entry
        var worstB = new double[thr.Length];
        foreach (string cls in new[] { "floor", "surface" })
        {
            log.AppendLine();
            log.AppendLine($"## receivers: {cls}");
            log.AppendLine($"{"elev",4} {"rays",6} {"lit %",6} {"leafblk %",9} {"side %",6} {"dense/ray",9} | rule A (transmittance), per eps: P(lit|cut) %  lost %  dense-cell steps saved %  || rule B (brick fill >= X blocks on entry): the same three");
            foreach (int el in elev)
            {
                long rays = 0, lit = 0, leafBlk = 0, side = 0, denseCells = 0;
                var cut = new long[eps.Length]; var wrong = new long[eps.Length]; var saved = new long[eps.Length];
                var cutAt = new int[eps.Length];
                var cutB = new long[thr.Length]; var wrongB = new long[thr.Length]; var savedB = new long[thr.Length]; var cutAtB = new int[thr.Length];
                for (int o = 0; o < ORIGINS; o++)
                {
                    int x = 128 + rng.NextInt(0, 384), z = 128 + rng.NextInt(0, 384);
                    int y = H - 1;
                    while (y >= 0 && (vol[Vi(x, y, z)] == 0 || (cls == "floor" && isTree[vol[Vi(x, y, z)]]))) y--;
                    if (y < 0 || y + 1 >= H || vol[Vi(x, y + 1, z)] != 0) continue;
                    y++;
                    float az = rng.NextFloat(0f, 2f * math.PI), e = math.radians(el);
                    float3 dir = new float3(math.cos(e) * math.cos(az), math.sin(e), math.cos(e) * math.sin(az));
                    int3 v = new int3(x, y, z), st = (int3)math.sign(dir);
                    float3 td = math.abs(1f / dir), tm = td * 0.5f;
                    float T = 1f;
                    int dc = 0; int outcome = 0;   // 0 lit (top), 1 lit (side), 2 leaf, 3 other
                    for (int i = 0; i < eps.Length; i++) cutAt[i] = -1;
                    for (int i = 0; i < thr.Length; i++) cutAtB[i] = -1;
                    int lastB = -1;
                    for (int it = 0; it < 20000; it++)
                    {
                        if (v.y >= H) { outcome = 0; break; }
                        if (v.y < 0) { outcome = 3; break; }
                        if (v.x < 0 || v.z < 0 || v.x >= W || v.z >= W) { outcome = 1; break; }
                        byte m = vol[Vi(v.x, v.y, v.z)];
                        if (m != 0 && it > 0) { outcome = FoliageDensity.IsLeaf(m) ? 2 : 3; break; }
                        int b = Bi(v.x, v.y, v.z);
                        if (dense[b])
                        {
                            dc++;
                            if (b != lastB) { lastB = b; for (int i = 0; i < thr.Length; i++) if (cutAtB[i] < 0 && bodyByte[b] >= thr[i]) cutAtB[i] = dc; }
                            T *= 1f - bodyByte[b] / 256f;
                            for (int i = 0; i < eps.Length; i++) if (cutAt[i] < 0 && T < eps[i]) cutAt[i] = dc;
                        }
                        if (tm.x < tm.y) { if (tm.x < tm.z) { v.x += st.x; tm.x += td.x; } else { v.z += st.z; tm.z += td.z; } }
                        else { if (tm.y < tm.z) { v.y += st.y; tm.y += td.y; } else { v.z += st.z; tm.z += td.z; } }
                    }
                    rays++; denseCells += dc;
                    bool isLit = outcome <= 1;
                    if (isLit) lit++;
                    if (outcome == 1) side++;
                    if (outcome == 2) leafBlk++;
                    for (int i = 0; i < thr.Length; i++)
                    {
                        if (cutAtB[i] < 0) continue;
                        cutB[i]++; savedB[i] += dc - cutAtB[i] + 1;
                        if (isLit) wrongB[i]++;
                    }
                    for (int i = 0; i < eps.Length; i++)
                    {
                        if (cutAt[i] < 0) continue;
                        cut[i]++; saved[i] += dc - cutAt[i];
                        if (isLit) wrong[i]++;
                    }
                }
                var row = new StringBuilder();
                row.Append($"{el,4} {rays,6} {100.0 * lit / rays,6:F1} {100.0 * leafBlk / rays,9:F1} {100.0 * side / rays,6:F1} {(double)denseCells / rays,9:F1} |");
                for (int i = 0; i < eps.Length; i++)
                {
                    double p = cut[i] == 0 ? 0 : 100.0 * wrong[i] / cut[i];
                    worst[i] = math.max(worst[i], p);
                    row.Append($"  1/{(int)math.round(1f / eps[i])}: {p:F1} {100.0 * wrong[i] / math.max(1, lit):F2} {100.0 * saved[i] / math.max(1, denseCells):F1}");
                }
                row.Append("  || B fill>=:");
                for (int i = 0; i < thr.Length; i++)
                {
                    double p = cutB[i] == 0 ? 0 : 100.0 * wrongB[i] / cutB[i];
                    worstB[i] = math.max(worstB[i], p);
                    row.Append($"  {100 * thr[i] / 256}%: {p:F1} {100.0 * wrongB[i] / math.max(1, lit):F2} {100.0 * savedB[i] / math.max(1, denseCells):F1}");
                }
                log.AppendLine(row.ToString());
            }
        }
        // The rule (fixed before the numbers): the LARGEST floor (most cut) whose P(lit|cut) <= 5 % in every row above, i.e.
        // the early-out is wrong at most one time in 20 when it fires, on every receiver class and sun height.
        int pick = -1;
        for (int i = 0; i < eps.Length; i++) if (worst[i] <= 5.0) { pick = i; break; }
        log.AppendLine();
        log.AppendLine("# worst P(lit|cut) % per eps: " + string.Join("  ", System.Linq.Enumerable.Range(0, eps.Length).Select(i => $"1/{(int)math.round(1f / eps[i])}: {worst[i]:F1}")));
        log.AppendLine("# rule B worst P(lit|cut) % per fill threshold: " + string.Join("  ", System.Linq.Enumerable.Range(0, thr.Length).Select(i => $"{100 * thr[i] / 256}%: {worstB[i]:F1}")));
        log.AppendLine(pick >= 0 ? $"# PICK eps = 1/{(int)math.round(1f / eps[pick])} = {eps[pick]} (rule: largest eps with P(lit|cut) <= 5 % in every row)"
                                 : "# PICK none: no eps meets the rule");
        log.AppendLine($"# {sw.ElapsedMilliseconds / 1000} s (generation {genMs / 1000} s)");
        Directory.CreateDirectory("Logs/TerrainGen/trees245");
        File.WriteAllText("Logs/TerrainGen/trees245/calibration.txt", log.ToString());
        UnityEngine.Debug.Log(log.ToString());
    }
}
