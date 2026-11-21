STOP_FOR_OWNER -- 216's code is done and committed with the default OFF. Its proof launches could not run: the memory watchdog killed the
player at jungle_high in BOTH arms (9.1 / 9.8 GB footprint, `-gpuviewevict 0` too), then the owner pause (`tools/heavy.sh`, 00:03:19) stopped
the rerun. I did not lift the pause or `tools/overnight/PAUSE`.
Questions (recommended first):
  Q1 jungle_high footprint (not caused by this task, see Evidence): (a) queue a bug task. Its first step: the same launch with `-trees2 0`
     (suspect: world trees, default since 214; banyans are jungle giants). (b) Leave it for the performance stage.
  Q2 216's proof: (a) run `Logs/overnight/216_pair.sh` as it stands (`-trees2 0`, the world 208 measured). If refusals go down, flip the default ON
     (one line each in `Phase4Bootstrapper` + `TerrainClipmap.ViewEvict`) and run SUITE=all. (b) Wait for Q1's fix and run it with defaults.

# 216-bug-gpu-view-priority-eviction: CODE DONE (commit 3aa8acd), NOT MEASURED, default OFF (`-gpuviewevict 1` = on)
## What changed
- `TerrainClipmap.ViewEvict.cs` (new): a chunk IN VIEW or in the camera's 5x5 that the full GPU body pool refuses makes room. Box chunks OUT OF
  VIEW and FARTHER (Chebyshev XZ) give up their bodies, farthest first, and only if the victims cover the whole shortfall. A victim is held by
  its slot, not dirty, not touched by live fluid, outside the 5x5 and outside the frustum. It is written as air (the refusal's write) and joins
  the refused set, so 197's hand-over draws it and 207's retry brings it back.
- `TerrainClipmap.cs` upload loop: a refusal first calls `TryEvictForView`, then maps again. `GpuPool.cs` retry: requesters are ranked first and
  may evict too; with eviction on, the retry also re-runs when the view moved. `StreamManager`: `ViewVersion` + `SetView` (208's frustum).
  `Phase4Bootstrapper`: `-gpuviewevict`. `-mirrorprobe 1` prints a new `[mirror gpuview]` line: refused now in view/5x5 vs other, victims,
  bodies, chunks saved, GPU pool use.
- Efficient by construction: one scan of the box's 4,096 slots per upload pass, and only in a pass where a requester is short. One sort of a
  reused list of packed longs (distance << 32 | slot), used from the far end. No per-chunk storage, no new GPU memory, no pool, layout or API
  change. At most 16 victims per pass; their 16 KB entry writes count against the pass's bytes. Off or no view = 207's path.
- Tests `GpuViewEvictTests` (11). TASKS.md "Task 216", KNOWN_ISSUES (208 entry + new "JUNGLE_HIGH FOOTPRINT"), CLAUDE.md index line.
## Evidence
- Fast suite BEFORE: PASS 1049 FAIL 0 SKIP 23 (`Logs/overnight/216/fast_before.out`). Filter run (GpuViewEvict, GpuRetryFit, ValveViewOrder,
  SealedBrick, Tier0LackHandOver): **PASS 40 FAIL 0** (`filter2.out`; run while the default was ON, and the tests set the flag themselves).
- NOT RUN (blocked by the pause): fast suite after, SUITE=all, the memory pair. The player build compiled ("build OK").
- Launch pair 1 (today's defaults, jungle_high + walk1/walk2): b1 (ON) and b2 (`-gpuviewevict 0`) were both killed at the first pose, ~6 min
  after start, at 9,088 / 9,782 MB footprint (`Logs/overnight/watchdog.log`, logs in `Logs/overnight/216/killed/`). No `[mem]` or `[mirror]`
  line was printed and no shot was taken. The `0` arm runs 207's code (TryEvictForView returns at once; the retry is unchanged), so this blow-up
  predates 216. Task 208 ran the same poses at a 2.8 GB process peak; 209-215 came after it.
- No pictures: the shots were never taken.
## Rule 11
No numbers yet. By construction the change holds < 0.1 MB (one scratch list <= 4,096 longs + 2 lists of refused coords) and no GPU memory.
## Next
Owner answers Q1 / Q2. The pair (`Logs/overnight/216_pair.sh`, ~10 min a launch, b1 then b2) needs the pause lifted.

## For the doc
- 216 GPU view-priority eviction: code + 11 tests done, committed DEFAULT OFF (`-gpuviewevict 1` = on). No before/after numbers yet.
- Blocker found: at jungle_high the player reached 9.1 / 9.8 GB physical footprint and the memory watchdog killed it, in both arms (ON and OFF).
  208 ran the same pose at a 2.8 GB peak (`Logs/overnight/watchdog.log` 2026-11-15 23:55:29 and 2026-11-16 00:02:54).
- Suite: fast before PASS 1049 FAIL 0; 40/40 related tests after; the full suites were blocked by the owner pause.
- Images: none (both launches died before the first shot).
