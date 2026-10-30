# Voxel Engine — Claude Code Project Rules

## Ground truth, in order

1. `ARCHITECTURE_v8.6.md` is the base specification.
2. Amendments override it where they conflict, in this order: 8.8 (occupancy
   bitmask) → 8.9 (LOD & resolution) → 8.10 (LOD cascade performance) →
   8.12 (Phase 7's substitute budget gate) → 8.14 (LOD shells, edit-op log,
   smaller 0.1 m window, quality presets; `AMENDMENT_8_14_LOD_SHELLS.md`,
   adopted 2026-10-11) → 8.15 (simulation reach: NEAR 128 m on every machine,
   only the DRAWN side shrinks; `AMENDMENT_8_15_SIMULATION_REACH.md`, adopted 2026-10-15) →
   8.16 (fluid gravity fall: free fall ungated by viscosity, same for every fluid;
   `AMENDMENT_8_16_FLUID_FALL.md`, DESIGN CHANGE owner-approved 2026-10-15).
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

**Order of future work: `ROADMAP.md` (owner, 2026-10-08) overrides older future plans.**
Engine capability + performance (lighting, LOD shells, resolution, streaming, fluids) come
BEFORE the player and placement.

**engine-phase merged to `main` on 2026-10-22** (task 109, merge `a5f6440`); work since then continues
on the `engine-phase` branch. **Now: the owner's quality pass (2026-10-22)** -- performance (stable 30 fps),
lighting, world gen, visuals BEFORE populating the world; crafting / the core loop come after. Task 146's
30 fps milestone was NOT met (walk p50 44.7 ms); `PERF_30FPS_PLAN.md` / `PERF_SHELL_MARCH_PLAN.md` drive the
cuts. Fast suite `PASS 948 FAIL 0 SKIP 15` (2026-10-30, task 153). Queue and open owner questions:
`SESSION_HANDOFF.md`, `tools/overnight/HANDOFF.md`. Older dated state notes (Phases 1-7, the surface
rebuild, the 2026-10-01 owner review and owner intent, the Phase 4 note): `PROJECT_HISTORY.md`.

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
- **Measure performance only in performance tasks** (owner, 2026-10-08). Feature,
  terrain, visual and bug-fix work is accepted on tests, metrics reports and
  screenshots; no stream/rig/frame-time arms. Performance is checked at milestones.
  Before any measurement, write what each possible result would make us do; if every
  outcome leads to the same action, skip the measurement and do the action.
- **Performance is quoted at WARM STEADY STATE** (owner, 2026-10-04, Q20 (a)). This Mac is
  a fanless M1 Air: a warm chip runs chunk generation up to ~2.2x slower (tasks 27/28), and
  4+ workers heat it within ~100 s even from cold. So every timed measurement (stream probe,
  acceptance rig, frame-time A/B) starts with ONE warm-up launch whose numbers are discarded
  (`MODE=stream MOTION=walk ./run-tg2world.sh`, ~6 min), then runs the measured arms back to
  back (no idle gap over ~2 min; interleave A and B, e.g. ABBA). Report "warm steady state"
  and the warm-up's log path. Cold numbers may be shown beside them, never as the result.

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
- **A new session opens by orienting the owner** (owner, 2026-10-08: "so I don't
  feel so lost"). The handoff starts with a `## For the owner` section written in
  plain words: why the switch happened (size, task switch, ...), a short summary of
  the previous chat (what the owner asked, what was decided, what changed, commits),
  what is running now, and what is still open or waiting on the owner. The new
  session's FIRST message relays it, plus which terminal tabs / tmux windows to
  close. Only then does it start working.
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
takes it, releases on exit). Every kept/updated runner does (task 148 added it to
editmode, phase7, acceptance, acceptance-fluid, fluid-tiled, 5b, 5d, brushguard, combined);
the to-be-retired/merged old rigs do NOT — wrap those: `tools/with-unity-lock.sh ./run-x.sh`. Never
put `pgrep -f '<pattern>'` inline in a shell command: it matches that shell
itself and deadlocks the queue (happened twice). **Never edit a `run-*.sh` while
it is running** — bash reads scripts incrementally and the edit shifts what it
executes next (happened 2026-09-24; harmless that time).
**Owner pause (tasks 14/14b):** `tools/heavy.sh pause` DRAINS: what is running
finishes, new builds/tests/launches wait (HOLD `/tmp/voxel-unity.hold`) until
`resume`; `status` shows the lock owner, waiters, "draining: N" then "idle".
Nothing is ever signalled. Agents keep running. Rigs print `NOTE: owner paused
during this run` -- the Mac was likely in use: rerun that arm.

## Commands

To PLAY the newest world (tg2 on map v4 + far band, interactive preset, keys listed):
`./run-play.sh` (`MAPV=3` old generator, `BUILD=1` rebuild first; holds the lock).
Play starts with FSR 3 dynamic (task 52, owner Q38 (a): 1080p from 540-720 rows);
`EXTRA="-upscaler none|taa|fsr1|stp|fsr3 -renderrows 540|720|dyn"` or `-renderpreset quality`
override it. Rigs and capture modes are unchanged (no upscaler unless they pass the flags).
F6 in play opens the settings menu (task 58): saved to `PlaySettings.json` beside
`PlayerConfig.json`, flags override it per field for one run; rigs/capture runs (and `-fluidprobe`) never read it.
Per-task feature notes (tasks 64-105+: survival, movement, rivers, see-through liquids, shells, far edits/ops,
edit summaries, GPU/CPU pools, fluid fall, world state, far query, tool tiers; their flags, tests and `EXTRA="-...demo"` shots)
live in `TASKS.md`, one paragraph each. `grep -n 'Task <N>\|<flag>' TASKS.md` before touching a feature; new tasks append there.

Run the FAST EditMode suite before and after any change, and the WHOLE suite
before any merge to `main` and on every terrain-generator change
(`Assets/TerrainGen/`):

```
./run-editmode-tests.sh             # fast: excludes [Category("Slow")], ~3 min
SUITE=all ./run-editmode-tests.sh   # whole suite, 630 cases, ~9.5 min
REPORTS=1 ...                       # also the report-only tests (opt-in)
```

It prints a `PASS n FAIL n SKIP n  suite=fast|all  wall=..` line and exits
non-zero on any failure; the fast suite also says how many cases it left out
(counted against the last `SUITE=all`'s `TestResults_all.xml`). Tag a test
`[Category("Slow")]` when it takes > ~10 s. Report-only tests (assert nothing)
call `ReportOnly.Require()` and are Ignored without `REPORTS=1`. TerrainGen
tests take maps from `SharedMaps.Get(spec)` (one build per spec per run) --
never Dispose or write into a shared map.
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

- GPU fluid CA read terrain from the tier-0 mirror box (93e; FIXED, Q55 (b): fluid terrain directory; 93f maps its bodies).
- Shells cost +10-15 ms steady, shell 3 unfilled while walking (93b; NOT FIXED; cut by 99/132/134; shells ON in play, 93c).
- Owner pause (`tools/heavy.sh`) DRAINS, it does not freeze (14/14b): rerun an arm that prints "owner paused".
- Stray resident chunks aliased into the mirror (21; FIXED 26; 32 parks out-of-box chunks).
- Black bar over the sea at `coast_from_sea_30m` (19; FIXED 22: integer-cell DDA in `FarStartT`).
- Streaming requirement, worst case (16b): 60 m/s dive needs 2,285 chunks/s, capacity 2-29/s; NOT MET (24's `-detailspeed`).
- Streaming baseline v3 (10): generation throughput is the bottleneck; `MODE=stream ./run-tg2world.sh`.
- Near-terrain shimmer baseline before TAA (05): `MODE=shimmer`.
- TAA exists, OFF on the preset path (06/07; `-taa 1`); play uses FSR 3 dyn (52).
- Checkerboard exists, OFF (08; `-checker 1`): -11.3 ms, 16.7 ms not reached; battery arms were wrong.
- "POOL FULL" in tg2 (39; FIXED in Playground by 54 awake-only tiles; 55 pool 1,024 -> 480).
- Fluid in tg2 does simulate (09); tick is fixed-rate since 91, 6.5 Hz + adaptive drain (97).
- The tg2 world is over the 3 GB cap before the player builds anything (2026-09-26, NOT FIXED).
- tg2 at walking height: `MODE=walk` baseline and memory peak (2026-10-01).
- Terrain-aware band placement (`StreamManager.PlaceBand`, 2026-10-01).
- Placeholder player ran up ~70° slopes (FIXED in play by 93f1; the legacy rig `Step` is unchanged).
- Phantom geometry counter `ClipmapValidator.CountStaleSlots` (`d60d986`; not closed).
- Standard `-sustained -motion orbit` passes through rock (2026-09-25, NOT FIXED, owner's call).
- Burst vs the Editor's Mono are NOT byte-identical for float code (2026-09-25).
- Every Playground probe measured inside a hole (biggest measurement defect so far).
- Tier-0 clipmap lost updates after a teleport or fast flight (FIXED 2026-09-25).
- Every Phase 4 acceptance run after Phase 7 rendered black (FIXED 2026-09-25).
- Playground HUD stalled the GPU every ~7.5 frames (FIXED `bf9aaba`, async readback).
- The interactive Playground no longer renders the way the rigs do (presets; 2026-09-26).
- Temporal shadow reuse showed the neighbours' shadows, "shadow dots" (FIXED task 56).
- While the camera moves, 8-10% of frames exceed 33 ms (2026-09-26, NOT FIXED).
- `FarField.GenToEngine` is the chunk material table: any new GenMat needs a mapping.
- A short cooled arm is no longer sufficient evidence for an FPS claim.
- §6.5's shadow fallback ladder does NOT work as a budget mechanism.
- `ArgValue`/`ArgInt`/`ArgFloat` in `PlaygroundCapture` take the LAST match -- deliberate, do not "fix".
- Emissive + mobile materials must NOT register as point lights.
- A camera pose can sit inside rock and still "measure" something.
- `-stutterprobe` flies in a straight line and leaves the world.
- `ShaderCompileCheck.CheckAll` can report a green shader that does not build.
- SM6.0 wave intrinsics are NOT available to compute shaders here.
- FLAGGED, NOT CHANGED: `run-acceptance-rig.sh` measures WINDOWED.
- The world/render-range mismatch reversed (now the opposite problem).
- Gate C is red on ONE upload-budget check: steady-state p99 (§4.3).
- `run-phase6-brushguard.sh` red since Playground was tiled: the RIG is stale, not the product.
- `run-fluid-tiled.sh`'s two memory-budget assertions were REBASED on 2026-09-11.

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