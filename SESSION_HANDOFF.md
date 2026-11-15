# Session handoff -- 2026-11-15 ~05:30 (supervisor; queue RUNNING 210; owner questions open: Q-AH, Q-AI)

## For the owner
Why: this chat passed ~10 MB (every turn re-sends it), so the rules say to start a fresh, cheaper session.
Covered in this chat (14 Nov 18:45 - 15 Nov 05:30):
- Old supervisor window voxel-sup-2052 closed by you. Runner had ended after 206 (it had snapshotted its list); restarted by sending the run-queue
  command into the existing idle `overnight` tmux window (the kill-session was blocked and not needed).
- You answered **Q-AG (a)** (still streams like rivers; stretch goal: look flowing) -> task **209b**; and **Q-AF (a)** plus "why can't we use the
  island-coast rules?" -> task **209** (done: yes, mostly; ponds now use the coast rule; report + pictures in the doc).
- Done + reported in the doc with pictures: **205** (white dashes: FSR 3 only dims them; fix `-dashfix 2` built, OFF -> **Q-AH**),
  **206** (debug views + torch-light kernel fixed in player builds), **207** (GPU retry only what fits: refusals 2,806 -> 685; process peak +68 MB, maybe noise),
  **208** (valve spares in-view chunks; stopped on rule 11: GPU refusals 857 -> 1,190 -> **Q-AI**; fix is committed ON), **209** (pond coast shores, ON).
**Open for you:** Q-AH (dash fix on by default? rec. a = `-dashfix 2`) and Q-AI (GPU-side eviction follow-up? rec. a) in "Questions for you".
Close THIS window once the new one is up: `! tmux kill-session -t voxel-sup-1832`.

## Next supervisor
- Queue: 210 running (trees space colonization; since 05:15), then 209b (pond streams; `Requires: 209`: the runner skipped it at 03:30/05:15, so check it
  actually runs after 210; if the runner ends with 209b still in queue/, restart it: `tmux send-keys -t overnight "cd <repo> && IDLE_MIN=0 caffeinate -ims nice -n 10 tools/overnight/run-queue.sh" Enter` when the window is an idle shell, or the kill-session `!` line from the owner), 211 (GDD batch-A answers), 212 (T0, last, idle-guarded).
- Blocked 208 file stays in blocked/ (its code is committed; do NOT requeue). If Q-AI (a): queue a GPU-side eviction task (details in `tools/overnight/blocked/208-bug-valve-spares-in-view-chunks.HANDOFF.md`, option a).
  If Q-AH (a): queue a tiny task making `-dashfix 2` the default (TASKS.md 205 paragraph).
- Doc debts (Queue decisions, https://claude.ai/artifact/DrfR6aQJHYmhnQ2Syu33Hq, container {kind:project,id:DrfR6aQJHYmhnQ2Syu33Hq}):
  - Reports done for 205-209 in "Task reports" (node d611dfba-5116; newest heading = 209 `mk2vq5dkmp6.163502`; insert before it).
  - STILL OWED: "Now" tab Done-table rows (node e04dd89d-c6f3, table mk2vq5dkmp6.7022; count rows first) for 205-209, and `Screenshots/205..209` entries in `tools/archive/README.md`.
  - Questions tab (node 08a1b590-58d9): newest Q-AI heading `mk2vq5dkmp6.49009`, then Q-AH `.47551`. Q-AF/Q-AG are answered but their headings are NOT yet prefixed "ANSWERED" nor moved to the Answered tab (Q-AF answer para `.45363`). A stray empty paragraph sits above the Q-AG heading.
  - Image recipe: Artifact publish {url: doc url, asset:true, file_paths:[...]} -> `create` object=blob engine=blob payload {asset:<id>} (the batch tool rejects `object`) -> `![alt](blob/<id>)`.
  - Ops on one tab must use that tab's block ids (I once mixed two tabs in one call: refused).
- Waiter: one background until-loop on Logs/overnight/runner.log (BLOCKED|done |queue runner end|FAILED|INCOMPLETE); `wc -l < runner.log > /tmp/sup_n` first; re-arm after each event.
- Owner rules (memory dir): plain words, look at every picture before captioning, questions in the doc tab, no paid-plan suggestions, max two Claude sessions, efficient by construction.

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the overnight queue. You do no engineering yourself.
> Put every question for me in the "Queue decisions" doc's "Questions for you" tab, in plain words with pictures you have looked at
> yourself. After every finished task, update the doc right away with a report and pictures. Start by telling me why this new
> session started and what the previous chat covered (SESSION_HANDOFF.md, "For the owner").
