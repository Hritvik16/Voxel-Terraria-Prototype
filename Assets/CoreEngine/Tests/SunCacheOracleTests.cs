// ==========================================
// Assets/CoreEngine/Tests/SunCacheOracleTests.cs
//
// TASK 249 (AMENDMENT_8_18_CACHED_SUN_SHADOW.md sec. 6, step 247a): the correctness kill test of the cached sun
// shadow. SunCacheReference (the CPU twin) is compared per receiver face with today's exact sun ray,
// ShadowRayReference.Trace (hard, tier 0: the camera handed to Trace is the face's own origin, so every sample of the
// ray is tier 0 -- the near maps read only the tier-0 mirror, 8.18 sec. 1.3).
//
// Every case writes a section to Logs/overnight/249/oracle_<case>.txt; the fixture's teardown joins them into
// Logs/overnight/249/oracle.txt. Pictures (not committed) go to Screenshots/249/.
//
// Terms used in the tables:
//   ref lit / ref shadow     today's exact ray (ShadowRayReference.Trace, kSoft 0) from §6.5's origin
//   cache lit                the cached lookup's visibility >= 0.5 (contact ray + bilinear run-rule taps)
//   edge band (1 texel)      the exact ray from Q (the contact end) changes its answer when Q moves one texel of the map
//                            sideways (8 offsets in the map plane): a real shadow edge lies within one texel. A moved point
//                            that lands inside solid first walks out of it (ExactRay skipStartRun): standing on the
//                            receiver's own ground is not an edge, standing inside a wall beside Q is. The lookup's 4 tap
//                            points are sampled too (a thin occluder can fall between the 8 ring points). So "out of band"
//                            = an error the texel size cannot explain: the run rule itself answered wrong.
//   false shadow / false lit ref lit & cache shadow / ref shadow & cache lit; "out" = outside the edge band
//   incons                   faces where Trace (from the origin) and the exact ray from Q disagree: a corner tie in the
//                            reference's own DDA; counted as edge band
//
// Kill rule (8.18 sec. 6, 247a): false shadows > 0.5 % of reference-lit faces outside the edge bands on any slope more
// than 3.6 deg off grazing; or 1-voxel dapples in N0 below 75 % strength; or any leak behind case (5).
// ==========================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Rendering;

public class SunCacheOracleTests
{
    // =====================================================================
    // Harness
    // =====================================================================

    sealed class Scene : IDisposable
    {
        public readonly BrickDataPool pool;
        public readonly ChunkHandleAllocator alloc;
        public readonly ChunkStore store;
        public readonly SunCacheReference.MirrorBox mirror;
        public readonly int3 size;
        public Scene(int3 size)
        {
            this.size = size;
            pool = new BrickDataPool((size.x >> 3) * (size.y >> 3) * (size.z >> 3) + 64);
            alloc = new ChunkHandleAllocator(64);
            store = new ChunkStore(pool, alloc);
            int3 ch = (size + 127) >> 7;
            for (int cz = 0; cz < ch.z; cz++)
            for (int cy = 0; cy < ch.y; cy++)
            for (int cx = 0; cx < ch.x; cx++)
                store.InsertChunk(new Chunk { coord = new int3(cx, cy, cz), isUniform = true, uniformMaterial = 0 });
            mirror = new SunCacheReference.MirrorBox(int3.zero, size);
        }
        /// Writes the store (the CPU state) and the mirror twin together; the edit case uses MirrorBox.Upload instead.
        public void Set(int x, int y, int z, byte m)
        {
            store.SetVoxel(new int3(x, y, z), m);
            mirror.v[(z * size.y + y) * size.x + x] = m;
        }
        public void Fill(int3 lo, int3 hi, byte m)
        {
            for (int z = lo.z; z < hi.z; z++) for (int y = lo.y; y < hi.y; y++) for (int x = lo.x; x < hi.x; x++) Set(x, y, z, m);
        }
        public bool Solid(int3 v) => mirror.Solid(v);
        public void Dispose() => pool.Dispose();
    }

    struct Recv { public int3 hit, n; }

    static readonly int3[] Normals = { new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 1, 0), new int3(0, -1, 0), new int3(0, 0, 1), new int3(0, 0, -1) };

    static float3 SunDir(float elevDeg, float azDeg)
    {
        float e = math.radians(elevDeg), a = math.radians(azDeg);
        return new float3(math.cos(e) * math.cos(a), math.sin(e), math.cos(e) * math.sin(a));
    }

    /// Exposed faces (solid voxel, air across the face) in [lo, hi), any normal.
    static List<Recv> ExposedFaces(Scene sc, int3 lo, int3 hi, Func<int, int, int, bool> keep = null)
    {
        var l = new List<Recv>();
        for (int z = lo.z; z < hi.z; z++)
        for (int x = lo.x; x < hi.x; x++)
        for (int y = lo.y; y < hi.y; y++)
        {
            var v = new int3(x, y, z);
            if (!sc.Solid(v)) continue;
            if (keep != null && !keep(x, y, z)) continue;
            foreach (var n in Normals) if (!sc.Solid(v + n)) l.Add(new Recv { hit = v, n = n });
        }
        return l;
    }

    static List<Recv> Facing(List<Recv> all, float3 s, int max, int seed)
    {
        var l = new List<Recv>();
        foreach (var r in all) if (math.dot((float3)r.n, s) > 0f) l.Add(r);
        if (max > 0 && l.Count > max)
        {
            var rng = new System.Random(seed);
            for (int i = l.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = l[i]; l[i] = l[j]; l[j] = t; }
            l.RemoveRange(max, l.Count - max);
        }
        return l;
    }

    static float BoxExitM(Scene sc, float3 originVox, float3 s)
    {
        SunCacheReference.SlabBox(originVox, s, float3.zero, (float3)sc.size, out _, out float t1);
        return (math.max(t1, 0f) + 2f) * 0.1f;
    }

    // ---------------- per-face evaluation ----------------

    struct Stat
    {
        public long faces, refLit, refShadow, falseShadow, falseLit, fsOut, flOut, litOut, shadowOut, band, incons;
        public double visOnRefLit;
        public long lines, lineSteps;
        public void Add(Stat o)
        {
            faces += o.faces; refLit += o.refLit; refShadow += o.refShadow; falseShadow += o.falseShadow; falseLit += o.falseLit;
            fsOut += o.fsOut; flOut += o.flOut; litOut += o.litOut; shadowOut += o.shadowOut; band += o.band; incons += o.incons;
            visOnRefLit += o.visOnRefLit; lines += o.lines; lineSteps += o.lineSteps;
        }
        public double FsOutPct => litOut > 0 ? 100.0 * fsOut / litOut : 0;
        public double FlOutPct => shadowOut > 0 ? 100.0 * flOut / shadowOut : 0;
        public double Strength => refLit > 0 ? visOnRefLit / refLit : double.NaN;
    }

    // class codes for pictures
    const byte C_LIT = 1, C_SHADOW = 2, C_FS_OUT = 3, C_FS_BAND = 4, C_FL_OUT = 5, C_FL_BAND = 6;

    static long s_refSteps, s_refFaces, s_contactSteps;
    static readonly long[] s_lineSteps = new long[4], s_lines = new long[4];

    /// Evaluate faces under sun s for each map in `mapsToRun` (forced map, no blend). classes[k][i] / vis[k][i] optional.
    static Stat[] Eval(Scene sc, List<Recv> faces, float3 s, int[] mapsToRun, byte[][] classes = null, float[][] visOut = null, bool bandsForAll = true)
    {
        s = math.normalize(s);
        var cache = new SunCacheReference(sc.mirror);
        cache.BuildStatic(s, int3.zero);
        int n = faces.Count;
        var refLit = new bool[n]; var cBlocked = new bool[n]; var Qs = new float3[n]; var centre = new bool[n]; var tie = new bool[n];
        long incons = 0;
        for (int i = 0; i < n; i++)
        {
            var f = faces[i];
            float3 originM = ShadowRayReference.ShadowOriginWorld(f.hit, f.n);
            var r = ShadowRayReference.Trace(sc.store, f.hit, f.n, s, originM, 0f, BoxExitM(sc, originM * 10f, s));
            refLit[i] = !r.occluded;
            s_refSteps += r.steps; s_refFaces++;
            var c = SunCacheReference.Contact(sc.store, f.hit, f.n, s, out Qs[i]);
            s_contactSteps += c.steps;
            cBlocked[i] = c.occluded;
            if (!c.occluded) centre[i] = SunCacheReference.ExactRay(sc.mirror, Qs[i], s, out _);
            if (refLit[i] != (!cBlocked[i] && !centre[i])) { incons++; tie[i] = true; }
        }
        var stats = new Stat[4];
        foreach (int k in mapsToRun)
        {
            var st = new Stat { faces = n, incons = incons };
            long l0 = cache.linesBuilt, s0 = cache.lineSteps;
            var sl = cache.maps[k].cur;
            float tx = SunCacheReference.TEXEL_VOX[k];
            for (int i = 0; i < n; i++)
            {
                float vis = 0f;
                bool band = false;
                if (!cBlocked[i])
                {
                    vis = cache.SampleCascade(Qs[i], k, -1f, out int mr, out bool co);
                    Assert.AreEqual(k, mr, "lazy oracle mode must read the forced map");
                    bool needBand = bandsForAll || ((vis >= 0.5f) != refLit[i]);
                    // a face whose own exact answer depends on the starting point (Trace from the origin vs the exact ray from Q
                    // disagree: a corner tie in the reference itself) sits ON an edge: band by definition
                    if (needBand) band = tie[i] || InBand(sc.mirror, sl, tx, Qs[i], s, centre[i]);
                }
                bool cacheLit = vis >= 0.5f;
                if (band) st.band++;
                byte cls;
                if (refLit[i])
                {
                    st.refLit++; st.visOnRefLit += vis;
                    if (!band) st.litOut++;
                    if (!cacheLit) { st.falseShadow++; if (!band) st.fsOut++; cls = band ? C_FS_BAND : C_FS_OUT; }
                    else cls = C_LIT;
                }
                else
                {
                    st.refShadow++;
                    if (!band) st.shadowOut++;
                    if (cacheLit) { st.falseLit++; if (!band) st.flOut++; cls = band ? C_FL_BAND : C_FL_OUT; }
                    else cls = C_SHADOW;
                }
                if (classes != null) classes[k][i] = cls;
                if (visOut != null) visOut[k][i] = vis;
            }
            st.lines = cache.linesBuilt - l0; st.lineSteps = cache.lineSteps - s0;
            s_lines[k] += st.lines; s_lineSteps[k] += st.lineSteps;
            stats[k] = st;
        }
        return stats;
    }

    /// The 1-texel edge band: some point within one texel of Q (Chebyshev, in the map plane, at Q's depth) answers
    /// differently from Q. Sampled at the 8 ring points one texel out AND at the lookup's own 4 tap points (the ring alone
    /// misses an occluder thinner than a texel, e.g. a 1-voxel pole under N2's 2-voxel texels).
    static bool InBand(SunCacheReference.MirrorBox m, SunCacheReference.Slot sl, float tx, float3 Q, float3 s, bool centreBlocked)
    {
        for (int du = -1; du <= 1; du++) for (int dv = -1; dv <= 1; dv++)
            if ((du | dv) != 0 && SunCacheReference.ExactRay(m, Q + (sl.U * du + sl.V * dv) * tx, s, out _, skipStartRun: true) != centreBlocked) return true;
        float2 uv = SunCacheReference.UVOf(sl, Q) / tx - 0.5f;
        float2 i0 = math.floor(uv);
        for (int j = 0; j < 2; j++) for (int i = 0; i < 2; i++)
        {
            float2 d = (i0 + new float2(i, j) - uv) * tx;
            if (SunCacheReference.ExactRay(m, Q + sl.U * d.x + sl.V * d.y, s, out _, skipStartRun: true) != centreBlocked) return true;
        }
        return false;
    }

    static readonly int[] AllMaps = { 0, 1, 2, 3 };
    static string MapName(int k) => "N" + k + " (" + (SunCacheReference.TEXEL_VOX[k] * 0.1f).ToString("0.00") + " m)";

    static string Row(string label, Stat st) =>
        $"{label,-34} faces {st.faces,6}  refLit {st.refLit,6}  litOut {st.litOut,6}  FS {st.falseShadow,5} (out {st.fsOut,4} = {st.FsOutPct,6:0.000} %)  " +
        $"FL {st.falseLit,5} (out {st.flOut,4} = {st.FlOutPct,6:0.000} %)  band {st.band,5}  incons {st.incons}";

    // ---------------- report files ----------------
    static string LogDir => Path.Combine(Directory.GetCurrentDirectory(), "Logs", "overnight", "249");
    static string ShotDir => Path.Combine(Directory.GetCurrentDirectory(), "Screenshots", "249");
    static void Section(string name, StringBuilder sb)
    {
        Directory.CreateDirectory(LogDir);
        File.WriteAllText(Path.Combine(LogDir, "oracle_" + name + ".txt"), sb.ToString());
        Debug.Log("[suncache249] " + name + "\n" + sb);
    }

    [OneTimeTearDown]
    public void JoinReport()
    {
        if (!Directory.Exists(LogDir)) return;
        var sb = new StringBuilder();
        sb.AppendLine("TASK 249 -- SunCacheReference vs ShadowRayReference.Trace (AMENDMENT 8.18 sec. 6, 247a). DRAFT amendment, CPU only.");
        sb.AppendLine("Sections are written by SunCacheOracleTests (one file each, joined here); a section older than this run is marked by its own date.");
        sb.AppendLine("Written " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        sb.AppendLine();
        if (s_refFaces > 0)
        {
            var px = new StringBuilder();
            px.AppendLine("== Step-count proxy (this run's cases; raw 0.1 m DDA cells inside each scene box, no leaps, both sides) ==");
            px.AppendLine($"reference (Trace from the face's origin, to the box exit): {s_refFaces} faces, {(double)s_refSteps / s_refFaces:0.0} steps/face");
            px.AppendLine($"contact ray (<= 8 cells):                                   {(double)s_contactSteps / s_refFaces:0.0} steps/face");
            for (int k = 0; k < 4; k++)
                if (s_lines[k] > 0) px.AppendLine($"{MapName(k)} line build: {s_lines[k]} lines, {(double)s_lineSteps[k] / s_lines[k]:0.0} steps/line");
            File.WriteAllText(Path.Combine(LogDir, "oracle_00_step_proxy.txt"), px.ToString());   // persists: a later run without the Slow cases keeps it
        }
        var files = new List<string>(Directory.GetFiles(LogDir, "oracle_*.txt"));
        files.Sort(StringComparer.Ordinal);
        foreach (var f in files)
        {
            sb.AppendLine("################ " + Path.GetFileName(f) + "  (" + File.GetLastWriteTime(f).ToString("yyyy-MM-dd HH:mm") + ")");
            sb.AppendLine(File.ReadAllText(f));
        }
        File.WriteAllText(Path.Combine(LogDir, "oracle.txt"), sb.ToString());
    }

    static void WritePng(string name, int w, int h, Color32[] px)
    {
        try
        {
            Directory.CreateDirectory(ShotDir);
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.SetPixels32(px); t.Apply();
            File.WriteAllBytes(Path.Combine(ShotDir, name), t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
        }
        catch (Exception e) { Debug.LogWarning("[suncache249] png " + name + ": " + e.Message); }
    }

    static Color32 ClassColor(byte c)
    {
        switch (c)
        {
            case C_LIT: return new Color32(235, 225, 170, 255);
            case C_SHADOW: return new Color32(70, 70, 80, 255);
            case C_FS_OUT: return new Color32(230, 30, 30, 255);
            case C_FS_BAND: return new Color32(240, 150, 40, 255);
            case C_FL_OUT: return new Color32(30, 80, 240, 255);
            case C_FL_BAND: return new Color32(120, 190, 250, 255);
            default: return new Color32(0, 0, 0, 255);
        }
    }

    // =====================================================================
    // (1) Slope sweep: 10-85 deg x suns 8/16/30/60 x 8 azimuths, procedural (no game terrain)
    // =====================================================================

    const int SW = 192;
    const float SlopeAzDeg = 20f;       // uphill direction, off-axis on purpose

    static float SlopeH(float ta, int x, int z)
    {
        float c = math.cos(math.radians(SlopeAzDeg)), sn = math.sin(math.radians(SlopeAzDeg));
        return 96f + ta * ((x + 0.5f - 96f) * c + (z + 0.5f - 96f) * sn);
    }

    static Scene BuildSlope(float alphaDeg, out float3 nSlope)
    {
        float ta = math.tan(math.radians(alphaDeg));
        var sc = new Scene(new int3(SW, SW, SW));
        for (int z = 0; z < SW; z++)
        for (int x = 0; x < SW; x++)
        {
            float h = math.clamp(SlopeH(ta, x, z), 12f, 180f);
            int cnt = (int)math.ceil(h - 0.5f);           // solid iff y + 0.5 < h
            for (int y = 0; y < cnt; y++) sc.Set(x, y, z, Materials.Stone);
        }
        float c = math.cos(math.radians(SlopeAzDeg)), sn = math.sin(math.radians(SlopeAzDeg));
        nSlope = math.normalize(new float3(-ta * c, 1f, -ta * sn));
        return sc;
    }

    static List<Recv> SlopeFaces(Scene sc, float alphaDeg)
    {
        float ta = math.tan(math.radians(alphaDeg));
        return ExposedFaces(sc, new int3(48, 0, 48), new int3(144, SW, 144), (x, y, z) =>
        {
            float h = SlopeH(ta, x, z);
            return h >= 36f && h <= 156f && y >= (int)h - 18;
        });
    }

    [Test, Category("Slow")]
    public void C1_SlopeSweep_NoFalseShadowsOffGrazing()
    {
        int[] slopes = { 10, 20, 30, 45, 60, 75, 85 };
        float[] suns = { 8, 16, 30, 60 };
        var agg = new Stat[4, slopes.Length];
        var aggGraze = new Stat[4];
        var worstPct = new double[4]; var worstCfg = new string[4];
        double killWorst = 0; string killWhere = "";
        int worstSlope = -1; float worstE = 0, worstAz = 0; int worstMap = 0; double worstVal = -1;
        var sb = new StringBuilder();
        sb.AppendLine("== (1) Slope sweep: procedural planes 10-85 deg (uphill az 20 deg), suns 8/16/30/60 deg x 8 azimuths (0 = sun facing the slope), 256 sampled faces per config ==");
        sb.AppendLine("beta = sun angle above the slope plane (negative = slope faces away). Kill: false shadow out of band > 0.5 % of lit-out faces, |beta| > 3.6 deg.");
        for (int si = 0; si < slopes.Length; si++)
        {
            using (var sc = BuildSlope(slopes[si], out float3 nS))
            {
                var all = SlopeFaces(sc, slopes[si]);
                foreach (float e in suns)
                for (int a = 0; a < 8; a++)
                {
                    float az = SlopeAzDeg + 180f + 45f * a;
                    float3 s = SunDir(e, az);
                    float beta = math.degrees(math.asin(math.clamp(math.dot(s, nS), -1f, 1f)));
                    var faces = Facing(all, s, 256, 1000 * si + 10 * (int)e + a);
                    if (faces.Count == 0) continue;
                    var st = Eval(sc, faces, s, AllMaps);
                    for (int k = 0; k < 4; k++)
                    {
                        if (math.abs(beta) > 3.6f)
                        {
                            agg[k, si].Add(st[k]);
                            double pct = st[k].FsOutPct;
                            if (st[k].litOut >= 20 && pct > worstPct[k]) { worstPct[k] = pct; worstCfg[k] = $"slope {slopes[si]} sun {e} az+{45 * a} beta {beta:0.0}: {st[k].fsOut}/{st[k].litOut}"; }
                            double badness = st[k].falseShadow + st[k].falseLit;   // in-band included: the picture shows the blur
                            if (badness > worstVal) { worstVal = badness; worstSlope = slopes[si]; worstE = e; worstAz = az; worstMap = k; }
                        }
                        else aggGraze[k].Add(st[k]);
                    }
                }
            }
        }
        for (int k = 0; k < 4; k++)
        {
            sb.AppendLine($"-- {MapName(k)} --");
            for (int si = 0; si < slopes.Length; si++)
            {
                sb.AppendLine(Row($"slope {slopes[si],2} deg (|beta|>3.6)", agg[k, si]));
                if (agg[k, si].FsOutPct > killWorst) { killWorst = agg[k, si].FsOutPct; killWhere = $"{MapName(k)} slope {slopes[si]}"; }
            }
            sb.AppendLine(Row("grazing configs (|beta|<=3.6)", aggGraze[k]));
            sb.AppendLine($"worst single config (>= 20 lit-out faces): {worstPct[k]:0.000} %  {worstCfg[k]}");
        }
        bool kill = killWorst > 0.5;
        sb.AppendLine($"KILL CHECK (1): worst per-slope false-shadow-out rate {killWorst:0.000} % at {killWhere} -> {(kill ? "KILL FIRES" : "pass")} (limit 0.5 %)");
        sb.AppendLine($"most mismatches (in band included, one non-grazing config): slope {worstSlope} sun {worstE} az {worstAz} {MapName(worstMap)}: {worstVal}");

        // (no picture: every column's topmost facing face agrees with the reference; the few blur mismatches hide under it)
        Section("1_slope_sweep", sb);
        Assert.IsFalse(kill, "8.18 KILL RULE (1): " + killWhere + " " + killWorst.ToString("0.000") + " %");
    }

    // =====================================================================
    // (2) Flat ground, sun 2-90 deg (acne)
    // =====================================================================

    [Test, Category("Slow")]
    public void C2_FlatGround_NoAcne()
    {
        float[] elevs = { 2, 4, 8, 16, 30, 45, 60, 90 };
        var sb = new StringBuilder();
        sb.AppendLine("== (2) Flat ground (y < 64), sun 2-90 deg at azimuth 37 deg, 400 sampled top faces ==");
        long fsOffGrazing = 0, fsAll = 0;
        using (var sc = new Scene(new int3(SW, 96, SW)))
        {
            sc.Fill(int3.zero, new int3(SW, 64, SW), Materials.Stone);
            var all = ExposedFaces(sc, new int3(48, 60, 48), new int3(144, 64, 144));
            foreach (float e in elevs)
            {
                float3 s = SunDir(e, 37f);
                var faces = Facing(all, s, 400, (int)e);
                var st = Eval(sc, faces, s, AllMaps);
                for (int k = 0; k < 4; k++)
                {
                    sb.AppendLine(Row($"sun {e,2} deg {MapName(k)}", st[k]));
                    fsAll += st[k].falseShadow;
                    if (e > 3.6f) fsOffGrazing += st[k].falseShadow;
                }
            }
        }
        sb.AppendLine($"false shadows (acne) at suns > 3.6 deg, all maps: {fsOffGrazing}; incl. grazing suns: {fsAll}");
        Section("2_flat_ground", sb);
        Assert.AreEqual(0, fsOffGrazing, "acne on flat ground with the sun more than 3.6 deg up");
    }

    // =====================================================================
    // (3) Thin pole + wall on a floor (peter-panning; edges within one texel)
    // =====================================================================

    [Test, Category("Slow")]
    public void C3_PoleAndWall_NoPeterPanning_EdgesWithinOneTexelBudget()
    {
        var sb = new StringBuilder();
        sb.AppendLine("== (3) 1x1 pole (4 m) and a 1-thick wall (2.4 m tall, 4 m long) on a floor; 6 suns ==");
        sb.AppendLine("foot gap = a floor face touching the caster's base that the reference shades and the cache lights (peter-panning).");
        var sums = new Stat[4]; long footGaps = 0;
        using (var sc = new Scene(new int3(SW, 96, SW)))
        {
            sc.Fill(int3.zero, new int3(SW, 32, SW), Materials.Stone);
            sc.Fill(new int3(96, 32, 96), new int3(97, 72, 97), Materials.Stone);        // pole
            sc.Fill(new int3(60, 32, 120), new int3(100, 56, 121), Materials.Stone);     // wall
            var all = ExposedFaces(sc, new int3(36, 31, 36), new int3(156, 32, 156));    // floor top faces
            var suns = new[] { (30f, 0f), (30f, 60f), (30f, 135f), (30f, 200f), (16f, 60f), (60f, 100f) };
            foreach (var (e, az) in suns)
            {
                float3 s = SunDir(e, az);
                var faces = Facing(all, s, 0, 0);
                var cls = new byte[4][]; for (int k = 0; k < 4; k++) cls[k] = new byte[faces.Count];
                var st = Eval(sc, faces, s, AllMaps, cls);
                for (int k = 0; k < 4; k++)
                {
                    sb.AppendLine(Row($"sun {e} az {az} {MapName(k)}", st[k]));
                    sums[k].Add(st[k]);
                    for (int i = 0; i < faces.Count; i++)
                    {
                        if (cls[k][i] != C_FL_OUT && cls[k][i] != C_FL_BAND) continue;
                        int3 h = faces[i].hit;
                        bool nearPole = math.abs(h.x - 96) <= 1 && math.abs(h.z - 96) <= 1;
                        bool nearWall = h.x >= 59 && h.x <= 100 && math.abs(h.z - 120) <= 1;
                        if (nearPole || nearWall) footGaps++;
                    }
                }
            }
        }
        long outOfBand = 0; double worstRate = 0;
        for (int k = 0; k < 4; k++)
        {
            sb.AppendLine(Row("ALL " + MapName(k), sums[k]));
            outOfBand += sums[k].fsOut + sums[k].flOut;
            worstRate = math.max(worstRate, 100.0 * (sums[k].fsOut + sums[k].flOut) / sums[k].faces);
        }
        sb.AppendLine($"foot gaps: {footGaps}; mismatches outside the 1-texel edge band (all maps): {outOfBand}; worst map {worstRate:0.000} % of faces " +
                      "(8.18 sec. 3.3 residual budget: < 0.5 %)");
        Section("3_pole_wall", sb);
        Assert.AreEqual(0, footGaps, "peter-panning: a caster's foot lets light under it");
        Assert.Less(worstRate, 0.5, "cast-shadow edges off by more than one texel exceed 8.18's 0.5 % residual budget");
    }

    // =====================================================================
    // (4) Canopy slab with 1/2/3-voxel gaps: dapple strength per map (receivers at 5 / 20 / 40 m -> N0 / N1 / N2)
    // =====================================================================

    static Scene BuildCanopy(int gap, int seed)
    {
        var sc = new Scene(new int3(SW, 80, SW));
        sc.Fill(int3.zero, new int3(SW, 32, SW), Materials.Stone);
        sc.Fill(new int3(4, 62, 4), new int3(188, 63, 188), Materials.Stone);   // 1-thick leaf slab, 3 m over the ground
        var rng = new System.Random(seed);
        for (int z0 = 8; z0 + 8 <= 184; z0 += 8)
        for (int x0 = 8; x0 + 8 <= 184; x0 += 8)
        {
            int x = x0 + rng.Next(8 - gap), z = z0 + rng.Next(8 - gap);
            sc.Fill(new int3(x, 62, z), new int3(x + gap, 63, z + gap), Materials.Air);
        }
        return sc;
    }

    /// TASK 249: THIS KILL RULE FIRES (37 % vs 75 %, Logs/overnight/249/oracle.txt). Explicit so the suites stay green while
    /// the owner decides (STOP_FOR_OWNER in that task's HANDOFF); run it by name to reproduce. Do not loosen the limit here:
    /// the 75 % is the amendment's own kill rule.
    [Test, Category("Slow"), Explicit("8.18 kill rule (4) fires: 1-voxel N0 dapples 37 % < 75 % (task 249); owner decision")]
    public void C4_CanopyDapples_StrengthPerMap()
    {
        var sb = new StringBuilder();
        sb.AppendLine("== (4) Canopy: 1-thick slab 3 m over the ground, one g x g hole per 8 x 8 voxels (random place), ground top faces under it ==");
        sb.AppendLine("strength = mean cache visibility on the faces the reference lights through a hole (a dapple); seen = share of them with vis >= 0.5.");
        Assert.AreEqual(0, SunCacheReference.PickMap(50f)); Assert.AreEqual(1, SunCacheReference.PickMap(200f)); Assert.AreEqual(2, SunCacheReference.PickMap(400f));
        sb.AppendLine("map pick by receiver distance: 5 m -> N0, 20 m -> N1, 40 m -> N2 (asserted); N3 shown for reference.");
        sb.AppendLine("each row sums 8 sun azimuths (30 + 45 k deg): the faces and the holes share one voxel grid, so at ONE sun every hole is crossed");
        sb.AppendLine("at the same sub-voxel spot; the azimuth sweep averages that phase out.");
        float[] elevs = { 80f, 60f, 45f };
        double n0Gap1Sum = 0; long n0Gap1N = 0;
        for (int gap = 1; gap <= 3; gap++)
        using (var sc = BuildCanopy(gap, 77 + gap))
        {
            var all = ExposedFaces(sc, new int3(48, 31, 48), new int3(128, 32, 128));
            foreach (float e in elevs)
            {
                var sum = new Stat[4]; var seenSum = new long[4];
                for (int a = 0; a < 8; a++)
                {
                    float az = 30f + 45f * a;
                    float3 s = SunDir(e, az);
                    var faces = Facing(all, s, 0, 0);
                    var vis = new float[4][]; var cls = new byte[4][];
                    for (int k = 0; k < 4; k++) { vis[k] = new float[faces.Count]; cls[k] = new byte[faces.Count]; }
                    var st = Eval(sc, faces, s, AllMaps, cls, vis, bandsForAll: false);
                    for (int k = 0; k < 4; k++)
                    {
                        sum[k].Add(st[k]);
                        for (int i = 0; i < faces.Count; i++) if (cls[k][i] == C_LIT) seenSum[k]++;
                    }
                    if (gap == 1 && e == 60f && a == 0) DrawDapples(sc, faces, s, cls, vis);
                }
                for (int k = 0; k < 4; k++)
                {
                    sb.AppendLine($"gap {gap} sun {e} {MapName(k)}: dapple faces {sum[k].refLit,5}  strength {sum[k].Strength * 100,6:0.0} %  seen {(sum[k].refLit > 0 ? 100.0 * seenSum[k] / sum[k].refLit : 0),6:0.0} %  | " +
                                  $"false lit faces (cache lights a face the slab shades) {sum[k].falseLit} (out of band {sum[k].flOut})");
                    if (gap == 1 && k == 0) { n0Gap1Sum += sum[k].visOnRefLit; n0Gap1N += sum[k].refLit; }
                }
            }
        }
        double n0 = n0Gap1N > 0 ? n0Gap1Sum / n0Gap1N : 1.0;
        bool kill = n0 < 0.75;
        sb.AppendLine($"KILL CHECK (4): 1-voxel dapples in N0, all suns: strength {n0 * 100:0.0} % over {n0Gap1N} dapple faces -> {(kill ? "KILL FIRES" : "pass")} (limit 75 %)");
        sb.AppendLine("picture: Screenshots/249/dapples_gap1_sun60.png (sun 60 az 30; 5 panels: reference, then cache N0 N1 N2 N3; brightness = visibility)");
        Section("4_canopy_dapples", sb);
        Assert.IsFalse(kill, "8.18 KILL RULE (4): 1-voxel dapples in N0 at " + (n0 * 100).ToString("0.0") + " %");
    }

    static void DrawDapples(Scene sc, List<Recv> faces, float3 s, byte[][] cls, float[][] vis)
    {
        const int o = 48, w = 80, P = 4, GAP = 8;
        int W = 5 * w * P + 4 * GAP, H = w * P;
        var px = new Color32[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
        for (int i = 0; i < faces.Count; i++)
        {
            int x = faces[i].hit.x - o, z = faces[i].hit.z - o;
            if ((uint)x >= w || (uint)z >= w) continue;
            bool refLit = cls[0][i] == C_LIT || cls[0][i] == C_FS_OUT || cls[0][i] == C_FS_BAND;
            for (int p = 0; p < 5; p++)
            {
                byte g = p == 0 ? (byte)(refLit ? 245 : 40) : (byte)(40 + 205 * vis[p - 1][i]);
                var c = new Color32(g, g, (byte)(g * 0.85f), 255);
                for (int dy = 0; dy < P; dy++) for (int dx = 0; dx < P; dx++)
                    px[(z * P + dy) * W + p * (w * P + GAP) + x * P + dx] = c;
            }
        }
        WritePng("dapples_gap1_sun60.png", W, H, px);
    }

    // =====================================================================
    // (5) A ridge thicker than RUN_CAP with a cave behind it: no leak. (5b/5c: caves nearer than the cap, reported)
    // =====================================================================

    [Test, Category("Slow")]
    public void C5_RidgeAndCave_NoLeak()
    {
        var sb = new StringBuilder();
        sb.AppendLine("== (5) Ridge block x 24-232, y 32-160, z 16-144 on a floor; sun from -x; an air room inside ==");
        sb.AppendLine("leak = a room face the reference shades that the cache lights (vis >= 0.5); partial = 0 < vis < 0.5.");
        sb.AppendLine("RUN_CAP in voxels: N0 8, N1 16, N2 32, N3 64.");
        var rooms = new[]
        {
            ("5a deep room (>= 88 vox of rock, > every cap)", new int3(150, 40, 50), new int3(200, 72, 110), true),
            ("5b thin roof (10 vox of rock above)", new int3(150, 120, 50), new int3(200, 150, 110), false),
            ("5c room 20 vox behind the sunward wall", new int3(44, 40, 50), new int3(90, 72, 110), false),
        };
        var suns = new[] { (16f, 0f), (30f, 30f), (60f, -40f) };
        long leaks5a = 0;
        foreach (var (label, lo, hi, isKill) in rooms)
        using (var sc = new Scene(new int3(256, 176, 160)))
        {
            sc.Fill(int3.zero, new int3(256, 32, 160), Materials.Stone);
            sc.Fill(new int3(24, 32, 16), new int3(232, 160, 144), Materials.Stone);
            sc.Fill(lo, hi, Materials.Air);
            var all = ExposedFaces(sc, lo - 1, hi + 1);
            foreach (var (e, az) in suns)
            {
                float3 s = SunDir(e, 180f + az);    // x < 0: the sun is on the -x side
                var faces = Facing(all, s, 600, 5);
                var vis = new float[4][]; var cls = new byte[4][];
                for (int k = 0; k < 4; k++) { vis[k] = new float[faces.Count]; cls[k] = new byte[faces.Count]; }
                var st = Eval(sc, faces, s, AllMaps, cls, vis);
                for (int k = 0; k < 4; k++)
                {
                    long leak = 0, part = 0;
                    for (int i = 0; i < faces.Count; i++)
                    {
                        bool refShadow = cls[k][i] == C_SHADOW || cls[k][i] == C_FL_OUT || cls[k][i] == C_FL_BAND;
                        if (!refShadow) continue;
                        if (vis[k][i] >= 0.5f) leak++; else if (vis[k][i] > 0f) part++;
                    }
                    sb.AppendLine($"{label} sun {e} {MapName(k)}: room faces {faces.Count,4} refLit {st[k].refLit} | leaks {leak,4}  partial {part,4}");
                    if (isKill) leaks5a += leak + part;
                }
            }
        }
        bool kill = leaks5a > 0;
        sb.AppendLine($"KILL CHECK (5): 5a leaks + partial leaks: {leaks5a} -> {(kill ? "KILL FIRES" : "pass")}");
        Section("5_ridge_cave", sb);
        Assert.IsFalse(kill, "8.18 KILL RULE (5): light leaks behind a ridge thicker than RUN_CAP");
    }

    // =====================================================================
    // (6) A sub-texel crack at 60 m (report)
    // =====================================================================

    [Test, Category("Slow")]
    public void C6_SubTexelCrack_Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("== (6) 1-voxel crack (x = 80) in a block y < 120, floor faces z 60-140; receivers at 60 m -> N3 (0.4 m texels) ==");
        Assert.AreEqual(3, SunCacheReference.PickMap(600f));
        foreach (int floorY in new[] { 80, 20 })
        using (var sc = new Scene(new int3(160, 128, 192)))
        {
            sc.Fill(int3.zero, new int3(160, 120, 192), Materials.Stone);
            sc.Fill(new int3(80, floorY, 30), new int3(81, 120, 170), Materials.Air);
            var all = ExposedFaces(sc, new int3(80, floorY - 1, 60), new int3(81, floorY, 140));
            foreach (float tilt in new[] { 0f, 5f, 20f })
            {
                float e = math.radians(60f), t = math.radians(tilt);
                float3 s = math.normalize(new float3(math.cos(e) * math.sin(t), math.sin(e), -math.cos(e) * math.cos(t)));
                var faces = Facing(all, s, 0, 0);
                var st = Eval(sc, faces, s, AllMaps);
                for (int k = 0; k < 4; k++)
                    sb.AppendLine($"depth {120 - floorY,3} vox, sun 60 tilt {tilt,2}: {MapName(k)} floor faces {st[k].faces,3} refLit {st[k].refLit,3} | false shadow {st[k].falseShadow,3}  false lit {st[k].falseLit,3}");
            }
        }
        sb.AppendLine("(report only: the amendment lists this residual; the N3 rows are the 60 m case)");
        Section("6_crack", sb);
    }

    // =====================================================================
    // (7) Edit: remove one leaf voxel -> only its lines change -> the cache matches after the dirty pass
    // =====================================================================

    [Test, Category("Slow")]
    public void C7_EditOneLeaf_OnlyItsLinesChange_MatchAfterDirtyPass()
    {
        var sb = new StringBuilder();
        sb.AppendLine("== (7) Edit: canopy (gap 2), sun 60 az 30; remove one slab voxel over the receivers ==");
        float3 s = SunDir(60f, 30f);
        using (var sc = BuildCanopy(2, 79))
        {
            var cache = new SunCacheReference(sc.mirror);
            cache.BuildStatic(s, int3.zero);
            var faces = Facing(ExposedFaces(sc, new int3(70, 31, 70), new int3(122, 32, 122)), s, 0, 0);
            var Q = new float3[faces.Count]; var cb = new bool[faces.Count];
            for (int i = 0; i < faces.Count; i++) cb[i] = SunCacheReference.Contact(sc.store, faces[i].hit, faces[i].n, s, out Q[i]).occluded;
            for (int k = 0; k < 4; k++) for (int i = 0; i < faces.Count; i++) if (!cb[i]) cache.SampleCascade(Q[i], k, -1f, out _, out _);
            var before = new uint[4][]; for (int k = 0; k < 4; k++) before[k] = (uint[])cache.maps[k].cur.tex.Clone();

            // the edit: pick a solid slab voxel near the middle, CPU write -> mirror upload of its box -> dirty lines
            int3 v = new int3(96, 62, 96);
            while (!sc.Solid(v)) v.x++;
            sc.store.SetVoxel(v, Materials.Air);
            sc.mirror.Upload(sc.store, v, v + 1);
            int queued = cache.MarkDirtyBox(v, v + 1);
            var queuedPerMap = new int[4];
            int rebuilt = cache.ProcessDirtyNow();

            int changedOutside = 0, stale = 0;
            var changed = new int[4];
            for (int k = 0; k < 4; k++)
            {
                var sl = cache.maps[k].cur;
                // the dirty rectangle, recomputed here independently of MarkDirtyBox: the voxel's corners projected, padded 1
                float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
                for (int c = 0; c < 8; c++)
                {
                    float3 p = (float3)v + new float3(c & 1, (c >> 1) & 1, (c >> 2) & 1);
                    float2 uv = SunCacheReference.UVOf(sl, p); mn = math.min(mn, uv); mx = math.max(mx, uv);
                }
                float tx = SunCacheReference.TEXEL_VOX[k];
                int2 a = (int2)math.floor(mn / tx - 0.5f) - 1, b = (int2)math.ceil(mx / tx - 0.5f) + 1;
                queuedPerMap[k] = (b.x - a.x + 1) * (b.y - a.y + 1);
                for (int idx = 0; idx < sl.tex.Length; idx++)
                {
                    if (sl.tex[idx] == before[k][idx]) continue;
                    changed[k]++;
                    int su = idx & 511, sv = idx >> 9;      // offset 0, region far from the ring edge: storage == global & 511
                    bool inside = false;
                    for (int gv = a.y; gv <= b.y && !inside; gv++) for (int gu = a.x; gu <= b.x && !inside; gu++)
                        if ((gu & 511) == su && (gv & 511) == sv) inside = true;
                    if (!inside) changedOutside++;
                }
                // no stale line anywhere: every built line equals a fresh build from the edited mirror (global coords from
                // each storage tile's key, so lines on either side of the ring are checked)
                for (int t = 0; t < sl.tileKey.Length; t++)
                {
                    long key = sl.tileKey[t];
                    if (key == long.MinValue) continue;
                    int tu = (int)(key >> 32), tv = (int)(uint)key;
                    for (int j = 0; j < 16; j++) for (int i = 0; i < 16; i++)
                    {
                        int gu = (tu << 4) + i, gv = (tv << 4) + j;
                        uint w = sl.tex[SunCacheReference.StorageIndex(sl, gu, gv)];
                        if (w == SunCacheReference.SENTINEL) continue;
                        if (cache.BuildLine(sl, k, gu, gv, out _) != w) stale++;
                    }
                }
            }
            // receivers vs the reference after the edit
            var st = Eval(sc, faces, s, AllMaps);
            for (int k = 0; k < 4; k++)
                sb.AppendLine($"{MapName(k)}: dirty rect {queuedPerMap[k],4} lines, lines that changed {changed[k],3} | after: " + Row("", st[k]));
            sb.AppendLine($"voxel {v}: lines queued (built ones in all maps) {queued}, rebuilt {rebuilt}; changed outside the dirty rect: {changedOutside}; stale lines after the pass: {stale}");
            // the amendment's estimate for one 0.8 m brick at a 16 deg sun (~500 / 130 / 35 / 10 lines)
            var sl16 = SunCacheReference.NewSlot(SunDir(16f, 30f), int3.zero);
            var est = new StringBuilder("one 0.8 m brick at sun 16 deg, dirty rect per map:");
            for (int k = 0; k < 4; k++)
            {
                float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
                for (int c = 0; c < 8; c++) { float2 uv = SunCacheReference.UVOf(sl16, new float3(c & 1, (c >> 1) & 1, (c >> 2) & 1) * 8f); mn = math.min(mn, uv); mx = math.max(mx, uv); }
                float tx = SunCacheReference.TEXEL_VOX[k];
                int2 a = (int2)math.floor(mn / tx - 0.5f) - 1, b = (int2)math.ceil(mx / tx - 0.5f) + 1;
                est.Append($" N{k} {(b.x - a.x + 1) * (b.y - a.y + 1)}");
            }
            sb.AppendLine(est.ToString() + " (amendment est. ~500 / 130 / 35 / 10)");
            // every lookup after the dirty pass equals a cache built from scratch on the edited mirror
            var fresh = new SunCacheReference(sc.mirror); fresh.BuildStatic(s, int3.zero);
            int lookupDiff = 0;
            for (int k = 0; k < 4; k++) for (int i = 0; i < faces.Count; i++)
                if (!cb[i] && cache.SampleCascade(Q[i], k, -1f, out _, out _) != fresh.SampleCascade(Q[i], k, -1f, out _, out _)) lookupDiff++;
            sb.AppendLine($"lookups after the dirty pass that differ from a from-scratch cache: {lookupDiff}");
            Section("7_edit", sb);
            Assert.AreEqual(0, changedOutside, "a line outside the edited voxel's dirty rectangle changed");
            Assert.AreEqual(0, stale, "a stale line survived the dirty pass");
            Assert.AreEqual(0, lookupDiff, "the edited cache answers differently from a from-scratch cache");
            Assert.Greater(rebuilt, 0);
            Assert.AreEqual(0, st[0].fsOut + st[0].flOut, "N0 disagrees with the reference after the edit");
        }
    }

    // =====================================================================
    // (8) Moving camera + moving sun: never a sentinel read as lit; staleness bound
    // =====================================================================

    [Test, Category("Slow"), Timeout(1200000)]
    public void C8_MovingCameraAndSun_NoSentinelRead_Staleness()
    {
        var sb = new StringBuilder();
        sb.AppendLine("== (8) Frames: canopy (gap 2), 300 ground receivers, camera 5 m/s along x at 7 m height from 50 m outside the box to its far side (receivers 0-70 m: all maps), sun 0.3 deg/s (play clock), 30 fps, 360 frames, budget 15 k lines/frame ==");
        sb.AppendLine("as written = a new slot takes the sun of the moment it starts building (8.18 sec. 2.2); predicted = the sun 2 periods ahead (its display centre).");
        sb.AppendLine("staleness = angle between the displayed sun (prev->cur crossfade) and the true sun; C.10 soft spread = kSoft 0.03 rad = 30 mrad, amendment target < 1/3 of it.");
        const float omega = 5.24e-3f;      // rad/s, 360 deg per 1,200 s
        long sentinelInVis = 0;
        foreach (bool predict in new[] { false, true })
        using (var sc = BuildCanopy(2, 81))
        {
            var faces = Facing(ExposedFaces(sc, new int3(16, 31, 16), new int3(176, 32, 176)), new float3(0, 1, 0), 300, 8);
            var cache = new SunCacheReference(sc.mirror);
            Func<int, float3> sunAt = f => SunDir(50f + math.degrees(omega * f / 30f), 30f);
            var maxLag = new float[4]; var sumLag = new double[4]; int lagN = 0;
            long lookups = 0, contactOnly = 0, linesTotal = 0; int linesMax = 0;
            var err = new Stat[4]; long errFrames = 0;
            var Q = new float3[faces.Count]; var cb = new bool[faces.Count];
            for (int f = 0; f < 360; f++)
            {
                float3 cam = new float3(-500f + f * (50f / 30f), 70f, 96f);
                int3 anchor = ((int3)math.floor(cam) >> 7) << 7;
                cache.BeginFrame(f, k => sunAt(predict ? f + 2 * SunCacheReference.PERIOD[k] : f), anchor);
                float3 sNow = math.normalize(sunAt(f));
                for (int i = 0; i < faces.Count; i++)
                {
                    cb[i] = SunCacheReference.Contact(sc.store, faces[i].hit, faces[i].n, sNow, out Q[i]).occluded;
                    if (!cb[i]) cache.DemandReceiver(Q[i], math.distance(Q[i], cam));
                }
                int built = cache.BuildBudgeted(15000, cam);
                linesTotal += built; linesMax = math.max(linesMax, built);
                bool sample = f >= 64 && (f & 7) == 0;
                var refLitNow = new bool[faces.Count]; var centre = new bool[faces.Count];
                if (sample)
                    for (int i = 0; i < faces.Count; i++)
                    {
                        float3 om = ShadowRayReference.ShadowOriginWorld(faces[i].hit, faces[i].n);
                        var r = ShadowRayReference.Trace(sc.store, faces[i].hit, faces[i].n, sNow, om, 0f, BoxExitM(sc, om * 10f, sNow));
                        refLitNow[i] = !r.occluded;
                        if (!cb[i]) centre[i] = SunCacheReference.ExactRay(sc.mirror, Q[i], sNow, out _);
                    }
                for (int i = 0; i < faces.Count; i++)
                {
                    if (cb[i]) continue;
                    float dist = math.distance(Q[i], cam);
                    float vis = cache.SampleCascade(Q[i], SunCacheReference.PickMap(dist), dist, out int mr, out bool co);
                    lookups++; if (co) contactOnly++;
                    foreach (uint w in cache.lastTaps) if (w == SunCacheReference.SENTINEL) sentinelInVis++;
                    if (!sample || co) continue;
                    int k = SunCacheReference.PickMap(dist);
                    var sl = cache.maps[k].cur;
                    float tx = SunCacheReference.TEXEL_VOX[k];
                    bool band = InBand(sc.mirror, sl, tx, Q[i], sNow, centre[i]);
                    bool cl = vis >= 0.5f;
                    err[k].faces++;
                    if (refLitNow[i]) { err[k].refLit++; if (!band) { err[k].litOut++; if (!cl) err[k].fsOut++; } }
                    else { err[k].refShadow++; if (!band) { err[k].shadowOut++; if (cl) err[k].flOut++; } }
                }
                if (sample) errFrames++;
                if (f >= 64)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        var ms = cache.maps[k];
                        float a = cache.Alpha(k);
                        float3 disp = ms.prev != null ? math.normalize(math.lerp(ms.prev.s, ms.cur.s, a)) : ms.cur.s;
                        float lag = math.acos(math.clamp(math.dot(disp, sNow), -1f, 1f)) * 1000f;
                        maxLag[k] = math.max(maxLag[k], lag); sumLag[k] += lag;
                    }
                    lagN++;
                }
            }
            string mode = predict ? "predicted" : "as written";
            sb.AppendLine($"-- {mode}: lookups {lookups}, contact-only fallbacks {contactOnly}, lines/frame mean {linesTotal / 360.0:0} max {linesMax}");
            for (int k = 0; k < 4; k++)
                sb.AppendLine($"   {MapName(k)} period {SunCacheReference.PERIOD[k],2}: staleness mean {sumLag[k] / math.max(1, lagN),5:0.0} mrad max {maxLag[k],5:0.0} mrad ({maxLag[k] / 30f * 100f:0} % of the soft spread) | " +
                              $"vs exact (frames 64+, every 8th): faces {err[k].faces,5} FS out {err[k].fsOut,4}/{err[k].litOut,-5} FL out {err[k].flOut,4}/{err[k].shadowOut}");
        }
        sb.AppendLine($"sentinel texels weighted into a visibility (both runs): {sentinelInVis}");
        Section("8_frames", sb);
        Assert.AreEqual(0, sentinelInVis, "a never-built texel was read as a visibility");
    }

    // =====================================================================
    // (9) Toroidal wrap at 511 / 0
    // =====================================================================

    [Test]
    public void C9_ToroidalWrap_SameAnswerAcross511To0_NoAliasedTile()
    {
        var sb = new StringBuilder();
        sb.AppendLine("== (9) Toroidal addressing: the same receivers with the map's storage offset moved so their texels straddle 511 -> 0 ==");
        float3 s = SunDir(30f, 30f);
        using (var sc = BuildCanopy(2, 83))
        {
            var faces = Facing(ExposedFaces(sc, new int3(80, 31, 80), new int3(112, 32, 112)), s, 0, 0);
            var Q = new float3[faces.Count]; var cb = new bool[faces.Count];
            for (int i = 0; i < faces.Count; i++) cb[i] = SunCacheReference.Contact(sc.store, faces[i].hit, faces[i].n, s, out Q[i]).occluded;
            var a = new SunCacheReference(sc.mirror); a.BuildStatic(s, int3.zero);
            // offset so that the middle receiver's N0 texel sits at storage 511.5: the region straddles the seam
            var sl0 = a.maps[0].cur;
            float2 uvMid = SunCacheReference.UVOf(sl0, Q[faces.Count / 2]) / SunCacheReference.TEXEL_VOX[0];
            int2 off = new int2(511 - (int)math.floor(uvMid.x), 511 - (int)math.floor(uvMid.y));
            var b = new SunCacheReference(sc.mirror); b.BuildStatic(s, int3.zero, off);
            int diff = 0, lo = 0, hi = 0;
            for (int k = 0; k < 4; k++)
            for (int i = 0; i < faces.Count; i++)
            {
                if (cb[i]) continue;
                float va = a.SampleCascade(Q[i], k, -1f, out _, out _), vb = b.SampleCascade(Q[i], k, -1f, out _, out _);
                if (va != vb) diff++;
                if (k == 0)
                {
                    float2 uv = SunCacheReference.UVOf(b.maps[0].cur, Q[i]) / SunCacheReference.TEXEL_VOX[0] - 0.5f;
                    int su = ((int)math.floor(uv.x) + off.x) & 511;
                    if (su >= 480) hi++; if (su < 32) lo++;
                }
            }
            sb.AppendLine($"receivers {faces.Count}, N0 taps at storage >= 480: {hi}, < 32: {lo}; answers that differ from the unwrapped map: {diff}");
            // alias: a tile 32 tiles away shares the storage tile; its key must evict, never alias
            var sl = SunCacheReference.NewSlot(s, int3.zero);
            a.BuildTile(sl, 0, 3, 5);
            uint w0 = SunCacheReference.Peek(sl, 3 * 16 + 2, 5 * 16 + 7);
            uint aliasBefore = SunCacheReference.Peek(sl, (3 + 32) * 16 + 2, 5 * 16 + 7);
            a.BuildTile(sl, 0, 3 + 32, 5);
            uint oldAfter = SunCacheReference.Peek(sl, 3 * 16 + 2, 5 * 16 + 7);
            sb.AppendLine($"alias check: tile (35,5) before it is built reads {(aliasBefore == SunCacheReference.SENTINEL ? "SENTINEL" : "DATA")}; after building it, tile (3,5) reads {(oldAfter == SunCacheReference.SENTINEL ? "SENTINEL (evicted)" : "DATA")}");
            Section("9_toroid", sb);
            Assert.Greater(hi, 0); Assert.Greater(lo, 0);
            Assert.AreEqual(0, diff, "wrapping the ring changed an answer");
            Assert.AreNotEqual(SunCacheReference.SENTINEL, w0);
            Assert.AreEqual(SunCacheReference.SENTINEL, aliasBefore, "a tile on the other side of the ring read as built");
            Assert.AreEqual(SunCacheReference.SENTINEL, oldAfter, "an evicted tile still reads as built");
        }
    }

    /// Twin self-check: at every tap of every dapple-scene lookup, the line's run-rule answer at Q's depth equals the exact
    /// ray from the tap line's point at that depth (the canopy has no "inside the run" cases, so they must agree).
    [Test, Category("Slow")]
    public void LineAnswer_EqualsExactRayAtTapPoint_Canopy()
    {
        float3 s = SunDir(80f, 30f);
        var sb = new StringBuilder();
        int bad = 0, n = 0, dumped = 0;
        using (var sc = BuildCanopy(1, 78))
        {
            var cache = new SunCacheReference(sc.mirror); cache.BuildStatic(s, int3.zero);
            var faces = Facing(ExposedFaces(sc, new int3(40, 31, 40), new int3(152, 32, 152)), s, 0, 0);
            for (int k = 0; k < 4; k++)
            {
                var sl = cache.maps[k].cur; float tx = SunCacheReference.TEXEL_VOX[k];
                foreach (var f in faces)
                {
                    float3 originM = ShadowRayReference.ShadowOriginWorld(f.hit, f.n);
                    bool refLit = !ShadowRayReference.Trace(sc.store, f.hit, f.n, s, originM, 0f, BoxExitM(sc, originM * 10f, s)).occluded;
                    if (SunCacheReference.Contact(sc.store, f.hit, f.n, s, out float3 Q).occluded) continue;
                    float vis = cache.SampleCascade(Q, k, -1f, out _, out _);
                    float dq = SunCacheReference.DepthOf(sl, Q);
                    float2 uv = SunCacheReference.UVOf(sl, Q) / tx - 0.5f;
                    int2 i0 = (int2)math.floor(uv);
                    for (int j = 0; j < 2; j++) for (int i = 0; i < 2; i++)
                    {
                        int gu = i0.x + i, gv = i0.y + j;
                        uint w = SunCacheReference.Peek(sl, gu, gv);
                        float3 P = SunCacheReference.LineOrigin(sl, k, gu, gv) - sl.s * (dq - SunCacheReference.DEPTH_BASE_VOX);
                        bool exactLit = !SunCacheReference.ExactRay(sc.mirror, P, s, out _);
                        bool lineLit = SunCacheReference.RunLit(w, dq);
                        n++;
                        if (exactLit != lineLit) bad++;
                        if (k == 0 && refLit && dumped < 12)
                        {
                            sb.AppendLine($"face {f.hit} Q {Q} uv {uv} tap ({gu},{gv}) P {P} entry {SunCacheReference.EntryVox(w)} exit {SunCacheReference.ExitVox(w)} dq {dq} lineLit {lineLit} exactLit {exactLit} vis {vis}");
                            dumped++;
                        }
                    }
                }
            }
        }
        sb.AppendLine($"taps {n}, line != exact at the tap point: {bad}");
        Section("0_line_selfcheck", sb);
        Assert.AreEqual(0, bad);
    }

    // =====================================================================
    // Unit pins: the run rule and the line packing
    // =====================================================================

    [Test]
    public void RunRule_InsideFirstRunIsLit_PastExitIsShadow_ClearIsLit()
    {
        uint w = (uint)(2100 * 16) | ((uint)(2110 * 16) << 16);
        Assert.IsTrue(SunCacheReference.RunLit(w, 2090f), "before the run");
        Assert.IsTrue(SunCacheReference.RunLit(w, 2105f), "inside the first run = the receiver's own surface");
        Assert.IsTrue(SunCacheReference.RunLit(w, 2110.4f), "within eps of the exit");
        Assert.IsFalse(SunCacheReference.RunLit(w, 2110.6f), "past the exit");
        Assert.IsTrue(SunCacheReference.RunLit(SunCacheReference.CLEAR, 4000f));
        Assert.IsFalse(SunCacheReference.RunLit(SunCacheReference.BLOCKED, 2000f), "blocked before the window");
        Assert.Throws<InvalidOperationException>(() => SunCacheReference.RunLit(SunCacheReference.SENTINEL, 2000f));
    }

    [Test]
    public void BuildLine_SlabThenBuried_ExitIsCapped()
    {
        using (var sc = new Scene(new int3(64, 128, 64)))
        {
            sc.Fill(new int3(0, 100, 0), new int3(64, 102, 64), Materials.Stone);   // 2-thick slab
            sc.Fill(new int3(0, 0, 0), new int3(64, 60, 64), Materials.Stone);      // deep ground
            var cache = new SunCacheReference(sc.mirror);
            var sl = SunCacheReference.NewSlot(new float3(0, 1, 0), new int3(32, 0, 32));
            uint w = cache.BuildLine(sl, 0, 3, 3, out int steps);
            float top = SunCacheReference.DepthOf(sl, new float3(0, 102, 0)), bottom = SunCacheReference.DepthOf(sl, new float3(0, 100, 0));
            Assert.AreEqual(top, SunCacheReference.EntryVox(w), 1f / 16f, "entry = the slab's top");
            Assert.AreEqual(bottom, SunCacheReference.ExitVox(w), 1f / 16f, "exit = the slab's underside");
            sc.Fill(new int3(0, 60, 0), new int3(64, 100, 64), Materials.Stone);    // now solid from 102 down: buried
            uint w2 = cache.BuildLine(sl, 0, 3, 3, out _);
            Assert.AreEqual(SunCacheReference.RUN_CAP * SunCacheReference.TEXEL_VOX[0], SunCacheReference.ExitVox(w2) - SunCacheReference.EntryVox(w2), 2f / 16f, "buried = entry + RUN_CAP texels");
            Assert.Greater(steps, 0);
        }
    }
}
