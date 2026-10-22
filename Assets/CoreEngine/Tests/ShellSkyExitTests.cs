// Assets/CoreEngine/Tests/ShellSkyExitTests.cs
//
// Task 99 (owner Q61 (a)): the shells' SKY-RAY EXIT (Raymarch.compute ShellSkyY /
// ShellMarch's skyY, the CPU twin ShellRayReference.March(skyY)). A rising ray whose
// voxel is at or above ShellStore.SolidTopY (the top of the highest non-air brick any
// store was given; never lowered) ends there as a no-hit, END_SKY. It must be exact:
//   * hits are the same hit, field for field (only Iters may drop);
//   * a ray the exit ends would have ended without a hit, at a t >= the exit's t, so the
//     far band's march from either t stops at once when the exit also waited for the
//     far band's max height (PrimarySkyY): FarMarch's first test, D.y >= 0 and
//     p.y >= max height, holds at the exit's t and so at every later one;
//   * every other ray is unchanged (same end, same t).
// Real ShellStores (shells 1-4, shadow uploads) hold a synthetic world: a rolling ground,
// a tower capped with WATER near the camera (fluids count) and a taller peak only shell 4
// holds, so rays that clear the near tower still march until they clear the peak.
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Mirror;
using VoxelEngine.WorldGen;

public class ShellSkyExitTests
{
    const int GROUND = 16384;
    const int SHELLS = 5;   // shell 0 (no tier 0 here) + shells 1-4
    // Tower: x [40448, 40832), z [39936, 40320) (128-voxel aligned: whole bricks in every
    // shell), solid up to TOWER_TOP, its top 30 voxels water. Peak (shell 4's box only):
    // x [43520, 44032), same z, up to PEAK_TOP.
    const int TOWER_X0 = 40448, TOWER_X1 = 40832, TOWER_Z0 = 39936, TOWER_Z1 = 40320;
    const int TOWER_TOP = GROUND + 1504, WATER_DEPTH = 30;
    const int PEAK_X0 = 43520, PEAK_X1 = 44032, PEAK_TOP = GROUND + 3008;   // tops on 16-voxel cell faces

    static float Height(float x, float z) =>
        GROUND + 300f * math.sin(x / 2000f) * math.cos(z / 1700f) + 80f * math.sin(x / 310f + z / 170f);

    static readonly float3 Cam = new float3(40000.5f, math.floor(Height(40000.5f, 40000.5f)) + 140.5f, 40000.5f);

    /// Material of shell k's cell c (cell centre p): ground stone, the tower (water cap), the peak.
    static byte Cell(int k, int3 c, float ground)
    {
        float3 p = ((float3)c + 0.5f) * (1 << k);
        bool inZ = p.z >= TOWER_Z0 && p.z < TOWER_Z1;
        if (inZ && p.x >= TOWER_X0 && p.x < TOWER_X1 && p.y < TOWER_TOP)
            return p.y >= TOWER_TOP - WATER_DEPTH ? Materials.Water : Materials.Stone;
        if (k == 4 && inZ && p.x >= PEAK_X0 && p.x < PEAK_X1 && p.y < PEAK_TOP) return Materials.Stone;
        return p.y < ground ? Materials.Stone : Materials.Air;
    }

    /// The stores' view, as StoreView but with a box of +-2 chunks round the camera's chunk,
    /// the generated layers in y, and a hole of +-1 for shells >= 2 (the finer shell's box).
    sealed class StoresView : ShellRayReference.IView
    {
        public readonly ShellStore[] St = new ShellStore[SHELLS];
        public readonly int[] YLo = new int[SHELLS], YHi = new int[SHELLS];
        public int ShellCount => SHELLS;
        public float OuterVoxels(int k) => 256 << k;
        public bool Box(int k, out int3 lo, out int3 hi, out int3 cc, out int hole)
        {
            cc = (int3)math.floor(Cam) >> (7 + k);
            lo = new int3(cc.x - 2, YLo[k], cc.z - 2); hi = new int3(cc.x + 2, YHi[k], cc.z + 2);
            hole = k == 1 ? -1 : 1;
            return St[k] != null;
        }
        public uint Dir(int k, int3 c) => St[k].Holds(c) ? St[k].DirectoryEntry(St[k].Slot(c)) : ShellStore.ABSENT;
        public uint Page(int k, int page, int lb) => St[k].ShadowPages[page * EngineConfig.BRICKS_PER_CHUNK + lb];
        public byte Voxel(int k, int body, int vIdx) => (byte)(St[k].ShadowBricks[body * 128 + (vIdx >> 2)] >> ((vIdx & 3) * 8));
        public bool Tier0Missing(int3 voxel) => false;
        public bool Tier0Holds(int3 voxel) => false;
        public int TopY
        {
            get { int y = int.MinValue; for (int k = 1; k < SHELLS; k++) y = math.max(y, St[k].SolidTopY); return y; }
        }
        public int MaxCellTop;   // the highest non-air cell's top (exclusive), voxels, from the generator
    }

    private static StoresView _view;

    /// Fill sc with shell k's chunk c: bricks wholly under the ground (and clear of the
    /// tower / peak) uniform stone, wholly over it uniform air, the rest cell by cell
    /// (uniform when every cell agrees). hMin / hMax: the ground's range per brick column.
    static void MakeChunk(TerrainGenChunkSource.ShellChunk sc, int k, int3 c, float[] h, float[] hMin, float[] hMax, ref int maxCellTop)
    {
        sc.Shell = k; sc.Coord = c; sc.IsUniformAir = false; sc.Redraw = false;
        int cellsY0 = c.y << 7;
        float cs = 1 << k;
        bool anySolid = false;
        for (int bz = 0; bz < 16; bz++)
        for (int by = 0; by < 16; by++)
        for (int bx = 0; bx < 16; bx++)
        {
            int bi = (bz << 8) | (by << 4) | bx;
            int3 b0 = (c << 7) + new int3(bx, by, bz) * 8;            // first cell
            float3 v0 = (float3)b0 * cs, v1 = v0 + 8f * cs;          // voxel span
            bool feature = v0.z < TOWER_Z1 && v1.z > TOWER_Z0 && v0.y < PEAK_TOP
                           && ((v0.x < TOWER_X1 && v1.x > TOWER_X0) || (k == 4 && v0.x < PEAK_X1 && v1.x > PEAK_X0));
            float yLoC = v0.y + 0.5f * cs, yHiC = v1.y - 0.5f * cs;   // lowest / highest cell centre
            int col = (bz << 4) | bx;
            int first;
            if (!feature && yLoC >= hMax[col]) first = Materials.Air;
            else if (!feature && yHiC < hMin[col]) first = Materials.Stone;
            else
            {
                first = -1;
                for (int vz = 0; vz < 8; vz++)
                for (int vy = 0; vy < 8; vy++)
                for (int vx = 0; vx < 8; vx++)
                {
                    int x = (bx << 3) + vx, y = (by << 3) + vy, z = (bz << 3) + vz;
                    byte m = Cell(k, (c << 7) + new int3(x, y, z), h[(z << 7) | x]);
                    sc.Mats[TerrainGenChunkSource.Index(x, y, z)] = m;
                    if (m != Materials.Air) maxCellTop = math.max(maxCellTop, (cellsY0 + y + 1) << k);
                    if (first == -1) first = m; else if (first != m) first = -2;
                }
            }
            if (first == Materials.Stone) maxCellTop = math.max(maxCellTop, (int)v1.y);
            if (first != Materials.Air) anySolid = true;
            sc.BrickUniform[bi] = first >= 0;
            sc.BrickMat[bi] = first >= 0 ? (byte)first : (byte)0;
        }
        sc.IsUniformAir = !anySolid;
    }

    static StoresView Build()
    {
        if (_view != null) return _view;
        ShellStore.ShadowUploads = true;
        var v = new StoresView();
        var sc = new TerrainGenChunkSource.ShellChunk();
        var h = new float[128 * 128];
        var hMin = new float[256];
        var hMax = new float[256];
        int maxCellTop = int.MinValue;
        try
        {
            for (int k = 1; k < SHELLS; k++)
            {
                int3 cc = (int3)math.floor(Cam) >> (7 + k);
                v.YLo[k] = (GROUND - 500) >> (7 + k);
                // Sky chunks (uniform air) up to 120 m over the peak in every shell, so rising
                // rays keep marching past the bound instead of leaving the boxes first.
                v.YHi[k] = (PEAK_TOP + 1200) >> (7 + k);
                int layers = v.YHi[k] - v.YLo[k] + 1;
                v.St[k] = new ShellStore(k, new int3(8, ShellLayout.Pow2Ceil(layers), 8), 40000, 25 * layers);
                int budget = int.MaxValue;
                for (int cz = cc.z - 2; cz <= cc.z + 2; cz++)
                for (int cx = cc.x - 2; cx <= cc.x + 2; cx++)
                {
                    float s = 1 << k;
                    for (int z = 0; z < 128; z++)
                    for (int x = 0; x < 128; x++)
                        h[(z << 7) | x] = Height(((cx << 7) + x + 0.5f) * s, ((cz << 7) + z + 0.5f) * s);
                    for (int i = 0; i < 256; i++) { hMin[i] = float.MaxValue; hMax[i] = float.MinValue; }
                    for (int z = 0; z < 128; z++)
                    for (int x = 0; x < 128; x++)
                    {
                        int col = ((z >> 3) << 4) | (x >> 3);
                        hMin[col] = math.min(hMin[col], h[(z << 7) | x]); hMax[col] = math.max(hMax[col], h[(z << 7) | x]);
                    }
                    for (int cy = v.YLo[k]; cy <= v.YHi[k]; cy++)
                    {
                        MakeChunk(sc, k, new int3(cx, cy, cz), h, hMin, hMax, ref maxCellTop);
                        budget = int.MaxValue;
                        Assert.AreEqual(ShellStore.StoreResult.Stored, v.St[k].Store(sc, ref budget), $"shell {k} chunk ({cx},{cy},{cz})");
                    }
                }
                budget = int.MaxValue;
                Assert.IsTrue(v.St[k].FlushDirectory(ref budget));
            }
        }
        finally { sc.Dispose(); ShellStore.ShadowUploads = false; }
        v.MaxCellTop = maxCellTop;
        _view = v;
        return v;
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (_view == null) return;
        for (int k = 1; k < SHELLS; k++) _view.St[k]?.Dispose();
        _view = null;
    }

    static void AssertSameHit(ShellRayReference.Result on, ShellRayReference.Result off, string what)
    {
        Assert.AreEqual(off.End, on.End, what + ": end");
        Assert.AreEqual(off.Shell, on.Shell, what + ": shell");
        Assert.AreEqual(off.T, on.T, what + ": t (exact)");
        Assert.AreEqual(off.Voxel, on.Voxel, what + ": voxel");
        Assert.AreEqual(off.Normal, on.Normal, what + ": normal");
        Assert.AreEqual(off.Mat, on.Mat, what + ": mat");
        Assert.AreEqual(off.InsideStart, on.InsideStart, what + ": inside");
        Assert.AreEqual(off.Iters, on.Iters, what + ": iterations (the exit cannot fire before a hit)");
    }

    /// One ray, exit on vs off (Raymarch.compute's two call shapes). Returns true when the exit ended it.
    static bool Compare(StoresView v, float3 start, float3 dir, bool shadow, int skyY, bool farOn, float farMaxH, string what,
                        ref long itersOn, ref long itersOff)
    {
        int cap = shadow ? 192 : 384;
        var off = ShellRayReference.March(v, start, dir, 0f, shadow, Cam, cap, 0f);
        var on = ShellRayReference.March(v, start, dir, 0f, shadow, Cam, cap, 0f, skyY: skyY);
        itersOn += on.Iters; itersOff += off.Iters;
        Assert.AreEqual(off.End == ShellRayReference.END_HIT, on.End == ShellRayReference.END_HIT, what + ": hit / no hit");
        if (off.End == ShellRayReference.END_HIT) { AssertSameHit(on, off, what); return false; }
        if (on.End != ShellRayReference.END_SKY)
        {
            Assert.AreEqual(off.End, on.End, what + ": end"); Assert.AreEqual(off.T, on.T, what + ": t"); Assert.AreEqual(off.Iters, on.Iters, what + ": iters");
            return false;
        }
        Assert.Greater(dir.y, -1e-6f, what + ": only a rising ray exits");
        Assert.GreaterOrEqual(off.T, on.T, what + ": the march would have ended later");
        Assert.LessOrEqual(on.Iters, off.Iters, what + ": fewer iterations");
        if (farOn && !shadow)
        {
            // The far march from on.T stops at its first test, as it does from off.T (>= on.T).
            Assert.GreaterOrEqual(start.y + dir.y * on.T, farMaxH, what + ": above the far band's max height at the exit");
            Assert.GreaterOrEqual(start.y + dir.y * off.T, farMaxH, what + ": ... and where the march would have ended");
        }
        return true;
    }

    [Test]
    public void SolidTop_CoversEveryStoredSolidAndFluidCell_Tightly()
    {
        var v = Build();
        int top = v.TopY;
        UnityEngine.Debug.Log($"[ShellSkyExit] stores' solid top {top} voxels, highest non-air cell top {v.MaxCellTop}; per shell " +
                              string.Join(", ", new[] { 1, 2, 3, 4 }.Select(k => $"s{k} {v.St[k].SolidTopY}")));
        Assert.GreaterOrEqual(top, v.MaxCellTop, "the bound is never below a stored non-air cell");
        Assert.Less(top - v.MaxCellTop, 8 << 4, "and at most one shell-4 brick above it");
        Assert.AreEqual(PEAK_TOP, v.MaxCellTop, "the peak is the highest thing");
        // The near shells' bounds come from the tower's WATER cap (fluids count).
        int3 waterCell = new int3(TOWER_X0 + 8, TOWER_TOP - 1, TOWER_Z0 + 8) >> 1;
        Assert.AreEqual(Materials.Water, Cell(1, waterCell, 0f));
        Assert.GreaterOrEqual(v.St[1].SolidTopY, TOWER_TOP);
        Assert.Less(v.St[1].SolidTopY, TOWER_TOP + 16);
    }

    [Test]
    public void ExitOnOff_SameHitsAndNoHits_RandomRaysAndShadows()
    {
        var v = Build();
        int shellTop = v.TopY;
        var rng = new Unity.Mathematics.Random(99u);
        var sun = math.normalize(new float3(0.35f, 0.8f, -0.2f));
        // Three skyY shapes: far band off (the shells' top), far band on with its max height
        // above the shells' top (that decides), and below it (the shells' top decides).
        var cases = new (bool farOn, float farMaxH)[] { (false, 0f), (true, shellTop + 437.3f), (true, shellTop - 900f) };
        foreach (var (farOn, farMaxH) in cases)
        {
            int skyY = ShellRayReference.PrimarySkyY(shellTop, farOn, farMaxH);
            int rays = 0, hits = 0, exits = 0, shadows = 0, shadowExits = 0, shadowHits = 0;
            long iOn = 0, iOff = 0, sOn = 0, sOff = 0;
            for (int i = 0; i < 2400; i++)
            {
                float az = rng.NextFloat(0f, 2f * math.PI), el = math.radians(rng.NextFloat(-30f, 80f));
                if (i < 300) el = math.radians(rng.NextFloat(-2f, 4f));    // grazing the horizon
                var dir = math.normalize(new float3(math.cos(az) * math.cos(el), math.sin(el), math.sin(az) * math.cos(el)));
                string what = $"far {farOn} ray {i} az {math.degrees(az):F1} el {math.degrees(el):F2}";
                if (Compare(v, Cam, dir, false, skyY, farOn, farMaxH, what, ref iOn, ref iOff)) exits++;
                rays++;
                var r = ShellRayReference.March(v, Cam, dir, 0f, false, Cam, 384, 0f);
                if (r.End != ShellRayReference.END_HIT) continue;
                hits++;
                // The shell-hit sun shadow, as CSMainBody starts it (ShellSkyY(false): the shells' top).
                float3 so = Cam + dir * r.T + (float3)r.Normal * 0.01f;
                if (Compare(v, so, sun, true, shellTop, false, 0f, what + " shadow", ref sOn, ref sOff)) shadowExits++;
                shadows++;
                if (ShellRayReference.March(v, so, sun, 0f, true, Cam, 192, 0f).End == ShellRayReference.END_HIT) shadowHits++;
            }
            UnityEngine.Debug.Log($"[ShellSkyExit] far {farOn} (max h {farMaxH:F0}), skyY {skyY}: {rays} primary rays, {hits} hits, {exits} exits; " +
                                  $"iterations {iOn:N0} vs {iOff:N0} off ({100.0 * iOn / math.max(1, iOff):F0}%); {shadows} shadows, {shadowHits} shadowed, " +
                                  $"{shadowExits} exits, iterations {sOn:N0} vs {sOff:N0}");
            Assert.Greater(hits, 300, "rays hit the ground and the tower");
            Assert.Greater(exits, 150, "rising rays exit");
            Assert.Less(iOn, iOff, "the exit saves iterations");
            Assert.Greater(shadowHits, 5, "some shadows are blocked (the tower)");
            Assert.Greater(shadowExits, 50, "shadow rays exit");
        }
    }

    /// The comparison can fail: a bound one shell-4 brick too low loses hits on the peak's top.
    [Test]
    public void Mutation_BoundOneBrickLow_IsCaught()
    {
        var v = Build();
        int low = v.TopY - (8 << 4);
        var rng = new Unity.Mathematics.Random(7u);
        int lost = 0;
        // Rays from over the tower toward the peak's top, rising and falling a little.
        for (int i = 0; i < 600; i++)
        {
            var start = new float3(40600.5f, low - rng.NextFloat(1f, 400f), 40100.5f);
            var target = new float3(rng.NextFloat(PEAK_X0, PEAK_X1), rng.NextFloat(low - 60f, PEAK_TOP - 1f), rng.NextFloat(TOWER_Z0, TOWER_Z1));
            var dir = math.normalize(target - start);
            var off = ShellRayReference.March(v, start, dir, 0f, false, Cam, 384, 0f);
            var on = ShellRayReference.March(v, start, dir, 0f, false, Cam, 384, 0f, skyY: low);
            if (off.End == ShellRayReference.END_HIT && on.End != ShellRayReference.END_HIT) lost++;
        }
        UnityEngine.Debug.Log($"[ShellSkyExit] MUTATED bound (one brick low): {lost} of 600 hits lost");
        Assert.Greater(lost, 0, "a bound below the stored solid must change results");
    }

    [Test]
    public void RaysGrazingTheBound_ExitOnlyAtOrAboveIt()
    {
        var v = Build();
        int top = v.TopY;
        long a = 0, b = 0;
        int exitsAt = 0, exitsBelow = 0;
        // Rays starting just under / at / over the bound, level or barely rising or barely falling,
        // over the peak (the cells right under the bound) and elsewhere.
        foreach (float x0 in new[] { PEAK_X0 + 40.5f, (PEAK_X0 + PEAK_X1) * 0.5f, Cam.x })
        foreach (float dy in new[] { -0.5f, 0.25f, 0.5f, 1.0f, 3.5f })
        foreach (float ey in new[] { 0f, 1e-7f, 2e-4f, 0.02f, -2e-4f })
        foreach (float azd in new[] { 0f, 90f, 180f, 270f, 33f })
        {
            float az = math.radians(azd);
            var start = new float3(x0, top + dy - 1f, (TOWER_Z0 + TOWER_Z1) * 0.5f + 0.37f);
            var dir = math.normalize(new float3(math.cos(az), ey, math.sin(az)));
            string what = $"x {x0} y top{dy - 1f:+0.00;-0.00} ey {ey} az {azd}";
            bool exited = Compare(v, start, dir, false, top, false, 0f, what, ref a, ref b);
            int vy = (int)math.floor(start.y);
            if (exited && vy >= top) exitsAt++;
            if (exited && vy < top) exitsBelow++;
            if (vy >= top && ey >= 0f) Assert.IsTrue(exited, what + ": starts at or over the bound, not falling: exits at once");
            if (ey < 0f) Assert.IsFalse(exited, what + ": a falling ray never exits");
        }
        // A ray from just under the bound that rises through the peak's top cells must still hit them.
        var up = math.normalize(new float3(0.3f, 1f, 0.1f));
        var hitUnder = ShellRayReference.March(v, new float3(PEAK_X0 + 100.5f, PEAK_TOP - 300.5f, TOWER_Z0 + 100.5f), up, 0f, false, Cam, 384, 0f, skyY: top);
        UnityEngine.Debug.Log($"[ShellSkyExit] grazing: {exitsAt} exits from at/over the bound, {exitsBelow} after rising into it; " +
                              $"iterations {a:N0} vs {b:N0}; ray inside the peak: end {hitUnder.End}");
        Assert.AreEqual(ShellRayReference.END_HIT, hitUnder.End, "a start inside the peak is a hit, exit or not");
        Assert.Greater(exitsAt, 50);
        Assert.Greater(exitsBelow, 5, "rays starting under the bound exit once they rise into it");
    }

    [Test]
    public void Store_RaisesTheBound_NeverLowersIt()
    {
        var sc = new TerrainGenChunkSource.ShellChunk();
        try
        {
            using (var st = new ShellStore(2, new int3(8, 8, 8), 8, 4))
            {
                Assert.AreEqual(int.MinValue, st.SolidTopY, "empty store");
                int budget;
                void Air(int3 c)
                {
                    sc.Shell = 2; sc.Coord = c; sc.IsUniformAir = false;
                    for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++) { sc.BrickUniform[i] = true; sc.BrickMat[i] = Materials.Air; }
                }
                // Chunk (3, 5, 3): one uniform stone brick at by = 4 -> top ((5 << 4) + 5) << 5.
                Air(new int3(3, 5, 3)); sc.BrickMat[(2 << 8) | (4 << 4) | 7] = Materials.Stone;
                budget = int.MaxValue; Assert.AreEqual(ShellStore.StoreResult.Stored, st.Store(sc, ref budget));
                Assert.AreEqual(((5 << 4) + 5) << 5, st.SolidTopY, "uniform stone brick");
                Assert.AreEqual(st.SolidTopY, ShellStore.ChunkSolidTopY(sc, 2));
                // Redraw (an op / summary): a dense brick at by = 9 holding ONE water voxel raises it.
                int bi = (5 << 8) | (9 << 4) | 1;
                sc.BrickUniform[bi] = false;
                for (int vz = 0; vz < 8; vz++) for (int vy = 0; vy < 8; vy++) for (int vx = 0; vx < 8; vx++)
                    sc.Mats[TerrainGenChunkSource.Index((1 << 3) + vx, (9 << 3) + vy, (5 << 3) + vz)] = Materials.Air;
                sc.Mats[TerrainGenChunkSource.Index(8 + 3, 72 + 2, 40 + 5)] = Materials.Water;
                budget = int.MaxValue; Assert.AreEqual(ShellStore.StoreResult.Stored, st.Store(sc, ref budget, replace: true));
                budget = int.MaxValue; Assert.IsTrue(st.FlushDirectory(ref budget));
                Assert.AreEqual(1, st.Replaced);
                Assert.AreEqual(((5 << 4) + 10) << 5, st.SolidTopY, "redraw with a fluid voxel higher up");
                // A lower redraw, an eviction and an all-air chunk higher up leave it.
                Air(new int3(3, 5, 3)); sc.BrickMat[0] = Materials.Stone;
                budget = int.MaxValue; Assert.AreEqual(ShellStore.StoreResult.Stored, st.Store(sc, ref budget, replace: true));
                Assert.AreEqual(((5 << 4) + 10) << 5, st.SolidTopY, "never lowered by a redraw");
                st.Evict(new int3(3, 5, 3));
                Assert.AreEqual(((5 << 4) + 10) << 5, st.SolidTopY, "never lowered by an eviction");
                Air(new int3(3, 7, 3)); sc.IsUniformAir = true;
                budget = int.MaxValue; Assert.AreEqual(ShellStore.StoreResult.Stored, st.Store(sc, ref budget));
                Assert.AreEqual(((5 << 4) + 10) << 5, st.SolidTopY, "an air chunk adds nothing");
                // A refused chunk (9 dense bricks, pool of 8) adds nothing either.
                Air(new int3(4, 7, 4));
                for (int i = 0; i < 9; i++) sc.BrickUniform[(15 << 4) | i] = false;
                for (int i = 0; i < sc.Mats.Length; i++) sc.Mats[i] = Materials.Stone;
                budget = int.MaxValue; Assert.AreEqual(ShellStore.StoreResult.Refused, st.Store(sc, ref budget));
                Assert.AreEqual(((5 << 4) + 10) << 5, st.SolidTopY, "refused: not raised");
                // A uniform solid chunk at y = 6 raises it to that chunk's top.
                Air(new int3(5, 6, 5));
                for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++) sc.BrickMat[i] = Materials.Stone;
                budget = int.MaxValue; Assert.AreEqual(ShellStore.StoreResult.Stored, st.Store(sc, ref budget));
                Assert.AreEqual(7 << 9, st.SolidTopY, "uniform solid chunk: its top");
            }
        }
        finally { sc.Dispose(); }
    }
}
