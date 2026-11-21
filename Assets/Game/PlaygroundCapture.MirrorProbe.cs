// Assets/Game/PlaygroundCapture.MirrorProbe.cs
//
// TASK 197 (DIAGNOSTIC, capture only, `-mirrorprobe 1`): which tier-0 box chunks the
// mirror does NOT hold at a shot, and why. Task 194 handed REFUSED chunks to the
// shells; 194b / 195 still saw chunk-shaped pits / a missing wall with 0 refused.
// For every chunk of the mirror box:
//   resident on the CPU, not uniform air, but its slot is not its own write
//     (owner != c, or the slot was last cleared / refused)      -> MISSING,
//     split by why: refused / dirty (upload queued) / parked / LOST (clean, nothing queued);
//   not resident, with the chunk below resident and solid      -> ABSENT over solid,
//     split by streaming stage (queued / Loading / Unloaded / untracked).
// Plus the column's residency / refused bits (what the hand-over sees). One summary
// line and up to 24 on-screen chunk lines (nearest first); MirrorProbeAgain re-reads
// the same chunks a few seconds later. Nothing here changes what is drawn.
using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Rendering;

public partial class PlaygroundCapture
{
    private static readonly List<int3> _mirrorProbeLast = new List<int3>();

    private static string MirrorWhy(TerrainClipmap clip, ChunkStore store, int3 c, out bool missing)
    {
        clip.DebugSlotState(c, out bool inBox, out bool own, out bool held, out bool dirty, out bool parked, out bool refused, out long ep);
        var ch = store.GetChunk(c);
        var sm = Phase4Bootstrapper.Streamer;
        string stage = sm != null ? sm.DebugChunkStage(c) : "?";
        missing = false;
        if (ch == null) return $"ABSENT stage {stage}";
        bool air = ch.isUniform && ch.uniformMaterial == 0;
        bool ok = own && held;
        missing = !ok && !air;
        string why = ok ? (dirty ? "held, update queued" : "held")
                   : refused ? "MISSING refused" : dirty ? "MISSING dirty (upload queued)" : parked ? "MISSING parked"
                   : "MISSING LOST (clean, nothing queued)";
        return $"{why} | stage {stage} | {(air ? "air" : ch.isUniform ? $"uniform {ch.uniformMaterial}" : "dense")} | inBox {inBox} owner {(own ? "self" : "other")} held {held} lastUpload {(ep < 0 ? "never" : (clip.UploadEpoch - ep).ToString() + " passes ago")}";
    }

    private static List<string> MirrorProbeLines(Camera cam, ShellStreamer sh)
    {
        var lines = new List<string>();
        var clip = Phase4Bootstrapper.Clipmap;
        var store = Phase4Bootstrapper.Store;
        var smr = Phase4Bootstrapper.Streamer;
        _mirrorProbeLast.Clear();
        if (clip == null || store == null) return lines;
        int3 o = clip.WindowOrigin, d = clip.WindowDimsChunks;
        int lo = 0, hi = 0;
        smr?.LayerRange(out lo, out hi);
        int nRefused = 0, nDirty = 0, nParked = 0, nLost = 0, nAbsentOverSolid = 0, nAbsentQueued = 0, nAbsentLoading = 0;
        var cand = new List<int3>();
        for (int z = 0; z < d.z; z++)
        for (int y = 0; y < d.y; y++)
        for (int x = 0; x < d.x; x++)
        {
            int3 c = o + new int3(x, y, z);
            var ch = store.GetChunk(c);
            if (ch == null)
            {
                var chB = store.GetChunk(c - new int3(0, 1, 0));
                if (chB == null || (chB.isUniform && chB.uniformMaterial == 0)) continue;
                nAbsentOverSolid++;
                string st = smr != null ? smr.DebugChunkStage(c) : "?";
                if (st == "queued") nAbsentQueued++; else if (st == "Loading") nAbsentLoading++;
                cand.Add(c);
                continue;
            }
            clip.DebugSlotState(c, out _, out bool own, out bool held, out bool dirty, out bool parked, out bool refused, out _);
            if ((own && held) || (ch.isUniform && ch.uniformMaterial == 0)) continue;
            if (refused) nRefused++; else if (dirty) nDirty++; else if (parked) nParked++; else nLost++;
            cand.Add(c);
        }
        // Task 216: refused now split into in-view / 5x5 chunks vs the rest (the rest includes view-evicted ones).
        Vector3 cp = cam.transform.position;
        int3 camChunk = new int3((int)math.floor(cp.x * 10f) >> 7, 0, (int)math.floor(cp.z * 10f) >> 7);
        clip.GpuRefusedByView(camChunk, out int refReq, out int refOther);
        lines.Add($"  [mirror gpuview] GPU view evict {(TerrainClipmap.ViewEvict ? "ON" : "OFF")}{(clip.ViewTest == null ? " (no view)" : "")}: " +
                  $"refused now in view/5x5 {refReq}, other {refOther} | victims total {clip.GpuViewEvictedTotal:N0} (bodies {clip.GpuViewEvictedBodiesTotal:N0}), requesters mapped by it {clip.GpuViewEvictSavedTotal:N0} | " +
                  $"GPU pool in use {clip.GpuPoolInUse:N0} of {clip.GpuPoolCapacity:N0}, peak {clip.GpuPoolPeakUsed:N0}");
        lines.Add($"  [mirror] box {o}..{o + d - 1} layers {lo}..{hi} | MISSING (resident, not air, slot not its own): refused {nRefused}, dirty {nDirty}, parked {nParked}, LOST {nLost} | " +
                  $"ABSENT over solid {nAbsentOverSolid} (queued {nAbsentQueued}, loading {nAbsentLoading}) | dirty set {clip.DirtyCount:N0}, pending loads {smr?.PendingLoads ?? -1}, in flight {smr?.InFlightLoads ?? -1}, refused now {clip.GpuPoolRefusedNow} (refusals total {clip.GpuPoolRefusedTotal:N0}, retries total {clip.GpuPoolRetriesTotal:N0}, re-refused {clip.GpuPoolReRefusedTotal:N0}, retry pending {clip.GpuPoolRetryPending}) | " +
                  $"evicted total {smr?.ChunksEvictedTotal ?? -1:N0} (LRU valve {smr?.LruEvictionsTotal ?? -1:N0}, in view {smr?.ValveInViewEvictionsTotal ?? -1:N0}, in view near {smr?.ValveInViewNearEvictionsTotal ?? -1:N0}) | hand-over columns {FarField.Active?.HandOverColumns ?? -1}");
        Vector3 e = cam.transform.position;
        float H(int3 c) => math.length(new float2(c.x * 12.8f + 6.4f - e.x, c.z * 12.8f + 6.4f - e.z));
        cand.Sort((a, b) => H(a).CompareTo(H(b)));
        int n = 0;
        foreach (int3 c in cand)
        {
            Vector3 mid = new Vector3(c.x * 12.8f + 6.4f, c.y * 12.8f + 6.4f, c.z * 12.8f + 6.4f);
            Vector3 vp = cam.WorldToViewportPoint(mid);
            bool onScreen = vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
            if (!onScreen) continue;
            if (n++ >= 24) break;
            _mirrorProbeLast.Add(c);
            FarField.ColumnState(store, c.x, c.z, lo, hi, clip, out bool colRes, out bool colRef,
                                 sh != null ? (System.Func<int3, bool>)(q => sh.PublishedHolds(sh.FirstShell, q >> sh.FirstShell)) : null);   // the hand-over's bits
            lines.Add($"  [mirror] chunk {c} dist {H(c):F0} m px ({vp.x * 1920f:F0}, {(1f - vp.y) * 1080f:F0}) | {MirrorWhy(clip, store, c, out _)} | column resident {colRes} hand-over {colRef}" +
                      $"{(sh != null ? $" | shell 1 {(sh.Wanted(1, c >> 1) ? "stores" : "HOLE")}" : "")}");
        }
        lines.AddRange(GpuProbeLines(cam, lo, hi));
        lines.AddRange(GenProbeLines(cam, lo, hi));
        return lines;
    }

    private static uint[] _gpuScratch;

    /// Task 197: the GPU's own tier-0 clipmap for every band chunk of the box, against what the CPU
    /// believes it wrote (TerrainClipmap.DebugGpuChunkDiff), and the packed air mip. A chunk drawn as a
    /// pit while the CPU says "held" shows here as GPU air where the CPU has solid bricks.
    private static List<string> GpuProbeLines(Camera cam, int lo, int hi)
    {
        var lines = new List<string>();
        var clip = Phase4Bootstrapper.Clipmap;
        var store = Phase4Bootstrapper.Store;
        if (clip == null || store == null || clip.ClipmapBuffer == null) return lines;
        _gpuScratch ??= new uint[EngineConfig.BRICKS_PER_CHUNK];
        int3 o = clip.WindowOrigin, d = clip.WindowDimsChunks;
        int nDiff = 0, nAir = 0, nAirHeld = 0;
        var bad = new List<(int3 c, int diff, int air, bool held)>();
        for (int z = 0; z < d.z; z++)
        for (int x = 0; x < d.x; x++)
        for (int y = math.max(lo, o.y); y <= math.min(hi, o.y + d.y - 1); y++)
        {
            int3 c = o + new int3(x, y - o.y, z);
            int diff = clip.DebugGpuChunkDiff(c, _gpuScratch, out int air);
            if (diff == 0) continue;
            nDiff++;
            clip.DebugSlotState(c, out _, out bool own, out bool held, out _, out _, out _, out _);
            if (air > 0) { nAir++; if (own && held) nAirHeld++; }
            bad.Add((c, diff, air, own && held));
        }
        lines.Add($"  [gpu] band chunks whose GPU clipmap differs from the CPU mirror: {nDiff} (GPU air over CPU solid: {nAir}, of them CPU says held: {nAirHeld}) | air-mip words differing: {clip.DebugAirMipDiffWords()}");
        Vector3 e = cam.transform.position;
        float H(int3 c) => math.length(new float2(c.x * 12.8f + 6.4f - e.x, c.z * 12.8f + 6.4f - e.z));
        bad.Sort((a, b) => H(a.c).CompareTo(H(b.c)));
        int n = 0;
        foreach (var b in bad)
        {
            Vector3 vp = cam.WorldToViewportPoint(new Vector3(b.c.x * 12.8f + 6.4f, b.c.y * 12.8f + 6.4f, b.c.z * 12.8f + 6.4f));
            if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) continue;
            if (n++ >= 16) break;
            lines.Add($"  [gpu] chunk {b.c} dist {H(b.c):F0} m px ({vp.x * 1920f:F0}, {(1f - vp.y) * 1080f:F0}) | entries differing {b.diff}, GPU air over CPU solid {b.air} | {MirrorWhy(clip, store, b.c, out _)}");
        }
        return lines;
    }

    private sealed class NoneResident : IVoxelResidency { public bool IsResident(int3 c) => false; }
    private static VoxelEngine.Streaming.FarQuery _genQuery;

    /// Task 197: every RESIDENT band chunk of the box that the store holds as UNIFORM AIR, against the
    /// generator's own voxels (FarQuery with nothing resident and no deltas = generation only): 8 x 8
    /// columns x 16 heights per chunk. A chunk that should hold ground but is air in the store is drawn as
    /// a pit by a correct mirror. Read-only.
    private static List<string> GenProbeLines(Camera cam, int lo, int hi)
    {
        var lines = new List<string>();
        var clip = Phase4Bootstrapper.Clipmap;
        var store = Phase4Bootstrapper.Store;
        var smr = Phase4Bootstrapper.Streamer;
        var src = smr?.TerrainGenSource;
        if (clip == null || store == null || src == null) return lines;
        _genQuery ??= new VoxelEngine.Streaming.FarQuery(store, new NoneResident(), src,
                          VoxelEngine.Streaming.EditOpLog.InMemory((uint)ArgInt("-worldseed", 42)), null, (uint)ArgInt("-worldseed", 42));
        int3 o = clip.WindowOrigin, d = clip.WindowDimsChunks;
        var airChunks = new List<int3>();
        for (int z = 0; z < d.z; z++)
        for (int x = 0; x < d.x; x++)
        for (int y = math.max(lo, o.y); y <= math.min(hi, o.y + d.y - 1); y++)
        {
            int3 c = o + new int3(x, y - o.y, z);
            var ch = store.GetChunk(c);
            if (ch != null && ch.isUniform && ch.uniformMaterial == 0) airChunks.Add(c);
        }
        const int PER = 8 * 8 * 16;
        var pts = new Unity.Collections.NativeArray<int3>(airChunks.Count * PER, Unity.Collections.Allocator.TempJob);
        var res = new Unity.Collections.NativeArray<byte>(airChunks.Count * PER, Unity.Collections.Allocator.TempJob);
        var bad = new List<(int3 c, int solid)>();
        try
        {
            int k = 0;
            foreach (int3 c in airChunks)
                for (int iy = 0; iy < 16; iy++) for (int iz = 0; iz < 8; iz++) for (int ix = 0; ix < 8; ix++)
                    pts[k++] = c * 128 + new int3(ix * 16 + 8, iy * 8 + 4, iz * 16 + 8);
            _genQuery.GetVoxels(pts, res);
            for (int i = 0; i < airChunks.Count; i++)
            {
                int solid = 0;
                for (int j = 0; j < PER; j++) if (res[i * PER + j] != 0) solid++;
                if (solid > 0) bad.Add((airChunks[i], solid));
            }
        }
        finally { pts.Dispose(); res.Dispose(); }
        lines.Add($"  [gen] resident band chunks stored as uniform AIR: {airChunks.Count}; of them the GENERATOR has solid voxels in {bad.Count} (sampled {PER} points each)");
        Vector3 e = cam.transform.position;
        float H(int3 c) => math.length(new float2(c.x * 12.8f + 6.4f - e.x, c.z * 12.8f + 6.4f - e.z));
        bad.Sort((a, b) => H(a.c).CompareTo(H(b.c)));
        int n = 0;
        foreach (var b in bad)
        {
            if (n++ >= 16) break;
            Vector3 vp = cam.WorldToViewportPoint(new Vector3(b.c.x * 12.8f + 6.4f, b.c.y * 12.8f + 6.4f, b.c.z * 12.8f + 6.4f));
            smr.DebugChunkRecord(b.c, out int gen, out long dl);
            lines.Add($"  [gen] chunk {b.c} dist {H(b.c):F0} m px ({vp.x * 1920f:F0}, {(1f - vp.y) * 1080f:F0}){(vp.z > 0f ? "" : " BEHIND")} | store AIR, generator solid at {b.solid} of {PER} points | " +
                      $"stage {smr.DebugChunkStage(b.c)} gen {gen} delta {dl} B | {MirrorWhy(clip, store, b.c, out _)}");
        }
        return lines;
    }

    /// The same chunks again (MirrorProbeLines' on-screen list), `dt` seconds later.
    private static List<string> MirrorProbeAgain(float dt)
    {
        var lines = new List<string>();
        var clip = Phase4Bootstrapper.Clipmap;
        var store = Phase4Bootstrapper.Store;
        if (clip == null || store == null) return lines;
        int still = 0;
        foreach (int3 c in _mirrorProbeLast)
        {
            string w = MirrorWhy(clip, store, c, out bool miss);
            if (miss || store.GetChunk(c) == null) still++;
            lines.Add($"  [mirror +{dt:F0}s] chunk {c} | {w}");
        }
        var smr = Phase4Bootstrapper.Streamer;
        lines.Insert(0, $"  [mirror +{dt:F0}s] of {_mirrorProbeLast.Count} listed: still missing/absent {still} | dirty set {clip.DirtyCount:N0}, refused now {clip.GpuPoolRefusedNow} | " +
                        $"evicted total {smr?.ChunksEvictedTotal ?? -1:N0} (LRU valve {smr?.LruEvictionsTotal ?? -1:N0}, in view {smr?.ValveInViewEvictionsTotal ?? -1:N0}, in view near {smr?.ValveInViewNearEvictionsTotal ?? -1:N0})");
        return lines;
    }
}
