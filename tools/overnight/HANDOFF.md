STOP_FOR_OWNER -- task 116 measured; the rule says OFF and the shell-march line closes. One question at the end (Q72).

# 116-perf-shell-sky-leap: DONE. Exact (0 px), shell iterations cut 37-64%, frame time unchanged, so OFF by default (`-shellcolleap 1`).

## What changed (commits `b7ab1f6` code + docs, `5a32e21` this file; branch engine-phase)
- `ShellStore`: 16 bytes of COLUMN TOPS per page, in the page buffer's tail after the 93b5 masks (`ColumnTops`, `ColTopBase`).
  Byte j = the highest non-air brick y + 1 in 4x4-brick column j. Uploaded with the page; a redraw makes a new page with its own tops,
  so they never go stale. 66 KB on High.
- `Raymarch.compute` `ShellMarch`: a rising ray in an air brick at or above its column's top leaps to the column's edge in one step,
  up to the chunk top (or across the whole chunk when every column is that low). Functions `ShellLeapBox`, `ShellBoxLeapOk`.
  - No far-band height test is needed: the leap stays inside one chunk and one distance band, so it never skips an ABSENT / PAST_LAST
    end, and the far band is always asked from the same t.
- Flag `-shellcolleap 0|1`, **default OFF**. Cost-split arms `colleap_on` / `colleap_off`; counters ShadowStats 65-66.
- CPU twin `ShellRayReference.March(colLeap)`. Tests: new `ShellColumnLeapTests` (6); `ShellAirMaskTests.CheckStored` also checks the
  tops of real tg2 pages; `ShellStorageTests` budget +16 B per page.
- Docs: TASKS.md (Task 116), KNOWN_ISSUES "SHELLS COST" (COLUMN SKY LEAP), CLAUDE.md index line (one clause).

## Evidence
- **Exact on the CPU** (2,376 rays + 687 shadows; holes, absent chunks, floating cells, tier 0):
  - Exit-plane leap (mask off and on): every field equal, t included.
  - Default leap: the same end / shell / cell / normal / material on every ray. 40 t's differ in their last bits (rel <= 1.2e-7).
  - A mutation (tops one brick low) is caught.
- **Exact on the GPU:** raw no-upscaler stills ON vs OFF: **0 px** at walk_near and walk_open.
- **Fast suite** PASS 894 FAIL 0 SKIP 14 (`Logs/overnight/116-tests.out`). Player build: no shader errors.
- **Measurement:** warm, AC (100%), no owner pause, noon, play settings (FSR 3 540, High, shells ON).
  - Warm-up (discarded): `Logs/tg2world_20261022_202312.txt`.
  - Cost split, F X X F (F = leap ON): `Logs/costsplit_2026-10-22_202941.txt` (copy `Screenshots/116/costsplit_116.txt`).
  - Walks A B B A: `Logs/tg2world_20261022_{203606,204059,204554,205047}.txt` (`Screenshots/116/walks_116.txt`).

| | leap ON | leap OFF | change |
|---|---|---|---|
| walk_near p50 (cost split) | 65.3 / 65.6 | 61.9 / 66.0 | ON +1.9 slower (pooled 65.4 vs 63.6; OFF passes 4 ms apart) |
| walk_open p50 | 45.5 / 45.5 | 45.8 / 45.6 | ON -0.2 ms |
| walk p50 (3 min x 2 each) | 41.91 / 41.51 | 41.50 / 41.89 | 0.0 |
| walk p95 / p99 | 77.9 / 74.9, 98.0 / 99.5 | 73.4 / 73.9, 97.0 / 94.1 | ON ~+3, inside the spread |
| walk % > 33 ms | 79.2 / 78.8 | 79.5 / 80.4 | ~0 |
| shell iterations per sky ray (near) / per pixel (open) | 29.5 / 19.0 | 82.2 / 30.3 | -64% / -37% |

- The task's rule: gain < 1 ms at both poses -> **OFF by default, and the shell-march line closes**. (Task 110 had near 60.7 / open 43.0.)
- This is the third big cut in shell iterations (93b5, 93b5 (b), 116) that bought ~0 ms. The march's cost is NOT its step count.
  My guess, not measured: SIMD divergence (hit rays at ~102-106 steps share groups with sky rays), or memory latency / occupancy.

## Left open / risk
- With the flag OFF the new block still sits in the kernel. A comparison inside one build cannot see a static cost (registers / occupancy).
- Weak hint, from a different hour: task 115's walks on the pre-116 build (same flags) had p50 41.9 / 40.7 / 41.1 and > 33 ms 72-78%.
  This build with the leap OFF: 41.5 / 41.9 and 79-80%. That is inside day-to-day spread, but a static cost is not ruled out.
- PERF_30FPS_PLAN row 1 still shows the estimate (supervisor: "measured ~0, OFF"). Next per the plan: 117 (Medium trial).

## Owner question
Q72. The leap's dead code: (a) leave it in, OFF, as 93b5's mask, and go on to 117 (recommended: the static-cost hint is weak, and 117/118
compare arms inside one build); (b) compile the block out (`#if`; the flag then does nothing), so the kernel is the pre-116 one;
(c) a two-build A/B (pre-116 vs this build, leap OFF) to settle it (a measurement task).

## For the doc
Task 116: rising shell rays above their column's terrain now leap to the column's edge. The image is identical and the step count fell
a lot, but the frame is not faster, so it ships OFF.
- Shell steps per sky ray, walk_near: **82.2 -> 29.5**. Per pixel, walk_open: **30.3 -> 19.0** (`Logs/costsplit_2026-10-22_202941.txt`).
- Frame p50, OFF -> ON: walk_near **63.6 -> 65.4 ms** (noisy: OFF passes 61.9 / 66.0), walk_open **45.7 -> 45.5 ms**.
- Walk p50, 3 min, ABBA: **41.70 -> 41.71 ms**; frames > 33 ms ~80% both (`Screenshots/116/walks_116.txt`). Image: **0 px** differ.
- `Screenshots/116/sheet_116.jpg` (not committed). Rows: walk_near OFF, ON, walk_open OFF, ON. Left = beauty (the same in all rows);
  right = shell-step heat (blue few -> green -> yellow -> red many; black = tier 0).
  - walk_near: the top-right sky goes from red/yellow to green/blue.
  - walk_open: the sky over the ridges goes from yellow/red to green/blue. The red ridge hits at the bottom left stay red.
