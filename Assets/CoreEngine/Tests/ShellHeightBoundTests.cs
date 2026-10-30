// Assets/CoreEngine/Tests/ShellHeightBoundTests.cs
//
// Task 153 (owner Q97 (a)): the shells' HEIGHT BOUND (ShellStore COLUMN HEIGHTS, Raymarch.compute
// SHELL_HEIGHTBOUND_ON, the CPU twin ShellRayReference.March(heightBound) / HeightLeapBox). A rising
// shadow ray at or above its shell-k column's height crosses the column in one leap. It must change no
// shadow: the same hit or no hit, in the same cell, for every ray the step cap did not end.
// Real ShellStores (shells 1-4, shadow uploads; the GPU's table is read from ShadowHeights) hold a
// synthetic world: a rolling ground and the owner's adversarial cases (generality rule, 2026-10-24):
//   * a PILLAR one cell wide in every shell, 260 m tall, 45 m from the camera: it must raise its column's
//     bound, so rays aimed at it are still blocked after leaping the low columns on the way;
//   * a TOWER added afterwards the way an edit op is (ShellStore.Store(replace: true) of every chunk it
//     touches, then a flush): rays aimed at it miss before and hit after, with the leap on and off.
// A table that lies (every column all air) must be caught: rays then miss the pillar.
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Mirror;
using VoxelEngine.WorldGen;

public class ShellHeightBoundTests
{
    const int GROUND = 16384;
    const int SHELLS = 5;            // shell 0 (no tier 0 here) + shells 1-4
    const int REACH = 3, HOLE = 1;   // boxes +-3 chunks; shells >= 2 have a +-1 hole (the finer shell's box)
    const int PX = 40576, PZ = 40037, PILLAR_TOP = GROUND + 2600;
    // Tower: 128-voxel aligned (whole cells in every shell), 90-102 m west of the camera.
    const int TX0 = 38976, TX1 = 39104, TZ0 = 40128, TZ1 = 40256, TOWER_TOP = GROUND + 1800;

    static float Height(float x, float z) =>
        GROUND + 60f * math.sin(x / 700f) * math.cos(z / 900f) + 15f * math.sin(x / 130f + z / 90f);

    static readonly float3 Cam = new float3(40000.5f, math.floor(Height(40000.5f, 40000.5f)) + 140.5f, 40000.5f);

    static float OuterOf(int k) => 333 << k;   // 2.6 of shell k's chunks: inside the +-3 box, past the +-1 hole

    static byte Cell(int k, int3 c, float ground, bool tower)
    {
        float3 p = ((float3)c + 0.5f) * (1 << k);
        if (c.x == PX >> k && c.z == PZ >> k && p.y < PILLAR_TOP) return Materials.Stone;
        if (tower && p.x >= TX0 && p.x < TX1 && p.z >= TZ0 && p.z < TZ1 && p.y < TOWER_TOP) return Materials.Stone;
        return p.y < ground ? Materials.Stone : Materials.Air;
    }

    sealed class StoresView : ShellRayReference.IHeightView
    {
        public readonly ShellStore[] St = new ShellStore[SHELLS];
        public readonly int[] YLo = new int[SHELLS], YHi = new int[SHELLS];
        public bool Lie;   // the mutation: every column claims to be all air
        public int ShellCount => SHELLS;
        public float OuterVoxels(int k) => OuterOf(k);
        public bool Box(int k, out int3 lo, out int3 hi, out int3 cc, out int hole)
        {
            cc = (int3)math.floor(Cam) >> (7 + k);
            lo = new int3(cc.x - REACH, YLo[k], cc.z - REACH); hi = new int3(cc.x + REACH, YHi[k], cc.z + REACH);
            hole = k == 1 ? -1 : HOLE;
            return St[k] != null;
        }
        public uint Dir(int k, int3 c) => St[k].ShadowDir[St[k].Slot(c)];   // the GPU reads the slot, no tag
        public uint Page(int k, int page, int lb) => St[k].ShadowPages[page * EngineConfig.BRICKS_PER_CHUNK + lb];
        public byte Voxel(int k, int body, int vIdx) => (byte)(St[k].ShadowBricks[body * 128 + (vIdx >> 2)] >> ((vIdx & 3) * 8));
        public bool Tier0Missing(int3 voxel) => false;
        public bool Tier0Holds(int3 voxel) => false;
        public int ColumnHeight(int k, int3 c) => Lie ? int.MinValue : (int)St[k].ShadowHeights[St[k].HeightColumn(c.x, c.z)];
    }

    /// Fill sc with shell k's chunk c (bricks wholly under / over the ground and clear of the features
    /// uniform, the rest cell by cell). h: the ground per cell column of the chunk column.
    static void MakeChunk(TerrainGenChunkSource.ShellChunk sc, int k, int3 c, float[] h, float[] hMin, float[] hMax, bool tower)
    {
        sc.Shell = k; sc.Coord = c; sc.IsUniformAir = false; sc.Redraw = false;
        float cs = 1 << k;
        bool anySolid = false;
        for (int bz = 0; bz < 16; bz++)
        for (int by = 0; by < 16; by++)
        for (int bx = 0; bx < 16; bx++)
        {
            int bi = (bz << 8) | (by << 4) | bx;
            float3 v0 = (float3)((c << 7) + new int3(bx, by, bz) * 8) * cs, v1 = v0 + 8f * cs;
            bool feature = (v0.x <= PX && v1.x > PX && v0.z <= PZ && v1.z > PZ && v0.y < PILLAR_TOP)
                           || (tower && v0.x < TX1 && v1.x > TX0 && v0.z < TZ1 && v1.z > TZ0 && v0.y < TOWER_TOP);
            int col = (bz << 4) | bx;
            int first;
            if (!feature && v0.y + 0.5f * cs >= hMax[col]) first = Materials.Air;
            else if (!feature && v1.y - 0.5f * cs < hMin[col]) first = Materials.Stone;
            else
            {
                first = -1;
                for (int vz = 0; vz < 8; vz++)
                for (int vy = 0; vy < 8; vy++)
                for (int vx = 0; vx < 8; vx++)
                {
                    int x = (bx << 3) + vx, y = (by << 3) + vy, z = (bz << 3) + vz;
                    byte m = Cell(k, (c << 7) + new int3(x, y, z), h[(z << 7) | x], tower);
                    sc.Mats[TerrainGenChunkSource.Index(x, y, z)] = m;
                    if (first == -1) first = m; else if (first != m) first = -2;
                }
            }
            if (first != Materials.Air) anySolid = true;
            sc.BrickUniform[bi] = first >= 0;
            sc.BrickMat[bi] = first >= 0 ? (byte)first : (byte)0;
        }
        sc.IsUniformAir = !anySolid;
    }

    static void Ground(int k, int cx, int cz, float[] h, float[] hMin, float[] hMax)
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
    }

    static void Flush(StoresView v, int k)
    {
        v.Box(k, out int3 lo, out int3 hi, out int3 cc, out int hole);
        v.St[k].SetHeightBox(lo, hi, cc, hole);
        int budget = int.MaxValue;
        Assert.IsTrue(v.St[k].FlushDirectory(ref budget));
    }

    /// Store every chunk of the boxes (tower: false), or redraw the chunks the tower touches (tower: true).
    static void Fill(StoresView v, TerrainGenChunkSource.ShellChunk sc, bool tower)
    {
        var h = new float[128 * 128]; var hMin = new float[256]; var hMax = new float[256];
        for (int k = 1; k < SHELLS; k++)
        {
            v.Box(k, out int3 lo, out int3 hi, out int3 cc, out int hole);
            for (int cz = lo.z; cz <= hi.z; cz++)
            for (int cx = lo.x; cx <= hi.x; cx++)
            {
                int x0 = cx << (7 + k), z0 = cz << (7 + k), w = 128 << k;
                bool touches = x0 < TX1 && x0 + w > TX0 && z0 < TZ1 && z0 + w > TZ0;
                if (tower && !touches) continue;
                Ground(k, cx, cz, h, hMin, hMax);
                for (int cy = lo.y; cy <= hi.y; cy++)
                {
                    if (hole >= 0 && math.all(math.abs(new int3(cx, cy, cz) - cc) <= hole)) continue;   // the finer shell's
                    if (tower && (cy << (7 + k)) >= TOWER_TOP) continue;
                    MakeChunk(sc, k, new int3(cx, cy, cz), h, hMin, hMax, tower);
                    int budget = int.MaxValue;
                    Assert.AreEqual(ShellStore.StoreResult.Stored, v.St[k].Store(sc, ref budget, replace: tower), $"shell {k} chunk ({cx},{cy},{cz})");
                }
            }
            Flush(v, k);
        }
    }

    static StoresView Build(TerrainGenChunkSource.ShellChunk sc)
    {
        ShellStore.ShadowUploads = true;
        var v = new StoresView();
        for (int k = 1; k < SHELLS; k++)
        {
            v.YLo[k] = (GROUND - 300) >> (7 + k);
            v.YHi[k] = (PILLAR_TOP + 600) >> (7 + k);
            int layers = v.YHi[k] - v.YLo[k] + 1;
            v.St[k] = new ShellStore(k, new int3(8, ShellLayout.Pow2Ceil(layers), 8), 40000, 8 * 49 + 16);
        }
        Fill(v, sc, false);
        return v;
    }

    struct Tally { public int Rays, Capped, Hits, LeapHits, Target, LeapTarget, Normals; public long ItersOn, ItersOff, Leaps; }

    /// One shadow ray, the leap on vs off. Returns whether the unleapt march hit the box [tLo, tHi).
    static bool Compare(StoresView v, float3 o, float3 dir, string what, ref Tally s, int3 tLo, int3 tHi)
    {
        const int CAP = 192;
        var off = ShellRayReference.March(v, o, dir, 0f, true, Cam, CAP, 0f);
        var on = ShellRayReference.March(v, o, dir, 0f, true, Cam, CAP, 0f, heightBound: true);
        s.Rays++; s.ItersOn += on.Iters; s.ItersOff += off.Iters; s.Leaps += on.HeightLeaps;
        if (off.End == ShellRayReference.END_CAP) { s.Capped++; return false; }
        bool hit = off.End == ShellRayReference.END_HIT;
        Assert.AreEqual(hit, on.End == ShellRayReference.END_HIT, $"{what}: hit / no hit (off end {off.End}, on end {on.End}, leaps {on.HeightLeaps})");
        if (!hit) return false;
        Assert.AreEqual(off.Shell, on.Shell, what + ": shell");
        Assert.AreEqual(off.Voxel, on.Voxel, what + ": the same cell");
        Assert.AreEqual(off.Mat, on.Mat, what + ": mat");
        if (!off.Normal.Equals(on.Normal)) s.Normals++;
        s.Hits++;
        if (on.HeightLeaps > 0) s.LeapHits++;
        int pad = 8 << off.Shell;
        bool inT = math.all(off.Voxel >= tLo - pad) && math.all(off.Voxel < tHi + pad);
        if (inT) { s.Target++; if (on.HeightLeaps > 0) s.LeapTarget++; }
        return inT;
    }

    static float3 GroundPoint(ref Random rng, float r0, float r1)
    {
        float a = rng.NextFloat(0f, 2f * math.PI), rr = rng.NextFloat(r0, r1);
        float x = Cam.x + rr * math.cos(a), z = Cam.z + rr * math.sin(a);
        return new float3(x, Height(x, z) + 60f, z);
    }

    /// Is a feature up to `top` (voxels) in chunk column `col` of shell k drawn only by the finer shell? True when
    /// every layer of the box holding its top chunk or below is in k's hole (the table skips those: header COLUMN
    /// HEIGHTS; shell 4's pillar column, shell 3's tower top here). The ray comparisons still cover these columns.
    static bool InHole(StoresView v, int k, int2 col, int top)
    {
        v.Box(k, out int3 lo, out int3 hi, out int3 cc, out int hole);
        if (hole < 0 || math.any(math.abs(col - cc.xz) > hole)) return false;
        int yTop = math.min(hi.y, (top - 1) >> (7 + k));
        for (int cy = lo.y; cy <= yTop; cy++) if (math.abs(cy - cc.y) > hole) return false;
        return true;
    }

    [Test]
    public void HeightBound_PillarAndEditTower_SameShadows()
    {
        var sc = new TerrainGenChunkSource.ShellChunk();
        StoresView v = null;
        try
        {
            v = Build(sc);
            // The pillar raised its column in every shell; a ground column did not.
            for (int k = 1; k < SHELLS; k++)
            {
                int3 pc = new int3(PX, 0, PZ) >> (7 + k);
                int hp = (int)v.St[k].ShadowHeights[v.St[k].HeightColumn(pc.x, pc.z)];
                if (!InHole(v, k, pc.xz, PILLAR_TOP)) Assert.GreaterOrEqual(hp, PILLAR_TOP - (1 << k), $"shell {k}: the pillar's column height");
                int3 gc = (int3)math.floor(Cam + new float3(-200f * (1 << k), 0, 0)) >> (7 + k);   // outside every shell's hole
                int hg = (int)v.St[k].ShadowHeights[v.St[k].HeightColumn(gc.x, gc.z)];
                Assert.Less(hg, GROUND + 1000, $"shell {k}: a ground column's height");
                Assert.Greater(hg, GROUND - 1000, $"shell {k}: ... above the ground");
            }

            var rng = new Random(153u);
            var t = new Tally();
            int3 pLo = new int3(PX, GROUND - 1000, PZ), pHi = new int3(PX + 1, PILLAR_TOP, PZ + 1);
            int3 tLo = new int3(TX0, GROUND - 1000, TZ0), tHi = new int3(TX1, TOWER_TOP, TZ1);
            // 1. Suns (20-88 deg) from ground points out to 130 m.
            for (int i = 0; i < 1500; i++)
            {
                float el = rng.NextFloat(0.35f, 1.54f), az = rng.NextFloat(0f, 2f * math.PI);
                var dir = new float3(math.cos(el) * math.cos(az), math.sin(el), math.cos(el) * math.sin(az));
                Compare(v, GroundPoint(ref rng, 5f, 1300f), dir, $"sun ray {i}", ref t, pLo, pHi);
            }
            // 2. Rays aimed at the pillar from all round (the leap must stop at its column).
            int pillarBlocked = 0;
            for (int i = 0; i < 1500; i++)
            {
                var o = GroundPoint(ref rng, 100f, 1500f);
                var target = new float3(PX + 0.5f, rng.NextFloat(GROUND + 600f, PILLAR_TOP - 60f), PZ + 0.5f);
                if (target.y <= o.y + 50f) continue;
                if (Compare(v, o, math.normalize(target - o), $"pillar ray {i}", ref t, pLo, pHi)) pillarBlocked++;
            }
            int leapPillar = t.LeapTarget;
            // 3. The tower is not there yet: rays aimed at it are not blocked by it.
            var towerRays = new List<(float3, float3)>();
            for (int i = 0; i < 800; i++)
            {
                var o = GroundPoint(ref rng, 100f, 1500f);
                var target = new float3(rng.NextFloat(TX0 + 4f, TX1 - 4f), rng.NextFloat(GROUND + 500f, TOWER_TOP - 60f), rng.NextFloat(TZ0 + 4f, TZ1 - 4f));
                if (target.y <= o.y + 50f) continue;
                towerRays.Add((o, math.normalize(target - o)));
            }
            var before = new Tally();
            int towerBefore = 0;
            foreach (var (o, d) in towerRays) if (Compare(v, o, d, "tower ray before", ref before, tLo, tHi)) towerBefore++;
            // 4. The tower goes in as an edit op does: every chunk it touches redrawn, then a flush.
            int3 tc1 = new int3(TX0, 0, TZ0) >> 8;
            int h1 = (int)v.St[1].ShadowHeights[v.St[1].HeightColumn(tc1.x, tc1.z)];
            Fill(v, sc, true);
            for (int k = 1; k < SHELLS; k++)
            {
                int3 tc = new int3(TX0, 0, TZ0) >> (7 + k);
                if (!InHole(v, k, tc.xz, TOWER_TOP))
                    Assert.GreaterOrEqual((int)v.St[k].ShadowHeights[v.St[k].HeightColumn(tc.x, tc.z)], TOWER_TOP - (1 << k), $"shell {k}: the redraw raised the tower's column");
            }
            var after = new Tally();
            int towerAfter = 0;
            foreach (var (o, d) in towerRays) if (Compare(v, o, d, "tower ray after", ref after, tLo, tHi)) towerAfter++;
            // 5. The mutation: a table that says every column is air lets rays through the pillar.
            v.Lie = true;
            int lieMisses = 0, lieRays = 0;
            var r2 = new Random(1530u);
            for (int i = 0; i < 1500; i++)
            {
                var o = GroundPoint(ref r2, 100f, 1500f);
                var target = new float3(PX + 0.5f, r2.NextFloat(GROUND + 600f, PILLAR_TOP - 60f), PZ + 0.5f);
                if (target.y <= o.y + 50f) continue;
                var d = math.normalize(target - o);
                var off = ShellRayReference.March(v, o, d, 0f, true, Cam, 192, 0f);
                var on = ShellRayReference.March(v, o, d, 0f, true, Cam, 192, 0f, heightBound: true);
                lieRays++;
                if (off.End == ShellRayReference.END_HIT && on.End != ShellRayReference.END_HIT) lieMisses++;
            }
            v.Lie = false;
            UnityEngine.Debug.Log($"[ShellHeightBound] {t.Rays} rays ({t.Capped} capped), {t.Hits} hits identical ({t.LeapHits} after a height leap, " +
                                  $"{t.Normals} normals differ); pillar blocks {pillarBlocked} ({leapPillar} after a leap); height leaps {t.Leaps:N0}; " +
                                  $"iterations {t.ItersOn:N0} vs {t.ItersOff:N0} ({(t.ItersOff > 0 ? 100.0 * t.ItersOn / t.ItersOff : 0):F0}%); " +
                                  $"tower rays {towerRays.Count}: blocked by it {towerBefore} before the redraw, {towerAfter} after ({after.LeapTarget} after a leap); " +
                                  $"shell-1 tower column height {h1} -> {(int)v.St[1].ShadowHeights[v.St[1].HeightColumn(tc1.x, tc1.z)]}; " +
                                  $"lying table: {lieMisses} of {lieRays} pillar rays leak");
            Assert.Greater(t.Leaps, 2000, "the leap is used");
            Assert.Less(t.ItersOn, t.ItersOff, "and saves iterations");
            Assert.Greater(pillarBlocked, 300, "rays aimed at the pillar are blocked by it");
            Assert.Greater(leapPillar, 100, "... many after leaping low columns on the way");
            Assert.AreEqual(0, towerBefore, "no tower before the redraw");
            Assert.Greater(towerAfter, 200, "the redrawn tower blocks the rays aimed at it");
            Assert.Greater(after.LeapTarget, 50, "... many after a height leap");
            Assert.Greater(lieMisses, 50, "a lying table is caught (the test can see a wrong bound)");
            Assert.AreEqual(0, t.Normals + after.Normals, "hit normals identical");
        }
        finally
        {
            sc.Dispose();
            ShellStore.ShadowUploads = false;
            if (v != null) for (int k = 1; k < SHELLS; k++) v.St[k]?.Dispose();
        }
    }

    /// The table's rules on one small store: absent counts as solid to its top, the hole is skipped, a redraw
    /// raises and lowers it at the flush (not before), the table and directory go up together or not at all,
    /// columns outside the box are HEIGHT_NONE, and a moved box rebuilds it.
    [Test]
    public void HeightTable_Rules()
    {
        const int K = 2, CS = K + 7;
        ShellStore.ShadowUploads = true;
        var sc = new TerrainGenChunkSource.ShellChunk();
        try
        {
            using (var st = new ShellStore(K, new int3(8, 8, 8), 64, 8))
            {
                var lo = new int3(10, 0, 10); var hi = new int3(12, 3, 12); var cc = new int3(11, 1, 11);
                int H(int cx, int cz) => (int)st.ShadowHeights[st.HeightColumn(cx, cz)];
                void Air(int3 c) { sc.Shell = K; sc.Coord = c; sc.IsUniformAir = true; for (int i = 0; i < 4096; i++) { sc.BrickUniform[i] = true; sc.BrickMat[i] = 0; } }
                void Solid(int3 c, int topBrick)   // bricks y < topBrick stone (uniform), the rest air: a paged chunk
                {
                    sc.Shell = K; sc.Coord = c; sc.IsUniformAir = topBrick <= 0;
                    for (int i = 0; i < 4096; i++) { sc.BrickUniform[i] = true; sc.BrickMat[i] = ((i >> 4) & 15) < topBrick ? Materials.Stone : (byte)0; }
                }
                int budget;
                void Put(bool replace = false) { budget = int.MaxValue; Assert.AreEqual(ShellStore.StoreResult.Stored, st.Store(sc, ref budget, replace)); }
                bool Flush() { st.SetHeightBox(lo, hi, cc, 0); budget = int.MaxValue; return st.FlushDirectory(ref budget); }

                Assert.AreEqual(ShellStore.HEIGHT_NONE, H(10, 10), "never flushed with a box: HEIGHT_NONE");
                // Column (10, 10): chunks y 0-3, y 1 held solid to brick 5, y 0, 2 held air, y 3 NOT held.
                Air(new int3(10, 0, 10)); Put(); Solid(new int3(10, 1, 10), 5); Put(); Air(new int3(10, 2, 10)); Put();
                Assert.IsTrue(Flush());
                Assert.AreEqual(4 << CS, H(10, 10), "an absent chunk counts as solid to its top");
                Air(new int3(10, 3, 10)); Put(); Assert.IsTrue(Flush());
                Assert.AreEqual(((1 << 4) + 5) << (K + 3), H(10, 10), "all held: the top of the highest non-air brick");
                // Column (11, 11) is the hole's (hole 0: only chunk (11, 1, 11)); its other chunks air.
                for (int y = 0; y <= 3; y++) if (y != 1) { Air(new int3(11, y, 11)); Put(); }
                Assert.IsTrue(Flush());
                Assert.AreEqual(int.MinValue, H(11, 11), "the hole's chunk is skipped; the rest is air");
                Assert.AreEqual(4 << CS, H(12, 12), "an empty column: all absent");
                Assert.AreEqual(ShellStore.HEIGHT_NONE, H(14, 14), "outside the box");
                // A redraw (an edit op) taller: raised at the flush, not before.
                Solid(new int3(10, 2, 10), 9); Put(replace: true);
                Assert.AreEqual(((1 << 4) + 5) << (K + 3), H(10, 10), "not before the flush");
                Assert.IsTrue(st.DirectoryDirty);
                budget = 100; st.SetHeightBox(lo, hi, cc, 0);
                Assert.IsFalse(st.FlushDirectory(ref budget), "over budget: neither goes up");
                Assert.AreEqual(((1 << 4) + 5) << (K + 3), H(10, 10), "... the table either");
                Assert.IsTrue(Flush());
                Assert.AreEqual(((2 << 4) + 9) << (K + 3), H(10, 10), "raised by the redraw");
                // And lower again: the redraw back to air lowers it.
                Air(new int3(10, 2, 10)); Put(replace: true); Assert.IsTrue(Flush());
                Assert.AreEqual(((1 << 4) + 5) << (K + 3), H(10, 10), "lowered by a lower redraw");
                // An eviction: absent again, solid to its top.
                st.Evict(new int3(10, 3, 10)); Assert.IsTrue(Flush());
                Assert.AreEqual(4 << CS, H(10, 10), "evicted: counts as solid");
                // A moved box: the table follows it (column 10 out, 13 in).
                lo = new int3(11, 0, 10); hi = new int3(13, 3, 12);
                Assert.IsTrue(Flush());
                Assert.AreEqual(4 << CS, H(13, 10), "a new box column with nothing held");
                Assert.AreEqual(ShellStore.HEIGHT_NONE, H(10, 10), "outside the moved box");
                // A slot that holds another chunk (ring alias) counts as solid: (18, 3, 10) takes (10, 3, 10)'s slot.
                lo = new int3(10, 0, 10); hi = new int3(12, 3, 12);
                Air(new int3(10, 3, 10)); Put(); Assert.IsTrue(Flush());
                Assert.AreEqual(((1 << 4) + 5) << (K + 3), H(10, 10), "held again");
                Air(new int3(18, 3, 10)); Put(); Assert.IsTrue(Flush());
                Assert.AreEqual(4 << CS, H(10, 10), "the aliased slot reads as solid to the chunk's top");
            }
        }
        finally { sc.Dispose(); ShellStore.ShadowUploads = false; }
    }
}
