// ==========================================
// Assets/CoreEngine/Rendering/SunCacheReference.cs
//
// TASK 249 (= AMENDMENT_8_18_CACHED_SUN_SHADOW.md sec. 6, step 247a): the CPU twin of the cached sun shadow.
// Amendment 8.18 is a DRAFT, NOT ADOPTED. Nothing in play reads this file: it exists so the run-map idea can be
// checked against today's exact sun ray (ShadowRayReference.Trace) BEFORE any GPU code (§0.1 invariant 4, "prove on
// the CPU before the GPU"). The oracle tests are SunCacheOracleTests; their table is Logs/overnight/249/oracle.txt.
//
// What is twinned (amendment section in brackets):
//   - MirrorBox: a dense byte copy of a box of the ChunkStore, updated per edited box. Stands in for the tier-0 mirror
//     (the cache is DERIVED state: the CPU stays the sole terrain writer, CLAUDE.md hard invariant).
//   - Run map lines [1.2]: one line per texel, parallel to the sun, through the texel centre, marched from the box entry
//     away from the sun with the 0.1 m voxel DDA; stored as two 16-bit depths (1/16 voxel units): `entry` = first solid,
//     `exit` = next air, at most RUN_CAP = 16 texels past entry ("buried" = entry + RUN_CAP). Packed entry | exit << 16.
//     Special values: SENTINEL (0xFFFF, 0) never built; CLEAR (no solid on the line); BLOCKED (0, 0) = far map said
//     "blocked before the window" (F is a stub here: never blocked).
//   - Cascades N0-N3 [1.3]: 512 x 512 texels of 0.05 / 0.1 / 0.2 / 0.4 m, camera bands 12.8 / 25.6 / 51.2 / 102.4 m,
//     toroidal addressing `& 511` over 32 x 32 tiles of 16 x 16 lines; tiles carry their global key, so a tile from the
//     other side of the ring is never read as this one (the §6.2 phantom-terrain trap).
//   - The lookup [3.2]: §6.5's origin rule; contact ray = ShadowRayReference.Trace capped at L_c = 8 cells; Q = its end;
//     map by the RECEIVER's camera distance with a 10 % blend band; 4 bilinear taps per slot; the run rule (shadow iff
//     depth(Q) > exit + eps, eps = 1/2 voxel); unbuilt taps (sentinel or wrong tile key) fall back to the next coarser map,
//     and past N3 to the contact ray alone. Hard shadows only (the PCSS-lite soft term of 3.2 step 5 is not twinned).
//   - Slots, demand tiles, dirty lines, crossfade [2.2-2.4]: three slots per map (prev / cur / next), a period per map,
//     the lookup blends prev and cur by alpha = frames since the swap / period; demanded tiles are built into cur first
//     (camera motion), then into next sliced over the period; dirty lines (an edited box's projection rectangle padded
//     one texel) are rebuilt first, in every live slot.
//
// Coordinates: integer voxel cells (bitwise: tile = texel >> 4, storage = (texel + offset) & 511). Float only appears in
// sun space, relative to the slot's integer anchor (the camera's chunk), so no large floats enter addressing.
// ==========================================
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using VoxelEngine.Memory;

namespace VoxelEngine.Rendering
{
    public sealed class SunCacheReference
    {
        // ---------------- constants (amendment 8.18 sec. 1.2-1.3, 2.2, 3.2) ----------------
        public const int RES = 512, RES_MASK = 511, TILE_SHIFT = 4, TILE = 16, TILES = 32, TILES_MASK = 31;
        public const int RUN_CAP = 16;                 // texels of THIS map
        public const int DEPTH_UNIT = 16;              // 1/16 voxel = 6.25 mm
        public const float DEPTH_BASE_VOX = 2048f;     // depth origin: 204.8 m sunward of the anchor plane
        public const int CONTACT_CELLS = 8;            // L_c, 0.8 m
        public const float EPS_VOX = 0.5f;             // run-rule eps
        public const int MAPS = 4;
        public static readonly float[] TEXEL_VOX = { 0.5f, 1f, 2f, 4f };
        public static readonly float[] MAP_OUTER_VOX = { 128f, 256f, 512f, 1024f };
        public static readonly int[] PERIOD = { 16, 32, 64, 64 };
        public const float BLEND_BAND = 0.1f;
        /// TASK 249 finding: line centres sit at (g + 0.5) x texel from an INTEGER anchor; for N2 / N3 (2 / 4 voxel texels)
        /// that is a whole voxel, so with the sun exactly on an axis in azimuth (U or V axis-aligned) a line runs exactly
        /// along a voxel face and the DDA picks the side by rounding (80 false shadows beside a wall at az 0, N2). Shifting
        /// every map's lines by 1/8 voxel in U and V keeps every centre off the voxel planes for all four texel sizes
        /// (0.5 g + 0.375, g + 0.625, 2 g + 1.125, 4 g + 2.125). Folded into UVOf, so lines and lookups agree by construction.
        public const float LINE_OFFSET_VOX = 0.125f;

        public const uint SENTINEL = 0x0000FFFFu;      // entry 0xFFFF, exit 0
        public const uint CLEAR = 0xFFFEFFFEu;         // no solid voxel on the line
        public const uint BLOCKED = 0u;                // entry = exit = 0 (far map; stub here)

        // ---------------- the mirror twin ----------------
        public sealed class MirrorBox
        {
            public readonly int3 lo, size;
            public readonly byte[] v;
            public MirrorBox(int3 lo, int3 size) { this.lo = lo; this.size = size; v = new byte[size.x * size.y * size.z]; }
            public bool Solid(int3 c)
            {
                int3 l = c - lo;
                if ((uint)l.x >= (uint)size.x || (uint)l.y >= (uint)size.y || (uint)l.z >= (uint)size.z) return false;
                return v[(l.z * size.y + l.y) * size.x + l.x] != 0;
            }
            /// Copy [boxLo, boxHi) (clipped to the mirror) from the store: the twin of TerrainClipmap's UploadDirty.
            public void Upload(ChunkStore store, int3 boxLo, int3 boxHi)
            {
                int3 a = math.max(boxLo, lo), b = math.min(boxHi, lo + size);
                for (int z = a.z; z < b.z; z++)
                for (int y = a.y; y < b.y; y++)
                for (int x = a.x; x < b.x; x++)
                    v[((z - lo.z) * size.y + (y - lo.y)) * size.x + (x - lo.x)] = store.GetVoxel(new int3(x, y, z));
            }
            public void UploadAll(ChunkStore store) => Upload(store, lo, lo + size);
        }

        // ---------------- a slot: one sun direction, one anchor, one 512 x 512 run map ----------------
        public sealed class Slot
        {
            public float3 s, U, V;          // s toward the sun; U, V span the map plane
            public int3 anchor;             // integer voxel (the camera's chunk)
            public int2 offset;             // storage offset (toroidal origin); tests move it to cross 511 -> 0
            public readonly uint[] tex = new uint[RES * RES];
            public readonly long[] tileKey = new long[TILES * TILES];
            public int createdFrame;
            public Slot() { for (int i = 0; i < tileKey.Length; i++) tileKey[i] = long.MinValue; }
        }

        public sealed class MapState
        {
            public Slot prev, cur, next;
            public int swapFrame;
            public readonly List<float3> demandPts = new List<float3>();     // receivers' contact ends demanded this frame
        }

        public readonly MirrorBox mirror;
        public readonly MapState[] maps = new MapState[MAPS];
        public bool lazy;                  // oracle mode: a lookup builds any line it needs (no budget)
        public int frame;

        // ---------------- counters (the step-count proxy and the sentinel checks) ----------------
        public long linesBuilt, lineSteps, dirtyLinesQueued, dirtyLinesBuilt, tilesBuilt;
        public long lookups, contactOnlyLookups, fallbackReads;
        public readonly long[] mapReads = new long[MAPS];

        /// The texel words the last lookup actually WEIGHTED (both slots, all maps it blended): a test re-reads them to
        /// prove no sentinel ever entered a visibility.
        public readonly List<uint> lastTaps = new List<uint>(32);

        public SunCacheReference(MirrorBox mirror)
        {
            this.mirror = mirror;
            for (int k = 0; k < MAPS; k++) maps[k] = new MapState();
        }

        // =====================================================================
        // Slots
        // =====================================================================

        public static Slot NewSlot(float3 sunDir, int3 anchor, int2 offset = default, int frame = 0)
        {
            var sl = new Slot { anchor = anchor, offset = offset, createdFrame = frame };
            sl.s = math.normalize(sunDir);
            float3 helper = math.abs(sl.s.y) < 0.99f ? new float3(0, 1, 0) : new float3(1, 0, 0);
            sl.U = math.normalize(math.cross(helper, sl.s));
            sl.V = math.cross(sl.s, sl.U);
            for (int i = 0; i < sl.tex.Length; i++) sl.tex[i] = SENTINEL;
            return sl;
        }

        /// Oracle mode: one slot per map with this sun, no crossfade, lines built on demand by the lookup.
        public void BuildStatic(float3 sunDir, int3 anchor, int2 offset = default)
        {
            lazy = true;
            for (int k = 0; k < MAPS; k++)
            {
                maps[k].prev = null; maps[k].next = null;
                maps[k].cur = NewSlot(sunDir, anchor, offset, frame);
                maps[k].swapFrame = frame - PERIOD[k];      // alpha = 1
            }
        }

        public float Alpha(int k) => math.saturate((frame - maps[k].swapFrame) / (float)PERIOD[k]);

        // ---------------- addressing (bitwise, CLAUDE.md) ----------------
        static long Key(int tu, int tv) => ((long)tu << 32) ^ (uint)tv;
        static int StorageTile(Slot sl, int gu, int gv)
            => ((((gv + sl.offset.y) >> TILE_SHIFT) & TILES_MASK) << 5) | (((gu + sl.offset.x) >> TILE_SHIFT) & TILES_MASK);
        static long GlobalTileKey(Slot sl, int gu, int gv)
            => Key((gu + sl.offset.x) >> TILE_SHIFT, (gv + sl.offset.y) >> TILE_SHIFT);
        public static int StorageIndex(Slot sl, int gu, int gv)
            => (((gv + sl.offset.y) & RES_MASK) << 9) | ((gu + sl.offset.x) & RES_MASK);

        public static bool TileBuilt(Slot sl, int gu, int gv) => sl.tileKey[StorageTile(sl, gu, gv)] == GlobalTileKey(sl, gu, gv);

        /// Claim the storage tile for (gu, gv)'s global tile: a different key there is evicted (all its lines -> sentinel).
        static void ClaimTile(Slot sl, int gu, int gv)
        {
            int st = StorageTile(sl, gu, gv);
            long key = GlobalTileKey(sl, gu, gv);
            if (sl.tileKey[st] == key) return;
            sl.tileKey[st] = key;
            int bu = ((gu + sl.offset.x) >> TILE_SHIFT) << TILE_SHIFT, bv = ((gv + sl.offset.y) >> TILE_SHIFT) << TILE_SHIFT;
            for (int j = 0; j < TILE; j++)
            for (int i = 0; i < TILE; i++)
                sl.tex[(((bv + j) & RES_MASK) << 9) | ((bu + i) & RES_MASK)] = SENTINEL;
        }

        /// The texel word, or SENTINEL when its tile is not this slot's (never built, or the other side of the ring).
        public static uint Peek(Slot sl, int gu, int gv) => TileBuilt(sl, gu, gv) ? sl.tex[StorageIndex(sl, gu, gv)] : SENTINEL;

        // ---------------- sun space ----------------
        public static float DepthOf(Slot sl, float3 pVox) => math.dot((float3)sl.anchor - pVox, sl.s) + DEPTH_BASE_VOX;
        public static float2 UVOf(Slot sl, float3 pVox) { float3 r = pVox - (float3)sl.anchor; return new float2(math.dot(r, sl.U), math.dot(r, sl.V)) - LINE_OFFSET_VOX; }

        /// The point where line (gu, gv) of map k crosses the anchor plane (depth = DEPTH_BASE_VOX).
        public static float3 LineOrigin(Slot sl, int k, int gu, int gv)
        {
            float tx = TEXEL_VOX[k];
            return (float3)sl.anchor + sl.U * ((gu + 0.5f) * tx + LINE_OFFSET_VOX) + sl.V * ((gv + 0.5f) * tx + LINE_OFFSET_VOX);
        }

        // =====================================================================
        // The line build (sec. 1.2): the twin of CSSunCacheBuild's body
        // =====================================================================

        public uint BuildLine(Slot sl, int k, int gu, int gv, out int steps)
        {
            steps = 0;
            float tx = TEXEL_VOX[k];
            float3 O = LineOrigin(sl, k, gu, gv);   // in the anchor plane: depth(O) = BASE
            float3 d = -sl.s;
            if (!SlabBox(O, d, mirror.lo, mirror.lo + mirror.size, out float t0, out float t1)) return CLEAR;

            // DDA from the box entry
            float3 p = O + d * t0;
            int3 c = (int3)math.floor(p + d * 1e-4f);
            c = math.clamp(c, mirror.lo, mirror.lo + mirror.size - 1);
            int3 st = new int3(d.x > 0 ? 1 : (d.x < 0 ? -1 : 0), d.y > 0 ? 1 : (d.y < 0 ? -1 : 0), d.z > 0 ? 1 : (d.z < 0 ? -1 : 0));
            float3 inv = new float3(d.x != 0 ? 1f / d.x : float.PositiveInfinity, d.y != 0 ? 1f / d.y : float.PositiveInfinity, d.z != 0 ? 1f / d.z : float.PositiveInfinity);
            float3 tDelta = math.abs(inv);
            float3 tm = new float3(
                st.x > 0 ? (c.x + 1 - O.x) * inv.x : st.x < 0 ? (c.x - O.x) * inv.x : float.PositiveInfinity,
                st.y > 0 ? (c.y + 1 - O.y) * inv.y : st.y < 0 ? (c.y - O.y) * inv.y : float.PositiveInfinity,
                st.z > 0 ? (c.z + 1 - O.z) * inv.z : st.z < 0 ? (c.z - O.z) * inv.z : float.PositiveInfinity);
            float tCell = t0;                 // t where the current cell was entered
            float cap = RUN_CAP * tx;
            bool inRun = false; float tEntry = 0f;
            int guard = 4 * (mirror.size.x + mirror.size.y + mirror.size.z) + 8;
            while (guard-- > 0)
            {
                steps++;
                bool solid = mirror.Solid(c);
                if (!inRun && solid) { inRun = true; tEntry = tCell; }
                else if (inRun && !solid) return Pack(tEntry, tCell);                    // exit = first air after the run
                if (inRun && tCell - tEntry >= cap) return Pack(tEntry, tEntry + cap);   // buried
                // advance
                float tNext;
                if (tm.x < tm.y) { if (tm.x < tm.z) { c.x += st.x; tNext = tm.x; tm.x += tDelta.x; } else { c.z += st.z; tNext = tm.z; tm.z += tDelta.z; } }
                else { if (tm.y < tm.z) { c.y += st.y; tNext = tm.y; tm.y += tDelta.y; } else { c.z += st.z; tNext = tm.z; tm.z += tDelta.z; } }
                tCell = tNext;
                if (tCell >= t1) break;
            }
            return inRun ? Pack(tEntry, tEntry + cap) : CLEAR;   // left the box while solid = buried
        }

        static uint Pack(float tEntry, float tExit)
        {
            // depth = BASE + t (O lies in the anchor plane). Entry rounds DOWN, exit UP: the stored run never shrinks.
            int qe = (int)math.floor((DEPTH_BASE_VOX + tEntry) * DEPTH_UNIT);
            int qx = (int)math.ceil((DEPTH_BASE_VOX + tExit) * DEPTH_UNIT);
            if (qe < 1 || qx > 0xFFFD || qx < qe)
                throw new InvalidOperationException($"SunCacheReference: depth out of 16-bit range ({qe}, {qx})");
            return (uint)qe | ((uint)qx << 16);
        }

        public static float EntryVox(uint w) => (w & 0xFFFF) / (float)DEPTH_UNIT;
        public static float ExitVox(uint w) => (w >> 16) / (float)DEPTH_UNIT;

        void BuildIntoSlot(Slot sl, int k, int gu, int gv)
        {
            uint w = BuildLine(sl, k, gu, gv, out int steps);
            sl.tex[StorageIndex(sl, gu, gv)] = w;
            linesBuilt++; lineSteps += steps;
        }

        public void BuildTile(Slot sl, int k, int tu, int tv)
        {
            int gu0 = (tu << TILE_SHIFT) - sl.offset.x, gv0 = (tv << TILE_SHIFT) - sl.offset.y;
            ClaimTile(sl, gu0, gv0);
            for (int j = 0; j < TILE; j++)
            for (int i = 0; i < TILE; i++)
                BuildIntoSlot(sl, k, gu0 + i, gv0 + j);
            tilesBuilt++;
        }

        // =====================================================================
        // The lookup (sec. 3.2)
        // =====================================================================

        public struct Result
        {
            public float vis;              // 0 = shadow, 1 = lit (bilinear between)
            public bool contactBlocked;
            public bool contactOnly;       // no built map anywhere: contact ray alone (2.5)
            public int map;                // the picked map (by receiver distance)
            public int mapRead;            // the map actually read (>= map after fallbacks)
            public float3 Q;               // contact ray end, voxel units
            public int contactSteps;
        }

        public static int PickMap(float receiverCamDistVox)
        {
            for (int k = 0; k < MAPS; k++) if (receiverCamDistVox < MAP_OUTER_VOX[k]) return k;
            return MAPS - 1;
        }

        /// The contact ray: §6.5's origin, today's exact DDA toward the sun for L_c cells (ShadowRayReference itself).
        public static ShadowRayReference.ShadowResult Contact(ChunkStore store, int3 hitVoxel, int3 normal, float3 sunDir, out float3 Q)
        {
            float3 s = math.normalize(sunDir);
            float3 originM = ShadowRayReference.ShadowOriginWorld(hitVoxel, normal);
            var r = ShadowRayReference.Trace(store, hitVoxel, normal, s, originM, kSoft: 0f, maxDistM: CONTACT_CELLS * 0.1f);
            Q = originM * 10f + s * CONTACT_CELLS;
            return r;
        }

        public Result Lookup(ChunkStore store, int3 hitVoxel, int3 normal, float3 sunNow, float receiverCamDistVox, int forceMap = -1)
        {
            lookups++;
            lastTaps.Clear();
            var res = new Result { map = forceMap >= 0 ? forceMap : PickMap(receiverCamDistVox) };
            var c = Contact(store, hitVoxel, normal, sunNow, out res.Q);
            res.contactSteps = c.steps;
            if (c.skipped) { res.vis = 0f; res.contactBlocked = true; return res; }
            if (c.occluded) { res.vis = 0f; res.contactBlocked = true; res.mapRead = res.map; return res; }
            res.vis = SampleCascade(res.Q, res.map, forceMap < 0 ? receiverCamDistVox : -1f, out res.mapRead, out res.contactOnly);
            return res;
        }

        /// Map k (falling back to coarser maps), with the 10 % blend into k + 1 near k's outer edge. distVox < 0 = no blend.
        public float SampleCascade(float3 Q, int k, float distVox, out int mapRead, out bool contactOnly)
        {
            contactOnly = false;
            int m = k;
            float v = 0f;
            for (; m < MAPS; m++)
            {
                if (SampleMap(m, Q, out v)) break;
                fallbackReads++;
            }
            if (m >= MAPS) { mapRead = MAPS; contactOnly = true; contactOnlyLookups++; return 1f; }
            mapRead = m; mapReads[m]++;
            if (distVox >= 0f && m == k && m + 1 < MAPS)
            {
                float outer = MAP_OUTER_VOX[m], start = outer * (1f - BLEND_BAND);
                if (distVox > start && SampleMap(m + 1, Q, out float v2))
                    v = math.lerp(v, v2, math.saturate((distVox - start) / (outer * BLEND_BAND)));
            }
            return v;   // x (1 - F at Q): F is a stub (never blocked) in this twin
        }

        /// One map, crossfaded prev -> cur. False = cur's taps are not all built (the caller falls back).
        public bool SampleMap(int k, float3 Q, out float vis)
        {
            vis = 0f;
            var ms = maps[k];
            if (ms.cur == null) return false;
            int tapStart = lastTaps.Count;
            if (!SampleSlot(ms.cur, k, Q, out float vc)) { lastTaps.RemoveRange(tapStart, lastTaps.Count - tapStart); return false; }
            float a = Alpha(k);
            if (ms.prev != null && a < 1f)
            {
                int t2 = lastTaps.Count;
                if (SampleSlot(ms.prev, k, Q, out float vp)) { vis = math.lerp(vp, vc, a); return true; }
                lastTaps.RemoveRange(t2, lastTaps.Count - t2);
            }
            vis = vc;
            return true;
        }

        bool SampleSlot(Slot sl, int k, float3 Q, out float vis)
        {
            vis = 0f;
            float tx = TEXEL_VOX[k];
            float2 uv = UVOf(sl, Q) / tx - 0.5f;
            int2 i0 = (int2)math.floor(uv);
            float2 w = uv - i0;
            float dq = DepthOf(sl, Q);
            float acc = 0f;
            for (int j = 0; j < 2; j++)
            for (int i = 0; i < 2; i++)
            {
                int gu = i0.x + i, gv = i0.y + j;
                uint word;
                if (lazy)
                {
                    if (!TileBuilt(sl, gu, gv)) ClaimTile(sl, gu, gv);
                    int si = StorageIndex(sl, gu, gv);
                    if (sl.tex[si] == SENTINEL) BuildIntoSlot(sl, k, gu, gv);
                    word = sl.tex[si];
                }
                else
                {
                    word = Peek(sl, gu, gv);
                    if (word == SENTINEL) return false;
                }
                lastTaps.Add(word);
                float wt = (i == 0 ? 1f - w.x : w.x) * (j == 0 ? 1f - w.y : w.y);
                acc += wt * (RunLit(word, dq) ? 1f : 0f);
            }
            vis = acc;
            return true;
        }

        /// The run rule (sec. 1.2): shadow iff depth(Q) > exit + eps. Q inside the first run = the receiver's own surface.
        public static bool RunLit(uint word, float depthQ)
        {
            if (word == CLEAR) return true;
            if (word == SENTINEL) throw new InvalidOperationException("SunCacheReference: a sentinel reached the run rule");
            return depthQ <= ExitVox(word) + EPS_VOX;
        }

        // =====================================================================
        // Frames: slots, demand tiles, the line budget, dirty lines (sec. 2.2-2.4)
        // =====================================================================

        public struct DirtyLine { public Slot slot; public int map, gu, gv; }
        readonly Queue<DirtyLine> _dirty = new Queue<DirtyLine>();
        public int DirtyPending => _dirty.Count;

        /// Start a frame. `sunForNewSlot(k)` = the sun direction a slot created now for map k is built with (the
        /// amendment: the sun of this moment; the tests also try a predicted sun). Rotates prev <- cur <- next per period.
        public void BeginFrame(int frameIndex, Func<int, float3> sunForNewSlot, int3 cameraAnchor)
        {
            lazy = false;
            frame = frameIndex;
            for (int k = 0; k < MAPS; k++)
            {
                var ms = maps[k];
                ms.demandPts.Clear();
                if (ms.cur == null)
                {
                    ms.cur = NewSlot(sunForNewSlot(k), cameraAnchor, default, frame);
                    ms.next = NewSlot(sunForNewSlot(k), cameraAnchor, default, frame);
                    ms.swapFrame = frame;      // no prev yet: the lookup reads cur alone
                }
                else if (frame - ms.swapFrame >= PERIOD[k])
                {
                    ms.prev = ms.cur; ms.cur = ms.next;
                    ms.next = NewSlot(sunForNewSlot(k), cameraAnchor, default, frame);
                    ms.swapFrame = frame;
                }
            }
        }

        /// Demand the tiles a receiver whose contact end is Q (camera distance distVox) can read: its map's taps, the
        /// blend partner, and N3 (kept built as the fallback, 2.3).
        public void DemandReceiver(float3 Q, float distVox)
        {
            int k = PickMap(distVox);
            DemandMapAt(k, Q);
            if (k + 1 < MAPS && distVox > MAP_OUTER_VOX[k] * (1f - BLEND_BAND)) DemandMapAt(k + 1, Q);
            if (k != MAPS - 1) DemandMapAt(MAPS - 1, Q);
        }

        void DemandMapAt(int k, float3 Q) => maps[k].demandPts.Add(Q);

        /// The global tile keys a slot needs for the demanded points (each point's 4 bilinear taps), nearest the camera first.
        static List<long> DemandedTiles(Slot sl, int k, List<float3> pts, float3 cameraVox)
        {
            var set = new HashSet<long>();
            float tx = TEXEL_VOX[k];
            foreach (var Q in pts)
            {
                int2 i0 = (int2)math.floor(UVOf(sl, Q) / tx - 0.5f);
                for (int j = 0; j < 2; j++) for (int i = 0; i < 2; i++)
                    set.Add(Key((i0.x + i) >> TILE_SHIFT, (i0.y + j) >> TILE_SHIFT));
            }
            var keys = new List<long>(set);
            float2 cuv = UVOf(sl, cameraVox) / (tx * TILE);
            keys.Sort((a, b) =>
            {
                float da = math.lengthsq(new float2((int)(a >> 32) + 0.5f, (int)(uint)a + 0.5f) - cuv);
                float db = math.lengthsq(new float2((int)(b >> 32) + 0.5f, (int)(uint)b + 0.5f) - cuv);
                return da.CompareTo(db);
            });
            return keys;
        }

        /// Build under a line budget: dirty lines first, then demanded tiles missing from cur (nearest first: camera
        /// motion), then next's demanded tiles sliced over the period. Returns lines built this frame.
        /// (Frame mode keeps every slot's storage offset at 0.)
        public int BuildBudgeted(int lineBudget, float3 cameraVox)
        {
            int built = 0;
            while (_dirty.Count > 0 && built < lineBudget)
            {
                var dl = _dirty.Dequeue();
                if (Peek(dl.slot, dl.gu, dl.gv) == SENTINEL) continue;   // its tile was evicted meanwhile
                BuildIntoSlot(dl.slot, dl.map, dl.gu, dl.gv); dirtyLinesBuilt++; built++;
            }
            // cur: every demanded tile, nearest first
            for (int k = 0; k < MAPS; k++)
            {
                var ms = maps[k];
                foreach (long key in DemandedTiles(ms.cur, k, ms.demandPts, cameraVox))
                {
                    int tu = (int)(key >> 32), tv = (int)(uint)key;
                    if (TileBuilt(ms.cur, tu << TILE_SHIFT, tv << TILE_SHIFT)) continue;
                    if (built + TILE * TILE > lineBudget) break;
                    BuildTile(ms.cur, k, tu, tv); built += TILE * TILE;
                }
            }
            // next: a slice of the demanded tiles per frame (all of them by the end of the period)
            for (int k = 0; k < MAPS; k++)
            {
                var ms = maps[k];
                var keys = DemandedTiles(ms.next, k, ms.demandPts, cameraVox);
                int missing = 0;
                foreach (long key in keys) if (!TileBuilt(ms.next, (int)(key >> 32) << TILE_SHIFT, (int)(uint)key << TILE_SHIFT)) missing++;
                int framesLeft = math.max(1, PERIOD[k] - (frame - ms.swapFrame));
                int quota = (missing + framesLeft - 1) / framesLeft;
                foreach (long key in keys)
                {
                    if (quota <= 0 || built + TILE * TILE > lineBudget) break;
                    int tu = (int)(key >> 32), tv = (int)(uint)key;
                    if (TileBuilt(ms.next, tu << TILE_SHIFT, tv << TILE_SHIFT)) continue;
                    BuildTile(ms.next, k, tu, tv); built += TILE * TILE; quota--;
                }
            }
            return built;
        }

        /// An edited voxel box [lo, hi): queue every BUILT line through its projection rectangle (padded one texel) in
        /// every live slot of every map. Returns the lines queued. (The mirror must already hold the edit: frame order
        /// CPU edit -> UploadDirty -> build -> lookup, 2.4.)
        public int MarkDirtyBox(int3 lo, int3 hi)
        {
            int n = 0;
            for (int k = 0; k < MAPS; k++)
            {
                var ms = maps[k];
                foreach (var sl in new[] { ms.prev, ms.cur, ms.next })
                {
                    if (sl == null) continue;
                    float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
                    for (int c = 0; c < 8; c++)
                    {
                        float3 p = new float3((c & 1) != 0 ? hi.x : lo.x, (c & 2) != 0 ? hi.y : lo.y, (c & 4) != 0 ? hi.z : lo.z);
                        float2 uv = UVOf(sl, p);
                        mn = math.min(mn, uv); mx = math.max(mx, uv);
                    }
                    float tx = TEXEL_VOX[k];
                    int2 a = (int2)math.floor(mn / tx - 0.5f) - 1, b = (int2)math.ceil(mx / tx - 0.5f) + 1;
                    for (int gv = a.y; gv <= b.y; gv++)
                    for (int gu = a.x; gu <= b.x; gu++)
                        if (Peek(sl, gu, gv) != SENTINEL) { _dirty.Enqueue(new DirtyLine { slot = sl, map = k, gu = gu, gv = gv }); n++; }
                }
            }
            dirtyLinesQueued += n;
            return n;
        }

        /// Oracle mode: rebuild every queued dirty line now.
        public int ProcessDirtyNow()
        {
            int n = 0;
            while (_dirty.Count > 0)
            {
                var dl = _dirty.Dequeue();
                if (Peek(dl.slot, dl.gu, dl.gv) == SENTINEL) continue;
                BuildIntoSlot(dl.slot, dl.map, dl.gu, dl.gv); dirtyLinesBuilt++; n++;
            }
            return n;
        }

        // =====================================================================
        // Helpers shared with the tests
        // =====================================================================

        public static bool SlabBox(float3 o, float3 d, float3 lo, float3 hi, out float t0, out float t1)
        {
            t0 = float.NegativeInfinity; t1 = float.PositiveInfinity;
            for (int a = 0; a < 3; a++)
            {
                if (d[a] == 0f) { if (o[a] < lo[a] || o[a] >= hi[a]) return false; continue; }
                float ta = (lo[a] - o[a]) / d[a], tb = (hi[a] - o[a]) / d[a];
                if (ta > tb) { float x = ta; ta = tb; tb = x; }
                t0 = math.max(t0, ta); t1 = math.min(t1, tb);
            }
            return t0 < t1;
        }

        /// Exact 0.1 m DDA from an arbitrary point toward the sun through the mirror box (the starting cell included).
        /// True = blocked. `steps` = cells visited inside the box. Used for edge bands and the step proxy.
        /// `skipStartRun`: a point that starts inside solid first walks out of it (it stands on that surface, the edge-band
        /// question "would a point one texel over, on this surface, see the sun"); the solid it starts in does not block.
        public static bool ExactRay(MirrorBox m, float3 p, float3 dir, out int steps, bool skipStartRun = false)
        {
            bool skipping = skipStartRun;
            steps = 0;
            float3 d = math.normalize(dir);
            float3 lo = m.lo, hi = m.lo + m.size;
            if (!SlabBox(p, d, lo, hi, out float t0, out float t1) || t1 <= 0f) return false;
            float tStart = math.max(0f, t0);
            float3 ps = p + d * tStart;
            int3 c = (int3)math.floor(tStart > 0f ? ps + d * 1e-4f : ps);
            int3 st = new int3(d.x > 0 ? 1 : (d.x < 0 ? -1 : 0), d.y > 0 ? 1 : (d.y < 0 ? -1 : 0), d.z > 0 ? 1 : (d.z < 0 ? -1 : 0));
            float3 inv = new float3(d.x != 0 ? 1f / d.x : float.PositiveInfinity, d.y != 0 ? 1f / d.y : float.PositiveInfinity, d.z != 0 ? 1f / d.z : float.PositiveInfinity);
            float3 tDelta = math.abs(inv);
            float3 tm = new float3(
                st.x > 0 ? (c.x + 1 - p.x) * inv.x : st.x < 0 ? (c.x - p.x) * inv.x : float.PositiveInfinity,
                st.y > 0 ? (c.y + 1 - p.y) * inv.y : st.y < 0 ? (c.y - p.y) * inv.y : float.PositiveInfinity,
                st.z > 0 ? (c.z + 1 - p.z) * inv.z : st.z < 0 ? (c.z - p.z) * inv.z : float.PositiveInfinity);
            int guard = 4 * (m.size.x + m.size.y + m.size.z) + 8;
            while (guard-- > 0)
            {
                steps++;
                if (m.Solid(c)) { if (!skipping) return true; }
                else skipping = false;
                float tNext;
                if (tm.x < tm.y) { if (tm.x < tm.z) { c.x += st.x; tNext = tm.x; tm.x += tDelta.x; } else { c.z += st.z; tNext = tm.z; tm.z += tDelta.z; } }
                else { if (tm.y < tm.z) { c.y += st.y; tNext = tm.y; tm.y += tDelta.y; } else { c.z += st.z; tNext = tm.z; tm.z += tDelta.z; } }
                if (tNext >= t1) return false;
            }
            return false;
        }
    }
}
