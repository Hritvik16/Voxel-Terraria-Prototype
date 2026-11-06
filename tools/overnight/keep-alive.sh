#!/bin/bash
# tools/overnight/keep-alive.sh -- keeps the overnight queue going through long usage-limit waits (owner, 2026-11-06:
# "make sure the queue keeps running"). The runner gives a task MAX_WAITS x 20 min of usage-limit waits, then blocks it
# as FAILED and, with every later task hitting the same limit, ends. This watcher (read-mostly; it starts no agent itself):
#   every 10 min, if the runner has ENDED and a task in blocked/ was blocked right after a usage-limit wait
#   ("usage limit on NAME ... (N/N)" then "BLOCKED NAME (FAILED"), moves NAME.md back to queue/ and restarts the runner.
# Tasks blocked for any other reason (STOP_FOR_OWNER, a real failure) are left alone. Stops when the queue is empty,
# after HOURS (default 14), or on tools/overnight/PAUSE. Runs under caffeinate so the Mac does not sleep between runs.
#   tmux new-session -d -s keepalive 'tools/overnight/keep-alive.sh'
cd "$(git rev-parse --show-toplevel)" || exit 1
DIR=tools/overnight; LOG=Logs/overnight/runner.log; HOURS="${HOURS:-14}"
end=$(( $(date +%s) + HOURS * 3600 ))
say() { echo "$(date '+%F %T') keep-alive: $*" | tee -a Logs/overnight/keepalive.log; }
say "start (HOURS=$HOURS)"
while [ "$(date +%s)" -lt "$end" ]; do
  [ -f "$DIR/PAUSE" ] && { say "PAUSE file present -- stopping"; exit 0; }
  ended=0
  tail -n 1 "$LOG" | grep -q 'queue runner end' && ended=1
  tmux has-session -t overnight 2>/dev/null || ended=1
  if [ "$ended" = 1 ]; then
    moved=0
    for f in "$DIR"/blocked/*.md; do
      [ -f "$f" ] || continue
      case "$f" in *.HANDOFF.md) continue;; esac
      n=$(basename "$f" .md)
      last_block=$(grep -n "BLOCKED $n (" "$LOG" | tail -n 1)
      echo "$last_block" | grep -q 'FAILED' || continue
      ln=${last_block%%:*}
      sed -n "$((ln > 3 ? ln - 3 : 1)),$((ln - 1))p" "$LOG" | grep -q "usage limit on $n" || continue
      mv "$f" "$DIR/queue/" && moved=$((moved + 1)) && say "requeued $n (blocked only by the usage limit)"
    done
    if ls "$DIR"/queue/*.md >/dev/null 2>&1 && { [ "$moved" -gt 0 ] || ! tmux has-session -t overnight 2>/dev/null; }; then
      tmux kill-session -t overnight 2>/dev/null; sleep 2
      IDLE_MIN=0 "$DIR/start.sh" >/dev/null && say "runner restarted"
    elif ! ls "$DIR"/queue/*.md >/dev/null 2>&1; then
      say "queue empty -- stopping"; exit 0
    fi
  fi
  sleep 600
done
say "HOURS limit reached -- stopping"
