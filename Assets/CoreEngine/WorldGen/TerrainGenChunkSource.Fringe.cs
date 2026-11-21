// Assets/CoreEngine/WorldGen/TerrainGenChunkSource.Fringe.cs
//
// Task 250 (WORLD_FILL_BUDGET.md sec. 5, experiment E3; `-fringe 1`, DEFAULT OFF): VOXEL GRASS FRINGE.
// 1-3 voxel tall stubs (0.1 m voxels) on grass surface voxels, as a COUNTS experiment: what does short
// voxel clutter cost the tier-0 march per 1 % of screen it covers (RayStats class "fringe")?
//
// EFFICIENT BY CONSTRUCTION (PREAMBLE rule 11): a stub is written ONLY into a brick that is already
// dense (not uniform) after the voxel pass and the tree / building overlays. A stub voxel whose brick is
// uniform (the air brick above a surface that ends on a brick's top layer) is refused, never expanded.
// So the CPU pool gets no new brick, the GPU mirror no new body (a dense brick stays dense; sealing
// can only gain: fringe turns air into a seal-solid id), and the chunk's brick table is unchanged.
// Counter slot [7] guards it (dense bricks after minus before the pass; must stay 0, logged as an error).
// Nothing is stored that is not drawn: no instance list, no per-frame work, no new pass.
//
// THE RULE (local data only, no terrain-specific tuning; memory "no terrain-specific engineering"):
//   host    a Grass / JungleGrass voxel (-> GrassFringe) or SavannaGrass voxel (-> SavannaFringe)
//           whose voxel above is AIR (the surface)
//   slope   none of the 8 horizontal neighbours of the stub's SECOND voxel (host y + 2) is solid:
//           ground rising more than 2 voxels per voxel (~63 deg) around it gets no stub
//   pick    a hash of the host's world position and the seed: 3 columns in 8 get a stub,
//           1 tall (8/16), 2 tall (6/16), 3 tall (2/16)
// Neighbours read the FINAL overlay state; fringe ids read as air, so the result does not depend on
// the order columns are visited. Reads outside the chunk count as air (a host on the chunk's top
// layer gets no stub). LOD-0 chunks only (Generate): the shells, the far band and the point query
// (FarQuery) do not have it -- 0.1-0.3 m stubs are below their resolution. Collision: the two ids
// never block a body (VoxelCollision.IsBlocking).
using System;
using Unity.Collections;
using Unity.Mathematics;

namespace VoxelEngine.WorldGen
{
    public sealed partial class TerrainGenChunkSource
    {
        /// -fringe 1 (task 250): spec.fringeOn = 1. DEFAULT OFF; last match wins.
        public static bool FringeArg()
        {
            string[] a = Environment.GetCommandLineArgs();
            bool v = false;
            for (int i = 0; i + 1 < a.Length; i++)
                if (a[i] == "-fringe" && int.TryParse(a[i + 1], out int p)) v = p != 0;
            return v;
        }

        /// Tests and reports (task 250): fringe on / off for the chunks generated from now on.
        /// Not thread-safe: call between Generate calls.
        public void SetFringe(bool on)
        {
            var t = Spec; t.fringeOn = on ? (byte)1 : (byte)0; Spec = t;
            _columnTop.Clear(); _specStamp++;
        }

        /// Fringe counters since construction, summed over every LOD-0 chunk (Generate):
        /// [0] chunks run, [1] stub columns, [2] fringe voxels written, [3] stub voxels refused because their
        /// brick was uniform (the no-new-brick rule acting), [4] dense bricks added by the pass (MUST be 0).
        public readonly long[] FringeStats = new long[5];

        /// Per-chunk counter slots in ColumnPass.voxStats (after SkipStats' four).
        public const int FS_STUBS = 4, FS_VOXELS = 5, FS_REFUSED = 6, FS_NEWBRICK = 7, VOXSTATS_LEN = 8;

        /// 3 columns in 8 (hash & 7 < FRINGE_PICK_OF_8).
        public const uint FRINGE_PICK_OF_8 = 3;
        private const uint FRINGE_SALT = 0x6b1f2a3du;

        private void AddFringeStats(NativeArray<int> st)
        {
            long n = System.Threading.Interlocked.Increment(ref FringeStats[0]);
            System.Threading.Interlocked.Add(ref FringeStats[1], st[FS_STUBS]);
            System.Threading.Interlocked.Add(ref FringeStats[2], st[FS_VOXELS]);
            System.Threading.Interlocked.Add(ref FringeStats[3], st[FS_REFUSED]);
            System.Threading.Interlocked.Add(ref FringeStats[4], st[FS_NEWBRICK]);
            if (st[FS_NEWBRICK] != 0)
                UnityEngine.Debug.LogError($"[fringe] {st[FS_NEWBRICK]} NEW dense bricks in one chunk (must be 0)");
            if (n == 1 || (n & 255) == 0) UnityEngine.Debug.Log("[fringe] " + FringeSummary());
        }

        /// One line: the fringe counters so far (the launch's own evidence that fringe made no new brick).
        public string FringeSummary()
            => $"-fringe 1: {FringeStats[0]:N0} chunks, {FringeStats[1]:N0} stubs, {FringeStats[2]:N0} voxels, " +
               $"{FringeStats[3]:N0} stub voxels refused (uniform brick), new bricks {FringeStats[4]} (must be 0)";

        /// The fringe pass over one LOD-0 chunk's voxel scratch (chunk layout Index(x, y, z), 128^3; bricks
        /// bi = bz << 8 | by << 4 | bx; a uniform brick's voxels live in brickMat only). Burst-callable.
        /// Writes only AIR voxels of NON-uniform bricks; never changes brickUniform / brickMat.
        /// stats[FS_*] are added to (the caller clears them).
        public static void OverlayFringe(uint seed, int3 origin, NativeArray<byte> toEngine,
                                         NativeArray<byte> mats, NativeArray<byte> brickMat, NativeArray<bool> brickUniform,
                                         NativeArray<int> stats)
        {
            const int E = 128;
            byte air = toEngine[TerrainGen.GenMat.Air];
            byte hG = toEngine[TerrainGen.GenMat.Grass], hJ = toEngine[TerrainGen.GenMat.JungleGrass];
            byte hS = toEngine[TerrainGen.GenMat.SavannaGrass];
            byte fG = toEngine[TerrainGen.GenMat.GrassFringe], fS = toEngine[TerrainGen.GenMat.SavannaFringe];
            int denseBefore = 0;
            for (int bi = 0; bi < 4096; bi++) if (!brickUniform[bi]) denseBefore++;
            for (int bi = 0; bi < 4096; bi++)
            {
                if (brickUniform[bi]) continue;   // a uniform brick holds no surface voxel with air above it inside it
                int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
                for (int vy = 0; vy < 8; vy++)
                for (int vz = 0; vz < 8; vz++)
                for (int vx = 0; vx < 8; vx++)
                {
                    int lx = (bx << 3) + vx, ly = (by << 3) + vy, lz = (bz << 3) + vz;
                    byte m = mats[(ly << 14) | (lz << 7) | lx];
                    byte f;
                    if (m == hG || m == hJ) f = fG;
                    else if (m == hS) f = fS;
                    else continue;
                    if (ly + 1 >= E) continue;
                    if (ReadF(lx, ly + 1, lz, mats, brickMat, brickUniform, air, fG, fS) != air) continue;   // not the surface
                    uint h = math.hash(new uint4((uint)(origin.x + lx), (uint)(origin.y + ly), (uint)(origin.z + lz), seed ^ FRINGE_SALT));
                    if ((h & 7u) >= FRINGE_PICK_OF_8) continue;
                    if (Steep(lx, ly + 2, lz, mats, brickMat, brickUniform, air, fG, fS)) continue;
                    uint u = (h >> 3) & 15u;
                    int tall = u < 8u ? 1 : (u < 14u ? 2 : 3);
                    stats[FS_STUBS]++;
                    for (int k = 1; k <= tall; k++)
                    {
                        int y = ly + k;
                        if (y >= E) break;
                        int tb = (lz >> 3 << 8) | (y >> 3 << 4) | (lx >> 3);
                        if (brickUniform[tb]) { stats[FS_REFUSED]++; break; }   // no new brick: never expand a uniform one
                        int idx = (y << 14) | (lz << 7) | lx;
                        if (mats[idx] != air) break;
                        mats[idx] = f;
                        stats[FS_VOXELS]++;
                    }
                }
            }
            // Guard: the dense-brick count the copy loop in Generate allocates bodies from, before vs after.
            int denseAfter = 0;
            for (int bi = 0; bi < 4096; bi++) if (!brickUniform[bi]) denseAfter++;
            stats[FS_NEWBRICK] += denseAfter - denseBefore;
        }

        private static byte ReadF(int x, int y, int z, NativeArray<byte> mats, NativeArray<byte> brickMat,
                                  NativeArray<bool> brickUniform, byte air, byte fG, byte fS)
        {
            if ((uint)x >= 128u || (uint)y >= 128u || (uint)z >= 128u) return air;
            int bi = (z >> 3 << 8) | (y >> 3 << 4) | (x >> 3);
            byte m = brickUniform[bi] ? brickMat[bi] : mats[(y << 14) | (z << 7) | x];
            return (m == fG || m == fS) ? air : m;
        }

        /// True when any of the 8 horizontal neighbours of (x, y, z) is not air (fringe reads as air).
        private static bool Steep(int x, int y, int z, NativeArray<byte> mats, NativeArray<byte> brickMat,
                                  NativeArray<bool> brickUniform, byte air, byte fG, byte fS)
        {
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                if (ReadF(x + dx, y, z + dz, mats, brickMat, brickUniform, air, fG, fS) != air) return true;
            }
            return false;
        }
    }
}
