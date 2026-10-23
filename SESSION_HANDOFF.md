# Session handoff -- 2026-10-22 23:45 (supervisor; queue RUNNING, task 117)

## For the owner
Why the switch: this supervisor saw 4 tasks finish (114, 115, 116, 119), the point where SUPERVISOR.md says hand off.
What you asked and decided in this chat:
- "Is the game possible on this Mac?" Answer given: not everything at once at stable 30 fps; frame time, flight-speed streaming,
  memory and lighting are the four conflicts. Full pre-generation of the 0.1 m world does not fit (hours to days, tens to hundreds of GB);
  coarse levels only could.
- Q64 (c) NO rows below 540 ("I don't like how bad it looks already"). Q66 shells stay ON (far edits must stay 3D). Q67 shadow clamp then
  reuse, judged on golden-hour stills. Q65 try Medium, show me. Q69 stutter first. Q68/Q70 defaults.
- Look review (114): yes, all of it, plus "far terrain looks like low-poly land instead of 0.1 m cubes". The bad "Low" you saw was the RIG
  builds (480x270 + FSR 1), not play. Side faces: warm bounce. Biome borders: blend after the renderer fixes.
Results:
- 114 look review: 10 defects, `VISUAL_REVIEW_114.md`.
- 115 stutter: the slow frames are slow VIEWS on the GPU, not hitches (removing all spikes: > 33 ms ~70 -> ~68%; p99 106 -> ~75 ms).
  Real spikes = chunk-crossing eviction -> task 123 queued (Q73 (a), after 118).
- 116 shell sky leap: exact, steps -37..-64%, frame 0 ms -> OFF. Third step-count cut worth ~0 ms: the shell march's cost is NOT its step
  count. The 30 fps plan's estimates are therefore optimistic; next real step is likely WHY the march is slow on this GPU (design question
  after 117/118). Q74 (a) leave the code in, OFF.
- 119 far-band dashes: gone (they were far sun shadows, not rock); 29.2% -> 5.2% dash pixels.
Running now: 117 (Medium trial: stills + numbers, no default change). Then 120 cube-face lighting, 121 sky + haze, 122 biome border blend;
118 shadow clamp waits on 117 (Requires), 123 eviction slicing waits on 118.
Open / waiting on you: nothing blocking yet; 117 will stop for "make Medium the default?".
Close: tmux window 0:0 (this old supervisor). The old 0:1 was already closed.

## Next supervisor
- Doc "Queue decisions" https://claude.ai/code/artifact/681cc873-8163-45da-a51f-32ffca6d7468 (container id the same). Now tab node
  `3b5fd194-f74f`. This session appended paragraphs after the Q64-Q70 block, newest last: `.66628` (Q64-70 answer), `.67381` (114),
  `.68108`/`.68733` (115, Q73), `.68966`/`.69577` (116, Q74), `.69804` (119). Ids are `mk2vq5dkmp6.<n>`. Last question number: Q74.
  Task reports tab and Screenshot Archive still lack 108-119 sections (only these Now-tab paragraphs).
- Wait loop: background `until tail -n +1213 Logs/overnight/runner.log | grep -qE 'BLOCKED|done |queue runner end|FAILED|INCOMPLETE'`.
- Blocked task that FINISHED: move its .md + .HANDOFF.md to done/ with a timestamp prefix; the runner re-checks `Requires:` every loop.
- Owner "pause builds for an hour": `tools/heavy.sh pause; touch tools/overnight/PAUSE` + a background `sleep 3600` that removes PAUSE,
  resumes, restarts the runner if dead. "resume": TaskStop that timer, rm PAUSE, `tools/heavy.sh resume`.
- After 117 and 118: ask the owner the shell-march design question (find why the march is slow on the M1 GPU: SIMD divergence /
  occupancy, e.g. a profiling or restructure task) -- design work, needs approval.

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the overnight queue as described there until
> nothing is left that can run without me. Only one agent works on the project at a time; you do no engineering
> yourself. Put every question for me in the "Queue decisions" doc. Start by telling me why this new session started and
> what the previous chat covered (SESSION_HANDOFF.md, "For the owner").
