# 135-check-r6-suite-and-pixel

Model: opus

Approval: supervisor, 2026-10-24, follow-up named in 134's HANDOFF ("Next" 1-3). Task 134 (R6 kernel diet, ON by default) committed
73b8ce7 WITHOUT the fast EditMode suite (owner pause) and left one unexplained pixel (cliff2 A native, (260,547), one level). Owner
decision recorded as "going ahead with keep ON unless you object". NO performance change, NO default change in this task.

## Read first
`tools/overnight/done/*134-perf-r6-shell-kernel-diet.HANDOFF.md` (or `blocked/` if not moved), `Screenshots/134/perf134.txt`
(the `TMP134_NOHOIST` temp-build recipe).

## Do
1. FIRST: the fast EditMode suite on the committed code (`tools/with-unity-lock.sh ./run-editmode-tests.sh`). If anything is red,
   fix it if it is clearly 134's (one small fix, re-run), else STOP_FOR_OWNER with the failure.
2. Cause check: temp build with `TMP134_NOHOIST`, then
   `SKIP_BUILD=1 PRESETS=high EXTRA="-cliffshadow 1 -cliffbefore 2 -shellworkers 6 -cliffwait 240" ./run-look-review.sh`; compare
   cliff2 A native0 vs native (shells filled). Result: either "the hoist changes that pixel" or "GPU compiler only (no source cause)".
   Do NOT change the default or remove the hoist; report which, with one crop image (caption: where the pixel is).
3. Normal rebuild of `Builds/Playground.app` from the committed code (no temp defines), and confirm it starts (one short launch or
   the look review's first pose is enough).

## Deliverable
HANDOFF: suite counts, the cause-check result with the crop path, rebuild confirmed. If the hoist is the cause, start HANDOFF with
STOP_FOR_OWNER and the question: keep the hoist (1 px, 1 level, part of the -7.1 / -5.5 ms) or drop it (cost unmeasured). Otherwise
no stop. TASKS.md one line under "Task 134".
