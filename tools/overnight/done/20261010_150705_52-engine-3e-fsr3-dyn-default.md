# 52-engine-3e-fsr3-dyn-default

Approval: owner, 2026-10-10 in chat, Q38 answer (a): "FSR 3 dynamic" -- 1080p output, internal rows chosen
dynamically between 540 and 720 (`UpscalerSelect.MinRows`/`MaxRows`), so it climbs toward 720 as the engine
phase frees frame time. Evidence behind the pick: tasks 48, 50, 51 (`tools/overnight/done/*48*`, 50 and 51
HANDOFFs; 51: at 540 rows the upscalers differ by only 1-4 ms; 720 costs +36 ms on the walk today).
This satisfies the old "no TAA/upscaler ON by default until the owner compares" rule: the owner compared.
Queued by the supervisor 2026-10-10. Branch `engine-phase`.

## Goal
Interactive play (`./run-play.sh`, i.e. no capture flags, `RenderPreset.Default`) starts with FSR 3 dynamic
exactly as `-upscaler fsr3 -renderrows dyn` gives today. Flags still override (`-upscaler none|taa|fsr1|stp|fsr3`,
`-renderrows 540|720|dyn`), so every older comparison stays reproducible. Rigs and capture modes keep their
current behaviour unless they pass the flags (do NOT change what any rig measures).

## Steps
1. Make the default; keep F3 (render preset) / F4 (TAA) behaving sanely with it (F3 already takes the frame back
   from -upscaler, task 47: say in HANDOFF what F3/F4 now do in play, and fix only if one leaves a broken frame).
   Show the active upscaler and current rows in the existing HUD line if there is one (no new UI).
2. Fast EditMode suite green. Build; one `SHOT=60 ./run-play.sh` still, midday, sun behind the camera (look at it):
   Player.log must show FSR 3 dyn active and the rows it chose.
3. Update `run-play.sh`'s header comment and KNOWN_ISSUES/CLAUDE.md lines that say TAA/upscalers are OFF by
   default (the TAA and checkerboard index bullets). While editing CLAUDE.md: the `MAPV=3` "byte-identical
   pre-rebuild map" phrase is stale since task 45 (same shapes, Burst floats) -- fix that wording too.
4. Disk: delete task 48's raw stills `Screenshots/Engine03/compare/*__*.png` (owner OK'd after the pick); keep
   the `sheet_*.png` contact sheets.

No frame-time measurement (PREAMBLE rule 10). `## For the doc`: what play starts with now, the flags that
override it, the one still with a caption.
