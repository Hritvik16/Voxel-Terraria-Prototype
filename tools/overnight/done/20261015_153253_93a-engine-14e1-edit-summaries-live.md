# 93a-engine-14e1-edit-summaries-live

Approval: owner, 2026-10-15 in the supervisor chat: Amendment 8.15 Q1-Q10 all (a) -- Q3 (a) "edit summaries", Q10 (a)
order (E1 first) -- recorded in `tools/overnight/done/90a-amendment-8-15-simulation-reach.md` "## Owner answer"; adopted
by task 90b. Row E1 of `AMENDMENT_8_15_SIMULATION_REACH.md` §11. Queued by task 90b 2026-10-15. Branch `engine-phase`.

## Why
Read 8.15 §0, §5 and §2 item 5 first. With shells on, shells hold generator + D.4 ops only (`ShellStreamer.cs` header,
`EditOpApply.TouchesChunk` / `CatchUp` near `ShellStreamer.cs:410-425`), and 0.1 m is drawn only to shell 0's hand-over
(69 m on High; `Raymarch.compute` tTier0 near :3625). So a block-by-block wall (D.1 / `SetVoxel` edits) 69-128 m away is
LIVE (CPU load radius 10 = 128 m, `EngineConfig.TG2_LOAD_RADIUS_CHUNKS`) but NOT DRAWN (found by code reading in 90a, not
yet seen in a shot). This is also the prerequisite for step 14 redone (D2 shrinks the GPU box to 16^3 = +-102 m) and for
shells becoming the play default (93c).

## Change (one change: resident edited chunks redraw into the shells that cover them)
1. Track which RESIDENT 0.1 m chunks differ from the generator by player edits (`SetVoxel` / EditService, i.e. what D.1
   would save; ops-only changes are already in the shells via tasks 81/82 -- do not double them).
2. For such a chunk lying where shells draw (beyond shell 0's hand-over, today; any covering shell >= 1), build a
   **summary**: the resident chunk downsampled 2x per shell step by majority vote (as the cascades do today), and
   redraw the covering shell chunk(s) as generator + ops with the summary's region overwritten (the resident chunk is
   the truth: it already contains D.1 and ops). Use task 82's gap-free path `ShellStore.Store(.., replace: true)`
   (old copy drawn until the new one is up). Shell cave fill (task 83, shells >= 3) runs AFTER the summary is applied.
3. Re-summarise an edited chunk at most once per second (8.15 §5 "Live"), and when it is evicted keep nothing (saved
   summaries are E2, a later task -- after eviction the edit disappears from far shells again; say so in HANDOFF).
4. Invariants: the CPU stays the sole terrain writer; the downsample runs on worker scratch from a snapshot the main
   thread copies (single-writer, §0.1; no locks in the streaming path -- CLAUDE.md); coordinates with `>>`/`&` only;
   a cap on summaries per frame in `EngineConfig` with a counter (refused-and-counted, like the fluid pool).
5. Shells OFF: nothing changes (byte-identical). Frozen APIs untouched (`GetVoxel`, `SetVoxel`, D.1 bytes, §4.4).

## Acceptance
- Tests (new `EditSummaryTests`): an edited resident chunk's covering shell chunk equals a reference (generator + ops,
  then the majority-vote downsample of the resident chunk) byte for byte; an unedited chunk causes no redraw; the
  once-per-second limit and the per-frame cap hold; shells-off unchanged. Run your own tests while iterating
  (`-testFilter`), the fast suite ONCE at the end (`./run-editmode-tests.sh`, via `tools/with-unity-lock.sh`).
- Screenshots, ONE launch (<= ~10 min, short fixed waits, report missing shell counts; no frame-time numbers, PREAMBLE
  rule 10): `-shells 1`, a placed wall/pillar (e.g. a box of `SetVoxel` edits, not an op) ~100 m from the camera,
  before (this change off) vs after, plus `-shellsdebug` tint. Midday, sun behind the camera. Captions say where the wall is.
- HANDOFF `## For the doc`: summaries built / redraws counted, test counts, the before/after pair.
