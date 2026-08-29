# Voxel Engine — Claude Code Project Rules

## Ground truth, in order

1. `ARCHITECTURE_v8.6.md` is the base specification.
2. Amendments override it where they conflict, in this order: 8.8 (occupancy
   bitmask) → 8.9 (LOD & resolution) → 8.10 (LOD cascade performance).
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

## Current state (verify, don't trust this note)

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

## Known open issues (act on these, don't rediscover them)

- **The world is still Phase 3's original island.** `AnchorPlanner`'s island
  geometry hardcodes a ~22-chunk span for `sizeClass 0`, and no larger
  `sizeClass` was ever added, despite the streaming window (27x27 chunks,
  166m load radius) being sized for something bigger. The window is wider
  than the island, which is why traversal spends most of its time over open
  water. A frame-budget fix will not touch this. The correct fix is a new,
  larger `sizeClass` (additive, per §D.2) — do not mutate `sizeClass 0` in
  place, doing so invalidates every content hash already recorded against it.
- **Gate C is red on two related upload-budget checks**: steady-state upload
  p99 (measured ~3.4ms against a 1.0ms budget, §4.3) and peak bytes/frame
  (measured ~3.28MB against a 3.145MB cap, §0.2 — roughly a 4% overrun).
  CAUSE VERIFIED 2026-08-27, read the code before re-theorising: it is NOT
  the upload-exempt radius. In `TerrainClipmap.UploadDirty` only the clipmap
  ENTRY bytes are tested against `byteBudget` (line ~286), and that portion
  is bounded far below the cap by `MAX_CLIPMAP_CHUNKS_PER_FRAME` (16 chunks
  x 16KB = 256KB) anyway. The two larger payloads are added to
  `stats.bytesUploaded` AFTER the throttle loop has already made every
  decision, so neither is budgeted at all: `UploadDirtyBrickBodies` (dense
  512B bodies, the dominant term — megabytes when freshly-admitted chunks
  are dense) and the packed air-mip `SetData` (~300KB whenever
  chunksUploaded > 0). Exempt chunks do skip the budget test, but the dirty
  list is sorted nearest-first so they are processed while `bytes` is still
  near zero — they never push the total over. This is the
  natural first fix target once the world-size issue above is scheduled
  separately from it — they are unrelated causes and shouldn't be fixed in
  the same change.

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