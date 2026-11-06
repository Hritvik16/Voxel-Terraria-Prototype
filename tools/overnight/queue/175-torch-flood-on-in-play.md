# 175-torch-flood-on-in-play

Model: opus

Requires: 174b-torch-flood-falloff-tune

Approval: owner, 2026-11-05, chat: "Tune first, then on" -- the torch flood ON in play WITH the falloff setting the owner picks from 174b (its
answer, 2026-11-05 chat: **S3 = `-tfi 1.6 -tfk 0.35`**, the point lights' own values; see 174b's HANDOFF table). LOOK stage (ROADMAP.md "CURRENT ORDER"). Read `tools/overnight/blocked/172-look-torch-cinematic.HANDOFF.md`
and TASKS.md tasks 158, 164, 165, 169.

## Do
1. Make the torch flood the PLAY default with the direction mode the owner picked in Q105 and 174b's picked -tfi/-tfk: `-torchflood 1 -tfdir 2` (L3 incremental path and
   169's edit cuts as they are today). `-torchflood 0` = the old point-light torches. Wire it the way other play defaults are wired (play's
   preset / PlaySettings, F6 menu entry if the existing settings menu has a lighting section). Rigs and capture modes stay UNCHANGED
   (they keep today's behaviour unless they pass the flags), so every frozen rig baseline stays comparable.
2. Task 174 already made `-fsr3sc 10 -fsr3reactive 1` the play default: the step-3 check runs with those play defaults (174b noted the 40 m strafe crawl 6.9 vs today 4.2 was measured WITHOUT sc10 + mask; report it again with them).
3. Check in play with ONE capture-only launch: a night camp, a cave, a wall (172's `-torchcine` scenes, default play flags only, no extra
   flood flags) that the flood is really on by default, plus placing and digging a torch in survival so edits re-flood.
4. Fast suite once. Rough cost note (look-stage rule) from runs you already do: the flood's memory (CPU/GPU MB as 165 reported) and 158's
   per-torch cost; no timing arms.
5. TASKS.md paragraph; CLAUDE.md "Commands" play line if it lists play defaults.

## Not in scope
170's memory trim (parked for the performance stage).
