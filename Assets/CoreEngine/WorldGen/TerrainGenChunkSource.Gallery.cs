// Assets/CoreEngine/WorldGen/TerrainGenChunkSource.Gallery.cs
//
// Task 200 (EXAMPLES ONLY, owner 2026-11-13 trees talk T2 "I'd need to see
// examples"): `-treegallery <layout file>` places TerrainGen.TreeGallery's
// prototype trees (and today's Trees.cs trees, moved) on chosen open sites, for
// in-engine stills. DEFAULT OFF: without the flag the gallery is empty and every
// hook below returns at its first line -- the world, today's trees included, is
// byte for byte what it was (TreeGalleryTests.TodayTrees_Pinned; the tree tests).
//
// The layout file (written by TreeGalleryTests.Gallery_Layout), one item a line,
// voxel coordinates:
//   tree   <species> <method> <seed> <x> <z>   a prototype tree rooted on the ground at (x, z)
//   today  <cx> <cz> <x> <z>                   Trees.Build's tree of tree cell (cx, cz), moved to (x, z)
//   marker <x> <z>                             a 0.4 x 0.3 x 1.8 m person-height post
//   clear  <x0> <z0> <x1> <z1>                 no natural tree rooted in this rectangle (tree plane cells zeroed)
//
// HOW IT IS DRAWN. Each item is built ONCE here (main thread, constructor) into a
// brick volume (8^3 bytes, GenMats), packed into unmanaged memory and published
// through a SharedStatic so the Burst chunk jobs can read it without new job
// fields. Then it follows today's trees' rule exactly: it fills AIR only;
//   GatherTrees   raises the column top by the gallery's crowns (air fast paths stay exact)
//   OverlayTrees  OverlayGallery first (gallery wins over a natural tree), LOD 0 and shells <= Trees.MAX_SHELL
//   PointTree     the same voxel for the point query (FarQuery)
// One gallery per process (the first source built with the flag owns it).
using System;
using System.Collections.Generic;
using System.IO;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace VoxelEngine.WorldGen
{
    public unsafe struct GalleryVol
    {
        public int3 lo, hi, nb;   // absolute voxel box (brick-aligned lo), bricks per axis
        public int* index;        // brick -> slot, -1 = empty; (b.y * nb.z + b.z) * nb.x + b.x
        public byte* bricks;      // slot * 512 + (y << 6 | z << 3 | x): GenMat, 0 = none
    }

    public unsafe struct GalleryData
    {
        public GalleryVol* vols;
        public int count;
        public int3 lo, hi;       // union of every volume's box
    }

    public static class TreeGalleryShared
    {
        private sealed class Key { }
        public static readonly SharedStatic<GalleryData> Data = SharedStatic<GalleryData>.GetOrCreate<Key>();
    }

    public sealed partial class TerrainGenChunkSource
    {
        private bool _galleryOwner;

        /// -treegallery <layout file> (task 200): absent = no gallery (the default). Last match wins.
        public static string TreeGalleryArg()
        {
            string[] a = Environment.GetCommandLineArgs();
            string v = null;
            for (int i = 0; i + 1 < a.Length; i++)
                if (a[i] == "-treegallery") v = a[i + 1];
            return string.IsNullOrEmpty(v) || v == "0" ? null : v;
        }

        /// Build the gallery from a layout file (constructor; also tests). `treeCells` is
        /// the tree density plane BEFORE the source copies it: clear rectangles are zeroed
        /// in it; today's trees are built from an untouched copy first.
        public unsafe void BuildGallery(string path, byte[] treeCells)
        {
            if (TreeGalleryShared.Data.Data.count != 0) { UnityEngine.Debug.LogWarning("[TreeGallery] a gallery already exists; ignored"); return; }
            if (!File.Exists(path)) { UnityEngine.Debug.LogError($"[TreeGallery] no layout file {path}"); return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var view = Map.View;
            var spec = Spec; var o = O;
            int GroundY(int x, int z) => TerrainGen.ShapeField.Sample(in spec, in view, in o, x + 0.5f, z + 0.5f, 1).height;
            var built = new List<(int3 root, TerrainGen.TreeGallery.Vol v)>();
            var origPlane = (byte[])treeCells.Clone();
            long bytes = 0; int trees = 0, todays = 0, markers = 0, clears = 0;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] f = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                int I(int k) => int.Parse(f[k], System.Globalization.CultureInfo.InvariantCulture);
                switch (f[0])
                {
                    case "tree":
                    {
                        int sp = I(1), me = I(2), x = I(4), z = I(5); uint seed = (uint)I(3);
                        int gy = GroundY(x, z);
                        var v = TerrainGen.TreeGallery.Build(sp, me, seed, (lx, lz) => GroundY(x + lx, z + lz) - gy, out var st, out _);
                        built.Add((new int3(x, gy, z), v)); trees++;
                        UnityEngine.Debug.Log($"[TreeGallery] tree {TerrainGen.TreeGallery.SpeciesName[sp]}/{TerrainGen.TreeGallery.MethodName[me]}/{seed} at ({x}, {gy}, {z}): " +
                                              $"trunk {st.trunk} leaf {st.leaf} vox, {st.segs} segs {st.clumps} clumps, {st.bytes / 1048576f:F1} MB, grow {st.msGrow:F0} + vox {st.msVox:F0} ms");
                        break;
                    }
                    case "today":
                    {
                        int cx = I(1), cz = I(2), x = I(3), z = I(4);
                        var buf = new TerrainGen.TreePrim[TerrainGen.Trees.MAX_PRIMS];
                        fixed (byte* pl = origPlane)
                        fixed (TerrainGen.TreePrim* pb = buf)
                        {
                            var tv = view; tv.trees = pl;
                            int n = 0;
                            if (!TerrainGen.Trees.Build(in spec, in tv, in o, cx, cz, pb, ref n, out var t)) { UnityEngine.Debug.LogWarning($"[TreeGallery] today cell ({cx}, {cz}) has no tree"); break; }
                            var v = TerrainGen.TreeGallery.FromPrims(pb, n, t.root, out var st);
                            built.Add((new int3(x, GroundY(x, z), z), v)); todays++;
                            UnityEngine.Debug.Log($"[TreeGallery] today cell ({cx}, {cz}) -> ({x}, {z}): trunk {st.trunk} leaf {st.leaf} vox, {n} prims");
                        }
                        break;
                    }
                    case "marker":
                    {
                        int x = I(1), z = I(2);
                        var v = new TerrainGen.TreeGallery.Vol(new int3(0, 0, 0), new int3(7, 23, 7));
                        for (int y = 1; y <= 18; y++) for (int dz = 0; dz < 3; dz++) for (int dx = 0; dx < 4; dx++)
                            v.SetIfEmpty(new int3(dx, y, dz), TerrainGen.TreeGallery.MARKER_MAT);
                        built.Add((new int3(x, GroundY(x, z), z), v)); markers++;
                        break;
                    }
                    case "clear":
                    {
                        int cv = Spec.mapCellVoxels, n = Spec.mapCells;
                        for (int cz = math.max(0, I(2) / cv); cz <= math.min(n - 1, I(4) / cv); cz++)
                        for (int cx = math.max(0, I(1) / cv); cx <= math.min(n - 1, I(3) / cv); cx++)
                            treeCells[cz * n + cx] = 0;
                        clears++;
                        break;
                    }
                }
            }
            // pack: one unmanaged block per volume
            var d = new GalleryData { count = built.Count, lo = int.MaxValue, hi = int.MinValue };
            d.vols = (GalleryVol*)UnsafeUtility.Malloc(sizeof(GalleryVol) * math.max(1, built.Count), 16, Allocator.Persistent);
            for (int i = 0; i < built.Count; i++)
            {
                var (root, v) = built[i];
                var g = new GalleryVol { lo = root + v.lo, nb = v.nb };
                g.hi = g.lo + v.nb * 8 - 1;
                g.index = (int*)UnsafeUtility.Malloc(4L * v.index.Length, 4, Allocator.Persistent);
                for (int k = 0; k < v.index.Length; k++) g.index[k] = v.index[k];
                g.bricks = (byte*)UnsafeUtility.Malloc(512L * math.max(1, v.bricks.Count), 16, Allocator.Persistent);
                for (int s = 0; s < v.bricks.Count; s++)
                    fixed (byte* src = v.bricks[s]) UnsafeUtility.MemCpy(g.bricks + 512L * s, src, 512);
                bytes += 4L * v.index.Length + 512L * v.bricks.Count;
                d.vols[i] = g;
                d.lo = math.min(d.lo, g.lo); d.hi = math.max(d.hi, g.hi);
            }
            TreeGalleryShared.Data.Data = d;
            _galleryOwner = true;
            _columnTop.Clear(); _specStamp++;   // a test may build it into a running source
            UnityEngine.Debug.Log($"[TreeGallery] {path}: {trees} prototype trees, {todays} today's trees, {markers} markers, {clears} clear rects; " +
                                  $"{bytes / 1048576f:F1} MB; built in {sw.ElapsedMilliseconds} ms; box {d.lo} .. {d.hi}");
        }

        private unsafe void DisposeGallery()
        {
            if (!_galleryOwner) return;
            _galleryOwner = false;
            var d = TreeGalleryShared.Data.Data;
            TreeGalleryShared.Data.Data = default;
            for (int i = 0; i < d.count; i++) { UnsafeUtility.Free(d.vols[i].index, Allocator.Persistent); UnsafeUtility.Free(d.vols[i].bricks, Allocator.Persistent); }
            if (d.vols != null) UnsafeUtility.Free(d.vols, Allocator.Persistent);
        }

        /// The gallery's GenMat at fine voxel v (0 = none). Burst-safe.
        public static unsafe byte GalleryAt(int3 v)
        {
            ref var d = ref TreeGalleryShared.Data.Data;
            if (d.count == 0 || math.any(v < d.lo) || math.any(v > d.hi)) return 0;
            for (int i = 0; i < d.count; i++)
            {
                byte m = VolAt(ref d.vols[i], v);
                if (m != 0) return m;
            }
            return 0;
        }

        private static unsafe byte VolAt(ref GalleryVol g, int3 v)
        {
            if (math.any(v < g.lo) || math.any(v > g.hi)) return 0;
            int3 l = v - g.lo, b = l >> 3, w = l & 7;
            int s = g.index[(b.y * g.nb.z + b.z) * g.nb.x + b.x];
            return s < 0 ? (byte)0 : g.bricks[512L * s + ((w.y << 6) | (w.z << 3) | w.x)];
        }

        /// The highest gallery voxel box over columns [x0, x1] x [z0, z1] (int.MinValue = none).
        internal static unsafe int GalleryTop(int x0, int z0, int x1, int z1)
        {
            ref var d = ref TreeGalleryShared.Data.Data;
            int top = int.MinValue;
            for (int i = 0; i < d.count; i++)
            {
                var g = d.vols[i];
                if (g.hi.x < x0 || g.lo.x > x1 || g.hi.z < z0 || g.lo.z > z1) continue;
                top = math.max(top, g.hi.y);
            }
            return top;
        }

        /// OverlayTrees' rule for the gallery: coarse voxel i = fine origin + (i << shift) + half;
        /// an AIR voxel takes the gallery's material. Uniform air bricks are expanded when first
        /// written; written bricks are re-checked for uniformity.
        internal static unsafe void OverlayGallery(int3 origin, int shift, int half, NativeArray<byte> toEngine,
                                                   NativeArray<byte> mats, NativeArray<byte> brickMat, NativeArray<bool> brickUniform)
        {
            ref var d = ref TreeGalleryShared.Data.Data;
            if (d.count == 0 || shift > TerrainGen.Trees.MAX_SHELL) return;
            const int E = 128;
            int3 cLo = origin + half, cHi = origin + half + ((E - 1) << shift);
            if (math.any(cHi < d.lo) || math.any(cLo > d.hi)) return;
            byte air = toEngine[TerrainGen.GenMat.Air];
            int step = 1 << shift;
            var touched = new NativeArray<bool>(EngineConfig.BRICKS_PER_CHUNK, Allocator.Temp);
            bool any = false;
            for (int t = 0; t < d.count; t++)
            {
                ref var g = ref d.vols[t];
                int3 i0 = math.max((g.lo - origin - half + (step - 1)) >> shift, 0);
                int3 i1 = math.min((g.hi - origin - half) >> shift, E - 1);
                if (math.any(i0 > i1)) continue;
                for (int by = i0.y >> 3; by <= i1.y >> 3; by++)
                for (int bz = i0.z >> 3; bz <= i1.z >> 3; bz++)
                for (int bx = i0.x >> 3; bx <= i1.x >> 3; bx++)
                {
                    int bi = (bz << 8) | (by << 4) | bx;
                    if (brickUniform[bi] && !touched[bi] && brickMat[bi] != air) continue;   // uniform solid / fluid: no air
                    int3 b0 = new int3(bx, by, bz) << 3;
                    int3 v0 = math.max(b0, i0), v1 = math.min(b0 + 7, i1);
                    for (int y = v0.y; y <= v1.y; y++)
                    for (int z = v0.z; z <= v1.z; z++)
                    for (int x = v0.x; x <= v1.x; x++)
                    {
                        byte gm = VolAt(ref g, origin + (new int3(x, y, z) << shift) + half);
                        if (gm == 0) continue;
                        if (!touched[bi])
                        {
                            if (brickUniform[bi])   // uniform air: expand before the first write
                                for (int vy = 0; vy < 8; vy++)
                                for (int vz = 0; vz < 8; vz++)
                                {
                                    int row = (((by << 3) + vy) << 14) | (((bz << 3) + vz) << 7) | (bx << 3);
                                    for (int vx = 0; vx < 8; vx++) mats[row + vx] = air;
                                }
                            touched[bi] = true; any = true;
                        }
                        int idx = (y << 14) | (z << 7) | x;
                        if (mats[idx] == air) mats[idx] = toEngine[gm];
                    }
                }
            }
            if (any)
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
    }
}
