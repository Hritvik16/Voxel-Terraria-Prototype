// Assets/TerrainGen/CrownCore.cs
//
// Task 243 (lever F3 of PERF_SLICE_BUDGETS.md sec. 6; owner Q-AT D4): SOLID CROWN CORES.
// Behind TerrainSpec.crownCore (`-crowncore 1|2`), DEFAULT OFF: a look change the owner
// approves by eye first. OFF = the library is built exactly as before, byte for byte.
//
// WHAT IT DOES. A world-tree crown is porous all the way through (TreeGallery.Voxelise:
// lumpy clumps with hash holes, gaps between clumps, an open interior where the species
// has one), so a primary ray, a sun ray or a sky ray threads deep into it before it stops.
// CrownCore fills the crown's INTERIOR air with the crown's own leaf, leaving the outer
// SKIN (2 voxels) exactly as grown -- a ray that slips through a skin hole stops ~3 voxels in.
//
// THE RULE IS DERIVED FROM THE CROWN'S OWN VOXELS (no per-species or per-seed numbers):
//   box B      the bounding box of the prototype's LEAF voxels (nothing outside the crown's
//              extent is ever touched: trunk below the crown, roots, feet stay as grown);
//   solid S    every non-air voxel in B (leaf, wood, a third material);
//   closed K   mode 1: K = S. Mode 2 ("closed crown"): K = the morphological CLOSING of S by a
//              (2 r + 1)^3 box, r = the crown's own leaf-clump radius (TreeGallery.Sp.clumpR of
//              that prototype, rounded): gaps between leaf masses narrower than ~2 clumps count
//              as crown. Closing never grows past the convex outline of the leaves, so the
//              crown's outer silhouette is kept; only its gaps become shallow dimples;
//   hull H     the voxels of B ENCLOSED by K along all three axes: on the line through the voxel
//              along x there is K on both sides (or the voxel is in K), the same along y and z.
//              Parameter-free; a concave notch open along any axis stays out;
//   core C     H eroded by a (2 s + 1)^3 box, s = SKIN = 2: a voxel is core only when every voxel
//              within Chebyshev distance 2 is in the hull, so the outer 2 voxels keep their grown
//              raggedness. 2 = the leaf texture's own hole cell (the hole hash runs on
//              (x >> 1, y >> 1, z >> 1) cells in Voxelise): one full hole cell of ragged skin;
//   fill       the AIR voxels of C that are 6-connected, through other filled voxels, to an
//              original solid voxel (which Voxelise's crumb filter already tied to the wood):
//              no floating leaves, the "no crumbs" contract holds. Wood is never overwritten.
// Adversarial cases (CrownCoreTests): a tiny crown (4^3: unchanged), a crown that is all skin
// (a 4-voxel slab: unchanged), a concave open bowl (its air stays air), a hollow 1-voxel shell
// (its core touches nothing: dropped, never floating) and the same with a branch (filled), a
// very large porous lens (400 x 80 x 400: solid 2 voxels under an unchanged skin).
// FINDING (task 243): the approved crowns are made of small flat clumps (~5-6 voxels tall) with
// sky between them, i.e. nearly ALL SKIN: mode 1 fills ~0-1 % more leaf; mode 2 is the variant
// that actually closes a crown (HANDOFF 243 has the numbers and pictures). The library writes only into
// bricks a prototype already has (keepBricks): a first launch that also filled the pure-interior bricks
// added 21 % prototype bricks, +4.5 % tree GPU bodies at forest_eye and refusals 21 -> 35 at an oak, for
// bricks no ray from outside can reach.
//
// WHY THIS IS THE EFFICIENT DESIGN (PREAMBLE rule 11). It runs ONCE PER PROTOTYPE at the
// library build, on the library's worker threads: every chunk, shell and point query reads
// the same prototype, so the world pays nothing per chunk, per frame or per tree. Scratch is
// bit-packed (one bit a voxel of B, x rows of 64-bit words; the largest banyan crown, padded,
// ~450 x 270 x 450 = ~7 MB a plane, four planes) and freed when the prototype is done. It does
// NOT change placement: the placement probabilities read the bricks the prototype had BEFORE
// the fill (ProtoBuild.placeBricks), so ON and OFF place the same trees, roots and turns.
//
// Integer math only (bit shifts, masks); deterministic (no hash, no float).
using System.Collections.Generic;
using Unity.Mathematics;

namespace TerrainGen
{
    public static class CrownCore
    {
        /// The skin left as grown, voxels (task 243: "the outer 1-2 voxels of leaf ragged").
        public const int SKIN = 2;

        public struct Stats
        {
            public int3 boxLo, boxSize;   // B (local voxels, padded by `close`)
            public int close;
            public long solid, closed, hull, core, filled, dropped, skipped;   // voxels: S, K, H, C, air of C written as leaf, air of C dropped (not connected), air of C in bricks the prototype does not have (keepBricks)
            public int bricksBefore, bricksAfter;
        }

        /// A bit plane over the box: rows (y, z) of W 64-bit words along x.
        private sealed class Bits
        {
            public readonly int nx, ny, nz, W; public readonly ulong[] a;
            public Bits(int nx, int ny, int nz) { this.nx = nx; this.ny = ny; this.nz = nz; W = (nx + 63) >> 6; a = new ulong[(long)ny * nz * W]; }
            public long Row(int y, int z) => ((long)y * nz + z) * W;
            public bool Get(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < nx && y < ny && z < nz && ((a[Row(y, z) + (x >> 6)] >> (x & 63)) & 1UL) != 0;
            public void Set(int x, int y, int z) => a[Row(y, z) + (x >> 6)] |= 1UL << (x & 63);
            public long Count() { long c = 0; foreach (var w in a) c += math.countbits(w); return c; }
        }

        /// One radius-1 erosion (dilate = false) or dilation along every axis of `src` into `src`
        /// (via `tmp`); outside the box reads 0. Bits at or past nx are never set.
        private static void Step(Bits s, Bits t, bool dilate)
        {
            int W = s.W, ny = s.ny, nz = s.nz; long rows = (long)ny * nz;
            ulong lastMask = (s.nx & 63) == 0 ? ~0UL : (1UL << (s.nx & 63)) - 1;
            // x
            for (long r = 0; r < rows; r++)
            {
                long o = r * W;
                for (int w = 0; w < W; w++)
                {
                    ulong e = s.a[o + w];
                    ulong left = (e << 1) | (w > 0 ? s.a[o + w - 1] >> 63 : 0UL);        // bit x = e[x - 1]
                    ulong right = (e >> 1) | (w + 1 < W ? s.a[o + w + 1] << 63 : 0UL);   // bit x = e[x + 1]
                    ulong res = dilate ? e | left | right : e & left & right;
                    t.a[o + w] = w == W - 1 ? res & lastMask : res;
                }
            }
            // y
            for (int y = 0; y < ny; y++)
            for (int z = 0; z < nz; z++)
            {
                long o = t.Row(y, z), dn = o - (long)nz * W, up = o + (long)nz * W;
                for (int w = 0; w < W; w++)
                {
                    ulong a0 = y > 0 ? t.a[dn + w] : 0UL, a1 = y < ny - 1 ? t.a[up + w] : 0UL, e = t.a[o + w];
                    s.a[o + w] = dilate ? e | a0 | a1 : e & a0 & a1;
                }
            }
            // z
            for (int y = 0; y < ny; y++)
            for (int z = 0; z < nz; z++)
            {
                long o = s.Row(y, z);
                for (int w = 0; w < W; w++)
                {
                    ulong a0 = z > 0 ? s.a[o - W + w] : 0UL, a1 = z < nz - 1 ? s.a[o + W + w] : 0UL, e = s.a[o + w];
                    t.a[o + w] = dilate ? e | a0 | a1 : e & a0 & a1;
                }
            }
            System.Array.Copy(t.a, s.a, s.a.Length);
        }

        /// Fill the crown interior of `v` with `leaf` (see the header). `close` = 0 (mode 1) or the
        /// closing radius (mode 2: the crown's clump radius). `keepBricks` (the library's setting): write only
        /// into bricks the prototype already has -- a brick of pure interior is enclosed by filled ones and never
        /// drawn, so it is not stored (no new CPU / GPU body). Returns the voxels written.
        public static long Fill(TreeGallery.Vol v, byte leaf, out Stats st, int skin = SKIN, int close = 0, bool keepBricks = false)
        {
            st = default; st.close = close;
            st.bricksBefore = CountBricks(v);
            // ---- B: the leaf voxels' box, padded by the closing radius ----
            int3 lo = int.MaxValue, hi = int.MinValue;
            for (int bi = 0; bi < v.index.Length; bi++)
            {
                int sl = v.index[bi]; if (sl < 0) continue;
                int3 bo = v.lo + BrickOrigin(v, bi);
                var b = v.bricks[sl];
                for (int j = 0; j < 512; j++)
                {
                    if (b[j] != leaf) continue;
                    int3 p = bo + new int3(j & 7, j >> 6, (j >> 3) & 7);
                    lo = math.min(lo, p); hi = math.max(hi, p);
                }
            }
            if (lo.x > hi.x) { st.bricksAfter = st.bricksBefore; return 0; }
            int3 ilo = lo, ihi = hi;   // the unpadded leaf box: S is read only inside it
            lo -= close; hi += close;
            int3 n = hi - lo + 1;
            st.boxLo = lo; st.boxSize = n;
            int nx = n.x, ny = n.y, nz = n.z;
            // ---- S ----
            var S = new Bits(nx, ny, nz);
            for (int bi = 0; bi < v.index.Length; bi++)
            {
                int sl = v.index[bi]; if (sl < 0) continue;
                int3 bo = v.lo + BrickOrigin(v, bi);
                if (math.any(bo + 7 < ilo) || math.any(bo > ihi)) continue;
                var b = v.bricks[sl];
                for (int j = 0; j < 512; j++)
                {
                    if (b[j] == 0) continue;
                    int3 p = bo + new int3(j & 7, j >> 6, (j >> 3) & 7);
                    if (math.any(p < ilo) || math.any(p > ihi)) continue;
                    p -= lo;
                    S.Set(p.x, p.y, p.z);
                }
            }
            st.solid = S.Count();
            // ---- K: S closed by a (2 close + 1)^3 box (separable: radius-1 steps) ----
            var K = new Bits(nx, ny, nz); var T = new Bits(nx, ny, nz);
            System.Array.Copy(S.a, K.a, S.a.Length);
            for (int i = 0; i < close; i++) Step(K, T, true);
            for (int i = 0; i < close; i++) Step(K, T, false);
            for (long i = 0; i < K.a.Length; i++) K.a[i] |= S.a[i];   // closing is extensive; exact for a box, kept explicit
            st.closed = K.Count();
            // ---- per-line extents of K along y (per x, z) and z (per x, y) ----
            var minY = new short[nz * nx]; var maxY = new short[nz * nx];
            var minZ = new short[ny * nx]; var maxZ = new short[ny * nx];
            for (int i = 0; i < minY.Length; i++) { minY[i] = short.MaxValue; maxY[i] = short.MinValue; }
            for (int i = 0; i < minZ.Length; i++) { minZ[i] = short.MaxValue; maxZ[i] = short.MinValue; }
            for (int y = 0; y < ny; y++)
            for (int z = 0; z < nz; z++)
            {
                long r = K.Row(y, z);
                for (int w = 0; w < K.W; w++)
                {
                    ulong a = K.a[r + w];
                    while (a != 0)
                    {
                        int x = (w << 6) + math.tzcnt(a); a &= a - 1;
                        int iy = z * nx + x, iz = y * nx + x;
                        if (y < minY[iy]) minY[iy] = (short)y;
                        if (y > maxY[iy]) maxY[iy] = (short)y;
                        if (z < minZ[iz]) minZ[iz] = (short)z;
                        if (z > maxZ[iz]) maxZ[iz] = (short)z;
                    }
                }
            }
            // ---- H: enclosed by K along x (first..last of the row), y and z ----
            var H = T; System.Array.Clear(H.a, 0, H.a.Length);
            for (int y = 0; y < ny; y++)
            for (int z = 0; z < nz; z++)
            {
                long r = K.Row(y, z);
                int fx = -1, lx = -1;
                for (int w = 0; w < K.W; w++) if (K.a[r + w] != 0) { fx = (w << 6) + math.tzcnt(K.a[r + w]); break; }
                if (fx < 0) continue;
                for (int w = K.W - 1; w >= 0; w--) if (K.a[r + w] != 0) { lx = (w << 6) + 63 - math.lzcnt(K.a[r + w]); break; }
                for (int x = fx; x <= lx; x++)
                {
                    int iy = z * nx + x, iz = y * nx + x;
                    if (y < minY[iy] || y > maxY[iy] || z < minZ[iz] || z > maxZ[iz]) continue;
                    H.a[r + (x >> 6)] |= 1UL << (x & 63);
                    st.hull++;
                }
            }
            minY = maxY = minZ = maxZ = null;
            // ---- C: H eroded by a (2 skin + 1)^3 box ----
            var C = H; var tmp = K;   // K is done
            for (int i = 0; i < skin; i++) Step(C, tmp, false);
            st.core = C.Count();
            // ---- A = C & ~S; keep what is 6-connected to S through A ----
            var A = C;
            for (long i = 0; i < A.a.Length; i++) A.a[i] &= ~S.a[i];
            if (keepBricks)
            {   // A &= the voxels of bricks v already holds
                var keep = new Bits(nx, ny, nz);
                for (int bi = 0; bi < v.index.Length; bi++)
                {
                    if (v.index[bi] < 0) continue;
                    int3 bo = v.lo + BrickOrigin(v, bi) - lo;
                    int3 b0 = math.max(bo, 0), b1 = math.min(bo + 7, n - 1);
                    if (math.any(b0 > b1)) continue;
                    // the brick's x run [b0.x, b1.x] (<= 8 bits, at most two words) on each of its (y, z) rows
                    int w0 = b0.x >> 6, w1 = b1.x >> 6;
                    ulong m0 = (~0UL << (b0.x & 63)) & (w0 == w1 ? (~0UL >> (63 - (b1.x & 63))) : ~0UL);
                    ulong m1 = w0 == w1 ? 0UL : ~0UL >> (63 - (b1.x & 63));
                    for (int y = b0.y; y <= b1.y; y++)
                        for (int z = b0.z; z <= b1.z; z++)
                        {
                            long r = keep.Row(y, z);
                            keep.a[r + w0] |= m0;
                            if (w1 != w0) keep.a[r + w1] |= m1;
                        }
                }
                for (long i = 0; i < A.a.Length; i++) { st.skipped += math.countbits(A.a[i] & ~keep.a[i]); A.a[i] &= keep.a[i]; }
            }
            var seen = tmp; System.Array.Clear(seen.a, 0, seen.a.Length);
            var stack = new Stack<long>();
            for (int y = 0; y < ny; y++)
            for (int z = 0; z < nz; z++)
            {
                long r = A.Row(y, z);
                for (int w = 0; w < A.W; w++)
                {
                    ulong a = A.a[r + w];
                    while (a != 0)
                    {
                        int x = (w << 6) + math.tzcnt(a); a &= a - 1;
                        if (S.Get(x - 1, y, z) || S.Get(x + 1, y, z) || S.Get(x, y - 1, z) || S.Get(x, y + 1, z) || S.Get(x, y, z - 1) || S.Get(x, y, z + 1))
                        {
                            seen.Set(x, y, z);
                            stack.Push(Pack(x, y, z));
                        }
                    }
                }
            }
            while (stack.Count > 0)
            {
                long p = stack.Pop();
                int x = (int)(p & 0xFFFF), y = (int)((p >> 16) & 0xFFFF), z = (int)((p >> 32) & 0xFFFF);
                for (int d = 0; d < 6; d++)
                {
                    int qx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), qy = y + (d == 2 ? 1 : d == 3 ? -1 : 0), qz = z + (d == 4 ? 1 : d == 5 ? -1 : 0);
                    if (!A.Get(qx, qy, qz) || seen.Get(qx, qy, qz)) continue;
                    seen.Set(qx, qy, qz);
                    stack.Push(Pack(qx, qy, qz));
                }
            }
            // ---- write ----
            long wrote = 0;
            for (int y = 0; y < ny; y++)
            for (int z = 0; z < nz; z++)
            {
                long r = A.Row(y, z);
                for (int w = 0; w < A.W; w++)
                {
                    st.dropped += math.countbits(A.a[r + w] & ~seen.a[r + w]);
                    ulong a = seen.a[r + w];
                    while (a != 0)
                    {
                        int x = (w << 6) + math.tzcnt(a); a &= a - 1;
                        if (v.SetIfEmpty(lo + new int3(x, y, z), leaf)) wrote++;
                    }
                }
            }
            st.filled = wrote;
            st.bricksAfter = CountBricks(v);
            return wrote;
        }

        private static long Pack(int x, int y, int z) => (long)x | ((long)y << 16) | ((long)z << 32);

        private static int3 BrickOrigin(TreeGallery.Vol v, int bi)
            => new int3(bi % v.nb.x, bi / (v.nb.x * v.nb.z), (bi / v.nb.x) % v.nb.z) * 8;

        private static int CountBricks(TreeGallery.Vol v)
        {
            int c = 0;
            for (int i = 0; i < v.index.Length; i++)
            {
                int sl = v.index[i]; if (sl < 0) continue;
                var b = v.bricks[sl];
                for (int j = 0; j < 512; j++) if (b[j] != 0) { c++; break; }
            }
            return c;
        }
    }
}
