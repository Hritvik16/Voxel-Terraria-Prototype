// ==========================================
// Assets/ContentModules/Config/EngineConfig.cs
//
// PHASE 4 REVISION. §0.1 invariant 8: "Every hard limit is defined once, in
// EngineConfig, and read everywhere."
//
// ---------------------------------------------------------------------------
// WINDOW_CHUNKS_Y: 16, NOT 2. THIS REVERSES AN EARLIER DECISION ON EVIDENCE.
// ---------------------------------------------------------------------------
// The first Phase 4 draft dropped this to 2, reasoning that
// WorldGenConstants.MAX_TERRAIN_HEIGHT is 120 and MIN_TERRAIN_HEIGHT is 1, so
// every generated voxel lives inside the cy=0 chunk layer and 14 of 16
// vertical slots are provably empty. That reasoning about the GAME is still
// correct. The reasoning about the CODEBASE was not: 53 EditMode tests failed
// immediately with
//     Insert of chunk int3(0,2,0) would overwrite live chunk int3(0,0,0)
//         in ring slot 0 without eviction
// because the raymarch oracle tests build their own synthetic worlds --
// RaymarchOccupancyTests.TallWindowBricks is 64x256x64 bricks, SIXTEEN chunks
// tall -- and insert them into a ChunkStore whose ring dims come from HERE.
// At WINDOW_CHUNKS_Y=2 the chunk-Y mask is (& 1) and cy=2 aliases onto cy=0.
//
// Two separate facts, kept distinct:
//   - The tests were never actually independent of this constant. They worked
//     only because 16 >= every synthetic window they declare. That latent
//     coupling is a real bug and ChunkStore.ConfigureWindow now exists to fix
//     it (see that method), but fixing it means editing 15 ChunkStore
//     construction sites across 7 test files.
//   - Reverting to 16 costs ~301 MB of clipmap that is 94% guaranteed-empty.
//     That is genuinely wasteful and it is a real cost on an 8 GB M1 Air. It
//     also still fits: §11.3 budgets 3,000 MB total and the full resident set
//     lands near 1,500 MB.
//
// The RUNTIME cost of Y=16 is separately recovered: StreamManager now streams
// only chunk layers that can contain content (cy in [0, MAX_GENERATED_CHUNK_Y]),
// not the full window height. Without that, load radius 15 would queue
// 31x31x16 = 15,376 chunks at startup instead of 961, and a slab crossing
// would be 8.0 MB of clipmap instead of 0.5 MB. So Y=16 now costs ADDRESS
// SPACE only, not work per frame.
//
// TO GET THE 301 MB BACK LATER (a real follow-up, not a maybe):
//   1. Give every test store its own window via ChunkStore.ConfigureWindow,
//      called from each test's fill helper with (windowBricks / 16).
//      Sites: RaymarchOccupancyTests 44/246, RaymarchMipTests 43/258,
//      RaymarchMipReseedTests 36/246/465, RaymarchDenseSkipTests 32/196,
//      RaymarchMacroSkipTests 28/378, RaymarchTests 17, MemoryModelTests 17,
//      LODDownsamplerTests 292, CascadeTierPoolTests 14.
//   2. Confirm all 156 pre-existing tests still green.
//   3. THEN set WINDOW_CHUNKS_Y = 2 and re-run.
// Doing 3 before 1 is what produced this note.
//
// STATUS OF EACH NEW NUMBER (kept distinct, per the project's evidence rules):
//   MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME - SPEC-MANDATED (§0.2), not measured.
//   WINDOW_CHUNKS_Y                    - constrained by the test harness above.
//   HYSTERESIS_RING_CHUNKS             - ASSUMPTION, flagged, Phase 4 measures.
//   MAX_CHUNK_LOADS_PER_FRAME          - ASSUMPTION, flagged, Phase 4 measures.
//   BRICK_POOL_HIGH_WATER_FRACTION     - ASSUMPTION, flagged, Phase 6 gate.
public static class EngineConfig
{
    // ---- Spatial units (§2.3, power-of-two chain; NEVER tune these) ----
    public const int BRICK_EDGE = 8;
    public const int CHUNK_EDGE_BRICKS = 16;

    // ---- Resident window (§3.3, §4.3) ----
    // Both MUST be powers of two: ChunkStore/TerrainClipmap/CascadeTierPool all
    // address toroidally via (coord & (dim-1)) per §3.3/§3.7. A non-power-of-two
    // does not fail loudly, it aliases silently -- the exact bug class
    // PHASE_3_COMPLETION.md §6.2 (phantom terrain) came from. ChunkStore's
    // constructor asserts on it rather than trusting this comment.
    public const int WINDOW_CHUNKS_XZ = 32;
    public const int WINDOW_CHUNKS_Y = 16;   // see header -- 2 is correct for the
                                             // game and wrong for the test harness

    // Chunks of slack past the load radius before a chunk is eligible for
    // distance eviction. Prevents thrash when the player hovers on a boundary:
    // without it, a chunk evicts and re-admits every time the camera jitters
    // across one plane.
    // ASSUMPTION, NOT MEASURED. 2 chunks = 25.6m of slack. §4.3 calls for "the
    // 128m LOD0 radius plus a hysteresis ring" without sizing the ring. Phase 4
    // reports admit/evict churn per traversal; if that number is non-trivial at
    // steady speed, raise this.
    public const int HYSTERESIS_RING_CHUNKS = 2;

    // ---- GPU MIRROR window height (NEW) ----
    // The store's ring and the GPU mirror do NOT need the same height, and
    // conflating them is what cost the first run its frame rate.
    //
    // WINDOW_CHUNKS_Y must stay 16 because the raymarch oracle tests build
    // synthetic worlds up to 16 chunks tall in their own ChunkStore. The GPU
    // mirror has no such caller: it only ever has to cover chunk layers that
    // generation can actually produce (cy=0 today, MAX_GENERATED_CHUNK_Y).
    //
    // Splitting them recovers the memory AND the per-frame cost that made
    // WINDOW_CHUNKS_Y=2 attractive, with none of the test breakage:
    //   clipmap        512x256x512 x4B = 268 MB  ->  512x64x512 x4B = 67 MB
    //   air-mip L1     256x128x256 = 8.4M cells  ->  256x32x256 = 2.1M cells
    //   cascade tier-1 33.6 MB                   ->  8.4 MB
    // The air-mip figure is the important one: AirMip.Pack rescans every cell
    // of every level on each upload, which under streaming is most frames.
    //
    // WHY 4 AND NOT 2 -- A REAL LIMITATION, FLAGGED:
    // The shader's window bounds guard terminates any ray whose CURRENT voxel
    // is outside the window, including in Y. So the mirror's height is also a
    // hard CAMERA ALTITUDE CEILING: above it every ray starts out of bounds and
    // the screen goes black. 4 chunks = 512 voxels = 51.2m. Terrain tops out at
    // 12m and the acceptance rig flies at y=12, so this is safe for Phase 4 --
    // but it is NOT a shippable ceiling for a game with flight.
    // The proper fix is to advance a ray to the window entry plane instead of
    // killing it, which is a new shader path and deliberately NOT bundled into
    // this run. Raise this constant if you need altitude before then; the cost
    // is linear.
    public const int MIRROR_CHUNKS_Y = 4;

    /// Camera altitude ceiling implied by MIRROR_CHUNKS_Y, in metres.
    public const float MIRROR_CEILING_METRES = MIRROR_CHUNKS_Y * 12.8f;

    // ---- Cascade throttle (NEW) ----
    // Cascade tiers re-run LODDownsampler.DownsampleChunkToTier per dirty chunk.
    // PHASE_3_COMPLETION.md §4 measured that whole pass at 3,033 ms for 484
    // chunks x 2 tiers, i.e. ~3ms per chunk-tier -- so an unbudgeted flush of a
    // streaming frame's worth of admissions is tens of ms before any GPU work.
    // Chunks not processed this frame stay dirty and are picked up next frame;
    // the visible effect is distant terrain resolving a few frames late.
    public const int MAX_CASCADE_CHUNKS_PER_FRAME = 2;

    // Wall-clock ceiling on the cascade pass, applied per tier on top of the
    // chunk cap -- whichever binds first wins, and at least one chunk always
    // processes so the queue drains.
    //
    // Measured need: with the chunk cap alone, cascades ran p50=22.5ms
    // p99=71.5ms of main-thread time per frame -- the ENTIRE upload budget
    // overrun once the terrain clipmap's own phases dropped to ~zero.
    // DownsampleChunkToTier is simply more expensive per chunk in the Editor
    // than the 3ms Phase 3's standalone numbers suggested, and a count cap
    // cannot express "stop when the frame is spent".
    public const float MAX_CASCADE_MS_PER_TIER = 2.0f;

    // ---- Upload throttle (§0.2, §3.7's anti-stutter guarantee) ----
    // §0.2: "~3MB (forces multi-frame spread)", "Raise only if... never (this
    // is the anti-stutter guarantee)". Treated as immovable.
    //
    // Worked example against THIS config, so the spread is not theoretical: one
    // X-slab crossing admits 1 x (content layers) x WINDOW_CHUNKS_XZ chunks.
    // With content confined to cy=0 that is 1 x 1 x 32 = 32 chunks, each 4096
    // bricks x 4B = 16 KB of clipmap entries => 0.5 MB, comfortably inside one
    // frame. If generation ever produces more vertical layers this scales
    // linearly: at all 16 layers it would be 8.0 MB, exactly the figure §3.7
    // flags as mandatory-to-spread across 2-3 frames. The budget below is what
    // enforces that automatically rather than by anyone remembering to.
    public const int MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME = 3 * 1024 * 1024;

    // Chunks uploaded per frame, independent of the byte cap.
    //
    // The byte cap alone was not a throttle: 3 MB / 16 KB = 192 chunks/frame,
    // and the real cost per chunk is CPU (a 4096-entry staging loop, an air-mip
    // region rebuild, a packed-mirror region update, a SetData), not the bus
    // traffic §0.2's cap was written to bound. Both limits now apply and
    // whichever binds first wins.
    // 16 chunks = 256 KB/frame, comfortably inside the byte cap, and matches
    // MAX_CHUNK_LOADS_PER_FRAME so admission and upload drain at the same rate.
    public const int MAX_CLIPMAP_CHUNKS_PER_FRAME = 16;

    // §3.7's "missing invariant" (8.4): chunks the player is standing in,
    // adjacent to, or has just edited upload THIS FRAME, exempt from the cap.
    // Only prefetched chunks ahead of the player are subject to the spread.
    public const int UPLOAD_EXEMPT_RADIUS_CHUNKS = 1;

    // ---- Streaming throughput (§4.3) ----
    // Cap on chunks promoted Loading->Resident per frame. Bounds the main
    // thread's drain cost: generation happens off-thread, but pool allocation,
    // store insertion and dirty-marking are single-writer main-thread work
    // (§3.2), so the drain is what must stay inside the frame budget.
    // ASSUMPTION, NOT MEASURED. At 60 m/s a boundary is crossed every ~0.21s
    // (§4.3), admitting 32 chunks per crossing => ~152 chunks/s => ~2.5
    // chunks/frame at 60fps. 16 gives ~6x burst headroom. Phase 4 reports
    // actual per-frame drain counts and ms; tune there.
    public const int MAX_CHUNK_LOADS_PER_FRAME = 16;

    // Cap on delta saves started per frame during eviction. The file write is
    // atomic and small, but the baseline REGENERATION that produces the diff is
    // a full chunk generate -- that is what this bounds.
    public const int MAX_CHUNK_SAVES_PER_FRAME = 4;

    // ---- Chunk generation concurrency (§0.1 invariant 8) ----
    // Worker threads generating chunks off the main thread. This WAS hardcoded
    // in StreamManager as max(2, ProcessorCount - 1), which is both a hard limit
    // living outside EngineConfig (invariant 8 says every limit is defined once,
    // here) and, on this machine, the direct cause of the frame stutter.
    //
    // MEASURED TRADE, rig runs 2026-08-28_173314 / _174620 / _175449, all with
    // macOS App Nap disabled and everything else identical:
    //
    //     workers   Gate C frame p99   load deficit p50
    //        7          961 ms                0
    //        4          846 ms               19
    //        2          326 ms              202
    //
    // Both move monotonically and in OPPOSITE directions. On a 4P+4E M1, seven
    // generation threads at normal priority oversubscribe the four performance
    // cores that the main and render threads also need, so the main thread is
    // descheduled -- Unity's own counters report a healthy ~21ms frame while
    // Time.unscaledDeltaTime records 1000ms+, because cpuMainThreadFrameTime
    // measures main-thread WORK, not wall time lost to not being scheduled.
    //
    // Thread priority is NOT an available lever: ThreadPriority.BelowNormal was
    // measured and changed nothing (Mono does not map it on macOS), which is why
    // count is the knob.
    //
    // 0 = derive as before (ProcessorCount - 1). Set a positive value to pin it.
    // Left at 0 because choosing between stutter and terrain popping is a
    // gameplay decision, not one to bake in from a benchmark.
    public const int CHUNK_GEN_WORKER_THREADS = 0;

    // ---- Brick Data Pool (§3.4, §3.6 LRU valve) ----
    // LOWERED 750,000 -> 500,000. RE-DERIVATION AGAINST MEASURED PEAK (§0.2).
    //
    // §0.2 says "raise only if Phase 6 shows normal building evicts too
    // aggressively"; this LOWERS it, which the table permits. Every unit of
    // capacity is paid TWICE -- 512 B of CPU array in BrickDataPool plus 512 B
    // of GPU mirror in TerrainClipmap.BrickDataBuffer, which is sized from
    // pool.Capacity -- so 750,000 cost 366 MB CPU + 366 MB GPU.
    //
    // MEASURED, via BrickDataPool.PeakUsed over a full acceptance run:
    //   Gate C peak 315,958   Gate E peak 336,731   (44.9% of the old cap)
    //   an earlier run measured 330,309 -- so the peak is stable near ~337k.
    // Phase 3's 151,470 figure in the old comment was a 22x22 world and is no
    // longer the relevant number; the streaming window is larger now.
    //
    // DERIVATION: §3.6's LRU valve fires at BRICK_POOL_HIGH_WATER_FRACTION
    // (0.85) of cap. The cap must keep the measured peak clear of that line or
    // the valve starts evicting chunks the player still wants:
    //     500,000 x 0.85 = 425,000 high-water
    //     425,000 / 336,731 = 26.2% headroom above the worst measured peak
    //     peak utilisation becomes 67.3% of cap
    // Saves 250,000 x 512 B = 122 MB CPU + 122 MB GPU = 244 MB.
    //
    // Valve BEHAVIOUR is unchanged in kind: it still fires only above 425,000,
    // which no measured run approaches. Overflow here is graceful (LRU evict),
    // unlike the cascade pools below, which throw -- hence the tighter relative
    // margin is acceptable here and deliberately not used there.
    public const int BRICK_POOL_CAP = 500000;

    /// Per-tier cascade pool capacities, sized against measured peaks.
    ///
    /// These used to be LODCascadeManager.DefaultTierPoolCapacity =
    /// BRICK_POOL_CAP / 4 for EVERY tier -- 187,500 each -- whose own comment
    /// conceded "/4 is STILL a guess, not a measured number". That guess cost
    /// 92 MB CPU + 92 MB GPU per tier, 366 MB across the two.
    ///
    /// MEASURED (BrickDataPool.PeakUsed, worst of both gates in one run):
    ///   tier 1 peak 112,566 of 187,500 = 60.0%
    ///   tier 2 peak  27,067 of 187,500 = 13.5%
    ///
    /// A cascade pool has NO valve -- BrickDataPool.Alloc throws when it runs
    /// dry, and LODCascadeManager's comment names that exception as the signal
    /// to raise the number. Exhaustion is therefore fatal rather than graceful,
    /// so these carry MORE relative headroom than BRICK_POOL_CAP, not less:
    ///   tier 1: 160,000 -> 42.1% above peak. Saves 27,500 x 512 B x2 =  27 MB
    ///   tier 2:  48,000 -> 77.3% above peak. Saves 139,500 x 512 B x2 = 136 MB
    /// Tier 2 is the outlier because it is the coarsest tier: an 8x downsample
    /// yields far fewer dense bricks, which is why 13.5% utilisation was never
    /// noticed behind a shared /4 constant.
    public static int CascadeTierPoolCap(int tier)
    {
        switch (tier)
        {
            case 1: return 160000;
            case 2: return 48000;
            default: return System.Math.Max(1024, BRICK_POOL_CAP / 4);
        }
    }

    // §3.6's valve: past this fraction of cap, StreamManager LRU-evicts the
    // coldest resident chunk to make room, so "the triggering edit always
    // succeeds -- you push the eviction radius inward, never fail."
    // ASSUMPTION, NOT MEASURED. 0.85 leaves ~112k bricks of slack, ~74% of the
    // entire measured Phase 3 world, so a single edit burst cannot outrun the
    // valve between frames. §3.6 states real pool sizes and whether eviction is
    // visually acceptable are "a Phase 6 test, not an assumption" -- this number
    // is explicitly on loan until then.
    public const float BRICK_POOL_HIGH_WATER_FRACTION = 0.85f;

    // ---- Derived helpers (single source of truth; never re-derive inline) ----
    public const int CHUNK_EDGE_VOXELS = CHUNK_EDGE_BRICKS * BRICK_EDGE;   // 128
    public const int BRICKS_PER_CHUNK = CHUNK_EDGE_BRICKS * CHUNK_EDGE_BRICKS * CHUNK_EDGE_BRICKS; // 4096
    public const int BRICK_BODY_BYTES = BRICK_EDGE * BRICK_EDGE * BRICK_EDGE; // 512

    // Clipmap bytes contributed by one chunk. Used by the upload throttle to
    // decide how many chunks fit under MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME.
    public const int CLIPMAP_BYTES_PER_CHUNK = BRICKS_PER_CHUNK * 4;       // 16384

    public static int BrickPoolHighWaterBricks =>
        (int)(BRICK_POOL_CAP * BRICK_POOL_HIGH_WATER_FRACTION);
}