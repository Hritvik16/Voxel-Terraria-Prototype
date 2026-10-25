# 135-check-r6-suite-and-pixel: DONE. Fast suite green on 134's code; the cliff2 pixel is the GPU compiler, not the hoist; Playground.app rebuilt from the committed code.

## What changed
- No code change. The temporary `TMP134_NOHOIST` edit to `Raymarch.compute` was reverted after the check (`git diff` clean).
- `TASKS.md`: one line under "Task 134". `Screenshots/134/perf134.txt`: the cause-check answer appended.
- `Builds/Playground.app` is again the committed code (73b8ce7 + supervisor commits), built 17:53, 0 errors (`Logs/lookreview_build.log`).

## Evidence
- **Fast suite** on the committed code: PASS 922 FAIL 0 SKIP 15, suite=fast, wall 7m32s (`Logs/overnight/135-suite.out`).
- **Cause check.** Temp build: lean kernel WITHOUT the band hoist. Run: `PRESETS=high EXTRA="-cliffshadow 1 -cliffbefore 2 -shellworkers 6 -cliffwait 240"`.
  - Run details: `Logs/lookreview_20261024_165729.txt`, on AC, shells filled (missing 0).
  - The look review did the build itself; it is the same build as the task's recipe.
  - cliff1 A native0 vs native: 0 px.
  - cliff2 A native0 vs native: 1 px at (260,547), (102,98,90) -> (101,97,89). This is the same pixel and the same values as 134.
  - The no-hoist lean still is **byte-identical (0 px)** to 134's hoisted lean still (`LookReview/2026-10-24_144938_high`). The diag stills also match across the two launches (0 px).
  - -> Removing the hoist changes nothing. The pixel comes from the GPU compiler: the lean kernel compiles always-off code out, and the compiler makes slightly different float code. There is no source cause.
- **Rebuild check:** the normal build ran the same cliff poses (`Logs/lookreview_20261024_175235.txt`, `-cliffwait 120`, missing 0).
  - cliff1: 0 px. cliff2: the same 1 px.
  - Its lean stills are byte-identical to 134's. The build starts and renders.
- Looked at `Screenshots/135/cliff2_pixel_crop.png`: the pixel sits on the grey stone arches left of the dome. All three panels look the same to the eye.
- Seen in passing, not this task's: the cliff2 A view has a large black jagged shape at the top right (the near cliff's edge). It is in the diag still too, so the diet did not cause it.

## Unfinished / next
- Nothing open from 134. The default stays ON (owner: "keep ON unless you object"). There is no reason left to object.
- The owner may close 134's question 1: (b) is moot.

## For the doc
| check | before | after |
|---|---|---|
| fast EditMode suite on 73b8ce7 | not run (owner pause) | PASS 922 FAIL 0 SKIP 15 (`Logs/overnight/135-suite.out`) |
| cliff2 A native, diag vs lean WITH hoist (shells filled) | 1 px, 1 level | unchanged |
| cliff2 A native, diag vs lean WITHOUT hoist | unanswered | 1 px, same pixel and values; lean stills with/without hoist 0 px apart (`Logs/lookreview_20261024_165729.txt`) |
| cliff1 A native, all builds | 0 px | 0 px |
| committed rebuild vs 134's lean stills | - | 0 px (`Logs/lookreview_20261024_175235.txt`) |

- `Screenshots/135/cliff2_pixel_crop.png`: top row, left to right: diag (before), lean with hoist, lean without hoist, diff (white = changed). The panels are 8x zoomed around the pixel (260,547), which has a red box. The bottom-left thumbnail is the whole cliff2 frame, with the pixel circled in red on the stone arches. The two lean panels are identical; only the one marked pixel differs from diag, by 1 level.
