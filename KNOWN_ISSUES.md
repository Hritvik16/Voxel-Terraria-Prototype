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
  needed. Cliffs rising > ~64 m above the
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
