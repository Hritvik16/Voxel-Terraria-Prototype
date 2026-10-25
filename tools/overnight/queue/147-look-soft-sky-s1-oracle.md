# 147-look-soft-sky-s1-oracle

Model: opus

Approval: owner, 2026-10-25, doc Q94: "pick best recommended" = 1a 2a 3a 4a 5a 6a (SOFT_SKY_PLAN.md section 4). Plan A.
Step S1 only (SOFT_SKY_PLAN.md section 3 step list): CPU oracle `SkyVisReference` + the direction sequence + tests (1)-(5), (7).
No GPU, shader or default change; not a performance task, no timing.

## Do
1. Read SOFT_SKY_PLAN.md (sections 1-3 and the test list it names). Implement the CPU oracle as the plan specifies: a cosine-weighted
   sky-visibility fraction per surface point, deterministic direction sequence, running mean 1/n then 1/16.
2. Tests (EditMode, fast): the plan's (1)-(5) and (7), including ADVERSARIAL geometry built with edit ops (not generator-specific
   cases): sealed room exactly 0, open ground exactly 1, 2 m hole into a 10 m room (Q3 (a) physical), tunnel, ledge, thin 0.1 m wall,
   a 1-voxel pillar. Per the 2026-10-24 rule: exact and data-derived, no tuning to current terrain.
3. Print the quadrature values at task 138's built site (sealed room, tunnel, mouth, ledge) into a report file under `Screenshots/147/`
   (text only). Fast suite once at the end.

## Deliverable
HANDOFF `## For the doc`: the quadrature table, test list, commit. TASKS.md: a "Task 147" paragraph. Next step named: S2 (GPU `-softsky 1`,
OFF) needs one more queue entry; do not start it.
