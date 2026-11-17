// ==========================================
// Assets/CoreEngine/Rendering/SkyFillReference.cs
//
// Task 193 (look, owner 2026-11-12; SOFT_SKY_PLAN.md step S2 + a ground-bounce half). The CPU
// twin of Raymarch.compute's SKY FILL (`-skyfill 1|2`, OFF by default). Read that block's header
// first; this file restates only what the tests pin.
//
// THE QUANTITIES, per face (axis normal n at the hit voxel), both cosine-weighted over the half
// of the face's hemisphere they cover:
//   S = share of the SKY half (w.y > 0) whose hard ray (ShadowRayReference.Trace, kSoft 0) reaches
//       the sky. = SkyVisReference's skyVis / its open value (1 for an up face, 0.5 for a side face),
//       so an open side face is 1 (the ambient term already carries the normal's share of sky).
//   B = share of the GROUND half (w.y < 0) that lands on sunlit ground: the ray runs to its first
//       solid voxel; one sun ray from the cell it came through (ShadowRayReference.Trace from that
//       voxel and its entry face). A ray that finds nothing within the clamp counts as open (1), the
//       GPU's "left the window" rule. The sun at or below the horizon: 0.
// Mode 1 uses S alone (down faces: no sky half, 0 -- the vertical probe's answer). Mode 2 mixes
// S and B by the weights of the sky and bounce terms at that normal (Open).
//
// THE ESTIMATE. A side face's cosine direction is FOLDED onto the half being sampled (y -> +|y| or
// -|y|; with n.y = 0 the fold keeps the cosine weight). Mode 2 samples a side face's sky half on even
// frames and its ground half on odd frames (index = frame >> 1 for both). The seed is the surface
// cell's key (Hash(OriginKey)), the direction sequence SkyVisReference's. Each half keeps its own
// running mean: 12-bit value, count capped at 15 (weight 1/n up to 16 samples, then 1/16).
// ==========================================

using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;

public static class SkyFillReference
{
    public const int SKY = 1, SKY_AND_BOUNCE = 2;

    /// The GPU's seed for a surface cell: lowbias32 of its key.
    public static uint Seed(int3 hitVoxel, int3 n) => SkyVisReference.Hash(SkyVisReference.OriginKey(hitVoxel + n));

    /// Which half `frame` samples: 0 = sky, 1 = ground.
    public static int Half(int mode, int3 n, uint frame)
    {
        if (mode != SKY_AND_BOUNCE || n.y > 0) return 0;
        if (n.y < 0) return 1;
        return (int)(frame & 1u);
    }

    /// This frame's direction, folded onto `half` for a side face.
    public static float3 Direction(int mode, int3 n, uint seed, uint frame, int half)
    {
        uint idx = (mode == SKY_AND_BOUNCE && n.y == 0) ? (frame >> 1) : frame;
        float3 d = SkyVisReference.Direction(n, seed, idx);
        if (n.y == 0) d.y = half != 0 ? -math.abs(d.y) : math.abs(d.y);
        return d;
    }

    /// Sky half: 1 = the ray reaches the sky.
    public static float SkySample(ChunkStore store, int3 hitVoxel, int3 n, float3 dir, float3 cameraPosM, float maxDistM)
    {
        if (!(dir.y > 0f)) dir.y = 1e-6f;   // the shader's axis lift (a folded side ray can be exactly level)
        return ShadowRayReference.Trace(store, hitVoxel, n, dir, cameraPosM, kSoft: 0f, maxDistM: maxDistM).occluded ? 0f : 1f;
    }

    /// The first solid voxel along `dir` from the origin cell (hitVoxel + n) and the face the ray entered it
    /// through. found false = nothing within the clamp. A hit in the origin cell itself has entry (0,0,0).
    public static (bool found, int3 voxel, int3 entry, float t) FirstSolid(ChunkStore store, int3 hitVoxel, int3 n, float3 dir,
                                                                  float3 cameraPosM, float maxDistM)
    {
        dir = math.normalize(dir);
        if (math.abs(dir.x) < 1e-6f) dir.x = dir.x >= 0f ? 1e-6f : -1e-6f;
        if (math.abs(dir.y) < 1e-6f) dir.y = dir.y >= 0f ? 1e-6f : -1e-6f;
        if (math.abs(dir.z) < 1e-6f) dir.z = dir.z >= 0f ? 1e-6f : -1e-6f;
        int3 voxel = hitVoxel + n;
        float3 o = new float3(voxel) + 0.5f;
        float maxVox = (maxDistM > 0f ? maxDistM : ShadowRayReference.MaxShadowDistanceM) * 10f;
        int3 step = (int3)math.sign(dir);
        float3 tDelta = math.abs(1f / dir);
        float3 tMax = new float3(
            step.x > 0 ? (voxel.x + 1f - o.x) : (o.x - voxel.x),
            step.y > 0 ? (voxel.y + 1f - o.y) : (o.y - voxel.y),
            step.z > 0 ? (voxel.z + 1f - o.z) : (o.z - voxel.z)) * tDelta;
        float t = 0f;
        int3 entry = int3.zero;
        int steps = 0, maxSteps = ShadowRayReference.MaxSteps;
        while (t < maxVox && steps++ < maxSteps)
        {
            float3 sampleM = (new float3(voxel) + 0.5f) * 0.1f;
            int tier = ShadowRayReference.SelectLODTier(math.distance(sampleM, cameraPosM) * 10f);
            if (ShadowRayReference.SolidAtTier(store, voxel, tier)) return (true, voxel, entry, t);
            if (tMax.x < tMax.y)
            {
                if (tMax.x < tMax.z) { voxel.x += step.x; t = tMax.x; tMax.x += tDelta.x; entry = new int3(-step.x, 0, 0); }
                else                 { voxel.z += step.z; t = tMax.z; tMax.z += tDelta.z; entry = new int3(0, 0, -step.z); }
            }
            else
            {
                if (tMax.y < tMax.z) { voxel.y += step.y; t = tMax.y; tMax.y += tDelta.y; entry = new int3(0, -step.y, 0); }
                else                 { voxel.z += step.z; t = tMax.z; tMax.z += tDelta.z; entry = new int3(0, 0, -step.z); }
            }
        }
        return (false, voxel, entry, t);
    }

    /// The shader's ShadowEntryNormal (the GPU ray leaps, so it re-derives the entry face from t): the axis
    /// whose entry plane the entry point lies closest to. (0,0,0) for t <= 0.
    public static int3 EntryNormal(int3 originCell, float3 dirIn, int3 v, float t)
    {
        if (t <= 0f) return int3.zero;
        float3 d = math.normalize(dirIn);
        if (math.abs(d.x) < 1e-6f) d.x = d.x >= 0f ? 1e-6f : -1e-6f;
        if (math.abs(d.y) < 1e-6f) d.y = d.y >= 0f ? 1e-6f : -1e-6f;
        if (math.abs(d.z) < 1e-6f) d.z = d.z >= 0f ? 1e-6f : -1e-6f;
        float3 p = new float3(originCell) + 0.5f + d * t;
        float3 e = math.abs(p - (new float3(v) + 1f - math.step(0f, d)));
        if (e.x <= e.y && e.x <= e.z) return new int3(d.x > 0f ? -1 : 1, 0, 0);
        if (e.y <= e.z) return new int3(0, d.y > 0f ? -1 : 1, 0);
        return new int3(0, 0, d.z > 0f ? -1 : 1);
    }

    /// Ground half: 1 = the ray lands on sunlit ground (or on nothing), 0 = on shaded ground or the sun is down.
    public static float GroundSample(ChunkStore store, int3 hitVoxel, int3 n, float3 dir, float3 sunDir, float3 cameraPosM,
                                     float maxDistM)
    {
        var g = FirstSolid(store, hitVoxel, n, dir, cameraPosM, maxDistM);
        if (!g.found) return 1f;
        if (!(sunDir.y > 0f) || math.all(g.entry == 0)) return 0f;
        return ShadowRayReference.Trace(store, g.voxel, g.entry, sunDir, cameraPosM, kSoft: 0f, maxDistM: maxDistM).occluded ? 0f : 1f;
    }

    public static float Sample(int half, ChunkStore store, int3 hitVoxel, int3 n, float3 dir, float3 sunDir, float3 cameraPosM,
                               float maxDistM) =>
        half == 0 ? SkySample(store, hitVoxel, n, dir, cameraPosM, maxDistM)
                  : GroundSample(store, hitVoxel, n, dir, sunDir, cameraPosM, maxDistM);

    // ---------------------------------------------------------------------
    // The truth: 1,024-direction midpoint quadratures of S and B
    // ---------------------------------------------------------------------

    private static float HalfQuadrature(int half, ChunkStore store, int3 hitVoxel, int3 n, float3 sunDir, float3 cameraPosM, float maxDistM)
    {
        int side = SkyVisReference.QUADRATURE_SIDE;
        float sum = 0f;
        for (int i = 0; i < side; i++)
        for (int j = 0; j < side; j++)
        {
            float3 d = SkyVisReference.CosineDir(n, new float2((i + 0.5f) / side, (j + 0.5f) / side));
            if (n.y == 0) d.y = half != 0 ? -math.abs(d.y) : math.abs(d.y);
            sum += Sample(half, store, hitVoxel, n, d, sunDir, cameraPosM, maxDistM);
        }
        return sum / (side * side);
    }

    /// S's truth (n.y >= 0 only; a down face has no sky half).
    public static float SkyQuadrature(ChunkStore store, int3 hitVoxel, int3 n, float3 cameraPosM, float maxDistM) =>
        HalfQuadrature(0, store, hitVoxel, n, new float3(0f, 1f, 0f), cameraPosM, maxDistM);

    /// B's truth (n.y <= 0 only; an up face has no ground half).
    public static float GroundQuadrature(ChunkStore store, int3 hitVoxel, int3 n, float3 sunDir, float3 cameraPosM, float maxDistM) =>
        HalfQuadrature(1, store, hitVoxel, n, sunDir, cameraPosM, maxDistM);

    // ---------------------------------------------------------------------
    // The per-pixel record and the mix
    // ---------------------------------------------------------------------

    public struct Record
    {
        public uint key;
        public float s, b;
        public uint cs, cb;

        public static Record Empty => new Record { key = SkyVisReference.NO_KEY };

        /// One half's running mean: 1/n for 16 samples, then 1/16; 12-bit value, count capped at 15.
        public static void Fold(ref float v, ref uint c, float sample)
        {
            uint n = math.min(c + 1u, 16u);
            v += (sample - v) / n;
            v = (uint)math.round(math.saturate(v) * 4095f) / 4095f;
            c = math.min(n, 15u);
        }

        /// Fold one sample of `half` in; a new key restarts both halves.
        public void Accumulate(uint sampleKey, int half, float sample)
        {
            if (sampleKey != key) this = new Record { key = sampleKey };
            if (half == 0) Fold(ref s, ref cs, sample); else Fold(ref b, ref cb, sample);
        }

        /// The GPU's uint2 (x = S 12b | B 12b << 12 | nS << 24 | nB << 28, y = key).
        public uint2 Pack() => new uint2((uint)math.round(s * 4095f) | ((uint)math.round(b * 4095f) << 12) | (cs << 24) | (cb << 28), key);

        public static Record Unpack(uint2 v) => new Record
        {
            key = v.y, s = (v.x & 0xFFFu) / 4095f, b = ((v.x >> 12) & 0xFFFu) / 4095f, cs = (v.x >> 24) & 0xFu, cb = (v.x >> 28) & 0xFu,
        };
    }

    /// The face's openness: mode 1 = S; mode 2 = S and B weighted by the sky and bounce terms at this
    /// normal (ws, wb; the GPU uses the lumas of FarShadeSky(n) and CubeBounceLight(n, sun)). A half
    /// with no sample yet takes the other's value.
    public static float Open(int mode, int3 n, float s, uint cs, float b, uint cb, float ws, float wb)
    {
        if (mode != SKY_AND_BOUNCE) return s;
        if (cs == 0) s = b;
        if (cb == 0) b = s;
        float w = ws + wb;
        return w > 1e-6f ? (ws * s + wb * b) / w : (n.y > 0 ? s : b);
    }

    /// The far-shade kernel's weights with its default look (FarShadeSky's (1 + n.y) / 2 share of a sky of
    /// luma `skyLuma`; CubeBounceLight's sun x bounce x sun height x (1 - n.y)).
    public static (float ws, float wb) Weights(int3 n, float skyLuma, float sunLuma, float bounce, float3 sunDir) =>
        ((0.5f + 0.5f * n.y) * skyLuma, sunLuma * bounce * math.saturate(sunDir.y) * math.saturate(1f - n.y));

    /// What one pixel's history gives after `frames` frames on a fixed surface point (the shader's loop).
    public static float Estimate(int mode, ChunkStore store, int3 hitVoxel, int3 n, int frames, float3 sunDir, float3 cameraPosM,
                                 float maxDistM, float ws, float wb, uint firstFrame = 0)
    {
        var r = Record.Empty;
        uint key = SkyVisReference.OriginKey(hitVoxel + n);
        uint seed = Seed(hitVoxel, n);
        bool any = false;
        for (int k = 0; k < frames; k++)
        {
            uint f = firstFrame + (uint)k;
            if (mode == SKY && n.y < 0) continue;   // no sky half: no sample, 0
            int half = Half(mode, n, f);
            float3 d = Direction(mode, n, seed, f, half);
            r.Accumulate(key, half, Sample(half, store, hitVoxel, n, d, sunDir, cameraPosM, maxDistM));
            any = true;
        }
        return any ? Open(mode, n, r.s, r.cs, r.b, r.cb, ws, wb) : 0f;
    }

    // ---------------------------------------------------------------------
    // TASK 227: LOW-RES SKY FILL (Raymarch.compute block of that name, CSSkyFillTrace; `-skyfillres 2|4`)
    // ---------------------------------------------------------------------

    public const uint REQ_NONE = 0xFFFFFFFFu;
    public const uint PRIOR_N = 8u;   // SKY_PRIOR_N: a neighbour's record counts as at most this many samples

    public static uint NormalIndex(int3 n) => n.x > 0 ? 0u : n.x < 0 ? 1u : n.y > 0 ? 2u : n.y < 0 ? 3u : n.z > 0 ? 4u : 5u;

    public static int3 NormalFromIndex(uint i)
    {
        int s = (i & 1u) != 0u ? -1 : 1;
        return i < 2u ? new int3(s, 0, 0) : i < 4u ? new int3(0, s, 0) : new int3(0, 0, s);
    }

    /// SkyReqPack: the face (hit voxel, normal) in two words; x, z 21 bits, y 18 bits, normal in b's top nibble.
    public static uint2 ReqPack(int3 v, int3 n)
    {
        uint a = ((uint)v.x & 0x1FFFFFu) | (((uint)v.y & 0x7FFu) << 21);
        uint b = ((uint)v.z & 0x1FFFFFu) | ((((uint)v.y >> 11) & 0x7Fu) << 21) | (NormalIndex(n) << 28);
        return new uint2(a, b);
    }

    public static void ReqUnpack(uint a, uint b, out int3 v, out int3 n)
    {
        v.x = ((int)(a << 11)) >> 11;
        v.z = ((int)(b << 11)) >> 11;
        uint y18 = (a >> 21) | (((b >> 21) & 0x7Fu) << 11);
        v.y = ((int)(y18 << 14)) >> 14;
        n = NormalFromIndex(b >> 28);
    }

    /// The plain rule: same normal, same plane (equal coordinate along the normal), tangential Chebyshev distance <= shareT.
    public static bool ShareOk(int3 v, int3 n, int3 rv, int3 rn, float shareT)
    {
        if (math.any(rn != n)) return false;
        int3 d = math.abs(rv - v);
        int along = n.x != 0 ? d.x : n.y != 0 ? d.y : d.z;
        int tang = n.x != 0 ? math.max(d.y, d.z) : n.y != 0 ? math.max(d.x, d.z) : math.max(d.x, d.y);
        return along == 0 && tang <= shareT;
    }

    /// The shader's SkyShareBad (integer arithmetic only; 0 = ShareOk), line for line.
    public static int ShareBad(int3 v, int3 n, int3 rv, int3 rn, float shareT)
    {
        int3 dn = math.abs(rn - n), d = math.abs(rv - v), an = math.abs(n);
        int along = an.x * d.x + an.y * d.y + an.z * d.z;
        int tang = math.max(math.max((1 - an.x) * d.x, (1 - an.y) * d.y), (1 - an.z) * d.z);
        int over = math.max(0, tang - (int)math.floor(shareT));
        return dn.x + dn.y + dn.z + along + over;
    }

    /// SkyShareT: 1 + 2 x block px x voxels per pixel at the face's distance (camera in voxels, pixel angle in rad).
    public static float ShareT(int3 v, float3 camVox, float blockPx, float pixelAngle) =>
        1f + 2f * blockPx * math.length(new float3(v) + 0.5f - camVox) * pixelAngle;

    /// CSSkyFillTrace's first scan slot for block (bx, by) at `frame` (nPix a power of two).
    public static int BlockStart(uint bx, uint by, uint frame, int nPix) =>
        (int)(((frame >> 1) + SkyVisReference.Hash(bx * 0x9E3779B1u ^ by)) & (uint)(nPix - 1));

    /// One pixel of a simulated frame: the face it shows (has = a tier-0 hit with sky fill) and its record.
    public struct Pixel
    {
        public bool has;
        public int3 v, n;
        public Record rec;
        public int samplesFromOtherFaces;   // test bookkeeping: samples folded in from another face of the plane
    }

    /// One frame of the low-res path over one bw x bw block (row-major `px`, a static view: every face keeps its record):
    /// the primary kernel's part (a face with no record is an orphan and traces inline) and CSSkyFillTrace's (one face's
    /// sample, folded into every pixel SkyShareOk accepts). Returns the representative's index, or -1.
    public static int LowResFrame(int mode, ChunkStore store, Pixel[] px, int bw, uint bx, uint by, uint frame, float shareT,
                                  float3 sunDir, float3 cameraPosM, float maxDistM)
    {
        for (int i = 0; i < px.Length; i++)
        {
            if (!px[i].has || (mode == SKY && px[i].n.y < 0)) continue;
            uint key = SkyVisReference.OriginKey(px[i].v + px[i].n);
            if (px[i].rec.key != key) px[i].rec = new Record { key = key };
            if (px[i].rec.cs == 0 && px[i].rec.cb == 0)
            {
                int h0 = Half(mode, px[i].n, frame);
                float3 d0 = Direction(mode, px[i].n, Seed(px[i].v, px[i].n), frame, h0);
                px[i].rec.Accumulate(key, h0, Sample(h0, store, px[i].v, px[i].n, d0, sunDir, cameraPosM, maxDistM));
            }
        }
        int nPix = bw * bw, start = BlockStart(bx, by, frame, nPix), rep = -1;
        for (int k = 0; k < nPix && rep < 0; k++)
        {
            int j = (start + k) & (nPix - 1);
            if (px[j].has && !(mode == SKY && px[j].n.y < 0)) rep = j;
        }
        if (rep < 0) return -1;
        int3 v = px[rep].v, n = px[rep].n;
        int half = Half(mode, n, frame);
        float smp = Sample(half, store, v, n, Direction(mode, n, Seed(v, n), frame, half), sunDir, cameraPosM, maxDistM);
        for (int i = 0; i < px.Length; i++)
        {
            if (!px[i].has || (mode == SKY && px[i].n.y < 0) || ShareBad(v, n, px[i].v, px[i].n, shareT) != 0) continue;
            if (half == 0) Record.Fold(ref px[i].rec.s, ref px[i].rec.cs, smp); else Record.Fold(ref px[i].rec.b, ref px[i].rec.cb, smp);
            if (math.any(px[i].v != v)) px[i].samplesFromOtherFaces++;
        }
        return rep;
    }
}
