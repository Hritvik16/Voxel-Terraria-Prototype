#!/bin/bash
# tools/overnight/start.sh -- run the overnight queue inside tmux, so it keeps
# going with the terminal closed. Watch it: `tmux attach -t overnight`.
# Removes the PAUSE file first only with --resume (otherwise a paused queue
# stays paused and the runner stops at once, saying so).
# Runs at low CPU priority (nice 10) so the owner's own apps stay responsive.
# Runs under `caffeinate -ims` (2026-10-03, task 12): the Mac cannot idle- or
# system-sleep while the runner lives (2026-10-02 it slept 7 h 20 min mid-queue).
# Battery-critical hibernation still wins -- keep it on AC.
# IDLE_MIN=0 tools/overnight/start.sh --resume  -- start tasks while the Mac is in use.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)" || exit 1
[ "${1:-}" = "--resume" ] && rm -f tools/overnight/PAUSE
if tmux has-session -t overnight 2>/dev/null; then echo "tmux session 'overnight' already exists -- attach: tmux attach -t overnight"; exit 1; fi
tmux new-session -d -s overnight -c "$PWD" "$SHELL -ic 'IDLE_MIN=${IDLE_MIN:-15} caffeinate -ims nice -n 10 tools/overnight/run-queue.sh; echo; echo queue runner finished -- see tools/overnight/HANDOFF.md; exec $SHELL -i'"
echo "overnight queue started in tmux session 'overnight' -- attach: tmux attach -t overnight"
