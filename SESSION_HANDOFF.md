# Session handoff -- 2026-10-04 06:15 (queue supervisor session; queue STOPPED, waiting on the owner)

## State
- **Branch `surface-rebuild`** (from `main`). `main` was fast-forwarded on 2026-10-03 14:40
  after SUITE=all 618/0/12. NOT pushed (owner hasn't asked).
- **Runner ended 06:07** ("no runnable task"). tmux `overnight` is idle. No PAUSE. To restart
  after an answer: `tmux kill-session -t overnight 2>/dev/null; IDLE_MIN=0 tools/overnight/start.sh --resume`.
- Queue (all held): **22** FarStartT integer DDA = coast black-bar fix (HELD by
  `OWNER-APPROVES-22` in its Requires line -> Q16) · **24** detail radius at speed (owner Q13 (a);
  opt-in `-detailspeed 1`; Requires 22-farstartt-integer-dda; if Q16 = (b), change it to
  19-coast-bar-pixel-probe) · **26** deferred eviction retry (HELD by `OWNER-APPROVES-26` -> Q17).
- Task agents number follow-ups after the last file they see. Re-check `ls queue done blocked`
  before numbering a supervisor task; renumber on a clash (happened twice: 22, 24).
- Fast EditMode 611/0/12 (28, 06:03). SUITE=all 629/0/12 (B2). `Assets/Scenes/Playground.unity`
  stays modified (build side effect).

## Done overnight 3-4 Oct (Done table in the doc has a row for each)
16b requirement (c8807c2) · 17 B2 landform by flatness (2855a36) · 19 coast bar = FarStartT
float stall (b128e1f) · 20 worker contention (76a717a) · 21 lost GPU updates = save-capped
evictions never retried (7d2125a; closed to done/ by the supervisor) · 23 coarse-first plan
(4fecdf6, PLAN_COARSE_FIRST_GEN.md) · 25 GC ruled out, per-thread slow state (a909953) · 27 slow
state is heat-triggered (c2bd8cb) · 28 four workers heat the chip themselves (2f8797f).
Owner: paused builds 21:52-22:52 (auto-resumed); answered Q13 (a), Q14 (a) -> tasks 24, 23.

## Waiting on the owner (doc "Waiting on you", rev 282). What each answer starts:
- **Q20** (27/28) warm / cold / both; "+QoS". Recommended (a) warm steady state. On an answer:
  propose the rule for PREAMBLE.md rule set + CLAUDE.md measurement discipline (owner approves the
  wording), then put it in every perf task file. "+QoS" -> a diagnostic flag task (evidence).
- **Q18** (23) order. Recommended (a): queue column-pass sharing (exact; SUITE=all; `MODE=stream`
  section 7, measured per Q20's rule). Copy the plan's step 0 numbers into the task file.
- **Q19** (23) coarse-first details "2a 3a 4a 5a" style. Nothing to queue until column sharing lands.
- **Q17** (21) approve 26. On (a): delete `OWNER-APPROVES-26` from 26's Requires, commit by path.
- **Q16** (19) approve 22. On (a): delete `OWNER-APPROVES-22` from 22's Requires, commit by path.
- **Q15** (B2) gentle share 40/50/60%, "+cliffs". On an answer: queue B3 from the rebuild plan
  with the picked share (and a sea-cliff task first if "+cliffs").
- Q6/Q7: owner compares TAA/checker stills; nothing queued.

## Documents
- Queue decisions: https://claude.ai/code/artifact/681cc873-8163-45da-a51f-32ffca6d7468
  (doc id 681cc873-8163-45da-a51f-32ffca6d7468, body node 8460ce71-6ba5, last rev 282)
- Surface rebuild plan: https://claude.ai/code/artifact/f6222d44-1457-4a92-9460-3390d44e75fd
- Game Design Brief: https://claude.ai/code/artifact/d31b4321-77c3-4026-93d2-460c190950bf
- Terrain review: https://claude.ai/code/artifact/7443de71-6716-4f99-869b-98d3c6814fd7
- TAA proposal (steps 1-3 approved): https://claude.ai/code/artifact/6745ee4a-4af7-482f-b07d-822e46270918

## Notes
- Status updates every 5 min while a task runs (one background wait: 300 s or a runner event).
- This Mac is a fanless M1 Air: back-to-back performance runs get slower (tasks 27/28). Until Q20
  is answered, say whether a number was measured warm or cold.

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md. Check the "Queue decisions" doc
> for my answers to Q15-Q20 and turn them into tasks as SESSION_HANDOFF.md says, then restart
> the runner and supervise the queue until nothing is left that can run without me. Only one
> agent works on the project at a time; you do no engineering yourself. Put every question for
> me in the "Queue decisions" doc. Give me a short status update every 5 minutes while a task runs.
