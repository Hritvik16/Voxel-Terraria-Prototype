# 185-worldgen-c7-stamps-sit-in-land

Model: opus

Approval: supervisor, 2026-11-07, from the approved WORLD_GEN_LOOK_PLAN C7 (listed by the previous supervisor as queueable without a new owner decision); reversible, behind `-stampfit 1`, default OFF.
Read `WORLD_GEN_LOOK_PLAN.md` C7 and finding 7, `StampTests` (keep `NoNeedles_OnTheMap` green), KNOWN_ISSUES "stamp openings need a sea keep-out".
Memory rule: every fix is an exact test on the stamp's own footprint / column heights, no seed-specific constants, proven on 4 seeds + one adversarial case.

## Do
- Cave mouth: placed only where its tube stays covered along its whole length (test on column heights), so a steep slope gives a cave, not a trench.
- Ledges: flat rock ceiling and lip (no egg shapes / contour rings).
- Geyser / wetland basin outline follows the ground (terraces down the slope).
- Lone hoops and slabs need a context rule: in rock or on a rise, not on a bare plain.
- Also add the sea keep-out for stamp openings (KNOWN_ISSUES entry).
- Tests: one per rule on the stamp footprint, 4 seeds + adversarial. Fast + whole suite (generator change). TASKS.md paragraph. Rough cost from the task's own runs; no timing arms.
- Shots, full size, ON vs OFF, same poses as plan pictures `cave_in`, `pair_desert_wetland-basin`, `river`. One caption each.

## End
Done, default stays OFF; owner decides default from the pictures. STOP_FOR_OWNER only if pictures look wrong or a frozen API / save format must change.
