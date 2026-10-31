// ==========================================
// Assets/CoreEngine/Rendering/TorchFloodPool.cs
//
// Task 158 (TORCH_LIGHT_PLAN.md step L2, owner "a to all" on task 155): the GPU
// LIGHT POOL for the torch flood, behind -torchflood 1, OFF by default.
//
// WHAT IT DOES. Each frame (LightingDriver.Update, main thread) Tick() checks
// whether the flood is stale: the registered torch set changed
// (LightSourceBuffer.Revision, then a sorted (voxel, material) signature), a
// chunk inside any torch's 12 m box was admitted / evicted (the Chunk object at
// each box chunk is remembered), or an EditService write landed in one
// (NotifyVoxelChanged). If so it REFILLS FROM SCRATCH: task 157's CPU oracle
// LightFloodReference floods every torch, Encode() packs the result into the
// layout TorchFlood.hlsl reads, and the three GraphicsBuffers are re-uploaded.
// Incremental edits / streaming are L3; a full refill is correct, only slow
// (task 157: ~0.5 s per outdoor torch, a hitch on the main thread).
//
// SINGLE WRITER. The CPU is the only writer of the pool; the oracle reads
// ChunkStore and writes its own storage; nothing here writes terrain. The pool
// is derived data (not ChunkStore, not saved), so D.1 and every frozen API are
// untouched.
//
// LAYOUT: see TorchFlood.hlsl. SampleLevels() below is its C# twin, used by the
// tests and the in-player GPU probe (Probe()).
//
// KNOWN GAPS (L3 / L4, not this step): fluid writes do not go through
// EditService, so a fluid flow in a lit room does not refill; no memory cap /
// furthest-first fallback (L4); coloured torch materials all reach the same
// brightness (the seed keeps the hue, not the emissive magnitude); a mobile
// emissive registered as a point light (AllowMobileEmissives) is ignored here.
// ==========================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using Debug = UnityEngine.Debug;

namespace VoxelEngine.Rendering
{
    public sealed class TorchFloodPool : IDisposable
    {
        // ---- switches (LightingDriver.Awake parses the flags) ----
        /// -torchflood 1. OFF = today's analytic torch path, byte for byte.
        public static bool Enabled;
        /// The world the flood reads (Phase4Bootstrapper sets it).
        public static ChunkStore Store;
        /// -tfi / -tfk / -tfreach / -tfwrap: the level -> light map (see TorchFlood.hlsl FloodLight).
        public static float Intensity = 2.0f, FalloffK = 0.10f, ReachM = LightFloodReference.DEFAULT_REACH_M, Wrap = 0.3f;
        /// Keep the oracle after a rebuild (the capture's GPU == oracle probe).
        public static bool KeepOracle;

        public static void ParseArgs()
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++)
            {
                string v = a[i + 1];
                switch (a[i].ToLowerInvariant())
                {
                    case "-torchflood": Enabled = v == "1"; break;
                    case "-tfi": float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out Intensity); break;
                    case "-tfk": float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out FalloffK); break;
                    case "-tfwrap": float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out Wrap); break;
                    case "-tfreach": float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ReachM); break;
                }
            }
        }

        private static TorchFloodPool _inst;
        public static TorchFloodPool Instance => _inst ??= new TorchFloodPool();
        /// The renderer draws the flood kernel only when this is true.
        public static bool Ready => Enabled && _inst != null && _inst._uploaded;

        /// Edit hook (EditService's write choke point): marks the flood stale if v
        /// lies in a chunk some torch's box covers. A no-op while OFF.
        public static void NotifyVoxelChanged(int3 v)
        {
            if (!Enabled || _inst == null || _inst._boxChunks.Count == 0) return;
            if (_inst._boxChunks.ContainsKey(v >> 7)) _inst._dirty = true;
        }

        // ---- encoded form ----
        public const uint KIND_AIR = 1u, KIND_MIXED = 2u, ESCAPE_BIT = 0x40000000u, OFFSET_MASK = 0x3FFFFFFFu;

        public sealed class Encoded
        {
            public int3 origin, dims;
            public uint[] dir = Array.Empty<uint>(), tables = Array.Empty<uint>(), data = Array.Empty<uint>();
            public int chunks, airBricks, mixedBricks, escapes, splitCells;
            public long Bytes => 4L * (dir.Length + tables.Length + data.Length);
            public bool Any => chunks > 0;
        }

        // ---- state ----
        private GraphicsBuffer _dir, _tables, _data;
        private bool _uploaded, _dirty = true;
        private long _seenRev = -1;
        private (int3 v, byte m)[] _builtSig = Array.Empty<(int3, byte)>();
        private readonly Dictionary<int3, Chunk> _boxChunks = new Dictionary<int3, Chunk>();
        public Encoded Current { get; private set; } = new Encoded();
        public LightFloodReference Oracle { get; private set; }
        public int Rebuilds { get; private set; }
        public double LastRebuildMs, LastFloodMs, LastEncodeMs, LastUploadMs;
        public int LastTorches, LastTorchesSkipped;

        public GraphicsBuffer DirBuffer => _dir;
        public GraphicsBuffer TablesBuffer => _tables;
        public GraphicsBuffer DataBuffer => _data;

        public Vector4 Params => new Vector4(Intensity, FalloffK, ReachM, (float)(LightFloodReference.UNITS_PER_VOXEL * 10.0));
        public Vector4 Params2 => new Vector4(LightFloodReference.ReachLevel(ReachM), LightFloodReference.STOPS_AT_REACH, Wrap, 0f);

        // =====================================================================
        // Per frame
        // =====================================================================

        /// Main thread, once per frame after the light upload. Refills if stale.
        public void Tick(LightSourceBuffer lights)
        {
            if (!Enabled || Store == null || lights == null) return;
            bool dirty = _dirty || !_uploaded;
            if (lights.Revision != _seenRev)
            {
                _seenRev = lights.Revision;
                if (!SameSig(Signature(lights), _builtSig)) dirty = true;
            }
            if (!dirty)
                foreach (var kv in _boxChunks)
                    if (!ReferenceEquals(Store.GetChunk(kv.Key), kv.Value)) { dirty = true; break; }
            if (dirty) Rebuild(lights);
        }

        /// Synchronous refill (also the capture's "build it now before timing").
        public void Rebuild(LightSourceBuffer lights)
        {
            var sw = Stopwatch.StartNew();
            var sig = Signature(lights);
            Oracle?.Dispose(); Oracle = null;
            var f = new LightFloodReference(Store);
            int top = LightFloodReference.ReachLevel(ReachM), skipped = 0;
            _boxChunks.Clear();
            var t0 = Stopwatch.StartNew();
            foreach (var (v, m) in sig)
            {
                bool resident = Store.GetChunk(v >> 7) != null;
                if (resident && f.Open(v)) { skipped++; continue; }    // a stale registration over air
                f.AddTorch(v, LightFloodReference.SeedForMaterial(m, ReachM));
                LightFloodReference.TorchBox(v, top, out int3 lo, out int3 dims);
                int3 c0 = (lo - 1) >> 7, c1 = (lo + dims) >> 7;
                for (int z = c0.z; z <= c1.z; z++)
                for (int y = c0.y; y <= c1.y; y++)
                for (int x = c0.x; x <= c1.x; x++)
                {
                    var c = new int3(x, y, z);
                    _boxChunks[c] = Store.GetChunk(c);
                }
            }
            LastFloodMs = t0.Elapsed.TotalMilliseconds;
            t0.Restart();
            Current = Encode(f, Store);
            LastEncodeMs = t0.Elapsed.TotalMilliseconds;
            t0.Restart();
            Upload(Current);
            LastUploadMs = t0.Elapsed.TotalMilliseconds;
            if (KeepOracle) Oracle = f; else f.Dispose();
            _builtSig = sig;
            _seenRev = lights.Revision;
            _dirty = false;
            Rebuilds++;
            LastTorches = sig.Length - skipped; LastTorchesSkipped = skipped;
            LastRebuildMs = sw.Elapsed.TotalMilliseconds;
            Debug.Log($"[torchflood] rebuild {Rebuilds}: {LastTorches} torches ({skipped} skipped), {Current.chunks} lit chunks, " +
                      $"air bricks {Current.airBricks}, mixed {Current.mixedBricks}, escapes {Current.escapes} (split cells {Current.splitCells}), " +
                      $"GPU {Current.Bytes / 1048576.0:F2} MB; flood {LastFloodMs:F0} ms, encode {LastEncodeMs:F0}, upload {LastUploadMs:F1}, " +
                      $"total {LastRebuildMs:F0} ms");
        }

        private static (int3 v, byte m)[] Signature(LightSourceBuffer lights)
        {
            var reg = lights.SnapshotRegistered();
            var l = new List<(int3, byte)>(reg.Length);
            foreach (var r in reg)
                if (!MaterialRules.IsMobile(r.material)) l.Add((r.voxel, r.material));
            l.Sort((p, q) => p.Item1.x != q.Item1.x ? p.Item1.x.CompareTo(q.Item1.x)
                           : p.Item1.y != q.Item1.y ? p.Item1.y.CompareTo(q.Item1.y)
                           : p.Item1.z != q.Item1.z ? p.Item1.z.CompareTo(q.Item1.z) : p.Item2.CompareTo(q.Item2));
            return l.ToArray();
        }

        private static bool SameSig((int3 v, byte m)[] a, (int3 v, byte m)[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (!a[i].v.Equals(b[i].v) || a[i].m != b[i].m) return false;
            return true;
        }

        // =====================================================================
        // Encode (CPU -> the GPU layout) and its sampler twin
        // =====================================================================

        /// 2x2x2 open-voxel mask -> number of face-connected groups.
        private static readonly byte[] Groups = BuildGroups();

        private static byte[] BuildGroups()
        {
            var g = new byte[256];
            for (int mask = 0; mask < 256; mask++)
            {
                int left = mask, n = 0;
                while (left != 0)
                {
                    int seed = left & -left, comp = seed, grow;
                    do
                    {
                        grow = comp;
                        for (int i = 0; i < 8; i++)
                            if ((comp & (1 << i)) != 0)
                                for (int bit = 1; bit < 8; bit <<= 1) grow |= (1 << (i ^ bit)) & mask;
                        if (grow == comp) break;
                        comp = grow;
                    } while (true);
                    left &= ~comp; n++;
                }
                g[mask] = (byte)n;
            }
            return g;
        }

        /// Pure air brick (no voxel to occlude inside it): its 0.4 m cells may be blended.
        private static bool BrickAllAir(ChunkStore s, int3 brick)
        {
            Chunk ch = s.GetChunk(brick >> 4);
            if (ch == null) return false;
            if (ch.isUniform) return ch.uniformMaterial == 0;
            uint h = ch.bricks[CoordMath.LocalBrickIndex(brick & 15)].data;
            return (h & 0x80000000u) == 0 && (h & 0xFFu) == 0;
        }

        public static Encoded Encode(LightFloodReference f, ChunkStore store)
        {
            var e = new Encoded();
            var bricks = new List<(int3 brick, uint[] data)>(f.LitBricks());
            if (bricks.Count == 0) return e;
            var chunkIdx = new Dictionary<int3, int>();
            int3 mn = new int3(int.MaxValue), mx = new int3(int.MinValue);
            foreach (var (b, _) in bricks)
            {
                int3 c = b >> 4;
                if (!chunkIdx.ContainsKey(c)) chunkIdx[c] = chunkIdx.Count;
                mn = math.min(mn, c); mx = math.max(mx, c);
            }
            e.origin = mn; e.dims = mx - mn + 1; e.chunks = chunkIdx.Count;
            e.dir = new uint[e.dims.x * e.dims.y * e.dims.z];
            foreach (var kv in chunkIdx)
            {
                int3 d = kv.Key - mn;
                e.dir[(d.z * e.dims.y + d.y) * e.dims.x + d.x] = (uint)kv.Value + 1u;
            }
            e.tables = new uint[e.chunks * 4096];
            var data = new List<uint>(bricks.Count * 64);
            var cell = new uint[8];
            foreach (var (b, lv) in bricks)
            {
                int ti = chunkIdx[b >> 4];
                int bi = CoordMath.LocalBrickIndex(b & 15);
                int off = data.Count;
                if (BrickAllAir(store, b))
                {
                    // cell value = the mean of the 2^3 voxels round its centre (in-brick 1-2 / 5-6),
                    // so the trilinear blend reproduces a linear field at the centres
                    for (int ci = 0; ci < 8; ci++)
                    {
                        int3 c0 = new int3(ci & 1, (ci >> 1) & 1, ci >> 2) * 4 + 1;
                        int r = 0, g = 0, bl = 0;
                        for (int k = 0; k < 8; k++)
                        {
                            uint p = lv[CoordMath.LocalVoxelIndex(c0 + new int3(k & 1, (k >> 1) & 1, k >> 2))];
                            r += LightFloodReference.R(p); g += LightFloodReference.G(p); bl += LightFloodReference.B(p);
                        }
                        data.Add(LightFloodReference.Pack((r + 4) >> 3, (g + 4) >> 3, (bl + 4) >> 3));
                    }
                    e.tables[ti * 4096 + bi] = (KIND_AIR << 30) | (uint)off;
                    e.airBricks++;
                    continue;
                }
                for (int ci = 0; ci < 64; ci++) data.Add(0u);
                int3 vb = b << 3;
                for (int ci = 0; ci < 64; ci++)
                {
                    int3 c0 = new int3(ci & 3, (ci >> 2) & 3, ci >> 4) * 2;
                    int open = 0; uint mxv = 0; bool any = false;
                    for (int k = 0; k < 8; k++)
                    {
                        int3 l = c0 + new int3(k & 1, (k >> 1) & 1, k >> 2);
                        uint p = lv[CoordMath.LocalVoxelIndex(l)];
                        cell[k] = p;
                        if (p != 0) { any = true; mxv = LightFloodReference.Max(mxv, p); }
                        if (f.Open(vb + l)) open |= 1 << k;
                    }
                    if (!any) continue;
                    if (Groups[open] >= 2)
                    {
                        e.splitCells++;
                        data[off + ci] = ESCAPE_BIT | (uint)data.Count;
                        for (int k = 0; k < 8; k++) data.Add(cell[k]);
                        e.escapes++;
                    }
                    else data[off + ci] = mxv;
                }
                e.tables[ti * 4096 + bi] = (KIND_MIXED << 30) | (uint)off;
                e.mixedBricks++;
            }
            e.data = data.ToArray();
            return e;
        }

        private static float3 Unpack(uint p) => new float3(p & 1023u, (p >> 10) & 1023u, (p >> 20) & 1023u);

        /// TorchFlood.hlsl FloodLevels, line for line.
        public static float3 SampleLevels(Encoded e, int3 v)
        {
            if (!e.Any) return 0f;
            int3 c = (v >> 7) - e.origin;
            if (math.any(c < 0) || math.any(c >= e.dims)) return 0f;
            uint t = e.dir[(c.z * e.dims.y + c.y) * e.dims.x + c.x];
            if (t == 0u) return 0f;
            int3 b = (v >> 3) & 15;
            uint en = e.tables[(t - 1u) * 4096u + (uint)((b.z << 8) | (b.y << 4) | b.x)];
            if (en == 0u) return 0f;
            uint off = en & OFFSET_MASK;
            int3 l = v & 7;
            if ((en >> 30) == KIND_MIXED)
            {
                int3 cl = l >> 1;
                uint p = e.data[off + (uint)((cl.z << 4) | (cl.y << 2) | cl.x)];
                if ((p & ESCAPE_BIT) != 0u)
                {
                    int3 q = l & 1;
                    p = e.data[(p & OFFSET_MASK) + (uint)((q.z << 2) | (q.y << 1) | q.x)];
                }
                return Unpack(p);
            }
            float3 fr = math.saturate(((float3)l + 0.5f - 2f) * 0.25f);
            float3 c000 = Unpack(e.data[off + 0]), c100 = Unpack(e.data[off + 1]);
            float3 c010 = Unpack(e.data[off + 2]), c110 = Unpack(e.data[off + 3]);
            float3 c001 = Unpack(e.data[off + 4]), c101 = Unpack(e.data[off + 5]);
            float3 c011 = Unpack(e.data[off + 6]), c111 = Unpack(e.data[off + 7]);
            float3 x00 = math.lerp(c000, c100, fr.x), x10 = math.lerp(c010, c110, fr.x);
            float3 x01 = math.lerp(c001, c101, fr.x), x11 = math.lerp(c011, c111, fr.x);
            return math.lerp(math.lerp(x00, x10, fr.y), math.lerp(x01, x11, fr.y), fr.z);
        }

        // =====================================================================
        // GPU
        // =====================================================================

        private static GraphicsBuffer Fit(GraphicsBuffer b, int count)
        {
            count = math.max(count, 1);
            if (b != null && b.count >= count) return b;
            b?.Release();
            return new GraphicsBuffer(GraphicsBuffer.Target.Structured, math.ceilpow2(count), 4);
        }

        private void Upload(Encoded e)
        {
            _dir = Fit(_dir, e.dir.Length);
            _tables = Fit(_tables, e.tables.Length);
            _data = Fit(_data, e.data.Length);
            if (e.dir.Length > 0) _dir.SetData(e.dir);
            if (e.tables.Length > 0) _tables.SetData(e.tables);
            if (e.data.Length > 0) _data.SetData(e.data);
            _uploaded = true;
        }

        public void Bind(UnityEngine.Rendering.ComputeCommandBuffer cmd, ComputeShader cs, int kernel)
        {
            cmd.SetComputeBufferParam(cs, kernel, "_FloodDir", _dir);
            cmd.SetComputeBufferParam(cs, kernel, "_FloodTables", _tables);
            cmd.SetComputeBufferParam(cs, kernel, "_FloodData", _data);
            var e = Current;
            cmd.SetComputeIntParams(cs, "_FloodDirOrigin", e.origin.x, e.origin.y, e.origin.z, 0);
            cmd.SetComputeIntParams(cs, "_FloodDirDims", e.dims.x, e.dims.y, e.dims.z, e.Any ? 1 : 0);
            cmd.SetComputeVectorParam(cs, "_FloodParams", Params);
            cmd.SetComputeVectorParam(cs, "_FloodParams2", Params2);
        }

        /// GPU == C# twin probe: samples FloodLevels on the GPU at `voxels`
        /// (synchronous readback; capture only). Returns (levels xyz, luma w).
        public float4[] Probe(IReadOnlyList<int3> voxels)
        {
            if (!_uploaded || voxels.Count == 0) return Array.Empty<float4>();
            var cs = Resources.Load<ComputeShader>("TorchFloodProbe");
            if (cs == null) { Debug.LogError("[torchflood] TorchFloodProbe.compute missing"); return Array.Empty<float4>(); }
            int k = cs.FindKernel("CSFloodProbe");
            var inB = new GraphicsBuffer(GraphicsBuffer.Target.Structured, voxels.Count, 16);
            var outB = new GraphicsBuffer(GraphicsBuffer.Target.Structured, voxels.Count, 16);
            var arr = new int4[voxels.Count];
            for (int i = 0; i < arr.Length; i++) arr[i] = new int4(voxels[i], 0);
            inB.SetData(arr);
            cs.SetBuffer(k, "_ProbeIn", inB);
            cs.SetBuffer(k, "_ProbeOut", outB);
            cs.SetInt("_ProbeCount", voxels.Count);
            cs.SetBuffer(k, "_FloodDir", _dir);
            cs.SetBuffer(k, "_FloodTables", _tables);
            cs.SetBuffer(k, "_FloodData", _data);
            var en = Current;
            cs.SetInts("_FloodDirOrigin", en.origin.x, en.origin.y, en.origin.z, 0);
            cs.SetInts("_FloodDirDims", en.dims.x, en.dims.y, en.dims.z, en.Any ? 1 : 0);
            cs.SetVector("_FloodParams", Params);
            cs.SetVector("_FloodParams2", Params2);
            cs.Dispatch(k, (voxels.Count + 63) / 64, 1, 1);
            var res = new float4[voxels.Count];
            outB.GetData(res);
            inB.Release(); outB.Release();
            return res;
        }

        public void Dispose()
        {
            _dir?.Release(); _tables?.Release(); _data?.Release();
            _dir = _tables = _data = null;
            Oracle?.Dispose(); Oracle = null;
            _uploaded = false;
            if (ReferenceEquals(_inst, this)) _inst = null;
        }
    }
}
