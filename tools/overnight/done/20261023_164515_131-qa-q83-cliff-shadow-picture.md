# 131-qa-q83-cliff-shadow-picture

Model: opus

Approval: supervisor, 2026-10-23, under the backlog rule "follow-ups that finished tasks name in their handoff (QA poses)":
task 129's HANDOFF named this QA pose as Q83 (a)'s first step. The owner said (2026-10-23) the questions lack pictures to
decide from; Q83 is asked only AFTER this task shows the problem in a real play frame. Capture-only: NO fix, NO default change,
NO look change. Not a performance task (no timing arms).

## Background (from 129's HANDOFF, `tools/overnight/blocked/129-test-lod-vs-fullres-reference.HANDOFF.md`)
Tier-0 terrain (the 0.1 m window, ~0-124 m round the player) never gets sun shadows from terrain outside that window: its
shadow ray counts as LIT at the window edge and at the 7-layer band's ceiling (`Raymarch.compute` TraceShadowRay, ~line 1940;
only shell hits march the shells, ~line 4555). So a cliff more than ~45 m above the player, or more than ~100 m away, shades
the far ground (shells) but not the near ground.

## Do
1. Find, in the default tg2 world (map v4, seed 42), one or two spots at the foot of a tall cliff (>= 60 m high) where, at a sun
   angle you choose (low afternoon sun, behind the cliff), the cliff's shadow should fall on open ground that reaches from
   right under the player out past ~150 m. Prefer the coast cliffs 129 used (`Screenshots/129/coast/` poses) if they fit.
2. In ONE player launch (existing capture flags; add a capture-only pose list if needed), per spot take:
   A. Player at eye height (1.6 m) standing IN the expected shadow, looking along the shadow toward the cliff and out past 150 m.
   B. The same view from ~300 m further back (so the ground in A's view is drawn by shells, not tier 0).
   C. A from-above view (~250 m up) of the same ground, so the shadow's outline and the ~124 m circle round the player show.
   Use the play look (preset path as `./run-play.sh` starts it, FSR 3 dyn) for A and B, plus a native-1080 copy of A.
3. Look at every shot. If the bug shows, the near ground in A is lit while the same ground in B is shadowed, and C shows a
   lit disc round the player inside the shadow. If it does NOT show at any spot you tried, say so plainly with the shots.
4. Build ONE side-by-side image per spot (A | B, and C below) with labels, and save it under `Screenshots/131/`.
5. Fast EditMode suite once at the end if any code changed (capture-only code must not change the default render).

## Deliverable
HANDOFF with `## For the doc`: per spot, the side-by-side image path with a caption saying exactly where to look, one line
on whether the bug shows in play and how big it is (disc radius in m, how often a player would stand in such a spot), and the
two options for the owner: (a) a fix task (continue tier-0 shadow rays into the shell march; name its likely frame-time cost
from 128/130's numbers as an estimate, not a measurement) or (b) leave it. No STOP_FOR_OWNER needed: the supervisor asks Q83.
TASKS.md paragraph if a flag or pose list was added.
