#!/bin/bash
# tools/overnight/start.sh -- run the overnight queue inside tmux, so it keeps
# going with the terminal closed. Watch it: `tmux attach -t overnight`.
# Removes the PAUSE file first only with --resume (otherwise a paused queue
# stays paused and the runner stops at once, saying so).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)" || exit 1
[ "${1:-}" = "--resume" ] && rm -f tools/overnight/PAUSE
if tmux has-session -t overnight 2>/dev/null; then echo "tmux session 'overnight' already exists -- attach: tmux attach -t overnight"; exit 1; fi
tmux new-session -d -s overnight -c "$PWD" "$SHELL -ic 'tools/overnight/run-queue.sh; echo; echo queue runner finished -- see tools/overnight/HANDOFF.md; exec $SHELL -i'"
echo "overnight queue started in tmux session 'overnight' -- attach: tmux attach -t overnight"
