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
//   * Tier 0's band (task 85, step 12: hand-over by distance, not residency).
//     A primary ray marches the shells from the camera. Up to tier 0's
//     hand-over distance (or where tier 0's own march stopped, if nearer) a
//     chunk tier 0 HOLDS is leapt: it marched it and saw air. One it does not
//     hold (non-resident, outside its window or its layers) is read from
//     shell 1, 2, ... like any absent chunk. No residency DDA (FarStartT).
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
        public const int END_SKY = 4;   // task 99: the sky-ray exit (rising, above everything left)
        public const int SKY_NEVER = int.MaxValue;

        /// Task 99: Raymarch.compute's ShellSkyY. shellTopY = ShellStreamer.SolidTopY. A primary
        /// ray with the far band on also waits until it is 2 voxels above the far band's max height
        /// (its march from the shells' end then stops at once, no hit, from any later t too).
        public static int PrimarySkyY(int shellTopY, bool farEnabled, float farMaxH) =>
            farEnabled ? math.max(shellTopY, (int)math.ceil(farMaxH) + 2) : shellTopY;
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
            /// Tier 0 holds this voxel (a resident column of its window, inside its band).
            bool Tier0Holds(int3 voxel);
        }

        /// Task 93b5: a view that also has each page's AIR-BRICK MASK (ShellStore.GroupMask:
        /// bit g = brick group g holds a brick that is not uniform air) and tier 0's layers
        /// (Tier0Holds is false outside [Tier0FloorY, Tier0CeilY): the shader's
        /// _ContentFloorVoxelY / _ContentCeilingVoxelY). March uses the mask only through this.
        public interface IMaskView : IView
        {
            ulong GroupMask(int k, int page);
            int Tier0FloorY { get; }
            int Tier0CeilY { get; }
        }

        /// Task 116: a mask view that also has each page's COLUMN TOPS (ShellStore.ColumnTops: group
        /// column j = (gz << 2) | gx holds non-air bricks up to local brick y top - 1; 0 = all air).
        /// March uses them only through this (colLeap).
        public interface IColumnView : IMaskView
        {
            int ColumnTop(int k, int page, int j);
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
            public int DirReads, PageReads;   // task 93b4: Resolve calls / brick-handle reads the caches did not answer
            public int MaskGroups;   // task 93b5: empty brick groups crossed in one leap each
            public int ColLeaps, ColLeapsChunk;   // task 116: column sky leaps / of them over the whole chunk
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
        public static int Resolve(IView v, int kd, int3 voxel, out uint e) => Resolve(v, kd, voxel, out e, out _);

        /// `whole` (task 93b4): the answer holds for every voxel of kd's chunk -- shell
        /// kd has that chunk, or it is absent and the coarser shells (whose chunks
        /// contain it) answered. False when kd's chunk is in its hole (finer chunks decide).
        public static int Resolve(IView v, int kd, int3 voxel, out uint e, out bool whole)
        {
            int s = ChunkState(v, kd, voxel, out e);
            whole = s != STATE_HOLE;
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

        /// Lookup with the brick handle `h` of a paged chunk already read (task 93b4).
        public static byte LookupHandle(IView v, int k, uint h, int3 voxel, out int spanShift)
        {
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
        /// `camera` for the distance rule). tTier0 (task 85): tier 0 marched
        /// [0, tTier0) and saw air; a chunk it holds there is leapt (0 = off).
        /// Mirrors Raymarch.compute's ShellMarch.
        /// useCache (task 93b4, the shader's SHELL_CACHE_ON): one-entry caches of
        /// Resolve's answer per (kd, kd's chunk) and of the brick handle per (shell,
        /// brick). Fewer reads, the same result (ShellMarchCacheTests).
        /// useMask (task 93b5, the shader's SHELL_MASK_ON; needs an IMaskView): an air
        /// brick of a paged chunk whose brick group is empty in the page's mask leaps the
        /// whole group in one iteration where GroupLeapOk allows it. Fewer iterations, the
        /// same result in every field but Iters and the read counters as planeLeap without the
        /// mask (ShellAirMaskTests). planeLeap (task 93b5 (b), implied by useMask): every leap is
        /// PlaneLeap (exit-plane t) instead of Leap (the shader's mask-off default).
        /// skyY (task 99, the shader's ShellSkyY; SKY_NEVER = off): a ray with step.y > 0 whose voxel.y
        /// >= skyY ends END_SKY there. Exact when skyY >= every non-air voxel's y + 1 the view holds:
        /// voxel.y never falls on a rising ray (leaps add step x counts >= 0), so it reads only air
        /// from there on (ShellSkyExitTests).
        /// colLeap (task 116, the shader's SHELL_COLLEAP_ON; needs an IColumnView): a rising ray in an air
        /// brick of a paged chunk at or above its group column's top crosses the rest of that column (to
        /// the chunk top) -- or of the chunk, when every column is that low -- in ONE leap where BoxLeapOk
        /// allows it (ShellColumnLeapTests).
        public static Result March(IView v, float3 start, float3 dir, float tStart, bool shadow, float3 camera, int maxIters,
                                   float tTier0 = 0f, bool useCache = true, bool useMask = false, bool planeLeap = false,
                                   int skyY = SKY_NEVER, bool colLeap = false)
        {
            var mv = useMask ? v as IMaskView : null;
            var cv = colLeap ? v as IColumnView : null;
            bool pl = planeLeap || useMask;   // the shader's SHELL_PLANE_LEAP
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
            int rcKd = -1, rcK = 0; int3 rcChunk = int3.zero; uint rcE = 0u;   // resolve cache
            int pcK = -1; int3 pcBrick = int3.zero; uint pcH = 0u;            // page cache
            for (int it = 0; it < maxIters; it++)
            {
                r.Iters = it + 1;
                if (step.y > 0 && voxel.y >= skyY) { r.End = END_SKY; r.T = t; return r; }   // task 99
                float camDist = shadow ? math.length((float3)voxel + 0.5f - camera) : t;
                if (t < tTier0 && v.Tier0Holds(voxel))
                {
                    // Tier 0's band, a chunk it holds: it marched this and saw air.
                    LeapAny(pl, ref voxel, ref tMax, ref t, ref normal, tDelta, step, start, (voxel >> 7) << 7, 128);
                    kPrev = 0;
                    continue;
                }
                int kd = ShellByDistance(v, camDist);
                if (kd < 0) { r.End = END_PAST_LAST; r.T = t; return r; }
                if (kPrev > 0 && kd > kPrev && !AtEntry(voxel, normal, step, kd)) kd = kPrev;
                int k; uint e;
                int3 kdChunk = voxel >> (7 + kd);
                if (useCache && kd == rcKd && kdChunk.Equals(rcChunk)) { k = rcK; e = rcE; }
                else
                {
                    k = Resolve(v, kd, voxel, out e, out bool whole);
                    rcKd = whole ? kd : -1; rcChunk = kdChunk; rcK = k; rcE = e;
                    r.DirReads++;
                }
                if (k == RESOLVE_TIER0 && !v.Tier0Missing(voxel))
                {
                    LeapAny(pl, ref voxel, ref tMax, ref t, ref normal, tDelta, step, start, (voxel >> 7) << 7, 128);
                    kPrev = 0;
                    continue;
                }
                if (k < 0) { r.End = END_ABSENT; r.T = t; return r; }
                byte mat;
                int sh;
                if ((e & ShellStore.UNIFORM) != 0) mat = Lookup(v, k, e, voxel, out sh);
                else
                {
                    // A brick of shell k lies in one chunk of shell k: same entry, same page.
                    int3 brick = voxel >> (k + 3);
                    if (!(useCache && k == pcK && brick.Equals(pcBrick)))
                    {
                        int3 b = brick & 15;
                        pcH = v.Page(k, (int)(e & ~ShellStore.PAGE), (b.z << 8) | (b.y << 4) | b.x);
                        pcK = k; pcBrick = brick;
                        r.PageReads++;
                    }
                    mat = LookupHandle(v, k, pcH, voxel, out sh);
                }
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
                // Task 116: the COLUMN SKY LEAP (Raymarch.compute's comment there): above its group
                // column's top a rising ray reads only air up to the chunk top; one leap crosses the
                // column, or the whole chunk's x and z when every column is that low.
                if (cv != null && step.y > 0 && sh == k + 3)
                {
                    int page = (int)(e & ~ShellStore.PAGE);
                    int3 bl = (voxel >> (k + 3)) & 15;
                    if (cv.ColumnTop(k, page, ((bl.z >> 2) << 2) | (bl.x >> 2)) <= bl.y)
                    {
                        bool low = true;
                        for (int j = 0; j < 16 && low; j++) low = cv.ColumnTop(k, page, j) <= bl.y;
                        int cs = k + 7, gs = k + 5;
                        int cMinY = (voxel.y >> cs) << cs;
                        int bMinY = (voxel.y >> (k + 3)) << (k + 3);
                        var bMin = new int3((voxel.x >> cs) << cs, bMinY, (voxel.z >> cs) << cs);
                        var bSize = new int3(1 << cs, cMinY + (1 << cs) - bMinY, 1 << cs);
                        bool ok = low && BoxLeapOk(cv, k, t, tTier0, shadow, camera, start, tDelta, step, bMin, bSize);
                        bool wholeChunk = ok;
                        if (!ok)
                        {
                            bMin = new int3((voxel.x >> gs) << gs, bMinY, (voxel.z >> gs) << gs);
                            bSize = new int3(1 << gs, bSize.y, 1 << gs);
                            ok = BoxLeapOk(cv, k, t, tTier0, shadow, camera, start, tDelta, step, bMin, bSize);
                        }
                        if (ok)
                        {
                            LeapAnyBox(pl, ref voxel, ref tMax, ref t, ref normal, tDelta, step, start, bMin, bSize);
                            r.ColLeaps++;
                            if (wholeChunk) r.ColLeapsChunk++;
                            kPrev = k;
                            continue;
                        }
                    }
                }
                // Task 93b5: an air brick of a paged chunk in an EMPTY brick group (Raymarch.compute's
                // comment there): ONE leap crosses the group when every iteration its bricks would take
                // is this brick's path (GroupLeapOk). Leap's state depends on its exit t alone, so it
                // ends exactly where the last brick leap would.
                if (mv != null && sh == k + 3)
                {
                    int3 gl = (voxel >> (k + 5)) & 3;
                    int g = (gl.z << 4) | (gl.y << 2) | gl.x;
                    if (((mv.GroupMask(k, (int)(e & ~ShellStore.PAGE)) >> g) & 1ul) == 0ul
                        && GroupLeapOk(mv, voxel, k, t, tTier0, shadow, camera, start, tDelta, step))
                    {
                        sh = k + 5;
                        r.MaskGroups++;
                    }
                }
                LeapAny(pl, ref voxel, ref tMax, ref t, ref normal, tDelta, step, start, (voxel >> sh) << sh, 1 << sh);
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

        /// The shell march's leap: PlaneLeap with the mask / planeLeap, else Leap (the shader's ShellLeap).
        private static void LeapAny(bool plane, ref int3 voxel, ref float3 tMax, ref float t, ref int3 normal,
                                    float3 tDelta, int3 step, float3 start, int3 spanMin, int span)
        {
            if (plane) PlaneLeap(ref voxel, ref tMax, ref t, ref normal, tDelta, step, start, spanMin, span);
            else Leap(ref voxel, ref tMax, ref t, ref normal, tDelta, step, start, spanMin, span);
        }

        /// Task 116: the shell march's BOX leap (the shader's ShellLeapBox): PlaneLeapBox with the
        /// mask / planeLeap, else LeapBox.
        private static void LeapAnyBox(bool plane, ref int3 voxel, ref float3 tMax, ref float t, ref int3 normal,
                                       float3 tDelta, int3 step, float3 start, int3 spanMin, int3 span)
        {
            if (plane) PlaneLeapBox(ref voxel, ref tMax, ref t, ref normal, tDelta, step, start, spanMin, span);
            else LeapBox(ref voxel, ref tMax, ref t, ref normal, tDelta, step, start, spanMin, span);
        }

        /// Task 116: Leap over a BOX [spanMin, spanMin + span) (per axis); a cube gives Leap's result.
        public static void LeapBox(ref int3 voxel, ref float3 tMax, ref float t, ref int3 normal,
                                   float3 tDelta, int3 step, float3 start, int3 spanMin, int3 span)
        {
            int3 exitCount = new int3(
                step.x > 0 ? (spanMin.x + span.x - voxel.x) : (voxel.x - (spanMin.x - 1)),
                step.y > 0 ? (spanMin.y + span.y - voxel.y) : (voxel.y - (spanMin.y - 1)),
                step.z > 0 ? (spanMin.z + span.z - voxel.z) : (voxel.z - (spanMin.z - 1)));
            float tx = tMax.x + (exitCount.x - 1) * tDelta.x;
            float ty = tMax.y + (exitCount.y - 1) * tDelta.y;
            float tz = tMax.z + (exitCount.z - 1) * tDelta.z;
            int axis = tx <= ty ? (tx <= tz ? 0 : 2) : (ty <= tz ? 1 : 2);
            float tHit = axis == 0 ? tx : axis == 1 ? ty : tz;
            const float eps = 1e-4f;
            int3 guard = math.min(exitCount, span + 1);
            if (axis == 0) voxel.x += step.x * exitCount.x;
            if (axis == 1) voxel.y += step.y * exitCount.y;
            if (axis == 2) voxel.z += step.z * exitCount.z;
            if (axis != 0) { int n = math.clamp((int)math.floor((tHit + eps - tMax.x) / tDelta.x) + 1, 0, guard.x); voxel.x += step.x * n; }
            if (axis != 1) { int n = math.clamp((int)math.floor((tHit + eps - tMax.y) / tDelta.y) + 1, 0, guard.y); voxel.y += step.y * n; }
            if (axis != 2) { int n = math.clamp((int)math.floor((tHit + eps - tMax.z) / tDelta.z) + 1, 0, guard.z); voxel.z += step.z * n; }
            tMax = ReseedTMax(voxel, start, tDelta, step);
            t = tHit;
            normal = axis == 0 ? new int3(-step.x, 0, 0) : axis == 1 ? new int3(0, -step.y, 0) : new int3(0, 0, -step.z);
        }

        /// Task 93b5 (b): the t of a voxel PLANE (integer coordinate on one axis), from the plane
        /// itself: the same value whatever path reached it. ReseedAxis(v) == PlaneT(st > 0 ? v + 1 : v).
        public static float PlaneT(int plane, float s, float td, int st) => (st > 0 ? (plane - s) : (s - plane)) * td;

        /// The planes one axis crosses in a leap ending at tHit: every plane from the cell's next one
        /// (np, t = tNext) whose t <= tHit, at most guard. The closed-form count is a guess, corrected
        /// against PlaneT, so the cell reached is a function of tHit alone (no eps).
        public static int PlanesCrossed(int np, float s, float td, int st, float tHit, float tNext, int guard)
        {
            int n = math.clamp((int)math.floor((tHit - tNext) / td) + 1, 0, guard);
            while (n > 0 && PlaneT(np + st * (n - 1), s, td, st) > tHit) n--;
            while (n < guard && PlaneT(np + st * n, s, td, st) <= tHit) n++;
            return n;
        }

        /// Raymarch.compute's ShellLeap (task 93b5 (b)): t = the smallest exit-plane t; each axis
        /// crosses the planes whose t <= that t; the normal is the first axis (x, y, z) whose last
        /// crossed plane has that t. The state after a leap depends on its exit t only.
        public static void PlaneLeap(ref int3 voxel, ref float3 tMax, ref float t, ref int3 normal,
                                float3 tDelta, int3 step, float3 start, int3 spanMin, int span)
            => PlaneLeapBox(ref voxel, ref tMax, ref t, ref normal, tDelta, step, start, spanMin, new int3(span));

        /// Task 116: PlaneLeap over a BOX [spanMin, spanMin + span) (per axis); the shader's ShellLeapBox, mask on.
        public static void PlaneLeapBox(ref int3 voxel, ref float3 tMax, ref float t, ref int3 normal,
                                        float3 tDelta, int3 step, float3 start, int3 spanMin, int3 span)
        {
            int3 ec = new int3(
                step.x > 0 ? (spanMin.x + span.x - voxel.x) : (voxel.x - (spanMin.x - 1)),
                step.y > 0 ? (spanMin.y + span.y - voxel.y) : (voxel.y - (spanMin.y - 1)),
                step.z > 0 ? (spanMin.z + span.z - voxel.z) : (voxel.z - (spanMin.z - 1)));
            int3 pe = new int3(step.x > 0 ? spanMin.x + span.x : spanMin.x,
                               step.y > 0 ? spanMin.y + span.y : spanMin.y,
                               step.z > 0 ? spanMin.z + span.z : spanMin.z);
            float tHit = math.min(PlaneT(pe.x, start.x, tDelta.x, step.x),
                         math.min(PlaneT(pe.y, start.y, tDelta.y, step.y), PlaneT(pe.z, start.z, tDelta.z, step.z)));
            int3 np = new int3(step.x > 0 ? voxel.x + 1 : voxel.x, step.y > 0 ? voxel.y + 1 : voxel.y, step.z > 0 ? voxel.z + 1 : voxel.z);
            int3 n = new int3(PlanesCrossed(np.x, start.x, tDelta.x, step.x, tHit, tMax.x, math.min(ec.x, span.x + 1)),
                              PlanesCrossed(np.y, start.y, tDelta.y, step.y, tHit, tMax.y, math.min(ec.y, span.y + 1)),
                              PlanesCrossed(np.z, start.z, tDelta.z, step.z, tHit, tMax.z, math.min(ec.z, span.z + 1)));
            voxel += step * n;
            tMax = ReseedTMax(voxel, start, tDelta, step);
            t = tHit;
            // The last plane crossed on each axis: the new cell's entry face.
            bool hx = n.x > 0 && PlaneT(step.x > 0 ? voxel.x : voxel.x + 1, start.x, tDelta.x, step.x) == tHit;
            bool hy = n.y > 0 && PlaneT(step.y > 0 ? voxel.y : voxel.y + 1, start.y, tDelta.y, step.y) == tHit;
            normal = hx ? new int3(-step.x, 0, 0) : hy ? new int3(0, -step.y, 0) : new int3(0, 0, -step.z);
        }

        /// Raymarch.compute's ShellBoxLeapOk (task 116; 93b5's group test for any box inside ONE chunk
        /// of shell k): no tier-0 leap inside it (t past tTier0, or the box outside tier 0's layers)
        /// and every distance in k's band: a primary ray's t's up to the box's exit t, a shadow ray's
        /// cell centres inside the box (1 voxel margin).
        public static bool BoxLeapOk(IMaskView v, int k, float t, float tTier0, bool shadow, float3 camera,
                                     float3 start, float3 tDelta, int3 step, int3 bMin, int3 bSize)
        {
            if (t < tTier0 && bMin.y + bSize.y > v.Tier0FloorY && bMin.y < v.Tier0CeilY) return false;
            float lo = k > 1 ? v.OuterVoxels(k - 1) : -1f;
            float hi = v.OuterVoxels(k);
            if (shadow)
            {
                float3 c0 = (float3)bMin + 0.5f - camera, c1 = c0 + (float3)(bSize - 1);
                return math.length(math.max(math.abs(c0), math.abs(c1))) < hi - 1f
                    && math.length(math.max(math.max(c0, -c1), 0f)) >= lo + 1f;
            }
            if (t < lo) return false;
            float tExit = math.min(PlaneT(step.x > 0 ? bMin.x + bSize.x : bMin.x, start.x, tDelta.x, step.x),
                          math.min(PlaneT(step.y > 0 ? bMin.y + bSize.y : bMin.y, start.y, tDelta.y, step.y),
                                   PlaneT(step.z > 0 ? bMin.z + bSize.z : bMin.z, start.z, tDelta.z, step.z)));
            return tExit <= hi;
        }

        /// Raymarch.compute's ShellGroupLeapOk: may the EMPTY brick group of shell k holding voxel be
        /// crossed in one leap from t? BoxLeapOk over the group (4x4x4 bricks).
        public static bool GroupLeapOk(IMaskView v, int3 voxel, int k, float t, float tTier0, bool shadow, float3 camera,
                                       float3 start, float3 tDelta, int3 step)
        {
            int gs = k + 5;
            int gsz = 1 << gs;
            return BoxLeapOk(v, k, t, tTier0, shadow, camera, start, tDelta, step, (voxel >> gs) << gs, new int3(gsz));
        }

        /// A view over real ShellStores (their CPU directory and, with
        /// ShellStore.ShadowUploads, the shadows of every page and body upload).
        public sealed class StoreView : IColumnView
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
            public bool Tier0Holds(int3 voxel) => false;
            public ulong GroupMask(int k, int page) =>
                _stores[k].ShadowMasks[page * ShellStore.MASK_UINTS] | ((ulong)_stores[k].ShadowMasks[page * ShellStore.MASK_UINTS + 1] << 32);
            public int ColumnTop(int k, int page, int j) => ShellStore.ColumnTop(_stores[k].ShadowColTops, j, page * ShellStore.COLTOP_UINTS);
            public int Tier0FloorY => 0;   // no tier 0: every group is outside its layers
            public int Tier0CeilY => 0;
        }
    }
}
