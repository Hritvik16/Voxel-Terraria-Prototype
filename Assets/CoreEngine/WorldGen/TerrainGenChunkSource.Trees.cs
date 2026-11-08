// Assets/CoreEngine/WorldGen/TerrainGenChunkSource.Trees.cs
//
// Task 180 (placement, first test, `-trees 1`; default OFF): GENERATED VOXEL
// TREES in the chunk pass. TerrainGen.Trees decides every tree from its 6.4 m
// tree cell (header there); this file plumbs it through the engine's generators
// so LOD 0, the shells and the point query give the same voxel:
//   ColumnJob / ShellColumnJob  gather the trees of the cells round the chunk's
//                               footprint into the column scratch (TreeScratch)
//                               and raise the column top by their crowns, so the
//                               air fast paths stay exact;
//   VoxelJob / ShellVoxelJob    OverlayTrees after the voxel pass: every AIR voxel
//                               a trunk, then a leaf primitive contains takes its
//                               material (coarse voxels test their sample point);
//                               touched bricks are re-checked for uniformity;
//   PointVoxel                  the same rule for one voxel (FarQuery).
// Shells above TerrainGen.Trees.MAX_SHELL gather nothing. The far band draws no
// trees (visible only past the last shell, where a texel is wider than a tree).
//
// THREADING: the plane is read-only and shared like the map arrays; each scratch
// belongs to the ColumnPass / ShellChunk that owns it (single-writer, CLAUDE.md).
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using VoxelEngine.Memory;

namespace VoxelEngine.WorldGen
{
    public sealed partial class TerrainGenChunkSource
    {
        private NativeArray<byte> _treePlane;   // task 180: TreeTable.DensityPlane (zeros without a record)

        internal NativeArray<byte> PointTreePlane => _treePlane;

        /// -trees 1 (task 180): spec.treesOn = 1, generated voxel trees. Absent or 0 =
        /// none (the default). Last match wins.
        public static bool TreesArg()
        {
            string[] a = Environment.GetCommandLineArgs();
            bool on = false;
            for (int i = 0; i + 1 < a.Length; i++)
                if (a[i] == "-trees" && int.TryParse(a[i + 1], out int p)) on = p != 0;
            return on;
        }

        /// Tests and reports (task 180's on / off): spec.treesOn for the chunks
        /// generated from now on. Not thread-safe: call between Generate calls.
        public void SetTrees(bool on)
        {
            var t = Spec; t.treesOn = on ? (byte)1 : (byte)0; Spec = t;
            _columnTop.Clear(); _specStamp++;
        }

        /// Tests and reports (task 180b's on / off): spec.MatRules (matRulesOff = !on) for the chunks
        /// generated from now on (bakes the shore field the first time). Not
        /// thread-safe: call between Generate calls.
        public void SetMatRules(bool on)
        {
            var t = Spec; t.matRulesOff = on ? (byte)0 : (byte)1; Spec = t;
            if (on)
            {
                Map.EnsureShoreField();
                if (_shore.IsCreated) _shore.Dispose();
                _shore = new NativeArray<ushort>(Map.ShoreField, Allocator.Persistent);
            }
            _columnTop.Clear(); _specStamp++;
        }

        /// The gathered trees of one footprint (a chunk column or a shell chunk).
        internal sealed class TreeScratch : IDisposable
        {
            internal NativeArray<TerrainGen.Tree> trees;
            internal NativeArray<TerrainGen.TreePrim> prims;
            internal NativeArray<int> counts;   // [0] trees, [1] primitives
            private readonly Allocator _a;

            internal TreeScratch(int maxTrees, Allocator a)
            {
                _a = a;
                Alloc(math.max(1, maxTrees));
                counts = new NativeArray<int>(2, a);
            }

            private void Alloc(int maxTrees)
            {
                trees = new NativeArray<TerrainGen.Tree>(maxTrees, _a, NativeArrayOptions.UninitializedMemory);
                prims = new NativeArray<TerrainGen.TreePrim>(maxTrees * TerrainGen.Trees.MAX_PRIMS, _a, NativeArrayOptions.UninitializedMemory);
            }

            /// Grow to hold `maxTrees` (contents lost).
            internal void Ensure(int maxTrees)
            {
                if (trees.Length >= maxTrees) return;
                trees.Dispose(); prims.Dispose();
                Alloc(maxTrees);
            }

            internal void CopyFrom(TreeScratch o)
            {
                Ensure(o.trees.Length);
                int nt = o.counts[0], np = o.counts[1];
                NativeArray<TerrainGen.Tree>.Copy(o.trees, trees, nt);
                NativeArray<TerrainGen.TreePrim>.Copy(o.prims, prims, np);
                counts[0] = nt; counts[1] = np;
            }

            internal long Bytes => (long)trees.Length * UnsafeUtility.SizeOf<TerrainGen.Tree>()
                                 + (long)prims.Length * UnsafeUtility.SizeOf<TerrainGen.TreePrim>();

            public void Dispose()
            {
                if (trees.IsCreated) trees.Dispose();
                if (prims.IsCreated) prims.Dispose();
                if (counts.IsCreated) counts.Dispose();
            }
        }

        /// Trees a LOD-0 chunk column can gather (16: 4 x 4 tree cells).
        internal static readonly int ChunkMaxTrees = TerrainGen.Trees.MaxTrees(0, 0, EngineConfig.CHUNK_EDGE_VOXELS - 1, EngineConfig.CHUNK_EDGE_VOXELS - 1);

        /// Trees a shell chunk's sampled footprint can gather (0 above Trees.MAX_SHELL or with trees off).
        internal int ShellMaxTrees(int shell, int3 origin, int half)
        {
            if (!Spec.Trees || shell > TerrainGen.Trees.MAX_SHELL) return 0;
            int w = (EngineConfig.CHUNK_EDGE_VOXELS - 1) << shell;
            return TerrainGen.Trees.MaxTrees(origin.x + half, origin.z + half, origin.x + half + w, origin.z + half + w);
        }

        /// Gather into a scratch from a Burst job (ColumnJob / ShellColumnJob).
        internal static unsafe void GatherTrees(in TerrainGen.TerrainSpec spec, in TerrainGen.MapView map, in TerrainGen.Offsets o,
                                                int x0, int z0, int x1, int z1,
                                                NativeArray<TerrainGen.Tree> trees, NativeArray<TerrainGen.TreePrim> prims,
                                                NativeArray<int> counts, ref int top)
        {
            int np;
            int nt = TerrainGen.Trees.Gather(in spec, in map, in o, x0, z0, x1, z1,
                (TerrainGen.Tree*)trees.GetUnsafePtr(), trees.Length,
                (TerrainGen.TreePrim*)prims.GetUnsafePtr(), prims.Length, out np, ref top);
            counts[0] = nt; counts[1] = np;
        }

        /// The tree overlay after a voxel pass (LOD 0: shift = half = 0). Coarse
        /// voxel i is fine voxel origin + (i << shift) + half; an AIR voxel takes
        /// the first trunk, then leaf primitive containing it. A uniform brick is
        /// expanded into mats when first touched (LOD 0 leaves its mats unwritten);
        /// touched bricks are re-checked for uniformity at the end.
        internal static void OverlayTrees(NativeArray<TerrainGen.Tree> trees, NativeArray<TerrainGen.TreePrim> prims, int nTrees,
                                          int3 origin, int shift, int half, NativeArray<byte> toEngine,
                                          NativeArray<byte> mats, NativeArray<byte> brickMat, NativeArray<bool> brickUniform)
        {
            if (nTrees <= 0) return;
            const int E = 128;
            byte air = toEngine[TerrainGen.GenMat.Air];
            int step = 1 << shift;
            var touched = new NativeArray<bool>(EngineConfig.BRICKS_PER_CHUNK, Allocator.Temp);
            for (int pass = 0; pass < 2; pass++)
            for (int t = 0; t < nTrees; t++)
            {
                var tr = trees[t];
                for (int pi = tr.prim0; pi < tr.prim0 + tr.primCount; pi++)
                {
                    var p = prims[pi];
                    if (p.leaf != pass) continue;
                    int3 i0 = math.max((p.lo - origin - half + (step - 1)) >> shift, 0);   // first sample >= lo
                    int3 i1 = math.min((p.hi - origin - half) >> shift, E - 1);           // last sample <= hi
                    if (math.any(i0 > i1)) continue;
                    byte m = toEngine[p.mat];
                    for (int by = i0.y >> 3; by <= i1.y >> 3; by++)
                    for (int bz = i0.z >> 3; bz <= i1.z >> 3; bz++)
                    for (int bx = i0.x >> 3; bx <= i1.x >> 3; bx++)
                    {
                        int bi = (bz << 8) | (by << 4) | bx;
                        if (brickUniform[bi] && !touched[bi])
                        {
                            if (brickMat[bi] != air) continue;   // uniform solid or fluid: no air to fill
                            for (int vy = 0; vy < 8; vy++)
                            for (int vz = 0; vz < 8; vz++)
                            {
                                int row = (((by << 3) + vy) << 14) | (((bz << 3) + vz) << 7) | (bx << 3);
                                for (int vx = 0; vx < 8; vx++) mats[row + vx] = air;
                            }
                        }
                        touched[bi] = true;
                        int3 b0 = new int3(bx, by, bz) << 3;
                        int3 v0 = math.max(b0, i0), v1 = math.min(b0 + 7, i1);
                        bool full = TerrainGen.Trees.ContainsBox(in p, origin + (v0 << shift) + half, origin + (v1 << shift) + half);
                        for (int y = v0.y; y <= v1.y; y++)
                        for (int z = v0.z; z <= v1.z; z++)
                        for (int x = v0.x; x <= v1.x; x++)
                        {
                            int idx = (y << 14) | (z << 7) | x;
                            if (mats[idx] != air) continue;
                            if (full || TerrainGen.Trees.Contains(in p, origin + (new int3(x, y, z) << shift) + half)) mats[idx] = m;
                        }
                    }
                }
            }
            for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++)
            {
                if (!touched[bi]) continue;
                int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
                byte first = mats[((by << 3) << 14) | ((bz << 3) << 7) | (bx << 3)];
                bool uni = true;
                for (int vy = 0; vy < 8 && uni; vy++)
                for (int vz = 0; vz < 8 && uni; vz++)
                {
                    int row = (((by << 3) + vy) << 14) | (((bz << 3) + vz) << 7) | (bx << 3);
                    for (int vx = 0; vx < 8; vx++) if (mats[row + vx] != first) { uni = false; break; }
                }
                brickUniform[bi] = uni;
                brickMat[bi] = first;
            }
            touched.Dispose();
        }

        /// PointVoxel's tree step: the trees touching column (v.x, v.z), gathered
        /// once per column into the PointColumn (Allocator.Temp: point jobs only).
        internal static unsafe byte PointTree(in TerrainGen.TerrainSpec spec, in TerrainGen.MapView map, in TerrainGen.Offsets o,
                                              int3 v, ref PointColumn col)
        {
            if (!col.treesValid)
            {
                int cap = TerrainGen.Trees.MaxTrees(v.x, v.z, v.x, v.z);
                if (!col.treePrims.IsCreated || col.treePrims.Length < cap * TerrainGen.Trees.MAX_PRIMS)
                {
                    col.treeList = new NativeArray<TerrainGen.Tree>(cap, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    col.treePrims = new NativeArray<TerrainGen.TreePrim>(cap * TerrainGen.Trees.MAX_PRIMS, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                }
                int top = 0;
                TerrainGen.Trees.Gather(in spec, in map, in o, v.x, v.z, v.x, v.z,
                    (TerrainGen.Tree*)col.treeList.GetUnsafePtr(), col.treeList.Length,
                    (TerrainGen.TreePrim*)col.treePrims.GetUnsafePtr(), col.treePrims.Length, out col.treePrimCount, ref top);
                col.treesValid = true;
            }
            if (col.treePrimCount == 0) return TerrainGen.GenMat.Air;
            return TerrainGen.Trees.MaterialAt((TerrainGen.TreePrim*)col.treePrims.GetUnsafeReadOnlyPtr(), col.treePrimCount, v);
        }
    }
}
