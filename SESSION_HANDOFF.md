# Session handoff -- 2026-10-04 13:20 (queue supervisor session; 26 running; builds running)

## State
- **Branch `surface-rebuild`** (from `main`). `main` was fast-forwarded on 2026-10-03 14:40
  after SUITE=all 618/0/12. NOT pushed (owner hasn't asked).
- Owner answered Q15-Q20 all (a) on 2026-10-04 (16e9330). Runner restarted 06:29 in tmux
  `overnight` (IDLE_MIN=0). Done: **22** coast black bar FIXED (e89501f; CLAUDE.md has rule 9).
  Queue, in order: **24** (running since 07:03) detail radius at speed (opt-in) -> **26** eviction
  retry -> **29** B3 light erosion (60 iterations; plan's B3 targets; owner render check) ->
  **30** column-pass sharing (bit-identical; records Q19 = 2a 3a 4a 5a in the plan).
- **Owner paused builds 07:04 for an hour.** A detached job runs `tools/heavy.sh resume` at
  "idle + 30 min" (owner re-paused 08:35: let the running player finish, then 30 min; no PAUSE file this time, so the runner keeps going and its builds wait). Check
  `tools/heavy.sh status` ~35 min after the drain; if it still says PAUSED, run `tools/heavy.sh resume`.
- **PREAMBLE rule 9 (owner Q20 (a)):** performance at WARM STEADY STATE: one discarded warm-up
  launch, then measured arms back to back, ABBA.
- After 24: owner question "switch -detailspeed on?" with off/on pictures. After 29: owner
  "still natural?" check with B2/B3 renders, then B4 (plan row B4). After 30: coarse-first
  step 1 needs nothing new from the owner (Q19 answered) -- queue it from plan section 5.
- Task agents number follow-ups after the last file they see. Re-check `ls queue done blocked`
  before numbering a supervisor task; renumber on a clash (happened twice: 22, 24).
- Fast EditMode 611/0/12 (28). SUITE=all 629/0/12 (B2). `Assets/Scenes/Playground.unity`
  stays modified (build side effect).

## Done overnight 3-4 Oct (Done table in the doc has a row for each)
16b requirement (c8807c2) · 17 B2 landform by flatness (2855a36) · 19 coast bar = FarStartT
float stall (b128e1f) · 20 worker contention (76a717a) · 21 lost GPU updates = save-capped
evictions never retried (7d2125a; closed to done/ by the supervisor) · 23 coarse-first plan
(4fecdf6, PLAN_COARSE_FIRST_GEN.md) · 25 GC ruled out, per-thread slow state (a909953) · 27 slow
state is heat-triggered (c2bd8cb) · 28 four workers heat the chip themselves (2f8797f).
Owner: paused builds 21:52-22:52 (auto-resumed); answered Q13 (a), Q14 (a) -> tasks 24, 23.

## Waiting on the owner
- **Q21 (doc rev 298): switch `-detailspeed` on by default?** Recommended (a): wait for 26b's walking
  shot. 24 DONE 13:15 (4b03fe9). Its follow-up 31 was renumbered 26b (runs after 26) so Q21 can close sooner.
- Older: Q6/Q7 (TAA/checker stills) still the owner's to look at; nothing queued.

## Documents
- Queue decisions: https://claude.ai/code/artifact/681cc873-8163-45da-a51f-32ffca6d7468
  (doc id 681cc873-8163-45da-a51f-32ffca6d7468, body node 8460ce71-6ba5, last rev 297)
- Surface rebuild plan: https://claude.ai/code/artifact/f6222d44-1457-4a92-9460-3390d44e75fd
- Game Design Brief: https://claude.ai/code/artifact/d31b4321-77c3-4026-93d2-460c190950bf
- Terrain review: https://claude.ai/code/artifact/7443de71-6716-4f99-869b-98d3c6814fd7
- TAA proposal (steps 1-3 approved): https://claude.ai/code/artifact/6745ee4a-4af7-482f-b07d-822e46270918

## Notes
- **Owner 2026-10-04: NO 5-minute updates** (each turn resends the whole conversation). Speak only
  when a task starts/finishes/blocks or the owner is needed. The owner watches the running worker
  in a Terminal tab titled "running worker" (opened 07:12; it follows each new task by itself).
- Keep supervisor sessions short: after ~3-4 finished tasks, hand off and start a fresh one
  with `tools/new-session.sh` (SUPERVISOR.md "The loop").
- This Mac is a fanless M1 Air: back-to-back performance runs get slower (tasks 27/28).

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the overnight
> queue as described there until nothing is left that can run without me. Only one agent
> works on the project at a time; you do no engineering yourself. Put every question for me
> in the "Queue decisions" doc. No periodic status updates: tell me only when a task starts or
> finishes, blocks, or needs me. When this session gets long, hand off to a fresh one yourself.
