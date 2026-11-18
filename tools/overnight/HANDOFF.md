# 233-look-sky-sun-grade: DONE. HEAVY. Pale sky + clouds and the grade are ON by default. The low start sun is built but OFF: it costs +27 ms at forest_eye. Commits 87893b5 (code + tests), 3db4292 (runner, reports)

## What changed (constants / per-pixel ALU only, no new pass, 0 GPU bytes)
- `-skypale 0|1` (default 1). The name is new because `-skylook` is already task 121's switch. Sky pixels and the haze target get a pale zenith/horizon and a wider horizon band. Clouds: 2 octaves of procedural value noise on MISS pixels only (`!hit && !farHit`). Lighting (ambient, far-shade sky, sky fill) takes the pale HUE at the OLD luma, so shade changes hue but not brightness, and the jungle trunks cannot go darker.
- `-grade 0|1` (default 1): `Grade233`, just before the ResultTexture write (render rows, before FSR). Greens a little olive, warm mids, S-curve pivoted at 0.40, toe lift, faint vignette. I tuned it offline first (`grade_tune.py`: mean luma -0.2 to +4.5 % on the 226/231 stills).
- `-startsun DEG` (default 0 = noon; `-startsun 16` = the look; morning side, because play's 20-minute day would put an evening start into night within about a minute). It sets play's start view and the cost split's default hours. Why 16 deg: the day factor is full from 14.5 deg, and the gold halo (0.39) and horizon rim (0.50) are still present.
- Sky and grade scale with the day factor, so night is identical by construction. Arms: `startsun_off|startsunNN|skypale_off|grade_off|look233_off|sun233_only|sky233_only|grade233_only`. New capture options: `-coststillarms` and `-costtimedpaths`.
- Why this design is the efficient one (rule 11): sky colours are CPU constants per frame. The cloud ALU runs only on sky pixels, about 8 hashes each. The grade is about 60 ALU ops on pixels already drawn. No textures or buffers. Not timed: any result would sit inside the 3-8 ms noise, so the pass cards stand in for timing (PLAN.md).

## Evidence
- Fast suite `PASS 1105 FAIL 0 SKIP 25` (`Logs/overnight/233/fast.txt`). 6 new `Look233Tests`. Two source-pin tests (SkyLook, DrawnEdgeFade) were updated to the new miss line.
- Sun cost, WARM steady state (warm-up `Logs/tg2world_20261117_165939.txt`), one cost split, ABBA, 432 rows, play flags (`Logs/overnight/233/costsplit_day.txt`). FULL = 16 deg; every arm has sky + grade on. p50 ms:
  pose        noon   16 deg   25 deg   sun off   noise -> sun step noon / 16 / 25
  walk_near   36.9   34.4     35.4     24.8      3.2   -> 12.1 / 9.6 / 10.6
  forest_eye  106.6  133.4    124.3    88.4      8.0   -> 18.2 / 45.0 / 35.9
  P3: `step sun: 45.0 ms at 16 deg (forest_eye, warm) vs share 5.0 ms OVER SHARE` (noon is already 18.2, also OVER SHARE).
  The plan's rule: no low elevation stays within noise of noon at forest_eye, so the default is noon. Note: every in-game morning and evening already shows this low sun, so the +27 ms is a sun-shadow cost play already pays during the day cycle (8.17 step S2). The start time is not the cause.
- Pictures (`Logs/overnight/233/metrics.txt`, 231's distance to LotL; lower = closer): forest_eye before 0.97 -> +sun 0.76, +sky 0.53, +grade 1.01, all three 0.66. redwood 1.07 -> 0.58 / 0.79 / 0.96, all 0.38. jungle 1.12 -> sky 0.68, all 0.84. By eye the sky is the biggest step. The grade alone barely moves anything: it is the weakest piece and the owner's call.
- Grade luma: at low sun (full vs grade_off) +2.8 / +1.2 / +6.1 / +1.0 %, never darker (231's grade lost 10-20 %). At noon +6.0 / +2.0 / +5.2 %. jungle noon -8.9 % is NOT valid: the grade233_only jungle still was framed with the camera turned (trunk moved), the only mis-framed tile, cause unknown.
- Night torch_camp (`costsplit_night.txt`): the sky differs by 0 px and mean luma is equal (35.04 vs 35.03). The leftover speckle on the far moonlit slope (raw mean|d| 0.88 vs floor 0.29) INFERRED: stochastic sky-fill history between arms, not a colour shift.
- Rule 11 (one process per launch, so ON and OFF share one ledger; the pieces allocate nothing): day peak 2,516 MB (232: 2,566), night 2,538. GPU refusals 0 / 0 / 5,360 / 5,360 (232: 2,859, on a different walk arc; the pool is full at jungle, a known issue). CPU pool peak 496,441 (232: 494,866). Valve evictions 0.
- P2 pass cards: no new pass. Clouds go in CSMainBody, render rows, every frame, sky pixels only, 8 hashes, 0 B, step S1/post. The grade goes in CSMainBody, render rows, every frame, about 60 ALU ops per pixel, 0 B, step "shading/post". P4/P5: no content and no ray-march change.
- Launch budget broken: the day launch took 110 min (17:05-18:55) because forest_eye runs at 90-130 ms per frame and each still arm takes about 2 min. The memory watchdog killed 3 builds at 15:21, 15:55 and 16:22 (swap > 10.24 GB: the owner's Firefox held ~7 GB). The 4th build ran with `-job-worker-count 1`. **`tools/overnight/PAUSE` was touched by the watchdog and is still there**: the supervisor/owner should remove it to resume the queue.

## Left / next
- Not mine, not committed: `Assets/Scenes/Playground.unity`, `Logs/TerrainGen/*`. Images are git-ignored (`Screenshots/233/`).
- Owner: look per piece; `-startsun 16` only if the forest +27 ms is acceptable, or after S2 sun work. Supervisor: HEAVY means run the budget check. Low-sun sun cost is the S2 problem to take next.

## For the doc
- Look: distance to LotL (lower = closer), all three pieces vs before: forest 0.97 -> 0.66, redwood 1.07 -> 0.38, jungle 1.12 -> 0.84 (`Logs/overnight/233/metrics.txt`).
- Pale sky alone is the biggest step (forest 0.97 -> 0.53). The grade alone is about neutral (1.01). Grade at low sun: luma +1 to +6 %, no darkening.
- Low sun cost (warm ABBA, p50): forest_eye 106.6 -> 133.4 ms at 16 deg (+26.8, noise 8.0), 124.3 at 25 deg. walk_near 36.9 -> 34.4. So `-startsun` stays OFF (noon).
- Memory: 0 bytes added; peak 2,516 MB (232: 2,566). Night unchanged (sky 0 diff, mean luma equal).
- `Screenshots/233/sheets/sheet_day.png`: rows forest / jungle / walk_near / redwood; columns before | +low sun | +pale sky | +grade | all three. Look at the sky (pale, soft clouds) and the grass (olive in the last column). walk_near is a wall close up: the low sun lights it brightly.
- `Screenshots/233/sheets/lotl_side_by_side.png`: ours before | ours all three | LotL. Closer: the pale cloudy sky (forest row) and the warm rock (redwood vs their canyon). Not closer: houses, props, characters, framing, and their soft light gradients. The jungle also gets darker at a low sun.
- `Screenshots/233/sheets/viewer.html`: full-size flip, forest_eye and redwood_far1 before / all three (Space flips, arrows change the pose).
