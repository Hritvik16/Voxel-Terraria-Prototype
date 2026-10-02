#!/bin/bash
# One-screen status of the overnight queue: running task + its last steps, queue, blocked.
cd "$(dirname "$0")/../.." || exit 1
echo "== $(date '+%H:%M')  runner: $(ps -axo command | grep -q '[r]un-queue.sh' && echo alive || echo NOT RUNNING)"
tail -3 Logs/overnight/runner.log | cut -c1-160
echo "== queue: $(ls tools/overnight/queue 2>/dev/null | sed 's/\.md$//' | tr '\n' ' ')"
echo "== blocked: $(ls tools/overnight/blocked 2>/dev/null | grep -v HANDOFF | grep '\.md$' | sed 's/\.md$//' | tr '\n' ' ')"
cur=$(sed -n 's/.* start \([0-9][0-9a-z-]*\)$/\1/p' Logs/overnight/runner.log | tail -1)
echo "== last steps of $cur:"
NOFOLLOW=1 tools/overnight/watch-task.sh "$cur" 2>/dev/null | sed 's/\x1b\[[0-9;]*m//g' | grep -v '^\s*$' | grep -v '^-- ' | tail -${1:-10} | cut -c1-200
