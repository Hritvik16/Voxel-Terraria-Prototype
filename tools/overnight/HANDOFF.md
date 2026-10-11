STOP_FOR_OWNER -- Roof skip makes play SLOWER on tg2, so I left it OFF. Pick one:
(a) keep it OFF; step 8 picks another shadow cut (recommended);
(b) ON anyway (it saves 1.5 ms only facing a wall at noon);
(c) a follow-up that rebuilds the pyramid only when the mirror changes. That saves at most the ~3 ms build, so it is roughly break-even with OFF.

# 59-engine-8-roofskip-on: STOPPED. Exact (0 px) and Gate 3 green, but ON is slower in 3 of 4 warm arms. The play default is OFF; the toggle is in the menu.
## What changed (branch `engine-phase`)
- d3b7c38: `PlaySettings.roofSkip` (saved; `-roofskip 0|1` overrides it per run, last match wins). `SettingsMenu` row "Roof skip" under Shadow quality (panel 634 px tall). Cost split: capture-only `-costarms a,b` (subset or repeats), `-costsun DEG` (evening sun DEG up), and arm `roof_on`. `run-phase7-rig.sh` passes `EXTRA`. `run-play.sh` comment updated. +1 PlaySettings test.
- 0bd1b4a: default `roofSkip = false` after the A/B (a file saved by task 58 also gets OFF). The menu hint quotes the numbers. Diagnostic arm `roof_build` (`RaymarchFeature.DebugRoofBuildOnly`: builds the pyramid but the march ignores it; capture-only).
- Rigs and capture runs are unchanged (OFF unless they pass `-roofskip 1`).

## Evidence
- Exactness, `-roofskipab` (9 poses, release, fullscreen). Run 2: beauty 0 px and sun visibility 0 px at all 9 poses; steps/ray 68.6 -> 41.6 (-39%) (`Logs/overnight/59_roofskipab2.out`). Run 1: high_golden differed on all 2,073,600 px because its OFF frame (skip not active) was blank; sun visibility was 0 px there too. I treated it as a capture glitch, not the skip, because run 2 was clean. Images below.
- Gate 3, `EXTRA="-roofskip 1" run-phase7-rig.sh`: 430/438 = 98.17% (>97%), the same 8 mismatches as today's OFF run 204717. Rig PASS 37 FAIL 0 (`Logs/overnight/59_phase7.out`).
- Warm A/B at WARM STEADY STATE. Warm-up `Logs/tg2world_20261010_224242.txt` (discarded), then back to back. Each launch runs F R R F R R F on both arcs (ABBA x2), FSR 3 at 540, fullscreen 1920x1080. p50 / p99 ms; "slower" = ON costs more:
| sun | arc | OFF p50/p99 | ON p50/p99 | ON - OFF | in-launch noise |
|---|---|---|---|---|---|
| noon 78 deg | walk_near (wall) | 48.0 / 56.7 | 46.6 / 57.5 | **-1.5 (faster)** | 0.0-0.1 |
| noon 78 deg | walk_open | 28.7 / 33.6 | 32.2 / 37.0 | **+3.5 slower** | 0.0-0.3 |
| golden 10 deg | walk_near | 36.7 / 45.5 | 39.0 / 49 | **+2.3 slower** (1.9/2.6) | 0.1-0.3 |
| golden 10 deg | walk_open | 34.9 / 41.8 | 39.6 / 50 | **+4.8 slower** (5.5/4.1) | 0.0-0.8 |
  Logs: `Logs/costsplit_2026-10-10_224908.txt` (noon), `_225726.txt` (10 deg). Every difference is well above 57's noise floor (1.4-1.6 ms).
- Cause, isolated (`Logs/costsplit_2026-10-10_230831.txt`, 10 deg, same launch). Pyramid build alone (`roof_build`): +3.4 ms near, +3.0 ms open. Build plus skip (`roof_on`): +2.9 ms near, +3.8 ms open. So the per-frame build (BuildHeightRaw scans every column of the tg2 window top-down) costs ~3 ms. The skip itself saves only 0.6 ms (near) or costs 0.8 ms (open): tg2's tall terrain keeps the coarse pyramid levels high, so a step pays up to 6 height reads and rarely skips. September's win was on Playground's low terrain.
- Fast suite PASS 648 FAIL 0 SKIP 14, 0 `error CS` (`Logs/overnight/59_fast2.out`).

## Not done / next
- The menu row was not clicked (headless). Fast suite only; no terrain code changed.
- Next, after the owner answers: step 8's ONE shadow change. If (a), step 8 is still open. 57's ranking says sun shadows cost 14.2 / 4.2 ms; rung 1 (half-res beyond LOD1) has never been measured on tg2.

## For the doc
- Exact: 0 differing px at 9 poses (run 2); Gate 3 98.17% with ON, the same as OFF; rig PASS 37 FAIL 0.
- Noon, p50 OFF -> ON: wall 48.0 -> 46.6 ms (20.8 -> 21.5 fps); open 28.7 -> 32.2 ms (34.8 -> 31.1 fps). `Logs/costsplit_2026-10-10_224908.txt`
- Sun 10 deg, p50 OFF -> ON: wall 36.7 -> 39.0 ms (27.2 -> 25.6 fps); open 34.9 -> 39.6 ms (28.7 -> 25.3 fps). `Logs/costsplit_2026-10-10_225726.txt`
- Cause: the pyramid build alone costs +3.0-3.4 ms/frame; the skip saves <=0.6 ms. `Logs/costsplit_2026-10-10_230831.txt`
- Result: play default stays OFF; "Roof skip" On/Off is in the F6 menu.
- Image `Screenshots/59/golden10_walk_near_roof_on.png`: walk_near at sun 10 deg, roof skip ON, FSR 3 at 540. The right half is the lit orange canyon wall with the sun behind-left; the lower left is covered by the debug HUD. The OFF frame for comparison is `golden10_walk_near_off.png`.
- Images `Screenshots/59/run1_high_golden_OFF_blank.png` / `_ON.png` / `_DIFF.png`: exactness run 1's glitch. The OFF frame (skip not active) is blank (blue with a dark top band), the ON frame shows terrain, and the DIFF is magenta over the whole frame. Run 2's same pose matched exactly (`run2_high_golden_ON.png`).
