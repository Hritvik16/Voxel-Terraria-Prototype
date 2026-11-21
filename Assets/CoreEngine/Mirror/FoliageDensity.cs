// Assets/CoreEngine/Mirror/FoliageDensity.cs
//
// TASK 245 (lever F4 of PERF_SLICE_BUDGETS.md sec. 6, owner Q-AT D4): the FOLIAGE LIGHT EARLY-OUT's per-brick
// LEAF DENSITY, a SIDE BUFFER beside the brick bodies (NOT a change to the Appendix A brick layout or to
// ShellBrickPack's: no handle, body or page changes; nothing reads this unless the shader switch is on).
//
// WHAT. One byte per GPU brick body = its LEAF voxel count (materials 183-186, 216 = GenMat's tree leaves and the
// world tree's Leaves through FarField.GenToEngine; FoliageDensityTests checks the set), encoded (count + 1) >> 1,
// capped 255: fill f = byte / 256 (0 = no leaf; any leaf voxel gives >= 1). Shell bodies take 8 bytes: a full body
// its count in byte 0; a COMPACT body (task 188: up to 7 two-valued bricks of 17 uints) one byte per sub-brick.
//
// WHO WRITES. The CPU only (hard invariant 1), on the main thread (single writer), at the moment a body goes up:
// TerrainClipmap.WriteBrickRunAt (tier 0: every body write, both the GPU-pool and the old layout; the index is the
// BrickDataBuffer body = the handle's low 30 bits) and ShellStore.Continue (the shells' body runs). The count runs
// in a Burst job over the very words being uploaded, so the density can never disagree with the body it describes.
// A freed body slot keeps a stale byte that no handle points at; its next upload rewrites it.
//
// GPU. ONE StructuredBuffer<uint> `FoliageDens` (one Metal buffer slot: CSMainFarShade sits at 30 of 31 with it):
// [0, Tier0Uints) tier 0 (4 bodies per uint, body b in byte b & 3), then from ShellBaseUint the shells (2 uints per
// shell body, global body index = the store's BrickBase + local body = _ShellHi[k].w + (offset >> 7)). The CPU copy
// is kept in a managed uint[]; dirty 1,024-uint blocks go up ONCE per frame (Flush, from RaymarchFeature before the
// raymarch is recorded), in merged runs, through the same path the bodies take (UploadRing when on, else SetData).
//
// SWITCH. `-foliageout 1` at launch (Enabled): allocate + write. Off (the default): nothing is allocated, counted or
// uploaded; the shader binds a 1-element dummy and never reads it (RaymarchFeature.FoliageOut false).
//
// COST. Tier 0: GPU body cap / 4 bytes (254,400 bodies -> 254,400 B) on the GPU and the same in the CPU copy;
// shells: 8 B per shell pool body. Per uploaded body one 512-byte Burst scan.
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Mirror;

namespace VoxelEngine.Memory
{
    public static class FoliageDensity
    {
        /// `-foliageout 1` at launch (task 245): build and upload the side buffer. Tests set it directly.
        public static bool Enabled = ReadArg();
        private static bool ReadArg()
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = a.Length - 2; i >= 0; i--) if (a[i] == "-foliageout") return a[i + 1] != "0";
            return false;
        }

        public const int BODY_UINTS = 128;
        public const int SHELL_UINTS_PER_BODY = 2;   // 8 bytes: a full body's count, or one per compact sub-brick
        public const int BLOCK_UINTS = 1024;          // dirty-tracking granularity (4 KB)

        /// The leaf materials (engine ids; Raymarch.compute RayStatsLeaf and FOLIAGE's read the same set).
        public static bool IsLeaf(uint m) => (m >= 183u && m <= 186u) || m == 216u;
        /// A brick's leaf count (0..512) as its byte: (count + 1) >> 1, capped 255. Fill = byte / 256.
        public static byte Encode(int leafCount) => (byte)math.min(255, (leafCount + 1) >> 1);

        private static uint[] _cpu;
        private static bool[] _dirty;
        private static int _t0Bodies, _shellBodies;
        public static int Tier0Bodies => _t0Bodies;
        public static int ShellBodies => _shellBodies;
        public static int Tier0Uints => (_t0Bodies + 3) >> 2;
        /// First uint of the shell region (Raymarch.compute _FoliageShellBase).
        public static int ShellBaseUint => Tier0Uints;
        public static int TotalUints => Tier0Uints + _shellBodies * SHELL_UINTS_PER_BODY;
        public static GraphicsBuffer Buffer { get; private set; }
        public static long BytesGpu => Buffer == null ? 0L : (long)Buffer.count * 4;
        public static long BytesCpu => _cpu == null ? 0L : (long)_cpu.Length * 4 + (_dirty?.Length ?? 0);
        public static long BodiesWritten, ShellBodiesWritten, UploadCalls, UploadBytes;
        /// Read-only view of the CPU copy (tests).
        public static uint[] Cpu => _cpu;

        private static NativeArray<byte> _scratch;

        /// TerrainClipmap's constructor: `bodies` = BrickDataBuffer.count / 128. Resets the tier-0 region.
        public static void RegisterTier0(int bodies)
        {
            if (!Enabled) return;
            Resize(math.max(0, bodies), _shellBodies, keepShells: true);
        }

        /// ShellStreamer's constructor: every shell's pool bodies together (the shared BrickBuffer). Resets the shell region.
        public static void RegisterShells(int bodies)
        {
            if (!Enabled) return;
            Resize(_t0Bodies, math.max(0, bodies), keepShells: false);
        }

        private static void Resize(int t0, int shells, bool keepShells)
        {
            uint[] old = _cpu;
            int oldShellBase = ShellBaseUint, oldShellUints = _shellBodies * SHELL_UINTS_PER_BODY;
            _t0Bodies = t0; _shellBodies = shells;
            _cpu = new uint[math.max(1, TotalUints)];
            if (keepShells && old != null && shells * SHELL_UINTS_PER_BODY == oldShellUints && oldShellUints > 0)
                Array.Copy(old, oldShellBase, _cpu, ShellBaseUint, oldShellUints);
            _dirty = new bool[(_cpu.Length + BLOCK_UINTS - 1) / BLOCK_UINTS];
            for (int i = 0; i < _dirty.Length; i++) _dirty[i] = true;   // the new GPU buffer starts from the whole copy
            Buffer?.Release(); Buffer = null;
        }

        /// Tier 0: the bodies in BrickDataBuffer uints [dstFirstUint, + countUints), from src[srcFirstUint ..].
        public static void WriteTier0(NativeArray<uint> src, int srcFirstUint, int dstFirstUint, int countUints)
        {
            if (_cpu == null || countUints <= 0) return;
            int n = countUints / BODY_UINTS, b0 = dstFirstUint / BODY_UINTS;
            if (b0 < 0 || b0 + n > _t0Bodies) n = math.max(0, math.min(n, _t0Bodies - b0));
            if (n <= 0) return;
            Scratch(n);
            new CountJob { src = src, first = srcFirstUint, bodies = n, counts = _scratch }.Run();
            for (int i = 0; i < n; i++)
            {
                int b = b0 + i, sh = (b & 3) * 8;
                _cpu[b >> 2] = (_cpu[b >> 2] & ~(0xFFu << sh)) | ((uint)_scratch[i] << sh);
            }
            MarkDirty(b0 >> 2, (b0 + n - 1) >> 2);
            BodiesWritten += n;
        }

        /// Shells: `bodies` staged bodies from staging body `stagingFirst` (the job's order: nFull full bodies, then
        /// the compact ones, ShellBrickPack.PER_BODY sub-bricks each, nCompact in all) to global shell body `globalFirst`.
        public static void WriteShell(NativeArray<uint> staging, int stagingFirst, int globalFirst, int bodies, int nFull, int nCompact)
        {
            if (_cpu == null || bodies <= 0) return;
            if (globalFirst < 0 || globalFirst + bodies > _shellBodies) bodies = math.max(0, math.min(bodies, _shellBodies - globalFirst));
            if (bodies <= 0) return;
            Scratch(bodies * 8);
            new ShellCountJob { staging = staging, first = stagingFirst, bodies = bodies, nFull = nFull, nCompact = nCompact, bytes = _scratch }.Run();
            int u0 = ShellBaseUint + globalFirst * SHELL_UINTS_PER_BODY;
            for (int i = 0; i < bodies; i++)
            {
                int o = i * 8;
                _cpu[u0 + i * 2] = _scratch[o] | ((uint)_scratch[o + 1] << 8) | ((uint)_scratch[o + 2] << 16) | ((uint)_scratch[o + 3] << 24);
                _cpu[u0 + i * 2 + 1] = _scratch[o + 4] | ((uint)_scratch[o + 5] << 8) | ((uint)_scratch[o + 6] << 16) | ((uint)_scratch[o + 7] << 24);
            }
            MarkDirty(u0, u0 + bodies * SHELL_UINTS_PER_BODY - 1);
            ShellBodiesWritten += bodies;
        }

        /// The byte of tier-0 body b / of shell body g, sub-brick s (tests; the shader's FoliageByte).
        public static byte Tier0Byte(int b) => (byte)(_cpu[b >> 2] >> ((b & 3) * 8));
        public static byte ShellByte(int g, int s) { int i = (ShellBaseUint + g * SHELL_UINTS_PER_BODY) * 4 + s; return (byte)(_cpu[i >> 2] >> ((i & 3) * 8)); }

        private static void MarkDirty(int u0, int u1)
        {
            for (int k = u0 / BLOCK_UINTS; k <= u1 / BLOCK_UINTS; k++) _dirty[k] = true;
        }

        /// Once per frame, before the raymarch is recorded: create the buffer if needed and upload the dirty blocks.
        public static void Flush()
        {
            if (_cpu == null) return;
            if (Buffer == null)
                Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, _cpu.Length, 4);
            int nb = _dirty.Length;
            for (int k = 0; k < nb; k++)
            {
                if (!_dirty[k]) continue;
                int e = k;
                // merge across gaps of up to 2 clean blocks (one call instead of several small ones)
                while (true)
                {
                    int nx = e + 1;
                    while (nx < nb && nx <= e + 3 && !_dirty[nx]) nx++;
                    if (nx < nb && nx <= e + 3 && _dirty[nx]) e = nx; else break;
                }
                int s = k * BLOCK_UINTS, n = math.min(_cpu.Length, (e + 1) * BLOCK_UINTS) - s;
                if (UploadRing.On) UploadRing.Shared.Write(Buffer, _cpu, s, s, n);
                else Buffer.SetData(_cpu, s, s, n);
                for (int j = k; j <= e; j++) _dirty[j] = false;
                UploadCalls++; UploadBytes += (long)n * 4;
                k = e;
            }
        }

        public static void Release()
        {
            Buffer?.Release(); Buffer = null;
            _cpu = null; _dirty = null; _t0Bodies = 0; _shellBodies = 0;
            if (_scratch.IsCreated) _scratch.Dispose();
            BodiesWritten = ShellBodiesWritten = UploadCalls = UploadBytes = 0;
        }

        public static string Report() =>
            $"[foliage] side buffer {(Enabled ? "ON" : "OFF")}: tier-0 bodies {_t0Bodies:N0} ({Tier0Uints * 4L:N0} B), shell bodies {_shellBodies:N0} " +
            $"({(long)_shellBodies * SHELL_UINTS_PER_BODY * 4:N0} B); GPU {BytesGpu:N0} B, CPU {BytesCpu:N0} B; written {BodiesWritten:N0} tier-0 / " +
            $"{ShellBodiesWritten:N0} shell bodies; uploads {UploadCalls:N0} calls, {UploadBytes:N0} B";

        private static void Scratch(int n)
        {
            if (_scratch.IsCreated && _scratch.Length >= n) return;
            if (_scratch.IsCreated) _scratch.Dispose();
            _scratch = new NativeArray<byte>(math.max(n, 4096 * 8), Allocator.Persistent);
        }

        [BurstCompile(CompileSynchronously = true)]
        public struct CountJob : IJob
        {
            [ReadOnly] public NativeArray<uint> src;
            public int first, bodies;
            [WriteOnly] public NativeArray<byte> counts;
            public void Execute()
            {
                for (int b = 0; b < bodies; b++)
                {
                    int o = first + b * BODY_UINTS, c = 0;
                    for (int u = 0; u < BODY_UINTS; u++)
                    {
                        uint w = src[o + u];
                        c += Leaf(w & 0xFFu) + Leaf((w >> 8) & 0xFFu) + Leaf((w >> 16) & 0xFFu) + Leaf(w >> 24);
                    }
                    counts[b] = Encode(c);
                }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        public struct ShellCountJob : IJob
        {
            [ReadOnly] public NativeArray<uint> staging;
            public int first, bodies, nFull, nCompact;
            [WriteOnly] public NativeArray<byte> bytes;
            public void Execute()
            {
                for (int i = 0; i < bodies; i++)
                {
                    int j = first + i, o = j * BODY_UINTS;
                    for (int s = 0; s < 8; s++) bytes[i * 8 + s] = 0;
                    if (j < nFull)
                    {
                        int c = 0;
                        for (int u = 0; u < BODY_UINTS; u++)
                        {
                            uint w = staging[o + u];
                            c += Leaf(w & 0xFFu) + Leaf((w >> 8) & 0xFFu) + Leaf((w >> 16) & 0xFFu) + Leaf(w >> 24);
                        }
                        bytes[i * 8] = Encode(c);
                        continue;
                    }
                    int cb = j - nFull;
                    int subs = math.min(ShellBrickPack.PER_BODY, nCompact - cb * ShellBrickPack.PER_BODY);
                    for (int s = 0; s < subs; s++)
                    {
                        int so = o + s * ShellBrickPack.UINTS;
                        uint p = staging[so];
                        int pop = 0;
                        for (int w = 1; w <= 16; w++) pop += math.countbits(staging[so + w]);
                        int c = Leaf((p >> 8) & 0xFFu) * pop + Leaf(p & 0xFFu) * (512 - pop);
                        bytes[i * 8 + s] = Encode(c);
                    }
                }
            }
        }

        private static int Leaf(uint m) => ((m >= 183u && m <= 186u) || m == 216u) ? 1 : 0;
    }
}
