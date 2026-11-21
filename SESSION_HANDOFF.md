# Session handoff -- 2026-11-20 ~17:20 (supervisor; queue 238 (timing, running) -> 250 -> 251 -> 252 -> 253; no owner question open)

## For the owner
Why: this chat passed ~8 MB (every turn re-sends it), so the rules say to start a fresh, cheaper session.
Covered in this chat (19-20 Nov):
- Finished and reported in the doc (Task reports tab, newest first): 243 (solid crown cores: don't work, dropped), 244 (sun rays at the
  forest take 2x the steps of noon), 245 (foliage light shortcut: small saving, often wrong, kept OFF), 246 (world-fill cost plan:
  see-through things are expensive, solid things cheap; grass needs its own near-camera layer), 247 (cached sunlight design, amendment 8.18
  DRAFT), 248 (what each pixel hits: leaves ~2 ms per 1 % of screen, trunks ~0), 249 (cached sunlight correctness: all fine except
  1-voxel leaf holes come out 37 % bright, needs 75 %).
- You chose cached sunlight over chunky leaf clumps ("2 doesn't account for the player messing with the leaves").
- You asked whether filling the world will keep hurting performance: answered by 246/248 (solid props cheap; leaves/grass are the cost).
- You said the Mac is in use all day: timing task 238 now waits in `later/` until 30 min without input (`release-when-idle.sh`,
  tmux window `overnight:idlegate`); it started 16:43 when you stepped away. If you say you're going to sleep, a supervisor releases
  timing tasks right away.
- The doc was restructured (fresh Now / Questions for you / Task reports tabs; old ones under Archive and Answered) and is now updated on
  every task end by a background watcher.
- 20 Nov ~17:00 you said "go with recommended for all questions": Q-249 (b), Q-247 (a), Q-246 a yes / b yes / c wait / d yes / e yes,
  Q-245 (a), Q-244 (a), Q-243 (a), Q-AS (a: merge + push). Recorded in the doc's Answered tab; tasks 251-253 queued for them.
Running now: 238 (forest cost split, timed, needs the Mac left alone ~40 min). Queued: 250 voxel grass counts, 251 cached-sun near-zone
rewrite (your Q-249 b), 252 merge to main + push (whole suite first; merge without checking out main), 253 solid props memory card.
Open for you: nothing.
Close THIS window once the new one is up: it is tmux session `0` -> `! tmux kill-session -t 0` (check `tmux ls` first; never kill `overnight`).

## Next supervisor
- First: `tmux ls`, `tail -5 Logs/overnight/runner.log`, `ls tools/overnight/queue`. Then arm the watcher with Bash `run_in_background`:
  `tools/overnight/wait-task-end.sh` (exits on the next done / BLOCKED / runner end; re-arm after every event).
- Runner: tmux `overnight:0`. Restart if ended: `tmux send-keys -t overnight:0 "IDLE_MIN=0 caffeinate -ims nice -n 10 tools/overnight/run-queue.sh" Enter`.
- On each event: read the task's `## For the doc` (HANDOFF in `tools/overnight/HANDOFF.md` at the task's commit, or `blocked/<task>.HANDOFF.md`),
  look at its image, insert a short report at the top of Task reports, rewrite the Now tab (table + intro time + "Waiting on you"),
  put new questions at the top of Questions for you (each with picture + numbers + recommendation), keep ONE owner-independent task queued.
- 238 result: compare against 247/248 estimates (2 ms per 1 % leaf, solid ~0). The idle gate is done (238 released); its window can stay.
- 251 writes `tools/overnight/later/252b-suncache-kill-timing.md` (timed, idle Mac). When it exists: start
  `tmux new-window -t overnight -n idlegate2 "tools/overnight/release-when-idle.sh later/252b-suncache-kill-timing.md 30"`.
- 252 merges with `git commit-tree` + `update-ref` (never checks out main: the working tree is shared) and pushes. Owner approved
  ("go with recommended for all", Q-AS (a)). If its push is blocked by permissions, give the owner the one-line `! git push origin main engine-phase`.
- After 253: next candidates per `WORLD_FILL_BUDGET.md` sec. 5 order -- E2 clutter-layer amendment draft (approved in principle by Q-246a,
  only if 250 shows voxel grass can't carry clutter; name it 8.19, 8.18 is the cached sun). 247c/247d (GPU cached sun) only after the
  252b timed kill test passes.
- Doc (Queue decisions, https://claude.ai/artifact/DrfR6aQJHYmhnQ2Syu33Hq, container {kind:"project",id:"DrfR6aQJHYmhnQ2Syu33Hq"}):
  Now node 5b31b670-4acc (intro `mk2vq5dkmp6.4`, table `mk2vq5dkmp6.6339`, waiting `mk2vq5dkmp6.619`); Questions node e733c3c0-08fd
  (single block `mk2vq5dkmp6.8523` "No open questions" -- insert new questions before it and remove it); Task reports node bf8cb8f4-1d14
  (newest heading 249 `mk2vq5dkmp6.8987`, insert before); Answered node 4e4a30eb-dbfc (newest `mk2vq5dkmp6.4702`).
  Recipes (memory `doc-update-after-every-task`): read `{kind:"view",sinceRev:0}` or `{kind:"search",text}`; insert
  `{op:"insert",target:{kind:"blocks",ids:[full id]},side:"before"|"after",source:{as:"markdown",from:{kind:"inline",content}}}`;
  replace `{op:"replace",ifHash,ifRev,target:{kind:"blocks",ids:[...]},with:{as:"markdown",from:{...}}}` (several ids: ifRev only).
  Images: Artifact publish {url: doc url, asset:true, file_path} -> docs `create` object "blob", engine "blob", payload {asset:id} -> `![alt](blob/<id>)`.
- Owner rules (memory dir): plain words, look at every picture first, questions in the doc tab, never leave the queue idle, timing only
  when the Mac is idle, no paid-plan suggestions, max two Claude sessions, auto mode blocks `kill`/`tmux kill-session` (give a `!` line).
- NOT committed by me: `Assets/Scenes/Playground.unity`, `Logs/TerrainGen/*`, untracked `ParallelEquivalenceTests.cs` / `TestPar.cs`
  (origin unknown -- ask the owner before deleting or committing).

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the overnight queue. You do no engineering yourself.
> Arm tools/overnight/wait-task-end.sh in the background so each finished task wakes you. After every finished task, update the
> "Queue decisions" doc right away (short report with pictures you looked at, rewrite the Now tab), keep one owner-independent task
> queued, and put any question for me in the "Questions for you" tab in plain words. Start by telling me why this new session started
> and what the previous chat covered (SESSION_HANDOFF.md, "For the owner").
