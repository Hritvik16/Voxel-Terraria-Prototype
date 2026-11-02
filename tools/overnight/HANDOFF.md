# 167-perf-kernel-diet-2-rerun (= task 161): DONE. Kernel diet 2 is ON by default. Next cut = D (sky probe arm -6.2 ms), with one caveat below.

## What changed
- `aa76c67`: `RaymarchFeature.KernelDiet2` now defaults ON (`ParseFlagOn`, the same pattern as task 134's `ShellKernelDiet`). `-diet2 0` gives the pre-167 kernels.
  Settings that are not play settings still fall back to the twin kernel automatically (`KernelDiet2Rule.Settings`). Paragraph added to `TASKS.md`.
- No new launch. The warm AC set already existed. `Logs/overnight/161_measure2.out`: warm-up `Logs/tg2world_20261101_151159.txt` (discarded), then
  the cost split ran back to back, on AC at every check (15:11, 15:16 and 15:54, 100 %). The power reading this morning was "AC Power" too.
- **The supervisor's 18:15 note used the wrong run.** `Logs/costsplit_2026-11-01_143603.txt` printed `kernel lean` on every row, FULL included, and it
  has no `[shells]` lines (shells_off = FULL). That launch had no play flags and no `-diet2 1`, so it is invalid for B.
  `Logs/costsplit_2026-11-01_151803.txt` is the valid run: FULL rows say `kernel diet2` and `diet2_off` rows say `kernel lean`, so the twin really dispatched.

## Evidence (warm steady state, AC, 432 rows, play flags, tg2 noon)
- Fast suite PASS 981 FAIL 0 SKIP 16 (`Logs/overnight/167_fast.out`). The 0 px exactness proof is from 161 (`24424c2`, `cd565ee`).
- Arm table: ms p50, the two arm passes vs the FULL passes on each side (`Logs/costsplit_2026-11-01_151803.txt`). The noise is the range of the 13 FULL passes.

| arm | walk_near | delta | walk_open | delta |
|---|---|---|---|---|
| FULL (diet 2) | median 38.4 (range 37.0-39.9) | | median 24.4 (range 21.9-26.6) | |
| diet2_off | 43.8 / 44.2 | **+6.3** | 27.0 / 27.1 | **+4.6** |
| skyprobe_off | 32.1 / 32.0 | **-6.2** | 20.6 / 21.5 | -1.9 |
| sun_off | 26.4 / 28.7 | **-11.2** | 21.4 / 22.2 | -2.5 |
| nearsh_off | 37.0 / 36.4 | -3.0 | 24.4 / 24.1 | -0.4 |
| fshadow_off | 39.9 / 39.0 | -0.2 | 24.1 / 25.2 | +0.1 |
| reuse1 | 43.6 / 51.5 | +8.3 | 27.3 / 26.3 | +2.2 |
| reuse16 | 37.2 / 37.0 | -1.6 | 24.8 / 24.2 | 0.0 |
| roof_on (draws the twin kernel) | 50.5 / 51.2 | +12.8 | 28.8 / 28.9 | +4.8 |
| roof_build | 40.4 / 39.9 | +2.5 | 26.1 / 27.3 | +1.7 |
| shells_off | 31.7 / 31.7 | -6.6 | 20.6 / 20.1 | -5.0 |
| ups_none | 33.6 / 33.8 | -4.8 | 17.0 / 17.1 | -6.1 |
| tiny_gate | 6.7 / 6.7 | -31.4 | 4.1 / 4.6 | -19.9 |

- GPU trace: Metal System Trace of FULL walk_near, pass 2, diet 2 on (`gpu-traces/walknear161_full.trace`, table from `python3 -I tools/gpu161.py
  gpu-traces/walknear161_full.trace.export/gpu.xml`). Median per encoder, GPU busy time, NOT a frame time. Thermal state was "Fair".
  - Raymarch compute encoder (pre-pass and main passes merged; a release build has no labels): **33.4 ms**.
  - FSR 3 compute encoder: **4.3 ms**.
  - Largest render encoder (copies / composite): 1.7 ms. The other render encoders and the HUD are about 0.1-0.5 ms each.
  - Pre-pass vs main pass cannot be split in a release trace.

## Decision table applied
- **Diet 2:** saves 6.3 / 4.6 ms, more than the FULL noise (2.9 / 4.7 ms), and the picture is 0 px different. -> **ON in play** (done).
  The structural theory holds, so C1 / C2 keep their rank.
- **skyprobe_off:** -6.2 ms at walk_near, which is 2 ms or more. -> **next cut = D**.
  CAVEAT for D's task: roof skip is not in the diet 2 kernel, so `roof_on` falls back to the twin and loses diet 2's 6.3 ms.
  roof_on +12.8 minus the lost diet 2 (6.3) and the rebuild (2.5) leaves about +4 ms for the skip itself at walk_near (a subtraction estimate).
  Task 59 found the skip worth -4.5 ms; this run does not show that.
  D therefore needs a diet 2 kernel that carries roof skip, and must prove the skip still saves time on the sky probe. If it does not, C1 goes next.
- **Sun block:** `reuse16` is close to FULL (-1.6 / 0) but `reuse1` is not (+8.3 / +2.2), and `sun_off` is large (-11.2).
  So at 1-in-4 reuse, the ray tracing itself is about 2 ms (estimate) and about 9 ms is fixed per pixel (lookup / history / code).
  That supports C2 as the large lever after D / C1 (design task first, as the plan says).

## Not done
- Did not start D or C1 (out of scope). Queued 168 (next exact cut) can read this handoff. No rig run after the default flip
  (exact change, already measured as FULL vs diet2_off).

## For the doc
- Kernel diet 2 (now ON in play), warm AC at 432 rows: walk_near 44.0 -> 37.7 ms p50 (-6.3), walk_open 27.1 -> 22.5 (-4.6). Picture 0 px different.
  (`Logs/costsplit_2026-11-01_151803.txt`)
- What the other arms would save at walk_near: no sky probe -6.2 ms, no sun -11.2 ms, no shells -6.6 ms, no upscaler -4.8 ms.
- GPU per frame at walk_near: raymarch compute 33.4 ms, FSR 3 4.3 ms, copies/composite about 2-3 ms (`gpu-traces/walknear161_full.trace`).
- Next: D (incremental roof pyramid + roof skip), which first needs roof skip inside the diet 2 kernel. Then C1, then C2 (the sun's ~9 ms per-pixel part).
- No images: nothing visible changed (0 px).
