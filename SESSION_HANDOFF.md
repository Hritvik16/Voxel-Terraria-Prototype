# Session handoff -- 2026-10-10 07:30 (supervisor; queue RUNNING task 49)

## For the owner
**Why a new session:** four tasks finished in this supervisor chat (the rule is to hand off after
3-4), and the chat has grown. Nothing is lost: everything is in the Queue decisions doc.

**What the previous chat covered (9 Oct 23:00 - 10 Oct 07:30):**
- You gave the engine plan the green light (Q36). Q1 a, Q2 a, Q5-Q8 a. **Q3:** compare all three
  resolutions. **Q4:** you asked for ready-made upscalers instead of building our own, so step 3 became
  "plug in FSR 1 / STP / FSR 3 port and compare".
- You said the doc was hard to navigate and asked for a report after every task. The Queue decisions doc
  now has tabs: **Now** (running / waiting on you / Done table), **Task reports** (numbers + pictures per
  task, newest first), **Answered**, **Archive (to 9 Oct)**. Tasks must now end their handoff with a
  "For the doc" block (PREAMBLE rule 8); SUPERVISOR.md's done-rule copies it into the doc.
- **45** one world-map generator: the Editor map now equals the game's (same hash), map build 27.5 -> 1.5 s,
  suite 665/0; Gemini's lava-torch fix merged.
- **46** torch shadows: no light through walls (0.061 -> 0.000), emissive bleed on, Phase 7 rig 37/0.
  Q37 (black recesses in the lava cavern): going ahead with (a) accept until the light grid, unless you object.
- **47** upscalers plugged in: none / TAA / FSR 1 / STP / FSR 3 all work; fixed STP drawing nothing in builds.
- **48** comparison: stopped for your pick, **Q38** (recommended FSR 3 at dynamic 540-720).
- All on branch `engine-phase`; `main` untouched, nothing pushed.
- Usage: 70% of the week at Fri 22:57 (resets Thu 06:00).

**Running:** task 49 (engine step 4: memory ledger -- counts how full each memory pool gets). Started 07:21.
**Open for you:** Q38 upscaler pick; the disk is 97% full (6.4 GB free) -- OK to delete task 48's 190 MB
of raw stills?; a usage screenshot before steps 5-6 are queued.
**Close:** the old supervisor's tmux window (`claude-retry-4036-1791585229869`, window 0, titled
"Overnight queue supervisor handoff"); the new supervisor closes it.

## State
- Branch `engine-phase` (from `surface-rebuild`): 45 `5410b23`, 46 `cd5266e`, 47 `a14b0f4`, 48 `f792468`.
- Queue: 49 running. Blocked: `48-engine-3b-upscaler-compare` (STOP_FOR_OWNER Q38); `37e` leftovers are
  reference only.
- Runner in tmux `overnight`; task-watcher Terminal tab is open.
- Last suites: SUITE=all 665/0/18 (task 45); fast 634/0/14 (task 48). Phase 7 rig 37/0 (46).

## Next for the supervisor
1. Arm the runner-event wait (SUPERVISOR.md "The loop"). On 49 done: report into the Task reports tab
   (49 has no images -- a table only) and the Now tab.
2. Before queueing steps 5 (fluid tiles for awake fluid only, BUGS #3) and 6 (cascade caps from 49's
   PeakUsed), ask the owner for a usage screenshot (both are M/S; the week was at 70% Fri night).
   Copy 49's measured peaks INTO those task files.
3. On Q38: record it in the Answered tab; write a small task that makes the pick the default in play
   (the old "TAA on by default" rule is satisfied: the owner compared), then step 7 (frame-time
   milestone, warm ABBA, CLAUDE.md measurement rules) -- requires 2, 3 done.
4. If the owner OKs the disk cleanup: `rm Screenshots/Engine03/compare/*__*.png` (uncommitted raw stills).
5. CLAUDE.md line ~54 says `MAPV=3` is "byte-identical pre-rebuild map": no longer true after 45 (same
   shapes, Burst floats). Fold the fix into the next task that edits CLAUDE.md, or tell the owner.

## Documents
- Queue decisions: https://claude.ai/code/artifact/681cc873-8163-45da-a51f-32ffca6d7468
  Tabs: Now = body node `3b5fd194-f74f` (rev 16; "Running" paragraph `mk2vq5dkmp6.121`, "Next up"
  `mk2vq5dkmp6.357`, Q38 `mk2vq5dkmp6.2876`, Q37 `mk2vq5dkmp6.1933`, Done table `mk2vq5dkmp6.886` --
  insert rows as `"as":"blocks"` row JSON, 4 cells; Usage paragraph `mk2vq5dkmp6.700`).
  Task reports = body `d611dfba-5116` (rev 5; insert new reports AFTER the intro `mk2vq5dkmp6.13`).
  Answered = body `4e4a30eb-dbfc`. Archive = body `8460ce71-6ba5`.
- Engine phase plan: https://claude.ai/code/artifact/62d9391e-b05a-42d0-83c2-34d9bfeb04f7 (repo copy
  ENGINE_PHASE_PLAN.md, now marked APPROVED)
- Game Design Brief: https://claude.ai/code/artifact/d31b4321-77c3-4026-93d2-460c190950bf

## Notes
- Images: Artifact publish (url = doc link, asset true, file_path[s]) -> Docs `batch`
  `{"verb":"create","object":"blob","engine":"blob","payload":{"asset":"<id>"}}` -> `![alt](blob/<minted>)`
  plus an italic caption. Look at each image before placing it.
- Owner answers arrive as short paragraphs under questions; read each tab with `sinceRev`.
- The 5-hour window can bind too (97% at 22:57 Fri): if a screenshot shows it near full, delay the runner
  start until its reset (a background `until [ $(date +%H%M) -ge HHMM ]` loop, then start.sh).

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the overnight
> queue as described there until nothing is left that can run without me. Only one agent
> works on the project at a time; you do no engineering yourself. Put every question for me
> in the "Queue decisions" doc. No periodic status updates: tell me only when a task starts or
> finishes, blocks, or needs me. When this session gets long, hand off to a fresh one yourself.
> Start by telling me why this new session started and what the previous chat covered
> (SESSION_HANDOFF.md, "For the owner").
