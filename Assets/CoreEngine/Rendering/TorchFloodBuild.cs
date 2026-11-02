// ==========================================
// Assets/CoreEngine/Rendering/TorchFloodBuild.cs
//
// Task 165 (TORCH_LIGHT_PLAN.md step L3; owner doc Q101 1 (a) + 2 (a)): the
// INCREMENTAL torch flood. TorchFloodPool drives it; this file is the data and
// the Burst jobs.
//
// THE QUANTITY is task 157's (LightFloodReference header): per torch t the
// cheapest valid 4/6/7 chamfer path d_t(v) through air with no corner cutting;
// light_c(v) = max over torches of max(0, seed_c(t) - d_t(v)). The GPU form is
// task 158's (TorchFlood.hlsl): 0.2 m mixed cells (per-channel max of the cell's
// voxels; a SPLIT cell escapes to its 8 voxels) and 0.4 m air-brick cells.
//
// WHY PER-TORCH CACHES. Max over torches is not invertible, so removing torch A
// from the combined field needs the other torches' values. Each torch keeps its
// OWN flood, already in the GPU's compressed form, as a min path cost (one
// ushort per cell, not RGB light): a mixed cell = min d over its voxels, an
// escaped cell = its 8 voxels' d, an air cell = min d over its 2x2x2 centre.
// Because light = seed - d is monotone and the cells are MAXES (min of d), the
// combined cell = max over torches of (seed - cell d) EXACTLY. (Task 158 stored an
// air cell as the MEAN of its 8 centre voxels; a mean of a max does not
// decompose, so L3 stores the max: at most one corner move, 7 levels, brighter;
// TorchFloodPool.Encode uses the same rule, so the oracle path and this one agree
// byte for byte.) An edit re-floods only the torches whose box it touches; a
// changed torch re-merges only the bricks it lit before or lights now.
//
// "DIRECT" BRICKS (pure-air bricks flooded as a unit). d_t(v) >= C(v - t), the
// closed-form chamfer distance (4 |hi| + 2 |mid| + |lo|). If an all-air brick B
// has every voxel of bbox(t, v) open for every v in B (checked at brick level by
// a summed-area table of not-all-air bricks, plus 27 sign cases inside the
// torch's own brick), the monotone chamfer path is valid, so d = C exactly: no
// search and no storage, one bit. Only the rest is searched (Dial's buckets, as
// the oracle), seeded from the direct bricks' faces that touch it. Outdoors that
// is the slab between the ground and the torch's brick layer.
//
// SINGLE WRITER. Snapshot() runs on the MAIN thread: it reads ChunkStore into a
// scratch the job then owns (brick kinds + 1 bit per voxel of mixed bricks).
// FloodBrickJob (worker) reads only that scratch and writes only its own outputs.
// MergeJob (worker) reads immutable caches, writes its own output. ApplyJob runs
// on the main thread (Burst .Run) and is the only writer of the union.
// ==========================================

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngine.Memory;

namespace VoxelEngine.Rendering
{
    /// One torch's flood in the compressed form, as min path cost per cell.
    public sealed unsafe class TorchFloodCache : IDisposable
    {
        public const ushort NONE = 0x7FFF, ESC = 0x8000;
        public int3 torch, lo, db;          // lo: box origin voxel (brick aligned); db: box dims in bricks
        public uint seed; public int top;
        public long serial;
        public NativeList<ulong> direct;    // bit per dense box brick: lit and d = C(v - t) exactly
        public NativeList<int> keys;        // sorted dense box brick indices of the stored lit bricks
        public NativeList<uint> entries;    // kind << 30 | offset into data
        public NativeList<ushort> data;     // air: 8 cells; mixed: 64 cells (ESC | k) + 8 per escape
        public int reached, directBricks, storedBricks, escapes, seeds;
        public double floodMs;

        public TorchFloodCache()
        {
            direct = new NativeList<ulong>(0, Allocator.Persistent);
            keys = new NativeList<int>(0, Allocator.Persistent);
            entries = new NativeList<uint>(0, Allocator.Persistent);
            data = new NativeList<ushort>(0, Allocator.Persistent);
        }

        public int LitBricks => directBricks + storedBricks;
        public long Bytes => direct.IsCreated ? 8L * direct.Capacity + 4L * keys.Capacity + 4L * entries.Capacity + 2L * data.Capacity : 0;

        public TorchFloodBuild.CacheDesc Desc() => new TorchFloodBuild.CacheDesc
        {
            torch = torch, lo = lo, db = db, seed = seed, top = top, serial = serial,
            direct = (ulong*)direct.GetUnsafeReadOnlyPtr(), keys = (int*)keys.GetUnsafeReadOnlyPtr(),
            entries = (uint*)entries.GetUnsafeReadOnlyPtr(), data = (ushort*)data.GetUnsafeReadOnlyPtr(), nKeys = keys.Length,
        };

        public void Dispose()
        {
            if (direct.IsCreated) direct.Dispose();
            if (keys.IsCreated) keys.Dispose();
            if (entries.IsCreated) entries.Dispose();
            if (data.IsCreated) data.Dispose();
        }
    }

    /// A flood job's scratch: built by Snapshot (main thread), then owned by ONE job.
    public sealed class TorchFloodScratch : IDisposable
    {
        public int shift = -1, P;
        public NativeArray<byte> kind;      // packed brick: 0 closed, 1 all air, 2 mixed
        public NativeArray<int> maskOff;    // packed brick -> 8 ulongs in masks (mixed only)
        public NativeArray<int> slot;       // packed brick -> 512-block in dist (-1 none)
        public NativeArray<byte> dirB;      // packed brick: direct
        public NativeList<ulong> masks;
        public NativeList<ushort> dist;
        public NativeList<ulong> pad;       // 16 per dist block: the 10^3 open bits round that brick
        public NativeList<int> sat;
        public NativeList<int2> dense;      // (packed brick, brick pool index) of dense bricks to convert
        public NativeList<int> b0, b1, b2, b3, b4, b5, b6, b7;
        public NativeList<int2> seeds;

        public TorchFloodScratch()
        {
            masks = new NativeList<ulong>(1024, Allocator.Persistent);
            dist = new NativeList<ushort>(4096, Allocator.Persistent);
            pad = new NativeList<ulong>(128, Allocator.Persistent);
            sat = new NativeList<int>(1024, Allocator.Persistent);
            dense = new NativeList<int2>(1024, Allocator.Persistent);
            b0 = new NativeList<int>(1024, Allocator.Persistent); b1 = new NativeList<int>(1024, Allocator.Persistent);
            b2 = new NativeList<int>(1024, Allocator.Persistent); b3 = new NativeList<int>(1024, Allocator.Persistent);
            b4 = new NativeList<int>(1024, Allocator.Persistent); b5 = new NativeList<int>(1024, Allocator.Persistent);
            b6 = new NativeList<int>(1024, Allocator.Persistent); b7 = new NativeList<int>(1024, Allocator.Persistent);
            seeds = new NativeList<int2>(1024, Allocator.Persistent);
        }

        public void Ensure(int s)
        {
            if (s == shift) return;
            if (kind.IsCreated) { kind.Dispose(); maskOff.Dispose(); slot.Dispose(); dirB.Dispose(); }
            shift = s; P = 1 << (3 * s);
            kind = new NativeArray<byte>(P, Allocator.Persistent);
            maskOff = new NativeArray<int>(P, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            slot = new NativeArray<int>(P, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            dirB = new NativeArray<byte>(P, Allocator.Persistent);
        }

        public long Bytes
        {
            get
            {
                long b = kind.IsCreated ? 10L * P : 0;
                b += 8L * masks.Capacity + 8L * pad.Capacity + 2L * dist.Capacity + 4L * sat.Capacity + 8L * dense.Capacity + 8L * seeds.Capacity;
                b += 4L * (b0.Capacity + b1.Capacity + b2.Capacity + b3.Capacity + b4.Capacity + b5.Capacity + b6.Capacity + b7.Capacity);
                return b;
            }
        }

        public void Dispose()
        {
            if (kind.IsCreated) { kind.Dispose(); maskOff.Dispose(); slot.Dispose(); dirB.Dispose(); }
            masks.Dispose(); dist.Dispose(); pad.Dispose(); sat.Dispose(); dense.Dispose(); seeds.Dispose();
            b0.Dispose(); b1.Dispose(); b2.Dispose(); b3.Dispose(); b4.Dispose(); b5.Dispose(); b6.Dispose(); b7.Dispose();
        }
    }

    public static unsafe class TorchFloodBuild
    {
        public const uint KIND_AIR = 1u, KIND_MIXED = 2u, ESCAPE_BIT = 0x40000000u, OFFSET_MASK = 0x3FFFFFFFu;

        public struct CacheDesc
        {
            public int3 torch, lo, db; public uint seed; public int top; public long serial;
            [NativeDisableUnsafePtrRestriction] public ulong* direct;
            [NativeDisableUnsafePtrRestriction] public int* keys;
            [NativeDisableUnsafePtrRestriction] public uint* entries;
            [NativeDisableUnsafePtrRestriction] public ushort* data;
            public int nKeys;
        }

        // ---- static tables (built once, Persistent; the jobs read them) ----
        private static NativeArray<int3> _delta;
        private static NativeArray<int> _w, _need, _loff;
        private static NativeArray<ulong> _layers;   // 26 x 8: the voxels of a brick facing neighbour brick o
        private static NativeArray<byte> _groups;

        public static void EnsureTables()
        {
            if (_delta.IsCreated) return;
            _delta = new NativeArray<int3>(26, Allocator.Persistent);
            _w = new NativeArray<int>(26, Allocator.Persistent);
            _need = new NativeArray<int>(26, Allocator.Persistent);
            _loff = new NativeArray<int>(26, Allocator.Persistent);
            for (int m = 0; m < 26; m++)
            {
                int3 d = LightFloodReference.MoveDelta[m];
                _delta[m] = d;
                _loff[m] = d.x + 8 * d.y + 64 * d.z;
                _w[m] = LightFloodReference.MoveWeight[m];
                int need = 1 << Bit27(d);
                foreach (int3 f in LightFloodReference.MoveFootprint[m]) need |= 1 << Bit27(f);
                _need[m] = need;
            }
            _layers = new NativeArray<ulong>(26 * 8, Allocator.Persistent);
            for (int m = 0; m < 26; m++)
            {
                int3 o = LightFloodReference.MoveDelta[m];
                for (int l = 0; l < 512; l++)
                {
                    int3 p = new int3(l & 7, (l >> 3) & 7, l >> 6);
                    bool on = true;
                    for (int a = 0; a < 3; a++) on &= o[a] == 0 || (o[a] > 0 ? p[a] == 7 : p[a] == 0);
                    if (on) _layers[m * 8 + (l >> 6)] |= 1UL << (l & 63);
                }
            }
            _groups = new NativeArray<byte>(256, Allocator.Persistent);
            for (int i = 0; i < 256; i++) _groups[i] = TorchFloodPool.GroupsOf(i);
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += DisposeTables;
#endif
        }

        public static void DisposeTables()
        {
            if (!_delta.IsCreated) return;
            _delta.Dispose(); _w.Dispose(); _need.Dispose(); _loff.Dispose(); _layers.Dispose(); _groups.Dispose();
        }

        public static int Bit27(int3 o) => (o.x + 1) + 3 * (o.y + 1) + 9 * (o.z + 1);

        /// The closed-form 4/6/7 chamfer distance (a lower bound of every valid path).
        public static int Chamfer(int3 d)
        {
            int3 a = math.abs(d);
            int hi = math.cmax(a), lo = math.cmin(a), mid = a.x + a.y + a.z - hi - lo;
            return 4 * hi + 2 * mid + lo;
        }

        public static uint Light(uint seed, int d) =>
            LightFloodReference.Pack(math.max(0, LightFloodReference.R(seed) - d), math.max(0, LightFloodReference.G(seed) - d),
                                     math.max(0, LightFloodReference.B(seed) - d));

        public static int ShiftFor(int3 db)
        {
            int m = math.cmax(db), s = 0;
            while ((1 << s) < m) s++;
            return s;
        }

        // =====================================================================
        // Snapshot (MAIN thread): the box's terrain into the scratch
        // =====================================================================

        public static void Snapshot(ChunkStore store, int3 lo, int3 db, TorchFloodScratch s)
        {
            int sh = ShiftFor(db);
            s.Ensure(sh);
            s.dense.Clear(); s.masks.Clear();
            int3 b0 = lo >> 3, b1 = b0 + db - 1;
            int3 c0 = b0 >> 4, c1 = b1 >> 4;
            byte* kind = (byte*)s.kind.GetUnsafePtr();
            for (int cz = c0.z; cz <= c1.z; cz++)
            for (int cy = c0.y; cy <= c1.y; cy++)
            for (int cx = c0.x; cx <= c1.x; cx++)
            {
                var c = new int3(cx, cy, cz);
                Chunk ch = store.GetChunk(c);
                int3 q0 = math.max(b0, c << 4), q1 = math.min(b1, (c << 4) + 15);
                byte fill = 255;
                if (ch == null) fill = 0;
                else if (ch.isUniform) fill = ch.uniformMaterial == 0 ? (byte)1 : (byte)0;
                for (int bz = q0.z; bz <= q1.z; bz++)
                for (int by = q0.y; by <= q1.y; by++)
                {
                    int rowP = ((by - b0.y) << sh) | ((bz - b0.z) << (2 * sh));
                    if (fill != 255)
                    {
                        for (int bx = q0.x; bx <= q1.x; bx++) kind[rowP | (bx - b0.x)] = fill;
                        continue;
                    }
                    int rowL = ((bz & 15) << 8) | ((by & 15) << 4);
                    for (int bx = q0.x; bx <= q1.x; bx++)
                    {
                        uint h = ch.bricks[rowL | (bx & 15)].data;
                        int pb = rowP | (bx - b0.x);
                        if ((h & 0x80000000u) != 0) { kind[pb] = 2; s.dense.Add(new int2(pb, (int)(h & 0x3FFFFFFFu))); }
                        else kind[pb] = (h & 0xFFu) == 0 ? (byte)1 : (byte)0;
                    }
                }
            }
            new MaskJob { dense = s.dense, raw = store.BrickPool.RawData, kind = s.kind, maskOff = s.maskOff, masks = s.masks }.Run();
        }

        [BurstCompile(CompileSynchronously = true, DisableSafetyChecks = true)]
        private struct MaskJob : IJob
        {
            [ReadOnly] public NativeList<int2> dense;
            [ReadOnly] public NativeArray<byte> raw;
            public NativeArray<byte> kind;
            public NativeArray<int> maskOff;
            public NativeList<ulong> masks;

            public void Execute()
            {
                byte* r = (byte*)raw.GetUnsafeReadOnlyPtr();
                ulong* m = stackalloc ulong[8];
                for (int i = 0; i < dense.Length; i++)
                {
                    int pb = dense[i].x;
                    byte* src = r + (long)dense[i].y * 512;
                    int open = 0;
                    for (int w = 0; w < 8; w++)
                    {
                        ulong bits = 0;
                        for (int b = 0; b < 64; b++) if (src[w * 64 + b] == 0) bits |= 1UL << b;
                        m[w] = bits;
                        open += math.countbits(bits);
                    }
                    if (open == 512) { kind[pb] = 1; continue; }
                    if (open == 0) { kind[pb] = 0; continue; }
                    kind[pb] = 2;
                    maskOff[pb] = masks.Length;
                    for (int w = 0; w < 8; w++) masks.Add(m[w]);
                }
            }
        }

        // =====================================================================
        // The flood (WORKER): one torch, sparse scratch, direct bricks skipped
        // =====================================================================

        public static FloodBrickJob MakeFloodJob(TorchFloodScratch s, TorchFloodCache outCache, int3 relTorch, int3 dims, int3 db, int top,
                                                 NativeArray<int> stats)
        {
            EnsureTables();
            return new FloodBrickJob
            {
                dims = dims, db = db, shift = s.shift, t = relTorch, top = top,
                kind = s.kind, maskOff = s.maskOff, masks = s.masks, slot = s.slot, dirB = s.dirB, dist = s.dist, pad = s.pad, sat = s.sat,
                b0 = s.b0, b1 = s.b1, b2 = s.b2, b3 = s.b3, b4 = s.b4, b5 = s.b5, b6 = s.b6, b7 = s.b7, seeds = s.seeds,
                delta = _delta, w = _w, need = _need, loff = _loff, layers = _layers, groups = _groups,
                outDirect = outCache.direct, outKeys = outCache.keys, outEntries = outCache.entries, outData = outCache.data,
                stats = stats,
            };
        }

        [BurstCompile(CompileSynchronously = true, DisableSafetyChecks = true)]
        public struct FloodBrickJob : IJob
        {
            public int3 dims, db, t; public int shift, top;
            /// Report-only phase cut (0 = the whole job): 1 stops after the direct bricks, 2 after the seeds.
            public int phaseLimit;
            [ReadOnly] public NativeArray<byte> kind;
            [ReadOnly] public NativeArray<int> maskOff;
            [ReadOnly] public NativeList<ulong> masks;
            public NativeArray<int> slot;
            public NativeArray<byte> dirB;
            public NativeList<ushort> dist;
            public NativeList<ulong> pad;
            public NativeList<int> sat;
            public NativeList<int> b0, b1, b2, b3, b4, b5, b6, b7;
            public NativeList<int2> seeds;
            [ReadOnly] public NativeArray<int3> delta;
            [ReadOnly] public NativeArray<int> w, need, loff;
            [ReadOnly] public NativeArray<ulong> layers;
            [ReadOnly] public NativeArray<byte> groups;
            public NativeList<ulong> outDirect;
            public NativeList<int> outKeys;
            public NativeList<uint> outEntries;
            public NativeList<ushort> outData;
            public NativeArray<int> stats;   // reached, direct lit bricks, stored bricks, escapes, seeds

            private int Pb(int bx, int by, int bz) => bx | (by << shift) | (bz << (2 * shift));

            private bool OpenAt(int x, int y, int z)
            {
                if ((uint)x >= (uint)dims.x || (uint)y >= (uint)dims.y || (uint)z >= (uint)dims.z) return false;
                int pb = Pb(x >> 3, y >> 3, z >> 3);
                byte k = kind[pb];
                if (k == 1) return true;
                if (k == 0) return false;
                int l = (x & 7) | ((y & 7) << 3) | ((z & 7) << 6);
                return ((masks[maskOff[pb] + (l >> 6)] >> (l & 63)) & 1UL) != 0;
            }

            private NativeList<int> Bucket(int i)
            {
                switch (i & 7)
                {
                    case 0: return b0; case 1: return b1; case 2: return b2; case 3: return b3;
                    case 4: return b4; case 5: return b5; case 6: return b6; default: return b7;
                }
            }

            private int3 rlo, rdim;      // the current case's table region (bricks)
            private int sx, sxy;

            private int SatAt(int x, int y, int z) => sat[x + sx * y + sxy * z];

            /// Blocking bricks in the inclusive brick range [a, b] (inside the current region).
            private int SatCount(int3 a, int3 b)
            {
                a -= rlo; b -= rlo;
                int3 h = b + 1;
                return SatAt(h.x, h.y, h.z) - SatAt(a.x, h.y, h.z) - SatAt(h.x, a.y, h.z) - SatAt(h.x, h.y, a.z)
                     + SatAt(a.x, a.y, h.z) + SatAt(a.x, h.y, a.z) + SatAt(h.x, a.y, a.z) - SatAt(a.x, a.y, a.z);
            }

            public void Execute()
            {
                int nb = db.x * db.y * db.z;
                outDirect.Clear(); outKeys.Clear(); outEntries.Clear(); outData.Clear();
                outDirect.Resize((nb + 63) >> 6, NativeArrayOptions.ClearMemory);
                dist.Clear(); pad.Clear(); seeds.Clear();
                b0.Clear(); b1.Clear(); b2.Clear(); b3.Clear(); b4.Clear(); b5.Clear(); b6.Clear(); b7.Clear();
                int reached = 0, directLit = 0, stored = 0, escapes = 0;

                // ---- direct bricks: bbox(t, v) open for every v of B, at voxel precision ----
                // The region the boxes bbox(t, v), v in B, cover = per axis [t, B's far side]. A brick X of the
                // brick range [bt, B] lies whole in it on every axis where X differs from bt; on an axis where X
                // shares bt's coordinate only t's side of the brick counts (B above: [t, 7], below: [0, t], same
                // row: all of it). So per sign case s = sign(B - bt) a brick is "blocking" iff that sub-box of it
                // holds a closed voxel (the torch voxel itself excepted), and B is direct iff the summed-area
                // table of blocking bricks is 0 over [min(bt, B), max(bt, B)].
                int3 bt = t >> 3, tl = t & 7;
                sat.Resize((db.x + 1) * (db.y + 1) * (db.z + 1), NativeArrayOptions.ClearMemory);
                var flag = new NativeArray<byte>(nb, Allocator.Temp);
                for (int bz = 0; bz < db.z; bz++)
                for (int by = 0; by < db.y; by++)
                for (int bx = 0; bx < db.x; bx++)
                {
                    int pb = Pb(bx, by, bz);
                    slot[pb] = -1; dirB[pb] = 0;
                    flag[bx + db.x * (by + db.y * bz)] = (byte)(kind[pb] != 1 ? 1 : 0);
                }
                // the 27 local sub-boxes (per axis 0: [0, t], 1: [0, 7], 2: [t, 7]) as 512-bit masks
                ulong* sub = stackalloc ulong[27 * 8];
                for (int r = 0; r < 27 * 8; r++) sub[r] = 0;
                for (int r = 0; r < 27; r++)
                {
                    int3 rr = new int3(r % 3, (r / 3) % 3, r / 9);
                    for (int l = 0; l < 512; l++)
                    {
                        int3 q = new int3(l & 7, (l >> 3) & 7, l >> 6);
                        bool on = math.all((rr != 0 | q <= tl) & (rr != 2 | q >= tl));
                        if (on) sub[r * 8 + (l >> 6)] |= 1UL << (l & 63);
                    }
                }
                ulong* om = stackalloc ulong[8];
                var planeList = new NativeList<int>(1024, Allocator.Temp);   // dense indices of bricks sharing a coordinate with bt
                for (int bz = 0; bz < db.z; bz++)
                for (int by = 0; by < db.y; by++)
                for (int bx = 0; bx < db.x; bx++)
                    if (bx == bt.x || by == bt.y || bz == bt.z) planeList.Add(bx + db.x * (by + db.y * bz));
                int tBit = tl.x | (tl.y << 3) | (tl.z << 6);
                for (int c = 0; c < 27; c++)
                {
                    int3 s = new int3(c % 3, (c / 3) % 3, c / 9) - 1;
                    if (math.all(s == 0)) continue;
                    // this case's flags on the three planes through bt
                    for (int k = 0; k < planeList.Length; k++)
                    {
                        int di = planeList[k];
                        int3 X = new int3(di % db.x, (di / db.x) % db.y, di / (db.x * db.y));
                        int pb = Pb(X.x, X.y, X.z);
                        bool3 shared = X == bt;
                        int3 rr = math.select(new int3(1), math.select(math.select(new int3(1), new int3(2), s > 0), new int3(0), s < 0), shared);
                        int r = rr.x + 3 * rr.y + 9 * rr.z;
                        byte kd = kind[pb];
                        bool isT = math.all(X == bt);
                        byte f;
                        if (kd == 1) f = 0;
                        else if (kd == 0 && !isT) f = 1;
                        else
                        {
                            for (int i = 0; i < 8; i++) om[i] = kd == 2 ? masks[maskOff[pb] + i] : 0UL;
                            if (isT) om[tBit >> 6] |= 1UL << (tBit & 63);
                            f = 0;
                            for (int i = 0; i < 8; i++) if ((om[i] & sub[r * 8 + i]) != sub[r * 8 + i]) { f = 1; break; }
                        }
                        flag[di] = f;
                    }
                    // the bricks B of this case, and the octant region the table needs: [bt, B's side]
                    int3 lo3 = math.select(math.select(int3.zero, bt + 1, s > 0), bt, s == 0);
                    int3 hi3 = math.select(math.select(db - 1, bt - 1, s < 0), bt, s == 0);
                    if (math.any(lo3 > hi3)) goto restore;
                    rlo = math.min(lo3, bt); rdim = math.max(hi3, bt) - rlo + 1;
                    sx = rdim.x + 1; sxy = sx * (rdim.y + 1);
                    for (int i = 0; i < sx; i++) sat[i] = 0;
                    for (int z = 0; z <= rdim.z; z++)
                    for (int y = 0; y <= rdim.y; y++)
                    {
                        int i0 = sx * y + sxy * z;
                        sat[i0] = 0;
                        if (y == 0 || z == 0) { for (int x = 1; x <= rdim.x; x++) sat[i0 + x] = 0; continue; }
                        int rowF = rlo.x + db.x * ((rlo.y + y - 1) + db.y * (rlo.z + z - 1));
                        for (int x = 1; x <= rdim.x; x++)
                        {
                            int i = i0 + x;
                            sat[i] = flag[rowF + x - 1] + sat[i - 1] + sat[i - sx] + sat[i - sxy]
                                   - sat[i - 1 - sx] - sat[i - 1 - sxy] - sat[i - sx - sxy] + sat[i - 1 - sx - sxy];
                        }
                    }
                    for (int bz = lo3.z; bz <= hi3.z; bz++)
                    for (int by = lo3.y; by <= hi3.y; by++)
                    for (int bx = lo3.x; bx <= hi3.x; bx++)
                    {
                        int pb = Pb(bx, by, bz);
                        if (kind[pb] != 1) continue;
                        int3 B = new int3(bx, by, bz);
                        if (SatCount(math.min(B, bt), math.max(B, bt)) != 0) continue;
                        dirB[pb] = 1;
                        int3 bl = B << 3;
                        int3 dd = math.select(math.select(int3.zero, bl - t, bl > t), t - (bl + 7), bl + 7 < t);
                        if (Chamfer(dd) < top)
                        {
                            int di = bx + db.x * (by + db.y * bz);
                            outDirect[di >> 6] |= 1UL << (di & 63);
                            directLit++;
                        }
                    }
                restore:
                    // restore the plane flags to "not all air" for the next case
                    for (int k = 0; k < planeList.Length; k++)
                    {
                        int di = planeList[k];
                        int3 X = new int3(di % db.x, (di / db.x) % db.y, di / (db.x * db.y));
                        flag[di] = (byte)(kind[Pb(X.x, X.y, X.z)] != 1 ? 1 : 0);
                    }
                }

                if (phaseLimit == 1) { stats[1] = directLit; return; }
                // ---- seeds: direct voxels facing a searched (not direct, not closed) brick ----
                ulong* sm = stackalloc ulong[8];
                for (int bz = 0; bz < db.z; bz++)
                for (int by = 0; by < db.y; by++)
                for (int bx = 0; bx < db.x; bx++)
                {
                    int pb = Pb(bx, by, bz);
                    if (dirB[pb] == 0) continue;
                    int di = bx + db.x * (by + db.y * bz);
                    if ((outDirect[di >> 6] & (1UL << (di & 63))) == 0) continue;   // beyond the reach
                    for (int i = 0; i < 8; i++) sm[i] = 0;
                    bool any = false;
                    for (int m = 0; m < 26; m++)
                    {
                        int3 N = new int3(bx, by, bz) + delta[m];
                        if (math.any(N < 0) || math.any(N >= db)) continue;
                        int pn = Pb(N.x, N.y, N.z);
                        if (kind[pn] == 0 || dirB[pn] != 0) continue;
                        for (int i = 0; i < 8; i++) sm[i] |= layers[m * 8 + i];
                        any = true;
                    }
                    if (!any) continue;
                    int3 bl = new int3(bx, by, bz) << 3;
                    for (int i = 0; i < 8; i++)
                    {
                        ulong bits = sm[i];
                        while (bits != 0)
                        {
                            int b = math.tzcnt(bits); bits &= bits - 1;
                            int l = i * 64 + b;
                            int3 v = bl + new int3(l & 7, (l >> 3) & 7, l >> 6);
                            int c = Chamfer(v - t);
                            if (c < top) seeds.Add(new int2((pb << 9) | l, c));
                        }
                    }
                }
                // counting sort of the seeds by cost
                var head = new NativeArray<int>(top + 1, Allocator.Temp);
                for (int i = 0; i < seeds.Length; i++) head[seeds[i].y + 1]++;
                for (int i = 1; i <= top; i++) head[i] += head[i - 1];
                var sorted = new NativeArray<int>(math.max(1, seeds.Length), Allocator.Temp);
                var fill = new NativeArray<int>(top + 1, Allocator.Temp);
                for (int i = 0; i < seeds.Length; i++) { int c = seeds[i].y; sorted[head[c] + fill[c]++] = seeds[i].x; }

                if (phaseLimit == 2) { stats[4] = seeds.Length; return; }
                // ---- Dial's algorithm over the searched voxels ----
                int tPb = Pb(bt.x, bt.y, bt.z), tL = (t.x & 7) | ((t.y & 7) << 3) | ((t.z & 7) << 6);
                long pops = 0;
                Relax((tPb << 9) | tL, t, 0);
                for (int d = 1; d < top; d++)
                {
                    NativeList<int> cur = Bucket(d);
                    for (int k = head[d]; k < head[d + 1]; k++) cur.Add(sorted[k]);
                    pops += cur.Length;
                    for (int k = 0; k < cur.Length; k++)
                    {
                        int id = cur[k];
                        int pb = id >> 9, l = id & 511;
                        if (dirB[pb] == 0)
                        {
                            if (dist[(slot[pb] << 9) | l] != d) continue;
                            reached++;
                        }
                        int msk = (1 << shift) - 1;
                        int3 p = new int3(((pb & msk) << 3) | (l & 7), (((pb >> shift) & msk) << 3) | ((l >> 3) & 7), ((pb >> (2 * shift)) << 3) | (l >> 6));
                        Relax(id, p, d);
                    }
                    cur.Clear();
                }

                // ---- encode (dense brick order = sorted keys) ----
                ushort* v8 = stackalloc ushort[8];
                for (int bz = 0; bz < db.z; bz++)
                for (int by = 0; by < db.y; by++)
                for (int bx = 0; bx < db.x; bx++)
                {
                    int pb = Pb(bx, by, bz);
                    if (dirB[pb] != 0) continue;
                    int s = slot[pb];
                    if (s < 0) continue;
                    int di = bx + db.x * (by + db.y * bz);
                    int off = outData.Length, sb = s << 9;
                    if (kind[pb] == 1)
                    {
                        for (int ci = 0; ci < 8; ci++)
                        {
                            int3 c0 = new int3(ci & 1, (ci >> 1) & 1, ci >> 2) * 4 + 1;
                            int mn = 0xFFFF;
                            for (int q = 0; q < 8; q++)
                            {
                                int3 lv = c0 + new int3(q & 1, (q >> 1) & 1, q >> 2);
                                mn = math.min(mn, dist[sb | (lv.z << 6) | (lv.y << 3) | lv.x]);
                            }
                            outData.Add(mn == 0xFFFF ? TorchFloodCache.NONE : (ushort)mn);
                        }
                        outEntries.Add((KIND_AIR << 30) | (uint)off);
                    }
                    else
                    {
                        outData.Resize(off + 64, NativeArrayOptions.UninitializedMemory);
                        int esc = 0, mo = maskOff[pb];
                        for (int ci = 0; ci < 64; ci++)
                        {
                            int3 c0 = new int3(ci & 3, (ci >> 2) & 3, ci >> 4) * 2;
                            int mn = 0xFFFF, open = 0;
                            for (int q = 0; q < 8; q++)
                            {
                                int3 lv = c0 + new int3(q & 1, (q >> 1) & 1, q >> 2);
                                int l = (lv.z << 6) | (lv.y << 3) | lv.x;
                                ushort dv = dist[sb | l];
                                v8[q] = dv;
                                mn = math.min(mn, dv);
                                if (((masks[mo + (l >> 6)] >> (l & 63)) & 1UL) != 0) open |= 1 << q;
                            }
                            if (mn == 0xFFFF) { outData[off + ci] = TorchFloodCache.NONE; continue; }
                            if (groups[open] >= 2)
                            {
                                outData[off + ci] = (ushort)(TorchFloodCache.ESC | esc);
                                for (int q = 0; q < 8; q++) outData.Add(v8[q] == 0xFFFF ? TorchFloodCache.NONE : v8[q]);
                                esc++;
                            }
                            else outData[off + ci] = (ushort)mn;
                        }
                        escapes += esc;
                        outEntries.Add((KIND_MIXED << 30) | (uint)off);
                    }
                    outKeys.Add(di);
                    stored++;
                }
                stats[0] = reached; stats[1] = directLit; stats[2] = stored; stats[3] = escapes; stats[4] = seeds.Length;
                stats[5] = (int)math.min(pops, int.MaxValue); stats[6] = dist.Length >> 9;
            }

            /// The 10^3 open bits round a searched brick (the brick plus a 1-voxel apron), built once when the
            /// brick gets its first tentative cost; a voxel's 27-neighbourhood is then 9 row reads.
            private void BuildPad(int pb, int s)
            {
                int msk = (1 << shift) - 1;
                int3 o = new int3(pb & msk, (pb >> shift) & msk, pb >> (2 * shift)) << 3;
                ulong* pw = (ulong*)pad.GetUnsafePtr() + s * 16;
                for (int i = 0; i < 16; i++) pw[i] = 0;
                byte k = kind[pb];
                for (int z = -1; z <= 8; z++)
                for (int y = -1; y <= 8; y++)
                {
                    bool rowInside = y >= 0 && y <= 7 && z >= 0 && z <= 7;
                    for (int x = -1; x <= 8; x++)
                    {
                        bool open;
                        if (rowInside && x >= 0 && x <= 7)
                        {
                            if (k == 1) open = true;
                            else if (k == 0) open = false;
                            else { int l = x | (y << 3) | (z << 6); open = ((masks[maskOff[pb] + (l >> 6)] >> (l & 63)) & 1UL) != 0; }
                        }
                        else open = OpenAt(o.x + x, o.y + y, o.z + z);
                        if (open) { int r = (x + 1) + 10 * (y + 1) + 100 * (z + 1); pw[r >> 6] |= 1UL << (r & 63); }
                    }
                }
            }

            private int Slot(int npb)
            {
                int s = slot[npb];
                if (s >= 0) return s;
                s = dist.Length >> 9;
                dist.Resize(dist.Length + 512, NativeArrayOptions.UninitializedMemory);
                ushort* dp = (ushort*)dist.GetUnsafePtr() + (s << 9);
                for (int i = 0; i < 512; i++) dp[i] = 0xFFFF;
                pad.Resize((s + 1) * 16, NativeArrayOptions.UninitializedMemory);
                BuildPad(npb, s);
                slot[npb] = s;
                return s;
            }

            private void Relax(int id, int3 p, int d)
            {
                int pb = id >> 9, l = id & 511;
                int3 lc = p & 7;
                int sOwn = slot[pb];
                int nm;
                bool interior = math.all(lc >= 1) && math.all(lc <= 6);
                if (interior && kind[pb] == 1) nm = (1 << 27) - 1;
                else if (sOwn >= 0)
                {
                    ulong* pw = (ulong*)pad.GetUnsafeReadOnlyPtr() + sOwn * 16;
                    nm = 0;
                    for (int dz = 0; dz < 3; dz++)
                    for (int dy = 0; dy < 3; dy++)
                    {
                        int r = lc.x + 10 * (lc.y + dy) + 100 * (lc.z + dz);
                        int wi = r >> 6, o = r & 63;
                        ulong bits = pw[wi] >> o;
                        if (o > 61) bits |= pw[wi + 1] << (64 - o);
                        nm |= (int)(bits & 7UL) << (3 * (dy + 3 * dz));
                    }
                }
                else
                {
                    nm = 0;
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (OpenAt(p.x + dx, p.y + dy, p.z + dz)) nm |= 1 << ((dx + 1) + 3 * (dy + 1) + 9 * (dz + 1));
                }
                if (interior && sOwn >= 0)
                {
                    // every neighbour is in this brick: no brick lookups
                    ushort* dp = (ushort*)dist.GetUnsafePtr() + (sOwn << 9);
                    for (int m = 0; m < 26; m++)
                    {
                        int nd = need[m];
                        if ((nm & nd) != nd) continue;
                        int c = d + w[m];
                        if (c >= top) continue;
                        int nl = l + loff[m];
                        if (c < dp[nl]) { dp[nl] = (ushort)c; Bucket(c).Add((pb << 9) | nl); }
                    }
                    return;
                }
                // the (at most 8) neighbour bricks this voxel's moves reach, looked up once each
                int* nbS = stackalloc int[27];
                for (int i = 0; i < 27; i++) nbS[i] = -2;
                int3 pbc = p >> 3;
                for (int m = 0; m < 26; m++)
                {
                    int nd = need[m];
                    if ((nm & nd) != nd) continue;
                    int c = d + w[m];
                    if (c >= top) continue;
                    int3 n = p + delta[m];
                    int3 bo = (n >> 3) - pbc + 1;
                    int bi = bo.x + 3 * bo.y + 9 * bo.z;
                    int npb = Pb(n.x >> 3, n.y >> 3, n.z >> 3);
                    int s = nbS[bi];
                    if (s == -2) { s = dirB[npb] != 0 ? -1 : Slot(npb); nbS[bi] = s; }
                    if (s < 0) continue;
                    int nl = (n.x & 7) | ((n.y & 7) << 3) | ((n.z & 7) << 6);
                    ushort* dp = (ushort*)dist.GetUnsafePtr() + ((s << 9) | nl);
                    if (c < *dp)
                    {
                        *dp = (ushort)c;
                        Bucket(c).Add((npb << 9) | nl);
                    }
                }
            }
        }

        // =====================================================================
        // Merge (WORKER): union cells of a batch of bricks from the caches
        // =====================================================================

        [BurstCompile(CompileSynchronously = true, DisableSafetyChecks = true)]
        public struct MergeJob : IJob
        {
            [ReadOnly] public NativeArray<int3> bricks;
            [ReadOnly] public NativeArray<CacheDesc> torches;
            public NativeList<uint> outData;
            public NativeArray<int2> outRec;    // (kind, offset into outData)

            private static int Find(int* keys, int n, int key)
            {
                int a = 0, b = n - 1;
                while (a <= b)
                {
                    int m = (a + b) >> 1, k = keys[m];
                    if (k == key) return m;
                    if (k < key) a = m + 1; else b = m - 1;
                }
                return -1;
            }

            public void Execute()
            {
                outData.Clear();
                uint* cell = stackalloc uint[64];
                uint* ev = stackalloc uint[512];
                byte* ef = stackalloc byte[64];
                for (int i = 0; i < bricks.Length; i++)
                {
                    int3 wb = bricks[i];
                    // the kind: from the newest cache that lights this brick (they agree once every flood is current)
                    uint kindSel = 0; long best = long.MinValue;
                    for (int j = 0; j < torches.Length; j++)
                    {
                        CacheDesc T = torches[j];
                        int3 r = wb - (T.lo >> 3);
                        if (math.any(r < 0) || math.any(r >= T.db)) continue;
                        int di = r.x + T.db.x * (r.y + T.db.y * r.z);
                        uint k = 0;
                        if ((T.direct[di >> 6] & (1UL << (di & 63))) != 0) k = KIND_AIR;
                        else { int f = Find(T.keys, T.nKeys, di); if (f >= 0) k = T.entries[f] >> 30; }
                        if (k != 0 && T.serial > best) { best = T.serial; kindSel = k; }
                    }
                    if (kindSel == 0) { outRec[i] = new int2(0, -1); continue; }
                    int n = kindSel == KIND_AIR ? 8 : 64;
                    for (int c = 0; c < n; c++) { cell[c] = 0; ef[c] = 0; }
                    for (int j = 0; j < torches.Length; j++)
                    {
                        CacheDesc T = torches[j];
                        int3 r = wb - (T.lo >> 3);
                        if (math.any(r < 0) || math.any(r >= T.db)) continue;
                        int di = r.x + T.db.x * (r.y + T.db.y * r.z);
                        if ((T.direct[di >> 6] & (1UL << (di & 63))) != 0)
                        {
                            if (kindSel != KIND_AIR) continue;
                            int3 wv = wb << 3;
                            for (int ci = 0; ci < 8; ci++)
                            {
                                int3 a = wv + new int3(ci & 1, (ci >> 1) & 1, ci >> 2) * 4 + 1;
                                int3 dd = math.select(math.select(int3.zero, a - T.torch, a > T.torch), T.torch - (a + 1), a + 1 < T.torch);
                                int dc = Chamfer(dd);
                                if (dc < T.top) cell[ci] = LightFloodReference.Max(cell[ci], Light(T.seed, dc));
                            }
                            continue;
                        }
                        int fi = Find(T.keys, T.nKeys, di);
                        if (fi < 0) continue;
                        uint en = T.entries[fi];
                        if ((en >> 30) != kindSel) continue;
                        ushort* src = T.data + (en & OFFSET_MASK);
                        if (kindSel == KIND_AIR)
                        {
                            for (int ci = 0; ci < 8; ci++)
                                if (src[ci] != TorchFloodCache.NONE) cell[ci] = LightFloodReference.Max(cell[ci], Light(T.seed, src[ci]));
                            continue;
                        }
                        for (int ci = 0; ci < 64; ci++)
                        {
                            ushort v = src[ci];
                            if ((v & TorchFloodCache.ESC) != 0)
                            {
                                ushort* e = src + 64 + 8 * (v & 0x7FFF);
                                if (ef[ci] == 0) { ef[ci] = 1; for (int q = 0; q < 8; q++) ev[ci * 8 + q] = 0; }
                                for (int q = 0; q < 8; q++)
                                    if (e[q] != TorchFloodCache.NONE) ev[ci * 8 + q] = LightFloodReference.Max(ev[ci * 8 + q], Light(T.seed, e[q]));
                            }
                            else if (v != TorchFloodCache.NONE) cell[ci] = LightFloodReference.Max(cell[ci], Light(T.seed, v));
                        }
                    }
                    int off = outData.Length;
                    outRec[i] = new int2((int)kindSel, off);
                    if (kindSel == KIND_AIR) { for (int ci = 0; ci < 8; ci++) outData.Add(cell[ci]); continue; }
                    outData.Resize(off + 64, NativeArrayOptions.UninitializedMemory);
                    int esc = 0;
                    for (int ci = 0; ci < 64; ci++)
                    {
                        if (ef[ci] == 0) { outData[off + ci] = cell[ci]; continue; }
                        outData[off + ci] = ESCAPE_BIT | (uint)esc++;
                        // a plain value for a split cell only happens while another torch's flood is stale: fold it in
                        for (int q = 0; q < 8; q++) outData.Add(LightFloodReference.Max(ev[ci * 8 + q], cell[ci]));
                    }
                }
            }
        }

        // =====================================================================
        // Apply (MAIN thread, Burst .Run): the merged bricks into the union
        // =====================================================================

        [BurstCompile(CompileSynchronously = true, DisableSafetyChecks = true)]
        public struct ApplyJob : IJob
        {
            [ReadOnly] public NativeArray<int3> bricks;
            [ReadOnly] public NativeArray<int2> rec;
            [ReadOnly] public NativeList<uint> src;
            public NativeList<uint> tables, data;
            public NativeHashMap<int3, int> chunkTable;
            public NativeList<int> chunkUsed, freeTables, free8, free64;
            public NativeList<ulong> dirtyData;      // bit per 8-uint unit of data
            public NativeList<byte> dirtyTable;     // per table
            public NativeList<int3> chunksAdded, chunksRemoved;
            public NativeArray<int> counts;         // air bricks, mixed bricks, escapes, chunks

            private void Dirty(int off, int n)
            {
                int u0 = off >> 3, u1 = (off + n - 1) >> 3;
                if (dirtyData.Length <= (u1 >> 6)) dirtyData.Resize((u1 >> 6) + 1, NativeArrayOptions.ClearMemory);
                for (int u = u0; u <= u1; u++) dirtyData[u >> 6] |= 1UL << (u & 63);
            }

            private int Alloc(int n)
            {
                NativeList<int> fl = n == 8 ? free8 : free64;
                if (fl.Length > 0) { int o = fl[fl.Length - 1]; fl.RemoveAt(fl.Length - 1); return o; }
                int off = data.Length;
                data.Resize(off + n, NativeArrayOptions.ClearMemory);
                return off;
            }

            private void FreeBrick(uint en)
            {
                uint kind = en >> 30, off = en & OFFSET_MASK;
                if (kind == KIND_AIR) { free8.Add((int)off); counts[0]--; return; }
                for (int c = 0; c < 64; c++)
                {
                    uint v = data[(int)off + c];
                    if ((v & ESCAPE_BIT) != 0) { free8.Add((int)(v & OFFSET_MASK)); counts[2]--; }
                }
                free64.Add((int)off);
                counts[1]--;
            }

            public void Execute()
            {
                for (int i = 0; i < bricks.Length; i++)
                {
                    int3 wb = bricks[i];
                    int3 c = wb >> 4;
                    int3 lb = wb & 15;
                    int bi = (lb.z << 8) | (lb.y << 4) | lb.x;
                    uint kind = (uint)rec[i].x;
                    bool has = chunkTable.TryGetValue(c, out int ti);
                    uint old = has ? tables[ti * 4096 + bi] : 0u;
                    if (kind == 0)
                    {
                        if (old == 0) continue;
                        FreeBrick(old);
                        tables[ti * 4096 + bi] = 0;
                        dirtyTable[ti] = 1;
                        if (--chunkUsed[ti] == 0)
                        {
                            chunkTable.Remove(c); freeTables.Add(ti); chunksRemoved.Add(c); counts[3]--;
                        }
                        continue;
                    }
                    if (!has)
                    {
                        if (freeTables.Length > 0) { ti = freeTables[freeTables.Length - 1]; freeTables.RemoveAt(freeTables.Length - 1); }
                        else
                        {
                            ti = chunkUsed.Length;
                            chunkUsed.Add(0); dirtyTable.Add(0);
                            tables.Resize((ti + 1) * 4096, NativeArrayOptions.ClearMemory);
                        }
                        chunkUsed[ti] = 0;
                        chunkTable.Add(c, ti); chunksAdded.Add(c); counts[3]++;
                    }
                    if (old == 0) chunkUsed[ti]++;
                    int s = rec[i].y;
                    int off;
                    if (kind == KIND_AIR)
                    {
                        if ((old >> 30) == KIND_AIR) off = (int)(old & OFFSET_MASK);
                        else { if (old != 0) FreeBrick(old); off = Alloc(8); counts[0]++; }
                        for (int q = 0; q < 8; q++) data[off + q] = src[s + q];
                        Dirty(off, 8);
                    }
                    else
                    {
                        if ((old >> 30) == KIND_MIXED)
                        {
                            off = (int)(old & OFFSET_MASK);
                            for (int q = 0; q < 64; q++)
                            {
                                uint v = data[off + q];
                                if ((v & ESCAPE_BIT) != 0) { free8.Add((int)(v & OFFSET_MASK)); counts[2]--; }
                            }
                        }
                        else { if (old != 0) FreeBrick(old); off = Alloc(64); counts[1]++; }
                        for (int q = 0; q < 64; q++)
                        {
                            uint v = src[s + q];
                            if ((v & ESCAPE_BIT) != 0)
                            {
                                int eo = Alloc(8);
                                int from = s + 64 + 8 * (int)(v & OFFSET_MASK);
                                for (int k = 0; k < 8; k++) data[eo + k] = src[from + k];
                                Dirty(eo, 8);
                                v = ESCAPE_BIT | (uint)eo;
                                counts[2]++;
                            }
                            data[off + q] = v;
                        }
                        Dirty(off, 64);
                    }
                    uint en = (kind << 30) | (uint)off;
                    if (en != old) { tables[ti * 4096 + bi] = en; dirtyTable[ti] = 1; }
                }
            }
        }

        /// Adds every world brick a cache lights to `set` (MAIN thread, Burst .Run).
        [BurstCompile(CompileSynchronously = true, DisableSafetyChecks = true)]
        public struct CollectJob : IJob
        {
            public CacheDesc c;
            public NativeHashSet<int3> set;

            public void Execute()
            {
                int3 b0 = c.lo >> 3;
                int nb = c.db.x * c.db.y * c.db.z;
                for (int w = 0; w < ((nb + 63) >> 6); w++)
                {
                    ulong bits = c.direct[w];
                    while (bits != 0)
                    {
                        int di = w * 64 + math.tzcnt(bits); bits &= bits - 1;
                        set.Add(b0 + new int3(di % c.db.x, (di / c.db.x) % c.db.y, di / (c.db.x * c.db.y)));
                    }
                }
                for (int k = 0; k < c.nKeys; k++)
                {
                    int di = c.keys[k];
                    set.Add(b0 + new int3(di % c.db.x, (di / c.db.x) % c.db.y, di / (c.db.x * c.db.y)));
                }
            }
        }

        /// Moves up to `max` bricks from `set` into `outList` (MAIN thread, Burst .Run).
        [BurstCompile(CompileSynchronously = true, DisableSafetyChecks = true)]
        public struct TakeJob : IJob
        {
            public NativeHashSet<int3> set;
            public NativeList<int3> outList;
            public int max;

            public void Execute()
            {
                outList.Clear();
                foreach (int3 b in set) { if (outList.Length >= max) break; outList.Add(b); }
                for (int i = 0; i < outList.Length; i++) set.Remove(outList[i]);
            }
        }
    }
}
