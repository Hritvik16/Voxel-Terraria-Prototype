STOP_FOR_OWNER -- task 161: the code and the exactness proof are done, but the MEASUREMENT DID NOT RUN. The Mac stayed on battery for the whole
2 h wait (19:03-21:03, 58 % -> 52 %, `Logs/overnight/161_acwait.txt` + this session's checks every 5-10 min).
**Q1 (recommended (a)):** (a) plug in the Mac and requeue 161's measurement as-is (commands under "Next"; ~1.5 h incl. warm-up). Diet 2 then goes ON in
play if the table says so. (b) Turn `-diet2` ON in play now on the preliminary numbers below (picture proven identical), and measure at the next milestone.

# 161-perf-kernel-diet-2: code + correctness DONE (exact, 0 px); warm AC arm set NOT RUN (battery); diet 2 stays behind `-diet2 1` (default OFF)

## What changed (branch engine-phase)
- `24424c2`: `Raymarch.compute` `CSMainFarShadeLean` / `CSShellPrepassLean` (`VE_DIET2`: OFF-in-play uniforms folded to `static const`, no
  `DebugOut`/`isDebugPixel`, no rung-1 quad reuse, no dead one-pass primary `ShellMarch`). `RaymarchFeature.KernelDiet2` (`-diet2 1|0`, default
  OFF) + `KernelDiet2Rule.Settings` + `Diet2DrawnLastFrame`; assigning `DebugPixel` keeps the twins. Cost split: `diet2_on/off`, `skyprobe_off`,
  432 clamp from `CaptureMinRows`, `-costd2pair`; look review `-cliffbefore 5`. `KernelDiet2Tests` (2). `tools/diet2_161.py`, `tools/gpu161.py`.
- This commit: `TASKS.md` "Task 161" paragraph, this HANDOFF.

## Evidence
- Fast suite: `PASS 971 FAIL 0 SKIP 15` (`Logs/overnight/161_fast.out`).
- Identity (raw no-upscaler stills, `python3 -I tools/diet2_161.py`): walk_near + walk_open, lean vs twin **0 px** in every pair (raw / raw2 / flip /
  back, 8 pairs, controls 0 px; the d2pair lines confirm the kernel switched: `kernel diet2` / `lean`). Run `Logs/costsplit_2026-10-31_171648.txt`.
  cliff1 A native (no jitter): **0 px** (`LookReview/2026-10-31_184831_high/cliff1_A_native{0,}.png`, `Logs/lookreview_20261031_184726.txt`).
  cliff1 A/C `play` stills differ (1.3-1.4 M px): FSR 3 is jittered/temporal, so not an identity measure (as in 134/153).
- PRELIMINARY timing, NOT the result (AC but battery 7-8 % charging, no warm-up, diet2_off arm only, 432 rows):
  `Logs/costsplit_2026-10-31_171648.txt`: FULL (diet 2 ON) vs `diet2_off`: walk_near 60.7 vs 68.4 ms p50 (**-7.3**, FULL spread 2.3),
  walk_open 22.2 vs 26.9 (**-4.8**, spread 0.1); gpu* 144 vs 201 / 61 vs 72 (relative only). The earlier launch `..._165802.txt` was noisy
  (FULL range 16-24 ms; an xctrace attach ran during it): not used.
- PRELIMINARY GPU traces (release player, xctrace attached in the 171648 launch; `gpu-traces/walknear161_{on,off}.trace`, table by
  `tools/gpu161.py`). A release player has NO debug-group labels, and Unity merges consecutive compute dispatches into one encoder, so
  **pre-pass vs main passes cannot be split** in this trace (the dev build `Builds/PlaygroundTrace.app` would label them). Per frame, median:
  compute encoder 7 (the raymarch chain: pre-pass + main + sky + far) **30.5 ms ON vs 62.0 OFF**; encoder 9 (~FSR 3) 6.6 / 5.6; render
  encoders (copies, HUD, minimap) ~3-4 ms. GPU perf state "Medium" most of the trace. The GPU halving vs -7 ms frame time is NOT
  explained (each trace caught a different run; 10.9 vs 18.4 frames/s under the tracer): treat it as direction only.

## Decision table (task file) -- cannot be applied without the warm AC run
- diet 2 >= 1 ms beyond noise: preliminary says yes (-7.3 / -4.8) -> lean ON in play, once confirmed warm on AC.
- `skyprobe_off` >= 2 ms -> D next, else C1: NOT MEASURED. `reuse1 ~ reuse16 ~ FULL` with `sun_off` large -> C2: NOT MEASURED.

## Next (the deferred measurement, on AC, back to back)
1. `MODE=stream MOTION=walk ./run-tg2world.sh` (warm-up, discarded).
2. `SKIP_BUILD=1 EXTRA="-renderrows 432 -diet2 1 -costarms diet2_off,skyprobe_off,sun_off,nearsh_off,fshadow_off,reuse1,reuse16,roof_on,roof_build,shells_off,ups_none,tiny_gate" ./run-cost-split.sh`
   (the build in `Builds/Playground.app` is from 17:14 and matches `24424c2`'s sources) with `Logs/overnight/161_trace_attach.sh <run dir> '^ +3 diet2_off' walknear161_full`
   started beside it for the trace. Then apply the table; next = D or C1 per the arms.

## For the doc
- Exactness: lean vs twin raw stills 0 px at walk_near, walk_open, cliff1 A (10 pairs, controls 0 px).
- Preliminary (AC, battery 8 %, NOT warm, NOT the result): walk_near 68.4 -> 60.7 ms p50 (-7.3, noise 2.3), walk_open 26.9 -> 22.2 (-4.8, noise 0.1), `Logs/costsplit_2026-10-31_171648.txt`.
- Preliminary GPU trace, release, unlabelled: raymarch compute encoder 62.0 -> 30.5 ms/frame median, FSR 3 encoder ~6 ms, render/copies ~3-4 ms (`gpu-traces/walknear161_{off,on}.trace`).
- Next cut: undecided (skyprobe_off / reuse / sun arms not run).
- No images: nothing visible changed (0 px). Reference still: `UpscalerClips/2026-10-31_171648/stills/walk_near_full_raw.png` -- walk_near raw frame (diet 2 ON), identical to `walk_near_diet2_off_raw.png`.
