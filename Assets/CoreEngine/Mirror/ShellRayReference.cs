// Assets/CoreEngine/Mirror/ShellRayReference.cs
//
// Engine step 11b (task 80; Amendment 8.14 rows 1, 2, 5, 15): the CPU twin of
// Raymarch.compute's SHELL MARCH, line for line, so EditMode (no GPU) can test
// the hand-over: ShellRayReference here, ShellMarch / ShellResolve /
// ShellChunkState / ShellLookup there. A change to one is a change to both.
//
// THE RULES (8.14 row 5, rule A; owner Q2 (a)):
//   * By distance. A ray reads shell k where its camera distance d is in
//     (outer_(k-1), outer_k] (High: 69 / 138 / 275 / 550 / 1,100 / 2,200 /
//     4,400 / 6,600 m). For a primary ray d is its own t; for a shadow ray it is
//     the sample's distance from the camera (§6.5's 8.2 rule, as SelectLODTier).
//   * Absent -> coarser. A chunk of shell k that is not stored (missing,
//     refused when the pool is full -- row 15 -- or outside the published box)
//     is read from shell k+1, k+2, ... The far band draws only past the last
//     shell or where no coarser shell has the chunk either (the march ends).
//   * Hole -> finer. A shell stores no chunk the finer shell fully covers (its
//     cubic hole, +-H chunks), but the hand-over is by Euclidean distance: in
//     the hole's corners (d up to sqrt(3) x outer_(k-1)) shell k-1 still has
//     the chunk (its box is +-R chunks >= outer_(k-1)), so it is read there.
//   * Hole all the way down -> tier 0. A chunk in the hole of every shell
//     down to shell 1 is tier 0's (shell 0's) space: tier 0 already marched it
//     and saw air, so the march LEAPS it (one tier-0 chunk, 12.8 m) -- unless
//     tier 0 is missing there (a non-resident column inside its band; the
//     far band drew that before 11b and still does). Without this a camera
//     outside tier 0's band (high up) would see no shell at all: its own chunk
//     is in every hole.
//   * Never coarser mid-cell. The switch to a coarser shell waits until the ray
//     enters a new cell of that shell (cells nest, so a coarser cell's faces
//     are finer faces). A ray that must start inside a coarser solid cell (the
//     hand-over from tier 0, or a fallback) is a hit with a TOP face normal
//     (a bottom face going up), not the side face it last crossed: that side
//     face, lit as a wall, is the dark arc at LOD seams (KNOWN_ISSUES "BLACK
//     BAR OVER THE SEA": "the dark arcs below it are voxel LOD tier seams"). A
//     shadow ray skips its start cell instead, and any solid cell within 3 of
//     that shell's cells of its origin (no self-shadowing across LOD).
// Coordinates are 0.1 m voxels throughout; every shell index is a shift
// (voxel >> k, >> k + 3, >> k + 7) and every ring a pow2 mask (§6.2).
using System;
using Unity.Mathematics;

namespace VoxelEngine.Mirror
{
    public static class ShellRayReference
    {
        public const int STATE_OK = 0, STATE_HOLE = 1, STATE_ABSENT = 2;
        public const int END_HIT = 0, END_PAST_LAST = 1, END_ABSENT = 2, END_CAP = 3;
        public const int RESOLVE_ABSENT = -1, RESOLVE_TIER0 = -2;

        /// What the march reads: the shader's constant buffer and three buffers.
        public interface IView
        {
            int ShellCount { get; }                     // including shell 0
            float OuterVoxels(int k);                   // hand-over distance, 0.1 m voxels
            /// Published box (inclusive) and hole; false = shell k has nothing.
            bool Box(int k, out int3 lo, out int3 hi, out int3 cc, out int hole);
            uint Dir(int k, int3 chunk);                // the entry of an in-box chunk
            uint Page(int k, int page, int localBrick);  // brick handle
            byte Voxel(int k, int body, int vIdx);       // (vz << 6) | (vy << 3) | vx
            /// Tier 0 should hold this voxel but does not (a non-resident column of its window, inside its band).
            bool Tier0Missing(int3 voxel);
        }

        public struct Result
        {
            public int End;          // END_*
            public int Shell;        // the shell that drew the hit (-1 none)
            public float T;          // hit t, or where the far band takes over
            public int3 Voxel, Normal;
            public byte Mat;
            public int Iters;
            public bool InsideStart; // the hit cell was entered mid-cell (top-face normal)
        }

        /// Smallest shell k >= 1 whose outer bound is beyond d; -1 = past the last.
        public static int ShellByDistance(IView v, float d)
        {
            for (int k = 1; k < v.ShellCount; k++) if (d < v.OuterVoxels(k)) return k;
            return -1;
        }

        public static int ChunkState(IView v, int k, int3 voxel, out uint e)
        {
            e = 0u;
            if (!v.Box(k, out int3 lo, out int3 hi, out int3 cc, out int hole)) return STATE_ABSENT;
            int3 c = voxel >> (7 + k);
            if (math.any(c < lo) || math.any(c > hi)) return STATE_ABSENT;
            int3 d = math.abs(c - cc);
            if (hole >= 0 && math.cmax(d) <= hole) return STATE_HOLE;
            e = v.Dir(k, c);
            return e == ShellStore.ABSENT ? STATE_ABSENT : STATE_OK;
        }

        /// The shell that answers at `voxel` when the distance asks for kd;
        /// RESOLVE_ABSENT = none (far band); RESOLVE_TIER0 = in every hole down to shell 1.
        public static int Resolve(IView v, int kd, int3 voxel, out uint e)
        {
            int s = ChunkState(v, kd, voxel, out e);
            if (s == STATE_OK) return kd;
            if (s == STATE_HOLE)
            {
                int j = kd - 1;
                for (; j >= 1; j--)
                {
                    int sj = ChunkState(v, j, voxel, out e);
                    if (sj == STATE_OK) return j;
                    if (sj == STATE_ABSENT) break;
                }
                if (j < 1) { e = 0u; return RESOLVE_TIER0; }
            }
            for (int j = kd + 1; j < v.ShellCount; j++)
                if (ChunkState(v, j, voxel, out e) == STATE_OK) return j;
            e = 0u;
            return -1;
        }

        /// The material at `voxel` in shell k (entry e) and the log2 span of the
        /// uniform region it belongs to (k: one voxel, k + 3: a brick, k + 7: the chunk).
        public static byte Lookup(IView v, int k, uint e, int3 voxel, out int spanShift)
        {
            if ((e & ShellStore.UNIFORM) != 0) { spanShift = k + 7; return (byte)(e & 0xFFu); }
            int page = (int)(e & ~ShellStore.PAGE);
            int3 b = (voxel >> (k + 3)) & 15;
            uint h = v.Page(k, page, (b.z << 8) | (b.y << 4) | b.x);
            if ((h & ShellStore.DENSE) == 0) { spanShift = k + 3; return (byte)(h & 0xFFu); }
            int3 s = (voxel >> k) & 7;
            spanShift = k;
            return v.Voxel(k, (int)(h & 0x3FFFFFFFu), (s.z << 6) | (s.y << 3) | s.x);
        }

        /// Did the last step enter a new cell of shell k (through the face `normal`)?
        public static bool AtEntry(int3 voxel, int3 normal, int3 step, int k)
        {
            int m = (1 << k) - 1;
            if (normal.x != 0) return step.x > 0 ? (voxel.x & m) == 0 : (voxel.x & m) == m;
            if (normal.y != 0) return step.y > 0 ? (voxel.y & m) == 0 : (voxel.y & m) == m;
            if (normal.z != 0) return step.z > 0 ? (voxel.z & m) == 0 : (voxel.z & m) == m;
            return false;
        }

        public static float SafeAxis(float d) => math.abs(d) < 1e-6f ? (d >= 0f ? 1e-6f : -1e-6f) : d;

        /// March the shells from t = tStart (primary: `start` = the ray origin =
        /// the camera, 0.1 m voxels; shadow: the origin cell's point, with
        /// `camera` for the distance rule). Mirrors Raymarch.compute's ShellMarch.
        public static Result March(IView v, float3 start, float3 dir, float tStart, bool shadow, float3 camera, int maxIters)
        {
            dir = new float3(SafeAxis(dir.x), SafeAxis(dir.y), SafeAxis(dir.z));
            int3 step = (int3)math.sign(dir);
            float3 tDelta = math.abs(1f / dir);
            float3 p = start + dir * tStart;
            int3 voxel = (int3)math.floor(p);
            float3 tMax = ReseedTMax(voxel, start, tDelta, step);
            float t = tStart;
            int3 normal = int3.zero;
            var r = new Result { End = END_CAP, Shell = -1, T = tStart };
            int kPrev = 0;
            for (int it = 0; it < maxIters; it++)
            {
                r.Iters = it + 1;
                float camDist = shadow ? math.length((float3)voxel + 0.5f - camera) : t;
                int kd = ShellByDistance(v, camDist);
                if (kd < 0) { r.End = END_PAST_LAST; r.T = t; return r; }
                if (kPrev > 0 && kd > kPrev && !AtEntry(voxel, normal, step, kd)) kd = kPrev;
                int k = Resolve(v, kd, voxel, out uint e);
                if (k == RESOLVE_TIER0 && !v.Tier0Missing(voxel))
                {
                    Leap(ref voxel, ref tMax, ref t, ref normal, tDelta, step, start, (voxel >> 7) << 7, 128);
                    kPrev = 0;
                    continue;
                }
                if (k < 0) { r.End = END_ABSENT; r.T = t; return r; }
                byte mat = Lookup(v, k, e, voxel, out int sh);
                if (mat != 0)
                {
                    bool entry = AtEntry(voxel, normal, step, k);
                    // Shadow: ignore solid cells within 3 of that shell's cells of the
                    // origin (a coarser shell standing in for an absent chunk stands
                    // up to a cell above the surface the ray left: self-shadow).
                    if (shadow && t < (3 << k)) entry = false;
                    if (entry || !shadow)
                    {
                        r.End = END_HIT; r.Shell = k; r.T = t; r.Voxel = voxel; r.Mat = mat;
                        r.InsideStart = !entry;
                        r.Normal = entry ? normal : new int3(0, dir.y > 0f ? -1 : 1, 0);
                        return r;
                    }
                    sh = k;   // a shadow ray's start cell: leave it
                }
                Leap(ref voxel, ref tMax, ref t, ref normal, tDelta, step, start, (voxel >> sh) << sh, 1 << sh);
                kPrev = k;
            }
            r.End = END_CAP; r.T = t;
            return r;
        }

        public static float3 ReseedTMax(int3 voxel, float3 start, float3 tDelta, int3 step) => new float3(
            ReseedAxis(voxel.x, start.x, tDelta.x, step.x),
            ReseedAxis(voxel.y, start.y, tDelta.y, step.y),
            ReseedAxis(voxel.z, start.z, tDelta.z, step.z));

        private static float ReseedAxis(int v, float s, float td, int st) => (st > 0 ? (v + 1f - s) : (s - v)) * td;

        /// LeapSpanReseedClosedForm (Raymarch.compute), without its counters.
        public static void Leap(ref int3 voxel, ref float3 tMax, ref float t, ref int3 normal,
                                float3 tDelta, int3 step, float3 start, int3 spanMin, int span)
        {
            int3 exitCount = new int3(
                step.x > 0 ? (spanMin.x + span - voxel.x) : (voxel.x - (spanMin.x - 1)),
                step.y > 0 ? (spanMin.y + span - voxel.y) : (voxel.y - (spanMin.y - 1)),
                step.z > 0 ? (spanMin.z + span - voxel.z) : (voxel.z - (spanMin.z - 1)));
            float tx = tMax.x + (exitCount.x - 1) * tDelta.x;
            float ty = tMax.y + (exitCount.y - 1) * tDelta.y;
            float tz = tMax.z + (exitCount.z - 1) * tDelta.z;
            int axis = tx <= ty ? (tx <= tz ? 0 : 2) : (ty <= tz ? 1 : 2);
            float tHit = axis == 0 ? tx : axis == 1 ? ty : tz;
            const float eps = 1e-4f;
            int guard = span + 1;
            if (axis == 0) voxel.x += step.x * exitCount.x;
            if (axis == 1) voxel.y += step.y * exitCount.y;
            if (axis == 2) voxel.z += step.z * exitCount.z;
            if (axis != 0) { int n = math.clamp((int)math.floor((tHit + eps - tMax.x) / tDelta.x) + 1, 0, math.min(exitCount.x, guard)); voxel.x += step.x * n; }
            if (axis != 1) { int n = math.clamp((int)math.floor((tHit + eps - tMax.y) / tDelta.y) + 1, 0, math.min(exitCount.y, guard)); voxel.y += step.y * n; }
            if (axis != 2) { int n = math.clamp((int)math.floor((tHit + eps - tMax.z) / tDelta.z) + 1, 0, math.min(exitCount.z, guard)); voxel.z += step.z * n; }
            tMax = ReseedTMax(voxel, start, tDelta, step);
            t = tHit;
            normal = axis == 0 ? new int3(-step.x, 0, 0) : axis == 1 ? new int3(0, -step.y, 0) : new int3(0, 0, -step.z);
        }

        /// A view over real ShellStores (their CPU directory and, with
        /// ShellStore.ShadowUploads, the shadows of every page and body upload).
        public sealed class StoreView : IView
        {
            private readonly ShellLayout _layout;
            private readonly ShellStore[] _stores;
            private readonly Func<int, (bool ok, int3 lo, int3 hi, int3 cc)> _box;
            public StoreView(ShellLayout layout, ShellStore[] stores, Func<int, (bool, int3, int3, int3)> box)
            { _layout = layout; _stores = stores; _box = box; }
            public int ShellCount => _layout.ShellCount;
            public float OuterVoxels(int k) => _layout[k].OuterM * 10f;
            public bool Box(int k, out int3 lo, out int3 hi, out int3 cc, out int hole)
            {
                hole = _layout[k].Hole;
                if (_stores[k] == null) { lo = hi = cc = default; return false; }
                var b = _box(k); lo = b.lo; hi = b.hi; cc = b.cc;
                return b.ok;
            }
            public uint Dir(int k, int3 c) => _stores[k].DirectoryEntry(_stores[k].Slot(c));
            public uint Page(int k, int page, int lb) => _stores[k].ShadowPages[page * EngineConfig.BRICKS_PER_CHUNK + lb];
            public byte Voxel(int k, int body, int vIdx) => (byte)(_stores[k].ShadowBricks[body * 128 + (vIdx >> 2)] >> ((vIdx & 3) * 8));
            public bool Tier0Missing(int3 voxel) => false;
        }
    }
}
