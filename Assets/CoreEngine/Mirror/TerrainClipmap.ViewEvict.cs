// Assets/CoreEngine/Mirror/TerrainClipmap.ViewEvict.cs
//
// TASK 216 (owner 2026-11-15, Q-AI (a): new eviction design approved): the GPU
// body pool makes room for what the player can see.
//
// Since task 208 the CPU valve drops far OUT-OF-VIEW chunks, mostly outside the
// mirror box, so they hold no GPU bodies: the GPU pool (254,400 bodies, 201a)
// stays full and IN-VIEW chunks entering the box are refused (857 -> 1,190
// refusals; 31-138 waiting at each shot, drawn by 197's shell hand-over). Now,
// when a REQUESTER is refused -- a chunk in the view frustum, or in the camera's
// 5x5 (208's valve exemption) -- the pool drops the bodies of mirror-box chunks
// that are OUT OF VIEW and FARTHER (Chebyshev XZ) than it, farthest first, until
// the requester fits, and the requester is mapped. A victim:
//   - is held by its box slot (owner in the box), has GPU bodies, is not dirty,
//   - is outside the frustum (same 5-plane test as 208's valve: the frustum is
//     the streamer's, handed over by SetView), outside the camera's 5x5,
//   - is not touched by a live fluid tile (SetFluidWanted: the GPU fluid CA reads
//     in-box terrain from the mirror),
//   - is farther than max(requester, 5x5 edge).
// A victim is written as AIR on the GPU (entries + air mips, the refusal's write)
// and joins the REFUSED set with its body count, so (a) FarField's hand-over
// draws it from shell 1 like any refused chunk (IsGpuRefused), and (b) 207's
// retry puts it back once its bodies fit. Nothing is evicted unless the victims
// found cover the WHOLE shortfall (no clear wasted on a requester that is
// refused anyway). The retry now ranks requesters (in view / 5x5) before the
// rest, so a victim never takes back free bodies an in-view chunk is waiting
// for, and an in-view refused chunk that does not fit may evict too (that is how
// it comes back when the camera turns: the retry also runs when the view moved).
//
// EFFICIENT BY CONSTRUCTION: one scan of the box's chunk slots per upload pass,
// and only in a pass where a requester is short (lazy); one sort of a reused
// scratch list of packed longs (distance << 32 | slot, <= 4,096 entries); the
// pass consumes it from the far end. No per-chunk storage, no new GPU memory,
// no pool made bigger, no layout or API change. At most ViewEvictMaxPerPass
// victims per pass (each clear costs what an upload of air costs and is charged
// to the pass's byte count). Main thread only (UploadDirty's caller).
//
// DEFAULT OFF until its launch pair runs (HANDOFF 216). `-gpuviewevict 1` turns it on.
// `-gpuviewevict 0` (ViewEvict = false) or no view (SetView(null)) = today's
// path byte for byte: TryEvictForView returns false at once and the retry order
// is 207's.
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using VoxelEngine.Memory;

public partial class TerrainClipmap
{
    /// Task 216: `-gpuviewevict 0|1` (launch-wide, Phase4Bootstrapper). Default OFF for now (its launch pair
    /// is still to run, HANDOFF 216); the task's target default is ON.
    public static bool ViewEvict = false;
    /// Victims never come from the camera's 5x5 (Chebyshev XZ <= 2, 208's valve exemption);
    /// requesters inside it qualify even when out of view (shadows, turning round).
    public const int ViewEvictKeepRadius = 2;
    /// At most this many victims per upload pass (= MAX_CLIPMAP_CHUNKS_PER_FRAME's chunk cap).
    public const int ViewEvictMaxPerPass = EngineConfig.MAX_CLIPMAP_CHUNKS_PER_FRAME;

    /// The view test (the streamer's frustum, StreamManager.ChunkInView); null = no view.
    public Func<int3, bool> ViewTest { get; private set; }
    private int _viewVersion;

    /// Victims written as air to make room (all time), the GPU bodies that freed, and the
    /// requesters mapped thanks to them (in the upload pass or re-queued by the retry).
    public long GpuViewEvictedTotal { get; private set; }
    public long GpuViewEvictedBodiesTotal { get; private set; }
    public long GpuViewEvictSavedTotal { get; private set; }

    private readonly List<long> _victims = new List<long>();   // (distance << 32) | slot, ascending
    private long _victimEpoch = -1;
    private int _victimEnd;          // entries [0, _victimEnd) not yet consumed; farthest at the end
    private int _victimsThisPass;
    private int3 _victimCam;
    private int _retryViewSeen = -1;

    /// Called before every UploadDirty by the streamer: its frustum test (null = none) and a
    /// version that changes whenever the frustum did (re-runs the retry while turning).
    public void SetView(Func<int3, bool> inView, int version)
    {
        ViewTest = inView;
        _viewVersion = inView == null ? -1 : version;
    }

    private bool ViewEvictActive => ViewEvict && ViewTest != null && _gpuOfCpu != null;

    /// In view, or in the camera's 5x5: a chunk whose refusal may evict.
    private bool IsViewRequester(int3 c, int3 cam)
        => ChebyshevXZ(c, cam) <= ViewEvictKeepRadius || ViewTest(c);

    /// Refused chunks now that are requesters (in view / 5x5) vs the rest (probe use: the
    /// "refused now" number counts out-of-view victims too).
    public void GpuRefusedByView(int3 cam, out int requesters, out int others)
    {
        requesters = 0; others = 0;
        foreach (int3 c in _gpuRefused)
            if (ViewTest != null && IsViewRequester(c, cam)) requesters++; else others++;
    }

    /// One scan of the box's slots, once per pass at most: candidate victims sorted by distance.
    private void EnsureVictims(int3 cam)
    {
        if (_victimEpoch == _uploadEpoch && _victimCam.Equals(cam)) return;
        _victimEpoch = _uploadEpoch; _victimCam = cam;
        _victimsThisPass = 0;
        _victims.Clear();
        for (int s = 0; s < _chunkSlotCount; s++)
        {
            if (!_slotHeld[s]) continue;
            List<int> held = _held[s];
            if (held == null || held.Count == 0) continue;
            int3 c = _slotOwner[s];
            int d = ChebyshevXZ(c, cam);
            if (d <= ViewEvictKeepRadius || !InMirrorBox(c)) continue;
            if (_dirtyChunks.Contains(c) || _fdWanted.Contains(c) || ViewTest(c)) continue;
            _victims.Add(((long)d << 32) | (uint)s);
        }
        _victims.Sort();
        _victimEnd = _victims.Count;
    }

    /// Requester `c` is short of `shortfall` GPU bodies: evict out-of-view victims farther than
    /// it, farthest first, if (and only if) they cover the whole shortfall. True = evicted (the
    /// caller maps the requester now). False = nothing done (off, no view, not a requester,
    /// not enough victims, or the pass's victim cap reached).
    private bool TryEvictForView(int3 c, int3 cam, int shortfall, ref UploadStats stats)
    {
        if (!ViewEvictActive || shortfall <= 0 || !IsViewRequester(c, cam)) return false;
        EnsureVictims(cam);
        int minDist = math.max(ChebyshevXZ(c, cam), ViewEvictKeepRadius);

        // Cover check first (victims may have changed since the scan: re-test them).
        int sum = 0, k = _victimEnd, n = 0;
        while (k > 0 && sum < shortfall)
        {
            long v = _victims[k - 1];
            if (v < 0) { k--; continue; }                     // found invalid by an earlier check
            if ((int)(v >> 32) <= minDist) break;
            if (_victimsThisPass + n >= ViewEvictMaxPerPass) break;
            int s = (int)(uint)v;
            k--;
            if (!VictimStillValid(s)) { _victims[k] = -1; continue; }   // skipped below too
            sum += _held[s].Count;
            n++;
        }
        if (sum < shortfall) return false;

        while (_victimEnd > k)
        {
            long v = _victims[--_victimEnd];
            if (v < 0) continue;
            EvictSlotForView((int)(uint)v, ref stats);
        }
        return true;
    }

    private bool VictimStillValid(int s)
    {
        if (!_slotHeld[s] || _held[s] == null || _held[s].Count == 0) return false;
        int3 c = _slotOwner[s];
        return InMirrorBox(c) && !_dirtyChunks.Contains(c);
    }

    /// Writes slot `s`'s chunk as air (the refusal's write: entries, air mips, height,
    /// sealed mask), frees its bodies and puts it in the refused set with its body count.
    private void EvictSlotForView(int s, ref UploadStats stats)
    {
        int3 c = _slotOwner[s];
        int need = _held[s].Count;
        int free0 = _gpuFreeTop;
        ReleaseHolder(s);
        if (SealActive || _sealedOfSlot != null) SetSlotSealed(s, 0);

        int3 baseBrick = c * EngineConfig.CHUNK_EDGE_BRICKS;
        Array.Clear(_chunkStaging, 0, _chunkStaging.Length);
        for (int z = 0; z < 16; z++)
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
            _clipmapLocal[BrickLinearIndex(baseBrick + new int3(x, y, z))] = 0u;
        WriteClipmapChunk(s * EngineConfig.BRICKS_PER_CHUNK, ref stats);
        MarkHeightDirty(c);
        _slotHeld[s] = false;
        int3 regionMax = baseBrick + new int3(15, 15, 15);
        AirMip.RebuildRegion(_clipmapLocal, _mips, baseBrick, regionMax);
        AirMip.PackRegion(_packed, _mips, baseBrick, regionMax);
        if (RecordUploadedChunks && UploadedChunks.Count < UploadedChunksCap) UploadedChunks.Add(c);

        _gpuRefused.Add(c);
        _gpuRefusedNeed[c] = need;
        _victimsThisPass++;
        stats.viewEvicted++;
        stats.viewEvictBytes += EngineConfig.CLIPMAP_BYTES_PER_CHUNK;
        GpuViewEvictedTotal++;
        GpuViewEvictedBodiesTotal += _gpuFreeTop - free0;
    }
}
