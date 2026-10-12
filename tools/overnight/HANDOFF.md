# 82-engine-13b-far-redraw: DONE (parts 1, 2, 4). An edit op recorded after the shells and the far band are full now shows in both within seconds. Part 3 (row 12) not built.
## What changed (commit `7be72cc`, branch engine-phase)
- **Shells** (`ShellStreamer`, `ShellStore`): each Update reads the ops the log gained since the last one. Every stored chunk an op touches
  (or a chunk mid-upload) is queued for a redraw. **The stored chunk is regenerated, not patched:** the GPU holds the only copy of shell bricks (row 1),
  so there is nothing on the CPU to patch. The result is byte-identical to a fresh `GenerateShellChunk` with the op by construction (tested below).
  Redraws generate before missing chunks and use the shared 3 MiB/frame budget.
  - `ShellStore.Store(sc, ref budget, replace: true)` swaps without a gap: the old copy draws until the new one is fully up. The old page and
    bodies are freed only after the next directory flush, so no body the GPU still points at is ever rewritten.
  - If the shell has no room for both copies, the old copy is evicted and the new one is stored fresh. The coarser shell draws that chunk for
    at least 1 frame. Counted as `ReplaceNoRoom`, not as a refusal.
  - A chunk still generating or waiting when an op lands gets the missed ops on arrival (`CatchUp`, the same sequential `ApplyToShell`).
- **Far band** (`FarField`; its header section EDITS lists what 2.5D can and cannot show):
  - Every fill job (initial fill, strip, full refill) applies the ops that reach its region to each texel, in seq order (Burst; `ApplyOp`).
  - A later op patches its footprint at every level: a fill job over the op's rectangle, clipped to the ring, uploaded through the existing
    strip staging buffer and FarScatter (taking turns with strips). A strip computed before an op gets a patch queued behind it.
  - **Can show:** a crater (the top drops to the carve's bottom; the floor gets the generator's material at that depth), a mound standing on
    the ground, a pit under water (deeper water), an island rising out of water.
  - **Cannot show:** tunnels, overhangs, floating fills, water draining into a dry crater, or anything under lava or water deeper than 12.7 m.
  - A texel shows an op only if the op covers the texel's centre column.
  - Constructor gains `EditOpLog ops` (from `Streamer.Ops`). Fill jobs: `CompileSynchronously = true` (Editor only).
- **Part 4:** `StreamManager.OpCatchUpEdited` is called with each box an in-flight catch-up wrote. Playground wires it to
  `EditService.NotifyEditedRegion`, which runs the fluid wake scan. It is compiled and covered by the suite only; no dedicated test.
- **Demo:** `-farop -faroplate [-faropr N]` picks the site at start-up but records the op only after the shells hold it. `Playground.FarOpDemo`
  shoots B before → A before → records the op and counts frames → A after (+ tint) → B after (+ tint).
## Evidence
- **`EditOpRedrawTests`**, 4/4 (Logs/overnight/82-filter1.log, 82-filter3.log; one is a unit test of the 2.5D rules):
  - **Replace:** shell 3, 1097 → 925 dense bricks over 7 slices of 64 KB. The old copy reads back byte-exact at every slice; 1 page is
    deferred, then freed at the flush; pool books balance. The no-room case keeps the old copy.
  - **Shells 1-7 (Slow, 203 s):** the High layout fills around the spawn, then 14 ops are recorded. 13 touched stored chunks (shells 1-7:
    1 3 2 1 2 3 1), all 13 differed before the redraw; after it **0 differ** from a fresh `GenerateShellChunk` with the ops. Stale slots 0,
    pool errors 0. Then the camera moves 60 m and 3 ops land on chunks still generating: 3 caught up on arrival, 35 touched chunks, **0 differ**.
  - **Far band:** a 20 m crater, a 6 m mound and a sea-bed pit are recorded after the fill. 11 patches (3,427 texels). Every texel of every
    level == a fresh FarField built with the ops (**0 differ**). Level 0: 1,309 texels lower, 34 higher. The crater's centre texel top = the
    sphere's bottom (5514). The sea pit (3.5 km away) is only in level 4's ring: 1 texel deeper.
- **SUITE=all PASS 755 FAIL 0 SKIP 19** (751 + 4; Logs/overnight/82-all.out).
- **`run-phase7-rig.sh`** (fresh build): **PASS 37 FAIL 0, 19/19 shots byte-identical** to 81's (Phase7Cycle/2026-10-12_101148 vs _061424).
- **Player** (`-farop -faroplate -faropr 400 -shells 1 -shellworkers 3 -shellwait 240`): 2nd launch Logs/overnight/82-farop3-player.log,
  log lines in Screenshots/82/farop_log.txt.
  - The op was recorded at A once shells 2 and 3 held the crater's chunk.
  - **Far band:** 5 patches (one per level) up **8 frames / 0.79 s** after the op.
  - **Shells:** 9 chunks redrawn and 5 caught up, all stored **88 frames / 3.14 s** after the op. 4 of the 9 took the no-room path.
  - 1st launch's B pose (3 km out, 18 deg down) hit a ridge; moved B to 1.5 km out, 2.6 km up. Looked at all shots.
## Not done / next
- **Row 12 (hand edits → shells by majority-vote downsample of the resident 0.1 m chunk): NOT built.** Shell workers cannot read
  `ChunkStore` (single writer). It needs a design: a main-thread copy of up to 2^3k fine chunks per shell chunk, plus a D.1 load for chunks
  that are no longer resident. Today a hand-dug pit shows in the 0.1 m window and cascades only; shells and the far band see ops, not D.1.
- **Shells near the spawn are FULL on High (77's caps, Q44).** All 13 test redraws and 4 of 9 player redraws took the evict-then-store path.
  A fresh store can then be refused if the new copy needs more bricks than the old one freed; the coarser shell draws that chunk until there
  is room (no error).
## For the doc
- **Op → visible** (player, High, 3 shell workers): far band **8 frames (0.79 s)** for 5 patches; shells **88 frames (3.1 s)** for 9 redrawn
  + 5 caught-up chunks. Before this task: never (old data stayed until the area streamed out and back in). Log: Screenshots/82/farop_log.txt.
- **Correctness:** shells 1-7, **13 of 13** touched stored chunks wrong before the redraw → **0 differ** after; 3 in-flight catch-ups, 0 differ.
  Far band: **0 of 5 × 1,048,576** texels differ from a fresh build with the ops. Tests: EditOpRedrawTests 4/4; SUITE=all **755/0/19**.
- **Rigs:** Phase 7 **37/0, 19/19 byte-identical** (no ops in rigs → no change).
- `Screenshots/82/farop_redraw_sheet.jpg`: left column BEFORE the op, right column AFTER.
  - Row 1, pose A, 360 m, shells: the 80 m crater appears at the crosshair as a stepped grey bowl, redrawn by shell 3 (0.8 m voxels).
  - Row 2, pose B, 3 km, shells off so the far band draws, enlarged 3x: after the op a small grey pit (the crater's stone floor) sits under
    the crosshair; before, that spot is yellow ground.
  - Row 3, tint shots: left, the crater at A inside shell 3's orange tint; right, pose B with shells on.
