# 175-torch-flood-on-in-play

Model: opus

Requires: 174b-torch-flood-falloff-tune

Approval: owner, 2026-11-05, chat: "Tune first, then on" -- the torch flood ON in play WITH the falloff setting the owner picks from 174b (its
answer is in the doc or 174b's HANDOFF; ask the supervisor if unclear). LOOK stage (ROADMAP.md "CURRENT ORDER"). Read `tools/overnight/blocked/172-look-torch-cinematic.HANDOFF.md`
and TASKS.md tasks 158, 164, 165, 169.

## Do
1. Make the torch flood the PLAY default with the direction mode the owner picked in Q105 and 174b's picked -tfi/-tfk: `-torchflood 1 -tfdir 2` (L3 incremental path and
   169's edit cuts as they are today). `-torchflood 0` = the old point-light torches. Wire it the way other play defaults are wired (play's
   preset / PlaySettings, F6 menu entry if the existing settings menu has a lighting section). Rigs and capture modes stay UNCHANGED
   (they keep today's behaviour unless they pass the flags), so every frozen rig baseline stays comparable.
2. Do NOT turn on `-fsr3sc 10` here; task 174 owns that decision.
3. Check in play with ONE capture-only launch: a night camp, a cave, a wall (172's `-torchcine` scenes, default play flags only, no extra
   flood flags) that the flood is really on by default, plus placing and digging a torch in survival so edits re-flood.
4. Fast suite once. Rough cost note (look-stage rule) from runs you already do: the flood's memory (CPU/GPU MB as 165 reported) and 158's
   per-torch cost; no timing arms.
5. TASKS.md paragraph; CLAUDE.md "Commands" play line if it lists play defaults.

## Not in scope
170's memory trim (parked for the performance stage).
