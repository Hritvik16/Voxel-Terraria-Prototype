#!/bin/bash
# Release a parked TIMING task into the queue once the owner has stepped away.
#   tools/overnight/release-when-idle.sh later/238-perf-split-rest-at-forest.md [MIN]
# Waits until the Mac has had no keyboard/mouse input for MIN minutes (default 20),
# moves the task file into queue/ and, if the queue runner has ended, restarts it
# in the tmux session `overnight` (window 0). Run it in its own tmux window:
#   tmux new-window -t overnight -n idlegate "tools/overnight/release-when-idle.sh later/238-....md"
# Why: timing arms are only comparable with nobody using the Mac (fanless M1 Air,
# CLAUDE.md "warm steady state"); everything else runs while the owner works.
set -u
cd "$(dirname "$0")/../.." || exit 1
DIR=tools/overnight
src="$DIR/${1:?task file under tools/overnight/, e.g. later/238-x.md}"
MIN="${2:-20}"
LOG=Logs/overnight/runner.log
[ -f "$src" ] || { echo "no such task: $src"; exit 1; }
idle_s() { ioreg -c IOHIDSystem | awk '/HIDIdleTime/ {print int($NF/1000000000); exit}'; }

echo "$(date '+%F %T') waiting for ${MIN} min without input to release $(basename "$src")"
while [ "$(idle_s)" -lt $((MIN * 60)) ]; do sleep 60; done

mv "$src" "$DIR/queue/" || exit 1
echo "$(date '+%F %T') released $(basename "$src") into queue/ (idle $(( $(idle_s) / 60 )) min)" | tee -a "$LOG"

# Restart the runner only if its last start has a matching end.
last_start=$(grep -n 'queue runner start' "$LOG" | tail -1 | cut -d: -f1)
last_end=$(grep -n 'queue runner end\|queue empty -- done' "$LOG" | tail -1 | cut -d: -f1)
if [ -n "$last_end" ] && [ "${last_end:-0}" -gt "${last_start:-0}" ]; then
  tmux send-keys -t overnight:0 "IDLE_MIN=0 caffeinate -ims nice -n 10 tools/overnight/run-queue.sh" Enter
  echo "$(date '+%F %T') runner was idle -- restarted it"
fi
