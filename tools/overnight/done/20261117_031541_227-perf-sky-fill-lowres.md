# 227-perf-sky-fill-lowres
Model: opus
Requires: 225-look-trunks-not-black
Approval: OWNER 2026-11-16 (Q-AN): "lower-resolution sky and bounce light, like Lumen's Low setting (sky fill costs 40 ms today)... Is there no way to make lighting cheaper? It seems like this is the main issue that needs to be solved."
Performance task: timing IS allowed here (owner rule 10), warm steady state per CLAUDE.md, ABBA interleaved, write what each outcome would make us do BEFORE measuring.
## Do
1. Read task 193/194a (sky fill 2 = sky + ground bounce) in TASKS.md and 212's cost split. Find where the 40 ms goes (rays per pixel, steps per ray, the sky visibility lookup).
2. Build the efficient design: compute the shade light at LOWER resolution (e.g. a quarter or an eighth of the pixels, or a world-space probe grid near the camera) and upsample with a depth/normal-aware filter, so edges of trunks and ledges do not bleed. Sky fill stays on the same switch values (`-skyfill 2` = new path, `-skyfill 3` or a new flag = keep the old full-res path for A/B).
3. Prove: pixel difference vs the old path at walk_near, forest_eye, jungle_eye, torch_camp (mean |d| and % pixels > 32 against the 219/221 noise floor); picture sheet; adversarial case with thin trunks and a ledge edge.
4. Cost: ms saved at walk_near (warm, ABBA), memory (rule 11: GPU bytes added, expect small).
5. Report the new "Fast lighting" idea for the preset ladder only as numbers; do not change preset defaults. Fast suite. STOP_FOR_OWNER if the frozen layout changes.
