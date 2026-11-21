# 238-perf-split-rest-at-forest: DONE -- "rest" (~58 ms at forest_eye, 16 deg) is mostly CPU floor + shells + far band; row 2 applies (and row 4)
Note: Q-249 (task 249's STOP_FOR_OWNER, the 8.18 cached-sun kill rule) is still open. This task did not touch it.
## What changed (no code)
- `Screenshots/238/report.md` (full report), `costsplit_A/B/D.txt`, `costsplit_C_trees2off_partial.txt`, `input_per_run.txt`,
  `drive.sh`, `drive2.sh`, `perrun.py` (the drivers + the per-run input check). Commit: see git log "238:".
## Evidence (warm steady state; warm-up `Logs/tg2world_20261120_172018.txt`; play's player DEV_KERNELS=0 built 16:51)
Split of "rest" (p50 ms, 432 rows; FULL A 140.4, B 142.3, D 132.3; sun 54.7 / sky fill 25.5 from 235):
- CPU + fixed floor `tiny_gate` **18.6** (walk_near 7.1) | shells **15.0** (walk 7.0) | far band **11.2** (D rerun; noise 5.4)
- look 226 `lotl_off` **-3.1 (~0)** | 233 pale sky **2.6** | 233 grade **4.4** | FSR 3 pass ~5.6 (146, not re-measured)
- remainder (tier-0 primary march + shading) **~4 ms, at most ~22** (if the CPU floor hides under the GPU frame)
- trees: `-trees2 0` FULL 85.6 / 87.1 vs 140.4 = **-54 ms**. Sun step with old trees 33.0 vs 54.7.
Sun levers AT 16 deg (B, FULL range 3.5): sunres4 **10.9**, clamp64 **8.9**, sunfar64 **10.3**, reuse16 **5.2** (all ~0 at noon).
Biggest unknown: `ups_none` saves **59.3** ms, but the upscale pass is ~5.6. That arm also drops FSR 3's per-frame jitter.
Hypothesis: the jitter defeats the keyed temporal sun reuse at a still pose (reuse16's small 5.2 fits). Test = a capture-only arm setting
`RaymarchFeature.Fsr3ZeroJitter` (needs a code change, not done).
Decision table: row 1 (tier-0 most of rest) NO. **Row 2 (floor/shells big at forest) YES.** Row 3 (look > 8 ms) NO, borderline at 7.0.
**Row 4 (sun levers pay at 16 deg) YES.**
Memory (no code change): CPU brick pool peak 495,923-498,923 (valve 484,500 engaged); GPU body pool 245,704 / 254,400, refused 0;
process peak 2,493-2,731 MB. Nothing drawn changed (rule 14 slice n/a). HEAVY: no. No EditMode run: no code changed.
## Owner input / deviations
- `ioreg` HIDIdleTime is reset every ~10 s by a running player, so arms were judged by `Logs/budget/hididle` (real input only).
- Try 1 (16:56) was aborted: the owner was active. Restarted after 20 min idle. far_off (A) had input in both timed windows -> rerun in D, clean.
  Settle-only input in A FULL pass 5 and B sunres4 pass 2 (kept).
- The 10-arm launch was split into A (6 arms) + B (4 sun levers), same flags. Forest runs wait the 180 s settle, so 10 arms in one launch is ~2 h.
- C had no `-costarms`, so it fell back to the default 8 arms. I stopped it after FULL x2 / sun_off x2 / torch_off x2, so there is no `[mem]` for old trees.
- D added `skypale_off` + `grade_off`: plan sec. 2's `look233_off` also switches the 16 deg start sun off (it read 30.6).
## Images looked at
- `Screenshots/238/A/forest_eye_full_clean.png`: FULL, dense trees. Porous leafy crowns fill the upper half at eye level, meadow below, mountains
  and ~25 % sky behind. Nothing floating or missing.
- `Screenshots/238/C/forest_eye_full_clean.png`: same pose, old trees. Big solid trunks cross the near frame and stop the rays early, which is why it is cheaper.
## Next
- Row 2: a forest streaming-floor / shell task (the 520-690-chunk deficit keeps the main thread busy). Row 4: queue S2 (sun-ray cap) behind it.
- Ask the owner whether to approve the one-line capture arm `fsr3_j0` (Fsr3ZeroJitter) first: it is the only test of the biggest number (59 ms).
- Fix in run-budget-check / plan: a FULL-only cost split needs an explicit arm list. No "none" exists, so add one or document it.

## For the doc
Task 238: where the forest frame's "rest" goes (forest_eye, sun 16 deg, 432 rows, warm, p50).

| piece | ms | walk_near |
|---|---|---|
| CPU + fixed floor (tiny_gate) | 18.6 | 7.1 |
| shells | 15.0 | 7.0 |
| far band | 11.2 | 0-2.4 |
| look passes (226 / 233 sky / 233 grade) | ~0 / 2.6 / 4.4 | -- |
| tier-0 march + shading (what is left) | ~4 (at most 22) | ~15 |
| trees (old vs dense, whole frame) | -54 | -- |
| sun levers at 16 deg: sunres4 / clamp64 / sunfar64 / reuse16 | 10.9 / 8.9 / 10.3 / 5.2 | (noon ~0) |

Next: the streaming-floor / shell task (row 2), then S2 (row 4). Open: turning off FSR 3 saves 59 ms here, not the expected ~6.
Suspect: its jitter breaks the sun reuse. Report: `Screenshots/238/report.md`.
Image: `Screenshots/238/A/forest_eye_full_clean.png`: the forest_eye frame. Look at the porous leaf crowns across the upper half at eye level:
every ray through them runs long, which is why this pose costs 140 ms.
