// Assets/Game/PlaygroundCapture.GpuPoolProbe.cs
//
// TASK 201a (DIAGNOSTIC, capture only, `-gpupoolprobe 1`; after the fix the sealed bricks are
// counted apart: they hold no body): what fills the near GPU
// body pool at a pose. Walks every resident chunk inside the tier-0 mirror box and
// classifies each DENSE brick (a 512 B body) the pool maps -- and, separately, the
// dense bricks of the chunks it REFUSED (the unmet demand):
//   source  tree (tree wood / leaves / cactus / world tree), ore, fluid, terrain (rest;
//           towns are terrain-coloured materials: their share is the -towns 0 arm's delta)
//   kind    one-value (512 equal bytes), surface (holds air), fluid-mixed (fluid, no air),
//           solid-mixed (every voxel opaque, 2+ materials)
//   hidden  every voxel opaque AND the 1-voxel shell around the brick opaque: no ray from
//           air or fluid can reach any of its voxels (and nothing within one voxel of air
//           reads it). "inner" = the shell lies inside the chunk (brick not on its border).
//   where   horizontal Chebyshev ring (chunks) and vertical layer relative to the camera chunk
//   dupes   byte-identical bodies (64-bit hash of the 512 bytes)
// Nothing here changes what is drawn; ~1-3 s of main thread once per pose.
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;

public partial class PlaygroundCapture
{
    // Engine-id classes (Materials ids; tg2 solids are 128 + GenMat).
    private static bool PpFluid(byte m) => m == Materials.Water || m == Materials.Lava || m == Materials.Honey
                                          || (m >= Materials.Oil && m <= Materials.Steam);
    /// Opaque = a voxel every tier-0 ray stops at: not air, no fluid / steam / glass / river lip.
    private static bool PpOpaque(byte m) => m != 0 && !PpFluid(m) && m != Materials.Glass && m != (byte)(Materials.TG2_BASE + TerrainGen.GenMat.RiverLip);
    private static bool PpTree(byte m)
    {
        if (m < Materials.TG2_BASE) return false;
        byte g = (byte)(m - Materials.TG2_BASE);
        return TerrainGen.GenMat.IsTreeTrunk(g) || TerrainGen.GenMat.IsTreeLeaf(g) || g == TerrainGen.GenMat.Bark || g == TerrainGen.GenMat.Leaves;
    }
    private static bool PpOre(byte m) => m >= Materials.TG2_BASE + 65 && m <= Materials.TG2_BASE + 69;

    private static List<string> GpuPoolProbeLines(Camera cam)
    {
        var lines = new List<string>();
        var clip = Phase4Bootstrapper.Clipmap;
        var store = Phase4Bootstrapper.Store;
        var pool = Phase4Bootstrapper.Pool;
        if (clip == null || !clip.GpuPoolEnabled || store == null || pool == null) return lines;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        NativeArray<byte> raw = pool.RawData;
        NativeArray<ulong> raw64 = raw.Reinterpret<ulong>(1);
        int3 o = clip.WindowOrigin, d = clip.WindowDimsChunks;
        Vector3 e = cam.transform.position;
        int3 camChunk = new int3((int)math.floor(e.x / 12.8f), (int)math.floor(e.y / 12.8f), (int)math.floor(e.z / 12.8f));

        // [src 0..3][kind 0..3] mapped counts; hidden counts per src; refused per src.
        string[] srcN = { "terrain", "tree", "ore", "fluid" };
        string[] kindN = { "one-value", "surface", "fluid-mixed", "solid-mixed" };
        var cnt = new long[4, 4];
        var hidInner = new long[4]; var hidAll = new long[4];
        var refSrc = new long[4]; long refDense = 0, refHidInner = 0, refHidAll = 0;
        var ring = new long[3]; var ringHid = new long[3];          // 0-2, 3-5, 6+
        var layer = new SortedDictionary<int, long[]>();             // dy -> {mapped, hiddenAll}
        var hashes = new Dictionary<ulong, int>(1 << 18);
        var dupByKind = new long[4];
        int chunksRes = 0, chunksUniform = 0, chunksDense = 0, chunksRefused = 0;
        long mapped = 0, unmappedInHeld = 0;
        long sealedHeld = 0, sealedNotProbeHidden = 0;

        for (int cz = o.z; cz < o.z + d.z; cz++)
        for (int cy = o.y; cy < o.y + d.y; cy++)
        for (int cx = o.x; cx < o.x + d.x; cx++)
        {
            int3 c = new int3(cx, cy, cz);
            Chunk ch = store.GetChunk(c);
            if (ch == null) continue;
            chunksRes++;
            if (ch.isUniform || ch.bricks == null) { chunksUniform++; continue; }
            bool refused = clip.IsGpuRefused(c);
            if (refused) chunksRefused++; else chunksDense++;
            int dyKey = cy - camChunk.y;
            int rg = math.max(math.abs(cx - camChunk.x), math.abs(cz - camChunk.z));
            int ri = rg <= 2 ? 0 : rg <= 5 ? 1 : 2;
            for (int bz = 0; bz < 16; bz++)
            for (int by = 0; by < 16; by++)
            for (int bx = 0; bx < 16; bx++)
            {
                uint h = ch.bricks[CoordMath.LocalBrickIndex(new int3(bx, by, bz))].data;
                if ((h & 0x80000000u) == 0) continue;
                int cpu = (int)(h & 0x3FFFFFFFu);
                int b0 = cpu * 512;
                // classify the body
                byte first = raw[b0];
                bool oneVal = true, hasAir = false, hasFluid = false, allOpaque = true, tree = false, ore = false;
                for (int i = 0; i < 512; i++)
                {
                    byte m = raw[b0 + i];
                    if (m != first) oneVal = false;
                    if (m == 0) hasAir = true;
                    else if (PpFluid(m)) hasFluid = true;
                    if (!PpOpaque(m)) allOpaque = false;
                    if (PpTree(m)) tree = true; else if (PpOre(m)) ore = true;
                }
                int src = tree ? 1 : ore ? 2 : hasFluid ? 3 : 0;
                int kind = oneVal ? 0 : hasAir ? 1 : hasFluid ? 2 : 3;
                bool hidA = false, hidI = false;
                if (allOpaque)
                {
                    hidA = ShellOpaque(store, ch, c, bx, by, bz, raw);
                    hidI = hidA && bx > 0 && by > 0 && bz > 0 && bx < 15 && by < 15 && bz < 15;
                }
                if (refused)
                {
                    refDense++; refSrc[src]++;
                    if (hidI) refHidInner++;
                    if (hidA) refHidAll++;
                    continue;
                }
                if (clip.IsSealedBrick(c, new int3(bx, by, bz))) { sealedHeld++; if (!hidI) sealedNotProbeHidden++; continue; }   // task 201a: staged uniform, no body
                if (clip.GpuSlotOf(cpu) < 0) { unmappedInHeld++; continue; }
                mapped++;
                cnt[src, kind]++;
                if (hidI) hidInner[src]++;
                if (hidA) hidAll[src]++;
                ring[ri]++; if (hidA) ringHid[ri]++;
                if (!layer.TryGetValue(dyKey, out long[] lv)) layer[dyKey] = lv = new long[2];
                lv[0]++; if (hidA) lv[1]++;
                ulong hs = 1469598103934665603UL;
                int w0 = cpu * 64;
                for (int w = 0; w < 64; w++) { hs ^= raw64[w0 + w]; hs *= 1099511628211UL; hs ^= hs >> 29; }
                if (hashes.TryGetValue(hs, out int n)) { hashes[hs] = n + 1; dupByKind[kind]++; }
                else hashes[hs] = 1;
            }
        }

        long hidInnerT = 0, hidAllT = 0; for (int s = 0; s < 4; s++) { hidInnerT += hidInner[s]; hidAllT += hidAll[s]; }
        long dupT = mapped - hashes.Count;
        lines.Add($"  [gpupool] in use {clip.GpuPoolInUse:N0} of {clip.GpuPoolCapacity:N0} (free {clip.GpuPoolCapacity - clip.GpuPoolInUse:N0}), peak {clip.GpuPoolPeakUsed:N0}, refused total {clip.GpuPoolRefusedTotal:N0} | " +
                  $"box {o}..{o + d - 1} cam chunk {camChunk} | resident chunks {chunksRes:N0}: uniform {chunksUniform:N0}, dense held {chunksDense:N0}, refused now {chunksRefused:N0} | " +
                  $"box bodies mapped {mapped:N0} (+ fluid dir / other {clip.GpuPoolInUse - mapped:N0}), dense not mapped in held chunks {unmappedInHeld:N0} | " +
                  $"refused chunks' dense bricks {refDense:N0} -> demand {mapped + refDense:N0} ({(mapped + refDense) * 512.0 / (1 << 20):F0} MB)");
        for (int s = 0; s < 4; s++)
        {
            long tot = 0; for (int k = 0; k < 4; k++) tot += cnt[s, k];
            lines.Add($"  [gpupool] src {srcN[s],-8} {tot,9:N0} ({100.0 * tot / math.max(1, mapped),5:F1}%) | " +
                      $"{kindN[0]} {cnt[s, 0]:N0}, {kindN[1]} {cnt[s, 1]:N0}, {kindN[2]} {cnt[s, 2]:N0}, {kindN[3]} {cnt[s, 3]:N0} | " +
                      $"hidden inner {hidInner[s]:N0}, hidden any {hidAll[s]:N0} | refused {refSrc[s]:N0}");
        }
        lines.Add($"  [gpupool] hidden (all-opaque brick in an opaque 1-voxel shell): inner {hidInnerT:N0} ({100.0 * hidInnerT / math.max(1, mapped):F1}%), " +
                  $"incl. chunk-border bricks {hidAllT:N0} ({100.0 * hidAllT / math.max(1, mapped):F1}%) | refused chunks: inner {refHidInner:N0}, any {refHidAll:N0} | " +
                  $"demand minus inner-hidden {mapped + refDense - hidInnerT - refHidInner:N0}, minus any-hidden {mapped + refDense - hidAllT - refHidAll:N0}");
        lines.Add($"  [gpupool] duplicates: distinct bodies {hashes.Count:N0} of {mapped:N0} (dupes {dupT:N0} = {100.0 * dupT / math.max(1, mapped):F1}%) | by kind " +
                  $"{kindN[0]} {dupByKind[0]:N0}, {kindN[1]} {dupByKind[1]:N0}, {kindN[2]} {dupByKind[2]:N0}, {kindN[3]} {dupByKind[3]:N0}");
        lines.Add($"  [gpupool] ring (chunks from cam, xz): 0-2 {ring[0]:N0} (hidden {ringHid[0]:N0}), 3-5 {ring[1]:N0} (hidden {ringHid[1]:N0}), 6+ {ring[2]:N0} (hidden {ringHid[2]:N0})");
        var sbL = new System.Text.StringBuilder("  [gpupool] layer dy (chunks from cam): ");
        foreach (var kv in layer) sbL.Append($"{kv.Key:+0;-0;0}: {kv.Value[0]:N0}/{kv.Value[1]:N0}h  ");
        lines.Add(sbL.ToString());
        lines.Add($"  [gpupool] sealed (task 201a, staged uniform, no body): held chunks {sealedHeld:N0} (not probe-inner-hidden {sealedNotProbeHidden:N0}), clipmap count {clip.SealedBricksNow:N0}, " +
                  $"staged total {clip.SealedBricksStagedTotal:N0} | seal {(TerrainClipmap.SealHiddenBricks ? "ON" : "OFF (-gpuseal 0)")}");
        lines.Add($"  [gpupool] probe {sw.Elapsed.TotalMilliseconds:F0} ms");
        return lines;
    }

    /// Material at chunk-local voxel (lx, ly, lz) in -1..128 (crosses into neighbours); 0 if not resident.
    private static byte PpVoxel(ChunkStore store, Chunk ch, int3 c, int lx, int ly, int lz, NativeArray<byte> raw)
    {
        if (lx < 0 || ly < 0 || lz < 0 || lx > 127 || ly > 127 || lz > 127)
        {
            int3 dc = new int3(lx >> 7, ly >> 7, lz >> 7);
            ch = store.GetChunk(c + dc);
            if (ch == null) return 0;
            lx &= 127; ly &= 127; lz &= 127;
        }
        if (ch.isUniform || ch.bricks == null) return ch.uniformMaterial;
        uint h = ch.bricks[CoordMath.LocalBrickIndex(new int3(lx >> 3, ly >> 3, lz >> 3))].data;
        if ((h & 0x80000000u) == 0) return (byte)(h & 0xFF);
        return raw[(int)(h & 0x3FFFFFFFu) * 512 + CoordMath.LocalVoxelIndex(new int3(lx & 7, ly & 7, lz & 7))];
    }

    /// True if every voxel of the 10^3 - 8^3 shell around brick (bx, by, bz) is opaque.
    private static bool ShellOpaque(ChunkStore store, Chunk ch, int3 c, int bx, int by, int bz, NativeArray<byte> raw)
    {
        int x0 = bx * 8 - 1, y0 = by * 8 - 1, z0 = bz * 8 - 1;
        for (int z = 0; z < 10; z++)
        for (int y = 0; y < 10; y++)
        for (int x = 0; x < 10; x++)
        {
            if (x > 0 && x < 9 && y > 0 && y < 9 && z > 0 && z < 9) continue;
            if (!PpOpaque(PpVoxel(store, ch, c, x0 + x, y0 + y, z0 + z, raw))) return false;
        }
        return true;
    }
}
