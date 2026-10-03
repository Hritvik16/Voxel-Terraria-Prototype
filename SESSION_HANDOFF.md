# Session handoff -- 2026-10-03 14:45 (queue supervisor session; rebuild started)

## State
- **Branch `surface-rebuild`** (from `main`). `main` was fast-forwarded to design-brief on
  2026-10-03 14:40 after SUITE=all 618/0/12 (Logs/overnight/premerge_all.out). NOT pushed.
- Runner alive in tmux `overnight` (IDLE_MIN=0, under caffeinate -ims). No PAUSE.
  Running: 14 heavy-pause toggle (started 14:42). Next: 15 B1 (Requires 14).
- Owner, 2026-10-03:
  - No day/night windows. Task 14 builds `tools/heavy.sh pause|resume|status`. When the owner
    says "pause"/"resume" in chat, run it. The agent keeps working; only Unity waits.
  - The Q8 worker sweep goes with B9, not now.
  - Every image gets a caption saying what to look at (PREAMBLE rule 8, memory).
- Rebuild order + rules: SUPERVISOR.md backlog. Q5-Q9 answered (a) and moved under "Answered"
  in the doc; nothing waiting on the owner.
- EditMode: fast 605/0/12, all 618/0/12. `Assets/Scenes/Playground.unity` stays modified.

## Done this session (rows in the Queue decisions doc, "Done")
05a pose dropped (0bbd1e6) · 05b metrics harness (0a80629, Q5 open) · 05c wall not reproduced
(b2ab00a) · 06 TAA core, off by default, `-taa 1` / F4 (718dd03) · 07 TAA risk checks (d6e97f1)
· 08 TAA checkerboard, off, `-checker 1`: 30.8 -> 19.6 ms, misses 16.7 ms by 2.9 (ecb1027, Q7 open)
· 09 tg2 fluid does simulate, no fault (3a99383) · 10 streaming baseline: CPU generation is the bottleneck (1c8e439, Q8 open)
· 11 EditMode suite 14m50s -> fast 3m02s / all 9m23s (f1e8dc4) · 12 rig power guard (7f59fbc)
· 13 fly pose: designed band limit at a cliff lip, not a bug (233562a, Q9 open, not blocking).

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
  (doc id 681cc873-8163-45da-a51f-32ffca6d7468, body node 8460ce71-6ba5, last rev 231)
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
