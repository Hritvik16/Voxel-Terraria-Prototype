STOP_FOR_OWNER -- task 116 measured; the rule says OFF and the shell-march line closes. One question at the end (Q72).

# 116-perf-shell-sky-leap: DONE. Exact (0 px), shell iterations cut 37-64%, frame time unchanged, so OFF by default (`-shellcolleap 1`).

## What changed (commit `b7ab1f6`, branch engine-phase)
- `ShellStore`: per page, 16 bytes of COLUMN TOPS in the page buffer's tail after the 93b5 masks (`ColumnTops`, `ColTopBase`,
  `COLTOP_UINTS` = 4). Byte j = the highest non-air brick y + 1 in 4x4-brick column j. Built in `Store` and uploaded with the page.
  A redraw (edit, op, summary) makes a new page with its own tops, so they never go stale. Cost: 66 KB on High (`ShellBytes.COLTOP`).
- `Raymarch.compute` `ShellMarch`: a rising ray in an air brick at or above its column's top leaps to that column's edge in ONE step,
  up to the chunk top. When every column of the chunk is that low, it leaps the whole chunk's x/z instead.
  - New functions: `ShellLeapBox`, `ShellBoxLeapOk` (93b5's group test, generalised to any box). Diagnostic counters: ShadowStats 65-66.
  - **No far-band height is needed (unlike the plan's wording).** The leap stays inside one chunk and one distance band, so it never
    skips an ABSENT / PAST_LAST end. The far band is always asked from the same t as before.
- `RaymarchFeature.ShellColumnLeap` (`-shellcolleap 0|1`, **default OFF**). Cost-split arms `colleap_on` / `colleap_off`.
- CPU twin `ShellRayReference.March(colLeap)`, plus `LeapBox`, `PlaneLeapBox`, `BoxLeapOk`, `IColumnView`.
- Tests:
  - New `ShellColumnLeapTests` (6). `ShellAirMaskTests.CheckStored` also checks the tops of real tg2 pages.
  - `ShellStorageTests`: the budget count now includes the extra 16 B per page.
- Docs: TASKS.md (Task 116), KNOWN_ISSUES "SHELLS COST" (COLUMN SKY LEAP paragraph), CLAUDE.md index line (one clause).

## Evidence
- **Exactness, CPU** (ShellAirMaskTests' world: High layout, holes, absent chunks, floating cells, tier 0; 2,376 rays + 687 shadows):
  - Exit-plane leap, mask off and mask on: every field equal, t included.
  - Default leap: same end, shell, cell, normal and material on every ray. 40 t's differ in their last bits (max rel 1.2e-7).
  - With the sky exit on: same.
  - Mutation (column tops one brick low): 5 results change, so the test can fail.
  - Test-world iterations fell to 66%.
- **Exactness, GPU:** no-upscaler raw stills, ON vs OFF: **0 px** at walk_near and at walk_open. raw vs raw2 is also 0.
- **Fast suite:** PASS 894 FAIL 0 SKIP 14 (`Logs/overnight/116-tests.out`). Player build: no shader errors.
- **Measurement:** warm steady state, AC power (100%, charged), no owner pause during the runs, noon, play settings (FSR 3 540, High, shells ON).
  - Warm-up (discarded): `Logs/tg2world_20261022_202312.txt`.
  - Cost split (F X X F, F = leap ON): `Logs/costsplit_2026-10-22_202941.txt` (copy `Screenshots/116/costsplit_116.txt`).
  - Walks: `Logs/tg2world_20261022_{203606,204059,204554,205047}.txt` = A B B A (`Screenshots/116/walks_116.txt`).
  - Driver: `Screenshots/116/116-driver.sh`.

| | leap ON | leap OFF | change |
|---|---|---|---|
| walk_near p50 (cost split) | 65.3 / 65.6 | 61.9 / 66.0 (noisy) | ON +1.9 ms slower (pooled 65.4 vs 63.6; noise: OFF passes 4 ms apart) |
| walk_open p50 | 45.5 / 45.5 | 45.8 / 45.6 | ON -0.2 ms |
| walk (3 min x 2 each) p50 | 41.91 / 41.51 | 41.50 / 41.89 | 0.0 |
| walk p95 / p99 | 77.9 / 74.9, 98.0 / 99.5 | 73.4 / 73.9, 97.0 / 94.1 | ON ~+2.7 / +3 (inside the spread) |
| walk % > 33 ms | 79.2 / 78.8 | 79.5 / 80.4 | ~0 |
| shell iters per sky ray (near) | 29.5 | 82.2 | -64% |
| shell iters per pixel (open) | 19.0 | 30.3 | -37% |

- vs task 110: near 60.7 / open 43.0 then; FULL today is 65.4 / 45.5.
- **Rule applied:** the gain is < 1 ms at both poses, so the leap is OFF by default and the shell-march line closes.
- This is the third time (93b5, 93b5 (b), 116) that a big cut in shell iterations bought ~0 ms. The march's cost is NOT its step count.
  It is probably SIMD divergence (hit rays at 102-106 iterations share groups with the sky rays) or memory latency / occupancy.
  That is a guess and was not measured.

## Left open / risk
- With the flag OFF, the new block is still compiled into the kernel. An ON-vs-OFF comparison inside one build cannot see a static cost
  (registers / occupancy).
- Indirect hint only, from a different hour: task 115's walks on the pre-116 build (same flags) had p50 41.9 / 40.7 / 41.1 and
  > 33 ms 72-78%. This build with the leap OFF: 41.5 / 41.9 and 79-80%. That is inside day-to-day spread, but I can't rule a static cost out.
- Not done: PERF_30FPS_PLAN.md row 1 still shows the estimate. The supervisor can mark it "measured ~0, OFF".
- Next per the plan: 117 (Medium preset trial).

## Owner question
Q72. The column leap's dead code: (a) leave it in, OFF (as 93b5's mask), and go on to 117 (recommended: the evidence of a static cost
is weak, and 117/118 compare arms inside one build); (b) compile the block out (`#if`, which makes the flag do nothing) so the kernel
is the pre-116 one; (c) a two-build A/B, pre-116 vs this build with the leap OFF, to settle it (a measurement task).

## For the doc
Task 116: rising shell rays above their column's terrain now leap to the column's edge. The image is exactly the same and the step
count fell a lot, but the frame is not faster, so it ships OFF.
- Shell steps per sky ray at walk_near: **82.2 -> 29.5**. Shell steps per pixel at walk_open: **30.3 -> 19.0** (`Logs/costsplit_2026-10-22_202941.txt`).
- Frame p50, leap OFF -> ON: walk_near **63.6 -> 65.4 ms** (noisy: OFF passes 61.9 / 66.0), walk_open **45.7 -> 45.5 ms**.
- Walk p50, 3 min, ABBA: **41.70 -> 41.71 ms**. Frames > 33 ms: **~80% both** (`Screenshots/116/walks_116.txt`).
- Image: **0 px** differ (no-upscaler stills, both poses).
- Image `Screenshots/116/sheet_116.jpg` (not committed; 4 rows: walk_near OFF, ON, walk_open OFF, ON):
  - Left column = beauty. It is the same in ON and OFF.
  - Right column = shell-march step heat: blue few, green, yellow, red = many; black = tier 0.
  - walk_near (rows 1-2): look at the top-right sky patch, red/yellow -> green/blue.
  - walk_open (rows 3-4): look at the sky over the ridges, yellow/red -> green/blue. The red ridge hits at the bottom left do not change.
