# Session handoff -- 2026-11-12 ~17:00 (supervisor; queue RUNNING 189; one owner question open: Q-V)

## For the owner
Why: this chat passed ~11 MB (size hook), so I hand over to a fresh session. One question waits for you in the doc ("Questions for you" tab):
- **Q-V** make "materials by the rules only, no random spray" (`-matvariant 0`) the default? Recommendation (a): yes. Two captioned before/after pictures are in the doc; all 6 poses in `Screenshots/188a/viewer.html`.
Covered in this chat (12 Nov):
- Disk cleanup: about 22 -> 36 GB free (caches, builds, logs; compare-project Library; old screenshots zipped to your Google Drive).
- Queue restarted after the weekly reset. Done today: 187 town/village sites (`-sites 1`, off), 187a stamps-fit now DEFAULT (your Q-F a), 188 trees no longer overflow the far-view memory (trees still OFF), 188a `-matvariant 0` switch (default unchanged until Q-V).
- You asked about the tint patches: that was the random variant spray (~22 % of ground) -> 188a. The renderer's soft warm/cool tint is left alone.
- You said yes to 190 (houses on the 187 sites, towns spread apart, `-towns 1`) and 191 (preview: everything ON vs OFF, ends asking which switches become default).
- Doc: Task reports has 188 and 188a with pictures; Now tab queue table current.
Running now: 189 (landform shapes: dune ridges, slot canyons, alpine ridges, `-landsig 1`). Queued behind it: 190, 191. Close THIS window once the new one is up.

## Next supervisor
- On Q-V: (a) -> small task "matvariant default OFF" like 187a (flip `matVariantOff` default, re-pin `MatVariantTests` to the new arm, `-matvariant 1` = old); (b) -> same plus the owner's chosen rule(s) for lost materials; (c) -> nothing.
- On every `done NAME` (owner rule 12 Nov, memory doc-update-after-every-task): FIRST read tools/overnight/HANDOFF.md `## For the doc`, look at 1-2 pictures (downscale with python PIL into the scratchpad), upload, add a Task reports entry (newest first) + update the Now table rows. Then a 3-5 line chat note.
- NEVER-IDLE: after 191 nothing owner-independent is queued. Ideas: Screenshot Archive catch-up is supervisor work, not a task; worldgen look plan `WORLD_GEN_LOOK_PLAN.md` next item after C6; lighting under the tree canopy is near-black (seen in 188's jungle shot) -> a lighting look task is a good owner-independent follow-up.
- 190 must also explain the grey box on the horizon (seen in 187 eye_frontier, 188 far jungle, 188a frontier).
- DOC (Queue decisions, https://claude.ai/artifact/DrfR6aQJHYmhnQ2Syu33Hq, project id DrfR6aQJHYmhnQ2Syu33Hq):
  - Task reports tab node d611dfba-5116: insert new reports after block mk2vq5dkmp6.38815 (lands on top).
  - Now tab node e04dd89d-c6f3: queue table mk2vq5dkmp6.7022 (rev 13). Rows' first-cell paragraphs: 188a .7427 (done), 189 .7606 (running), 190 .7719 (then), 191 .7847 (then). Reword a cell with replace + ifHash (read with `{kind:'search', blocks:{first:'mk2vq5dkmp6.3229', last:'mk2vq5dkmp6.7022'}}` for hashes).
  - Questions tab node 08a1b590-58d9: Q-V is the top block (heading mk2vq5dkmp6.20818, "Your answer:" .21922).
  - Owed: Screenshot Archive (`tools/archive/README.md`) needs folders 180-188a.
- Doc tool notes: replace takes `with` + ifHash/ifRev; insert takes `source {from:{kind:'inline',content}, as:'markdown'}`; full node ids ("mk2vq5dkmp6.18"); images: Artifact publish {url: doc url, asset:true, file_paths:[...]} -> `mcp__claude_ai_Claude_Docs__create` object=blob engine=blob container {kind:project,id} payload {asset:<id>} (batch cannot create blobs) -> cite `![alt](blob/<id>)`. Reading a whole tab overflows: use `{kind:'view', outline:true}` or `{kind:'search', blocks:{first,last}}`.
- Runner: tmux session `overnight`. If the runner ENDS, the supervisor cannot kill tmux sessions (auto mode); give the owner ONE line to send alone: `! tmux kill-session -t overnight; IDLE_MIN=0 tools/overnight/start.sh --resume`.
- Waiter: one background until-loop on Logs/overnight/runner.log (BLOCKED|done |queue runner end|FAILED|INCOMPLETE); count lines first (`n=$(wc -l < Logs/overnight/runner.log)`) so old lines do not match; re-arm after each event.
- Owner rules (memory dir): plain words, look at every picture before captioning, questions in the doc tab, no paid-plan suggestions, max two Claude sessions.

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the overnight queue. You do no engineering yourself.
> Put every question for me in the "Queue decisions" doc's "Questions for you" tab, in plain words with pictures you have looked at
> yourself. After every finished task, update the doc right away with a report and pictures. Start by telling me why this new
> session started and what the previous chat covered (SESSION_HANDOFF.md, "For the owner").
