STOP_FOR_OWNER -- one performance change made (R1). Please look before the next one. Next lever on the plan: R6 (kernel diet, Q72 (b)), or the Q83 shadow fix. Which one, or neither?

# 132-perf-shell-prepass-r1: DONE. R1 shell pre-pass is exact (0 px difference) and faster at both standing poses: p50 -4.2 ms walk_near, -6.9 ms walk_open. It is now ON by default.

## What changed (commit 33a992f)
- `Raymarch.compute`: new kernel `CSShellPrepass`. CSMainFarShade now runs in 2 passes (`_ShellPrepass` 1 / 2). Buffer header `ShellPre`, 20 B/px.
  - Pass 1 draws every 8x8 group that has no shell ray. In a group that has one, it hands those rays over (direction + tTier0) and returns.
  - The pre-pass marches the handed rays alone.
  - Pass 2 draws the handed groups: handed lanes skip tier 0 and use the march result; the other lanes re-run tier 0.
  - It works on whole groups, so the 2x2 shadow reuse and the 8.4 guard see the same lanes as in one pass.
  - With `-shellprepass 0` the path is the old one.
- `RaymarchFeature.cs`: `ShellPrepass` (ON; `-shellprepass 0` = off), with buffer alloc only while on, 3 dispatches, and the `[shellprepass] scratch` log line.
  It turns off with a debug view, with task 130's split arms, and with `shellmarch_off`.
- Cost-split arms `prepass_on` / `prepass_off`. TASKS.md "Task 132". One clause each in KNOWN_ISSUES "SHELLS COST" and in the CLAUDE.md index.
- The first build failed with an FXC/Metal error: a barrier sat after a return that depended on a groupshared value. Fixed with a sync after each read (the compiler's own hint).

## Evidence
- Fast suite: PASS 921 FAIL 0 SKIP 15 (`Logs/overnight/132-tests.out`). Player build clean (`Logs/costsplit_build.log`).
- Power: AC the whole time (55 -> 70%), no owner pause. Warm-up `Logs/tg2world_20261023_175145.txt` (discarded). Then, back to back:
  - the cost split `Logs/costsplit_2026-10-23_175808.txt`;
  - MODE=walk x4 `Logs/tg2world_20261023_{180502,180940,181427,181906}.txt`.
- Decisions were written before measuring: `Screenshots/132/decisions_before_measuring.txt`. Full table: `Screenshots/132/perf132.txt`.
- Identity: the no-upscaler raw stills OFF vs ON show 0 px different (out of 2,073,600) at both poses, for both the raw and raw2 stills.
  - Hit-ray counters match.
  - No-hit counters are ~4 x 960 lower ON. The pre-pass does not march the 4 dispatch rows below the 540-row frame, which are never drawn.
- Looked at `Screenshots/132/onoff_raw_sheet.png`: the two columns are the same. Details under "For the doc".
- The walking arm runs the performance preset at noon, as MODE=walk always does. That is why it is ~12.5 ms. It agrees with the standing result.
- Default: the pre-written rule was met (faster at both poses beyond noise, image identical), so the default is now ON.
- Not rebuilt after the default flip (C# only, compiled by the suite). The `Builds/Playground.app` on disk still defaults to OFF until the next build.

## Left open
- Only about half of task 130's ~10 ms "context" share came back. Pass 2 is a second big-kernel dispatch, and it re-runs tier 0 for the non-shell lanes of each handed group.
  - One possible next step: store those lanes' tier-0 result too, so pass 2 re-runs nothing. It needs more scratch and is not designed.
  - The march's own ~45% is R6's (kernel diet).
- Q83 (tier 0 draws a far cliff's shadow as lit) could join R1 as follows. Pass 1 already knows which tier-0 sun rays leave the window or band ceiling unblocked. It could hand those rays over the same way, and the pre-pass would march them through the shells with `ShellMarch(..., shadow=true)`. Pass 2 would then read sunVis.
  - Cost: +4-8 B/px and a second ray kind in the small kernel. Not asked of the owner yet, so nothing was built.
- Counters: with the pre-pass on, the march counters 48-60 / 65-66 come from the pre-pass kernel. Tier-0 counters in handed groups count twice for re-run lanes (diagnostic only).

## For the doc
| warm, AC, FSR 3 540 rows, quality, noon | OFF (today) | ON (-shellprepass) | change |
|---|---|---|---|
| walk_near p50 / p99 ms (cost split, 2 passes each) | 73.8 / 127 | 69.6 / 122 | -4.2 ms p50 (noise 1.5) |
| walk_open p50 / p99 ms | 48.8 / 90 | 41.8 / 64 | -6.9 ms p50 (noise 0.5) |
| MODE=walk 3 min (performance preset) p50 / p99 / > 33 ms | 12.73 / 37.1 / 1.63% | 12.46 / 33.8 / 1.14% | -0.3 ms p50, -3.2 ms p99 |
| GPU memory added | -- | 9.9 MiB at 960x540 (18.4 MB at 1280x720) | peak RAM +1 to +7 MB (noise) |
| image (no-upscaler stills) | | | 0 px differ at both poses |
- Sources: `Logs/costsplit_2026-10-23_175808.txt` (copy `Screenshots/132/costsplit_132.txt`), the walk logs above, table `Screenshots/132/perf132.txt`.
- `Screenshots/132/onoff_raw_sheet.png`: left = OFF (today), right = ON; top row walk_near, bottom row walk_open.
  - Where to look: in the bottom row, the shell-drawn right-hand mountain and the far sea on the left. In the top row, the top-right ridge, the only part of walk_near drawn by the shells.
  - The two sides are byte-identical.
- Default changed: yes, ON (`-shellprepass 0` = old path).
- Gameplay consequence in plain words: none. Only the order in which the GPU draws changed. The picture, distances and the 128 m simulation reach (8.15) are the same, and far edits are still drawn in 3D. No frozen API or saved-data change.
