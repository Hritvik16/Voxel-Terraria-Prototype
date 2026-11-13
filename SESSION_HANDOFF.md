# Session handoff -- 2026-11-13 ~12:50 (supervisor; queue RUNNING 194b; no owner question open)

## For the owner
Why: three tasks finished in this chat (193, 194, 194a), the point where the rules say to start a fresh, cheaper session.
Covered in this chat (13 Nov, 08:20-12:50):
- I took over from the old supervisor (you closed `voxel-sup-0009`).
- Your answers: **Q-X (a)** ores ON now, trees + towns ON after 192-194, pools OFF until their walls are softened;
  **Q-Y** first (a) 30, then changed to **(b) 45** (already the default, nothing to change);
  **Q-Z (a)** shade lighting ON in play (sky + bounce).
- Done and reported in the doc with pictures:
  - **193**: shade at noon no longer pitch black (`-skyfill 1|2`); a real cave stays black; costs x1.35-2.5 frame time in the picture launch.
  - **194**: the rectangular pits are FIXED. They were chunks the full near memory pool refused; the near view now hands them to the 0.2 m level.
  - **194a**: `./run-play.sh` starts with shade lighting ON; F6 "Shade light" row; rigs unchanged. Check once that the F6 panel's bottom row fits your screen (panel is 1226 px tall).
- You paused builds 09:26-09:58. Unity "not responding" in Activity Monitor at 11:30 was a slow but working build (~34 min, lighting shader compile).
- You said yes to cutting that compile time -> task 194b, RUNNING now.
- tmux's socket file vanished from /private/tmp at ~12:43 (sessions kept running); I recreated it.
Queued after 194b: 195 ores ON, 195a trees + towns ON (stops if memory > 3 GB), 196 natural pool basins (pools stay OFF).
Close THIS window (tmux session `40`) once the new one is up.

## Next supervisor
- On every `done NAME`: read tools/overnight/HANDOFF.md `## For the doc`, look at 1-2 pictures (side-by-side with PIL into the scratchpad), upload, Task reports entry (newest first), Now table cells, archive folder. Then a 3-5 line chat note.
- 194b ends with a compile-time table + a list of runtime-affecting shader cuts for the owner to decide later -> one question in the Questions tab if that list is worth it.
- 195a may STOP_FOR_OWNER on memory (> 3,000 MB) -> question with its numbers.
- 196 ends with pool pictures -> question: pools ON by default?
- NEVER-IDLE after 196: owner-independent ideas: render check of faint white dashes on steep faces (`WORLD_GEN_LOOK_PLAN.md` "Not world gen"); 194's open item (refused chunk inside shell 1's hole draws far band) only if it shows. Water look and arches (flat-topped slabs) are look questions -- ask first. C8 underground needs the owner's yes.
- DOC (Queue decisions, https://claude.ai/artifact/DrfR6aQJHYmhnQ2Syu33Hq):
  - Task reports tab node d611dfba-5116: newest report heading is 194a `mk2vq5dkmp6.140155` -> insert new reports BEFORE it.
  - Now tab node e04dd89d-c6f3, queue table mk2vq5dkmp6.7022. Rows (0 = header): ... 11 193 done, 12 194 done, 13 194a done, 14 194b running, 15 195, 16 195a, 17 196. Edit a status cell with `replace` find + `within {kind:cell, table, row, col}`; new rows: insert `as:'blocks'` with a row/cell/paragraph/text JSON array (topic.editing ex.2).
  - Questions tab node 08a1b590-58d9: new questions go BEFORE the top heading mk2vq5dkmp6.28272 (Q-Z, answered).
  - Screenshot Archive: https://claude.ai/artifact/9y7VxxX8FmsDMtVbSyFd2d -- recipe in `tools/archive/README.md`; publish needs a `read` of the live page first in a new session (the refused publish counts as the read; republish unchanged if only your new section differs). Commit tools/archive/index.json by path.
- Doc tool notes: images: Artifact publish {url: doc url, asset:true, file_paths:[...]} -> `mcp__claude_ai_Claude_Docs__create` object=blob engine=blob payload {asset:<id>} -> `![alt](blob/<id>)`.
- Runner: tmux session `overnight`. If it ENDS, give the owner ONE line: `! tmux kill-session -t overnight; IDLE_MIN=0 tools/overnight/start.sh --resume`. Usage-limit waits resolve by themselves. If `tmux ls` says "error connecting": `mkdir -m 700 /private/tmp/tmux-501; kill -USR1 <tmux server pid>`.
- Waiter: one background until-loop on Logs/overnight/runner.log (BLOCKED|done |queue runner end|FAILED|INCOMPLETE); count lines first; re-arm after each event.
- Owner rules (memory dir): plain words, look at every picture before captioning, questions in the doc tab, no paid-plan suggestions, max two Claude sessions.

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the overnight queue. You do no engineering yourself.
> Put every question for me in the "Queue decisions" doc's "Questions for you" tab, in plain words with pictures you have looked at
> yourself. After every finished task, update the doc right away with a report and pictures. Start by telling me why this new
> session started and what the previous chat covered (SESSION_HANDOFF.md, "For the owner").
