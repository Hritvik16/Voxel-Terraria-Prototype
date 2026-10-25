# 151-perf-torch-range-cost

Model: opus

Approval: owner, 2026-10-25, doc Q95 answer: "3 m doesn't seem good for gameplay or lighting the island up. I think 10-15 but cost is an issue".
A PERFORMANCE task. Goal: find the largest torch reach whose cost is acceptable, then ship it.
Sources: `Screenshots/143/torchreach143.txt` and `tools/overnight/HANDOFF`-style notes in task 143's commit 38a2e68 (arms: today k0.85/I0.9/R8.5;
small room 0.35/1.6/8; half tunnel 0.15/2.4/14; 10-15 m tunnel 0.06/3.0/24; model reach 1.2 / 2.9 / 5.8 / 10.4 m).

## Decision table (written before measuring)
Budget rule: we are 14.7 ms over 30 fps (task 146), so a torch setting ships as the play default only if its warm cost is <= +1.0 ms p50 in the
worst realistic torch scene (32 torches in range, ~half in the tier-0 box). Over that, record the cost and ship the largest arm under +1.0 ms.
- 10-15 m arm <= +1.0 ms: ship it (reach 10.4 m).
- Only half-tunnel <= +1.0 ms: ship it (5.8 m) and report the 10-15 m cost for the owner.
- Even the small room arm > +1.0 ms: do not change; STOP_FOR_OWNER with the numbers.
Also try cheap variants of the large arm if over budget (e.g. high intensity with range 14 instead of 24: range bounds the per-pixel cost,
LightingConfig comment) BEFORE giving up; one variant per measurement.

## Do
1. AC check. Warm-up launch discarded (`MODE=playwalk` or the cost-split driver of task 146, whichever places torches: use task 143's
   `-torchreach` capture scene extended to 32 torches placed in a ring at night, if the capture mode takes a count; otherwise add a
   count flag, report only). Then ABBA of today vs each arm using the existing three LightingConfig knobs only; report window means
   (task 150: whole-run p50 spreads 42-47 ms; A/Bs under 3 ms need window means or the cost-split's interleaved FULL range).
2. Torches leak through walls near the box edge for tier >= 1 hits (task 143 note); state whether any arm worsens that visibly (one still).
3. Ship the chosen arm by changing the preset's three numbers (+ a test if a preset test pins them). Keep lava's aggregate light: these knobs
   are global (143): look at one lava still and say whether it changed. Fast suite once at the end.

## Deliverable
HANDOFF `## For the doc`: table arm / ms / lit px, the shipped arm, the lava still, log paths, warm-up log. TASKS.md paragraph.
