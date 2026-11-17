// =============================================================================
// FloodTwinRule.cs -- task 229: draw the torch-flood kernel twin only when it
// can change a pixel.
//
// Since task 175 the flood is ON in play, and while TorchFloodPool.Ready the
// renderer swapped the far-shading kernel for CSMainFarShadeFlood -- EVERY
// frame, even with no torch anywhere (an empty pool still uploads, so Ready is
// true). The twin is not a diet-2 kernel, so it also cost play diet 2 (task 161).
//
// The twin and the plain kernel differ ONLY on tier-0 hits that are not
// self-lit (Raymarch.compute, both `#ifdef VE_TORCH_FLOOD` blocks):
//   twin:  FloodLight(voxel + normal) + FloodOtherLights / ...Omni (lights from _FloodLightFirst on)
//   plain: SumPointLightsShadowed / SumPointLights / ...Omni (every active light)
// Every one of those terms is gated per light by `dist^2 > range^2 -> skip`
// (positions: the hit point, the voxel centre; both inside the tier-0 window),
// and FloodLight is exactly 0 where FloodLevels reads no lit chunk (directory
// entry 0, or outside the directory's AABB, or no chunk at all). So when
//   (a) no active light's range sphere reaches the tier-0 window, and
//   (b) no lit flood chunk overlaps the tier-0 window,
// both kernels add exactly 0 light at every tier-0 hit, and every other line is
// the same source -> the plain kernel (and so diet 2) draws the same picture.
// The window is widened by one brick (8 voxels) on every side: the hit point,
// the voxel centre and voxel + normal are all within one voxel of it, and the
// margin also covers float rounding of the sphere test (0.8 m >> 1 ulp).
// Exact by construction; no distance tuned to any terrain.
// =============================================================================

using Unity.Mathematics;
using VoxelEngine.Memory;

namespace VoxelEngine.Rendering
{
    public static class FloodTwinRule
    {
        /// Bricks of margin round the tier-0 window (see the header).
        public const int MARGIN_BRICKS = 1;

        /// The tier-0 window in voxels, widened by MARGIN_BRICKS on every side (inclusive bounds).
        public static void WindowBoxVox(int3 originBricks, int3 dimsBricks, out int3 loVox, out int3 hiVox)
        {
            loVox = (originBricks - MARGIN_BRICKS) << 3;
            hiVox = ((originBricks + dimsBricks + MARGIN_BRICKS) << 3) - 1;
        }

        /// (a): does light i (i < count) reach the voxel box [loVox, hiVox]? Its sphere (centre (x, y, z) m,
        /// radius range m) against the box's metre bounds (voxel v spans [0.1 v, 0.1 (v + 1)]).
        public static bool AnyLightReaches(LightSource[] lights, int count, int3 loVox, int3 hiVox)
        {
            if (lights == null) return false;
            float3 lo = (float3)loVox * 0.1f, hi = ((float3)hiVox + 1f) * 0.1f;
            int n = math.min(count, lights.Length);
            for (int i = 0; i < n; i++)
            {
                var L = lights[i];
                if (!(L.range > 0f)) continue;   // range 0 (or NaN): the shader's dist^2 > range^2 skips every pixel but the point itself
                float3 p = new float3(L.x, L.y, L.z);
                float3 d = p - math.clamp(p, lo, hi);
                if (math.dot(d, d) <= L.range * L.range) return true;
            }
            return false;
        }

        /// (b): a non-zero directory entry inside the chunk range [cLo, cHi] (the directory covers
        /// [origin, origin + dims) in 128-voxel chunks, row order x fastest, then y, then z, as FloodLevels).
        public static bool DirHasLitChunkIn(uint[] dir, int3 origin, int3 dims, int3 cLo, int3 cHi)
        {
            if (dir == null || dir.Length == 0) return false;
            int3 a = math.max(cLo, origin) - origin, b = math.min(cHi, origin + dims - 1) - origin;
            if (math.any(a > b)) return false;
            for (int z = a.z; z <= b.z; z++)
            for (int y = a.y; y <= b.y; y++)
            {
                int row = (z * dims.y + y) * dims.x;
                for (int x = a.x; x <= b.x; x++) if (dir[row + x] != 0u) return true;
            }
            return false;
        }

        /// The renderer's question: may this frame's tier-0 window see flood or torch light?
        /// false = the plain kernel draws the same picture as the twin.
        public static bool Needed(TorchFloodPool pool, LightSource[] activeLights, int activeCount, int3 originBricks, int3 dimsBricks)
        {
            WindowBoxVox(originBricks, dimsBricks, out int3 lo, out int3 hi);
            if (AnyLightReaches(activeLights, activeCount, lo, hi)) return true;
            return pool != null && pool.LitChunkIn(lo, hi);
        }
    }
}
