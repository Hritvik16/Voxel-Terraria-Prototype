# Voxel Engine — Claude Code Project Rules

## Ground truth, in order

1. `ARCHITECTURE_v8.6.md` is the base specification.
2. Amendments override it where they conflict, in this order: 8.8 (occupancy
   bitmask) → 8.9 (LOD & resolution) → 8.10 (LOD cascade performance) →
   8.12 (Phase 7's substitute budget gate).
   **Note: no `.md` exists in this repo for amendment 8.8, or for 8.7.** Both
   are in force and implemented (`OccupancyMask.cs`, `AirMip*.cs`) and both are
   cited by the completion docs, but the documents themselves are absent and
   `git log` shows neither was ever deleted. Read the implementations.
   `AMENDMENT_8_11_RENDER_RANGE.md` exists but is **DRAFT, NOT ADOPTED**.
3. `PHASE_1/2/3_COMPLETION.md` are historical records of what was proven and
   how — useful for *why* something is shaped the way it is, never a source
   of current requirements. If a completion doc and the architecture doc
   disagree, the architecture doc wins.
4. When a rule below and the spec disagree, the spec wins. These rules are a
   distillation of consequences that already came up in practice, not a
   replacement for reading §0 (the Engine Constitution) and Appendices A/B
   (struct layouts) yourself when a task touches memory layout or the state
   machine. Cite the section you're relying on when you make a design choice,
   not just when asked.
5. **There is NO game-design document.** `ARCHITECTURE_v8.6.md` is the ENGINE
   architecture only; it defers objects/NPCs/towns/combat to "the second
   document" (its §5.4, and the at-a-glance cut list), which was never written.
   Do not cite it for game design. The only written game intent is
   `DESIGN_LARGE_WORLD_AND_TERRAIN_OVERHAUL.md` §2 (terrain, view distance,
   palette) plus the owner decisions recorded in its §12-15 and in the
   2026-10-01 review linked below. **The game-design brief (started
   2026-10-01, built from the owner's answers) is now the design authority:**
   https://claude.ai/code/artifact/d31b4321-77c3-4026-93d2-460c190950bf --
   its "Decided" rows bind terrain and placement work; "Leaning" and "Open"
   rows do not. The owner prefers to be asked specific questions in batches
   rather than to write long explanations.

## Current state (verify, don't trust this note)

Phases 1-7 are CLOSED and merged (Phase 7: `9b3ee50`, 2026-09-24). The
large-world / terrain-overhaul work -- TerrainGen v3, the far band, and the
walkable sizeClass 2 island (`-world tg2`), design doc §12-15 -- was merged to
`main` on 2026-10-01. EditMode is `PASS 625 FAIL 0 SKIP 2` (2026-10-01; skipped:
the report-only PaletteSceneExport and the world-tree test, parked while the
tree is off). `./run-phase7-rig.sh` last reported `PASS 32 FAIL 0` (2026-09-24).
Phase 7's §10 flags (shadow rung, torch-through-wall, lava light) are still the
owner's calls.

**Where the project is (2026-10-01).** Owner review:
https://claude.ai/code/artifact/7443de71-6716-4f99-869b-98d3c6814fd7 . The tg2
terrain looks good but is not shaped for play (seed 42: median slope 30°, 73% of
land "mountainous", 4% open; only the plains are gentle) and the owner wants it
to play well. The recommended next step is a short game-design brief (world
structure, planned movement abilities, progression, settlements, fluids,
signature features, placement), then a rebuild of the surface around play
(that report's "direction B"). **Do not start new terrain-generator work until
the owner picks a direction.** Owner intent recorded so far: minimum spec M1
8 GB; 3 GB process cap; long render distance; natural-looking but NOT bound to
realistic topography (nothing sticking out); biomes and landform regions are
INDEPENDENT maps (snow beside desert is intended), biomes should be LARGER and
not every biome x landform pair need appear in every world; object placement
will later read the pair from a "world record"; many towns; progression as fun
as Terraria's, carried by the overworld; spawn not decided (`Tg2Spawn` is a
Playground placeholder); the player character is a placeholder (don't design
its movement rules yet -- only a list of planned abilities is wanted).

Phase 4 (streaming & persistence) is functionally correct: all EditMode tests
pass, admission/eviction/persistence gates are green. Current work is
throughput and frame-time, not correctness. Don't assume this paragraph is
still accurate — run the EditMode suite before starting anything, and check
`git log` / the latest `Phase4Acceptance/*/phase4_report.txt` for the real
current numbers before reasoning about what's slow.

## Hard invariants

- The CPU is the sole terrain writer (`ChunkStore.SetVoxel`). The GPU mirror
  (`TerrainClipmap`, cascade tiers) is written FROM the CPU state, never the
  reverse.
- Coordinate math is bitwise integer (`>>`, `&`), never float division or
  modulo. Window/ring dimensions must be powers of two — a non-power-of-two
  doesn't fail loudly, it aliases silently. This exact bug (`§6.2 phantom
  terrain`) has already happened once in this codebase.
- Single-writer: only the main thread mutates `ChunkStore`, the pool
  allocators, and the sparse chunk table. Worker threads write only to
  scratch storage they exclusively own. If you find yourself reaching for a
  lock anywhere in the streaming path, that's a sign the single-writer rule
  was about to be violated — stop and restructure instead.
- Public APIs freeze once their phase's tests pass (`IWorldQuery.GetVoxel`,
  `IEditService.SetVoxel`, the D.1 delta format, the §4.4 state-transition
  table). Bug fixes inside a frozen system are fine; changing its signature
  or contract is a redesign and needs to be flagged as such before doing it.
- Never attribute a logic or coordinate bug to "floating point precision."
  This codebase's coordinate math is integer; trace the actual bitwise shift
  or mask instead.

## Measurement discipline

- Don't report a performance number, a "this should work now," or a fix as
  confirmed without evidence. Run `./run-acceptance-rig.sh` yourself and read
  its output before claiming anything about frame time changed.
- The ONLY trusted frame-time source is `./run-acceptance-rig.sh`'s output —
  a RELEASE standalone build, launched outside the Editor, reporting its own
  `Time.unscaledDeltaTime`-based figures. This gives magnitude and direction
  (faster/slower, by how much) but not GPU-stage attribution (no Metal
  capture in this workflow) — that's an accepted, deliberate trade, not an
  oversight.
- Editor Play-mode numbers (the Editor's own Stats/Profiler overlay) are
  NEVER performance evidence, for any reason, even "just to sanity check
  quickly." This is not a softer version of the standalone-build rule — it
  produced numbers off by 10-40x early in this project (0.8 FPS in Editor
  Play vs ~20 FPS in a real build, same code) because of Editor-only
  overhead layered on top of whatever's actually slow. If you're tempted to
  reason about performance from anything other than the rig's own printed
  report, stop and run the script instead.
- After a change intended to affect performance, run the full rig once,
  report the specific numbers that moved (and by how much) against the
  previous run, and STOP for the user to look before making another
  performance-affecting change. Do not chain multiple unverified
  performance changes and run the rig only at the end — several real bugs
  in this project (an uninitialized GPU buffer, an unbudgeted per-frame
  pass, a dropped-chunk admission bug) shipped together in single "fix
  everything" batches and took multiple extra round-trips to untangle
  because three things changed between measurements instead of one.
- When diagnosing a slowdown, isolate the cause before proposing a fix if
  there's any way to (split a combined timer into its phases, disable one
  code path at a time, suppress just the GPU writes and re-measure). Prefer
  "let's find out" over "let's guess and check."
- Keep "correctness proven" (EditMode tests), "performance proven" (a rig
  run), and "not yet tested" distinct. Don't blur them into "it works."

## Working style

- Before a change to the architecture itself (not an implementation bug fix),
  check whether there's actual evidence of a design problem, or whether this
  could still be a bug in a sound design. Most slowdowns found in this
  project turned out to be bugs, not design flaws.
- When a change touches more than a couple of lines, write the complete file
  or a complete, verbatim-anchored patch — never a fragment the user has to
  hand-merge. A mis-anchored patch has already caused a duplicate-definition
  compile error once in this repo.
- Before editing or patching any file, read its current on-disk content —
  don't reuse a description of it from earlier in the session. Files here
  get hand-edited between agent sessions.
- Push back if a proposed shortcut isn't supported by evidence. Say so
  directly, the way you would in code review.

## Known landmines in this specific repo

- Everything under `Assets/CoreEngine/Mirror/` except `CascadeTierPool.cs`
  and `LODCascadeManager.cs` is namespace `VoxelEngine.Memory`, not
  `VoxelEngine.Mirror`, despite the folder name. `AirMip`, `AirMipData`,
  `PackedMips` all live in `VoxelEngine.Memory`. Check the actual
  `namespace` line, never infer it from the directory.
- `WINDOW_CHUNKS_Y` (the CPU `ChunkStore` ring height, currently 16) and
  `MIRROR_CHUNKS_Y` (the GPU mirror height, currently 4) are intentionally
  different. Several oracle test files (`RaymarchOccupancyTests`,
  `RaymarchMipTests`, etc.) build their own synthetic `ChunkStore` windows
  up to 16 chunks tall and will break if `WINDOW_CHUNKS_Y` is lowered
  without first updating every one of those construction sites.
- `MAX_GENERATED_CHUNK_Y` (currently 0) bounds what actually streams;
  `WINDOW_CHUNKS_Y` bounds ring *size*. Keeping them separate is what makes
  a tall ring affordable — collapsing them back together was a real
  regression once.

## ONE UNITY PROCESS AT A TIME — the shared build lock (owner rule, 2026-09-24)

This 8 GB Mac must never run two Unity builds, test runs or players at once.
The sibling session in `../VoxelTerraria_compare` shares the lock
`/tmp/voxel-unity.lock` and has agreed to honour it. Before ANY batchmode
build, EditMode run or player launch: `source tools/unity-lock.sh` and call
`unity_lock_acquire` (waits for the lock and 45 s with no Unity/il2cpp/player,
takes it, releases on exit). Newer runner scripts already do; older ones
(`run-editmode-tests.sh`, `run-phase7-rig.sh`, `run-acceptance-rig.sh`, ...)
do NOT — wrap them: `tools/with-unity-lock.sh ./run-editmode-tests.sh`. Never
put `pgrep -f '<pattern>'` inline in a shell command: it matches that shell
itself and deadlocks the queue (happened twice). **Never edit a `run-*.sh` while
it is running** — bash reads scripts incrementally and the edit shifts what it
executes next (happened 2026-09-24; harmless that time).

## Commands

Run the EditMode suite before and after any change:

```
./run-editmode-tests.sh
```

It prints a `PASS n FAIL n SKIP n` line and exits non-zero on any failure.
Close the Editor first — a running Editor holds the project lock and the
batchmode run will fail.

If you invoke Unity directly instead, the equivalent is:

```
/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics \
  -projectPath . \
  -runTests -testPlatform EditMode \
  -testResults TestResults.xml \
  -logFile unity_test.log
```

**Do NOT add `-quit` to that command.** With `-runTests`, `-quit` shuts the
editor down as soon as the initial asset refresh finishes, BEFORE the test
runner starts. You get a clean-looking log ending in "Exiting batchmode
successfully", no `TestResults.xml`, and zero tests run — which reads as a
compile failure and isn't one. The test runner terminates the editor itself
when it is done. (`-quit` IS correct for `-executeMethod` builds, e.g.
`run-acceptance-rig.sh`'s build step — it only breaks `-runTests`.)

Then read `TestResults.xml` for pass/fail counts and `unity_test.log` for
compile errors — a compile error produces zero test results, not a failing
suite, so check the log first if the XML is empty or missing. Confirm the log
is actually free of `error CS` lines before concluding "compile error": a
missing XML with zero `error CS` hits means the runner never ran, not that
the code is broken.

For anything performance-related, run the full build-and-rig loop instead —
this builds a release standalone player, launches it, waits for the rig to
run to completion and quit itself, then prints `phase4_report.txt` directly:

```
./run-acceptance-rig.sh
```

This takes several minutes (build + window fill + traversal legs + soak) —
it is not a quick check, don't re-run it more than once per real change.
Screenshots from the run are listed at the end of its output; `Read` them
directly by path if you need to look rather than just read the numbers.

Never launch the Phase 4 scene from inside the Editor and report on its
frame time — see Measurement discipline above.

**Phase 7 (lighting) has two rigs, and they answer different questions:**

```
./run-phase7-rig.sh            # CORRECTNESS. §13's acceptance gates.
./run-phase7-lighting-ab.sh    # FRAME RATE. The substitute budget gate.
```

`run-phase7-rig.sh` asserts and exits; it quotes no frame time against any
budget, deliberately. `run-phase7-lighting-ab.sh` measures end-to-end sustained
rate across §6.5's four shadow rungs — the full matrix is ~40 launches at ~360 s
each (about four hours), reducible via `ARMS`/`SCENARIOS`/`GATES`/`REPS`, and
the report names which arms actually ran. **Read
`AMENDMENT_8_12_PHASE7_GATE.md` §3.2 and §3.3 before quoting anything it
produces** — §13's own gates are per-pass ms with Performance State checked and
are unrunnable in this workflow.

Both write their logs to `Logs/`, not the repo root.

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

## The build-run-review loop

When asked to iterate toward a stable build, follow this exact procedure
rather than inventing your own definition of done:

1. Run `./run-editmode-tests.sh`. Anything red here stops the loop —
   correctness always comes before performance.
2. Run `./run-acceptance-rig.sh`. Poll it; don't block synchronously, and
   don't call it stalled before ~5 minutes of genuine silence in both the
   build log and the player log (see Measurement discipline above).
3. Read the ENTIRE `phase4_report.txt`, not a subset of it. Specifically:
   report the **"frame total p50/p99"** line from Gate C's upload-phase
   breakdown as the frame-time number that matters. Do NOT report Gate B's
   isolation-probe number as if it were the same thing — that one measures
   a pinned, non-traversing window and understates real cost by a lot.
4. `Read` (actually open, not just list) at minimum: the first Beauty
   screenshot, the last `Traverse_*` still, and the Gate C/D end
   screenshots. Describe what's actually in frame — terrain vs. water
   fraction, horizon shape, anything floating or missing — before drawing
   any conclusion. A report with 0 FAIL and a screenshot that's 90% ocean
   are not in conflict; they measure different things, and only looking at
   both tells you that.
5. Compare against the previous run's report (the next-most-recent
   timestamped folder in the same output directory). State explicitly what
   got better, what got worse, what's unchanged.
6. Decide on exactly ONE change from everything above. Name the
   architecture section or known-issue bullet it addresses.
7. Return to step 1.

**Definition of "stable"** (the actual stopping condition, so this isn't
open-ended): every gate that runs reports 0 FAIL, load deficit is 0
throughout traversal, peak upload bytes/frame is under budget, and the
screenshots reviewed in step 4 show mostly terrain in frame with no floating
geometry, holes, or an obviously undersized world. Reaching this is very
unlikely to be one change — say so after each iteration instead of promising
the next one will finish it.

**Stop and report after every iteration**, even mid-plan. Do not chain more
than 3 iterations unsupervised without a human checkpoint. This project has
repeatedly lost more time to batches of coupled, unverified changes than it
ever saved by skipping a checkpoint — the cost of pausing to report is much
smaller than the cost of untangling three simultaneous changes after the
fact.