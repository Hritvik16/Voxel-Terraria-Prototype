# 139-look-dark-overhangs-comparison

Model: opus

Approval: owner, 2026-10-25, in chat, answering Q91 (what to do about pure-black overhangs after 138's tilt failed): "i need a side by
side comparison of this. Pitch black dark world would be interesting gameplay no? OR is it not worth it?" CAPTURE-ONLY comparison:
NO default change, NO look change shipped, no fix. Not a performance task (no timing arms; one cost note per option from 138's
numbers or a single cost-split if trivial is fine but not required).

## Background
136/138: the sky-visibility probe is ONE yes/no ray; blocked -> skyOpen = `_UndergroundAmbientFloor` (0.02-0.06), multiplying both
ambient and the shadow floor, so a face that is blocked AND out of the sun goes pure black: surface ledges/arches in daylight AND every
cave. Options on the table: (A) today; (B) a higher floor (art constant, 0 ms); (C) a proper soft sky term (design, ~+2 ms; can only be
APPROXIMATED here -- use 136's `nosky` arm as the "fully open" end of the range and say so); (D) dark caves but lit open overhangs is the
look a player would expect -- note honestly whether any arm gets close.

## Do (ONE launch, 3-5 places, short fixed waits, launch budget rule)
Places: cliff2 ledge (136 A), stone arches (136 other), the built cave mouth + tunnel interior + sealed room (138 `-skytiltqa 1`), and
one NATURAL cave or deep canyon floor in tg2 if a quick pose exists. Times: midday and dusk (sun ~10 deg); night for the cave rows.
Arms per place: (1) today; (2)-(4) floor = three values chosen to span "barely visible" to "dim grey" (e.g. 0.12 / 0.25 / 0.4 -- pick
from what reads well, state the values); (5) `nosky` (sky probe off = everything treated as open sky).
Torch rows: in the tunnel and sealed room, place ONE torch (Phase 7 point light via the existing edit/light path; see TASKS.md
`-farshadeshots` night torches) and shoot arms (1), (3) and (5) at night, so the owner sees pitch black + torch vs dim + torch.
Use the play look (FSR 3 dyn) for the sheet, native copies for counts.

## Deliverable
ONE clean labelled side-by-side sheet per question (rows = places/times, columns = arms, column headers in plain words like
"today: pitch black", "floor 0.25: dim grey", "sky check off"), saved under `Screenshots/139/`, plus pure-black pixel counts per cell.
HANDOFF `## For the doc`: the sheet path(s) with captions saying where to look, and 3-4 plain-words sentences on the gameplay trade
(pitch-black caves need light sources; surface overhangs in daylight going black). No STOP_FOR_OWNER needed: the supervisor asks.
TASKS.md paragraph if a capture flag was added. Fast EditMode suite only if code changed (wait out an owner pause; never skip).
