// ==========================================
// Assets/CoreEngine/Rendering/SunLowReference.cs
//
// TASK 232 (performance, owner Q-AN 2026-11-16; supervisor's (a) 2026-11-17): the CPU twin of Raymarch.compute's
// LOW-RES SUN (`-sunres 2|4`; SunHistFetch, the primary kernel's low path, CSSunTrace). Read that block's header
// first; this file restates only what the tests pin.
//
// A pixel's sun visibility comes from, in order:
//   1. its OWN record: last frame's record (one of the 4 reprojection probes) with the same shadow-origin key
//      (ShadowOriginKey(voxel + normal)). Its value is exact, traced at most a few frames ago.
//   2. a PRIOR: no key matched, but >= PRIOR_MIN probes hold a face on the SAME FACE PLANE (SkyShareBad 0, 1 px of
//      footprint) and their values agree within AGREE: the first such probe's value, flagged PRIOR.
//   3. the exact ray INLINE (neighbours disagree = a shadow edge or its penumbra; or none fits).
// Then CSSunTrace, per B x B block: one record (even frames: the first PRIOR one; else the rotating slot's first
// face) is traced exactly and written into every record of the block with the same face (flags cleared). A traced
// prior that was wrong drops the block's other priors on that plane (next frame they find no record and decide anew).
// A value is therefore always some face's EXACT ray, possibly of a neighbour on the same plane that agreed with
// another: never a coarse brick, never a value from another plane.
// ==========================================

using System;
using Unity.Mathematics;

public static class SunLowReference
{
    public const uint FLAG_PRIOR = 0x80000000u;   // SUN_REC_PRIOR
    public const uint FLAG_SHELL = 0x40000000u;   // SUN_REC_SHELL
    public const uint VIS_MASK = 0x3FFFFFFFu;     // SUN_REC_VIS
    public const float AGREE = 1f / 16f;          // SUN_AGREE
    public const uint PRIOR_MIN = 2u;             // SUN_PRIOR_MIN

    /// SunRec {v, a, b}: v = asuint(vis) | flags, (a, b) = SkyReqPack(voxel, normal); b == REQ_NONE = none.
    public struct Record
    {
        public uint v, a, b;
        public bool Has => b != SkyFillReference.REQ_NONE;
        public float Vis => math.asfloat(v & VIS_MASK);
        public uint Flags => v & (FLAG_PRIOR | FLAG_SHELL);
        public void Face(out int3 vox, out int3 n) => SkyFillReference.ReqUnpack(a, b, out vox, out n);
    }

    public static Record None => new Record { v = 0u, a = 0u, b = SkyFillReference.REQ_NONE };

    /// SunRecMake: saturate(vis) with its top two bits masked (always 0 for [0, 1]), flags ORed in.
    public static Record Make(float vis, int3 vox, int3 n, uint flags)
    {
        uint2 ab = SkyFillReference.ReqPack(vox, n);
        return new Record { v = (math.asuint(math.saturate(vis)) & VIS_MASK) | flags, a = ab.x, b = ab.y };
    }

    /// SunShareT: 1 + 2 x voxels per pixel at the face's distance (1 px of footprint, plus one voxel).
    public static float ShareT(int3 v, float3 camVox, float pixelAngle) =>
        1f + 2f * math.length(new float3(v) + 0.5f - camVox) * pixelAngle;

    /// The 4 reprojection probes of SunHistFetch, in its order: the nearest pixel, its x and y neighbours toward the
    /// point, the diagonal. ppf = the point's continuous previous-frame pixel coordinate.
    public static int2[] Probes(float2 ppf)
    {
        int2 p0 = (int2)math.floor(ppf);
        int2 dn = new int2(ppf.x - p0.x >= 0.5f ? 1 : -1, ppf.y - p0.y >= 0.5f ? 1 : -1);
        return new[] { p0, new int2(p0.x + dn.x, p0.y), new int2(p0.x, p0.y + dn.y), p0 + dn };
    }

    /// SunHistFetch: 1 = own record (key match), 2 = prior, 0 = none (trace inline). `hist` = last frame's records
    /// (row-major w x h). The shader walks the probes BACKWARDS without an early return, so the key match and the
    /// prior's value are both the FIRST in probe order; this walks forwards and keeps the first, which is the same.
    public static uint Fetch(Record[] hist, int w, int h, float2 ppf, int3 fv, int3 fn, float shareT, out float vis, out uint flags)
    {
        vis = 1f; flags = 0u;
        uint key = SkyVisReference.OriginKey(fv + fn);
        int2[] probes = Probes(ppf);
        bool keyHit = false; float keyVis = 1f; uint keyFlags = 0u;
        uint nComp = 0u; float pFirst = 1f, pMin = 2f, pMax = -1f;
        for (int q = 0; q < 4; q++)
        {
            int2 pp = probes[q];
            if (pp.x < 0 || pp.y < 0 || pp.x >= w || pp.y >= h) continue;
            Record hv = hist[pp.y * w + pp.x];
            if (!hv.Has) continue;
            hv.Face(out int3 hvV, out int3 hvN);
            if (SkyVisReference.OriginKey(hvV + hvN) == key)
            {
                if (!keyHit) { keyHit = true; keyVis = hv.Vis; keyFlags = hv.v & FLAG_PRIOR; }
            }
            else if (SkyFillReference.ShareBad(fv, fn, hvV, hvN, shareT) == 0)
            {
                if (nComp == 0u) pFirst = hv.Vis;
                pMin = math.min(pMin, hv.Vis); pMax = math.max(pMax, hv.Vis); nComp++;
            }
        }
        if (keyHit) { vis = keyVis; flags = keyFlags; return 1u; }
        if (nComp >= PRIOR_MIN && pMax - pMin <= AGREE) { vis = pFirst; flags = FLAG_PRIOR; return 2u; }
        return 0u;
    }

    /// CSSunTrace's first scan slot for block (bx, by) at `frame` (the sky-fill pass's rotation).
    public static int BlockStart(uint bx, uint by, uint frame, int nPix) => SkyFillReference.BlockStart(bx, by, frame, nPix);

    /// CSSunTrace for one block: picks the record to trace (index into `histW`, or -1), traces it with `trace(voxel,
    /// normal, shellFlag)` and writes it into the block's records of that face; a wrong prior drops the block's other
    /// priors on its plane. Returns the traced record's index (-1 = the block had no face).
    public static int TraceBlock(Record[] histW, int w, int h, uint bx, uint by, int bs, uint frame, Func<int3, float> shareT,
                                 Func<int3, int3, uint, float> trace)
    {
        int bw = 1 << bs, nPix = bw * bw;
        int ox = (int)bx << bs, oy = (int)by << bs;
        int start = BlockStart(bx, by, frame, nPix);
        int rep = -1, repP = -1;
        for (int k = 0; k < nPix; k++)
        {
            int j = (start + k) & (nPix - 1);
            int px = ox + (j & (bw - 1)), py = oy + (j >> bs);
            if (px >= w || py >= h) continue;
            int idx = py * w + px;
            if (!histW[idx].Has) continue;
            if (rep < 0) rep = idx;
            if (repP < 0 && (histW[idx].v & FLAG_PRIOR) != 0u) repP = idx;
        }
        if ((frame & 1u) == 0u && repP >= 0) rep = repP;
        if (rep < 0) return -1;
        Record r0 = histW[rep];
        r0.Face(out int3 v, out int3 n);
        uint shellF = r0.v & FLAG_SHELL;
        float vis = trace(v, n, shellF);
        bool priorBad = (r0.v & FLAG_PRIOR) != 0u && math.abs(r0.Vis - vis) > AGREE;
        Record traced = Make(vis, v, n, shellF);
        for (int k = 0; k < nPix; k++)
        {
            int px = ox + (k & (bw - 1)), py = oy + (k >> bs);
            if (px >= w || py >= h) continue;
            int idx = py * w + px;
            Record r = histW[idx];
            if (!r.Has) continue;
            if (r.a == r0.a && r.b == r0.b && (r.v & FLAG_SHELL) == shellF) histW[idx] = traced;
            else if (priorBad && (r.v & FLAG_PRIOR) != 0u)
            {
                r.Face(out int3 rv, out int3 rn);
                if (SkyFillReference.ShareBad(v, n, rv, rn, shareT(v)) == 0) histW[idx] = None;
            }
        }
        return rep;
    }

    /// One simulated frame over a w x h view: `face(px, py, out v, out n)` says what each pixel shows (false = sky),
    /// `prevPixel(v, n)` where that face's point was last frame (continuous pixel coordinates; the reprojection).
    /// The primary kernel's part writes `histW` from `histR` (inline traces counted in `inline`), then CSSunTrace runs
    /// over every block. `outVis` = what each pixel shaded with this frame (-1 = sky).
    public delegate bool FaceAt(int px, int py, out int3 v, out int3 n);

    public static void Frame(Record[] histR, bool histValid, Record[] histW, float[] outVis, int w, int h, int bs, uint frame,
                             FaceAt face, Func<int3, int3, float2> prevPixel, Func<int3, float> shareT,
                             Func<int3, int3, uint, float> trace, ref int inline, ref int priors)
    {
        for (int py = 0; py < h; py++)
        for (int px = 0; px < w; px++)
        {
            int idx = py * w + px;
            if (!face(px, py, out int3 v, out int3 n)) { histW[idx] = None; outVis[idx] = -1f; continue; }
            float vis = 1f; uint flags = 0u, kind = 0u;
            if (histValid) kind = Fetch(histR, w, h, prevPixel(v, n), v, n, shareT(v), out vis, out flags);
            if (kind == 0u) { vis = trace(v, n, 0u); flags = 0u; inline++; }
            if (kind == 2u) priors++;
            outVis[idx] = vis;
            histW[idx] = Make(vis, v, n, flags);
        }
        int bw = 1 << bs;
        for (int by = 0; by * bw < h; by++)
        for (int bx = 0; bx * bw < w; bx++)
            TraceBlock(histW, w, h, (uint)bx, (uint)by, bs, frame, shareT, trace);
    }
}
