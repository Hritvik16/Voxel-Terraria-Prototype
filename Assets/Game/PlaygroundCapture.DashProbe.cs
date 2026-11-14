// Assets/Game/PlaygroundCapture.DashProbe.cs
//
// TASK 198 (DIAGNOSTIC, capture only, `-dashprobe 1` with -worldsurvey): the faint white dashes on
// near-vertical faces. Are they VOXELS (top faces the generator really leaves on a steep face) or the
// RENDERER (a wrong normal / hit)?
//
// The debug views and the debug pixel cannot answer it in a player build today: they need the
// non-diet kernel CSMainFarShade, which fails there ("more uavs (9) than the maximum supported (8)",
// plus a Metal compile error), so debug frames come out as the beauty frame and DebugOut stays 0.
// So this probe is a CPU ORACLE instead. After each pose's FIRST arm shot:
//   1. DASH pixels from the beauty frame itself: a pixel brighter than both its upper and lower
//      neighbours by >= DASH_RATIO (a 1-px horizontal light line);
//   2. the same camera rays marched on the CPU through the GENERATOR's own shell cells
//      (GenerateShellChunk: the bytes the shells store), shell by distance as the shell march does
//      (ShellRayReference: never coarser mid-cell), on the 0.1 m voxel grid. Each pixel gets 4 rays at
//      sub-rows 1/8, 3/8, 5/8, 7/8; it "holds a top face" if any of them stops on a +Y face whose
//      cell above is air (a real tread);
//   3. counts: of the dash pixels, how many hold a real top face, vs random hit pixels (control);
//      the hits' shells / distances / materials; and a CPU top-face map of the densest dash window
//      (ws_<pose>_cputop.png, white = a top face in the pixel, grey = side face, black = no hit) to
//      lay beside the beauty crop.
// Nothing here changes what is drawn. Main thread only; the generator calls use their own scratch.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Mirror;
using VoxelEngine.WorldGen;

public partial class PlaygroundCapture
{
    private const float DASH_RATIO = 1.2f;

    private sealed class GenCells : System.IDisposable
    {
        readonly TerrainGenChunkSource _src;
        readonly Dictionary<long, TerrainGenChunkSource.ShellChunk> _chunks = new Dictionary<long, TerrainGenChunkSource.ShellChunk>();
        long _lastKey = long.MinValue; TerrainGenChunkSource.ShellChunk _last;
        public GenCells(TerrainGenChunkSource src) { _src = src; }
        public int Generated => _chunks.Count;
        public byte Cell(int k, int3 c)
        {
            int3 cc = c >> 7;
            long key = ((long)k << 60) ^ ((long)(cc.x & 0xFFFFF) << 40) ^ ((long)(cc.y & 0xFFFFF) << 20) ^ (cc.z & 0xFFFFF);
            if (key != _lastKey)
            {
                if (!_chunks.TryGetValue(key, out _last))
                {
                    _last = new TerrainGenChunkSource.ShellChunk(Unity.Collections.Allocator.Persistent);
                    _src.GenerateShellChunk(k, cc, _last);
                    _chunks[key] = _last;
                }
                _lastKey = key;
            }
            int3 l = c & 127;
            return _last.Get(l.x, l.y, l.z);
        }
        public void Dispose() { foreach (var s in _chunks.Values) s.Dispose(); _chunks.Clear(); }
    }

    private struct OracleHit { public bool hit, inside; public int k; public float tM; public int3 cell, normal; public byte mat, above; public float3 pos; }

    /// The CPU march: 0.1 m voxel DDA from the camera; shell k by distance (outer bands, metres),
    /// switched to a coarser shell only on entering a new cell of it; a hit is the first non-air cell.
    private static OracleHit OracleMarch(GenCells g, float[] outerM, Vector3 eye, Vector3 dirW, float maxM)
    {
        float3 start = new float3(eye.x, eye.y, eye.z) * 10f;
        float3 dir = math.normalize(new float3(dirW.x, dirW.y, dirW.z));
        dir = new float3(ShellRayReference.SafeAxis(dir.x), ShellRayReference.SafeAxis(dir.y), ShellRayReference.SafeAxis(dir.z));
        int3 step = (int3)math.sign(dir);
        float3 tDelta = math.abs(1f / dir);
        int3 voxel = (int3)math.floor(start);
        float3 tMax = ShellRayReference.ReseedTMax(voxel, start, tDelta, step);
        float t = 0f; int3 normal = int3.zero; int kPrev = -1;
        float tEnd = maxM * 10f;
        while (t < tEnd)
        {
            int kd = 0;
            while (kd < outerM.Length - 1 && t >= outerM[kd] * 10f) kd++;
            if (kPrev >= 0 && kd > kPrev && !ShellRayReference.AtEntry(voxel, normal, step, kd)) kd = kPrev;
            kPrev = kd;
            byte m = g.Cell(kd, voxel >> kd);
            if (m != 0)
            {
                bool entry = ShellRayReference.AtEntry(voxel, normal, step, kd);
                int3 cell = voxel >> kd;
                return new OracleHit { hit = true, inside = !entry, k = kd, tM = t * 0.1f, cell = cell, normal = normal, mat = m,
                                       above = g.Cell(kd, cell + new int3(0, 1, 0)), pos = start + dir * t };
            }
            if (tMax.x < tMax.y && tMax.x < tMax.z) { t = tMax.x; voxel.x += step.x; tMax.x += tDelta.x; normal = new int3(-step.x, 0, 0); }
            else if (tMax.y < tMax.z) { t = tMax.y; voxel.y += step.y; tMax.y += tDelta.y; normal = new int3(0, -step.y, 0); }
            else { t = tMax.z; voxel.z += step.z; tMax.z += tDelta.z; normal = new int3(0, 0, -step.z); }
        }
        return default;
    }

    /// The sun ray from a hit (voxel units), as the GPU shadow ray reads the shells: shell by the sample's
    /// camera distance, and solid cells within 3 of that shell's cells of the origin ignored (no self-shadow
    /// across LOD; ShellRayReference's header). True = it reaches maxM without a solid cell (lit).
    private static bool OracleSunLit(GenCells g, float[] outerM, float3 camera, in OracleHit h, float3 sunDir, float maxM)
    {
        float3 start = h.pos + (float3)h.normal * 0.01f;
        float3 dir = math.normalize(sunDir);
        dir = new float3(ShellRayReference.SafeAxis(dir.x), ShellRayReference.SafeAxis(dir.y), ShellRayReference.SafeAxis(dir.z));
        int3 step = (int3)math.sign(dir);
        float3 tDelta = math.abs(1f / dir);
        int3 voxel = (int3)math.floor(start);
        float3 tMax = ShellRayReference.ReseedTMax(voxel, start, tDelta, step);
        float t = 0f, tEnd = maxM * 10f;
        while (t < tEnd)
        {
            float d = math.length((float3)voxel + 0.5f - camera);
            int kd = 0;
            while (kd < outerM.Length - 1 && d >= outerM[kd] * 10f) kd++;
            if (t >= (float)(3 << kd) && g.Cell(kd, voxel >> kd) != 0) return false;
            if (tMax.x < tMax.y && tMax.x < tMax.z) { t = tMax.x; voxel.x += step.x; tMax.x += tDelta.x; }
            else if (tMax.y < tMax.z) { t = tMax.y; voxel.y += step.y; tMax.y += tDelta.y; }
            else { t = tMax.z; voxel.z += step.z; tMax.z += tDelta.z; }
        }
        return true;
    }

    /// Raymarch.compute's FarSlopeNormal validity, on the far band's CPU texel copy (FarField.Shadow):
    /// false = "a cave or an overhang" (p more than one texel below the lowest / above the highest of the
    /// four texel tops around it), where CubeFilterWeight is 0 and the hit is lit by its own cube face.
    private static bool OracleSlopeHolds(uint[] tex, float3 p, float distVoxels, float3 cam10, float pixelAngle)
    {
        if (tex == null) return true;
        const int USE_R = 440, LT = 1024 * 1024;
        float cheb = math.max(math.abs(p.x - cam10.x), math.abs(p.z - cam10.z));
        float fp = 2f * distVoxels * pixelAngle;
        int L = 0;
        while (L < 4 && ((float)(8 << L) < fp || cheb >= (float)((USE_R - 2) * (8 << L)))) L++;
        float tv = 8 << L;
        if (cheb >= (USE_R - 2) * tv) return false;
        float2 f = p.xz / tv - 0.5f;
        int2 c = (int2)math.floor(f);
        float H(int2 t) => tex[L * LT + ((t.y & 1023) << 10) + (t.x & 1023)] & 0xFFFFu;
        float h00 = H(c), h10 = H(c + new int2(1, 0)), h01 = H(c + new int2(0, 1)), h11 = H(c + new int2(1, 1));
        float lo = math.min(math.min(h00, h10), math.min(h01, h11)), hi = math.max(math.max(h00, h10), math.max(h01, h11));
        return !(p.y < lo - tv || p.y > hi + tv);
    }

    private static bool RealTop(in OracleHit h) => h.hit && !h.inside && h.normal.y == 1 && h.above == 0;

    private IEnumerator DashProbe(Camera cam, ShellStreamer sh, string pose)
    {
        var src = Phase4Bootstrapper.Streamer?.TerrainGenSource;
        if (src == null || sh == null || sh.Layout == null) { L("  [dash] no generator / shells"); yield break; }
        var layout = sh.Layout;
        var outer = new float[layout.ShellCount];
        for (int k = 0; k < outer.Length; k++) outer[k] = layout[k].OuterM;
        yield return new WaitForEndOfFrame();
        Texture2D beauty = ScreenCapture.CaptureScreenshotAsTexture();
        int w = beauty.width, h = beauty.height;
        Color32[] bp = beauty.GetPixels32();
        Destroy(beauty);
        float Lum(Color32 c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        // 1. dash pixels (texture rows: 0 = bottom)
        var dash = new bool[w * h];
        var dashList = new List<int2>();
        for (int y = 1; y < h - 1; y++)
        for (int x = 0; x < w; x++)
        {
            float l = Lum(bp[y * w + x]), la = Lum(bp[(y + 1) * w + x]), lb = Lum(bp[(y - 1) * w + x]);
            if (l > 40f && l >= DASH_RATIO * math.max(la, lb)) { dash[y * w + x] = true; dashList.Add(new int2(x, y)); }
        }
        Vector3 eye = cam.transform.position;
        Ray RayAt(float px, float py) => cam.ScreenPointToRay(new Vector3(px / w * Screen.width, py / h * Screen.height, 0f));
        float[] sub = { 0.125f, 0.375f, 0.625f, 0.875f };
        const float MAXM = 600f;
        using (var g = new GenCells(src))
        {
            float t0 = Time.realtimeSinceStartup;
            // 2./3. dash pixels vs controls
            float3 cam10 = new float3(eye.x, eye.y, eye.z) * 10f;
            float3 sunDir = SunCycle.Direction;
            const float SUNM = 150f;
            uint[] farTex = VoxelEngine.Rendering.FarField.Active?.Shadow;
            float pixAng = LODConfig.PixelAngleRad(cam.fieldOfView, RaymarchFeature.LastDispatchResolution.y > 0 ? RaymarchFeature.LastDispatchResolution.y : h);
            float cubeFullV = RaymarchFeature.CubeFilterFullM * 10f;
            if (farTex == null) L("  [dash] NOTE: no far texel CPU copy (FarField.Shadow): the slope test is not emulated");
            int predTops = 0; bool centreSlope = true;
            int Probe(int2 p, out OracleHit centre, out int litTops, out bool centreLit)
            {
                int tops = 0; centre = default; litTops = 0; centreLit = false; predTops = 0; centreSlope = true;
                for (int s = 0; s < 4; s++)
                {
                    Ray r = RayAt(p.x + 0.5f, p.y + sub[s]);
                    var oh = OracleMarch(g, outer, eye, r.direction, MAXM);
                    bool top = RealTop(oh);
                    bool lit = (top || s == 1) && oh.hit && OracleSunLit(g, outer, cam10, oh, sunDir, SUNM);
                    bool slope = !oh.hit || oh.tM * 10f < cubeFullV || OracleSlopeHolds(farTex, oh.pos, oh.tM * 10f, cam10, pixAng);
                    if (s == 1) { centre = oh; centreLit = lit; centreSlope = slope; }
                    if (top) { tops++; if (lit) { litTops++; if (!slope) predTops++; } }
                }
                return tops;
            }
            var rng = new Unity.Mathematics.Random(198u);
            int nD = math.min(300, dashList.Count);
            int dTop = 0, dHit = 0, dInside = 0, dLit = 0, dPred = 0;
            var shellN = new int[outer.Length]; float dSum = 0f;
            var mats = new Dictionary<int, int>();
            int shown = 0;
            for (int i = 0; i < nD; i++)
            {
                int2 p = dashList[(int)((long)i * dashList.Count / nD)];
                int tops = Probe(p, out var c, out int lt, out bool cl);
                if (lt > 0) dLit++;
                if (predTops > 0) dPred++;
                if (c.hit) { dHit++; shellN[c.k]++; dSum += c.tM; mats.TryGetValue(c.mat, out int mc); mats[c.mat] = mc + 1; }
                if (c.inside) dInside++;
                if (tops > 0) dTop++;
                if (shown++ < 8)
                    L($"  [dash] {pose} dash px ({p.x},{h - 1 - p.y}): sub-rays on a real top face {tops}/4 (sun-lit {lt}, of them slope test FAILS {predTops}); centre ray: hit {(c.hit ? 1 : 0)} " +
                      $"{c.tM:F1} m shell {c.k} cell {c.cell} normal {c.normal} mat {c.mat} above {c.above} inside {(c.inside ? 1 : 0)}");
                if ((i & 15) == 15) yield return null;
            }
            int nC = 0, cTop = 0, cDashFlag = 0, cLit = 0, cPred = 0;
            for (int i = 0; i < 4000 && nC < 300; i++)
            {
                int2 p = new int2(rng.NextInt(0, w), rng.NextInt(1, h - 1));
                int tops = Probe(p, out var c, out int lt, out bool cl);
                if (!c.hit) continue;
                nC++;
                if (tops > 0) cTop++;
                if (lt > 0) cLit++;
                if (predTops > 0) cPred++;
                if (dash[p.y * w + p.x]) cDashFlag++;
                if ((nC & 15) == 15) yield return null;
            }
            string ms = ""; foreach (var kv in mats) ms += $" {kv.Key}:{kv.Value}";
            string ss = ""; for (int k = 0; k < shellN.Length; k++) if (shellN[k] > 0) ss += $" s{k}:{shellN[k]}";
            L($"  [dash] {pose}: frame {w}x{h}, DASH pixels (>= x{DASH_RATIO} brighter than the pixels above and below) {dashList.Count} " +
              $"({100.0 * dashList.Count / (w * h):F2} %)");
            L($"  [dash] {pose} ORACLE: dash pixels probed {nD}: CPU centre ray hits {dHit}, pixel holds a REAL TOP FACE (any of 4 sub-rays) {dTop} " +
              $"({100.0 * dTop / math.max(nD, 1):F0} %), a SUN-LIT real top face {dLit} ({100.0 * dLit / math.max(nD, 1):F0} %), a sun-lit top where the SLOPE TEST FAILS (predicted dash) {dPred} ({100.0 * dPred / math.max(nD, 1):F0} %), inside-start {dInside}; centre hits by shell{ss}, mean {dSum / math.max(dHit, 1):F0} m; mats{ms}");
            L($"  [dash] {pose} CONTROL: random hit pixels {nC}: hold a real top face {cTop} ({100.0 * cTop / math.max(nC, 1):F0} %), a sun-lit one {cLit} ({100.0 * cLit / math.max(nC, 1):F0} %), predicted dash {cPred} ({100.0 * cPred / math.max(nC, 1):F0} %), flagged dash {cDashFlag}");
            // 4. CPU top-face map of 192 x 108 windows: -dashwin "pose:x,y;..." (top-left of the window, screenshot
            //    rows from the top) for this pose, else the densest dash window.
            const int WW = 192, WH = 108;
            var wins = new List<int2>();
            string dw = ArgValue("-dashwin");
            if (!string.IsNullOrEmpty(dw))
                foreach (string item in dw.Split(';'))
                {
                    string[] f = item.Split(':');
                    if (f.Length != 2 || f[0] != pose) continue;
                    string[] xy = f[1].Split(',');
                    if (xy.Length == 2 && int.TryParse(xy[0], out int wx) && int.TryParse(xy[1], out int wy))
                        wins.Add(new int2(math.clamp(wx, 0, w - WW), math.clamp(h - WH - wy, 0, h - WH)));   // -> texture rows
                }
            if (wins.Count == 0)
            {
                int bx0 = 0, by0 = 0, best = -1;
                for (int y0 = 0; y0 + WH <= h; y0 += 27)
                for (int x0 = 0; x0 + WW <= w; x0 += 48)
                {
                    int n = 0;
                    for (int y = y0; y < y0 + WH; y += 1) for (int x = x0; x < x0 + WW; x += 2) if (dash[y * w + x]) n++;
                    if (n > best) { best = n; bx0 = x0; by0 = y0; }
                }
                wins.Add(new int2(bx0, by0));
            }
            for (int wi = 0; wi < wins.Count; wi++)
            {
            int bx = wins[wi].x, by = wins[wi].y;
            var map = new Texture2D(WW, WH, TextureFormat.RGB24, false);
            int wTop = 0, wDash = 0, wBoth = 0, wLit = 0, wLitDash = 0, wPred = 0, wPredDash = 0;
            for (int y = 0; y < WH; y++)
            {
                for (int x = 0; x < WW; x++)
                {
                    int2 p = new int2(bx + x, by + y);
                    int tops = Probe(p, out var c, out int lt, out bool cl);
                    // white = a sun-lit real top face in the pixel, blue = only shadowed top faces, grey = side face
                    // (lit), dark grey = side face in sun shadow, black = no hit
                    // white = PREDICTED DASH (a sun-lit real top face where the slope test fails), cyan = a sun-lit top
                    // the filtered cube light averages away, blue = only shadowed tops, grey = side (lit), dark grey =
                    // side in sun shadow, red tint = the slope test fails at the centre ray (face light), black = no hit
                    Color32 col = !c.hit ? new Color32(0, 0, 0, 255) : predTops > 0 ? new Color32(255, 255, 255, 255)
                                : lt > 0 ? new Color32(0, 200, 200, 255)
                                : tops > 0 ? new Color32(60, 110, 255, 255) : cl ? new Color32(110, 110, 110, 255) : new Color32(45, 45, 45, 255);
                    if (c.hit && predTops == 0 && !centreSlope) col = new Color32((byte)math.min(255, col.r + 90), col.g, col.b, 255);
                    if (c.hit && tops == 0 && c.normal.y == 1) col = new Color32(255, 160, 0, 255);   // a +Y hit that is NOT a real face (orange)
                    map.SetPixel(x, y, col);
                    bool d = dash[p.y * w + p.x];
                    if (tops > 0) wTop++;
                    if (d) wDash++;
                    if (d && tops > 0) wBoth++;
                    if (lt > 0) { wLit++; if (d) wLitDash++; }
                    if (predTops > 0) { wPred++; if (d) wPredDash++; }
                }
                yield return null;
            }
            map.Apply();
            string mapName = $"ws_{pose}_cputop{(wi == 0 ? "" : wi.ToString())}";
            File.WriteAllBytes(Path.Combine(_folder, mapName + ".png"), map.EncodeToPNG());
            Destroy(map);
            L($"  [dash] {pose} WINDOW x {bx}-{bx + WW - 1}, y {h - by - WH}-{h - 1 - by} (top-left rows): dash pixels {wDash}, CPU top-face pixels {wTop}, " +
              $"both {wBoth} (dash pixels holding a top face {100.0 * wBoth / math.max(wDash, 1):F0} %); SUN-LIT top-face pixels {wLit}, of them dash {wLitDash}; " +
              $"dash pixels with a sun-lit top {100.0 * wLitDash / math.max(wDash, 1):F0} %; PREDICTED DASH pixels {wPred}, of them dash {wPredDash} " +
              $"(dash pixels predicted {100.0 * wPredDash / math.max(wDash, 1):F0} %, predicted pixels that are dashes {100.0 * wPredDash / math.max(wPred, 1):F0} %) -> {mapName}.png; " +
              $"{g.Generated} generator chunks, {Time.realtimeSinceStartup - t0:F0} s");
            }
        }
    }
}
