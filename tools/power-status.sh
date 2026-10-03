#!/bin/bash
# tools/power-status.sh [LABEL] -- print ONE line: power source and battery
# charge from `pmset -g batt`, e.g.
#   power start: 05:58:01 source=AC Power charge=100%; charged
# Exit status: 0 on AC power, 2 on battery (or unknown), so a caller can warn.
# Added 2026-10-03 (task 12): every rig run on 2026-10-02 ran on battery and
# nothing in the logs showed it; at ~1% one checker arm ran slow and the next
# slept mid-arm. Rigs log this at start and end; run-queue logs it per task.
B=$(pmset -g batt 2>/dev/null)
SRC=$(echo "$B" | head -n 1 | sed -n "s/.*drawing from '\(.*\)'.*/\1/p")
CHG=$(echo "$B" | grep -oE '[0-9]+%;[^;]*' | head -n 1)
echo "power ${1:-now}: $(date +%T) source=${SRC:-unknown} charge=${CHG:-none}"
[ "$SRC" = "AC Power" ] && exit 0 || exit 2
