// Assets/TerrainGen/Noise.cs
//
// The generator's noise vocabulary. Unity.Mathematics' simplex noise is pure
// arithmetic (floor, mod289 via floor, polynomials), which is what makes the
// Burst-vs-Mono byte-identity contract (TerrainSpec.cs) achievable.
using Unity.Mathematics;

namespace TerrainGen
{
    public static class Noise
    {
        /// fBm of 2D simplex noise, roughly [-1, 1].
        public static float Fbm(float2 p, int octaves)
        {
            float a = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                a += amp * noise.snoise(p);
                norm += amp;
                p = p * 2.03f + new float2(17.3f, -9.1f);
                amp *= 0.5f;
            }
            return a / norm;
        }

        /// Ridged multifractal, [0, ~1]: sharp crests where the noise crosses zero.
        /// Ridged multifractal. The crest rounding (RIDGE_ROUND) and feedback gain
        /// are knobs; they are at the ORIGINAL sharp values (0, 1.6). Rounding
        /// them (0.15, 1.2) was tried on 2026-09-25 against the horns and made
        /// every range soft -- owner: "too rounded and soft ... overcompensated".
        /// Linear crests are natural; only POINT peaks were the problem, and
        /// WorldMap.TrimSpikes removes those while keeping ridges.
        public const float RIDGE_ROUND = 0f;
        public const float RIDGE_FEEDBACK = 1.6f;

        public static float Ridged(float2 p, int octaves)
        {
            float a = 0f, amp = 0.5f, w = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float v = noise.snoise(p);
                float soft = math.sqrt(v * v + RIDGE_ROUND * RIDGE_ROUND) - RIDGE_ROUND;   // |v|, rounded at 0
                float n = 1f - soft;
                n *= n;
                n *= w;
                w = math.saturate(n * RIDGE_FEEDBACK);
                a += n * amp;
                norm += amp;
                p = p * 2.03f + new float2(-31.7f, 5.3f);
                amp *= 0.5f;
            }
            return a / norm;
        }

        public static float SmoothStep(float e0, float e1, float x)
        {
            float t = math.saturate((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        /// 32-bit integer hash -> [0, 1). For placement decisions (pockets),
        /// never for anything that must be continuous.
        public static float Hash01(int x, int y, int z, uint seed)
        {
            uint h = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)z * 0xcb1ab31fu ^ seed * 0x9e3779b9u;
            h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
            return (h & 0xFFFFFFu) / 16777216f;
        }
    }
}
