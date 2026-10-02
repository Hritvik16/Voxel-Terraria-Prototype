# Overnight handoff

**05a-drop-cliff-rise-pose: DONE. cliff_rise_2 removed, task 04 closed (owner's Q3 answer (c)).**
(Still open from 04b: the surface rebuild plan's 11 questions -- see commit 82d4416's HANDOFF.
From 05: `09-nearshimmer-eye2-wall-check.md` is queued; the shimmer baseline is in KNOWN_ISSUES.)

## What changed (commit 0bbd1e6 on design-brief)
- `Assets/Game/PlaygroundCapture.FarBand.cs`: 7805791's three hunks were reverse-applied (`git apply -R`,
  clean): the `rise` locals, the `if (found == 1)` search/fallback block, and the trailing
  `Look("cliff_rise_2", ...)`. No line from 7805791 remains (`grep rise` finds only an unrelated comment).
- `git diff 7805791^ -- Assets/Game/PlaygroundCapture.FarBand.cs` is now exactly 4d0d03c's change
  (the eye-level pose math moved into `Tg2EyeLevelPoses`, from task 05). Diffing the +/- lines confirms it.
  Every other pose, and the order, is as before 7805791.
- `KNOWN_ISSUES.md`, TERRAIN-AWARE BAND PLACEMENT: the TASK 04 paragraph now ends with
  "Pose dropped by the owner's call (2026-10-02, task 05a); the counts are the evidence."

## Evidence
- EditMode: PASS 626 FAIL 0 SKIP 3, 0 `error CS` (`Logs/overnight/05a-editmode.out`).
- No build or rig run, as the task said: capture-only, and no other pose changed.

## Left alone
- `Assets/Scenes/Playground.unity` (modified) and the deleted `queue/05-taa-shimmer-metric.md` were
  already in the working tree before this task. They are not mine, so I did not commit them.

## Next step
- The next queued task. Nothing new was queued by this one.
