// ==========================================
// Assets/CoreEngine/Rendering/TorchFlood.hlsl
//
// Task 158 (TORCH_LIGHT_PLAN.md step L2): the GPU side of the torch light
// flood. Included by Raymarch.compute ONLY under #ifdef VE_TORCH_FLOOD (the
// CSMainFlood / CSMainFarShadeFlood twins), so CSMain / CSMainFarShade compile
// from the pre-task source byte for byte, and by Resources/TorchFloodProbe.compute
// (the GPU == oracle probe). The CPU (TorchFloodPool.cs) is the only writer;
// TorchFloodPool.SampleLevels is this file's C# twin, line for line.
//
// LAYOUT (task 157's storage decision: 0.2 m mixed cells + a per-voxel escape).
//   _FloodDir     dense over the lit-chunk AABB (_FloodDirOrigin, _FloodDirDims,
//                 in chunks): lit-chunk table index + 1, 0 = no light there
//   _FloodTables  4,096 entries per lit chunk (CoordMath.LocalBrickIndex order):
//                 0 = dark brick, else kind << 30 | offset into _FloodData
//                   kind 1 AIR brick   (all 512 voxels air): 2^3 cells at 0.4 m,
//                                       sampled trilinearly between cell centres
//                   kind 2 MIXED brick (air and solid): 4^3 cells at 0.2 m,
//                                       nearest cell (no blending across a wall)
//   _FloodData    levels r | g << 10 | b << 20 (10 bits each, task 157's units:
//                 4.2913 per 0.1 m of path, 515 = 12 m). A mixed cell with bit 30
//                 set is an ESCAPE: its air splits into >= 2 face-connected
//                 groups inside the 0.2 m cell (a thin diagonal wall), so the
//                 low 30 bits point at 8 per-voxel values instead.
// ==========================================

#ifndef VE_TORCH_FLOOD_HLSL
#define VE_TORCH_FLOOD_HLSL

StructuredBuffer<uint> _FloodDir;
StructuredBuffer<uint> _FloodTables;
StructuredBuffer<uint> _FloodData;
int4   _FloodDirOrigin;   // xyz: AABB min chunk
int4   _FloodDirDims;     // xyz: AABB size in chunks; w: 1 if any light is stored
float4 _FloodParams;      // x intensity, y falloff k (1/m^2), z reach m, w level units per metre
float4 _FloodParams2;     // x top level (the brightest seed), y stops over the reach (colour), z wrap (direction floor), w direction term (task 164, -tfdir)

float3 FloodUnpack(uint p)
{
    return float3((float)(p & 1023u), (float)((p >> 10) & 1023u), (float)((p >> 20) & 1023u));
}

/// The flood's per-channel levels at world voxel v (0 = dark).
float3 FloodLevels(int3 v)
{
    if (_FloodDirDims.w == 0) return float3(0.0, 0.0, 0.0);
    int3 c = (v >> 7) - _FloodDirOrigin.xyz;
    if (any(c < 0) || any(c >= _FloodDirDims.xyz)) return float3(0.0, 0.0, 0.0);
    uint t = _FloodDir[(uint)((c.z * _FloodDirDims.y + c.y) * _FloodDirDims.x + c.x)];
    if (t == 0u) return float3(0.0, 0.0, 0.0);
    int3 b = (v >> 3) & 15;
    uint e = _FloodTables[(t - 1u) * 4096u + (uint)((b.z << 8) | (b.y << 4) | b.x)];
    if (e == 0u) return float3(0.0, 0.0, 0.0);
    uint off = e & 0x3FFFFFFFu;
    int3 l = v & 7;
    if ((e >> 30) == 2u)
    {
        int3 cl = l >> 1;
        uint p = _FloodData[off + (uint)((cl.z << 4) | (cl.y << 2) | cl.x)];
        if ((p & 0x40000000u) != 0u)
        {
            int3 q = l & 1;
            p = _FloodData[(p & 0x3FFFFFFFu) + (uint)((q.z << 2) | (q.y << 1) | q.x)];
        }
        return FloodUnpack(p);
    }
    // AIR brick: cell centres sit at in-brick voxel coordinate 2.0 and 6.0
    float3 f = saturate(((float3)l + 0.5 - 2.0) * 0.25);
    float3 c000 = FloodUnpack(_FloodData[off + 0u]), c100 = FloodUnpack(_FloodData[off + 1u]);
    float3 c010 = FloodUnpack(_FloodData[off + 2u]), c110 = FloodUnpack(_FloodData[off + 3u]);
    float3 c001 = FloodUnpack(_FloodData[off + 4u]), c101 = FloodUnpack(_FloodData[off + 5u]);
    float3 c011 = FloodUnpack(_FloodData[off + 6u]), c111 = FloodUnpack(_FloodData[off + 7u]);
    float3 x00 = lerp(c000, c100, f.x), x10 = lerp(c010, c110, f.x);
    float3 x01 = lerp(c001, c101, f.x), x11 = lerp(c011, c111, f.x);
    return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
}

/// The brightest channel's level at v.
float FloodMax(int3 v)
{
    float3 L = FloodLevels(v);
    return max(L.x, max(L.y, L.z));
}

/// Task 164, -tfdir 1: the flood's gradient from central differences over 0.4 m
/// (v +- 2 on each axis = the neighbouring 0.2 m cells; one-sided where a side
/// is dark, 0 where both are), normalised; the cosine to the face normal.
float FloodGradCos(int3 v, int3 n, float m)
{
    float3 g;
    [unroll] for (int a = 0; a < 3; a++)
    {
        int3 e = int3(a == 0 ? 2 : 0, a == 1 ? 2 : 0, a == 2 ? 2 : 0);
        float p = FloodMax(v + e), q = FloodMax(v - e);
        g[a] = (p > 0.0 && q > 0.0) ? (p - q) * 0.5 : p > 0.0 ? p - m : q > 0.0 ? m - q : 0.0;
    }
    float gl = length(g);
    return gl > 1e-3 ? saturate(dot(float3(n), g) / gl) : 0.5;
}

/// Level -> light. d = metres of PATH (top - the brightest channel); the C.11
/// shape I / (1 + k d^2) on that path distance, times a smooth window that
/// reaches 0 at the reach (hides the chamfer's octagonal outer edge). Colour:
/// a dimmer channel sits (top - its seed) levels below the brightest, which
/// SeedFor set to log2(fraction) * top / stops, so exp2 of the gap restores it.
/// DIRECTION (n = the face normal, v = the air voxel in front of the face; n = 0
/// skips it), dir = wrap + (1 - wrap) x ndl, ndl by _FloodParams2.w (-tfdir):
///   0 (task 158) levels rise toward the light along the way it came, so the rise
///     from v to v + 2n (always the next 0.2 m cell), over the most an axis step can
///     rise (2 x W_FACE = 8 levels); v + 2n dark: half. Piecewise constant (the
///     chamfer's gradient is): the facets of 158's sheet.
///   1 (task 164) FloodGradCos: the normalised 3-axis gradient over 0.4 m.
///   2 (task 164) the cosine toward the torch whose straight-line distance matches
///     the flood's path (FloodTorchCos, supplied by the includer under
///     VE_FLOOD_TORCH_DIR), weighted by how straight that path is; mode 1 where it
///     bends or no torch matches.
///   3 (task 164) flat: 0.5 (no direction).
/// Every mode only scales a nonzero level: a dark voxel stays exactly dark.
float3 FloodLight(int3 v, int3 n)
{
    float3 L = FloodLevels(v);
    float m = max(L.x, max(L.y, L.z));
    if (m <= 0.0) return float3(0.0, 0.0, 0.0);
    float d = max(_FloodParams2.x - m, 0.0) / _FloodParams.w;
    float dir = 1.0;
    if (any(n != 0))
    {
        int mode = (int)(_FloodParams2.w + 0.5);
        float ndl;
        if (mode == 1) ndl = FloodGradCos(v, n, m);
        else if (mode == 2)
        {
            float wS = 0.0, cosT = 0.0;
#ifdef VE_FLOOD_TORCH_DIR
            wS = FloodTorchCos(((float3)v + 0.5) * 0.1, float3(n), d, cosT);
#endif
            ndl = wS >= 1.0 ? cosT : lerp(FloodGradCos(v, n, m), cosT, wS);
        }
        else if (mode == 3) ndl = 0.5;
        else
        {
            float m2 = FloodMax(v + n * 2);
            ndl = m2 > 0.0 ? saturate((m2 - m) * 0.125) : 0.5;
        }
        dir = _FloodParams2.z + (1.0 - _FloodParams2.z) * ndl;
    }
    float x = saturate(d / _FloodParams.z);
    x *= x; x *= x;
    float w = 1.0 - x;
    w *= w;
    float s = _FloodParams.x * w / (1.0 + _FloodParams.y * d * d);
    float3 col = exp2(-_FloodParams2.y * (m - L) / _FloodParams2.x);
    col = (L > 0.0) ? col : float3(0.0, 0.0, 0.0);
    return s * dir * col;
}

#endif
