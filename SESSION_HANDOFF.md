# Session handoff -- 2026-11-07 ~06:00 (supervisor; queue RUNNING 182; two owner questions open)

## For the owner
Why: this chat passed ~9 MB (size hook), so I hand over to a fresh session. Two questions are waiting on you in the doc ("Questions for you" tab):
- **Q-M** make the new simple material rules (`-matrules 1`) the default? Recommendation (a): fix then default (task 182 is already running).
- **Q-T** turn the voxel trees (`-trees 1`) on by default? Recommendation: keep OFF for now (shell pools overflow in dense forest, far chunks cost 2x).
Covered in this chat (6-7 Nov):
- 179 rivers (curved, cut-in banks, route test green) done `c3f612b`. 180 voxel trees done `cd7e342`, flag off. 181 resource deposits done `a7a7bfb`, flag off (`-ores 1`).
- Your idea: material distribution should come from a few simple per-biome rules (depth below surface, slope, height, fluid nearness, one noise per biome; biome paints everything,
  no 3-D noise, no separate bedrock field; materials come from the ORIGINAL surface/water map so edits never change what is below). You approved it; 180b built it behind `-matrules 1`
  and stopped for your look. Defects found: deep stone as black/blue boxes on tall faces; far-hill brick mosaic. 182 (running) fixes both, default stays OFF.
- Docs: Task reports tab has 179, 180, 180b, 181 (178c short). Screenshot Archive has 178c and 179 (not yet 180, 180b, 181).
- The old supervisor tmux session `voxel-sup` was closed. Close THIS window (`voxel-sup-1623`) once the new one is up.
Running now: 182 (matrules fixes). Nothing is queued behind it.

## Next supervisor
- NEVER-IDLE GAP: queue ONE owner-independent task behind 182 at once. Candidates (placement plan APPROVED, https://claude.ai/artifact/T6Rt4gT4HeWtj898ctkzSd; task sessions cannot read claude.ai, so copy the numbers into the task file):
  (1) fluid pockets at rest, all 8 fluids placed by worldgen (plan step 4); (2) towns' modular building kit (plan step 3); (3) a geometry-first cliff task is NOT approved: ask the owner first with a picture
  (honest reading of Q-M's forest cliff: stripes gone but one flat brown wall); (4) C6 landform shapes / C7 stamps (WORLD_GEN_LOOK_PLAN). Do NOT write look tasks that depend on the Q-M answer.
- On `done 182`: read tools/overnight/HANDOFF.md, add a 182 section to the doc's Task reports (newest first, look at pictures first), update Q-M with the new pictures (the doc says "I already queued 182"). The owner chooses the default.
- Doc housekeeping still owed: "Now" tab (e04dd89d-c6f3) Done rows for 177b-181 and queue table tidy; archive `Screenshots/180`, `180b`, `181` (see tools/archive/README.md; the live page must be viewed before publishing: Artifact read, then merge check by sheet names, then publish).
- Open owner choices elsewhere: push `origin/engine-phase`? (not asked again); stamp openings need a sea keep-out (KNOWN_ISSUES seed 2026, not queued); big rivers barely meander (renderer flow map; owner call, not queued);
  CLAUDE.md index line "Slow route test red" can now read green (owner edits CLAUDE.md).
- Blocked dir has STOP_FOR_OWNER tasks 180 and 180b (their code is committed; patches only hold the scene file). Never put `Requires:` on a task that stops for the owner (181 lost its Requires on 180 for this).
- Doc: "Queue decisions" https://claude.ai/artifact/DrfR6aQJHYmhnQ2Syu33Hq (project 681cc873-8163-45da-a51f-32ffca6d7468). Docs tool notes: node ids in `update` targets must be FULL ("mk2vq5dkmp6.38815", not ".38815");
  blobs: Artifact publish with asset:true and file_path(s), then `create object=blob payload={asset:<id>}`; edits are written directly (no suggest). Questions body 08a1b590-58d9, Task reports d611dfba-5116.
- Waiter: one background until-loop on runner.log (BLOCKED|done |queue runner end|FAILED|INCOMPLETE), re-arm after each event. Watcher tab is open.
- Owner rules (memory dir): plain words, look at every picture, questions only in the doc tab, no paid-plan suggestions, simple-material-rules note, game-feel notes.

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the overnight queue. You do no engineering yourself.
> Put every question for me in the "Queue decisions" doc's "Questions for you" tab, in plain words with pictures you have looked at
> yourself. Start by telling me why this new session started and what the previous chat covered (SESSION_HANDOFF.md, "For the owner").
