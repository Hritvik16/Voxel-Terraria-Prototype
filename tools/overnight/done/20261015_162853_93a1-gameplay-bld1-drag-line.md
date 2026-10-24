# 93a1-gameplay-bld1-drag-line

Requires: 63-gameplay-phase-plan

Approval: owner, 2026-10-15 (supervisor chat, AskUserQuestion on task 92's questions): Q4 (a) "alternate, one gameplay
task after each engine task". BLD1 is the first row of `GAMEPLAY_PHASE_PLAN.md` §4 run order (written by task 92,
`7107b4f`). Queued by the supervisor 2026-10-15. Branch `engine-phase`.

## Change
Implement **BLD1 (drag line)** of `GAMEPLAY_PHASE_PLAN.md`, exactly as its step table describes, and nothing else. Read
the plan's §2 guardrails first. Where the step leaves a look-and-feel or tuning choice, pick the recommended option
yourself, record it in HANDOFF under "Picks (owner may change)", and carry on. STOP_FOR_OWNER only for: a frozen-API
change (GetVoxel, SetVoxel, D.1, the 4.4 table), a memory-layout change, anything on `main`, or a step that turns out
not to fit one task (then do the first useful half, commit it, and say what remains).

**Also (doc fix, same commit):** record the owner's answers to task 92's questions in `GAMEPLAY_PHASE_PLAN.md`
("Owner may change" list and the rows named), dated 2026-10-15:
- Q1 plane stroke: NOT (a). Owner: "look into how Lay of the Land does this [later] ... for now very simply: place one
  block down, extend it into a line by selecting one of its faces, then select one of the line's faces [to extend it
  into a plane]" -> BLD2 = face pull (task 93b1). A later research row: study Lay of the Land's building system.
- Q2 hardcore death: (a) the world locks to a fly-only viewer.
- Q3 classic death: OPEN -- owner: "you only lose some items", exact rule decided in the object-placement phase.
  9b waits for it (soft + hardcore could ship earlier; not queued).
- Q4: (a) alternate gameplay and engine tasks.

## Acceptance
- BLD1's own acceptance from the plan table.
- Fast EditMode suite green; `SUITE=all` if anything under `Assets/TerrainGen/` changed.
- No frame-time measurement (owner rule: features over measurement).
- Screenshots of the feature in play (well lit: midday, sun behind the camera), paths in HANDOFF, each with a one-line
  caption of what to look at. Update the F2 help panel if a key was added.
- HANDOFF `## For the doc`: one-line result, what the owner can now do in `./run-play.sh` (keys), the picks, the
  images.
