STOP_FOR_OWNER -- 168c-perf-c1-shadow-continuation: C1 is NOT exact (12 / 55 px, each off by 1 level) and, in an indicative cold run, SLOWER. It stays OFF behind `-sundefer 1`. No warm measurement was taken: the task's rule for a non-exact result is to stop.

## Owner question (decision table: "STOP_FOR_OWNER for a non-exact result")
Sun visibility does NOT feed the shading linearly, so C1's "a short pass applies the sun visibility" can only work by shading every handed-on pixel twice (once lit, once blocked).
In an indicative cold launch, that double shading cost more than moving the march out saved.
- **(a) Drop C1 (recommended).** The flag stays OFF and play is unchanged. Unblock 168d (E, streamer time-slicing), which `Requires: 168c done`. 168b already showed that the tail is streaming, not shading.
- (b) A design task for C2-style split shading: the shading inputs go to a G-buffer, and one shading pass runs after all sun visibility is known. That means more scratch (~16+ B/px), and the same +/-1 code-gen risk.
- (c) Accept a +/-1-level difference as "exact". This is pointless for this form, because it is slower anyway.
168b's note still holds: exact shading cuts cannot reach the <= 5 % > 33 ms tail.

## What changed (`5ceaea5`, flag OFF by default, so play is unchanged)
- `Raymarch.compute`: new kernels `CSMainFarShadeLeanSun` (`VE_FAR_SHADE VE_DIET2 VE_SUNDEFER`) and `CSSunDeferLean`.
  - In the Sun kernel, task 133's near continuation and task 80's shell-hit sun march are not run. Instead, a `[loop]` shades from the colour onward twice: pass 0 uses the unblocked sunVis and writes the target; pass 1 uses sunVis 0 and writes `SunDeferB`. VAO, point lights and bleed are reused from pass 0. The ray goes into the pixel's `ShellPre` entry (e = 4).
  - `CSSunDeferLean` runs after pass 2. It marches the ray; on a hit it copies `SunDeferB` into the target and writes 0 into the temporal history.
  - Every task-168c line is under `VE_SUNDEFER` (a test pins this), so the other kernels are unchanged.
- `RaymarchFeature.cs`: `-sundefer 1|0` (default 0), `KernelDiet2Rule.SunDeferKernel` (needs the pre-pass), `SunDeferB` render-graph texture, dispatch, `[sundefer]` log line.
- Capture tools: cost-split arms `sundefer_on|off` (run line `kernel diet2+sun`), `-costsdpair`, `-cliffbefore 7`, `[cliffkernel]` lines. `tools/roof168.py pair <stills> sd` / `arms <stills> sundefer_on`.
- Tests: `SunDeferTests` covers the non-linearity twin, the march arguments matching the inline ones, the ifdef scan and the rule. TASKS.md paragraph added.

## Evidence
- Linearity: on paper, sunVis passes through lerp -> UniformFaceHue -> haze -> grade shoulder -> `pow(saturate(x), 2.2)` -> SeeCompose. Not linear.
  The test confirms it: f(0.5) != (f(0)+f(1))/2, and a bright face clips, so f(0.6) == f(1) but f(0) is darker.
- 0 px check (`Logs/costsplit_2026-11-02_145837.txt`, stills `~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/UpscalerClips/2026-11-02_145837/stills`, `roof168.py`):
  - Control raw/raw2 is 0 px. The in-place flip (sun defer OFF -> ON -> OFF) changes **12 px at walk_near and 55 px at walk_open**, and back again. raw2/back is 0 px.
  - All the changed pixels sit on lit terrain (not water). Each differs by +/-1 in one 8-bit channel, in both directions, and the counts are deterministic.
  - Reading: these are float code-gen differences in the restructured shading, crossing a quantization step. A wrong sunVis would flip whole shadows.
  - cliff1 was not shot: it was not needed once walk_near / walk_open failed.
- Scratch: `SunDeferB` uses the target's format, B10G11R11 (4 B/px). That is 1.27 MB at 432 rows (log `[sundefer] SunDeferB 768x432`) and 3.5 MB at 720 rows. Within the 3-6 MB budget.
- Indicative only (one cold launch, shells still filling, 8 s runs; NOT a measurement), p50:

  | view | sun kernel | FULL |
  |---|---|---|
  | walk_near | 67.7 / 68.0 ms | 64.7 / 59.9 ms |
  | walk_open | 41.7 / 42.4 ms | 30.1 / 34.0 ms |

  At walk_open almost every lit tier-0 pixel has a continuation, so almost the whole frame shades twice.
- Fast suite: PASS 989 FAIL 0 SKIP 16 (`Logs/overnight/168c_fast.out`). Filtered run PASS 7 (`Logs/overnight/168c_filter.out`).
- Stills looked at: `walk_open_full_raw_sdflip.png` (above dir) shows the noon desert ridge with the sea at the left. The image is normal; the 55 changed pixels are spread over the lit slopes (x 140-1904, y 566-1060).
- Cost: the build now compiles 2 more Raymarch kernels (slower shader compile per build). If (a), the supervisor may want them removed.

## Next
- The supervisor relays (a)/(b)/(c). If (a): mark 168c done (C1 dropped) so 168d runs.

## For the doc
- C1 (sun-ray shell marches out of the big kernel), `Logs/costsplit_2026-11-02_145837.txt`:
  - Sun visibility is non-linear in the shading, so the shading must be split (shade both outcomes).
  - Exactness: NOT 0 px. In-place flip: walk_near 12 px, walk_open 55 px, each +/-1 level (control 0 px).
  - Memory added: 1.27 MB at 432 rows (3.5 MB at 720), B10G11R11.
  - Indicative (cold, not a measurement) p50: walk_near 59.9-64.7 -> 67.7-68.0 ms, walk_open 30.1-34.0 -> 41.7-42.4 ms. Slower: the double shading costs more than the march it moves.
- Image: `.../UpscalerClips/2026-11-02_145837/stills/walk_open_full_raw2.png` vs `walk_open_full_raw_sdflip.png`: the same desert ridge at noon. They look identical; only 55 scattered pixels on the lit slopes differ, by 1 level.
