STOP_FOR_OWNER -- Q84: build the restructure the probes picked (R1, shell pre-pass kernel), or something else? No restructure was started.

# 130-perf-shell-march-probes: DONE (capture only, no default change). About 55% of the shell march's cost is the big kernel around it and 45% is the march itself. Not divergence, not cache misses. Plan rule -> R1.

## What changed (commit 8af33bb)
- `Raymarch.compute`: new kernel `CSShellProbe` (VE_FAR_SHADE, 8x8). It runs the same `ShellMarch` call on the same rays, in a kernel that holds nothing else.
  In the `shellsplit` arms, CSMainFarShade writes each pixel's tTier0 (or -1) to `ShellProbeIO` (4 B/px) and skips its own march (as `shellmarch_off` does).
  E3: per-SIMD counters (8x4 half-tile) gathered in groupshared memory, ShadowStats 67-76.
- `RaymarchFeature.cs`: `DebugShellSkip` bit 5 (32) = split, bit 6 (64) = march twice. STATS_LEN 67 -> 77.
  The probe buffer is allocated only in those arms; CSMainFarShade otherwise gets a 1-element dummy. The probe dispatch runs only in those arms.
- `PlaygroundCapture.CostSplit.cs`: arms `shellsplit` (34) / `shellsplit2` (98), plus a `[simd]` counters line. TASKS.md "Task 130"; one clause in KNOWN_ISSUES SHELLS COST.
- Reports: `Screenshots/130/perf130.txt` (full table), `decisions_before_measuring.txt` (written before the launch), `costsplit_130.txt`.

## Evidence
- Fast suite PASS 921 FAIL 0 SKIP 15 (`Logs/overnight/130-tests.out`). Player build: no shader / C# errors.
- Default path unchanged. walk_open FULL counters = task 116's pre-change run (hit rays 31,813 = 31,813; iterations 3,367,918 vs 3,367,874;
  shell shadow iterations 489,985 vs 489,992; differences are FSR jitter). The only default-path change is a branch on a uniform that is 0 in play.
- The probe marches the same rays: hit rays 26 = 26 and 31,811 vs 31,813; iterations within 1.2% / 0.5%. The gap is exactly the 4 rows below the 540-row frame:
  the main kernel marches and counts them but does not draw them.
- Warm steady state: warm-up `Logs/tg2world_20261023_161730.txt` (discarded), then straight into ONE cost split `Logs/costsplit_2026-10-23_162359.txt`, order F then X X F per arm.
  **On BATTERY** (61% -> 50%, Low Power Mode off, no owner pause). The arm deltas inside the launch hold. Absolute FULL can't be compared with the AC runs.
- No stills looked at: in the split arms the image is wrong by design (misses draw sky), so they are timing only.

## Left open
- R1's shade pass (shell-hit pixels shaded after the pre-pass) is untested. If that pass is another big kernel, part of the ~10 ms could come back.
- E2 is unusual: the warm second march costs MORE than the first (1.33-1.40x). So the march is execution-bound even in a small kernel (3.3 / 0.77 ns per iteration).
  Not explained. With R1 done, the remaining ~8-10 ms belongs to R6 (kernel diet: ShellMarch's per-step ALU).
- E1 sits between the plan's lines (0.45, where A <= 1/3 and B ~ 1). My pre-written tie rule (A if <= 1/2) chose A; it is a weak A.

## Owner question Q84 (after 130's probes)
- **(a) Recommended: R1, the shell pre-pass kernel.** The main kernel does tier 0 + flags, a small kernel marches the shells, then a pass shades those pixels.
  Expected up to ~9-11 ms per standing pose (the context half, minus a ~0.5-1 ms extra pass). Exact, same image.
  Cost: +~10 MB GPU scratch at 540 rows (~18 MB at 720; play peaks ~2.4 GB of the 3 GB cap). Then ONE warm ABBA against today.
- (b) R6 first: slim ShellMarch and the play kernel, dropping the OFF paths (Q72 (b)). No memory, attacks both halves a little, gain unknown.
- (c) Stop: accept the shells' standing-still cost and move on to lighting / world gen.

## For the doc
| p50 ms, warm, battery | walk_near | walk_open |
|---|---|---|
| FULL (4 runs) | 71.3 | 47.5 (passes 2-4) |
| shellmarch_off (no shell march) | 53.1 | 27.4 |
| shellsplit (march alone in CSShellProbe) | 61.2 | 36.9 |
| shellsplit2 (march twice in the probe) | 72.5 | 49.5 |
| march in kernel M / alone S1 / S1 as share of M | 18.2 / 8.1 / 0.45 | ~21 / 9.5 / 0.44-0.47 |
| second warm march S2 (S2 / S1) | 11.3 (1.40) | 12.6 (1.33) |
| E3 SIMD efficiency sum/(32 x max); SIMDs >= 0.6 | 0.906; 95% | 0.932; 99% |
- Outcome rows: E1 = A (weak; the context is ~55% of the cost), E2 = A (execution-bound -> R5 / R4 dropped), E3 = A (no divergence -> no R2).
  Plan §5 rule: E1 = A -> **R1**, no R2; R6 is the follow-on lever for the march's own half.
- R1, gameplay in plain words: none. It changes only how the GPU draws. Same picture, same distances; fluids, edits and physics keep their 128 m reach (8.15).
  Far edits are still drawn in 3D. No frozen API or saved-data change.
- R1 cost: +~10 MB GPU at 540 rows (~18 MB at 720); one extra full-screen pass (~0.5-1 ms).
- Source: `Logs/costsplit_2026-10-23_162359.txt` (copy `Screenshots/130/costsplit_130.txt`), table `Screenshots/130/perf130.txt`.
  No images: the split arms' frames are wrong by design, so this task has nothing to show.
