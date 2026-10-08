# Session handoff -- 2026-10-08 (owner chat; queue restarted, 29 running)

## State (2026-10-08, owner chat)
- **Branch `surface-rebuild`** (from `main`). NOT pushed. `Assets/Scenes/Playground.unity` stays modified (build side effect).
- The 4-5 Oct queue run stopped without engineering: 29 hit the WEEKLY usage limit after ~9 min
  (reset 8 Oct 06:00 PT), then 30 failed on an expired OAuth login (owner re-logged in 8 Oct).
  Both were requeued on 8 Oct; 29's untested partial patch is `Logs/overnight/b3/29-first-attempt.patch`
  (restart note appended to the task). Runner restarted in tmux `overnight` (IDLE_MIN=0).
- Queue: **29** B3 light erosion (owner, 8 Oct: run AS WRITTEN; owner then checks "still natural?")
  -> **30** column-pass sharing. Nothing after 30 yet.
- Last suites: fast 618/0/12 (task 26), SUITE=all 629/0/12 (B2).

## Owner answers 8 Oct (recorded in the Queue decisions doc, rev 314)
- **Q24 palette bricks: (c) NOT NOW.** The world design is not done (placement, ores, overhaul
  unseen), so counts/savings/caps would be measured on terrain that will change. Revisit after the
  terrain overhaul and placement. Item 5 (cascade cap shrink / task 31) waits too. Do NOT queue T1.
- **Q23: (a, a)** -- rig frame-time bisect LATER (after the B steps); `UploadDirty` inside-the-box
  check (skip and count) as a small task after 30. NOT YET WRITTEN.
- **Q22 (answered 4 Oct, a, a)** -- (2) an evidence task: memory cost of a 0.4 m voxel tier to
  ~256 m vs pushing the far band out; (3) coarse-first step 1 after 30 (PLAN_COARSE_FIRST_GEN.md
  section 5, plus the owner decisions 30 records there). NEITHER WRITTEN YET.
- Q6/Q7 answered (a): owner looks at the TAA/checker stills; nothing queued.
- Owner pushed back on B3 ("does 60 passes make terrain more controllable?"): B2's per-class medians
  vary by seed (gentle 8-15 deg), and 29 measures only island-wide totals. The owner chose to run it
  as written anyway. When 29 reports, quote the "class median slope g/b/s" column beside the totals.
- Owner asked about per-task models: `run-queue.sh` takes one `MODEL=` for the whole queue only.

## Owner, 8 Oct (later): ways of working (CLAUDE.md, PREAMBLE rule 10, SUPERVISOR.md; commit 0785a7e)
- Opus only, no per-task models. Measure performance ONLY in performance tasks; milestone checks.
- Recommended option is the default for reversible choices ("going ahead unless you object").
- Owner's biggest issue: we measure more than we build. Focus on shipping features.
- **Terrain reviews: ONE review pack after B5 and one at the end of the B steps.** B steps run back
  to back (tests + metrics + renders each); a step whose own renders look clearly broken still
  stops. 29's "still natural?" check folds into the B5 pack.
- **Roadmap (owner, 8 Oct):** 29 -> 30 -> UploadDirty box check (Q23-2, small fix) -> B4..B8 back
  to back -> review packs (after B5, at end) -> v4 default + merge + ONE milestone perf check ->
  movement-abilities question batch (owner wants VERY FAST movement; get a top speed) ->
  coarse-first step 1 (Q22-3, deferred, NOT dropped) -> placement (ores, objects, towns).
  The 0.4 m tier memory evidence (Q22-2; owner: matters so 0.1 m detail can shine) is done with
  palette bricks (Q24) after placement. Rig bisect (Q23-1) PARKED -- owner: "the worst thing to do".

## Next for the supervisor
Write and queue, after 30: the UploadDirty box check (Q23-2), then B4 (easy areas only) and the
following B steps from the surface rebuild plan, each WITHOUT streaming/perf arms (PREAMBLE rule 10). Re-check `ls queue done blocked dropped` before numbering (31 is taken).

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
