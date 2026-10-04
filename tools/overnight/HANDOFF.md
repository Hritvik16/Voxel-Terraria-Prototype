# 26-deferred-eviction-retry: DONE. Save-capped evictions are now retried every frame while any is deferred. v4 poses MIRROR lost/lag/stale is 0/0/0 on all 18 poses in 2 of 2 valid runs (was 12232/5249 lost at the first two). Resident never exceeds 3087.

## What changed
- Commit d4165c9, `StreamManager.cs` (§4.5):
  - `EvictOutOfRange` sets `_evictDeferred` when the `MAX_CHUNK_SAVES_PER_FRAME` cap skips a dirty chunk.
  - `Update` has a new `else if (_evictDeferred)` branch: on a frame with no camera-chunk change, it calls `EvictOutOfRange(_lastCameraChunk)`. The cap stays 4/frame. That frame skips the drained-queue re-sweep, which runs on the next frame.
  - `EvictionDeferred` is a read-only test hook.
- New `Assets/CoreEngine/Tests/DeferredEvictionTests.cs` (fast, flat world, load radius 2): dirties 10 chunks, teleports 2 km and holds still. Asserts saves of 4, then <= 4 per still frame, all 10 saved, none left resident, and the destination square fully resident. Log: `10 dirty chunks saved over 1 + 2 frames at cap 4`.
- `TerrainClipmap.UploadDirty` is untouched (no in-box test, per the task).
- `KNOWN_ISSUES.md` entry marked FIXED with a fix sub-bullet. The `CLAUDE.md` index line is updated.

## Evidence
- **Fast EditMode suite:** PASS 618 FAIL 0 SKIP 12, i.e. 617 + 1 new (`Logs/overnight/26/suite1.out`). Since then only docs changed.
- **`MAPV=4 MODE=poses`, run 1 (BUILD=1, d4165c9):**
  - Logs: `Logs/overnight/26/poses1.out`, `Logs/tg2world_20261004_132136.txt`. Shots: `FarBand/2026-10-04_132705_world/`.
  - MIRROR is 0/0/0 on every pose. Resident is 3087, or 1764 at the band-91 poses (top layer clipped).
  - Pixel-identical to task 24's run (`095643_world`) on 59/61 shots. The 2 minimap_check shots are known run-to-run noise. As expected, the fix changes no rendered pixel: task 21 already saw no visible hole.
- **Run 2:**
  - Logs: `Logs/overnight/26/poses2.out`, `Logs/tg2world_20261004_135346.txt`. Shots: `FarBand/2026-10-04_145512_world/`.
  - MIRROR is 0/0/0 on all 18 poses, with the same bands and residents as run 1.
  - 60/61 shots are pixel-identical to run 1. Only minimap_check_noon differs (noise).
  - It waited 13:53-14:54 on the owner pause.
- **Discarded attempt:** `Logs/overnight/26/invalid_devmap_open/poses2.out` (shots `134809_world`). The DEV MAP was open from pose 2 on (someone at the Mac). The camera sat ~5.7 km up, so every band was 91. MIRROR was still 0/0/0, but the shots are meaningless.
- Looked at `145512_world/peak_from_2km_ground_golden.png` (the pose that had 12232 lost): a grass slope fills the right half, with rock peaks and a red-capped summit on the left horizon. No holes, nothing floating.
- **`./run-acceptance-rig.sh`, warm steady state:**
  - Warm-up (discarded): `Logs/overnight/26/warmup.out` -> `Logs/tg2world_20261004_151746.txt`. The rig ran right after it: `Logs/overnight/26/rig.out`, report in `Phase4Acceptance/2026-10-04_152558/`.
  - **PASS 52 FAIL 2**, the same set as the previous report (`2026-09-25_145426`). The 2 FAILs are the known §4.3 steady-state upload p99 checks (1.956 and 1.680 ms vs 1.355 and 1.704 before).
  - Persistence: 0 save failures, 0 LOST UPDATES, 0 reload hash mismatches. Resident stays between the load and evict squares (729 <= 756 <= 961). Deficit inside 128 m is 0.
  - Moved vs 09-25:

    | | 09-25 | now |
    |---|---|---|
    | Gate C frame total p50 / p99 | 19.07 / 129.69 ms | 22.03 / 204.03 ms |
    | Gate D frame total p50 / p99 | 19.91 / 138.89 ms | 23.16 / 255.00 ms |
    | Deficit 128-166 m, p50 | 9 | 88 |

  - **Not attributable to task 26.** The baseline is 9 days and many commits old (Phase 7 and later), and it was not warm. The retry only runs on frames after a capped eviction. A same-day A/B would be needed to attribute any of this.
  - Screenshot quad (`/tmp/rig26.png`, rebuild from the run folder): top-left `GateB_Initial_Beauty`, top-right `Traverse_49`, bottom row `GateC_AfterReturn` / `GateD_AfterPersistence`.
    - Top-left: snow plain with contour lines, green hills and a dark peak on the horizon.
    - Top-right: grass and sand, peaks on the horizon.
    - Bottom row: the same view as Gate B. Nothing floating, no holes, mostly terrain.

## Unfinished / next
- `TerrainClipmap.UploadDirty` still uploads any resident chunk into its toroidal slot with no "inside the mirror box" test. A defensive check (skip or assert) is a separate change and needs the owner's approval. Not queued.
- The rig's frame-total rise since 09-25 has never been bisected. If it matters, it needs a warm same-day A/B, not this task.
