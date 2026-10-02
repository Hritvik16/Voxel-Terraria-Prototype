Approved by the owner 2026-10-02 (Queue decisions Q4 / plan answer 11a): the
Surface rebuild plan is APPROVED; queue its step B0 now.

Plan (the source of truth for this task -- read its section 4 "Steps", row B0,
and section 8 "Owner answers", which override the plan's defaults):
https://claude.ai/code/artifact/f6222d44-1457-4a92-9460-3390d44e75fd
Exception to preamble rule 7: this task MAY read that Claude Doc (targeted reads:
outline, then the B0 row and section 8). Do not edit it.

Task: step B0 ONLY, as the plan defines it: a report-only metrics harness (EditMode,
no build, no rig) that measures the plan's acceptance numbers on a world, and must
reproduce the terrain review's seed-42 numbers on today's v3 (e.g. median slope ~30
degrees, ~73% "mountainous", ~4% open -- take the exact figures and tolerances from
the plan). No generator change: v3 output must stay byte-identical (determinism
tests green). Opt-in like the existing report tests (env var), so the default
EditMode run does not slow down.

Images (owner, 2026-10-02: reports must include images): the harness also writes
small top-down map renders (PNG) of what it measures for a seed -- at least
landform class, slope bands and easy ground -- under Logs/ or Screenshots/, and
HANDOFF lists their paths. Look at them before concluding.

Acceptance: EditMode 0 FAIL; the harness reproduces the review's seed-42 numbers
within the plan's tolerance (show both side by side in HANDOFF); runs on seeds
42/7/2026/31337 and reports them; PNGs written and looked at. Commit. Do NOT start
B1: B1 onward is queued by the supervisor after the TAA tasks.
