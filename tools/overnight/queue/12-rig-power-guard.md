# 12-rig-power-guard

Follow-up from task 08 (low-risk evidence tooling, no performance change).

Task 08 found that every rig run on 2026-10-02 (tasks 07 and 08) ran on BATTERY;
at ~1% charge a checker arm ran at TAA-only speed with half the rays (arm 6),
then the Mac slept mid-arm (arm 7, a 440-minute "window"). Nothing in the logs
showed it.

Do: make `run-tg2world.sh` (and `run-acceptance-rig.sh` if it is not running)
log `pmset -g batt` (power source + charge) at start and end into its log, and
for `MODE=walk|orbit` print a loud `WARNING: ON BATTERY` line when not on AC
(do not refuse to run). Wrap the player in `caffeinate -dimsu` so the machine
cannot sleep mid-arm. Never edit a run-*.sh while it is running.
Acceptance: one short `MODE=walk MINUTES=1` run showing the new lines; note it
in KNOWN_ISSUES.md's TAA/checkerboard entry. No other changes.
