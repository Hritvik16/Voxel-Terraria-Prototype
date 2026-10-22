# 108-engine-e2-saved-edit-summaries: DONE -- edited chunks keep their look after eviction (D.5 `summary/`), shells + far band

## What changed (commit f8ff7d5 on engine-phase)
- NEW `Assets/CoreEngine/Streaming/SavedEditSummaries.cs`: D.5 codec (`SummaryCodec`) + store (index, LRU, far spans).
  `StreamManager.SaveDelta` calls `SavedSummaries.DeltaWritten` right after D.1's rename (93a's Snapshot/Run/Extract, no
  second downsampler; bricks = D.1's records); a deleted D.1 deletes the summary. `StreamManager.ClearSaveFiles` (used by
  `-cleardeltas`) clears deltas + ops.log + world.state + summary/.
- `EditSummary.OpsBefore` (S; live = int.MaxValue); `GenerateShellChunk` applies ops <= S, summary, ops > S (row 10).
- `ShellStreamer.EditSummaries.cs` point 5: saved summaries of covered chunks without a live one join the snapshot
  (<= 8 disk reads/frame); eviction swaps live -> saved with NO redraw (same look); toggling redraws.
- `FarField`: `FarSpan`s (2 sampled columns per edited brick column) folded 2.5D in both fill jobs (`ApplySpan`, S order
  with the ops), per-job copies, patches on store change (`QueueSummaryPatches`, `PatchAfter`).
- Flags: `-savesummaries 0` (not written), `-savedsummaries 0` (shells don't read them; the far band still does).
  Demo: `-farwall -farwallsaved`. TASKS.md paragraph "Task 108". D.1, SetVoxel, GetVoxel, the 4.4 table: unchanged.

## File format (D.5 `summary/{cx}_{cy}_{cz}.sum`, little-endian, v1)
32 B header {magic 'ESUM', version, seed, chunk int3, D.1 CRC, S, brick count} + brick list (2 B each) + level-1 voxels per
brick (tag 0: 64 B raw; tag 1: uniform, 1 B) + levels 4-7 (1 B per edited group) + CRC32. Levels 2-3 are re-voted from
level 1 on load, so every level equals the live 93a summary byte for byte (tested shells 1-7).

## Evidence
- `EditSummarySaveTests` 5/5 (Logs/overnight/108-filter.log): saved == live at all 7 levels; new session's shell 1 ==
  generator + live summary (0 voxels differ); stale D.1 -> StaleDelta, shell == generator; flipped byte -> CrcMismatch;
  5 truncations rejected; wrong chunk/seed -> BadHeader; D.1 missing -> DeltaMissing; ops order == (ops <= S, summary,
  op 2) and differs from summary-after-all by 604 voxels; ClearSaveFiles; far band patch == fresh build (0 texels differ),
  tower texel top 6242 (plain 6182) stone, pit texel 6178 (plain 6190, floor 6177).
- Fast suite: `PASS 886 FAIL 0 SKIP 14 suite=fast wall=5m08s` (Logs/overnight/108-fast.out).
- Demo (one launch, Player.log `[FarWall]`): 60x270x60 SetVoxel tower, 6 chunks; all 6 evicted 47 s after going far;
  6 D.5 written, 0 rejected; shells held 6 saved summaries; saved OFF -> 0 held (generator look), ON -> 6 held.
- Screenshots looked at (Screenshots/108/, not committed):
  - `farwall_saved_off.png` -- FAR pose 241 m, tower chunks evicted, saved summaries OFF: centre of frame is bare grass
    where the tower stands (only a small dark outline at its foot -- likely the far band's folded column shading the ground;
    the far band ignores `-savedsummaries`; not verified).
  - `farwall_saved_on.png` -- same pose, saved ON: the dark stone tower is back in the centre, drawn from D.5.
  - `farwall_saved_on_tint.png` -- shell tint: the tower has the surrounding shell band's tint (a shell draws it, not tier 0).
  - `farwall_near.png` -- 100 m, before eviction: the tower as tier 0 draws it (reference look).

## Deviations from 8.15 §5 (my calls, recorded)
1. Levels 4-7 are stored too (<= 4 B/brick): they span unedited bricks, so "reduce at use" could not match the live look.
2. "Bricks that differ from GenerateShellChunk at shell 1" = D.1's bricks (differ from the 0.1 m baseline); no shell-1
   generation per save.
3. The build is SYNCHRONOUS on the main thread inside SaveDelta: ~3.1 ms/summary in the player demo (18.6 ms / 6), 5.9 ms
   median in Editor tests -- the amendment estimated ~1 ms. With MAX_CHUNK_SAVES_PER_FRAME = 4 a frame evicting 4 edited
   chunks can carry ~12-24 ms extra (not a frame-time measurement; rule 10). Follow-up for the owner (performance code,
   needs approval): schedule the vote job off the main thread (93a's pattern) and/or vote only the edited groups.

## Next step
Queue goes on with 110. Owner: decide on the follow-up in deviation 3 at the perf milestone.

## For the doc
- Saved summaries: an evicted edited chunk now keeps its look in the shells and far band (was: dropped on eviction).
- Size: tower demo D.5 75,919 B vs D.1 546,676 B (13.9%; amendment est. +~12%); test tower+pit 736 B vs 15,474 B (4.8%).
- Build cost per summary: 3.1 ms (player demo, 6 chunks) / 5.9 ms median (Editor test) + 0.5 ms write; main thread.
- RAM: index ~48 B/chunk + far spans 32 B each + LRU (cap 16 MB; tower chunk 29 KB in memory, test chunk 4.3 KB) + one
  4.5 MB scratch after the first save -- a 1,000-chunk megabase of tower-like chunks is ~29 MB of summaries (> 20 MB: the
  LRU caps the store at 16 MB, the shells' snapshot holds what they cover).
- Tests: EditSummarySaveTests 5/5; fast suite PASS 886 / FAIL 0 / SKIP 14.
- Images: `Screenshots/108/farwall_saved_off.png` (far pose, saved OFF: no tower in the centre) vs
  `Screenshots/108/farwall_saved_on.png` (same pose, saved ON: the tower is back) -- look at the centre of the frame.
