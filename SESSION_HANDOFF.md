# Session handoff -- 2026-10-10 15:10 (supervisor; queue RUNNING task 52)

## For the owner
**Why a new session:** three tasks finished in this chat (49, 50, 51) and the week's usage is nearly gone;
a fresh session is cheaper per turn. Everything is in the Queue decisions doc.

**What the previous chat covered (10 Oct 07:20 - 15:00):**
- **49** memory ledger: brick pools ~2x too big (~535 MB could go), fluid pool always full, every run over 3 GB.
- You said the upscaler stills were not enough and too dark, and asked for something automated comparing
  looks AND fps. **50** recorded every option on identical frames; **51** timed each option warm and
  re-recorded the motion clips at real rate, in midday light. Both are on the page
  [Upscaler Clips](https://claude.ai/artifact/CDAhSF142yvbD25AVeREWX) (sets: Same frames 30 fps / Real frame rate).
- **51's key number:** no option reaches 30 fps at eye level on this Mac (540-row options 16-23 fps,
  FSR 3 720 10-13, native 6-7).
- Disk clean-up (your OK): 7.1 -> 31 GB free (build debug folders, old run frames, palette voxel exports, Unity cache).
- Usage: 94% of the week at Sat 14:03 (resets Thu 06:00). You will press the free "Full reset" (expires 22 Oct)
  when it hits 100%. Queue paused (`tools/overnight/PAUSE`).

- **Q38 answered 15:05: (a) FSR 3 dynamic** (540-720 rows). Queued 52 (make it the play default; also deletes
  task 48's raw stills and fixes the stale CLAUDE.md MAPV=3 line), 53 (engine step 6: cascade caps from 49's
  peaks), 54 (engine step 5: fluid tiles for awake fluid only; stops for you first if it needs a contract change).
  Pause lifted, runner restarted 15:07.

**Running:** task 52 (started 15:07). **Open for you:** press the free "Full reset" when the week hits 100%
(the runner retries every 20 min for up to 6 h, then sets the task aside).

## State
- Branch `engine-phase`: 49 `1fcfcb8`, 50 `5c2eca3`.., 51 `0624152`/`3fe865a`/`e71ec70`. main untouched, nothing pushed.
- Queue: 52 running, then 53, 54. No PAUSE. Task 48 moved to done/ (Q38 answered).
- Clips: raw in `Screenshots/Engine03/clips{,_realrate}/`; web re-encodes (avconvert Preset1920x1080, H.264,
  <=15 MB per file, the page's per-file limit) in `Screenshots/Engine03/clips_web/` with the page source
  `upscaler-clips.html` (republish with `root` = that folder). All uncommitted.
- Task 48's 190 MB raw stills: delete after Q38 (owner OK'd in principle).

## Q38 as it stands (supervisor's read, posted in the doc under your answer)
STP 540 and FSR 3 720 look closest to native. FSR 3 720 costs +36 ms/frame (10 fps walk) -- not affordable now.
At equal cost (540): STP is sharper but greyer and restless when still (task 48 flagged this may be our
integration); FSR 3 is steadier, true colour, softer. Options to offer: (a) FSR 3 dynamic, climbing to 720 as the
engine phase frees frame time; (b) STP 540 + a small task to fix its tone / rest shimmer first.

## Next for the supervisor
1. Arm the runner-event wait. Report 52, 53, 54 as they finish (Task reports tab + Done row). Move Q38's text to
   the Answered tab when convenient (it is marked ANSWERED in place on the Now tab).
   After 54: plan step 7 (frame-time milestone). Task 51 already shows ~60 ms walk p50 at the quality preset
   (target 16.7), so step 7 is very likely red -> step 8 (shadow cost). Ask the owner whether to skip step 7's
   measurement and go straight to step 8 (CLAUDE.md: skip a measurement whose every outcome leads to the same
   action) -- recommend skipping.
2. Owner-level flag worth raising once: 16-23 fps at eye level is far below 30; plan step 7 (frame-time milestone)
   and the engine-phase perf steps matter more than the upscaler pick.
3. (CLAUDE.md MAPV=3 wording: folded into task 52.)

## Documents
- Queue decisions: https://claude.ai/code/artifact/681cc873-8163-45da-a51f-32ffca6d7468
  Now = body `3b5fd194-f74f` (rev 98; Running `mk2vq5dkmp6.121`, Next up `mk2vq5dkmp6.357`, Q38 `mk2vq5dkmp6.2876`,
  clips note `mk2vq5dkmp6.4002`, owner's Q38 answer `mc1batkm7es.1`, Usage `mk2vq5dkmp6.700`, Done table
  `mk2vq5dkmp6.886`: append rows with `"side":"end"`, `"as":"blocks"`, 4 cells).
  Task reports = body `d611dfba-5116` (rev 8; insert after `mk2vq5dkmp6.13`). Answered = `4e4a30eb-dbfc`.
- Upscaler Clips page: https://claude.ai/artifact/CDAhSF142yvbD25AVeREWX
- Engine phase plan: https://claude.ai/code/artifact/62d9391e-b05a-42d0-83c2-34d9bfeb04f7

## Notes
- Owner's usage screenshot arrives as a doc image: read the blob -> asset id -> Artifact read `path` = asset id.
- Comparison images/clips: midday, sun behind camera (memory `comparison-images-well-lit`).

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the overnight
> queue as described there until nothing is left that can run without me. Only one agent
> works on the project at a time; you do no engineering yourself. Put every question for me
> in the "Queue decisions" doc. No periodic status updates: tell me only when a task starts or
> finishes, blocks, or needs me. When this session gets long, hand off to a fresh one yourself.
> Start by telling me why this new session started and what the previous chat covered
> (SESSION_HANDOFF.md, "For the owner").
