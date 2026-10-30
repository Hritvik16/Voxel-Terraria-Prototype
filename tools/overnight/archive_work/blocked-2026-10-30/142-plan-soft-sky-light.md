# 142-plan-soft-sky-light

Model: opus

Approval: owner, 2026-10-25, in chat (AskUserQuestion), Q91 "Soft sky light (Recommended)": design task FIRST (questions + plan for the
owner), then build. PLAN ONLY in this task: no product code, no default/look change. Not a performance task (no timing arms; cost
estimates from existing numbers are fine).

## Goal (the look the owner chose)
Dark caves (enclosed, underground: black at night, near-black by day unless open to the sky) AND gradually shaded open overhangs in
daylight (ledges, arches, cave mouths read as shaded rock, not black holes). Today one yes/no vertical sky ray decides both ambient and the
shadow floor (136/138/139); every cheap variant failed (138: tilt; 139: floors / probe off).

## Read first
136/138/139 HANDOFFs (`tools/overnight/done/*13[689]*`, `Screenshots/139/darkcompare139.txt`), the sky probe and ambient code in
`Raymarch.compute` (skyOpen / ambientScale / `_UndergroundAmbientFloor`), Phase 7 docs / `AMENDMENT_8_12_PHASE7_GATE.md`, the existing
temporal reuse for shadows (task 118/124), `KNOWN_ISSUES.md` lighting entries.
Generality rule (owner, 2026-10-24): no terrain-specific constants; must hold for any generator output, future features, player builds.

## Do
Write `SOFT_SKY_PLAN.md` (short): 2-3 candidate designs (e.g. a cosine-weighted random sky ray per pixel per frame, accumulated with the
existing temporal reuse; a few fixed directions; a coarse sky-visibility volume from the shells/air-mips), each with: how it looks
(dark caves? overhang gradient? noise/lag when moving or editing?), GPU memory, estimated ms at 540 rows, how it behaves with edits and
the 0.1 m window edge (no pops), what it needs from frozen APIs or layouts (if anything: flag it), and test/oracle changes. Pick ONE
recommendation and a step list (one change per task, each with a look check + one measurement). Include 4-6 SPECIFIC owner questions
with a recommended answer each (e.g. how dark a sealed cave at noon; acceptable noise; does sky light reach through a 2 m hole).

## Deliverable
`SOFT_SKY_PLAN.md` committed; HANDOFF starting with STOP_FOR_OWNER listing the questions (recommendation first). If a quick still helps
explain a candidate, at most 1-2 existing shots may be referenced; no new launches required.
