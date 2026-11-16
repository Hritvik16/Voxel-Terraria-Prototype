# 217-plan-gdd-town-safety-answers: DONE (plan only; doc + GAME_DESIGN.md updated, two new owner questions C6-C7)

## What changed
- Claude Doc "Game design" (https://claude.ai/artifact/BprGXbxRr6W5MPdDAXxYp8), edited directly (rev 51 -> 71), and the repo copy
  `GAME_DESIGN.md`, same content. `TASKS.md` "Task 217" paragraph. Commit: see `git log -1` (217).
- "Town safety by NPC behaviour" heading now Decided (Q-AK, 15 Nov). The sketch is now six short rules: one danger map (C1), lights/rooms/people
  lower it (C1), guards (C3), leaving (C2), the 64 m settlement (C5), size steps by resident count (C4).
- "What it needs" became "Missing pieces, in build order": step 0 furnished buildings (placement phase); before character work 1 danger map,
  2 room detection (incl. 64 m grouping and resident count), 3 NPC day routine (daily stay/leave check, guard night walk), 4 spawn rule;
  humanoid generator and creatures go with character work. Note says steps 1-4 are rules + data, testable with stand-ins (marked as the
  agent's plan). No new numbers: only 0.55 MB, 8 m, 64 m, 5/15/40 from the answers; "about"/"for example" kept.
- C1-C5 answer lines filled ("(a)", Q-AK) with where each was folded in.
- Settlements table: "How residents react to edits" Leaning -> Decided (leaving), Open (C7); "Size tiers" gains the resident-count steps;
  "Your own settlement" says no claimed area, 64 m (C5); "Safety" Open (rules) -> Decided. A6 (a)'s "inside your claimed area" replaced by
  "(Terraria's rule; no claimed area: C5 (a))". Header and "New, 15 Nov" lines updated.
- Placement checklist: towns + "Settlements by the 64 m rule"; lairs + "Danger-map memory ~0.55 MB, reported vs switch OFF"; Before character
  work: the town-safety item now names steps 1-4.

## Evidence
- Plan only: no Unity, no tests, no memory cost (task file). Doc writes acked at rev 54, 56, 71 with no refusals; the doc matched the repo copy
  before editing (rev 51 read).

## Left / next
- Owner answers C6 and C7 (below). Nothing queued from this task.

## For the doc
What changed (task 217, plan only):
- Town safety is Decided (C1-C5 all (a), Q-AK): six rules in the Game design doc, Settlements rows updated, A6 wording fixed.
- Build order before character work: danger map -> room detection -> NPC day routine -> spawn rule (furnished buildings in placement first).
- Numbers carried from the answers: danger map 1 byte per 8 m cell = ~0.55 MB on Medium; settlement = ~64 m; steps e.g. 5 / 15 / 40 residents.
- No images: a plan-only text task, nothing visible changed.

NEW questions (recommendation first; also in the Game design doc under C5):
**C6. How is the 64 m measured?**
- (a) As a chain: a room joins a settlement when it is within about 64 m of any room already in it; two settlements that grow to within 64 m
  merge. Cost: none beyond room detection. Unlocks: a city of any size stays one settlement, districts and castle included.
- (b) From a centre (the first room or a hall): everything within about 64 m of it. Cost: simplest, but a settlement is at most about 128 m
  across, so a big generated city splits into several.
**C7. Anger and repair requests?** The brief said residents react to edits with "anger, leaving, asking for repairs"; C2 decided leaving.
- (a) No separate rule: leaving (C2) is the reaction, and a resident whose room fails the daily check says why (no door, no bed, no light,
  too dangerous) while the days run out. Cost: one line of text per failed check. Unlocks: the player sees what to fix ("asking for repairs").
- (b) A mood value per resident (anger) that changes prices and dialogue, on top of leaving.
- (c) Leaving only, silent.
