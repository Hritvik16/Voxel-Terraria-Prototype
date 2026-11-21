// Assets/CoreEngine/Tests/FringeTests.cs
//
// Task 250 (WORLD_FILL_BUDGET sec. 5 E3): the voxel grass fringe (`-fringe 1`, TerrainSpec.fringeOn, DEFAULT OFF).
//   Synthetic_NoNewBricks_OnlyAirAboveHosts   a synthetic chunk scratch (no generator): grass planes ending inside a
//                                brick and on a brick's top layer, a steep staircase, a stone plane. Stubs only on grass
//                                with air above, 1-3 tall, only in dense bricks (the top-layer plane's stubs are REFUSED
//                                by the uniform air brick above), none on the staircase or stone; brick flags unchanged
//   World_FringeOff_ByteIdentical   seed 42, the surface chunk + the one above at forest_eye and savanna_eye, default
//                                spec (fringe OFF): FNV-1a hash of every voxel == the hash pinned from the code before
//                                task 250 (Logs/TerrainGen/fringe250/hash_off.txt)
//   World_FringeOn_OnlyAirBecomesFringe_NoNewBodies   the same chunks with fringe ON: the dense-brick table is identical
//                                (no new CPU brick, so no new GPU body), every changed voxel was AIR and is a fringe id,
//                                standing on its host or another fringe voxel; both sites get stubs
using System.IO;
using NUnit.Framework;
using TerrainGen;
using Unity.Collections;
using Unity.Mathematics;
using VoxelEngine;
using VoxelEngine.Memory;
using VoxelEngine.WorldGen;

public class FringeTests
{
    // FNV-1a of the 4 chunks below, fringe OFF, measured on the code BEFORE task 250 wired the pass in
    // (0 = not pinned yet: the test writes the hash and is Ignored). Pinned 2026-11-20 from the SUITE=all "before" run.
    private const ulong PINNED_OFF_HASH = 0x019EEF889FB4C206UL;

    private const ulong FNV_OFFSET = 14695981039346656037UL, FNV_PRIME = 1099511628211UL;

    private static byte E(byte g) => VoxelEngine.Rendering.FarField.GenToEngine(g);

    // ==================================================================
    [Test]
    public void Synthetic_NoNewBricks_OnlyAirAboveHosts()
    {
        const int N = 128;
        var toEngine = new NativeArray<byte>(256, Allocator.Temp);
        for (int i = 0; i < 256; i++) toEngine[i] = E((byte)i);
        var mats = new NativeArray<byte>(N * N * N, Allocator.Temp);
        var bMat = new NativeArray<byte>(4096, Allocator.Temp);
        var bUni = new NativeArray<bool>(4096, Allocator.Temp);
        var stats = new NativeArray<int>(TerrainGenChunkSource.VOXSTATS_LEN, Allocator.Temp);
        byte air = E(GenMat.Air), grass = E(GenMat.Grass), sav = E(GenMat.SavannaGrass), stone = E(GenMat.Limestone);
        // Ground height per column: x < 32 grass top at y 60 (inside brick row 7), 32..63 grass top at y 63 (brick row 7's
        // TOP layer: brick row 8 above is uniform air), 64..95 savanna at y 60, 96..111 stone at y 60, 112..127 a grass
        // staircase rising 3 per column (~72 deg; its top column x 127 is a peak, not a slope, and is not asserted).
        int Top(int x) => x < 32 ? 60 : x < 64 ? 63 : x < 112 ? 60 : 20 + 3 * (x - 112);
        byte Host(int x) => x < 64 ? grass : x < 96 ? sav : x < 112 ? stone : grass;
        for (int z = 0; z < N; z++) for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            mats[TerrainGenChunkSource.Index(x, y, z)] = y < Top(x) ? stone : y == Top(x) ? Host(x) : air;
        // brick flags as the voxel pass sets them; a uniform brick's scratch is junk (the generator never writes it)
        for (int bi = 0; bi < 4096; bi++)
        {
            int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
            byte first = mats[TerrainGenChunkSource.Index(bx * 8, by * 8, bz * 8)]; bool uni = true;
            for (int z = 0; z < 8 && uni; z++) for (int y = 0; y < 8 && uni; y++) for (int x = 0; x < 8; x++)
                if (mats[TerrainGenChunkSource.Index(bx * 8 + x, by * 8 + y, bz * 8 + z)] != first) { uni = false; break; }
            bUni[bi] = uni; bMat[bi] = first;
            if (uni)
                for (int z = 0; z < 8; z++) for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                    mats[TerrainGenChunkSource.Index(bx * 8 + x, by * 8 + y, bz * 8 + z)] = 0x5A;   // junk
        }
        var uniBefore = bUni.ToArray(); var matBefore = bMat.ToArray(); var before = mats.ToArray();
        try
        {
            TerrainGenChunkSource.OverlayFringe(42u, new int3(0, 0, 0), toEngine, mats, bMat, bUni, stats);
            CollectionAssert.AreEqual(uniBefore, bUni.ToArray(), "brick uniform flags changed");
            CollectionAssert.AreEqual(matBefore, bMat.ToArray(), "brick materials changed");
            byte fG = E(GenMat.GrassFringe), fS = E(GenMat.SavannaFringe);
            int[] perZone = new int[5]; int bad = 0, maxTall = 0;
            for (int z = 0; z < N; z++) for (int x = 0; x < N; x++)
            {
                int tall = 0;
                for (int y = 0; y < N; y++)
                {
                    int i = TerrainGenChunkSource.Index(x, y, z);
                    if (mats[i] == before[i]) continue;
                    int bi = (z >> 3 << 8) | (y >> 3 << 4) | (x >> 3);
                    bool ok = !bUni[bi] && before[i] == air && y > Top(x) && y <= Top(x) + 3
                              && mats[i] == (Host(x) == sav ? fS : fG) && Host(x) != stone;
                    if (!ok) bad++;
                    tall++;
                }
                maxTall = System.Math.Max(maxTall, tall);
                if (tall > 0 && x < 127) perZone[x < 32 ? 0 : x < 64 ? 1 : x < 96 ? 2 : x < 112 ? 4 : 3]++;
            }
            UnityEngine.Debug.Log($"[fringe] synthetic: stub columns per zone (grass@60, grass@63, savanna@60, staircase, stone) " +
                                  $"{string.Join(" / ", perZone)}; stats stubs {stats[TerrainGenChunkSource.FS_STUBS]} voxels {stats[TerrainGenChunkSource.FS_VOXELS]} " +
                                  $"refused {stats[TerrainGenChunkSource.FS_REFUSED]} new bricks {stats[TerrainGenChunkSource.FS_NEWBRICK]}");
            Assert.AreEqual(0, bad, "a changed voxel that is not air -> the host's fringe id, 1-3 above a host, in a dense brick");
            Assert.AreEqual(0, stats[TerrainGenChunkSource.FS_NEWBRICK], "new dense bricks");
            Assert.Greater(perZone[0], 32 * 128 / 4, "grass inside a brick: expected ~3/8 of the columns to get a stub");
            Assert.Less(perZone[0], 32 * 128 / 2, "grass inside a brick: far more stubs than 3/8 of the columns");
            Assert.AreEqual(0, perZone[1], "a plane on a brick's top layer: every stub must be refused (uniform air above)");
            Assert.Greater(stats[TerrainGenChunkSource.FS_REFUSED], 0, "the refusal counter never moved");
            Assert.Greater(perZone[2], 32 * 128 / 4, "savanna grass got no stubs");
            Assert.AreEqual(0, perZone[3], "the 72 deg staircase must get no stub");
            Assert.AreEqual(0, perZone[4], "stone must get no stub");
            Assert.LessOrEqual(maxTall, 3);
            Assert.AreEqual(stats[TerrainGenChunkSource.FS_VOXELS], CountChanged(before, mats), "voxel counter");
        }
        finally { toEngine.Dispose(); mats.Dispose(); bMat.Dispose(); bUni.Dispose(); stats.Dispose(); }
    }

    private static int CountChanged(byte[] a, NativeArray<byte> b)
    {
        int n = 0;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) n++;
        return n;
    }

    // ==================================================================
    // forest_eye (3366.4, 4950.0 m) and savanna_eye (2060.8, 4950.0 m): the surface chunk and the one above.

    private static TerrainGenChunkSource Source() => new TerrainGenChunkSource(42u);   // default spec: map v4, trees on

    private static int3[] Chunks(TerrainGenChunkSource src)
    {
        var list = new System.Collections.Generic.List<int3>();
        foreach (var p in new[] { new int2(33664, 49500), new int2(20608, 49500) })
        {
            var spec = src.Spec; var view = src.Map.View; var o = src.O;
            var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, p.x, p.y, 1);
            int3 c = new int3(p.x >> 7, col.height >> 7, p.y >> 7);
            list.Add(c); list.Add(c + new int3(0, 1, 0));
        }
        return list.ToArray();
    }

    private sealed class Gen
    {
        public readonly byte[] v = new byte[128 * 128 * 128];
        public readonly bool[] dense = new bool[4096];
        public Gen(TerrainGenChunkSource src, int3 coord)
        {
            var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK + 8);
            try
            {
                var chunk = new Chunk();
                src.Generate(coord, chunk, new ChunkHandleAllocator(4), pool, null);
                if (chunk.isUniform) { for (int i = 0; i < v.Length; i++) v[i] = chunk.uniformMaterial; return; }
                var raw = pool.RawData;
                for (int bi = 0; bi < 4096; bi++)
                {
                    uint d = chunk.bricks[bi].data;
                    int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
                    bool dn = (d & 0x80000000u) != 0; dense[bi] = dn;
                    int body = (int)(d & 0x3FFFFFFFu) * 512;
                    for (int z = 0; z < 8; z++) for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                        v[TerrainGenChunkSource.Index(bx * 8 + x, by * 8 + y, bz * 8 + z)] = dn ? raw[body + ((z << 6) | (y << 3) | x)] : (byte)d;
                }
            }
            finally { pool.Dispose(); }
        }
    }

    private static ulong Hash(ulong h, byte[] v)
    {
        for (int i = 0; i < v.Length; i++) h = (h ^ v[i]) * FNV_PRIME;
        return h;
    }

    [Test, Category("Slow")]
    public void World_FringeOff_ByteIdentical()
    {
        using var src = Source();
        Assert.AreEqual(0, src.Spec.fringeOn, "fringe must be OFF by default");
        ulong h = FNV_OFFSET;
        foreach (var c in Chunks(src)) h = Hash(h, new Gen(src, c).v);
        string dir = "Logs/TerrainGen/fringe250";
        Directory.CreateDirectory(dir);
        File.WriteAllText($"{dir}/hash_off.txt", $"seed 42, forest_eye + savanna_eye surface chunk and the one above, fringe OFF: 0x{h:X16}\n");
        UnityEngine.Debug.Log($"[fringe] off hash 0x{h:X16}");
        if (PINNED_OFF_HASH == 0UL) Assert.Ignore($"not pinned yet: 0x{h:X16}");
        Assert.AreEqual(PINNED_OFF_HASH, h, "fringe OFF is no longer byte-identical to the code before task 250");
    }

    [Test, Category("Slow")]
    public void World_FringeOn_OnlyAirBecomesFringe_NoNewBodies()
    {
        using var src = Source();
        var cs = Chunks(src);
        var off = new Gen[cs.Length];
        for (int i = 0; i < cs.Length; i++) off[i] = new Gen(src, cs[i]);
        src.SetFringe(true);
        try
        {
            byte air = E(GenMat.Air), fG = E(GenMat.GrassFringe), fS = E(GenMat.SavannaFringe);
            byte hG = E(GenMat.Grass), hJ = E(GenMat.JungleGrass), hS = E(GenMat.SavannaGrass);
            long[] perSite = new long[2]; long bad = 0; int denseOff = 0, denseOn = 0;
            for (int k = 0; k < cs.Length; k++)
            {
                var on = new Gen(src, cs[k]);
                for (int bi = 0; bi < 4096; bi++) { if (off[k].dense[bi]) denseOff++; if (on.dense[bi]) denseOn++; }
                CollectionAssert.AreEqual(off[k].dense, on.dense, $"chunk {cs[k]}: the dense-brick table changed");
                for (int i = 0; i < on.v.Length; i++)
                {
                    byte o = off[k].v[i], n = on.v[i];
                    if (o == n) continue;
                    int y = i >> 14;
                    byte below = y > 0 ? on.v[i - (1 << 14)] : (byte)0;
                    bool ok = o == air && (n == fG || n == fS)
                              && (below == n || (n == fG ? (below == hG || below == hJ) : below == hS));
                    if (!ok) bad++; else perSite[k >> 1]++;
                }
            }
            UnityEngine.Debug.Log($"[fringe] world seed 42: fringe voxels forest_eye {perSite[0]:N0}, savanna_eye {perSite[1]:N0}; " +
                                  $"dense bricks off {denseOff:N0} on {denseOn:N0}; bad {bad}; {src.FringeSummary()}");
            Assert.AreEqual(0, bad, "fringe changed something other than air -> fringe on a host");
            Assert.AreEqual(denseOff, denseOn, "new dense bricks (= new bodies)");
            Assert.AreEqual(0, src.FringeStats[4], "the pass's own new-brick guard");
            Assert.Greater(perSite[0], 0, "no fringe at forest_eye");
            Assert.Greater(perSite[1], 0, "no fringe at savanna_eye");
        }
        finally { src.SetFringe(false); }
    }
}
