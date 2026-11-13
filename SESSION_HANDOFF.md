# Session handoff -- 2026-11-13 ~00:20 (supervisor; queue RUNNING 190b; no owner question open)

## For the owner
Why: three tasks finished in this chat (189, 190, 190a), the point where the rules say to start a fresh, cheaper session.
Covered in this chat (12-13 Nov):
- You answered **Q-V (a)**: no random material spray by default -> task 190a, now DONE (spray on land 22 % -> 0 %; `-matvariant 1` = old look).
- You answered **Q-W (b)**: landform shapes (189's canyon benches, slot canyons, mountain grooves) ON for everyone, dunes as they are -> task 190b, RUNNING now.
- You asked to fix the trees' look first (generic, too dense) -> task 192 queued: branches + leaf clumps with gaps, every tree different within its biome's look, pictures at 3 leaf densities for you to pick.
- You said yes to shade lighting -> task 193 queued after 192: sky light into trenches, under trees and on house walls (190's walls are black under the eaves at noon).
- Done and reported in the doc with pictures: 189 (landform shapes), 190 (houses on town sites, towns spread apart: seed 31337 closest towns 899 -> 2363 m; `-towns 1`, still OFF), 190a.
- Screenshot Archive caught up: folders 176-190a added.
Running now: 190b. Queued: 191 (everything-on preview, asks you which switches become default), 192, 193. Close THIS window (tmux session `voxel-sup`) once the new one is up.

## Next supervisor
- On every `done NAME`: read tools/overnight/HANDOFF.md `## For the doc`, look at 1-2 pictures (downscale with PIL into the scratchpad), upload, Task reports entry (newest first), Now table rows, archive folder. Then a 3-5 line chat note.
- 191 ends with a question (which switches become default ON): turn it into ONE question in the Questions tab with its pictures. Note 191 runs before 192, so it shows today's (old) trees.
- NEVER-IDLE: after 193 nothing is queued. Owner-independent ideas: render check of faint white dashes on steep faces (`WORLD_GEN_LOOK_PLAN.md` "Not world gen"); water look (flat blue, waterfall sheets, colour seam at a shell edge) is a visuals-stage look task -- ask first. C8 underground needs the owner's yes. Possible look question from 190: arches are flat-topped slabs that read as grey boxes on the horizon (rounder tops?).
- DOC (Queue decisions, https://claude.ai/artifact/DrfR6aQJHYmhnQ2Syu33Hq, project id DrfR6aQJHYmhnQ2Syu33Hq):
  - Task reports tab node d611dfba-5116: insert new reports AFTER block mk2vq5dkmp6.38815 (lands on top).
  - Now tab node e04dd89d-c6f3: queue table mk2vq5dkmp6.7022. Rows (0 = header): 1 187, 2 187a, 3 188, 4 188a, 5 189, 6 190, 7 190a, 8 190b (running), 9 191, 10 192, 11 193. Edit a status cell with `replace` find + `within {kind:cell, table, row, col}`.
  - Questions tab node 08a1b590-58d9: new questions go BEFORE the top heading mk2vq5dkmp6.22127 (Q-W, answered). Answered questions stay in place with "ANSWERED (x)" in the heading (the earlier sessions' pattern).
  - Screenshot Archive: https://claude.ai/artifact/9y7VxxX8FmsDMtVbSyFd2d -- recipe in `tools/archive/README.md`. In zsh pass the folder names as separate words (an unquoted `$D` does not split). Commit tools/archive/index.json by path.
- Doc tool notes: replace takes `with` + ifHash/ifRev (find targets need none); to remove words use `delete` with a find target (an empty replace is refused); insert takes `source {from:{kind:'inline',content}, as:'markdown'}`; table rows `as:'blocks'`; images: Artifact publish {url: doc url, asset:true, file_paths:[...]} -> `mcp__claude_ai_Claude_Docs__create` object=blob engine=blob payload {asset:<id>} -> `![alt](blob/<id>)`.
- Runner: tmux session `overnight`. If it ENDS, the supervisor cannot kill tmux sessions (auto mode); give the owner ONE line to send alone: `! tmux kill-session -t overnight; IDLE_MIN=0 tools/overnight/start.sh --resume`. Usage-limit waits ("waiting 20 min (n/18)") resolve by themselves.
- Waiter: one background until-loop on Logs/overnight/runner.log (BLOCKED|done |queue runner end|FAILED|INCOMPLETE); count lines first so old lines do not match; re-arm after each event.
- Owner rules (memory dir): plain words, look at every picture before captioning, questions in the doc tab, no paid-plan suggestions, max two Claude sessions.

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the overnight queue. You do no engineering yourself.
> Put every question for me in the "Queue decisions" doc's "Questions for you" tab, in plain words with pictures you have looked at
> yourself. After every finished task, update the doc right away with a report and pictures. Start by telling me why this new
> session started and what the previous chat covered (SESSION_HANDOFF.md, "For the owner").
