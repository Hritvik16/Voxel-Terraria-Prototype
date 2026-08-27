// ==========================================
// Assets/ContentModules/Config/EngineConfig.cs
//
// PHASE 4 REVISION. §0.1 invariant 8: "Every hard limit is defined once, in
// EngineConfig, and read everywhere. Tuning is a number change, never a code
// change." Every value added below is a §0.2 hard-limits-table entry that
// Phase 4 is responsible for measuring and recording (§13 Phase 4 item 5).
//
// WHAT CHANGED FROM PHASE 3 AND WHY (each number traceable, none from memory):
//
//   WINDOW_CHUNKS_Y  16 -> 2   [DECIDED THIS PHASE, MEASURED CONSEQUENCE BELOW]
//     §4.3 offers "16 chunks vertical (204.8m band)" but explicitly labels it
//     "Placeholder for derivation, not a decided number", and §13 Phase 4
//     item 5 makes measuring/recording this a Phase 4 DELIVERABLE.
//     The derivation: WorldGenConstants.MAX_TERRAIN_HEIGHT is 120 and
//     MIN_TERRAIN_HEIGHT is 1, i.e. ALL generated terrain lives in world
//     voxels y in [1,120], entirely inside the cy=0 chunk layer (y in
//     [0,128)). Content.cs's own header records this and defers sea-level
//     recentering to Phase 4. So 14 of 16 vertical slots are PROVABLY empty,
//     not "probably" empty.
//     Consequence, derived not guessed (see §11.3 lines this fills in):
//       tier-0 clipmap  512x256x512 x4B = 268.4 MB  ->  512x32x512 x4B = 33.6 MB
//       air-mip pyramid              ~38.0 MB       ->              ~4.8 MB
//       tier-1 cascade  256x128x256 x4B = 33.6 MB   ->  256x16x256 x4B =  4.2 MB
//       tier-2 cascade  128x64x128  x4B =  4.2 MB   ->  128x8x128  x4B =  0.5 MB
//     ~301 MB reclaimed, and proportionally fewer bytes to push per slab.
//     RAISE THIS the moment generation produces any chunk outside cy=0 --
//     that is a correctness dependency, not a preference. StreamManager
//     asserts on it rather than trusting this comment.
//
//   WINDOW_CHUNKS_XZ  32 (UNCHANGED)
//     §4.3 sizes the window against 60 m/s: 32 chunks x 12.8m = 409.6m,
//     comfortably over the 128m LOD0 radius + hysteresis ring. LODConfig's
//     tier-2 outer bound (290m) is derived from THIS value's half-diagonal
//     (409.6/2 * sqrt(2) = 289.6m). Changing it silently invalidates that
//     derivation -- LODConfig's own comment says "Re-derive if
//     WINDOW_CHUNKS_XZ changes." Left alone.
//
// STATUS OF EACH NEW NUMBER (kept distinct per the project's evidence rules):
//   MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME - SPEC-MANDATED (§0.2), not measured.
//   WINDOW_CHUNKS_Y                    - DERIVED from MAX_TERRAIN_HEIGHT.
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
    // here does not fail loudly, it aliases silently -- which is the exact bug
    // class PHASE_3_COMPLETION.md §6.2 (phantom terrain) came from.
    public const int WINDOW_CHUNKS_XZ = 32;
    public const int WINDOW_CHUNKS_Y = 2;   // was 16 -- see header derivation

    // Chunks of slack past the LOD0 radius before a chunk is eligible for
    // distance eviction. Prevents thrash when the player hovers on a boundary:
    // without it, a chunk evicts and re-admits every time the camera jitters
    // across one plane.
    // ASSUMPTION, NOT MEASURED. 2 chunks = 25.6m of slack. §4.3 calls for
    // "the 128m LOD0 radius plus a hysteresis ring" without sizing the ring.
    // Phase 4's acceptance run reports admit/evict churn per minute of
    // traversal; if that number is non-trivial at steady speed, raise this.
    public const int HYSTERESIS_RING_CHUNKS = 2;

    // ---- Upload throttle (§0.2, §3.7's anti-stutter guarantee) ----
    // §0.2: "~3MB (forces multi-frame spread)", "Raise only if... never (this
    // is the anti-stutter guarantee)". Treated as immovable.
    // Worked example against THIS config, so the spread is not theoretical:
    // one X-slab crossing admits 1 x WINDOW_CHUNKS_Y x WINDOW_CHUNKS_XZ =
    // 1 x 2 x 32 = 64 chunks, each 4096 bricks x 4B = 16 KB of clipmap
    // entries => 1.0 MB. That now fits in ONE frame under the cap.
    // (At the old WINDOW_CHUNKS_Y=16 the same slab was 512 chunks = 8.0 MB,
    // which is precisely the "~8 MB of clipmap entries in a single frame"
    // §3.7 flags as mandatory-to-spread. Shrinking the Y window did not just
    // save memory, it moved the common case under the cap.)
    public const int MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME = 3 * 1024 * 1024;

    // §3.7's "missing invariant" (8.4): chunks the player is standing in,
    // adjacent to, or has just edited upload THIS FRAME, exempt from the cap.
    // Only prefetched chunks ahead of the player are subject to the spread.
    // Radius in chunks around the camera's chunk that gets the exemption.
    public const int UPLOAD_EXEMPT_RADIUS_CHUNKS = 1;

    // ---- Streaming throughput (§4.3) ----
    // Cap on chunks promoted Loading->Resident per frame. Bounds the main
    // thread's drain cost: generation happens off-thread, but pool allocation,
    // store insertion and dirty-marking are single-writer main-thread work
    // (§3.2), so the drain is what must stay inside the frame budget.
    // ASSUMPTION, NOT MEASURED. At 60 m/s a boundary is crossed every ~0.21s
    // (§4.3), admitting 64 chunks per crossing => ~305 chunks/s => ~5.1
    // chunks/frame at 60fps. 16 gives ~3x burst headroom over that steady
    // rate. Phase 4 reports actual per-frame drain counts and ms; tune there.
    public const int MAX_CHUNK_LOADS_PER_FRAME = 16;

    // Cap on delta saves started per frame during eviction. Saving is file
    // I/O off the main thread, but the baseline-diff that produces the bytes
    // is CPU work; this bounds it.
    public const int MAX_CHUNK_SAVES_PER_FRAME = 4;

    // ---- Brick Data Pool (§3.4, §3.6 LRU valve) ----
    // §0.2: 750,000 bricks (~384MB x2). "Raise only if Phase 6 shows normal
    // building evicts too aggressively." Phase 3's census measured 151,470
    // dense bricks for a 22x22 world = 20.2% of cap, so there is real
    // headroom at this world size. Unchanged.
    public const int BRICK_POOL_CAP = 750000;

    // §3.6's valve: past this fraction of cap, StreamManager LRU-evicts the
    // coldest resident chunk to make room, so "the triggering edit always
    // succeeds -- you push the eviction radius inward, never fail."
    // ASSUMPTION, NOT MEASURED. 0.85 leaves ~112k bricks of slack, ~74% of
    // the entire measured Phase 3 world, so a single edit burst cannot
    // outrun the valve between frames. §3.6 states real pool sizes and
    // whether eviction is visually acceptable are "a Phase 6 test, not an
    // assumption" -- this number is explicitly on loan until then.
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