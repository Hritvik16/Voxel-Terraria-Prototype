# Session handoff -- 2026-11-17 ~00:30 (supervisor; queue RUNNING 226, then 227-230; one owner question open: Q-AL)

## For the owner
Why: this chat passed ~20 MB (every turn re-sends it), so the rules say to start a fresh, cheaper session.
Covered in this chat (15 Nov 23:30 - 17 Nov 00:30):
- 216 (GPU view eviction), 218 (jungle 10 GB memory blow-up: fixed, peak 2.5 GB), 218b (eviction ON), 219 (preset ladder: no preset reaches 30 fps on this Mac).
- You asked about 30 fps on any setting. Answer: not reachable without making lighting cheaper. 221 took pictures with each lighting switch off; you answered Q-AN:
  keep the look, move toward Lay of the Land (LotL), cheap look polish, lower-res sky/bounce light, coarse far shadows; torches stay.
- 222 researched LotL (UE5, meshed terrain, 10 cm voxels, minimum GPU ~2.1x our M1, no spoken devlogs, their sand sim is small-scale). Report + picture in the doc.
- 223 (audit of temp memory in parallel jobs: no pile-up; allocation warnings 169 -> 0), 224 (generator scratch reused per worker).
- 225: trunks were black because of two bugs that multiply (brightness curve crush + partial sky share). Fixed, ON (`-lookexact 0` / `-skylinear 0` = old). Jungle still darker than LotL (needs a second bounce: owner design choice, not yet asked).
- 220 planned the budget guard; you picked option b (check every 5 drawing tasks, after heavy ones, before merges). 230 writes the script.
Running now: **226** (look polish toward LotL: colour variation, crevice shading, haze, bloom; started 00:17). Then 227 (low-res sky fill), 228 (coarse far shadows), 229 (torch flood only when torches), 230 (budget check script + first baseline).
**Open for you:** Q-AL (towns C6/C7) only. Possible new question once 226 reports: whether to add a second light bounce / leaf light for jungle shade.
Housekeeping owed (mine): archive Screenshots 209b-225 into Archive 2, mark Q-AF to Q-AK answered and move them to the Answered tab, mark Q-AN/Q-AO headings ANSWERED.
Close THIS window once the new one is up: `! tmux kill-session -t voxel-sup-2328`.
Mac notes: the memory watchdog PAUSEs the queue when swap passes 10.2 GB (it did at 19:46 on 16 Nov; Firefox and other apps were open). Usage-limit waits also stalled 225 for ~3 h overnight.

## Next supervisor
- Queue: 226 running (since 00:17 on 17 Nov), then 227, 228, 229, 230 (each `Requires:` the previous). After 230 the queue is EMPTY: never leave it idle -> queue the next owner-independent task
  (candidates: second-bounce plan for jungle shade (plan only, ask owner), 212's next steps T2/T4, pre-existing blocked tasks stay blocked).
- Waiter: one background until-loop on `Logs/overnight/runner.log` (BLOCKED|done |queue runner end|FAILED|INCOMPLETE); `wc -l < runner.log > /tmp/sup_n` first; re-arm after each event.
  If the runner ends (PAUSE present, watchdog), check `sysctl vm.swapusage` (< ~7 GB), `rm tools/overnight/PAUSE`, then
  `tmux send-keys -t overnight "cd <repo> && IDLE_MIN=0 caffeinate -ims nice -n 10 tools/overnight/run-queue.sh" Enter`.
  Done tasks move to `tools/overnight/done/<stamp>_<name>.md`; a task that ends STOP_FOR_OWNER sits in `blocked/`: once the owner answers, `mv` it to `done/` so `Requires:` chains unblock (did this for 219, 220).
- Doc (Queue decisions, https://claude.ai/artifact/DrfR6aQJHYmhnQ2Syu33Hq, container {kind:project,id:DrfR6aQJHYmhnQ2Syu33Hq}):
  - Reports are in "Task reports" (node d611dfba-5116; newest heading is 225 `mk2vq5dkmp6.189564`; insert new ones `before` it). Done rows: "Now" (node e04dd89d-c6f3, table `mk2vq5dkmp6.7022`, rows APPEND with side:end, as:"blocks" with a content array of row/cell/paragraph/text objects, 3 cells).
  - Questions: node 08a1b590-58d9. Q-AO heading `mk2vq5dkmp6.65018` and Q-AN `.59423` (both answered); Q-AL `.55026` is open. New questions go BEFORE the newest heading.
  - Docs recipes: update engine "prose" with `payload.ops`; insert `target {kind:blocks,ids:[full id]}`, `side` before/after/end, `source {as:markdown, from:{kind:inline, content}}` (for rows `as:"blocks"` and content = array); text find target uses `parentId`, not scope/within.
    Images: Artifact publish {url: doc url, asset:true, file_path(s)} -> docs `create` object=blob engine=blob payload {asset:<id>} -> markdown `![alt](blob/<id>)`. Read an unfamiliar doc with `{"kind":"view","outline":true}` or `{"kind":"view","sinceRev":N}` (no `limit`).
- Owner rules (memory dir): plain words, look at every picture before captioning, questions in the doc tab, no paid-plan suggestions, max two Claude sessions, efficient by construction, answer the owner's questions in the doc too.
- Blocked and staying blocked: 178c, 180, 180b, 184, 184b, 208, 216.
- NOT committed by me: `Assets/Scenes/Playground.unity` (regenerated), `Logs/TerrainGen/*`; the mars-survival-nav side project (see git diff in that folder).

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the overnight queue. You do no engineering yourself.
> Put every question for me in the "Queue decisions" doc's "Questions for you" tab, in plain words with pictures you have looked at
> yourself, and answer any question I ask in my doc answers. After every finished task, update the doc right away with a report and
> pictures. Start by telling me why this new session started and what the previous chat covered (SESSION_HANDOFF.md, "For the owner").
