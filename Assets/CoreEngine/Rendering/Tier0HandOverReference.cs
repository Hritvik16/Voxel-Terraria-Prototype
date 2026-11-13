// Assets/CoreEngine/Rendering/Tier0HandOverReference.cs
//
// TASK 194: CPU twin of Raymarch.compute's REFUSED-CHUNK HAND-OVER block and of
// the column walk it calls (ColumnWalkT = FarStartT's DDA, line for line). All in
// 0.1 m voxels; chunk columns are 128 voxels; `stopAt` takes WINDOW-relative column
// coords: the shader's FarStartT stops at !FarChunkResident, the hand-over at
// Tier0ColumnRefused (FarField.ColumnRefused of origin + rc).
//
// The bug it fixes: a chunk the full near GPU body pool refused is held as air in
// tier 0 and its column is marked non-resident so the shells draw it -- but tier 0
// still drew the rest of that column (the uniform rock under it needs no GPU body)
// and its neighbours, so a ray through the refused chunk's air hit the rock below:
// a 12.8 m box pit. The rule: a tier-0 hit past the ray's entry into a REFUSED
// column is not tier 0's; its march ends at that entry and the shells take over.
// REFUSED columns only: a column that is non-resident because it is still
// streaming stays tier 0's (near the camera it lies in shell 1's hole, where no
// shell holds anything -- a first version that stopped at any non-resident column
// lost houses at eye level this way, task 194's seed-42 town_eye shot).
using System;
using Unity.Mathematics;

namespace VoxelEngine.Rendering
{
    public static class Tier0HandOverReference
    {
        private static float3 SafeDir(float3 d)
        {
            d.x = math.abs(d.x) < 1e-6f ? (d.x >= 0f ? 1e-6f : -1e-6f) : d.x;
            d.y = math.abs(d.y) < 1e-6f ? (d.y >= 0f ? 1e-6f : -1e-6f) : d.y;
            d.z = math.abs(d.z) < 1e-6f ? (d.z >= 0f ? 1e-6f : -1e-6f) : d.z;
            return d;
        }

        /// ColumnWalkT: the first t in [window entry, tLimit) at which the ray enters a
        /// column of the window where stopAt is true; tLimit if none.
        public static float FirstColumnT(Func<int2, bool> stopAt, int2 originChunksXZ, int2 dimsXZ,
                                         float3 O, float3 D, float tLimit)
        {
            D = SafeDir(D);
            int2 oc = originChunksXZ, dims = dimsXZ;
            float2 wmin = new float2(oc * 128);
            float2 wmax = wmin + new float2(dims * 128);
            float2 invD = 1f / D.xz;
            float2 ta = (wmin - O.xz) * invD, tb = (wmax - O.xz) * invD;
            float2 tlo = math.min(ta, tb), thi = math.max(ta, tb);
            float t0 = math.max(math.max(tlo.x, tlo.y), 0f);
            float t1 = math.min(math.min(thi.x, thi.y), tLimit);
            if (t0 >= t1) return tLimit;
            int2 rc = math.clamp((int2)math.floor((O.xz + D.xz * t0) / 128f) - oc, int2.zero, dims - 1);
            int2 s = new int2(D.x > 0f ? 1 : -1, D.z > 0f ? 1 : -1);
            float tEnter = t0;
            for (int i = 0; i < 96; i++)
            {
                if (stopAt(rc)) return tEnter;
                float2 bmin = new float2((rc + oc) * 128);
                float2 bmax = bmin + 128f;
                float2 tE = (new float2(s.x > 0 ? bmax.x : bmin.x, s.y > 0 ? bmax.y : bmin.y) - O.xz) * invD;
                if (tE.x < tE.y) { tEnter = tE.x; rc.x += s.x; }
                else             { tEnter = tE.y; rc.y += s.y; }
                if (tEnter >= t1 || math.any(rc < 0) || math.any(rc >= dims)) return tLimit;
            }
            return tLimit;
        }

        /// The hand-over block: does tier 0 keep its hit at tHit? tEnd = where tier 0's
        /// march ends (the shells take over from there when the hit is dropped).
        /// refusedNow = Raymarch.compute's _Tier0Refused (any refused chunk this frame).
        public static bool KeepsHit(bool refusedNow, Func<int2, bool> refused, int2 originChunksXZ, int2 dimsXZ,
                                    float3 O, float3 D, float tHit, out float tEnd)
        {
            tEnd = tHit;
            if (!refusedNow) return true;
            float tRes = FirstColumnT(refused, originChunksXZ, dimsXZ, O, D, tHit);
            if (tRes < tHit) { tEnd = tRes; return false; }
            return true;
        }
    }
}
