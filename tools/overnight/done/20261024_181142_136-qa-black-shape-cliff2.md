# 136-qa-black-shape-cliff2

Model: opus

Approval: supervisor, 2026-10-24, backlog rule "follow-ups that finished tasks name in their handoff (QA poses)": tasks 131 and 135 both
saw a pure-black jagged shape on the near rock wall at the cliff2 spot (131: "black crescent ... overhang underside?"; 135: "large black
jagged shape at the top right (the near cliff's edge)", present in diag and lean stills, so not caused by 133/134). Capture + diagnosis
ONLY: NO fix, NO default or look change. Not a performance task.

## Do
1. Re-shoot cliff2 (task 131's spot, `-lookreview -cliffshadow 1`, sun 30 deg) in ONE launch: the A view at native 1080, the same view
   at noon, from 10-20 m closer and from the side, plus debug views that tell which tier drew the black pixels (view 14 HitInfo) and
   whether they are hits with black shading or misses / out-of-box (e.g. a no-hit or a wrong normal / shadow / AO).
2. Look at every shot. Name the cause as precisely as the shots allow (which tier, which pass: primary hit, shadow, AO, material,
   window/box edge, overhang underside lit only by sky). Check one other overhang elsewhere if a quick pose exists, to say how common it is.
3. Fast EditMode suite only if any code changed (capture-only code).

## Deliverable
HANDOFF `## For the doc`: one labelled image (normal view + crop + the debug view) with a caption saying where to look, the cause in
plain words, how often a player would see it, and a proposed fix with its rough cost (estimate). No STOP_FOR_OWNER needed: the
supervisor asks the owner. TASKS.md paragraph only if a flag was added.
