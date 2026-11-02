// ==========================================
// Assets/CoreEngine/Tests/RoofPyramidIncrementalTests.cs
//
// TASK 168 (PERF_EXACT_CUTS D). Raymarch.compute's incremental roof pyramid
// (BuildHeightRawCols / DilateCols / LevelCols) rebuilds only the ring chunk
// columns TerrainClipmap wrote, plus the 1-brick dilation margin and every coarser
// cell above them. This is a CPU twin of the same per-word functions and the
// same region formulas, checked word for word against a full rebuild after
// random write sequences (single columns, neighbours, the ring seam, many at
// once). The GPU side is checked in the player by -roofverify (HeightVerify).
// ==========================================
using System;
using System.Collections.Generic;
using NUnit.Framework;

public class RoofPyramidIncrementalTests
{
    const int Levels = 6, ChunkBricks = 16;

    sealed class Pyr
    {
        public readonly int Nx, Nz;
        public readonly uint[] Src;      // per ring brick column: the "clipmap contents" (its raw height)
        public readonly uint[] Raw;      // the raw scratch (_HeightOffsetsB.z)
        public readonly uint[][] Lv;     // levels 0..5
        public Pyr(int nx, int nz)
        {
            Nx = nx; Nz = nz; Src = new uint[nx * nz]; Raw = new uint[nx * nz]; Lv = new uint[Levels][];
            for (int L = 0; L < Levels; L++) Lv[L] = new uint[(nx >> L) * (nz >> L)];
        }
        public void RawAt(int rx, int rz) => Raw[rx + rz * Nx] = Src[rx + rz * Nx];
        public void DilateAt(int rx, int rz)
        {
            uint m = 0;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                m = Math.Max(m, Raw[((rx + dx) & (Nx - 1)) + ((rz + dz) & (Nz - 1)) * Nx]);
            Lv[0][rx + rz * Nx] = (m + 15u) & ~15u;
        }
        public void LevelAt(int L, int cx, int cz)
        {
            int nx = Nx >> L, cnx = nx << 1;
            uint[] s = Lv[L - 1];
            int x2 = cx << 1, z2 = cz << 1;
            Lv[L][cx + cz * nx] = Math.Max(Math.Max(s[x2 + z2 * cnx], s[x2 + 1 + z2 * cnx]),
                                           Math.Max(s[x2 + (z2 + 1) * cnx], s[x2 + 1 + (z2 + 1) * cnx]));
        }
        public void Full()
        {
            for (int z = 0; z < Nz; z++) for (int x = 0; x < Nx; x++) RawAt(x, z);
            for (int z = 0; z < Nz; z++) for (int x = 0; x < Nx; x++) DilateAt(x, z);
            for (int L = 1; L < Levels; L++)
                for (int z = 0; z < (Nz >> L); z++) for (int x = 0; x < (Nx >> L); x++) LevelAt(L, x, z);
        }
        // The shader's *Cols kernels, thread for thread (dispatch sizes 16x16, 24x24 with an 18 guard, 16x16 with the nX guard).
        public void Incremental(List<(int cx, int cz)> cols)
        {
            foreach (var (cx, cz) in cols)
                for (int ty = 0; ty < 16; ty++) for (int tx = 0; tx < 16; tx++) RawAt(cx * 16 + tx, cz * 16 + ty);
            foreach (var (cx, cz) in cols)
                for (int ty = 0; ty < 24; ty++) for (int tx = 0; tx < 24; tx++)
                {
                    if (tx >= 18 || ty >= 18) continue;
                    DilateAt((cx * 16 - 1 + tx) & (Nx - 1), (cz * 16 - 1 + ty) & (Nz - 1));
                }
            for (int L = 1; L < Levels; L++)
                foreach (var (cx, cz) in cols)
                {
                    int bx = cx * 16, bz = cz * 16;
                    int lox = (bx - 1) >> L, loz = (bz - 1) >> L;
                    int nX = ((bx + 16) >> L) - lox + 1, nZ = ((bz + 16) >> L) - loz + 1;
                    for (int ty = 0; ty < 16; ty++) for (int tx = 0; tx < 16; tx++)
                    {
                        if (tx >= nX || ty >= nZ) continue;
                        LevelAt(L, (lox + tx) & ((Nx >> L) - 1), (loz + ty) & ((Nz >> L) - 1));
                    }
                }
        }
    }

    static int Diff(Pyr a, Pyr b)
    {
        int n = 0;
        for (int i = 0; i < a.Raw.Length; i++) if (a.Raw[i] != b.Raw[i]) n++;
        for (int L = 0; L < Levels; L++) for (int i = 0; i < a.Lv[L].Length; i++) if (a.Lv[L][i] != b.Lv[L][i]) n++;
        return n;
    }

    [TestCase(8, 8, 1)]
    [TestCase(16, 16, 2)]     // play's ring: 16x16 chunks = 256x256 bricks
    [TestCase(16, 8, 3)]
    public void Incremental_EqualsFullRebuild_AfterRandomWrites(int chunksX, int chunksZ, int seed)
    {
        int nx = chunksX * ChunkBricks, nz = chunksZ * ChunkBricks;
        var rng = new Random(seed);
        var inc = new Pyr(nx, nz);
        for (int i = 0; i < inc.Src.Length; i++) inc.Src[i] = (uint)rng.Next(0, 40) * 8u;
        inc.Full();
        for (int step = 0; step < 60; step++)
        {
            // Which columns this "frame" writes: none, one, a neighbour pair, the seam corners, or many.
            var cols = new List<(int, int)>();
            int kind = step % 5;
            if (kind == 1) cols.Add((rng.Next(chunksX), rng.Next(chunksZ)));
            else if (kind == 2) { int x = rng.Next(chunksX), z = rng.Next(chunksZ); cols.Add((x, z)); cols.Add(((x + 1) & (chunksX - 1), z)); }
            else if (kind == 3) { cols.Add((0, 0)); cols.Add((chunksX - 1, chunksZ - 1)); cols.Add((0, chunksZ - 1)); }
            else if (kind == 4) for (int k = rng.Next(1, chunksX * chunksZ / 2); k > 0; k--) cols.Add((rng.Next(chunksX), rng.Next(chunksZ)));
            // A write changes the contents of ITS column only (a chunk-column's bricks), sometimes to 0
            // (eviction to air) or to the top (a tall edit).
            var seen = new HashSet<(int, int)>();
            foreach (var (cx, cz) in cols)
            {
                if (!seen.Add((cx, cz))) continue;
                for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
                {
                    int r = rng.Next(10);
                    if (r < 4) continue;
                    inc.Src[cx * 16 + x + (cz * 16 + z) * nx] = r == 4 ? 0u : r == 5 ? 512u : (uint)rng.Next(0, 40) * 8u;
                }
            }
            inc.Incremental(cols);   // duplicates allowed, as on the GPU (TakeHeightDirtyColumns dedups anyway)
            var full = new Pyr(nx, nz);
            Array.Copy(inc.Src, full.Src, inc.Src.Length);
            full.Full();
            Assert.AreEqual(0, Diff(inc, full), $"step {step} kind {kind}: incremental differs from a full rebuild");
        }
    }
}
