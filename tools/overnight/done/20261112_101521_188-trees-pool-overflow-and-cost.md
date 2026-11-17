# 188-trees-pool-overflow-and-cost

Model: opus

Approval: owner, 2026-11-07, Q-T answer (a): keep voxel trees (`-trees 1`) OFF for now; fix the pool overflow and the cost first. Read task 180's HANDOFF (git `cd7e342`), TASKS.md 180 paragraph and KNOWN_ISSUES shell entries.

## Do
- Reproduce what 180 reported: in dense forest the shell pools overflow (some far chunks missing) and far chunks cost ~2x. First write down the exact numbers and which pool (name, capacity, peak use) from the task's own runs.
- Find the CAUSE before changing anything (isolate: tree voxel count per shell chunk, pool bytes per chunk, brick density). Fix by an exact, data-derived means (e.g. tree voxels as sparse overlay not dense bricks, coarse-level tree representation that keeps the trunk/canopy envelope, pool accounting that counts the real bytes), not a cap tuned to the current forest. Prove on the densest forest seed and an adversarial case (a solid forest cover).
- Flag stays `-trees 1`, default OFF. Near == far agreement must still hold; keep every 180 test green; fast + whole suite.
- Report: pool peak / capacity before -> after; per-chunk generation cost ON vs OFF before -> after from the task's own runs (tests/metrics report, no rig). Shots of a dense forest ON before/after at distance. Captions.
- Do NOT touch the black-shadow look (lighting task later).

## End
Done, default stays OFF; the owner decides the default from the numbers. HANDOFF with the table.
