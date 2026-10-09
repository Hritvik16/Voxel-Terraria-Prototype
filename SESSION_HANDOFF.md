# Session handoff -- 2026-10-08 23:55 (supervisor; queue running, 35 B6 in progress)

## For the owner
**Why a new session:** this chat reached ~11 MB (the size hook fired). You had answered the B5
review pack, so it was a natural stop. 35 B6 is running and nothing is waiting on you.

**What the previous chat covered (8 Oct, 19:15-23:55):**
- **You played tg2 and asked four things** (screenshots in `~/Desktop/Bugs?/` -- keep that folder until
  tasks 38/39 have run):
  - Resolution: the game draws 960x540 and stretches it with no upscaler (`RenderPreset.cs:49`).
    E1 (engine phase) = 1080p from 540 or 720 with MetalFX/STP; 540 vs 720 is your call then.
    Estimate: 540 + upscaler ~ today's ~32 ms (no FPS gain by itself); 720 ~55 ms. 60 fps needs E4.
  - Moire: TAA (F4) removes it, but you found it soft/blurry with silhouette jitter and shimmer, no
    FPS change. So TAA stays OFF; those four complaints become E1's pass conditions.
  - Shadow dots: queued as **38**. Fluids: they simulate anywhere within 128 m of you; the HUD's
    "pool FULL 129,689x" looked wrong: queued as **39**. Both run after the B steps (your wish).
  - Lowest hardware for 1080p60: unknown (only this Mac measured). Today's code would need ~2x an
    M1 GPU (M1 Pro class / GTX 1660 Super-RTX 3050 class, rough); the engine phase aims for M1 itself.
- **34 B5 done** (`005815e`, `fff8753`, SUITE=all 645/0): 5 signature features per world, a canyon
  always, >= 1.2 km apart, 82-93% of land sees one within 3 km.
- **You answered Q29 (B5 pack): 1 a, 2 b, 3 a, 4 a, 5 a.** So: terrain looks natural; open-ground miss
  accepted; queued **37b deepen canyons** and **37c highland variation** after B10 (targets are the
  supervisor's proposal, written in the doc); **40** black-crescent render bug after 39.

**Running:** 35 B6 danger record. **Next:** 36 B8, 37 B10 (B10 review pack + palette pick), 37b, 37c,
38, 39, 40.
**Open for you:** nothing now. The Mac went on battery at 23:09 (87%) -- plug it in overnight.
**Close:** tmux session `0`, window 4 (this 19:15 session). Windows 1-3 too, if still open.

## State
- Branch `surface-rebuild`, not pushed. `Assets/Scenes/Playground.unity` stays modified (build side
  effect). Runner alive in tmux `overnight` (IDLE_MIN=0). Watcher tab open (bash -c loop).
- Queue: 35 (running), 36, 37, 37b, 37c, 38, 39, 40. Blocked: none.
- Last suites: SUITE=all 645/0/15 (task 34).

## Next for the supervisor
- On each done: one row in the doc's Done table (insert after header row mk2vq5dkmp6.1425; 5 cells:
  time, task, result, commits, evidence), and replace the "Running now" paragraph mk2vq5dkmp6.55626
  (read it first for its h; guarded replace with markdown).
- When 37 finishes: the B10 review pack + palette pick. Better: wait until 37b and 37c are done too
  and show all three in ONE pack (owner was told the B10 pack will show 37b/37c). Then ask about v4
  default + merge, then start the engine-phase plan doc (ROADMAP.md step 3; include Q26's item; E1's
  pass conditions from the owner's TAA verdict: sharp at 1080, no silhouette jitter, no shimmer, no
  moire; note roadmap says play = 480+FSR but code default is Quality 960x540 nearest; offer a
  Windows build/test for a real PC min-spec answer).
- Images in the doc: Artifact publish (url = doc link, asset true, file_paths) -> batch create
  object=blob {asset:id} -> `![caption](blob/..)`. Look at each image first.

## Documents
- Queue decisions: https://claude.ai/code/artifact/681cc873-8163-45da-a51f-32ffca6d7468
  (doc id 681cc873-8163-45da-a51f-32ffca6d7468, body node 8460ce71-6ba5, last rev 396;
  Done table mk2vq5dkmp6.1424, header row mk2vq5dkmp6.1425; "Running now" mk2vq5dkmp6.55626;
  "Waiting on you" lead paragraph mk2vq5dkmp6.27623, now "Nothing is open. ..."; Q29 closed)
- Surface rebuild plan: https://claude.ai/code/artifact/f6222d44-1457-4a92-9460-3390d44e75fd
- Game Design Brief: https://claude.ai/code/artifact/d31b4321-77c3-4026-93d2-460c190950bf
- Terrain review: https://claude.ai/code/artifact/7443de71-6716-4f99-869b-98d3c6814fd7
- TAA proposal (steps 1-3 approved): https://claude.ai/code/artifact/6745ee4a-4af7-482f-b07d-822e46270918

## Notes
- **No periodic updates**; speak only when a task starts/finishes/blocks or the owner is needed.
- Keep supervisor sessions short: hand off after ~3-4 finished tasks or a long chat.
- A `Requires:` line must name real task files only (the runner treats every word as a dependency;
  "(none; ...)" would block forever).
- Task agents number follow-ups after the last file they see; re-check `ls queue done blocked
  dropped` before numbering. Name order = run order.
- The owner answers in the doc by typing letters under a question; check with a `sinceRev` read.
- Moving a task file out of `blocked/`: plain `mv` (untracked files).

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the overnight
> queue as described there until nothing is left that can run without me. Only one agent
> works on the project at a time; you do no engineering yourself. Put every question for me
> in the "Queue decisions" doc. No periodic status updates: tell me only when a task starts or
> finishes, blocks, or needs me. When this session gets long, hand off to a fresh one yourself.
> Start by telling me why this new session started and what the previous chat covered
> (SESSION_HANDOFF.md, "For the owner").
