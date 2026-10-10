# Session handoff -- 2026-10-09 22:30 (supervisor; queue STOPPED, waiting on the engine plan)

## For the owner
**Why a new session:** this supervisor chat reached ~8 MB (the size hook fired), and it is a natural
stopping point: the terrain phase is closed and the queue is stopped waiting for your approval of the
engine phase plan.

**What the previous chat covered (19:00-22:30):**
- **37e flat lake water** finished: every lake sits still at one level. It made rectangular shores,
  so you picked a fix (Q35 3a) -> **42** made shores follow the ground (grid-aligned shore 25-36% ->
  8.5-10%) and renamed the landform Badlands -> **Breaks**.
- **41 lava look:** Gemini's lava merged; you answered Q34 a/a/a (keep it; emissive bleed off until
  measured; lava ignores torch light). You said what you liked in the SVO is "how light reacts to its
  surroundings".
- **Gemini** did the lava-ignores-torch fix + a written SVO-vs-ours lighting comparison (branch
  `gemini/lava-torch-light`, bd60a30, in `../VT_gemini`). Code fix correct; the notes' line numbers were
  wrong, but the main finding holds: the SVO's torches cast shadows, ours shine through walls; the
  SVO has bleed on. Both went to BUGS.md #4 and into the engine plan.
- **B10 review pack (Q35):** 1a keep the shipped savanna/badlands colours; 2a park the straight
  biome borders (BUGS deferred); 3a fix the lake shores (42); 4a rename (42); 5a biome size fine;
  6a v4 default + merge.
- **43:** map v4 is the default world; `surface-rebuild` merged into LOCAL `main` (884e993). Nothing
  pushed -- main is 137+ commits ahead of origin; pushing is your call. The perf check was dropped
  (ROADMAP step 3 replaces it).
- **44 engine phase plan** written: https://claude.ai/code/artifact/62d9391e-b05a-42d0-83c2-34d9bfeb04f7
- Usage: 64% of the week at Fri 20:11 (resets Thu 06:00). Your usage page shows a FREE "Full reset"
  (expires Oct 22) -- worth keeping for when the week runs out early.

**Running:** nothing. The queue is stopped.
**Open for you:** Q36 = the engine plan's Q1-Q8 (answer in the plan doc or the Queue decisions doc;
"all (a)" approves it as written). Also BUGS.md #5 (river water steps into lakes behind grid squares)
is your call some day.
**Close:** the old supervisor's window (tmux `claude-retry-4036-1791585229869`, window 1 -- the one
titled with this handoff); the new supervisor closes it.

## State
- Branch `surface-rebuild` = `main` + supervisor/44 commits (tree clean but for Playground.unity churn
  and HANDOFF.md). Runner idle (tmux `overnight`, ended). Queue empty.
- Blocked: `44-engine-phase-plan-doc` (STOP_FOR_OWNER, waiting on Q36); `37e` leftovers are reference only.
- Later: `later/37c`, `38` (shadow dots -> plan step 8), `40` (ledge crescent -> plan step 11b).
- BUGS.md open rows 1-5. Last suite SUITE=all 662/0/18 (task 43).
- Gemini worktrees: `../VT_gemini` (gemini/lava-torch-light bd60a30 -- engine plan step 1 merges it),
  `../VT_gemini2` (audit report only; never merge; delete when the owner agrees).

## Next for the supervisor
1. On the owner's Q36 answers: record them (Answered), write plan step 1's task file from
   `ENGINE_PHASE_PLAN.md` (one compiled world-map generator path; first commit merges
   `gemini/lava-torch-light` bd60a30 and compile-checks + reshoots lava pose 4 under torches), and
   steps 2-3 per Q6. Copy every number the task needs INTO the task file. Engine steps are
   performance work: CLAUDE.md measurement rules apply only to the [measure] steps.
2. Ask for a usage screenshot before queueing big steps late in the week.

## Documents
- Queue decisions: https://claude.ai/code/artifact/681cc873-8163-45da-a51f-32ffca6d7468
  (doc id 681cc873-8163-45da-a51f-32ffca6d7468, body node 8460ce71-6ba5, last rev 448;
  "Waiting on you" lead mk2vq5dkmp6.27623 -- Q36 sits right below it; Done table header row
  mk2vq5dkmp6.1425 (insert rows after it, 5 cells); "Running now" paragraph mk2vq5dkmp6.55626
  (edit it with a `find` replace of its text, no guard needed))
- Engine phase plan: https://claude.ai/code/artifact/62d9391e-b05a-42d0-83c2-34d9bfeb04f7 (repo copy ENGINE_PHASE_PLAN.md)
- Surface rebuild plan: https://claude.ai/code/artifact/f6222d44-1457-4a92-9460-3390d44e75fd
- Game Design Brief: https://claude.ai/code/artifact/d31b4321-77c3-4026-93d2-460c190950bf

## Notes
- Images in the doc: Artifact publish (url = doc link, asset true, file_path[s]) -> Docs `batch`
  `{"verb":"create","object":"blob","engine":"blob","payload":{"asset":"<id>"}}` -> `![alt](blob/<minted>)`
  plus an italic caption line.
- Gemini runs in `../VT_gemini` with shell disabled and cannot read outside its folder: copy reference
  files into it (e.g. `SVO_REF/`) and point the brief at them. Gemini cannot commit; the supervisor
  reviews and commits on its branch.
- Owner answers in the doc as bare "a" paragraphs under each question -- read with `sinceRev`; a
  missing answer = ask in chat (Q2/Q6 were missed once).

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the overnight
> queue as described there until nothing is left that can run without me. Only one agent
> works on the project at a time; you do no engineering yourself. Put every question for me
> in the "Queue decisions" doc. No periodic status updates: tell me only when a task starts or
> finishes, blocks, or needs me. When this session gets long, hand off to a fresh one yourself.
> Start by telling me why this new session started and what the previous chat covered
> (SESSION_HANDOFF.md, "For the owner").
