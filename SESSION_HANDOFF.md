# Session handoff -- 2026-10-04 16:15 (queue supervisor session; 29 running)

## State
- **Branch `surface-rebuild`** (from `main`). `main` was fast-forwarded on 2026-10-03 14:40
  after SUITE=all 618/0/12. NOT pushed (owner hasn't asked).
- Runner alive in tmux `overnight` (IDLE_MIN=0) since 06:29. Queue, in order:
  **29** B3 light erosion (running since 16:06; owner render check after) -> **30** column-pass sharing.
- **26c done** (5449ab6): AMENDMENT_8_13 (DRAFT) + histogram; 1,194 -> 232 MB brick bodies (derived).
  Put to the owner as **Q24** (adopt + 5 choices). Its follow-up 31 (memledger peaks, measure-only)
  PARKED in `dropped/` (7e7ac31) = Q24 item 5. If Q24 item 0 = (a): queue T1 from the amendment's
  section 12 spec after 30, renumbered (31 is taken).
- Done this session: **24** detail radius at speed (4b03fe9; -detailspeed OFF), **26** deferred
  eviction retry (d4165c9; MIRROR 0/0/0 on all v4 poses; rig PASS 52 FAIL 2, known upload p99).
- Owner decisions in chat today (recorded in the doc):
  - **-detailspeed RETIRED** (stays OFF, never default): it breaks the LOD pixel rule (amendment
    8.9: nothing coarser than its distance allows). 26b (walk shot) dropped to
    `tools/overnight/dropped/`. Coarse-first generation is the rule-respecting route.
  - **Terrain memory: per-brick material palettes approved to TRY** (not surface-only tickets,
    which would make saves depend on a changing generator). 26c drafts
    `AMENDMENT_8_13_PALETTE_BRICKS.md` (DRAFT, NOT ADOPTED). When 26c is done: put the amendment,
    the histogram and the savings table in the doc as an owner approval question; no code until
    the owner adopts it. Then queue its implementation one change per task.
  - Owner: "diagnostics and analysis later" -- do not queue measure-only work (memory ledger,
    rig bisect) unless the owner asks; terrain-memory REDUCTION is wanted soon.
  - SVO comparison (`../VoxelTerraria_compare`): owner wants it "much later". Not queued.
- Owner pauses today: 07:04-08:46, 08:35 drain, 13:53-14:54. Use `tools/heavy.sh pause` +
  a detached `until status idle; sleep N; resume` job when asked for a timed pause.
- Fast EditMode 618/0/12 (26). SUITE=all 629/0/12 (B2). `Assets/Scenes/Playground.unity`
  stays modified (build side effect).

## Waiting on the owner (Queue decisions doc, rev 312)
- **Q24** (16:10): adopt palette bricks (8.13)? items 0-5, recommended (a) each.
- **Q22** items 2-3: far band starts ~2x too close at 128 m even standing still (rec. (a)
  evidence task on the memory cost of a 0.4 m tier to ~256 m); coarse-first = main track for
  speed (rec. (a) queue step 1 after 30). Item 1 decided (detailspeed retired).
- **Q23**: (1) rig frame time worse than 25 Sep (rec. (a) bisect later, after the B steps);
  (2) `UploadDirty` inside-the-box safety check (rec. (a) small task after 30).
- After 29: owner "still natural?" check with B2/B3 renders, then B4 (easy areas only).
- After 30: coarse-first step 1 (if Q22 item 3 = (a); plan section 5 of PLAN_COARSE_FIRST_GEN.md).
- Q6/Q7 (TAA/checker stills) still the owner's to look at; nothing queued.

## Documents
- Queue decisions: https://claude.ai/code/artifact/681cc873-8163-45da-a51f-32ffca6d7468
  (doc id 681cc873-8163-45da-a51f-32ffca6d7468, body node 8460ce71-6ba5, last rev 312;
  Done table id mk2vq5dkmp6.1424, header row mk2vq5dkmp6.1425; "Running now" paragraph
  mk2vq5dkmp6.55626; "Waiting on you" lead paragraph mk2vq5dkmp6.27623)
- Surface rebuild plan: https://claude.ai/code/artifact/f6222d44-1457-4a92-9460-3390d44e75fd
- Game Design Brief: https://claude.ai/code/artifact/d31b4321-77c3-4026-93d2-460c190950bf
- Terrain review: https://claude.ai/code/artifact/7443de71-6716-4f99-869b-98d3c6814fd7
- TAA proposal (steps 1-3 approved): https://claude.ai/code/artifact/6745ee4a-4af7-482f-b07d-822e46270918

## Notes
- **No periodic updates**; speak only when a task starts/finishes/blocks or the owner is needed.
  The owner watches the worker in a Terminal tab "running worker".
- Keep supervisor sessions short: hand off after ~3-4 finished tasks or a long chat.
- Task agents number follow-ups after the last file they see; re-check `ls queue done blocked
  dropped` before numbering. Name order = run order (26c runs before 29).
- This Mac is a fanless M1 Air: back-to-back performance runs get slower (tasks 27/28).

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the overnight
> queue as described there until nothing is left that can run without me. Only one agent
> works on the project at a time; you do no engineering yourself. Put every question for me
> in the "Queue decisions" doc. No periodic status updates: tell me only when a task starts or
> finishes, blocks, or needs me. When this session gets long, hand off to a fresh one yourself.
