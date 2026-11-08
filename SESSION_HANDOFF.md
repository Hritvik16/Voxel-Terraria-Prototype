# Session handoff -- 2026-11-07 ~17:30 (supervisor; queue RUNNING 187; one owner question open)

## For the owner
Why: this chat passed ~9 MB (size hook), so I hand over to a fresh session. One question waits for you in the doc ("Questions for you" tab):
- **Q-F** make the new "stamps sit in the land" rules (`-stampfit 1`: cave mouths, ledges, geyser basin, hoops) the default? Recommendation (a): yes. Three captioned pictures are in the doc.
Covered in this chat (7 Nov):
- 182 material-rule fixes done. You said (a) default ON: 186 done (`063fe27`), the material rules are now the default, `-matrules 0` is the way back.
- 183 fluid pockets at rest (`-fluidpockets 1`, off) done. 184 house kit built (`2aacfba`) but found NO town sites: you said (a): 187 site planner is running now (`-sites 1`, off).
- 184b cut the whole suite 72 -> 20.8 min (fast 2.6 min). You said 20 min is fine; 184c parked in `later/`.
- 185 stamps fit the land done (`c7ab7e4`), behind `-stampfit 1` -> Q-F.
- You said trees stay OFF (Q-T a): 188 (pool overflow + cost) is queued.
- Doc: Now tab rewritten, Q-M/Q-S/Q-T marked ANSWERED in place, Task reports has 182 and 183.
Running now: 187 (town/village site planner). Queued behind it: 188 (trees). Close THIS window (`voxel-sup-0534`) once the new one is up.

## Next supervisor
- NEVER-IDLE: queue ONE owner-independent task behind 188 at once (ideas: C6 landform signatures `WORLD_GEN_LOOK_PLAN.md`; place BuildingKit on the sites behind `-towns 1` AFTER 187 is done and reviewed; stamp default switch if Q-F = a).
- On Q-F answer: (a) -> task "stampfit default ON" like 186 (see tools/overnight/done 186 file / commit 063fe27); (b) -> follow-up for the geyser leftovers (comb of soil at uphill edge, narrow 1.5 m steps); (c) -> drop.
- On `done 187`: read tools/overnight/HANDOFF.md, report with pictures (island maps with sites marked), then queue the building placement task.
- DOC OWED (Queue decisions, https://claude.ai/artifact/DrfR6aQJHYmhnQ2Syu33Hq): Task reports (newest first, tab node d611dfba-5116, insert after .38815) needs 184 (kit; short, kit_sheet), 184b (before/after table), 185, 186; Now tab (node e04dd89d-c6f3) Done rows for 183-186 and queue table; move ANSWERED sections from Questions to the Answered tab; date lines still say 6 Nov.
  Screenshot Archive (`tools/archive/README.md`) needs folders 180, 180b, 181, 182, 183, 184.
- Doc tool notes: update ops: replace takes `with` (not `source`) and needs `force:true` (or ifHash+ifRev); insert takes `source`; node ids in targets must be FULL ("mk2vq5dkmp6.18"); blobs: Artifact publish asset:true to the doc url, then `create object=blob engine=blob payload={asset:<id>}` (use the create tool), cite `![alt](blob/<id>)`.
  Questions body node 08a1b590-58d9 (insert new questions after block mk2vq5dkmp6.18). Reading a whole tab overflows: use `payload {kind:'view', outline:true}` or `{kind:'search',...}`.
- Blocked dir: STOP_FOR_OWNER tasks 184 (kit committed, no sites; answered), 184b (answered: 20 min fine), 180, 180b (committed). Their commits are in git; nothing to requeue.
- CLAUDE.md edit proposal from 184b (owner edits): "Budgets: fast <= 3 min, SUITE=all ~20 min (generator changes), SUITE=nightly (~70 min) only before a merge to main; a test > 2 s is Slow; engine test > 2 s or cost report is Nightly."
- Mac was on battery earlier; AC now. Pause state: not paused. Waiter: one until-loop on runner.log (BLOCKED|done |queue runner end|FAILED|INCOMPLETE), re-arm after each event; remember to count lines first (`n=$(wc -l < Logs/overnight/runner.log)`) so old lines do not match.
- Owner rules (memory dir): plain words, look at every picture, questions only in the doc tab, 20 min suite OK, no paid-plan suggestions.

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the overnight queue. You do no engineering yourself.
> Put every question for me in the "Queue decisions" doc's "Questions for you" tab, in plain words with pictures you have looked at
> yourself. Start by telling me why this new session started and what the previous chat covered (SESSION_HANDOFF.md, "For the owner").
