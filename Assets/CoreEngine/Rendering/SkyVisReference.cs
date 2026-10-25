// ==========================================
// Assets/CoreEngine/Rendering/SkyVisReference.cs
//
// Task 147 (SOFT_SKY_PLAN.md step S1, owner Q94 plan A). The CPU oracle for the
// SOFT SKY term: a graded sky-visibility fraction per surface point, replacing
// the yes/no vertical probe (Raymarch.compute's AMBIENT SKY VISIBILITY block).
// No GPU code uses this yet; S2 ports it behind `-softsky 1`, OFF.
//
// THE QUANTITY (what "exact" means here). For a face with axis normal n at the
// hit voxel, the sky visibility is the cosine-weighted fraction of the face's
// hemisphere whose ray reaches the sky:
//
//     skyVis = (1/pi) * Integral over the hemisphere of  V(w) * dot(n, w) dw
//     V(w)   = 1  iff  w.y > 0  AND  the hard shadow ray along w
//                     (ShadowRayReference.Trace, kSoft 0: §6.5's origin rule,
//                     the 8.2 tier rule, the same DDA) is not occluded;
//              0  otherwise.
//
// BELOW THE HORIZON IS GROUND, NOT SKY (w.y <= 0 -> V = 0, no ray). This is the
// sky view factor, nothing tuned: a face on open ground is 1, a vertical face
// with nothing in front of it is exactly 0.5, a down face is exactly 0 (the
// same answer as today's vertical probe, whose own voxel blocks it). It also
// keeps the answer independent of what lies under the window. FLAGGED in task
// 147's HANDOFF: the plan's table expected arch / ledge UNDERSIDES at ~0.2-0.5;
// under this definition they are 0 -- lighting them is bounce light, not sky.
// Do NOT port TraceShadowRay's y >= 1e-6 lift for these directions: it turns
// "ground" into "horizontal ray", and the report's lift column shows what that
// would do. Quadrature(..., liftBelowHorizon: true) exists only for that column.
//
// THE ESTIMATE (what S2's shader does per pixel per frame): ONE direction
// Direction(n, seed, frame), one 0/1 Sample, folded into a History keyed on the
// surface point (ShadowOriginKey of voxel + normal): a running mean with weight
// 1/n for the first SETTLE_SAMPLES samples, then a fixed 1/SETTLE_SAMPLES. A key
// change restarts the count. Exactly 0 and exactly 1 stay exact through it.
//
// THE DIRECTION SEQUENCE. Index -> (u, v) is integer only and is the bit-exact
// GPU contract: a per-seed Cranley-Patterson rotation (lowbias32 hash) of the
// R2 rank-1 lattice (0.32 fixed point), any 16 consecutive indices being a
// rotated copy of the first 16 (stratified at every window). (u, v) take 23 bits
// plus a half-step, so both lie strictly inside (0, 1) and every float step is
// exact. (u, v) -> direction is the cosine map (sqrt / sincos): the GPU matches
// it to a few ulps, not bit for bit (Metal's sqrt/sincos are not IEEE-rounded).
//
// THE TRUTH. Quadrature: a 32 x 32 midpoint grid on (u, v) through the same
// cosine map = 1,024 directions, each weight 1/1024.
// ==========================================

using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;

public static class SkyVisReference
{
    /// Running-mean length: weight 1/n up to here, then fixed 1/SETTLE_SAMPLES.
    public const int SETTLE_SAMPLES = 16;

    /// The quadrature grid edge: QUADRATURE_SIDE^2 = 1,024 directions.
    public const int QUADRATURE_SIDE = 32;

    /// R2 lattice steps in 0.32 fixed point: 1/g and 1/g^2, g = 1.3247179572 (plastic number).
    public const uint R2_A = 0xC13FA9A9u;
    public const uint R2_B = 0x91E10DA6u;

    /// History key meaning "nothing stored" (ShadowOriginKey never produces it: bit 31 is clear).
    public const uint NO_KEY = 0xFFFFFFFFu;

    // ---------------------------------------------------------------------
    // Keys and seeds
    // ---------------------------------------------------------------------

    /// CPU twin of Raymarch.compute's ShadowOriginKey: the origin cell (voxel + normal),
    /// x and z 11 bits, y 9 bits, bit 31 clear.
    public static uint OriginKey(int3 c) =>
        ((uint)c.x & 0x7FFu) | (((uint)c.y & 0x1FFu) << 11) | (((uint)c.z & 0x7FFu) << 20);

    /// lowbias32 (C. Wellons): the integer hash behind the per-seed rotation.
    public static uint Hash(uint x)
    {
        x ^= x >> 16; x *= 0x7FEB352Du;
        x ^= x >> 15; x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
    }

    /// The per-pixel seed S2 uses (pixel x | y << 16; widths below 65,536).
    public static uint PixelSeed(uint2 px) => Hash(px.x | (px.y << 16));

    // ---------------------------------------------------------------------
    // The direction sequence
    // ---------------------------------------------------------------------

    /// Index -> (u, v), integer only: the bit-exact part of the GPU contract.
    public static float2 SamplePoint(uint seed, uint index)
    {
        uint r1 = Hash(seed), r2 = Hash(r1);
        uint ux = unchecked(index * R2_A + r1);
        uint vx = unchecked(index * R2_B + r2);
        const float inv23 = 1f / 8388608f;
        return new float2(((ux >> 9) + 0.5f) * inv23, ((vx >> 9) + 0.5f) * inv23);
    }

    /// Cosine-weighted map of (u, v) onto the hemisphere of axis normal `n`.
    /// The tangent frame is axis-aligned, so it adds no rounding.
    public static float3 CosineDir(int3 n, float2 uv)
    {
        float r = math.sqrt(uv.x), z = math.sqrt(1f - uv.x);
        math.sincos(2f * math.PI * uv.y, out float s, out float c);
        float a = r * c, b = r * s;
        if (n.y != 0) return new float3(a, z * n.y, b);
        if (n.x != 0) return new float3(z * n.x, a, b);
        return new float3(a, b, z * n.z);
    }

    /// Direction for frame `index` at seed `seed` (S2: seed = PixelSeed(pixel), index = frame).
    public static float3 Direction(int3 n, uint seed, uint index) => CosineDir(n, SamplePoint(seed, index));

    // ---------------------------------------------------------------------
    // One sample, the quadrature, the running mean
    // ---------------------------------------------------------------------

    /// V(w): 1 = this direction reaches the sky, 0 = blocked or below the horizon.
    /// `maxDistM` <= 0 = the shipped clamp (ShadowRayReference.MaxShadowDistanceM).
    public static float Sample(ChunkStore store, int3 hitVoxel, int3 n, float3 dir, float3 cameraPosM,
                               float maxDistM = 0f, bool liftBelowHorizon = false)
    {
        if (!(dir.y > 0f))
        {
            if (!liftBelowHorizon) return 0f;
            dir.y = 1e-6f;                      // report column only (see the header)
        }
        return ShadowRayReference.Trace(store, hitVoxel, n, dir, cameraPosM, kSoft: 0f, maxDistM: maxDistM).occluded ? 0f : 1f;
    }

    /// The truth: the 1,024-direction midpoint quadrature of skyVis.
    public static float Quadrature(ChunkStore store, int3 hitVoxel, int3 n, float3 cameraPosM,
                                   float maxDistM = 0f, bool liftBelowHorizon = false)
    {
        int open = 0;
        for (int i = 0; i < QUADRATURE_SIDE; i++)
        for (int j = 0; j < QUADRATURE_SIDE; j++)
        {
            float2 uv = new float2((i + 0.5f) / QUADRATURE_SIDE, (j + 0.5f) / QUADRATURE_SIDE);
            if (Sample(store, hitVoxel, n, CosineDir(n, uv), cameraPosM, maxDistM, liftBelowHorizon) > 0f) open++;
        }
        return open / (float)(QUADRATURE_SIDE * QUADRATURE_SIDE);
    }

    /// One pixel's history: the running mean of its samples, keyed on the surface point.
    public struct History
    {
        public uint key;
        public float vis;
        public int count;

        public static History Empty => new History { key = NO_KEY, vis = 0f, count = 0 };

        /// Fold one 0/1 sample in. A new key restarts: count 1, vis = the sample.
        public void Accumulate(uint sampleKey, float sample)
        {
            if (sampleKey != key) { key = sampleKey; count = 0; vis = 0f; }
            count = math.min(count + 1, SETTLE_SAMPLES);
            vis += (sample - vis) / count;
        }
    }

    /// What one pixel's history holds after `frames` frames on a fixed surface point,
    /// frame indices first .. first + frames - 1.
    public static float Estimate(ChunkStore store, int3 hitVoxel, int3 n, uint seed, int frames, float3 cameraPosM,
                                 float maxDistM = 0f, uint firstFrame = 0)
    {
        var h = History.Empty;
        uint key = OriginKey(hitVoxel + n);
        for (int k = 0; k < frames; k++)
            h.Accumulate(key, Sample(store, hitVoxel, n, Direction(n, seed, firstFrame + (uint)k), cameraPosM, maxDistM));
        return h.vis;
    }
}
