# Session handoff -- 2026-10-08 12:25 (supervisor; queue running, 32b in progress)

## For the owner
**Why a new session:** the 08:52 supervisor session grew long (three finished tasks plus a long
discussion about engine capabilities), and every turn re-sends the whole history. Handing off at a
quiet point (32b running) keeps the cost down. Nothing is waiting on you except Q25.

**What the previous chat covered (8 Oct, 08:52-12:25):**
- You asked why sessions restart without explanation. New rule (b2fd5f5): every new session opens
  with this section -- why it started, what the last chat covered, what is running, what is open.
- Queued the rest of your 8 Oct roadmap: 32 UploadDirty fix, 32b `run-play.sh` launcher (newest world
  in one command), 33 B4 easy areas, 34 B5 features (your B5 review pack), 35 B6 danger, 36 B8 routes,
  37 B10 savanna + badlands (your B10 review pack + palette pick). B7 and B9 not queued.
- Engine rundown and your verdict: lighting far short of Terraria-in-3D; 480x270 unacceptable; LOD
  steps visible; generation speed and frame time "not good / abysmal"; fluid tick tied to frame rate;
  fluid freezes beyond 128 m; water must always settle flat; far bombs are refused. **You decided:
  player and placement wait until an engine capability + performance phase is done.** Recorded in
  `ROADMAP.md` (5cbc9df, 24d99ec): order, backlog E1-E10, and six tensions the phase plan must
  resolve (top speed, memory, engine rules, far fluid, the detailspeed workaround, E1 before E2).
  `ROADMAP.md` overrides older future plans. Nothing from it is queued; it starts with a plan doc
  after B10.
- Task 29 (B3) was stopped mid-measurement with your OK and restarted as finish-and-report; task 30
  dropped its streaming runs (output byte-identical, so speed is kept regardless).
- Watcher tab rule (ffa23c5): a Terminal tab always follows the running task; open one if missing.
- Your questions answered in chat: time estimate (~20 h of queue, B5 pack ~tonight, B10 ~tomorrow,
  usage limits may stretch it), what 30's 1.76x means (CPU time to generate one 7-layer column,
  isolated test, not frame time), the shell memory trade (~1.8 GB naive, ~150-250 MB estimated with
  GPU-only + compressed shells; unmeasured), resolution (recommended dynamic 540-720p internal +
  temporal upscaler to 1080p; shell distances for 720 rows: 0.2 m 125 m, 0.4 m 249 m, 0.8 m 499 m,
  1.6 m 1 km, 3.2 m 2 km).

**Finished today:** 30 column sharing (e4fa714, 1.76x per column, byte-identical); 29 B3 (469ce00,
slope targets MISSED -> Q25); 32 UploadDirty box check (3cb76d1).
**Running:** 32b launcher. **Next:** 32c slab bug, then B4 -> B5 -> B6 -> B8 -> B10.
**Open for you:** Q25 in the Queue decisions doc (which lever for slopes: (a) soften region shapes
that do not match their class -- recommended; nothing waits on it). Also still yours: the resolution
target (720p / 540p / dynamic) when the engine-phase plan starts. Old tmux windows to close: the
previous supervisor windows in tmux session `0` (`claude-fresh` = the 08:52 session; window 0 = the
07:31 one if still open).

## State
- Branch `surface-rebuild`, not pushed. `Assets/Scenes/Playground.unity` stays modified (build side
  effect). Runner alive in tmux `overnight` (IDLE_MIN=0). Watcher tab open (bash -c loop).
- Queue: 32b (running), 32c, 33, 34, 35, 36, 37. Blocked: none.
- Last suites: SUITE=all 637/0/14 (task 30), fast 622/0/12 (task 32).

## Next for the supervisor
- On each done: one row in the doc's Done table (insert after header row mk2vq5dkmp6.1425).
- When 34 (B5) finishes: build the **B5 review pack** in the doc as one batch of questions (its
  HANDOFF top section has the images + per-class slope table; include B3's "still natural?" images
  from `Logs/overnight/b3/maps/`). When 37 finishes: the B10 pack + palette pick, then ask about v4
  default + merge, then start the engine-phase plan doc (ROADMAP.md step 3).
- If the owner answers Q25 (a)/(b): write ONE B-step task for it (after 37 unless they say sooner).

## Documents
- Queue decisions: https://claude.ai/code/artifact/681cc873-8163-45da-a51f-32ffca6d7468
  (doc id 681cc873-8163-45da-a51f-32ffca6d7468, body node 8460ce71-6ba5, last rev 312;
  Done table id mk2vq5dkmp6.1424, header row mk2vq5dkmp6.1425; "Running now" paragraph
  mk2vq5dkmp6.55626; "Waiting on you" lead paragraph mk2vq5dkmp6.27623)
- Surface rebuild plan: https://claude.ai/code/artifact/f6222d44-1457-4a92-9460-3390d44e75fd
- Game Design Brief: https://claude.ai/code/artifact/d31b4321-77c3-4026-93d2-460c190950bf
- Terrain review: https://claude.ai/code/artifact/7443de71-6716-4f99-869b-98d3c6814fd7
- TAA proposal (steps 1-3 approved): https://claude.ai/code/artifact/6745ee4a-4af7-482f-b07d-822e46270918

## Notes
- **No periodic updates**; speak only when a task starts/finishes/blocks or the owner is needed.
  The owner watches the worker in a Terminal tab "running worker".
- Keep supervisor sessions short: hand off after ~3-4 finished tasks or a long chat.
- Task agents number follow-ups after the last file they see; re-check `ls queue done blocked
  dropped` before numbering. Name order = run order (26c runs before 29).
- This Mac is a fanless M1 Air: back-to-back performance runs get slower (tasks 27/28).

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the overnight
> queue as described there until nothing is left that can run without me. Only one agent
> works on the project at a time; you do no engineering yourself. Put every question for me
> in the "Queue decisions" doc. No periodic status updates: tell me only when a task starts or
> finishes, blocks, or needs me. When this session gets long, hand off to a fresh one yourself.
> Start by telling me why this new session started and what the previous chat covered
> (SESSION_HANDOFF.md, "For the owner").
