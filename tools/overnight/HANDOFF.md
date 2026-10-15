# 93a-engine-14e1-edit-summaries-live: DONE -- block edits on resident chunks now redraw into the shells (8.15 E1)

## What changed (commit f271d77)
- `ChunkStore.SetVoxel`: with an `EditSink` attached (shells on only) it marks the edited brick in the new CPU-only
  `Chunk.editMask` and tells the sink once per chunk until the next snapshot. Op writes (`EditOps.Record`,
  `StreamManager.CatchUpOps`) are muted: they mark no brick, but they still re-summarise a chunk that is already edited.
  On reload, the loading worker sets the mask from the D.1 records (`EditSummaryOps.MaskFromDelta`). The D.1 format is unchanged.
- `EditSummary.cs` (new): the main thread copies the chunk's brick words and bodies; a Burst job expands them and
  majority-votes levels 1-7 (the cascades' rule, fuzz-equal to `LODDownsampler.MajorityVote`). A summary keeps only the
  coarse voxels whose fine cube touches an edited brick, so unedited ground keeps the generator's look.
- `ShellStreamer.EditSummaries.cs` (new): rebuilds a summary at most once per second for each edited chunk that a shell >= 1 covers.
  `EngineConfig.EDIT_SUMMARIES_PER_FRAME` = 1; extra builds are refused, counted (`SummariesRefused`) and retried next frame.
  2 scratch buffers of ~4.3 MB each, allocated on the first edit. A new summary goes into an immutable snapshot, and the
  covering held, uploading or generating shell chunks are queued through task 82's gap-free replace.
  `GenerateShellChunk(.., ops, summaries)` builds generator + ops + summaries, then runs the task 83 fill, which also
  treats summary boxes as open-air seeds. A chunk that already has a summary is redrawn rather than caught up when an op lands late.
- Eviction keeps nothing: the summary leaves the snapshot. A shell chunk already drawn with it keeps showing it until that
  chunk is generated again; after that the edit is gone from the far shells until the chunk is resident again.
  Saving summaries is E2.
- `-editsummaries 0` = off; shells off = no sink, nothing changes; frozen APIs untouched. Demo `-farwall`
  (`Playground.FarWallDemo.cs`). Docs: a note in 8.15 §5 and a task 93a entry in CLAUDE.md.

## Finding: 90a's "not drawn 69-128 m" does not hold today
Tier 0 marches the whole GPU mirror box (±204 m) and the shells run only where tier 0 misses
(`Raymarch.compute`, `if (_ShellCount > 1 && !hit ...)`). So a resident SetVoxel tower 100 m away is drawn by tier 0
with summaries off (tint: tier-0 blue). The gap 8.15 §5 describes opens with D2's 16³ box (±102 m), and beyond the CPU window.
E1 is now ready for D2. The near before/after pair is therefore the same picture. The summary is shown from 230 m instead.

## Evidence
- `EditSummaryTests`, 5 of 5 passing, each test under 9 s:
  - Vote fuzz: 200 k samples. Shells 1-7: the generated shell chunk matches an independent reference byte for byte (generator, then
    `DownsampleOnce` chained over the resident chunk); the summary changes 205/22/2 voxels at shells 1/2/3.
  - D.1 records equal the marked bricks. Streamer: unedited chunks give 0 notices; an op gives no summary; the redraw has 0 voxels different from the reference;
    the 1 s interval and the 1-per-frame cap hold (refusals counted); eviction drops the summary.
    Shells off: no marks, generation byte-identical.
- Fast suite: `PASS 737 FAIL 0 SKIP 14` (732 + 5 new), `Logs/overnight/93a-fast.out`.
- Demo `BUILD=1 ./run-play.sh EXTRA="-farwall -shells 1 -shellworkers 3"` (High): `Logs/overnight/93a-farwall-player.log`, `Screenshots/93a/farwall.txt`.
  - Tower 6 x 25 x 6 m: 975,600 SetVoxel voxels, 6 chunks. After going far and coming back, the 6 chunks reloaded from D.1 in 6.6 s. That gave 6 notices and 6 summaries
    (176,812 B), with 15 refusals spread over the frames. 4 shell chunks were redrawn, 1.20 s (17 frames) after turning summaries on.
    Main-thread snapshot + extract time: 20.4 ms in total.
    At the far pose all 6 were dropped; shell 2 held the tower's chunks.
- Shells missing at the shots (60/45 s waits): shell 1 ~600-900, 2 ~1,200-1,700, 3 ~1,500-1,660; the tower's covering chunks were held. No frame times (rule 10).

## Not done / next
- Saved summaries (D.5) are E2. D2 (GPU box 16³) is where E1 becomes visible near the player; a check like D2's should
  confirm a resident edit at 102-128 m is drawn by shell 1 (`-farwall -farwalldist 115` once D2 lands).
- Launch 1 put the tower uphill outside the CPU's ±3 layers (edits refused); the demo now picks a resident site below the eye.
  The sun is ~60° off "behind the camera", so the tower's near face is in shade. No follow-up queued.

## For the doc
Task 93a (8.15 E1): a resident block-built chunk is now summarised into the shells (majority vote of the real voxels) and
the covering shell chunks are redrawn. Today tier 0 already draws such edits out to the GPU box, so this matters from D2 on.
- Shell chunk vs reference (generator + vote of the resident chunk), shells 1-7: 0 voxels differ (`EditSummaryTests`)
- Tower reload: 6 edited chunks -> 6 summaries (177 KB) -> 4 shell redraws, 1.2 s after turning summaries on (`Logs/overnight/93a-farwall-player.log`)
- Main-thread cost: 20.4 ms in total for 6 summaries (snapshot + extract); 1 build per frame, 15 refusals counted
- Fast suite 732 -> 737 passing, 0 failing
- `Screenshots/93a/farwall_far_before.png` -- 241 m from the tower, summaries OFF: the yellow ridge in the centre is empty (the chunks are not resident, and shell 2 has the generator only).
- `Screenshots/93a/farwall_far_after.png` -- same pose, summaries ON: the dark stone tower stands on that ridge, centre of the frame (its `_tint` twin shows it yellow = shell 2).
- `Screenshots/93a/farwall_near_before_tint.png` -- 100 m, summaries OFF: the tower in the centre is tinted tier-0 blue, i.e. it was already drawn before this change.
