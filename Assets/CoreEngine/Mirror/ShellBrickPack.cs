// Assets/CoreEngine/Mirror/ShellBrickPack.cs
//
// Task 188 (owner 2026-11-07 Q-T (a): fix the shell pool overflow trees cause): the
// COMPACT shell brick body (an Appendix A addition beside ShellStore's layout).
//
// WHY: task 180's trees roughly double the dense bricks of shells 1-2 (canopies are a
// second surface over the ground) and a solid forest cover runs the brick pools 30-57 %
// over cap (Logs/TerrainGen/trees188/pools.txt). 92 % of the dense bricks trees add hold
// exactly TWO materials (leaf or wood + air), and a 512 B body spends 8 bits per voxel
// to tell two values apart. So a dense brick whose voxels take at most two values is
// stored as a 1-bit plane instead -- EXACT (every voxel decodes to the byte it had),
// decided per brick from its own voxels, for every shell brick (terrain too), no cap,
// no tree or terrain constant.
//
// LAYOUT (shell bricks only; the 0.1 m pool, tier 0, is untouched):
//   page handle   DENSE (bit 31) | COMPACT (bit 30) | uint offset (low 30 bits, shell-
//                 LOCAL, in ShellBricks uints: body * 128 + sub * 17). A full body's
//                 handle keeps bit 30 clear (body index < 2^30), so it is unchanged.
//   compact body  17 uints: [0] = a | (b << 8) (a = the brick's voxel 0, b = the other
//                 value; a == b allowed); [1 + (v >> 5)] bit (v & 31) set = voxel v is b
//                 (v = (vz << 6) | (vy << 3) | vx, BrickDataPool's order).
//   packing       PER_BODY = 7 compact bricks in one 128-uint body slot (7 x 17 = 119;
//                 the last 9 uints zero). A chunk's compact bricks are packed into its
//                 own bodies (after its full ones), so a page still owns a plain list of
//                 bodies: allocation, upload, deferral and freeing are unchanged and the
//                 pool's unit is still the 512 B body (PoolCap, BricksUsed count bodies).
//   reads         Raymarch.compute ShellBrickVoxel; ShellRayReference.IView.VoxelOf;
//                 ShadowVoxel (EditMode shadows). All three decode the same bits.
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace VoxelEngine.Mirror
{
    public static class ShellBrickPack
    {
        public const uint COMPACT = 0x40000000u;
        public const int UINTS = 17;
        public const int PER_BODY = 7;
        public const int BODY_UINTS = 128;
        /// pair[] value of a brick with three or more values (or a uniform brick): a full body.
        public const uint NOT_PAIR = 0xFFFFFFFFu;

        /// `-shellpack 0` (diagnostic, the task-188 before/after shots): every dense brick
        /// takes a full body again, as before task 188. Read once; tests may set it.
        public static bool Disabled = ReadArg();
        private static bool ReadArg()
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = a.Length - 2; i >= 0; i--) if (a[i] == "-shellpack") return a[i + 1] == "0";
            return false;
        }

        /// Bodies a chunk needs for `full` full and `compact` compact bricks.
        public static int Bodies(int full, int compact) => full + (compact + PER_BODY - 1) / PER_BODY;

        /// pair[bi] for every brick (NOT_PAIR for uniform ones and those with >= 3 values;
        /// else a | (b << 8)); counts[0] = full, counts[1] = compact.
        [BurstCompile(CompileSynchronously = true)]
        public struct ClassifyJob : IJob
        {
            [ReadOnly] public NativeArray<byte> mats;
            [ReadOnly] public NativeArray<bool> brickUniform;
            public NativeArray<uint> pair;
            public NativeArray<int> counts;
            public bool disabled;   // -shellpack 0: every dense brick full

            public void Execute()
            {
                int full = 0, compact = 0;
                for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++)
                {
                    pair[bi] = NOT_PAIR;
                    if (brickUniform[bi]) continue;
                    if (disabled) { full++; continue; }
                    int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
                    int a = mats[((by << 3) << 14) | ((bz << 3) << 7) | (bx << 3)], b = -1;
                    bool two = true;
                    for (int vz = 0; vz < 8 && two; vz++)
                    for (int vy = 0; vy < 8 && two; vy++)
                    {
                        int row = (((by << 3) + vy) << 14) | (((bz << 3) + vz) << 7) | (bx << 3);
                        for (int vx = 0; vx < 8; vx++)
                        {
                            int m = mats[row + vx];
                            if (m == a || m == b) continue;
                            if (b < 0) { b = m; continue; }
                            two = false; break;
                        }
                    }
                    if (!two) { full++; continue; }
                    if (b < 0) b = a;
                    pair[bi] = (uint)a | ((uint)b << 8);
                    compact++;
                }
                counts[0] = full; counts[1] = compact;
            }
        }

        /// The bodies of one chunk into `staging` (body i at i * 128): bricks[0..nFull) as
        /// full bodies (BrickDataPool's layout), then bricks[nFull..nFull + nCompact) packed
        /// PER_BODY to a body (compact brick j: body nFull + j / 7, uint (j % 7) * 17).
        [BurstCompile(CompileSynchronously = true)]
        public struct StageJob : IJob
        {
            [ReadOnly] public NativeArray<byte> mats;
            [ReadOnly] public NativeArray<int> bricks;
            [ReadOnly] public NativeArray<uint> pair;
            public int nFull, nCompact;
            public NativeArray<uint> staging;

            public void Execute()
            {
                for (int i = 0; i < nFull; i++)
                {
                    int bi = bricks[i];
                    int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
                    int o = i * BODY_UINTS;
                    for (int vz = 0; vz < 8; vz++)
                    for (int vy = 0; vy < 8; vy++)
                    {
                        int row = (((by << 3) + vy) << 14) | (((bz << 3) + vz) << 7) | (bx << 3);
                        int u = o + (((vz << 6) | (vy << 3)) >> 2);
                        staging[u] = mats[row] | ((uint)mats[row + 1] << 8) | ((uint)mats[row + 2] << 16) | ((uint)mats[row + 3] << 24);
                        staging[u + 1] = mats[row + 4] | ((uint)mats[row + 5] << 8) | ((uint)mats[row + 6] << 16) | ((uint)mats[row + 7] << 24);
                    }
                }
                int packed = (nCompact + PER_BODY - 1) / PER_BODY;
                for (int u = nFull * BODY_UINTS; u < (nFull + packed) * BODY_UINTS; u++) staging[u] = 0u;
                for (int j = 0; j < nCompact; j++)
                {
                    int bi = bricks[nFull + j];
                    uint p = pair[bi];
                    byte bm = (byte)(p >> 8);
                    int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
                    int o = (nFull + j / PER_BODY) * BODY_UINTS + (j % PER_BODY) * UINTS;
                    staging[o] = p;
                    for (int vz = 0; vz < 8; vz++)
                    for (int vy = 0; vy < 8; vy++)
                    {
                        int row = (((by << 3) + vy) << 14) | (((bz << 3) + vz) << 7) | (bx << 3);
                        int v0 = (vz << 6) | (vy << 3);   // 8 bits at v0 .. v0 + 7, inside one uint
                        uint bits = 0u;
                        if ((byte)p != bm)
                            for (int vx = 0; vx < 8; vx++) if (mats[row + vx] == bm) bits |= 1u << vx;
                        staging[o + 1 + (v0 >> 5)] |= bits << (v0 & 31);
                    }
                }
            }
        }

        /// Full and compact brick counts of a chunk's non-uniform bricks (Burst; main thread).
        public static void Count(NativeArray<byte> mats, NativeArray<bool> brickUniform, out int full, out int compact)
        {
            var pair = new NativeArray<uint>(EngineConfig.BRICKS_PER_CHUNK, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var counts = new NativeArray<int>(2, Allocator.TempJob);
            new ClassifyJob { mats = mats, brickUniform = brickUniform, pair = pair, counts = counts, disabled = Disabled }.Run();
            full = counts[0]; compact = counts[1];
            pair.Dispose(); counts.Dispose();
        }

        /// The voxel a dense shell handle `h` holds at vIdx, from a CPU copy of ShellBricks
        /// (shell-local, the ShadowUploads layout) -- what the shader's ShellBrickVoxel reads.
        public static byte ShadowVoxel(uint[] bricks, uint h, int vIdx)
        {
            int off = (int)(h & 0x3FFFFFFFu);
            if ((h & COMPACT) == 0) return (byte)(bricks[off * BODY_UINTS + (vIdx >> 2)] >> ((vIdx & 3) * 8));
            uint bit = (bricks[off + 1 + (vIdx >> 5)] >> (vIdx & 31)) & 1u;
            return (byte)(bricks[off] >> (int)(bit * 8u));
        }
    }
}
