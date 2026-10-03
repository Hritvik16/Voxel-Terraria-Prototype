# Session handoff -- 2026-10-03 08:55 (queue supervisor session; stopped at usage limit)

## State
- Branch `design-brief`. Runner alive in tmux session `overnight` (IDLE_MIN=0, nice 10). No PAUSE.
- Running: `12-rig-power-guard` (started 08:51). Queue after it: 13 fly pose draws no voxels (10
  follow-up, evidence only). After 13 NOTHING is runnable without the owner: B1 needs Q5 (and
  maybe Q8's pass line). Add 12 and 13's Done rows to the doc when the next session starts.
- 11 done (f1e8dc4); PREAMBLE rule 3 updated (2198010). CLAUDE.md "Current state" still quotes an
  old EditMode count (625/2); new: fast 605/0/12, all 618/0/12 -- leave for a task, not the supervisor.
- Pre-B1 v3 poses baseline (04b plan) is effectively covered by task 10's reference run
  `Logs/tg2world_20261003_062458.txt` (on AC, peak 3358 MB, dense bricks per pose).
- The Mac ran out of battery at 21:44 on 10-02 and hibernated until 05:04. The supervisor started
  `caffeinate -ims -w <run-queue pid>` by hand; after a runner restart, start it again until task 12
  lands. Check `pmset -g batt` before trusting any cost number.
- Blocked: `05b-surface-b0-metrics-harness` (done except Q5; harness committed 0a80629).
- EditMode last: PASS 626 FAIL 0 SKIP 4 (task 08, 20:24). The 4th skip is the opt-in PlayMetricsReport.
- `Assets/Scenes/Playground.unity` stays modified (scene builds regenerate it); tasks leave it alone.

## Done this session (rows in the Queue decisions doc, "Done")
05a pose dropped (0bbd1e6) · 05b metrics harness (0a80629, Q5 open) · 05c wall not reproduced
(b2ab00a) · 06 TAA core, off by default, `-taa 1` / F4 (718dd03) · 07 TAA risk checks (d6e97f1)
· 08 TAA checkerboard, off, `-checker 1`: 30.8 -> 19.6 ms, misses 16.7 ms by 2.9 (ecb1027, Q7 open)
· 09 tg2 fluid does simulate, no fault (3a99383) · 10 streaming baseline: CPU generation is the bottleneck (1c8e439, Q8 open).

## Waiting on the owner (doc "Waiting on you")
- **Q5** (05b): B1's biome-size target. Recommended (a): 297 m baseline, B1 >= 2.5x (~740 m).
  When answered: append to `blocked/05b...md`, close the question; B1's gate quotes the number.
- **Q6** (07): TAA light lag at slow sun + unresolved cost. Recommended (a): owner compares the
  stills first, nothing queued. If they like the look: a 960 px cost run on an idle Mac
  (IDLE_MIN=15 / overnight), then a light-aware history rejection.

- **Q7** (08): checkerboard misses 60 FPS by 2.9 ms. Recommended (a): compare pictures with Q6,
  nothing queued. (c) = approve step 4 shadow tuning; (d) = fix horizon teeth / fluid softness.
- **Q8** (10): generation throughput. Recommended (a): queue an evidence-only worker-count/QoS sweep
  (opt-in flag) AND make "gen ms not worse than baseline" a pass line of every B step.
- Q6 got an update line: task 07's cost runs were on battery, so its cost figure is unreliable.

## Owner decisions this session (2026-10-02)
- Over-3 GB memory: not now; fix pool sizes after the surface rebuild (pools are allocated at capacity).
- Entity physics (PhysX cages vs voxel motor): recorded as Open question 13 in the brief, NOT queued.
- Surface rebuild must account for performance: task 10 = v3 streaming baseline; EVERY B step
  re-measures it (SUPERVISOR.md backlog).
- Task 11 approved, incl. "slow group runs only before merges and on generator changes".
  After 11 lands: update PREAMBLE.md rule 3 (SUPERVISOR.md backlog says how).
- B1 onward: queued by the supervisor after TAA 08 (and after 10/11), one step per task, from the
  plan's section 4 with section 8's answers; needs Q5 for B1's gate.

## Documents
- Queue decisions: https://claude.ai/code/artifact/681cc873-8163-45da-a51f-32ffca6d7468
  (doc id 681cc873-8163-45da-a51f-32ffca6d7468, body node 8460ce71-6ba5, last rev 214)
- Surface rebuild plan: https://claude.ai/code/artifact/f6222d44-1457-4a92-9460-3390d44e75fd
- Game Design Brief: https://claude.ai/code/artifact/d31b4321-77c3-4026-93d2-460c190950bf
- Terrain review: https://claude.ai/code/artifact/7443de71-6716-4f99-869b-98d3c6814fd7
- TAA proposal (steps 1-3 approved): https://claude.ai/code/artifact/6745ee4a-4af7-482f-b07d-822e46270918

## Notes
- Status updates every 5 min while a task runs (one background wait: 300 s or a runner event).
- Claude Code auto-updated at 17:02; the binary is fine.

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the
> overnight queue as described there until nothing is left that can run without
> me. Only one agent works on the project at a time; you do no engineering
> yourself. Put every question for me in the "Queue decisions" doc. Give me a short
> status update here every 5 minutes while a task runs (see SESSION_HANDOFF.md).
