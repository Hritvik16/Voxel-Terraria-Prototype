## YOUR TASK: 10-streaming-perf-baseline

Approved by the owner 2026-10-02 (in chat with the supervisor): "when we do the
terrain overhaul we need to track things like how long it takes for a chunk to
load on the cpu, travel to the gpu ... right now it looks like the terrain is
taking a while to load. I can see the lod changing right around us as the camera
moves. So we need to make sure the redesign takes into account performance."

This is the v3 (today's terrain) BASELINE that every surface-rebuild step (B1
onward) will be compared against. Measurement only: no behaviour change, no
tuning, no fix. Instrumentation (timers, report lines) is allowed if it does not
change what is generated, uploaded or drawn (prove it: a `MODE=poses` md5 check
against the previous commit, as task 06 did, or explain any difference).

Measure on the tg2 world (`./run-tg2world.sh`), walking (`MODE=walk`) and in a
fast straight flight that stays over the island (NOT `-stutterprobe`, which
leaves the world -- see KNOWN_ISSUES), and report, per stage, p50 / p95 / max:
1. CPU: time to generate one chunk (worker time; existing chunk cost reporting
   in `TerrainGenChunkSourceTests` / `StreamManager` -- reuse, do not duplicate),
   and chunks generated per second with the worker count used.
2. Main thread: time to admit a generated chunk into `ChunkStore`.
3. CPU -> GPU: time from admission to the chunk being in the GPU mirror
   (`TerrainClipmap` upload), bytes uploaded per frame vs the §4.3 budget.
4. Time to visible: from the moment a chunk is first REQUESTED to the first
   frame it is drawn at full detail, and how far from the camera that happens.
5. Load deficit over time (chunks wanted but not resident) while moving.
6. The LOD hand-offs the owner sees moving round the camera: at what distance
   each detail level changes (near voxels / cascades / far band), and whether
   what is visible is SLOW LOADING (a deficit, chunks arriving late) or a
   BOUNDARY BY DESIGN (rings that follow the camera even when fully loaded).
   Screenshots: a still pose with the rings fully loaded, and the same pose
   mid-flight, crops at the hand-offs, paths in HANDOFF.

Also answer in HANDOFF, from the code (cite files), in 3-5 lines: on this Mac's
unified memory, what does "upload to the GPU" actually cost here -- a copy, a
format conversion (occupancy masks, mips), or both -- and which share of
stage 3 is which. No change, just the answer with numbers.

Write the numbers as a short table at the top of a new KNOWN_ISSUES entry
"STREAMING BASELINE v3 (2026-10-02)" and a one-line index entry in CLAUDE.md.
Rules: shared Unity lock; frame times only from standalone builds via the rig
scripts; one run per arm is not enough for a frame-time claim (KNOWN_ISSUES: "A
SHORT COOLED ARM IS NO LONGER SUFFICIENT") -- report spread, not one number.
EditMode 0 FAIL before committing. Commit.
