# Session handoff -- 2026-11-17 ~22:30 (supervisor; builds PAUSED until ~23:05; queue 236 -> 235 -> 234; no owner question open)

## For the owner
Why: this chat passed ~18 MB (every turn re-sends it), so the rules say to start a fresh, cheaper session.
Covered in this chat (17 Nov 00:30 - 22:30):
- Finished and reported in the doc: 226 (look polish: voxel colour, crevice, haze, bloom, all ON), 227 (shade light one ray per 4x4 block: walk_near 106.8 -> 72.3 ms), 228 (coarse far sun shadows: saved 2/6/0 ms, false shadows, kept OFF), 229 (torch light kernel only when torches near: 6-13 ms by day, nothing at night), 231 (why LotL looks cinematic: sky and low sun matter most; the five post factors close ~30 % of the gap), 232 (low-res sun shadows: exact, saves ~0 ms, OFF), 233 (pale sky + clouds ON, grade ON, low-sun start built but OFF: +27 ms at forest), 230 (budget check script works; first run NOT CLEAN because the Mac was in use; found night torch memory ~6 GB).
- Answered: Q-AL (C6 a, C7 a), Q-AP (a: build sky/low sun/grade), Q-AQ (a: sky + grade ON, low sun OFF). You asked: why LotL looks better (answered by 231), and said the "performance features" (low sun at start, torches off) are not real gains if low sun and torches occur in play. Agreed: judge by worst-case poses. You chose that (point 1). I told you 30 fps is not reachable from the known cuts: walk_open ~31 ms, walk_near ~60-67, jungle ~66-70, forest ~96-100 (held-pose captures); sun shadows ~16-21 ms, sky fill ~6-12, shells ~6, flood +9 at night.
- You said: push and merge to origin main once stable. Task 234 only CHECKS readiness (whole test suite, budget check, merge plan); NOTHING touches main or origin without your explicit go (ask first).
- You said: pause builds for an hour (22:04). `tools/heavy.sh pause` + `tools/overnight/PAUSE` are set; a background timer in the OLD session resumes heavy and removes PAUSE at ~23:05. It does NOT restart the runner: the new supervisor must (see below).
Running now: nothing (paused). Queued: 236 (bug: night torch memory 6 GB -> find and fix, I went ahead with option (a) of 230's question; "Going ahead unless you object" is in the doc), then 235 (worst-case poses in the budget check + clean rebaseline; needs an IDLE Mac, ~35-40 min), then 234 (merge readiness; no merge).
Open for you: nothing. Going-ahead item: 236 (b = accept the night peak, if you object).
Mac notes: the memory watchdog killed 3 builds on 17 Nov (swap > 10.2 GB; Firefox held ~7 GB) and left PAUSE. Budget checks and timing launches need the Mac idle (hididle watches input): tell the owner before 235 starts. Usage-limit waits stalled 232 for ~4 h.
Close THIS window once the new one is up: `! tmux kill-session -t <the old window shown in tmux ls>` (list with `tmux ls`; the old supervisor is `voxel-sup-0018`).

## Next supervisor
- First: `date`; builds resume ~23:05 (check `cat /tmp/sup_resume.out` for RESUMED_AT; if missing after 23:10: `tools/heavy.sh resume; rm -f tools/overnight/PAUSE`). Then check `sysctl vm.swapusage` (< ~7 GB), and start the runner:
  `tmux send-keys -t overnight "cd '<repo>' && IDLE_MIN=0 caffeinate -ims nice -n 10 tools/overnight/run-queue.sh" Enter` (macOS: `sed -i ''`).
  `wc -l < Logs/overnight/runner.log > /tmp/sup_n`, then ONE background until-loop on runner.log (BLOCKED|done |queue runner end|FAILED|INCOMPLETE); re-arm after each event.
- Queue order: 236 (Requires 233, done), 235 (Requires 236), 234 (Requires 235). After 234 the queue is EMPTY: never leave it idle. Candidates (need owner OK where noted): diet-2 torch kernel (-6 ms with torches near; changes performance: ask), a plan task "path to 33 ms at the worst poses" after 235's table (plain-words options with look trade-offs; owner decides), jungle second-bounce plan, 212's T2/T4.
- 235 and every timing run: tell the owner to leave the Mac idle (no typing/mouse) for ~40 min.
- After 234 says READY: ASK the owner for an explicit go before merging to `main` or pushing `origin` (https://github.com/Hritvik16/Voxel-Terraria-Prototype.git; `main` == `origin/main`, engine-phase is 223+ commits ahead). Tasks never push.
- Housekeeping still owed (mine): archive Screenshots 209b-233 into Archive 2 (`tools/archive/README.md` volume 2 recipe), mark Q-AF to Q-AK, Q-AN, Q-AO headings ANSWERED and move to the Answered tab. Done tasks move to `tools/overnight/done/<stamp>_<name>.md`; 230 was moved there although its baseline is pending (235 does the clean rebaseline).
- Doc (Queue decisions, https://claude.ai/artifact/DrfR6aQJHYmhnQ2Syu33Hq, container {kind:project,id:DrfR6aQJHYmhnQ2Syu33Hq}):
  - Reports: "Task reports" (node d611dfba-5116); newest heading is 230 `mk2vq5dkmp6.202290`; insert new ones `before` it. Done rows: "Now" (node e04dd89d-c6f3, table `mk2vq5dkmp6.7022`, append `side:end`, `as:"blocks"`, content = array of `{type:"tableRow",content:[{type:"tableCell",content:[{type:"paragraph",content:[{type:"text",text}]}]}...]}`, 3 cells).
  - Questions: node 08a1b590-58d9. Newest "Going ahead" heading `mk2vq5dkmp6.71188` (236); new questions/sections go BEFORE the newest heading. Q-AQ/Q-AP/Q-AL are marked ANSWERED.
  - Docs recipes: update engine "prose" with `payload.ops`; insert `target {kind:blocks,ids:[full id]}`, `side`, `source {as:markdown, from:{kind:inline, content}}`; a replace of a whole block needs `ifHash` + `ifRev` (read first with `{"kind":"search","blocks":{"first":id,"last":id}}`). Images: Artifact publish {url: doc url, asset:true, file_path(s)} -> docs `create` object=blob engine=blob payload {asset:<id>} -> markdown `![alt](blob/<new id>)`. Look at every picture before captioning.
- Owner rules (memory dir): plain words, look at every picture, questions in the doc tab, no paid-plan suggestions, max two Claude sessions, efficient by construction, answer the owner's questions in the doc and chat, explain performance changes in terms of the WORST case (low sun, torches), not noon.
- Blocked and staying blocked: 178c, 180, 180b, 184, 184b, 208, 216.
- NOT committed by me: `Assets/Scenes/Playground.unity` (regenerated), `Logs/TerrainGen/*`; `git status` shows Screenshots viewer files (git-ignored images).
- auto mode blocks `tmux kill-session` and a Bash call that contains one is denied whole; give the owner a one-line `!` command.

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the overnight queue. You do no engineering yourself.
> Put every question for me in the "Queue decisions" doc's "Questions for you" tab, in plain words with pictures you have looked at
> yourself, and answer any question I ask in my doc answers. After every finished task, update the doc right away with a report and
> pictures. Start by telling me why this new session started and what the previous chat covered (SESSION_HANDOFF.md, "For the owner").
