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

## Session economy (owner on a usage-limited plan, 2026-10-01)

- **Fresh sessions over long ones.** Every turn re-sends the whole history. At a
  natural stopping point or a task switch, finish and commit the step, write
  `SESSION_HANDOFF.md` (done, evidence, state, next step, a ready-to-paste
  prompt), then START the fresh session yourself with `tools/new-session.sh`
  (a new tmux window seeded with that prompt) and tell the owner to switch to it
  and close this one. The owner is on the $20 Pro plan.
  A `UserPromptSubmit` hook (`tools/session-size-hint.sh`) adds this reminder
  automatically each time the transcript grows another 8 MB.
- **Unattended / overnight work goes in the queue**, one task per file in
  `tools/overnight/queue/`; `tools/overnight/run-queue.sh` (in tmux via `tools/overnight/start.sh`) runs each in its own
  fresh headless session (`--permission-mode auto`), passes
  `tools/overnight/HANDOFF.md` along, stops on `tools/overnight/PAUSE`, a
  failure or `STOP_FOR_OWNER`. When the owner says "pause builds", also `touch`
  that PAUSE file.
- **Cheap by default:** grep and targeted reads, not whole files; claude.ai docs
  and screenshots only at decision points; subagents only for self-contained
  searches whose result is a short summary.

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

## RIG SELECTION RULE (short; detail in KNOWN_ISSUES.md)

Identity/hash gates (Gates C/D) assume nothing but the test changes terrain.
With live fluid they fail BY DESIGN -- never loosen them. Persistence evidence:
`run-acceptance-rig.sh`; frame time under fluid: `run-acceptance-fluid.sh`.

## Known open issues -- INDEX (full text in KNOWN_ISSUES.md; read it first)

Before touching streaming, rendering, fluids, memory or a rig, or quoting a
number, read the matching entry in `KNOWN_ISSUES.md`. One line each:

- STREAMING BASELINE v3 (2026-10-02, task 10): `MODE=stream ./run-tg2world.sh`; generation throughput is the bottleneck (walk deficit ~2,700/3,087, chunk 350 ms on a worker vs 62 ms alone), upload ~1 ms; the fly pose draws no voxels (task 13).
- NEAR-TERRAIN SHIMMER BASELINE before TAA (2026-10-02): `MODE=shimmer ./run-tg2world.sh`; mean of poses near 4.14 / 5.84%, all 5.89 / 7.59%.
- TAA EXISTS, OFF BY DEFAULT (`-taa 1` / F4): task 07 risk checks -- fluid/edit/turns/seams pass, sun reset lowered to 0.25 deg/frame (0.2 deg/frame still lags ~3 luma), cost 0.8-2 ms at 480 px (budget ~1.5 ms not resolved).
- CHECKERBOARD EXISTS, OFF (`-checker 1`, implies TAA; task 08): walk at 960 30.8 -> 19.6 ms (-11.3 ms paired), 16.7 ms NOT reached (2.9 ms over); softer, horizon teeth in fast spins. Cost arms on battery at ~1% were wrong -- check `pmset -g batt`.
- FLUID IN TG2 DOES SIMULATE (task 09): "frozen" = 6-frame CA tick (decision 11) + lava every 6th tick; `MODE=fluidprobe`.
- THE TG2 WORLD IS OVER THE 3 GB CAP BEFORE THE PLAYER BUILDS ANYTHING (found 2026-09-26, NOT FIXED).
- TG2 AT WALKING HEIGHT (first measured 2026-10-01, `MODE=walk ./run-tg2world.sh`: `-motion walk`, a 150 m circle round the spawn at 1.6 m eye...
- TERRAIN-AWARE BAND PLACEMENT (2026-10-01; owner: near cliffs drawn by the far band -- streaks, stepped edges).
- The placeholder player can run up ~70° slopes
- Phantom geometry: `ClipmapValidator.CountStaleSlots`
- THE STANDARD GROUND `-sustained -motion orbit` PASSES THROUGH ROCK (found 2026-09-25, NOT FIXED -- owner's call, it feeds historical comparisons).
- Burst vs the EDITOR's Mono are NOT byte-identical for float code (found 2026-09-25).
- EVERY PLAYGROUND PROBE HAS BEEN MEASURING INSIDE A HOLE. This is the biggest measurement defect found in this project so far.
- TIER-0 CLIPMAP LOST UPDATES AFTER A TELEPORT OR FAST FLIGHT (found and FIXED 2026-09-25).
- EVERY PHASE 4 ACCEPTANCE RUN AFTER PHASE 7 RENDERED BLACK (found and FIXED 2026-09-25).
- THE PLAYGROUND HUD STALLED THE GPU PIPELINE EVERY ~7.5 FRAMES (found 2026-09-25, FIXED in bf9aaba: async readback; >100 ms frames 646 -> 16).
- THE INTERACTIVE PLAYGROUND NO LONGER RENDERS THE WAY THE RIGS DO (2026-09-26).
- WHILE THE CAMERA MOVES, 8-10% OF FRAMES EXCEED 33 ms (found 2026-09-26, NOT FIXED).
- `FarField.GenToEngine` IS NOW THE CHUNK MATERIAL TABLE, not only the far band's.
- A SHORT COOLED ARM IS NO LONGER SUFFICIENT EVIDENCE FOR AN FPS CLAIM.
- §6.5's shadow fallback ladder does NOT work as a budget mechanism.
- `ArgValue`/`ArgInt`/`ArgFloat` in `PlaygroundCapture` take the LAST match, and that is deliberate -- do not "fix" it back to first-match.
- EMISSIVE + MOBILE MATERIALS MUST NOT REGISTER AS POINT LIGHTS.
- A CAMERA POSE CAN SIT INSIDE ROCK AND STILL "MEASURE" SOMETHING.
- `-stutterprobe` flies in a straight line and leaves the world
- `ShaderCompileCheck.CheckAll` CAN REPORT A GREEN SHADER THAT DOES NOT BUILD.
- SM6.0 wave intrinsics are NOT available to compute shaders here.
- FLAGGED FOR THE OWNER, NOT CHANGED: `run-acceptance-rig.sh` measures WINDOWED.
- The world/render-range mismatch REVERSED, and is now the opposite problem.
- Gate C is red on ONE upload-budget check: steady-state p99 (§4.3).
- `run-phase6-brushguard.sh` has been red since Playground was tiled, and it is the RIG that is stale, not the product.
- `run-fluid-tiled.sh`'s two memory-budget assertions were REBASED on 2026-09-11

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