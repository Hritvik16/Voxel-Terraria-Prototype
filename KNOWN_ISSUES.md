# Known issues and rig rules (detail)

Moved out of CLAUDE.md on 2026-10-01 (owner: lighter sessions). Verbatim, nothing
dropped. CLAUDE.md keeps a one-line index; READ THIS FILE before touching
streaming, rendering, fluids, memory or any rig, and before quoting a number.

## RIG SELECTION RULE: terrain-identity gates vs live fluid

**Do not run the acceptance rig's identity/hash gates with a live fluid load
and treat the failures as bugs. They are expected, they are documented here,
and the assertions must NOT be loosened to make them pass.**

Gates C and D verify persistence by hashing a chunk's content, doing something
(reload, a 500 m round trip, a dig and refill), and asserting the content is
IDENTICAL afterwards. That assertion presumes **no legitimate writer is
changing terrain during the run**. Fluid is exactly such a writer: water flows
into the chunk between the two hashes, so the content genuinely did change and
the hash genuinely does differ. The gate is reporting the truth; it is the
question that no longer applies.

Measured with an 8,000-voxel tiled load (`run-acceptance-fluid.sh`), these fail
and are expected to:

- `no reloaded chunk changed content (N of 9 hash-mismatched)`
- `edits survived a 500m round trip: 0x… == 0x…`
- `no brick that was UNIFORM before the dig failed to coalesce back` — the
  tunnel refilled with water, so those bricks legitimately are not uniform air

**This is a rig-selection rule, not a defect and not a TODO.** Run the
terrain-only rig (`run-acceptance-rig.sh`) for persistence and identity
evidence; run the combined-load rig (`run-acceptance-fluid.sh`) for frame-time
and upload-pressure evidence under fluid. Do not merge the two by weakening an
assertion — a fluid-aware identity gate would be a **new gate with a different
contract** (hash only chunks fluid never touched, or snapshot-and-compare with
fluid paused), and designing one is a deliberate decision, not a cleanup task.

Anything that IS a real failure under combined load will be something other
than these three. §4.3's upload budget, for instance, fails under fluid for a
real and separate reason — see the LOD-cascade finding in
`FLUID_PERFORMANCE_AB_RESULTS.md` §3.


## Known open issues (act on these, don't rediscover them)

- **OWNER PAUSE (`tools/heavy.sh pause|resume|status`, tasks 14/14b, 2026-10-03) DRAINS, it does not freeze.** The current lock holder runs to completion; every new `unity_lock_acquire` waits (no timeout) until `resume`, then the usual 45 s clear check. It takes effect at the next LOCK ACQUISITION or, since task 16, at the next LAUNCH of a script that takes the lock once and loops (`run-lavalight-ab`, `run-upscale-ab`, `run-roofskip-ab`, `run-perf60`, `run-looksweep`, `run-overhaul-batch`, `run-flyfast`, `run-farband`, `run-phase7-lighting-ab`): each calls `unity_hold_wait` before every player launch / build step / sub-rig, KEEPING the lock while it waits (`resume` touches it), and before the `sleep 60` / `COOL` settle, so the settle follows the pause. Those in-loop waits skip the 45 s clear check (the script owns the lock). Proven 2026-10-03 on `ORDER="S S" MINUTES=1 ./run-perf60.sh` (`Logs/overnight/16_perf60_pause.out`): launch 1 finished, `held by owner pause -- waiting`, listed under "waiting on the pause", ran arm 2 after `resume`. A NEW looping script must do the same or it drains to its end; per-launch callers (`run-tg2world.sh` loops) stop at the next launch. A run that printed `NOTE: owner paused during this run` may have contaminated timings (Mac likely in use) -- rerun that arm. Nothing frees RAM early any more (the owner dropped `--kill`).
- **BLACK BAR OVER THE SEA AT `coast_from_sea_30m` = A FAR-BAND HAND-OFF FAULT IN THE NON-RESIDENT STRIP OF THE WINDOW (task 18, 2026-10-03; DIAGNOSED, MECHANISM NOT PINNED, NOT FIXED).**
  - Seen in every `-world tg2` poses run since 2026-09-26 (pixel-identical in v3 07:47 and v4 16:16, so NOT from B1). It is in none of the `-farpreview` (far-band-only) runs, so the voxel window is involved.
  - The dark arcs below the bar are a SEPARATE thing. They are the voxel LOD tier boundaries: the dark rows equal the tier changes of view 6 in every column tested (rows 777/840 for tier 1->2, 1048 for 0->1). This is a tier seam on flat water, not this bug.
  - **What the bar is (measured):**
    - Every bar pixel is a far-band F0 hit (view 9, green), not a voxel hit.
    - It is not a far shadow: it is identical with `far.ShadowsOn = false`.
    - The voxel march was not capped over resident terrain (view 11 all green).
    - The far march hits within a few steps there: the only low-step (blue) patch below the horizon in view 10.
    - Its colour is water albedo on an unlit face, RGB (1,5,13).
  - **Where it is (measured):**
    - Pose eye (4000, 542, 400) m. Window origin chunks (296, 34, 15), 32x32. Resident = the +-10-chunk (128 m) load square, 3087 = 21x21x7.
    - Bar bottom (row ~754) = the far edge of the load square, 137.6 m out.
    - Bar top (row ~692, 60 deg FOV, 0.8 deg pitch) = rays that reach the WINDOW's far edge (z chunk 47, 201.6 m) exactly at sea level.
    - So the bar is the in-window, NON-resident strip (z chunks 42-46). It is drawn only for chunk columns x < 312 (left of the camera's chunk, right edge at screen x 915). The same strip right of x = 312 draws normal sea.
  - **Most likely mechanism (fits the geometry exactly, code path not pinned):** for those rays the far march starts at the voxel march's end (about the window edge), not at the strip entry (`FarStartT`). There the ray is already below the far sea top (`seaLevelY + 1`), so `FarMarch`'s first test `y0 <= H` returns a -z SIDE-FACE hit on the first water texel outside the window: a dark water wall.
    - `FarStartT` can only skip the strip if it reads those columns as resident. The CPU bitmap (`FarField.UpdateResidency`, store identity-checked) should be 0 there, so the left-only extent is unexplained.
    - The alternative (wrong far texels about 10 m above sea at the strip entry) fits only by coincidence, and the preview runs argue against it.
  - Pinning it is queued as an evidence task: `tools/overnight/queue/19-coast-bar-pixel-probe.md`. A fix (likely in `FarStartT` / the residency bits, or clamping a below-surface far start) waits for that and for the owner.
  - **Images** (all in `~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/FarBand/2026-10-03_181049_world/`):
    - `coast_from_sea_30m_noon.png`: the black bar is left of centre, about 2/3 down, over the sea. The two thin dark arcs below it are the tier seams.
    - `coast_from_sea_30m_noon_farshadowoff.png`: the same frame with far shadows off. The bar is unchanged.
    - `coast_from_sea_30m_golden_band.png` (view 9): the bar's rows are green (F0). Blue (voxel) starts at row 754 across the whole width.
    - `coast_from_sea_30m_golden_view10.png` (far step heat): the blue rectangle at the bar's position = an immediate far hit. The rest of the sea is yellow.
    - `coast_from_sea_30m_golden_view11.png` (voxel stop): all green or blue, with no magenta or red. The voxel march was not capped over resident terrain.
    - `coast_from_sea_30m_golden_view6.png` (voxel tier): the red/green and green/blue boundaries lie exactly on the two dark arcs of the noon shot.
- **STREAMING REQUIREMENT (worst case, derived 2026-10-03, task 16b). It REPLACES
  B1's "no more than 5% worse than v3" pass line (owner removed it 2026-10-03).**
  Code: `StreamingRequirement.cs` (pure, live constants), pinned by
  `StreamingRequirementTests`. `MODE=stream ./run-tg2world.sh` prints it as
  report section 7: `REQUIRED x/s, CAPACITY y/s -> MET / NOT MET (short by fx)`,
  plus latency and main-thread lines. `tools/streamprobe-table.py` tabulates them.
  **Today: NOT MET on every line, and the latency line CANNOT be met at any
  throughput. The latency line is a design choice for the owner, not engine speed.**
  - **Constants.**
    - Chunk edge E = 12.8 m: `CHUNK_EDGE_VOXELS` = 128 (`EngineConfig.cs:465`), x 0.1 m (`CoordMath.cs:12`).
    - Load radius r = 10 (`EngineConfig.cs:268` `TG2_LOAD_RADIUS_CHUNKS`). Side 21. Chebyshev square round the camera's chunk (`StreamManager.cs:696-740`).
    - Band L = 7 layers = 2 x `V_LOAD_LAYERS` 3 + 1 (`StreamManager.cs:235`). Y recentre moves `V_STREAM_LAG` + 1 = 2 layers at once (`StreamManager.cs:236`, `CentredBand` :275; a fast fall uses it, `FALL_SPEED_CENTRED` 4 m/s, :306).
    - Window 32 x 16 x 32 (`EngineConfig.cs:70-71`); evict radius 12 = r + `HYSTERESIS_RING_CHUNKS` 2 (`EngineConfig.cs:82`); max evict radius 15 (`StreamManager.cs:432`). GPU mirror 32 x 8 x 32 (`EngineConfig.cs:261`).
    - **R_d (where voxels hand over to the far band) = the residency edge**: `FarStartT` hands a ray to the far band at the first NON-resident chunk column (`Raymarch.compute:1977-2003`). So R_d = r x E = 128 m (to 140.8 m, by where the camera is in its chunk).
    - **Recentre rule: no request-side hysteresis in X/Z.** A chunk is requested in the Update of the frame the camera crosses a chunk plane (`StreamManager.cs:596-604`), so up to one frame late. Its near face is then exactly r x E = 128 m away, i.e. already ON R_d.
    - Prefetch margin M = (request radius - R_d) x E = **0**.
    - Hysteresis exists only for EVICTION. The window's 3 spare chunks (15 - 12) are unused by requests.
  - **The worst case, factor by factor.**
    - v = 60 m/s: the brief's Decided "late-game flight". Nothing in the game is faster: terminal fall 55 m/s (`PlayerConfig.cs:86`) + 5.2 m/s run (:53). The dev fly camera (up to 120 x4, `PlaygroundFlyCamera.cs:22-25`) is a tool.
    - Heading: an X or Z plane crossing adds side x L = 147 chunks; a Y layer adds side^2 = 441. Rate = ((|vx|+|vz|) x 147 + |vy| x 441) / E. By Cauchy-Schwarz the max is v/E x sqrt(2 x 147^2 + 441^2): a diagonal DIVE about 65 deg down a mountainside (tg2 has 70 deg slopes; a fast fall takes the centred band).
    - Terrain: skimming the ground, so every chunk is full cost (no uniform/air fast path).
    - Latency-worst heading: along an axis (closing speed v on the face).
  - **Requirement table** (r 10, L 7, E 12.8, v 60, 60 Hz):
    | line | formula | number |
    |---|---|---|
    | A_max, worst heading | v/E x sqrt(2(side L)^2 + side^4) | **2,285 chunks/s** |
    | level diagonal only | sqrt2 v side L / E | 974 /s |
    | straight dive only | v side^2 / E | 2,067 /s |
    | brief's estimate (axis, level) | v side L / E | 689 /s (= its "~700") |
    | burst per crossing | side L; (2 side - 1) L; 2 side^2 | 147; 287 corner; 882 per Y recentre |
    | L_min, request -> on GPU p99 | M / v - 1 frame | **0 s: M = 0, unmeetable** |
    | per-chunk gen time at W workers | <= W / A_max | 3.1 ms (7 workers), 1.75 ms (4 P-cores) |
    | main thread | A_max / 60 x admit ms <= 16.7 ms | 38.1 admits/frame |
    | unload | = A_max | 38.1 evictions/frame |
    | cold start / teleport | band / A_max | 3,087 chunks -> 1.35 s. **PROPOSED** target 2 s (behind a fade); met by construction if A_max is |
  - **The gap** (existing data: task 10 + B1, 21 arms; non-uniform chunks; 7 workers;
    capacity = 7 x 1000 / (gen + downsample ms)):
    | mode | gen+down p50 / p99 ms | capacity p50 / p99 /s | short of 2,285 by | worst-case speed sustained |
    |---|---|---|---|---|
    | fly 56 m/s (worst observed) | 984 / 3,230 | 7.1 / 2.2 | 322x / 1,053x | 0.19 m/s |
    | walk, slow mode, v3 | 437 / 1,154 | 16.0 / 6.1 | 143x / 377x | 0.42 m/s |
    | walk, slow mode, v4 (4 of 4 runs) | 462 / 1,010 | 15.2 / 6.9 | 150x / 330x | 0.40 m/s |
    | walk, fast mode, v3 (3 of 7 v3 runs) | 241 / 754 | 29.0 / 9.3 | 79x / 246x | 0.76 m/s |
    | isolated (main thread, workers idle) x 7, ideal | 49-65 p50 | ~121 | 19x | 3.2 m/s (7.4 m/s level) |
    - Measured admitted rate (incl. 37-86% uniform chunks): 20-53 chunks/s.
    - Latency: request -> on GPU p99 8.8 s (fly 56) to 33.6 s (walk) vs a 0 s budget.
    - Main thread: 38.1 x admit p50 0.86-1.97 ms = 33-75 ms per frame (2-4.5x over 16.7 ms). Admission cap `MAX_CHUNK_LOADS_PER_FRAME` = 16 (`EngineConfig.cs:177`), 2.4x under.
    - Upload: `MAX_CLIPMAP_CHUNKS_PER_FRAME` = 16 (`EngineConfig.cs:161`), 2.4x under. Mean ~260 KB per uploaded chunk -> ~9.9 MB/frame vs the 3 MB cap (`:150`), 3.3x (estimate: bytes/frame are not linear in chunks).
    - Unload: ~0.12 ms p50 / 0.52 ms p99 per evicted chunk (estimate: frames with >= 20 evictions and no admission; includes slide + pending rebuild). x 38.1 = 4.5 / 20 ms: fits at p50, not at p99.
    - **Stage that limits: generation**, by 19x even with perfect 7-thread scaling of the isolated cost. Admission and upload are 2-4x short behind it.
    - Section 7 confirmed on 2 walk launches, AC, no owner pause (one build, 2026-10-03 20:16). v3 (`Logs/tg2world_20261003_201553.txt`): capacity 16.2 / 7.5 /s, short 141x / 303x; latency p99 34.1 s; main thread 41.0 ms. v4 (`Logs/tg2world_20261003_202723.txt`): 13.9 / 6.8 /s, short 164x / 334x; 35.4 s; 37.8 ms.
  - **The latency line needs a design change.**
    - Because R_d IS the load radius, every newly requested chunk is already inside R_d. Even walking fails it.
    - Any budget needs request radius > detail radius. Examples: at 60 m/s, request 10 / detail 7, or request 13 / detail 10 (the most today's window allows: `MaxLoadRadiusChunks(32)` = 13). Either gives M = 38.4 m -> L_min = 0.62 s.
    - Request 13 also raises A_max to 3,640/s (side 27). Detail 7 at speed keeps 2,285/s.
    - Both need the far hand-over decoupled from residency, or a speed-dependent detail radius.
- **STREAMING BASELINE v3 (2026-10-02, task 10; measured 2026-10-03).** The
  numbers every surface-rebuild step (B1 on) is compared against. (Since task 16b
  the PASS LINE is the STREAMING REQUIREMENT entry above, not a % vs this table.)
  `MODE=stream ./run-tg2world.sh` (`MOTION=walk|fly`, `EXTRA="-movespeed 14"`):
  `-streamprobe` (`PlaygroundCapture.StreamProbe.cs`) with `StreamTrace` (opt-in,
  observe-only, per-chunk timeline). Side-by-side table:
  `python3 tools/streamprobe-table.py <StreamProbe run dirs>`. Release standalone,
  Performance preset (480), AC power, 7 workers, load radius 10 chunks (+-128 m),
  7-layer band = 3,087 chunks. Cells are p50 / p95 / max. A range means the spread
  across launches: walk 2, fly14 2, fly56 3.

  | stage | walk 5 m/s | fly 14 m/s | fly 56 m/s |
  |---|---|---|---|
  | frame ms | 9.9-10.1 / 20.6-20.7 / 91-235 | 6.9 / 14.6-15.2 / 68-269 | 6.4-6.6 / 12.1-12.2 / 172-262 |
  | 1 gen 1 solid chunk, worker ms | 349-354 / 644-708 / 1.3-2.0 s | 481-486 / 1120-1140 / 4.2-8.8 s | 895-1118 / 2429-2650 / 3.6-3.9 s |
  | 1 same, isolated (main thread, workers idle) ms | 62-65 p50 | 58-67 p50 | 59-73 p50 |
  | 1 chunks admitted / s | 24-25 | 20 | 7-11 |
  | 1 request -> dispatch (pending queue) s | 11.0-12.6 / 27 / 51 | 7.4-9.3 / 17-18 / 36 | 1.7-2.5 / 3.4-4.4 / 6-9 |
  | 2 admit 1 chunk, main ms | 1.07-1.11 / 4.4-4.5 / 10-16 | 1.00-1.07 / 4.1-4.3 / 9-13 | 0.90-0.98 / 3.6-4.4 / 9-12 |
  | 3 admitted -> in mirror ms | 2.9 / 9.2-9.8 / 155-195 | 2.9-3.0 / 10-12 / 316-329 | 3.5-3.8 / 10-11.5 / 24-715 |
  | 3 upload pass ms / frame | 0.32-0.34 / 1.32-1.37 / 10.7 | 0.27-0.28 / 1.25-1.39 / 10.5 | 0.37-0.38 / 1.34-1.87 / 10.3 |
  | 3 bytes / frame (cap 3.0 MB) | 0 / 1.48 MB / 3.41 MB | 0 / 0.84 MB / 2.2 MB | 0 / 0-0.62 MB / 2.4 MB |
  | 4 request -> in mirror s | 11.7-13.2 / 27.6-27.8 / 51 | 8.2-9.6 / 17.5-18.6 / 38 | 2.8-3.0 / 5.8-6.1 / 10-13 |
  | 4 LATE: inside 64 m before arriving, for s | 3.8-4.1 / 10.4-10.7 / 18-22 | 3.4-3.6 / 16.5-17.8 / 29-30 | 1.3-1.5 / 4.8-5.1 / 8-11 |
  | 5 deficit (of 3,087) | 2650-2690 / 2933-2952 | 2912-2915 / 3013-3028 | 3075-3081 / 3087 |
  | fill of a fresh band at a still pose, s | 35-40 | 25-35 | 24-49 |

  **The bottleneck is stage 1, generation throughput. The upload is not it.**
  - Even WALKING outruns generation. Walk A's deficit climbs 136 -> ~2,900 within 60 s.
  - The workers are saturated (busy >= 102%, a lower bound).
  - A solid chunk costs 58-73 ms alone, but 350 ms (walk) to ~1 s (fly) on a
    worker under load, i.e. 5-17x contention. The cause is NOT isolated: 7 workers
    on 4P+4E cores plus the main and render threads, the E-core/QoS class, or memory
    bandwidth.
  - Stages 2-3 are cheap. Admission is ~1 ms per chunk, of which the cascade
    repack + write is 0.95 ms and the scratch->pool copy 0.01 ms. A chunk reaches the
    mirror in the same frame (0 frames p95). The upload pass is 0.3 / 1.3 ms.
  - 1-2 frames per walk exceed the 3 MB cap (3.41 MB). These are exempt-radius
    chunks, by §3.7's 8.4 invariant.
  - fly56's short request->mirror time is survivor bias: 269k requests were dropped
    because the camera moved on.
  - "Deficit" and "missing" count air and deep chunks too. The probe cannot say which
    of them would be visible. At the moving shot: walk A 19 and walk B 198 missing
    inside 64 m.

  **LOD hand-offs.** From the code (`LODConfig`, `FarField`): tier 0 (0.1 m) <= 64 m,
  tier 1 <= 128 m, tier 2 <= 290 m. Voxel DATA exists only inside the +-128 m XZ load
  square and the 89.6 m band. Beyond that the far band draws, F0..F4 at 0.8-12.8 m
  texels, rings ~352 m x 2^L.
  - Walk still vs moving (`StreamProbe/2026-10-03_071515_streamwalk_*`, `crops_horizon.png`):
    - The tier 0->1 boundary sits at the visible horizon in both: a BOUNDARY BY DESIGN.
    - Mid-walk, the left half of the near horizon becomes far F1, with a straight
      vertical edge where the still frame had voxels: SLOW LOADING.
  - So the owner sees both, and the moving pop is the deficit.
  - **The fly pose draws NO voxels even fully loaded: a DESIGNED BOUNDARY, not a bug
    (task 13, 2026-10-03).** The eye sits on a cliff lip (ground 805.8 m right below).
    The view NE looks down onto a plain at ~752 m, 53 m lower, which is BELOW THE
    BAND: band 62..68 = 793.6 m floor at the 63 m pose. `MODE=flysweep ./run-tg2world.sh`
    (`-flysweep`, `PlaygroundCapture.FlySweep.cs`, `FlySweep/2026-10-03_105638_*`)
    swept eye height 2/8/16/32/48/63 m x pitch 8/30. Voxel % of the frame:
    0 everywhere except 16 m/30 deg 1.5, 8 m/30 deg 6.7, 2 m/30 deg 20.4 and
    2 m/8 deg 1.5; that is only the near lip and the higher sides.
    - Evidence for the boundary, per probe ray (shader debug pixel vs CPU oracles):
      - Shader steps <= 44 of 1024; no iteration cap.
      - The march leaves through the band floor and finds no hit. The CPU store
        (`ChunkStore.GetVoxel`) holds no solid voxel on the same ray.
      - The world map's surface on every probe ray is at 750-754 m: "in band False".
      - Mirror check: lost 0, lag 0, stale 0.
    - Why the band misses the plain:
      - Vertically, `BandHolds` (`StreamManager.cs:348`) must keep the eye's layer and
        the one below, so the 752 m plain (layer 58) can enter the band only with the
        eye below ~832 m.
      - Below that height, `BandScore` (`:354`, distance-weighted, ignores view
        direction) chose 60..66 (768 m floor) over a band reaching layer 58.
    - Horizontally, the 8 deg centre ray meets the ground 397-1504 m out, beyond
      the +-128 m load square. That boundary is designed too.
    - Owner's call: a view-aware band score or a taller band (memory) would change
      it. Task 10's fly numbers stay valid as a streaming baseline; its fly still
      just shows far band only.
    - Capture-mode trap hit again: a new `-flag` missing from `Playground.cs`'s
      three capture lists leaves the walk player driving the camera (it was at
      796.2 m inside rock: black beauty, 100% "voxel" debug views).

  Unified-memory "upload" (walk, frames that uploaded, sums of phase p50 / p95):
  - Format conversion 0.12 / 1.03 ms: the staging table, `AirMip.RebuildRegion`, `PackRegion`.
  - SetData copies 0.11 / 0.59 ms.
  - So ~55-65% is conversion, ~35-45% copy. Metal's work after SetData is unmeasured.

- **NEAR-TERRAIN SHIMMER BASELINE, BEFORE TAA (measured 2026-10-02, task 05).**
  `MODE=shimmer ./run-tg2world.sh` (`-nearshimmer -renderpreset quality`: 960x540
  gate, noon, temporal shadows ON, shadow level 0) -> `Logs/nearshimmer_<run>.txt`.
  Per pose: |luma change| between the base frame and four QUARTER-GATE-PIXEL
  nudges (an offset in the camera's projection matrix -- the jitter TAA itself
  applies; a 0.03 deg camera rotation never reached the frame, the first try's
  nudged frames were byte-identical), split by CPU-DDA depth through the
  ChunkStore: near < 50 m, mid 50-200 m, far = no voxel hit in 200 m, sky masked
  out via the band view. Threshold 24/255 as `tools/farshimmer-report.py`.
  BASELINE (`Logs/nearshimmer_20261002_113842.txt`, mean |dL| / % > 24):

  | pose | near | mid | far | all non-sky |
  |---|---|---|---|---|
  | eye_level_1 | 1.98 / 1.46 | 1.18 / 0.08 | 1.37 / 0.62 | 1.73 / 1.05 |
  | eye_level_2 | 8.09 / 12.92 | 7.54 / 9.06 | 11.06 / 16.21 | 9.26 / 13.25 |
  | eye_level_3 | 2.63 / 3.91 | (0% of frame) | 10.71 / 14.60 | 8.98 / 12.30 |
  | slope_walk | 3.87 / 5.07 | 3.18 / 1.62 | 2.38 / 2.01 | 3.58 / 3.74 |
  | MEAN of poses | 4.14 / 5.84 | 3.96 / 3.59 | 6.38 / 8.36 | 5.89 / 7.59 |

  REPEATABILITY: three runs (two on the same build) agree to +-0.01 on every
  value; the noise floor (base vs the unmoved repeat) is 0.00 everywhere --
  without TAA a still frame is reproduced exactly. With TAA ON the floor will
  not be 0 (jitter phase); read the nudge numbers against it. slope_walk is
  confirmed on the voxels (centre ray 68.7 m, cliff 30-80 m away); eye_level_1
  is 47% near terrain but dim (low |dL|); eye_level_3 is 86% beyond 200 m (a
  far-band frame). Shots: `FarBand/2026-10-02_114151_nearshimmer/`.
  UNEXPLAINED, NOT FOLLOWED UP: in the FIRST run (rotation nudges, no slope
  search, so eye_level_1 -> eye_level_2 was a direct jump) eye_level_2 looked
  into a wall 0.5-1.4 m away, in the frame AND in the CPU store's depth
  (`FarBand/2026-10-02_111820_nearshimmer/eye_level_2_base.png`); every later
  run at the same eye sees an open valley (median hit 51 m). If it recurs it is
  stale CPU-store content after a jump. Re-run with `EXTRA=-noslope`.
  NOT REPRODUCED (task 05c, 2026-10-02): two `-noslope` runs (direct
  eye_level_1 -> eye_level_2 jump), one freshly built
  (`Logs/nearshimmer_20261002_132001.txt`, shots `FarBand/2026-10-02_132546_nearshimmer/`)
  and one on the same build (`Logs/nearshimmer_20261002_133003.txt`, shots
  `FarBand/2026-10-02_133144_nearshimmer/`): eye_level_2 is an open valley in
  both, median hit 51.4 m, depth bands identical to the baseline; the
  screenshots match each other and show a cliff on the left and the valley
  and river beyond. The first run also differed at eye_level_1 (<50 m 56.1% /
  50-200 m 1.7%, against 46.6 / 15.7 in every later run), and it ran
  uncommitted pre-`4d0d03c` code, so a difference in the code is a likelier
  cause than the order of the jumps. Closed unless the wall shows up again.

- **TAA EXISTS, OFF BY DEFAULT (task 06, 2026-10-02). Risk checks done in
  task 07 (results below); still OFF until the owner compares stills.** `-taa 1` or Playground F4
  (`RaymarchFeature.Taa`); `-taasharpen <percent>` (default 25). Raymarch.compute's
  TAA block: Halton(2,3) 8-frame jitter of the primary ray, reprojection of the
  exact hit point (far band and sky too) through the previous UNJITTERED
  view-projection, a 4-tap history fetch that keeps only taps with the same
  surface (distance within 2% + 2 voxels, widened by the local depth slope) and
  drops a tap whose voxel is the same but whose material changed, YCoCg 3x3
  variance clip, new-frame weight 0.1 still -> 0.4 at 8 px of motion,
  luma-weighted blend, then a 0.25 cross sharpen clamped to its neighbours. The
  history is reset by a resize, a non-Beauty view, a teleport (> 20 m), the sun
  moving > ~1 deg in a frame, or a look / shadow-rung / tonemap change. Chunks
  that `TerrainClipmap` re-uploaded this frame (nearest 16) take no history at
  all: that is CHUNK granularity (12.8 m), so a live-fluid chunk never
  accumulates. Buffers: W*H*56 B (29 MB at 960x540) plus one scratch target.
  OFF is untouched: `MODE=poses` 55/56 shots md5-identical to the previous
  commit (`FarBand/2026-10-02_150159_world` vs `_143337_world`); the 56th is the
  minimap's underground slice, which is still streaming at capture. ON, same
  build (`Logs/nearshimmer_20261002_154753.txt` vs `_155606.txt`, mean |dL| / % > 24):
  near 4.14 / 5.84 -> 2.67 / 1.57, far 6.38 / 8.36 -> 3.55 / 2.50, all
  5.89 / 7.59 -> 3.04 / 1.76. BUT the still-camera FLOOR is no longer 0: 0.73-3.18
  mean |dL| per pose (the residual of the 8-frame jitter cycle at weight 0.1), so
  a still TAA frame is not exactly reproducible and part of every ON nudge
  number is that floor. Stills (`FarBand/2026-10-02_152951_world/eye_level_*`):
  sub-pixel speckle on distant slopes and snow becomes smooth texture, near
  blocks keep their edges but are visibly softer; no ghosting or holes.
  **TASK 07 RISK CHECKS (2026-10-02), ON vs OFF, same binary per pair.**
  `-taarisk` (`MODE=taarisk ./run-tg2world.sh`, `EXTRA="-taa 0|1"`,
  `TAAWORLD=` for the default world) captures consecutive frames;
  `tools/taarisk-report.py OFF ON OUT` writes numbers and OFF-over-ON sheets.
  (1) FLUID, PASS: arena chaos burst (water/sand/lava) falling and settling,
  three runs of 8 frames: no trails or doubled edges (`Screenshots/TaaRisk/arena/
  fluid_*`). Live-fluid chunks take no history, so TAA is effectively off there.
  In tg2, fluid at y ~695 m looked frozen; task 09 found it DOES move, just
  slowly (see "FLUID IN TG2 DOES SIMULATE" below).
  (2) EDIT, PASS: a 2 m dig is fully clean in the first frame that shows it;
  ghost index f0 0.07 ON vs 0.02 OFF, ON's floor 0.05 (`arena/edit_hole_crop.png`).
  (3) FAST TURNS / 60 m/s FLIGHT, PASS (soft, no smear trails): spin 1.5 and 6
  deg/frame, edges 0.58-0.61x OFF in motion vs 0.48-0.50x still. Flight ON-vs-OFF
  numbers are CONFOUNDED by streaming: same poses, but near chunks landed on
  different frames in the two runs (both show the same unloaded block).
  (4) LIGHT, FIXED + RESIDUAL: below the old ~1 deg/frame sun reset, the history
  lagged the light: at 0.5 and 0.9 deg/frame ON was +2.6..+4 luma brighter
  than OFF while the sun set, edges 0.32-0.38x. The held time scrubber (3 h/s)
  moves ~0.73 deg/frame at 60 fps, so it was in that band. The reset is now 0.25
  deg/frame (`TaaSunResetDot`); 0.5/0.9 now reset (bias -0.04, edges 1.04x). The
  RESIDUAL: 0.2 deg/frame still lags (+2.8..+3.1 luma, edges 0.40-0.44x).
  The shipped 120 s day is ~0.1 deg/frame at 30 fps. Fixing it needs a
  light-aware history rejection: a tuning/design call, not done.
  (5) FAR SEAMS, PASS: `-farseams` beauty shots at 150/300/1000 m, settled and
  in flight: no new seams or streaks; dL 1.3-3.0 (the jitter floor).
  (6) SHIMMER, same build: OFF 5.89 / 7.59 (= task 05) -> ON 3.01 / 1.66 all;
  near 4.14 / 5.84 -> 2.66 / 1.52 (`Logs/nearshimmer_20261002_172129.txt` /
  `_173032.txt`).
  (7) COST, NOT RESOLVED AGAINST THE ~1.5 ms BUDGET: `MODE=walk MINUTES=3` (the
  Performance preset, 480 px internal), 12 arms ABBAAB + BAABBA. OFF 13.43
  ms, ON 15.41 ms (means of arm means). Paired ON-OFF: +4.21 +7.00 -0.23 +2.94
  -2.14 +0.05 = +1.97 ms, SE 1.37. Medians: +1.2 ms of arm medians, +0.8 ms
  per window. So 0.8-2 ms, and the walk circle's own window spread (8-26 ms)
  hides the rest. At the 960 Quality gate the TAA passes cover 4x the pixels;
  not measured. Logs `Logs/overnight/07_cost_arm*_taa*.out`.
  **TASK 08 CHECKERBOARD (2026-10-03), `-checker 1` (implies TAA), OFF by
  default.** Kernel `CSMainChecker` (CSMain's body with a compile-time
  `checker = true`) runs at half width, tracing pixels with ((x + y) & 1) ==
  parity, which flips every frame. TaaResolve: a traced pixel resolves as before
  but uses only traced neighbours (itself + diagonals; +-2 for the depth slope);
  a missing pixel borrows the reprojection of the cross neighbour whose history
  matches, takes that history clipped to the 4 neighbours' box, and otherwise
  (disocclusion, dirty chunk, reset) uses the edge-directed neighbour average,
  which never feeds the history. The jitter is held 2 frames (else each half
  sees only odd or only even Halton indices: a half-pixel bias); the
  temporal-shadow turn advances every 2nd frame and its history lookup tries the
  horizontal neighbour (each ping-pong buffer only ever holds one checker half).
  A RUNTIME uniform inside CSMain moved TAA-off images by a few silhouette
  pixels (Metal recompiled the float maths), hence the separate kernel.
  OFF: `MODE=poses` 55/56 md5-identical to `FarBand/2026-10-02_150159_world`
  (`_195352_world`; the 56th is the minimap underground slice, as in task 06).
  COST, `MODE=walk MINUTES=3 EXTRA="... -renderpreset quality"` (960x540, the
  owner's minimum), ABBAAB + ABBA: TAA-only 30.81 ms mean of 30 s windows (worst
  39.74, 419 frames > 33 ms per 1000), TAA+checker 19.61 ms (worst 25.09, best
  15.34, 13 per 1000). Paired: -11.19 -11.13 -11.55 -11.17 = **-11.26 ms**. 16.7 ms
  is NOT reached: mean 2.9 ms over, worst window 8.4 ms over. Arms 6-8 excluded:
  the laptop was on battery and hit 1% (Low Power Sleep at 21:44, then
  hibernate); arm 6 (checker, ~1% battery) ran at TAA-only speed with half
  the rays. Summary `Logs/overnight/08_cost_summary.txt`. ALL RIGS IN THIS
  SESSION AND TASK 07 RAN ON BATTERY -- check `pmset -g batt` before a cost arm.
  GUARD (2026-10-03, task 12): `run-tg2world.sh` and `run-acceptance-rig.sh` now
  log `power start:/end: ... source=... charge=...` (`tools/power-status.sh`)
  and run under `caffeinate -dimsu -w <script pid>`; `MODE=walk|orbit` prints
  `WARNING: ON BATTERY` (still runs). Read that line before quoting a cost arm.
  Caffeinate does NOT stop battery-critical hibernation -- keep the Mac on AC.
  QUALITY vs TAA-only, same binary: shimmer all 3.01/1.74 -> 3.31/2.34, near
  2.68/1.70 -> 2.80/1.89, far 3.30/2.14 -> 4.18/3.61 (OFF 5.89/7.59);
  `Logs/nearshimmer_20261002_202848.txt` / `_203508.txt`. No checker pattern
  in stills (gate-scale 2x2 checker energy / gradient 0.14-0.23 in both); 10-25%
  less edge energy (softer), still frames 0.74-0.82x TAA-only's edges. Edit:
  ghost f0 0.10 vs 0.11. Fluid: no trails; live-fluid chunks are visibly softer
  (half their pixels are filled every frame). Spin: no trails; at 6 deg/frame
  the sea/sky horizon shows small alternating-pixel teeth. Sun 0.2 deg/frame:
  bias +0.1 luma vs TAA-only (no extra lag). Sheets `Screenshots/Checker/`.

- **THE TG2 WORLD IS OVER THE 3 GB CAP BEFORE THE PLAYER BUILDS ANYTHING (found
  2026-09-26, NOT FIXED).** Peak phys_footprint 3,218-3,814 MB across four runs
  (`Logs/tg2world_*_mem.txt`). On mountain ground untouched terrain holds
  ~849k of the 1.0 M brick-pool slots and the LRU valve is already firing
  (deficit 322-870). Player building is NOT the risk -- §3.6's valve caps that
  by design; the risk is that generated terrain leaves no headroom and that the
  pools are sized past 3 GB in total. Measured lever not yet enabled:
  `TerrainSpec.brickGeologyDepth` at 3 m cuts mountain dense bricks ~45%
  (`Logs/TerrainGen/v2_mountain_dense.txt`). **No breakdown of the remaining
  ~2.9 GB exists -- measure one before changing anything.**
- **TG2 AT WALKING HEIGHT (first measured 2026-10-01, `MODE=walk
  ./run-tg2world.sh`: `-motion walk`, a 150 m circle round the spawn at 1.6 m
  eye height, 5 m/s, Performance preset, 5 min).** 30 s windows 10.0-13.8 ms
  except a 3.0-3.5 min stretch at 17.8-21.3 ms with 53 frames/1000 over 33 ms
  (`Logs/tg2world_20261001_151146.txt`). Peak memory 3,328 MB =
  **1,638 MB IOAccelerator (Metal buffers) + 1,460 MB Untagged (Unity native
  heap, where the CPU brick pool lives) + ~230 MB the rest**
  (`..._mem_peak_breakdown.txt`). `MODE=poses` uses the default render path
  (32-82 ms on the ground) and is not the walking number.
  **Per-owner ledger (`EXTRA=-memledger`, `MemoryLedger.cs`, run
  `Logs/tg2world_20261001_162217.txt`, peak 3,259 MB):** GPU -- mirror 635 MB,
  **fluid sim 549 MB (the Playground demo's 750k slots + 1,024 tiles)**,
  cascades 221 MB, far band 27 MB (1,436 of 1,586 MB IOAccelerator attributed);
  native -- CPU brick pool 488 MB, cascades 211 MB, far band 12 MB; managed
  arrays 260 MB (clipmap 148, cascades 46, fluid 43); managed heap 359 MB.
  **Pools are allocated at CAPACITY**, so fewer dense bricks (e.g. 3 m brick
  geology) saves memory only if the pool cap is then lowered to the measured
  need.
  **Striped strata replaced by thick depth layers (2026-10-01, owner):**
  mountain-pose dense bricks 849k -> 458-499k, deficit 870/322 -> 0, LRU
  evictions 26,656 -> 0 (`Logs/tg2world_20261001_171115.txt`); walk
  10.1-14.8 ms, worst window 11.3 frames/1000 > 33 ms. Footprint UNCHANGED
  (3,260 MB) as predicted -- the pool is still 1.0 M slots. Next lever: lower
  the pool cap to the measured need plus building headroom.
  **Pool lowered 1.0 M -> 750k (2026-10-01):** mountain poses still deficit 0,
  no evictions; walk steady state 2,931-3,005 MB (was ~3,250-3,330), with a
  one-off 3,141 MB spike during the initial fill (`Logs/tg2world_20261001_182253_mem.txt`).
  That run's frame times are CONTAMINATED: load average 46, a WebKit process at
  112% CPU; even all-air poses slowed. Re-measure on a quiet machine.
  **480 vs 960 internal, quiet machine, ABBA walk arms (2026-10-01,
  `Logs/tg2world_20261001_18{3250,3741,4224,4713}.txt`, 750k pool):**
  Performance (480) 12.0 / 12.2 ms mean of 30 s windows (10.0-15.8), <= 8.1
  frames/1000 over 33 ms; Quality (960) 31.5 / 33.9 ms (24.9-43.4), 135-980
  frames/1000 over 33 ms. 960 costs ~2.7x 480. The owner set 960 as the
  minimum internal resolution (game-design brief) -- at 960 the M1 walks at
  ~30 FPS, not 60.
  **Where the 960 walk's ~32 ms goes (2026-10-01, one arm each, -frametiming):**
  shadows off 22 ms (shadows ~10 ms), fluid off 29.5 ms (~2.5 ms); from 480 vs
  960 ~5 ms is fixed and ~27 ms scales with pixels traced. The 2.0-min window
  is the worst in every arm: a heavy spot on the walk circle, not drift. TAA +
  checkerboard proposal (owner approval pending):
  https://claude.ai/code/artifact/6745ee4a-4af7-482f-b07d-822e46270918
- **TERRAIN-AWARE BAND PLACEMENT (2026-10-01; owner: near cliffs drawn by the
  far band -- streaks, stepped edges).** The 7-layer band no longer just centres
  on the eye: `StreamManager.PlaceBand` scores the map's surface per chunk column
  within the load radius and shifts the band (centre in [cy-3, cy+2], camera
  layer and the one below always kept) toward the visible ground; centred
  underground (> 8 m under the surface) or falling > 4 m/s; switches need a
  1.25x better score. Same memory, same mirror box. `-centredband` restores the
  V2 rule for A/B; `VerticalStreamingTests` covers both. QA: the cliff that
  needed it (cliff_foot_2, band 46 -> 48) is all voxels in
  `FarBand/2026-10-01_213438_world`. A/B (`-centredband`,
  `FarBand/2026-10-01_234258_world`, repeat of `_231459`): cliff_foot_1/3 band 44
  both ways; cliff_foot_2 band 46 (deficit 308 at golden, dense 359539) vs 48
  (deficit 0, dense 442074) -- but every PNG, cliff_foot_2 golden and _band
  included, is BYTE-IDENTICAL (md5) to the AFTER run: the frame is ~99% voxels
  either way (a thin sky strip top-left, no far band, no streaks), so this pose
  does NOT show the fix; a pose whose visible cliff tops out above layer 49 is
  needed. TASK 04 (2026-10-02) tried one, `cliff_rise_2` (appended last, so the
  older poses keep their order): the cliff_foot_2 site. Its map top is 649.3 m.
  The centred band's top is 640.0 m and the shifted band's maximum top is 665.6 m.
  Try 1 stepped 40 m straight back to get a clear sight line. The ground
  there is 32 m higher (eye 624.2 m, layer 48), so the centred band is
  48 as well. No A/B is possible there, so BEFORE was not run
  (`FarBand/2026-10-02_092640_world`; band view ~90% voxels). Try 2 kept the
  eye in the foot's layer 46 and searched 8 directions x 0-60 m. No spot
  within 100 m of the top had a clear sight line to it, so the pose fell
  back to the foot, aimed at the top. AFTER `_095000` band 48, deficit 0,
  dense 442074. BEFORE (`-centredband`) `_100905` band 46, deficit 0, dense
  374876. All three cliff_rise_2 PNGs and the cliff_foot_2 golden PNGs are
  BYTE-IDENTICAL (md5) across the two runs. The frame is a dark-rock voxel
  slope (~85%) up to a convex crest, with sky above. The crest is below 640 m, and
  there is no far band, no streaks and no steps. So at this site, the cliff
  above layer 49 cannot be seen from the foot's layer. The shift loads 67k
  more dense bricks that nobody sees. The first AFTER run's cliff_foot_2
  golden read resident 685 / deficit 2462 (the ring was still filling after
  the jump; noon 3087/0). The first shot after a jump depends on timing.
  Pose dropped by the owner's call (2026-10-02, task 05a); the counts are the evidence.
  Cliffs rising > ~64 m above the
  eye still fall back to the far band -- its walls took the top texel's colour
  (streaks). FIXED 2026-10-02 (shader only, `Raymarch.compute`
  `FarWallMaterial`): a far SIDE face > 15 voxels (1.5 m) below its column
  top is shaded with the biome's layer-0 stone (grass/loam -> granite,
  sand/packed sand -> orange sandstone, snow/packed ice -> silver schist,
  jungle grass/mud -> karst limestone; BeachSand and rocks keep themselves);
  the top 1.5 m keeps the surface material (grass lips). Evidence
  (`FarBand/2026-10-02_001850_world` vs `_234258`): every `_band` view,
  cliff_foot_1/2/3 and ledge_view byte-identical; changed pixels in
  eye_level_2 golden 0.8% (green-dominant 3736 -> 487: the ridge's steep far
  face is now dark rock, crest grass kept), eye_level_3 0.19%, peak_from_2km
  0.06%. Only steep steps change: at F0 (0.8 m texels) a 30° slope steps
  ~0.5 m per column, inside the lip, so gentle far slopes keep their colour.
  Walk (Performance, `Logs/tg2world_20261002_003414.txt`): 30 s windows mean
  11.0 ms vs 12.2 ms at 184713 -- unchanged within noise.
  Pose QA lesson: cliff poses need the EXACT ground (texel 1); the 0.8 m sample
  put the eye inside rock (two pure-black frames).
  SLOPE RULE (task 03, 2026-10-02) -- NOT CHANGED, owner question open. The
  bake (`FarField` fill job) already applies the voxel rule:
  `DetailField.TopMaterial` with `MaxStep` at texel spacing, rock above 1.5.
  `FarSlopeRockReport` (opt-in, FARSLOPE=1) measured 3000 land texels per
  level on seed 42: the share of steep VOXEL columns (1-voxel step > 1.5,
  detail included) in a texel's footprint is 1-3% at texel slope 0.5-0.7
  (~30°), 20% at 1.0, crosses 50% at ~1.35 on F0, F1 and F2, and is 80%+ above
  1.9. The bake's 1.5 agrees with the voxel majority on 93% of texels; the best
  threshold (1.3-1.4) gains under 1 point. So green on a 30° far flank is what
  the voxels there show too. Turning it to rock means changing the VOXEL rule
  as well (~60% of land is steeper than 30°), which is an art/terrain decision.
- **The placeholder player can run up ~70° slopes** (3-voxel step-up on every
  blocked move, no slope rule, `PlayerMotor.MoveAxisWithStepUp`). Terrain can't
  be judged as an obstacle until the character is designed -- the owner does
  NOT want movement rules designed yet.
- **Phantom geometry: `ClipmapValidator.CountStaleSlots`** (commit `d60d986`)
  counts mirror positions with no resident chunk that still draw. First use: 1
  stale slot at one pose, still dirty (queued clear), 0 elsewhere. Not closed.

- **THE STANDARD GROUND `-sustained -motion orbit` PASSES THROUGH ROCK (found
  2026-09-25, NOT FIXED -- owner's call, it feeds historical comparisons).** The
  orbit runs at the arena pose's height (eye y = 5.7 m) on a 60 m radius; the
  terrain there reaches 12 m, so part of every arm is inside hills. Evidence:
  arms whose end-pose shot is pure black and whose shadow counters read "every
  pixel traced, 1.0 steps per ray". Arms end in or out of rock depending on
  frame pacing, which is a large part of that scenario's rep spread (3.8-5.8 ms
  measured). For an in-air comparison use `-orbitheight 8` (13.7 m, above the
  tallest terrain): its rep spread was <= 0.05 ms. `run-farband.sh`'s `low8`.
  **Decided 2026-09-25 (owner delegated):** the historical rig stays exactly as
  it is, so old comparisons stay valid; every NEW budget or frame-time claim
  also reports the lifted `-orbitheight 8` arm, and that arm is the one quoted.
- **Burst vs the EDITOR's Mono are NOT byte-identical for float code (found
  2026-09-25).** Probe: today's `ColumnSampleJob` differs on 1 of 262,144 columns
  (`BurstMonoIdentityProbeTests`); TerrainGen's S shows 1-3 ULP differences in
  plain multiply-add chains (`Logs/TerrainGen/determinism_diag.txt`). The
  existing "Burst == Mono" benchmark runs in the IL2CPP player, not the Editor.
  TerrainGen's gate is therefore Burst self-consistency (one compiled path for
  every authoritative evaluation), with the Editor-Mono rate reported.

- **EVERY PLAYGROUND PROBE HAS BEEN MEASURING INSIDE A HOLE. This is the biggest
  measurement defect found in this project so far.** `TeleportToArena` -- where
  every Playground-based capture mode starts -- puts the camera inside the dug-out
  arena facing a near wall, so most of the frame is occluded and rays die within a
  few voxels: **7.7 ms**. `-movespeed 12` then flies 360 m in 30 s and spends most
  of the run off the island over empty sky: **3.1 ms**. A player standing on open
  ground looking at the landscape measures **26.5 ms**. Both poses are
  photographed in `Screenshots/StageK/`. Consequence: **the DIFFERENCES reported
  by historical A/B arms remain valid (they compared like with like), but every
  ABSOLUTE frame time and every "fits the budget" conclusion in every document
  here is suspect until re-measured.** Use `-sustained -motion orbit` for anything
  that feeds a budget claim. **NOT FIXED** -- rewriting the historical rigs would
  invalidate load-bearing comparisons in `PHASE_4_COMPLETION.md` and
  `FLUID_*_RESULTS.md`, which is the owner's call. Full analysis in
  `PHASE_7_COMPLETION.md` §K.
- **TIER-0 CLIPMAP LOST UPDATES AFTER A TELEPORT OR FAST FLIGHT (found and
  FIXED 2026-09-25).** `TerrainClipmap.UploadDirty` uploads nearest-first; an
  evicted chunk and its replacement share a toroidal slot, so the replacement
  uploaded and THEN the eviction's clear wiped it: 57-94 resident chunks with
  empty GPU slots ("LOST 83,138" in the validator), seen as chunk-shaped holes
  and as a tier 0 -> 1 "pop" (the coarse tiers still held the chunk). The
  eviction clear now skips a slot a resident chunk owns. The Phase 4 rig never
  saw it because it traverses continuously and never teleports; `-lodpop`'s
  `MirrorDiagnostics` (after a teleport) is the check. Verified 0 LOST.
- **EVERY PHASE 4 ACCEPTANCE RUN AFTER PHASE 7 RENDERED BLACK (found and FIXED
  2026-09-25).** Phase 7's `LightBuffer` was bound only when a LightingDriver
  existed; the acceptance scene has none, Metal SKIPS a dispatch with an unset
  buffer ("Property (LightBuffer) ... is not set" in the player log), so the
  frame was black and "frame total" measured an empty frame (1.3 ms). Its CPU/
  buffer gates stayed valid. A dummy is bound now. **Always open the rig's
  screenshots** -- 53 PASS / 0 FAIL came with four pure-black PNGs.
- **THE PLAYGROUND HUD STALLED THE GPU PIPELINE EVERY ~7.5 FRAMES (found
  2026-09-25, FIXED in bf9aaba: async readback; >100 ms frames 646 -> 16).** The fluid panel
  calls `ReadSlotCounters` (a blocking `GraphicsBuffer.GetData`) every 15
  countdown ticks, and the countdown runs from `OnGUI`, which Unity calls
  twice a frame -- so every 7-8 frames the CPU waits for the whole GPU queue.
  Near the ground (~45 ms GPU frames) that is a ~150 ms hitch followed by two
  ~10 ms catch-up frames: THE DEMO'S STUTTER. `-flyfast` at 60 m/s, far band
  off, HUD on vs `-nohud`: frames > 100 ms 646 -> 19, p99 160 -> 83 ms, mean
  47.2 -> 45.6 (`Logs/flyfast_20260925_105003.txt`). Every `-sustained` arm
  defaults to HUD ON, so the per-frame TAIL of any arm before bf9aaba (not its
  A/B deltas) carries this.
- **THE INTERACTIVE PLAYGROUND NO LONGER RENDERS THE WAY THE RIGS DO (2026-09-26).**
  `RenderPreset.Default` is Performance: URP FSR 1.0 from 480x270 plus temporal
  shadow reuse (`-tshadow`), applied ONLY when no capture flag is present
  (`Playground.IsCaptureRun`). Every rig still runs the shipped 960 gate
  unless it passes `-renderpreset`/`-upscale`, so historical comparisons stay
  like for like -- but a number from a rig is NOT what a player sees, and vice
  versa. `run-perf60.sh` arms PP/PB/PPd measure the presets' own path. An
  upscaling preset swaps in an EMPTY URP volume profile (SampleSceneProfile's
  Bloom/Vignette/tonemap otherwise tints the frame grey once post is on for
  FSR). Measured: Performance 15.1-15.6 ms, 16.8 ms pitched at the ground,
  Balanced 19.7 ms, vs 55.2 ms shipped (`Logs/perf60_20260926_031103.txt`).
- **WHILE THE CAMERA MOVES, 8-10% OF FRAMES EXCEED 33 ms (found 2026-09-26, NOT
  FIXED).** Present at every preset. Not the fluid (`-nofluid`), not the HUD
  (`-nohud`). The one valid still-camera arm had the same mean and ~0 such
  frames, so translation (streaming) is the lead suspect, unproven. Only the
  per-30 s count exists today (`framesOver33` in sustained.tsv); isolate
  further (per-frame capture; far band off; streaming budget) before fixing.
  **`-motion spin`/`static` stay at the ARENA pose (orbitheight moves only the
  orbit centre), and the fluid demo buries that camera: 3 of 4 such arms ended
  pure black at "4 ms". Check the end-pose shot of any non-orbit arm.**
- **`FarField.GenToEngine` IS NOW THE CHUNK MATERIAL TABLE, not only the far
  band's.** V1 (`TerrainGenChunkSource`, sizeClass 2) maps every voxel through
  it. It had no case for `GenMat.Air` (the far band never passes one), so air
  became Stone and every generated chunk was solid to its top -- caught by
  `TerrainGenChunkSourceTests`. Any new GenMat needs a mapping there.
- **A SHORT COOLED ARM IS NO LONGER SUFFICIENT EVIDENCE FOR AN FPS CLAIM.** Use
  `-sustained`, which runs continuously for N minutes and reports rolling 30 s
  windows -- the curve is the result, not the endpoint. Thermal throttling was
  tested directly and is NOT a factor on this machine: 20 continuous minutes made
  it **5% faster**, `pmset -g therm` nominal across 12 samples. (`pmset`/
  `powermetrics` are OS utilities, not GPU profilers, and are not covered by the
  no-Xcode rule.)
- **§6.5's shadow fallback ladder does NOT work as a budget mechanism.** Measured
  at a representative pose: rung 0 (full soft) 25.9 ms, rung 1 28.1 ms (worse than
  rung 0), rung 2 (hard only) 25.1 ms, rung 3 (off) 16.5 ms. **Rungs 0/1/2 are
  within 3 ms of each other because the cost is TRACING THE RAY, not the soft
  edge.** Do not plan on stepping down a rung to recover frame time -- only "off"
  recovers anything. The lever that does work is internal resolution (Amdt 8.9):
  gate 560x315 holds 61 FPS with full shadows where 960x540 gets 37.7.
- **`ArgValue`/`ArgInt`/`ArgFloat` in `PlaygroundCapture` take the LAST match, and
  that is deliberate -- do not "fix" it back to first-match.** Runner scripts pass
  a default first and the per-arm override second (`--args -shadowlevel 0 "$@"`).
  First-match silently ignored the override, so an arm ran the DEFAULT while its
  label, log line and report all claimed otherwise. That produced a wrong
  "shadows cost nothing" reading that had to be retracted, and a gate-520 arm that
  was really gate 960.

- **EMISSIVE + MOBILE MATERIALS MUST NOT REGISTER AS POINT LIGHTS.** Lava is
  `IsFluid | IsEmissive`. §6.6's enqueue runs on every `SetVoxel`, so before
  2026-09-13 a lava flow registered thousands of lights and the §3.8 selection
  sorted all of them every frame -- **43.46 ms/frame against 9.32 ms
  pre-Phase-7**. `LightSourceBuffer.Register` now refuses mobile materials, and
  selection is partial (O(n x cap)) so no future emissive can make it quadratic
  again. §3.8's "a handful of placed torches, not a lit city" is a statement
  about PLACED torches -- do not apply it to every emissive material.
- **A CAMERA POSE CAN SIT INSIDE ROCK AND STILL "MEASURE" SOMETHING.** Found three
  times on 2026-09-24: Phase7CycleRig Gate 1's falloff poses (fixed, now a
  checked precondition), its `04_torch_through_wall` shot (not fixed), and
  Stage H1's lava-cavern "wide" pose (`cave + (-40,4,0)`; the carved spheres end
  at x = -34). A black frame reads as a very dark scene. Any new pose inside
  carved geometry should assert the camera voxel is air.
- **`-stutterprobe` flies in a straight line and leaves the world** -- see the
  measuring-inside-a-hole entry above; its absolute numbers are not
  representative. It also **does not pin the time of day** and the Playground clock runs
  (`dayLengthSeconds` 120). Its arms average over a day-night cycle, so shadow
  rays are skipped for part of every run. Fine for before/after comparisons
  where every arm shares it; **never** quote a rung cost from it -- use
  `-lightingab`, which pins golden hour.

- **`ShaderCompileCheck.CheckAll` CAN REPORT A GREEN SHADER THAT DOES NOT
  BUILD.** Found 2026-09-12: it printed `Raymarch.compute: OK (0 message(s))`
  and `DONE totalErrors=0` for a file the standalone build then refused with
  `undeclared identifier 'WaveActiveAnyTrue' ... (on metal)`. `ShaderUtil`'s
  message list comes from the **Editor's import-time compile**, which does not
  cover the player's Metal target. **Always also grep the build log for
  `Shader error`** — `run-phase7-rig.sh` does, and that grep is what caught it.
  Running `ShaderCompileCheck` remains worthwhile; treating it as sufficient is
  not.
- **SM6.0 wave intrinsics are NOT available to compute shaders here.**
  `WaveActiveAnyTrue`/`QuadReadLaneAt` fail on the legacy compiler ("undeclared
  identifier ... on metal") and, under `#pragma use_dxc`, fail again with
  "Subgroups are only supported in Metal 2.0 and up". Phase 7 uses a
  `groupshared` + `GroupMemoryBarrierWithGroupSync` equivalent instead. Raising
  the Metal/MSL floor would change every shader and the shipping macOS minimum,
  and is an owner decision — `PHASE_7_COMPLETION.md` §10 flag 7. **Do not
  re-attempt `use_dxc` without deciding that first**: it also changes the
  compiler for the whole shader, which would make every historical frame-time
  comparison carry a codegen confound.
- **FLAGGED FOR THE OWNER, NOT CHANGED: `run-acceptance-rig.sh` measures
  WINDOWED.** Neither the script, nor `Phase4AcceptanceRig.cs`, nor
  `Phase4Bootstrapper.cs` ever calls `Screen.SetResolution` or sets fullscreen
  (verified by grep 2026-09-12). `FPS_INVESTIGATION_RESULTS.md` §0.1 — written
  later — demonstrates windowed capture on this machine is worthless (69.6 vs
  26.9 FPS for identical back-to-back runs of the same binary, and "fluid ON"
  measuring *faster* than "fluid OFF"). That sits awkwardly beside Measurement
  discipline's "the ONLY trusted frame-time source is
  `./run-acceptance-rig.sh`'s output". **Nothing was changed**: making that rig
  fullscreen would silently invalidate every historical Gate C comparison in
  `PHASE_4_COMPLETION.md`, which is the owner's call, not a cleanup. The rule
  is left exactly as written; this note exists so the next session does not have
  to rediscover the tension.

- **The world/render-range mismatch REVERSED, and is now the opposite problem.**
  This entry used to read "the world is still Phase 3's original island... no
  larger sizeClass was ever added". That is STALE: `Content.cs` added
  `sizeClass 1` (additive per §D.2, sizeClass 0 untouched) scaling the island
  9.09x to a **954.5m coast radius, ~1909m across**. The window is no longer
  wider than the island — the island is now **6.6x wider than the renderer can
  draw**, because max render distance is 290m (`LODConfig.TIER_OUTER_RANGE_M`,
  itself derived from the window's corner half-diagonal). Flying up to see the
  island end-to-end shows a hard flat cutoff against sky. This is NOT a
  streaming or pool bug and not fixable by adding LOD tiers: the cascade is not
  a clipmap cascade — tiers 1/2 cover the SAME extent as tier 0, sampled
  coarser (`CascadeTierPool.cs:83-103`, and `Raymarch.compute:562-565` says so
  in a comment). Full analysis and four costed options in
  `AMENDMENT_8_11_RENDER_RANGE.md` (draft, not adopted).
- **Gate C is red on ONE upload-budget check: steady-state p99 (§4.3).**
  Measured 0.98-1.63ms against the 1.0ms budget, variable enough that whole
  runs pass (one run measured Gate C at 0.981ms and 51 PASS / 0 FAIL).
  **The peak-bytes/frame half of this is GREEN and has been since 2c0b999** --
  the byte-projection fix charges dense bodies and the packed air-mip to the
  budget before admitting a chunk, and every run since measures 2.74-3.00 MB
  against the 3.145 MB cap. Do not go looking for a peak-bytes failure.
  PROFILED 2026-08-31, read PHASE_4_COMPLETION.md §3.1 before touching this:
  the 2.09ms staging figure that used to motivate "optimise staging" is STALE
  (it predates the cascade leak fix). Current per-phase p99 is brick bodies
  0.29-1.15, staging 0.26-0.76, everything else under 0.25, and there is no
  dominant hotspot. Free-list defragmentation was tried against the measured
  fragmentation (runs/slots 0.224) and made the timing WORSE while improving
  fragmentation -- reverted, do not retry it. The write mechanism is settled
  and load-bearing; §0.2 forbids raising MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME.

- **`run-phase6-brushguard.sh` has been red since Playground was tiled, and it
  is the RIG that is stale, not the product.** 15 PASS / 15 FAIL as of
  2026-09-12 (was 16/14). Every failure
  runs through `Fluid.InRegion` or `Fluid.SphereFitsInRegion` — dense-path
  predicates. Under tiling `InRegion(v)` answers "is v in a RESIDENT TILE"
  (true only where fluid already is) and `SphereFitsInRegion` tests a 64³ box
  at world origin. `Playground.cs`'s own doc comment on
  `SphereFitsInWakeRadius` spells both traps out. The guard being tested is
  fine: mobile brushes outside §7.4's wake radius ARE refused, with a status
  line. Confirmed NOT caused by the 1024 tile cap — re-running with the cap
  reverted to 512 gives the identical 16/14. Fixing it means rewriting the
  rig's arena predicate in terms of the wake radius. Full analysis in
  `FLUID_TILE_CAP_RESULTS.md` §12.2.
  **The 15th failure appeared 2026-09-12 and is the same class, not new
  breakage:** restoring Playground's demo radius to the shipped 1280 voxels
  (128 m, up from a 128-voxel / 12.8 m value) means the point the rig flies to
  as "outside the arena" is now INSIDE the simulated region, so fluid there
  correctly simulates instead of freezing — and the assertion that expects the
  frozen bug.png signature therefore fails. The product behaves better; the
  rig's premise is stale.
- **`run-fluid-tiled.sh`'s two memory-budget assertions were REBASED on
  2026-09-11** when the tile cap went 512 -> 1024 (active set 264 -> 520 MB).
  Budget is now "< 544 MB" and the dense/tiled ratio bound is ">= 100x"
  (measured 252x, was 496x). Rebased with the cap/demand arithmetic recorded
  in the source next to the assertions, NOT loosened to pass: the old >400x
  bound required cap <= 639 while measured peak demand reached 779, so the
  assertion and the measurement were in direct conflict. The claim asserted is
  unchanged -- "orders of magnitude below the dense region" -- and the
  design's load-bearing claim, that the footprint does not move WITH THE
  RADIUS, is a separate assertion and was never in question. Rig is 19/19
  again. Background in `FLUID_TILE_CAP_RESULTS.md` §12.1.
- **FLUID IN TG2 DOES SIMULATE: task 07's "never moved" was a misreading (task
  09, 2026-10-03; no engine change).** `-fluidprobe` (`MODE=fluidprobe
  ./run-tg2world.sh`; `PROBEWORLD=` for the default-world control) repeats task
  07's placement and logs each stage. Water: 1029 voxels in a 7x21x7 box. Lava:
  784 voxels. Camera at (4600, 695.4, 4000).
  - **Every stage works in tg2.** The chunk's fluid-tile mask is set. The tiles
    are acquired (4 -> 12 of 1024, 0 pool exhaustions). The box is inside the
    mirror window (origin brick y 800, 128 bricks tall, so voxel y 6400-7423).
    The wake requests are dispatched, with 0 rejections and 0 released stale.
    The readback applies 52k ops, with 0 stale and 0 non-resident.
  - **CPU store**, voxels still in the box (tg2 vs default-world control):
    - water: 374 vs 345 at +45 frames, 0 vs 0 at +180.
    - lava: 296 vs 244 at +225, 184 vs 123 at +400.
  - **Why it looked frozen.** The CA ticks once per readback round trip:
    0.16 ticks/frame in tg2, 0.20 in the control. That is
    `FLUID_TICK_DIAGNOSIS.md`'s known 6-7 frames per tick (owner decision 11,
    still open). Lava also acts only every 6th tick (§7.4), so in 225 frames it
    takes ~6 steps (~0.6 m).
  - Task 07's own stills agree:
    - `fluid_b0` (+53 frames): the water column is mid-fall and sparse.
    - `fluid_c0` (+240 frames): the water is gone into the hole, and the lava
      column hangs only slightly lower.
    - The arena looked alive only because the chaos burst and vents pour
      continuously.
  - Logs: `Logs/tg2world_20261003_055038.txt` (tg2) and `_055801.txt`
    (control). Nothing to fix here beyond decision 11.
