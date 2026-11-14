# Session handoff -- 2026-11-13 ~21:00 (supervisor; queue RUNNING 197; ONE owner question open: Q-AA)

## For the owner
Why: four tasks finished in this chat (194b, 195, 195a, 196), the point where the rules say to start a fresh, cheaper session.
Covered in this chat (13 Nov, 12:45-21:00):
- You closed the old supervisor (`40`).
- Done and reported in the doc with pictures:
  - **194b**: the lighting shader compiles 25 % faster (25 -> 19 min; whole build 31 -> 20 min; C#-only rebuild under 5 min). Nothing on screen changes.
  - **195**: ores ON in every world (`-ores 0` = old world). +47 MB; far chunks ~1/3 slower to build (perf stage note).
  - **195a**: trees + towns ON in every world (`-trees 0 -towns 0` = old). Memory peak 2,504 of 3,000 MB. The near memory pool is now full
    everywhere, no holes in the shots.
  - **196**: pool walls are now natural basins (30 deg banks, shore level with the fluid). Pools still OFF -> your question **Q-AA**.
- 194b's pictures showed the rectangular pits come back in 1 of 3 launches, and 195 had one launch with a whole rock wall missing.
  You said it might just be terrain not loading in time -> task **197** checks that first (running now).
- Queued behind it: **198**, a check of the faint white dashes on steep rock faces (world or drawing bug; fix only a drawing bug).
**Open for you:** Q-AA in the "Questions for you" tab: turn the surface pools ON now? (recommended (a) ON).
Close THIS window (tmux session `voxel-sup`) once the new one is up: `! tmux kill-session -t voxel-sup`.

## Next supervisor
- On every `done NAME`: read tools/overnight/HANDOFF.md `## For the doc`, look at 1-2 pictures (side-by-side with PIL into the scratchpad), upload,
  Task reports entry (newest first), Now table cells, archive folder. Then a 3-5 line chat note.
- If the owner answers Q-AA (a): write `199-defaults-fluidpockets-on.md` like 195/195a (TerrainGenChunkSource default flip; `-fluidpockets 0`
  = old world byte for byte; whole suite; one launch of 3 pool poses + memory). (c) = water only. Move Q-AA to answered.
- 197 may STOP_FOR_OWNER if the fix needs a pool/eviction/layout change -> question with its pictures.
- NEVER-IDLE: after 197 only 198 is queued. Next owner-independent ideas: 194's open item (refused chunk inside shell 1's hole draws the far
  band) if 197 shows it; perf-stage notes collected so far (generation +30-40 % from ores/trees, near pool full, lean kernels unused under torch
  flood -- 194b note 2) go into a plan task only when the look stage ends (owner order 2026-11-05). Water look and arches are look questions -- ask first.
- DOC (Queue decisions, https://claude.ai/artifact/DrfR6aQJHYmhnQ2Syu33Hq; container {kind:project,id:DrfR6aQJHYmhnQ2Syu33Hq}):
  - Task reports tab node d611dfba-5116: newest report heading is 196 `mk2vq5dkmp6.146052` -> insert new reports BEFORE it.
  - Now tab node e04dd89d-c6f3, queue table mk2vq5dkmp6.7022. Rows (0 = header): ... 14 194b done, 15 195 done, 16 195a done, 17 196 done,
    18 197 running, 19 198 queued. Cols: 0 status, 1 task, 2 result. Whole-cell edit: `replace` target {kind:cell, table, row, col} +
    with {as:'text', from:{kind:'inline',content}} + `ifRev` (Now tab rev 59 at handoff). New rows: insert side:'end' on the table id,
    as:'blocks', content [{type:row,content:[{type:cell,content:[{type:paragraph,content:[{type:text,text}]}]}, ...]}] (key is `content`, not `children`).
  - Questions tab node 08a1b590-58d9: Q-AA (open) heading `mk2vq5dkmp6.30338`, its "Your answer:" paragraph `.31730`. New questions go BEFORE `.30338`.
  - Screenshot Archive: https://claude.ai/artifact/9y7VxxX8FmsDMtVbSyFd2d -- recipe in `tools/archive/README.md`; a new session's first publish is
    refused until it has viewed the live page: Read a few lines of the saved copy, check the sheet list matches (only your new folder differs),
    publish again unchanged. Commit tools/archive/index.json by path.
- Doc tool notes: images: Artifact publish {url: doc url, asset:true, file_paths:[...]} -> `mcp__claude_ai_Claude_Docs__create` object=blob
  engine=blob container=C payload {asset:<id>} -> `![alt](blob/<id>)`. Search reads use payload {kind:'search', text:'...'}.
- Runner: tmux session `overnight`. If it ENDS, give the owner ONE line: `! tmux kill-session -t overnight; IDLE_MIN=0 tools/overnight/start.sh --resume`.
  Usage-limit waits resolve by themselves. If `tmux ls` says "error connecting": `mkdir -m 700 /private/tmp/tmux-501; kill -USR1 <tmux server pid>`.
- Waiter: one background until-loop on Logs/overnight/runner.log (BLOCKED|done |queue runner end|FAILED|INCOMPLETE); count lines first; re-arm after each event.
- Owner rules (memory dir): plain words, look at every picture before captioning, questions in the doc tab, no paid-plan suggestions, max two Claude sessions.

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the overnight queue. You do no engineering yourself.
> Put every question for me in the "Queue decisions" doc's "Questions for you" tab, in plain words with pictures you have looked at
> yourself. After every finished task, update the doc right away with a report and pictures. Start by telling me why this new
> session started and what the previous chat covered (SESSION_HANDOFF.md, "For the owner").
