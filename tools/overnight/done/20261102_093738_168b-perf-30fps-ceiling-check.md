# 168b-perf-30fps-ceiling-check

Model: opus

Approval: owner, 2026-11-02, chat: "Is there absolutely no estimate? Can we at least do a check to see whether it is really possible?" A PERFORMANCE task (measure-only,
owner-requested). Requires: 168 done (so D, if it paid, is in). Power first: AC only, as in 161/167; warm-up launch discarded (CLAUDE.md warm steady state).
Read `PERF_EXACT_CUTS.md` section 3 ("the honest sum": p50 <= 30 plausible; <= 5 % frames > 33 ms NOT reachable with exact cuts, p95 est. 45-55 ms).

## Question
Is the owner's 30 fps target (E4: walk p50 <= 30 ms AND <= 5 % of frames > 33 ms) reachable at all, and by which of the remaining levers? Find the CEILING, not a fix.

## Decision table (written before measuring)
- Ceiling arm (all cut candidates off together) walk p95 <= 33: the target is reachable in principle -> the supervisor queues the remaining cuts in order of ms-per-effort.
- Ceiling p50 <= 30 but p95 > 33: spikes are the problem, not the average -> owner question: spike work (E, streaming pacing) vs lower the target (p95 <= 45).
- Ceiling p50 > 30 on the heavy views: exact cuts cannot reach it -> owner question: which picture-quality trade (rows floor, shells, sun reuse) the owner accepts.

## Do
Cost-split rig as in 167 (`./run-cost-split.sh`, play flags, `-renderrows 432`, tg2 noon, walk_near / walk_open), WITH a new stacked arm that turns off
together: sky probe + sun rays + shells + the upscaler's cost is NOT removed (keep FSR 3). Also arms for each pair (sky+sun, sun+shells) and the single arms
as 167. Report ms p50 AND p95 AND % of frames > 33 ms per arm, from the rig's own output; the walk mode (`MODE=walk`) once for the ceiling arm to get the
frame-time distribution while moving (spikes are a moving-camera effect; static poses hide them). Interleave ABBA. Say plainly which levers are exact
and which would change the picture.

## For the doc
One table: arm -> p50 / p95 / % > 33 ms at walk_near, walk_open and the walk. One sentence answering "is 30 fps reachable, by what, at what cost".
