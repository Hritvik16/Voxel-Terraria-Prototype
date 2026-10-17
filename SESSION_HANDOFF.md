# Session handoff -- 2026-10-17 ~01:00 (supervisor; queue RUNNING task 93b5)

## For the owner
**Why a new session (01:00):** the supervisor in tmux window 22:0 handled four finished tasks, which is the point where
SUPERVISOR.md says to hand off (every turn re-sends the whole chat). Nothing is wrong.

**What happened in 22:0 (16 Oct 19:30 -> 17 Oct 01:00):**
- **93b3 stopped -> Q57, went ahead with (a) by default** (`6e66b24`): the far band is NOT why shells cost more (it
  costs ~0 after them); the shell march is the +12-15 ms. 93b had blamed the far band because of a rig artefact.
- **93f done** (`cf23259`, `dd8f595`): with shells on the GPU keeps its own brick pool: GPU terrain 441 -> 193 MB,
  Metal 1,296 -> 1,041 MB, footprint median 2,672 -> 2,371 MB; no visible change.
- **93b4 stopped -> Q58, you answered (a)** (`7a5231e`): the primary shell march is the cost (13.7 / 19.1 ms), 43-75% of
  its steps go through empty bricks; pixel-identical read caches saved 0.6-2.6 ms. Your (a) = an air-brick mask (a new
  ~50 KB GPU buffer): task 93b5, now running.
- **93g done** (`f989de9`): tg2 CPU brick cap 750,000 -> 570,000; step 14 (D1-D4) total memory 2,999 -> 2,362 MB.
- You asked whether you can fix the permission refusals yourself: yes, add allow rules via `/permissions`
  (`Artifact`, `Bash(tmux kill-window:*)`, `mcp__claude_ai_Claude_Docs__read/update/batch`). Not known yet whether you did.
- The Mac ran on battery down to 12% during 93b3 (its numbers are less trustworthy); on AC since ~22:00.

**Running:** queue worker on **93b5 shell air-brick mask** since 00:53 (performance task, on AC).
**Close:** tmux windows **22:0** (this supervisor, once the new one greets you) and **22:2** (the one before it, still open).
**Waiting on you:** nothing in the queue. Still open from earlier: merge `engine-phase` -> `main` (230+ commits ahead)
and the next performance milestone; classic-death rule (placement phase); whether 97 is still wanted after 94.
**Other window:** tmux `12:0` "gen-talk", your side discussion from 12 Oct; close it when done.

## State for the next supervisor
- Doc: "Queue decisions" https://claude.ai/code/artifact/681cc873-8163-45da-a51f-32ffca6d7468. Refs are
  `{"object":"node","id":...}` with container `{"kind":"project","id":"681cc873-8163-45da-a51f-32ffca6d7468"}`.
  Now tab body `3b5fd194-f74f` (rev 249; it is ~80K chars: do not read it whole, extract with python from the saved
  result). Running line = paragraph `mk2vq5dkmp6.121`, h `71cd1b20` at rev 249 (replace with ifHash + ifRev).
  Done table `mk2vq5dkmp6.886` (4 cells: task, result, commit, Task reports), append with `insert` `"as":"blocks"`
  `side:"end"`. Q questions go right after the "Waiting on you" heading `mk2vq5dkmp6.573`. Last Q: **Q58** (answered).
  Task reports body `d611dfba-5116` (rev 65): newest report goes BEFORE heading `mk2vq5dkmp6.68893` (93g's).
  To mark a Q answered: `replace` with a `find` target on its words (char ids are not block ids).
  Avoid `"` inside doc markdown content.
- Screenshot Archive https://claude.ai/artifact/9y7VxxX8FmsDMtVbSyFd2d (`tools/archive/README.md`), version 21. A new
  session must `read` it (no path) before its first publish (the read is ~50K tokens: do it only when a task has images).
  `rm -rf <scratchpad>/archive/sheets` before each build so only the new task's sheets are sent. 93g had no images.
- Permission checker: doc image upload, `tmux kill-window` refused; a doc `read` was refused once then worked.
- Runner: `MAX_TASKS` = 20 per start; on "queue runner end" with tasks left, restart:
  `tmux kill-session -t overnight; IDLE_MIN=0 tools/overnight/start.sh --resume`.
- Queue order: 93b5 (running) -> 93b re-run (Requires 93b5 + 93b2d) -> 93c (Requires 93b) -> 93g1 -> 94 -> 97 -> 98.
  93b3, 93b4 are in done/ with the decisions appended; old HANDOFFs of 93b/93b2/93b3/93b4/93e stay in blocked/ for workers.
- `later/`: 70 grapple, 71 wall-run (next gameplay after 93g1), 37c, 40, 78, 79, 89/90 copies. Step 14 (D1-D4) is now
  complete. Next engine work after the queue: C1, Q1, Q2, E2 (8.15 rows), engine steps 16 (streaming milestone), 18-23;
  keep alternating with gameplay (Q50 (4)).
- Still worth weighing: walking needs ~2x the shell throughput for 90% fill (93b2's (b) ration shell 1 / (c) 4th worker
  were not chosen) -- ask again only if 93b/93c show it matters.

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the overnight queue as described there until
> nothing is left that can run without me. Only one agent works on the project at a time; you do no engineering
> yourself. Put every question for me in the "Queue decisions" doc. Start by telling me why this new session started and
> what the previous chat covered (SESSION_HANDOFF.md, "For the owner").
