#!/bin/bash
# Exits (waking the supervisor) when the queue runner logs the next task end
# (done / BLOCKED) or "queue runner end", or when the runner window is gone.
cd "$(dirname "$0")/../.." || exit 1
LOG=Logs/overnight/runner.log
start=$(wc -l < "$LOG")
while :; do
  new=$(tail -n +$((start + 1)) "$LOG" | grep -E '^[0-9-]+ [0-9:]+ (done |BLOCKED |queue runner end)')
  if [ -n "$new" ]; then
    echo "$new" | cut -c1-300
    exit 0
  fi
  if ! tmux has-session -t overnight 2>/dev/null; then
    echo "tmux session 'overnight' is gone"
    exit 1
  fi
  sleep 60
done
