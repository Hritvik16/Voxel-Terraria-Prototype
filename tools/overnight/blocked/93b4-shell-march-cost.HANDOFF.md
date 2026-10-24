STOP_FOR_OWNER -- task 93b4: the shells' +12-15 ms is the PRIMARY shell march's ITERATION COUNT, mostly air-brick steps (43-75%).
The one fix that needs no design (read caches, byte-identical) is in and saves only 0.6-2.4 ms. Question: may the next task
(a) add an AIR-BRICK MASK per paged shell chunk (64 bits = which 4x4x4 brick groups hold any non-air brick, ~50 KB GPU, a new
buffer + ShellStore upload + CPU twin; exact, so byte-identical or 1 LSB) so a ray leaps a whole empty brick group per iteration?
Estimate (NOT measured): iterations -40-50% walk_near / -25-30% walk_open, about -5-7 / -4-6 ms. Or (b) end upward sky rays at a
per-shell max-solid height (cheaper, but NOT byte-identical: the far band may draw where the shells said air), (c) lower
`ShellIterCap` 384 (changes the look), (d) leave it. Recommended: (a). 93b (Requires this task) should wait for that answer.

# 93b4 shell march cost: SPLIT done; byte-identical read-cache fix in (-2.4 / -0.6 ms); the rest needs design

## What changed (commit 7a5231e, engine-phase)
- `Raymarch.compute`: `ShellMarch` keeps one-entry caches (`SHELL_CACHE_ON`): ShellResolve's answer per (kd, kd's chunk) unless that
  chunk is in kd's hole (`whole`), and the brick handle per (shell, brick). Under `#ifdef VE_FAR_SHADE` only (capture diagnostics):
  ShadowStats 48-59 (shell iterations by ray kind, by step kind, reads), `_DebugShellSkip` (bit 0 shell shadow off, bit 1 primary
  shell march + far march off, bit 2 caches off).
- `RaymarchFeature.cs`: `STATS_LEN` 48 -> 60, `DebugShellSkip`. `PlaygroundCapture.CostSplit.cs`: arms `shellshadow_off`,
  `shellmarch_off`, `shellcache_off`, `-coststillraw` (no-upscaler stills x2 per arm), the `[shell ...]` counter lines.
- CPU twin `ShellRayReference.March(..., useCache)` + `LookupHandle` + `Resolve(.., out whole)`; new `ShellMarchCacheTests`.
- KNOWN_ISSUES "SHELLS COST" entry (SPLIT paragraph) and its CLAUDE.md index line.

## Evidence (AC power both launches; warm steady state)
- Split: warm-up `Logs/tg2world_20261016_223104.txt` (discarded), then `Logs/costsplit_2026-10-16_223720.txt` (copy in `Screenshots/93b4/split/`).
| arm (saved p50 ms vs bracketing FULL) | walk_near (FULL 62.4, noise 3.9) | walk_open (FULL 44.7, noise 0.8) |
|---|---|---|
| shellmarch_off (primary shell march + far march skipped) | +13.7 | +19.1 |
| shellshadow_off (shell-hit sun shadow skipped) | +3.0 (only 26 rays/frame: noise) | +2.4 |
| shells_off (tiers 1-2 drawn) | +15.5 | +11.3 |
- Counters: walk_near 5.8% of pixels (top-right sky) run the march at 94 iterations each, 2.85 M/frame (5.5 per pixel): the SIMD lanes
  of their groups wait, which is why so few pixels cost 13.7 ms. walk_open 62% of pixels: no-hit 40.7 iters/ray (end ABSENT ~51 m),
  hit 105.9 iters/ray, 30.3 per pixel. Iterations by step (near / open): air bricks of paged chunks 75 / 43%, uniform air chunks
  13 / 34%, tier-0 leaps 10 / 17%, cells 1 / 6%. Step 1.3: nothing else runs per pixel where tier 0 hits (shellmarch_off removes
  13.7 of walk_near's 15.5 ms; shell shadows run only from shell hits). Lines: `ShellMarch(rayStart, rayDir, ...)` in CSMainBody
  (the primary call) and the loop's `LeapSpanReseedClosedForm(... (voxel >> sh) << sh, 1 << sh ...)` taken with sh = k + 3 (one brick).
- Fix ABBA (FULL = caches on, X = `shellcache_off`, F X X F in one launch): warm-up `Logs/tg2world_20261016_232811.txt`, then
  `Logs/costsplit_2026-10-16_233425.txt` (copy in `Screenshots/93b4/abba/`). p50 before -> after: walk_near 66.3 -> 63.7 (noise 1.6),
  walk_open 47.9 -> 47.3 (noise 0.2). p99: walk_near 103.8 -> 197.6 pooled, but that is ONE FULL run's hitches (pass 2 p99 206.8;
  pass 1 102.9); walk_open 81.6 -> 79.8. Directory reads per iteration 0.90 -> 0.20 / 0.89 -> 0.46; page reads unchanged (0.75 / 0.45:
  each air-brick step is a new brick). Iteration counts identical on/off.
- Identity: no-upscaler stills `*_raw.png` cache on vs off: 0 pixels differ at both poses; raw vs raw2 (10 frames later) also 0 (static scene).
- Tests: `ShellMarchCacheTests` (3,100 rays incl. rays through every hole corner's tip, + their shadows: every Result field equal on/off,
  reads 55%); a mutation (cache kept through holes) FAILS it. Filter run 10/10 with ShellHandOverTests + ShellOrderTests.
  Fast suite **PASS 809 FAIL 0 SKIP 14** (`Logs/overnight/93b4-fast.out`).

## Not done
- The air-brick mask (option (a) above) and any other iteration cut: design + new GPU buffer, owner's call. 93b's re-run should wait.
- Shell fill while walking is unchanged (93b2d's worker shortage).

## For the doc
Task 93b4: the shells' extra cost is the primary shell march, and inside it the step count through empty bricks, not the shadow ray.
- Primary shell march: **13.7 ms** (walk_near) / **19.1 ms** (walk_open) of p50; shell shadow ~2.4 ms (`Logs/costsplit_2026-10-16_223720.txt`).
- Iterations: 94 per sky ray at walk_near (5.8% of pixels), 30 per pixel at walk_open; **75% / 43% are one-brick steps through air**.
- Read caches (byte-identical, 0 px differ): p50 **66.3 -> 63.7 ms** / **47.9 -> 47.3 ms**; directory reads per step 0.90 -> 0.20 (`Logs/costsplit_2026-10-16_233425.txt`).
Images:
- `Screenshots/93b4/sheet_93b4.jpg`: top left = walk_open beauty (no upscaler; desert ridges, sea and snow at left, sky above);
  top right = its shell-march heat: blue (<= 24 steps) high sky, green (25-48) sky near the horizon, yellow/red (49-96+) distant ridges
  on the right and the slope at lower left; black = tier-0 hits (no shell march). Bottom = walk_near: a canyon wall almost all tier 0
  (black); only the top-right sky patch marches the shells, all red (96+), and that patch costs the 13.7 ms.
- `Screenshots/93b4/abba/walk_open_full_raw.png` vs `walk_open_shellcache_off_raw.png`: the same frame with the caches on / off,
  byte-identical (look at anything: there is no difference).
