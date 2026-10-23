// Assets/CoreEngine/Tests/ShellColumnLeapTests.cs
//
// Task 116 (owner Q66 (a), PERF_30FPS_PLAN row 1): the shells' COLUMN SKY LEAP
// (Raymarch.compute ShellMarch's SHELL_COLLEAP_ON block, the CPU twin
// ShellRayReference.March(colLeap)). A rising ray in an air brick of a paged chunk at
// or above its GROUP COLUMN's top (ShellStore COLUMN TOPS: 4x4 bricks in x and z, the
// highest non-air brick at any y, per page) reads only air up to the chunk's top, so
// ONE leap crosses that column -- or the whole chunk's x and z when every column is
// that low -- where ShellBoxLeapOk allows it (no tier-0 leap, distance shell k).
//   (1) With the exit-plane leap (the mask's, 93b5 (b)) the leap ends exactly where its
//       brick leaps end, so every Result field but the counters is equal on and off --
//       mask off and mask on -- on ShellAirMaskTests' world (High layout, holes, absent
//       chunks, floating cells in the sky, a tier-0 window) + the shadows of its hits.
//   (2) With the default leap (LeapSpanReseedClosedForm's, eps-rounded per leap): the
//       same end, shell, cell, normal, material on every ray; t may differ in its last
//       bits (one leap's exit t is summed from another tMax than the last brick's).
//   (3) With the sky-ray exit (task 99) too: the same, and a SKY end never earlier.
//   (4) A mutation (every column top one brick low) FAILS (1).
//   (5) The store uploads the tops with the page (ShadowColTops), and a redraw's new page
//       carries its own tops (higher and lower: unlike SolidTopY they are exact, a page is
//       never rewritten). Real tg2 pages: ShellAirMaskTests.CheckStored checks them too.
//   (6) LeapBox over a cube is Leap, field for field.
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class ShellColumnLeapTests
{
    static readonly float3 Cam = ShellAirMaskTests.Cam;
    const float TTIER0 = 640f;                 // tier 0 marched 64 m and saw air (ShellAirMaskTests)
    const int CAP = 4096, SHADOW_CAP = 2048;   // no ray ends at the cap: every field compares

    struct Tally
    {
        public int Rays, Hits, Shadows, ShadowHits, CapOff, Differ, TDiffer, SkyEnds, ColLeaps, ColChunk;
        public long ItersOn, ItersOff;
        public double MaxRelT;
        public string First;
        public override string ToString() =>
            $"{Rays} primary rays, {Hits} hits, {Shadows} shadows ({ShadowHits} shadowed), sky ends {SkyEnds}, capped {CapOff}; " +
            $"differ {Differ}, t differs (last bits only) {TDiffer} (max rel {MaxRelT:E1}); column leaps {ColLeaps:N0} (whole chunk {ColChunk:N0}); " +
            $"iterations on {ItersOn:N0} vs off {ItersOff:N0} ({100.0 * ItersOn / math.max(1, ItersOff):F0}%)";
    }

    static void Check(ref Tally t, ShellRayReference.Result on, ShellRayReference.Result off, string what)
    {
        t.ItersOn += on.Iters; t.ItersOff += off.Iters; t.ColLeaps += on.ColLeaps; t.ColChunk += on.ColLeapsChunk;
        if (off.End == ShellRayReference.END_CAP) t.CapOff++;
        bool cells = on.End == off.End && on.Shell == off.Shell && on.Voxel.Equals(off.Voxel) && on.Normal.Equals(off.Normal)
                     && on.Mat == off.Mat && on.InsideStart == off.InsideStart;
        bool sky = on.End == ShellRayReference.END_SKY && off.End == ShellRayReference.END_SKY;
        if (sky)
        {
            // A sky end's cell and t are where the exit fired: the leap may carry it further up
            // (never lower than the last bits: the same exit plane, its t summed another way).
            t.SkyEnds++;
            if (on.T < off.T * (1f - 1e-6f)) { t.Differ++; t.First ??= $"{what}: SKY end earlier with the leap: on {ShellAirMaskTests.Show(on)} / off {ShellAirMaskTests.Show(off)}"; }
            return;
        }
        if (!cells) { t.Differ++; t.First ??= $"{what}: on {ShellAirMaskTests.Show(on)} / off {ShellAirMaskTests.Show(off)}"; return; }
        if (on.T != off.T)
        {
            t.TDiffer++;
            t.MaxRelT = math.max(t.MaxRelT, math.abs((double)on.T - off.T) / math.max(1.0, math.abs((double)off.T)));
        }
    }

    /// March RaySet (and the shadow of every hit) with the column leap on and off.
    static Tally Compare(ShellAirMaskTests.BrickView v, bool planeLeap, bool useMask, int skyY = ShellRayReference.SKY_NEVER)
    {
        var sun = math.normalize(new float3(0.35f, 0.8f, -0.2f));
        var t = new Tally();
        foreach (var dir in ShellMarchCacheTests.RaySet(v.L, Cam, out _))
        {
            var on = ShellRayReference.March(v, Cam, dir, 0f, false, Cam, CAP, TTIER0, useMask: useMask, planeLeap: planeLeap, skyY: skyY, colLeap: true);
            var off = ShellRayReference.March(v, Cam, dir, 0f, false, Cam, CAP, TTIER0, useMask: useMask, planeLeap: planeLeap, skyY: skyY);
            t.Rays++;
            Check(ref t, on, off, $"primary {dir}");
            if (off.End != ShellRayReference.END_HIT) continue;
            t.Hits++;
            float3 so = Cam + dir * off.T + (float3)off.Normal * 0.01f;
            var sOn = ShellRayReference.March(v, so, sun, 0f, true, Cam, SHADOW_CAP, 0f, useMask: useMask, planeLeap: planeLeap, skyY: skyY, colLeap: true);
            var sOff = ShellRayReference.March(v, so, sun, 0f, true, Cam, SHADOW_CAP, 0f, useMask: useMask, planeLeap: planeLeap, skyY: skyY);
            t.Shadows++;
            if (sOff.End == ShellRayReference.END_HIT) t.ShadowHits++;
            Check(ref t, sOn, sOff, $"shadow of {dir}");
        }
        return t;
    }

    static void AssertCoverage(Tally t)
    {
        Assert.AreEqual(0, t.CapOff, "no ray ends at the cap (its fields would not compare)");
        Assert.Greater(t.Hits, 200, "rays hit the ground");
        Assert.Greater(t.ShadowHits, 10, "some shadow rays are blocked");
        Assert.Greater(t.ColLeaps, 1000, "rising rays leap columns");
        Assert.Greater(t.ColChunk, 100, "... and whole chunks");
        Assert.Less(t.ItersOn, t.ItersOff, "the leap saves iterations");
    }

    [Test]
    public void ColumnLeap_ExitPlaneLeap_ChangesNoResult_MaskOffAndOn()
    {
        foreach (bool mask in new[] { false, true })
        {
            var v = new ShellAirMaskTests.BrickView(ShellLayout.From(QualityPreset.High));
            var t = Compare(v, planeLeap: true, useMask: mask);
            Debug.Log($"[116] column leap on vs off, exit-plane leap, mask {(mask ? "on" : "off")}: {t}");
            Assert.AreEqual(0, t.Differ, t.First);
            Assert.AreEqual(0, t.TDiffer, "exact: t too");
            AssertCoverage(t);
        }
    }

    [Test]
    public void ColumnLeap_DefaultLeap_SameEndsCellsAndNormals()
    {
        var v = new ShellAirMaskTests.BrickView(ShellLayout.From(QualityPreset.High));
        var t = Compare(v, planeLeap: false, useMask: false);
        Debug.Log($"[116] column leap on vs off, default leap (the shader's mask-off path): {t}");
        Assert.AreEqual(0, t.Differ, t.First);
        Assert.Less(t.MaxRelT, 1e-5, "t differs only in its last bits");
        AssertCoverage(t);
    }

    [Test]
    public void ColumnLeap_WithSkyExit_SameResults_SkyEndNeverEarlier()
    {
        var v = new ShellAirMaskTests.BrickView(ShellLayout.From(QualityPreset.High));
        // Above every floating cell (BLOB_TOP - 100 m + one shell-7 brick) and the ground.
        int skyY = ShellAirMaskTests.GROUND + 2600 + 1024;
        var t = Compare(v, planeLeap: false, useMask: false, skyY: skyY);
        Debug.Log($"[116] column leap on vs off with the sky exit at {skyY}: {t}");
        Assert.AreEqual(0, t.Differ, t.First);
        Assert.Less(t.MaxRelT, 1e-5);
        Assert.Greater(t.SkyEnds, 100, "rays end at the sky exit");
    }

    [Test]
    public void ColumnLeap_Mutation_TopsOneBrickLow_IsCaught()
    {
        var v = new ShellAirMaskTests.BrickView(ShellLayout.From(QualityPreset.High)) { MutateTops = true };
        var t = Compare(v, planeLeap: true, useMask: false);
        Debug.Log($"[116] MUTATED column tops (one brick low): {t}");
        Assert.Greater(t.Differ, 0, "tops below a non-air brick must change some result");
    }

    [Test]
    public void ColumnTops_UploadedWithThePage_RedrawCarriesItsOwn()
    {
        ShellStore.ShadowUploads = true;
        var sc = new TerrainGenChunkSource.ShellChunk();
        try
        {
            using (var st = new ShellStore(2, new int3(8, 8, 8), 64, 4))
            {
                int budget;
                void Air(int3 c)
                {
                    sc.Shell = 2; sc.Coord = c; sc.IsUniformAir = false;
                    for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++) { sc.BrickUniform[i] = true; sc.BrickMat[i] = Materials.Air; }
                }
                int Top(int3 c, int j)
                {
                    uint e = st.DirectoryEntry(st.Slot(c));
                    Assert.AreNotEqual(0u, e & ShellStore.PAGE, "paged");
                    return ShellStore.ColumnTop(st.ShadowColTops, j, (int)(e & ~ShellStore.PAGE) * ShellStore.COLTOP_UINTS);
                }
                var c0 = new int3(3, 5, 3);
                // Stone at brick (7, 4, 2) -> column (gz 0, gx 1) = 1, top 5; water (a dense brick with one
                // voxel) at (13, 11, 14) -> column (3, 3) = 15, top 12; stone at (0, 15, 15) -> column 12, top 16.
                Air(c0);
                sc.BrickMat[(2 << 8) | (4 << 4) | 7] = Materials.Stone;
                int bi = (14 << 8) | (11 << 4) | 13;
                sc.BrickUniform[bi] = false;
                for (int vz = 0; vz < 8; vz++) for (int vy = 0; vy < 8; vy++) for (int vx = 0; vx < 8; vx++)
                    sc.Mats[TerrainGenChunkSource.Index((13 << 3) + vx, (11 << 3) + vy, (14 << 3) + vz)] = Materials.Air;
                sc.Mats[TerrainGenChunkSource.Index((13 << 3) + 2, (11 << 3) + 7, (14 << 3) + 1)] = Materials.Water;
                sc.BrickMat[(15 << 8) | (15 << 4) | 0] = Materials.Stone;
                budget = int.MaxValue; Assert.AreEqual(ShellStore.StoreResult.Stored, st.Store(sc, ref budget));
                budget = int.MaxValue; Assert.IsTrue(st.FlushDirectory(ref budget));
                for (int j = 0; j < 16; j++)
                    Assert.AreEqual(j == 1 ? 5 : j == 15 ? 12 : j == 12 ? 16 : 0, Top(c0, j), $"column {j}");
                // A redraw (an op / edit summary) raising column 1 and dropping column 15's water: the new page's tops.
                sc.BrickMat[(1 << 8) | (9 << 4) | 5] = Materials.Stone;
                sc.BrickUniform[bi] = true; sc.BrickMat[bi] = Materials.Air;
                budget = int.MaxValue; Assert.AreEqual(ShellStore.StoreResult.Stored, st.Store(sc, ref budget, replace: true));
                budget = int.MaxValue; Assert.IsTrue(st.FlushDirectory(ref budget));
                for (int j = 0; j < 16; j++)
                    Assert.AreEqual(j == 1 ? 10 : j == 12 ? 16 : 0, Top(c0, j), $"redrawn: column {j}");
                // Lower again: the tops follow (they are exact per page, not a running max like SolidTopY).
                Air(c0); sc.BrickMat[(4 << 8) | (0 << 4) | 4] = Materials.Stone;
                budget = int.MaxValue; Assert.AreEqual(ShellStore.StoreResult.Stored, st.Store(sc, ref budget, replace: true));
                budget = int.MaxValue; Assert.IsTrue(st.FlushDirectory(ref budget));
                for (int j = 0; j < 16; j++)
                    Assert.AreEqual(j == 5 ? 1 : 0, Top(c0, j), $"lowered: column {j}");
                // The static builder on a random page agrees with a brute-force scan.
                var rng = new Unity.Mathematics.Random(116u);
                var handles = new uint[EngineConfig.BRICKS_PER_CHUNK];
                var tops = new uint[ShellStore.COLTOP_UINTS];
                for (int rep = 0; rep < 20; rep++)
                {
                    for (int lb = 0; lb < handles.Length; lb++) handles[lb] = rng.NextInt(0, 40) == 0 ? (rng.NextBool() ? 7u : ShellStore.DENSE | (uint)lb) : 0u;
                    ShellStore.ColumnTops(handles, tops);
                    for (int j = 0; j < 16; j++)
                    {
                        int want = 0;
                        for (int lb = 0; lb < handles.Length; lb++)
                            if (handles[lb] != 0u && ((((lb >> 10) & 3) << 2) | ((lb >> 2) & 3)) == j) want = math.max(want, ((lb >> 4) & 15) + 1);
                        Assert.AreEqual(want, ShellStore.ColumnTop(tops, j), $"random page {rep} column {j}");
                    }
                }
            }
        }
        finally { sc.Dispose(); ShellStore.ShadowUploads = false; }
    }

    [Test]
    public void LeapBox_OverACube_IsLeap()
    {
        var rng = new Unity.Mathematics.Random(1160u);
        for (int i = 0; i < 20000; i++)
        {
            var dir = rng.NextFloat3Direction();
            if (i % 3 == 0) dir = math.normalize(new float3(rng.NextBool() ? 1 : -1, rng.NextBool() ? 1 : -1, rng.NextBool() ? 1 : -1));
            dir = new float3(ShellRayReference.SafeAxis(dir.x), ShellRayReference.SafeAxis(dir.y), ShellRayReference.SafeAxis(dir.z));
            int sh = rng.NextInt(1, 13);
            var start = new float3(40960f + rng.NextInt(-3000, 3000) + rng.NextInt(0, 4) * 0.25f,
                                   16384f + rng.NextInt(-400, 800) + rng.NextInt(0, 4) * 0.25f,
                                   40960f + rng.NextInt(-3000, 3000) + rng.NextInt(0, 4) * 0.25f);
            int3 step = (int3)math.sign(dir);
            float3 tDelta = math.abs(1f / dir);
            int3 v0 = (int3)math.floor(start);
            float3 m0 = ShellRayReference.ReseedTMax(v0, start, tDelta, step);
            int3 va = v0, vb = v0, na = int3.zero, nb = int3.zero; float3 ma = m0, mb = m0; float ta = 0f, tb = 0f;
            ShellRayReference.Leap(ref va, ref ma, ref ta, ref na, tDelta, step, start, (v0 >> sh) << sh, 1 << sh);
            ShellRayReference.LeapBox(ref vb, ref mb, ref tb, ref nb, tDelta, step, start, (v0 >> sh) << sh, new int3(1 << sh));
            if (ta != tb || !va.Equals(vb) || !na.Equals(nb) || !ma.Equals(mb))
                Assert.Fail($"ray {i}: Leap t {ta:R} voxel {va} normal {na}; LeapBox t {tb:R} voxel {vb} normal {nb}");
        }
    }
}
