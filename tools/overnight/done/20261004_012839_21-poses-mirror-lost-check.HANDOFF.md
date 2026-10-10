STOP_FOR_OWNER -- task 21 found the cause of the v4 MIRROR lost counts. The fix changes streaming eviction (§4.5), so it is not a mirror-only fix and is not applied. Approve it by deleting `OWNER-APPROVES-26` from the Requires line of `tools/overnight/queue/26-deferred-eviction-retry.md`.
Still waiting from 19: approve task 22 (coast black bar) the same way, by deleting `OWNER-APPROVES-22`.

# 21-poses-mirror-lost-check: DONE (report only). The lost counts are deterministic and only on map v4.

Cause (from the code, not instrumented):
- `EvictOutOfRange` evicts at most 4 dirty chunks per call and runs only when the camera moves a chunk. Skipped chunks are not retried.
- So after a teleport they stay resident outside the band.
- `UploadDirty` does not check that a resident chunk is inside the mirror box, so these chunks write into the slots of in-box chunks.

## What changed (no engine code)
- `KNOWN_ISSUES.md`: new entry "SAVE-CAPPED EVICTIONS ARE NEVER RETRIED".
- `CLAUDE.md`: one index line for it.
- New queue task `26-deferred-eviction-retry` (needs owner approval).

## Evidence
- One build, four `MODE=poses` runs, interleaved v4 / v3 / v4 / v3.
- Driver log: `Logs/overnight/21_driver.out`. Per-run logs: `Logs/overnight/21_arm{1..4}_v{4,3}.out`. Player logs: `21_arm{1,2,3}_player.log`.
- Machine state for all four runs: AC power, 100% charged. No owner-pause NOTE in any run.

| run | map | peak_from_2km_ground | summit_view | glide_400m | other 15 poses | peak footprint | Untagged / IOAccel(graphics) |
|---|---|---|---|---|---|---|---|
| 1 (012919) | v4 | lost 12232 lag 0 stale 2 | 5249/0/2 | 0/0/1 | 0/0/0 | 3770 MB | 2099 / 1445 MB |
| 2 (015047) | v3 | 0/0/0 | 0/0/0 | 0/0/0 | 0/0/0 | 3385 MB | 1717 / 1444 MB |
| 3 (020959) | v4 | 12232/0/2 | 5249/0/2 | 0/0/1 | 0/0/0 | 3495 MB | 1824 / 1445 MB |
| 4 (023103) | v3 | 0/0/0 | 0/0/0 | 0/0/0 | 0/0/0 | 3550 MB | 1889 / 1444 MB |

**The v4 numbers repeat exactly.** Both v4 runs, and B2's run, give the same counts and the same stale examples:
- `int3(348,43,406)` at peak_from_2km_ground;
- `int3(284,83,246)` at summit_view;
- `int3(189,67,182)` (uniform material 6) at glide_400m.

All three have `dirty=False`. So this is not timing.

**The mechanism shows in the resident count.** On v4 it is 3096 / 3092 / 3088 / 3087 over the first four poses. The full box is 21x21x7 = 3087, so the extras are 9 / 5 / 1 / 0, meaning 4 more are evicted at each teleport. That matches `MAX_CHUNK_SAVES_PER_FRAME` = 4. On v3 the count never goes above 3087.

**Which chunks:**
- The validator names only one stale example per pose. It gives no coordinates for lost chunks, and I did not change it (the task said not to).
- 12232 lost bricks is about 3 chunks' worth (4096 bricks per chunk).
- The stray chunks are the 9 that were dirty when the camera left its starting window. Every `SetVoxel` sets `deltaDirty`, so moving fluid there is the likely source; this is not proven.

**Is only the first pose affected?** No. The first three poses after the initial fill are affected, and the effect fades as each teleport evicts 4 more.

**Screenshots** (`~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/FarBand/2026-10-04_013142_world/`):
- `peak_from_2km_ground_golden.png`: a dark green voxel hillside fills the right two-thirds, with mountains on the left horizon. Look at the hillside: no hole and no misplaced chunk.
- `peak_from_2km_ground_golden_band.png`: the voxel region (blue) is the whole foreground. Far-band tiers (green/yellow/orange/red) appear only on the left horizon, with no gaps.
- `summit_view_golden.png`: a red-brown slope fills about 85% of the frame, with sea at the right edge. Nothing is visibly wrong.
- `summit_view_golden_band.png`: blue voxels cover the slope, a thin green tier runs along the ridge top, and far tiers show only at the lower right.

**Memory: the B2 vs B1 footprint gap was run-to-run spread, not v4.** Peak footprint was 3495-3770 MB on v4 and 3385-3550 MB on v3. The two ranges overlap, and all of the spread is in Untagged (1717-2099 MB). IOAccelerator stayed flat at ~1445 MB.

The EditMode suite was not run because no engine code changed.

## Next step
- If the owner approves 26: retry the deferred evictions every frame until none are left, then re-run v4 poses and expect 0/0/0 everywhere.
- A separate, optional defensive fix: an in-box test in `UploadDirty`. It is not queued because it touches the mirror's upload contract.
