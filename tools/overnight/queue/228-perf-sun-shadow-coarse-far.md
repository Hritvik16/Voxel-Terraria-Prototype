# 228-perf-sun-shadow-coarse-far
Model: opus
Requires: 227-perf-sky-fill-lowres
Approval: OWNER 2026-11-16 (Q-AN): "coarse far shadows". Sun shadows cost 28.1 ms (task 212).
Performance task (timing allowed, warm steady, ABBA, outcomes written first).
## Do
1. Read how the sun shadow ray is traced today (steps, resolution, which tier it marches). Design: near shadows exact as today; beyond a distance, march a coarse copy of the world (the shell / air-mip level the ray march already has) so far shadow rays take few steps. No new world copy in memory unless it is an existing mip.
2. Proof: near shadows byte-identical inside the near range; far shadows compared with the old path (pixel diff, picture sheet with trees at 5 m, 30 m, 100 m); adversarial: a thin pole and a tall wall far away.
3. Cost vs 212's 28.1 ms and memory (rule 11). Switch: new flag, old path kept for A/B. Do not change preset defaults. Fast suite. STOP_FOR_OWNER on frozen API / layout change.
