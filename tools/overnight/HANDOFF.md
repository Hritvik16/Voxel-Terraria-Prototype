# 93d-engine-14d1-free-cascade-tiers: DONE -- with `-shells 1` cascade tiers 1-2 are not allocated; peak 2,978 -> 2,728 MB

## What changed (commit 977f25e)
- `LODCascadeManager.TiersWanted(tg2, shells, keeptiers)`: tiers 1-2 are skipped only for tg2 + `-shells 1`. Everything
  else allocates them as before. `Phase4Bootstrapper.Cascades` is null in that case, and the log says "NOT allocated".
- `StreamManager`: every `_cascades` call is null-safe. With no cascades the worker skips the downsample: no
  PrepareTier0/DownsampleTier work and `cascadeData` stays null. Scratch contexts get no `DownsampleScratch` (~2.3 MB each).
  New `CascadeTiersOn` and `ScratchDownsampleBuffers` (for tests).
- Readers checked: RaymarchFeature already binds the tier-0 buffers as stand-ins when `LODCascadeManager.Active` is null.
  MemoryLedger and the acceptance rig already handle null. `PlaygroundCapture.LodPop` is now null-safe.
  CoalesceScheduler only stores the reference.
- `-keeptiers 1` allocates the tiers anyway. It is for the diagnostic arms that set `DrawShells = false` (`-costarms
  shells_off`, the farpreview `A_off` still, the `-farop` B shot). Without it those arms now show tier 0 + far band, not
  tiers 1-2 (CostSplit comment updated). Cascade code, `-detailspeed` and `FarStartT` are untouched. No frozen API changed.
- `CascadeTiersOffTests` (3): the start-up rule; streaming, editing, evicting and re-admitting with no cascades (the edit
  survives, no downsample buffers, no manager created, the tier-0 mirror drains); and the shells-off control, which holds
  its buffers. CLAUDE.md has a commands entry.

## Evidence
- Own tests: PASS 15 FAIL 0 (new tests + DeferredEviction, CascadeAsyncRebuild, EditOp). Fast suite **PASS 761 FAIL 0
  SKIP 14** (`Logs/overnight/93d-fast.out`; 758 before, +3).
- Shells off: `run-phase7-rig.sh` **PASS 37 FAIL 0**, and all 19 shots are **byte-identical** to a same-day baseline:
  HEAD without this change (`Phase7Cycle/2026-10-16_024701`) vs with it (`2026-10-16_023443`).
  Compared with the last earlier run (10-13_021118, task 85b), 10/19 shots differ. Both of today's runs differ the same
  way, so commits made since 85b (86, 91, 93a...) cause it, not this change. Nobody re-baselined after those.
- Memory: two launches, before = HEAD build and after = this commit, same flags. Logs `Logs/overnight/93d-before.out` and
  `93d-after.out`, copied to `Screenshots/93d/{before,after}/`. Flags: `MODE=poses EXTRA="-shells 1 -shellworkers 3
  -gateres 1280 -shellwait 60 -poses peak_from_2km_ground,cliff_foot_1 -memledger"`.
  - Peak phys_footprint **2,978 -> 2,728 MB (-250)**. IOAccelerator (graphics, the Metal buffers) **1,466 -> 1,378 MB (-88)**.
    Untagged malloc 1,295 -> 1,144 MB (-151). From the `mem_peak_breakdown.txt` files.
  - Memledger: the `LODCascadeManager` row (79.0 GPU / 67.6 native / 43.8 managed = 190.4 MB) is now "(not present)".
    `cascade tier 1/2` pool rows are "(not present)". Ledger SUM 2,091.2 -> 1,900.7 MB. No other row moved by more than 0.1 MB.
  - The cut is larger than the 193 MB estimate. The extra ~60 MB is the workers' DownsampleScratch buffers and the
    per-chunk cascade result arrays, which the ledger does not walk.
- No frame-time numbers (rule 10).
- Stills: the shells-ON frames, before vs after. cliff_foot_1 is 0.00% different. peak_from_2km_ground differs on 2.06%
  of pixels, in one band of the mid-distance snowfield. In that band shell 2 had filled further in the before run
  (s2 677 vs 236 chunks stored at 60 s; s2 px 29,884 vs 10,705). The after counts match task 85's run exactly
  (3,059 / 10,705). So this is shell streaming timing, not a missing tier: tiers 1-2 never drew with shells on.
- Missing at 60 s, after: peak s1 638 / s2 1,934 / s3-7 all; cliff s1 956 FULL / s2 1,643 FULL. Same pattern as before.

## Not done / notes
- MemoryLedger has no ShellStreamer root, so the shells' own GPU memory is not in the ledger SUM. That would be a one-line
  addition to the roots list, and 93b2 or 93g may want it.

## Next step
Next queued task (93e, D2).

## For the doc
Task 93d (engine 14d1): with the shells on, the unused cascade tiers 1-2 are no longer created.
- Peak process footprint, shells on, 2 poses: 2,978 -> **2,728 MB** (cap 3,000). `Screenshots/93d/{before,after}/mem.txt`
- Metal (IOAccelerator graphics): 1,466 -> **1,378 MB**. Malloc 1,295 -> 1,144 MB. `.../mem_peak_breakdown.txt`
- Memledger: LODCascadeManager 79.0 GPU + 67.6 native + 43.8 managed MB -> not present. Ledger sum 2,091 -> 1,901 MB.
  `Screenshots/93d/{before,after}/farpreview.txt`
- Shells off unchanged: Phase 7 rig PASS 37 FAIL 0, 19/19 shots byte-identical. Fast suite 758 -> 761 PASS, 0 FAIL.
Images:
- `Screenshots/93d/before_after_93d.jpg`: shells ON, BEFORE (left) vs AFTER (right). Top row, peak_from_2km_ground: the
  horizon line, the far mountains and the cliff on the left are the same. Bottom row, cliff_foot_1: the mid-distance red
  slope and its skyline are pixel-identical. Nothing that tiers 1-2 drew went missing.
- `Screenshots/93d/peak_diff_strip.jpg`: rows 732-820 of the peak pose. Strips top to bottom: before, after, and the
  pixels that differ by 16 or more (white). The white speckle sits on the mid-distance snowfield, where shell 2 had more
  chunks in the before run. That is streaming progress at 60 s, not this change.
- `Screenshots/93d/peak_Aoff_diff_strip.jpg`: the diagnostic A_off still (shells not drawn). Before, tiers 1-2 drew the
  snowfield past tier 0. After, the far band draws it. Coarser, but there are no holes. `-keeptiers 1` restores the old arm.
