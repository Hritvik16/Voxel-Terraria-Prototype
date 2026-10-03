# Session handoff -- 2026-10-03 15:30 (queue supervisor session; B1 running)

## State
- **Branch `surface-rebuild`** (from `main`). `main` was fast-forwarded to design-brief on
  2026-10-03 14:40 after SUITE=all 618/0/12. NOT pushed (owner hasn't asked).
- Runner alive in tmux `overnight` (IDLE_MIN=0, under caffeinate -ims). No PAUSE.
  Running: **15 B1** (started 15:21): map v4 opt-in + world record + biomes x3. It also renders
  x2/x3/x4 maps for the owner's pick and runs the streaming A/B against v3.
  Next: 16 pause gate in older multi-launch loop scripts (14b follow-up, tooling).
- **Owner pause (tasks 14/14b done):** when the owner says "pause" or "resume", run
  `tools/heavy.sh pause` or `tools/heavy.sh resume`. DRAIN semantics: running work finishes,
  nothing new heavy starts. Use `tools/heavy.sh status` and tell the owner when it says idle.
  Never freeze or kill (owner correction 2026-10-03).
- Owner, 2026-10-03:
  - The Q8 worker sweep goes with B9.
  - Every image gets a caption saying what to look at (PREAMBLE rule 8).
  - Rebuild order and rules: SUPERVISOR.md backlog. Copy the plan numbers INTO task files.
- After B1: Done row in the doc with the x2/x3/x4 maps (captioned). Add "Owner pick: biome scale"
  as a question. Then queue B2 (needs the plan's rule 2 + B2 row; render 40/50/60% gentle for
  the owner's pick).
- Doc: nothing waiting on the owner. EditMode fast 605/0/12, all 618/0/12.
  `Assets/Scenes/Playground.unity` stays modified.

## Done this session (rows in the Queue decisions doc, "Done")
05a pose dropped (0bbd1e6) · 05b metrics harness (0a80629, Q5 open) · 05c wall not reproduced
(b2ab00a) · 06 TAA core, off by default, `-taa 1` / F4 (718dd03) · 07 TAA risk checks (d6e97f1)
· 08 TAA checkerboard, off, `-checker 1`: 30.8 -> 19.6 ms, misses 16.7 ms by 2.9 (ecb1027, Q7 open)
· 09 tg2 fluid does simulate, no fault (3a99383) · 10 streaming baseline: CPU generation is the bottleneck (1c8e439, Q8 open)
· 11 EditMode suite 14m50s -> fast 3m02s / all 9m23s (f1e8dc4) · 12 rig power guard (7f59fbc)
· 13 fly pose: designed band limit (233562a) · 14 pause tool (74f263a) · 14b pause = drain (bc98a7b).

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
- **Q9** (13): band can't reach ground seen from a ledge. Recommended (a): no change; revisit after
  the rebuild. Not blocking.
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
  (doc id 681cc873-8163-45da-a51f-32ffca6d7468, body node 8460ce71-6ba5, last rev 235)
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
