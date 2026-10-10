# 56-engine-8-shadow-dots

Approval: owner, 2026-10-08 (genuine bugs, queue after the B steps) and 2026-10-09 (engine plan Q36: ENGINE
PLAN STEP 8, the shadow-dots half; BUGS #1). Queued by the supervisor 2026-10-10. Plan step 7 (the frame-time
pass/fail) is known red (task 51: 16-23 fps at 540 rows, target 60), so the shadow COST half of step 8 waits
for task 57's cost split; this task is the dots fix only. Branch `engine-phase`.

The full spec is the ready task file `tools/overnight/later/38-bug-shadow-dots.md` -- read it and do exactly
that (its `Requires:` line is already met: B10 is done). Changes since it was written:
- Play now starts with FSR 3 dynamic (task 52). Reproduce in play's default AND with `-upscaler none
  -renderpreset quality` (the owner saw the dots on the quality preset, TAA off) to tell an upscaler effect
  from a shadow-pass effect.
- Torch shadow rays exist since task 46 (`cd5266e`); the dots are about SUN shadows.
- No performance arms (PREAMBLE rule 10); name any expected cost change in HANDOFF.
When done, move `tools/overnight/later/38-bug-shadow-dots.md` to `tools/overnight/done/` and mark BUGS #1
fixed (or explained) in `tools/overnight/BUGS.md`.
