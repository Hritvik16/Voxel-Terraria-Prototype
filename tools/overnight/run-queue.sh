#!/bin/bash
# tools/overnight/run-queue.sh -- run queued tasks, EACH IN A FRESH headless
# Claude Code session (owner, 2026-10-01: save plan usage; overnight work).
#
#   tools/overnight/queue/NN-name.md   one task per file, run in name order
#   tools/overnight/PREAMBLE.md        rules every task session gets first
#   tools/overnight/HANDOFF.md         written by each task, read by the next
#   tools/overnight/done/              finished task files (timestamped)
#   Logs/overnight/                    one JSON result + stderr per task
#
# Stops when: the queue is empty; tools/overnight/PAUSE exists (checked before
# every task -- `touch` it to pause, delete it to resume); a task fails; or a
# task writes STOP_FOR_OWNER in HANDOFF.md (a decision only the owner can make).
# On a usage limit it waits WAIT_MIN minutes and retries, up to MAX_WAITS times.
#
#   tools/overnight/run-queue.sh                 # run everything queued
#   MODEL=sonnet tools/overnight/run-queue.sh    # cheaper model for routine work
#   MAX_TASKS=2 tools/overnight/run-queue.sh
#
# Permission mode is `auto`: the session cannot ask anyone, and auto mode blocks
# risky actions instead of prompting. Unity runs still take the shared lock
# (the task's own scripts do that).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)" || exit 1
DIR=tools/overnight; Q=$DIR/queue; D=$DIR/done; L=Logs/overnight
mkdir -p "$Q" "$D" "$L"
MAX_TASKS="${MAX_TASKS:-20}"; WAIT_MIN="${WAIT_MIN:-20}"; MAX_WAITS="${MAX_WAITS:-18}"
CLAUDE_BIN="$(command -v claude)" || { echo "claude CLI not on PATH"; exit 1; }
say() { echo "$(date '+%F %T') $*" | tee -a "$L/runner.log"; }

branch="$(git branch --show-current)"
if [ "$branch" = "main" ]; then say "refusing to run on main -- check out a working branch first"; exit 1; fi
say "queue runner start on branch $branch ($(ls "$Q"/*.md 2>/dev/null | wc -l | tr -d ' ') task(s) queued)"

n=0
while [ "$n" -lt "$MAX_TASKS" ]; do
  [ -f "$DIR/PAUSE" ] && { say "PAUSE file present -- stopping before the next task"; break; }
  task="$(ls "$Q"/*.md 2>/dev/null | sort | head -n 1)"
  [ -z "$task" ] && { say "queue empty -- done"; break; }
  name="$(basename "$task" .md)"; ts="$(date +%Y%m%d_%H%M%S)"; out="$L/${ts}_${name}"
  prompt="$(cat "$DIR/PREAMBLE.md")

---
## YOUR TASK: $name

$(cat "$task")"
  hmt0="$(stat -f %m "$DIR/HANDOFF.md" 2>/dev/null || echo 0)"
  waits=0
  while :; do
    say "start $name"
    "$CLAUDE_BIN" -p "$prompt" --permission-mode auto --output-format json ${MODEL:+--model "$MODEL"} \
      > "$out.json" 2> "$out.err"
    rc=$?
    if [ "$rc" -eq 0 ] && ! jq -e '.is_error == true' "$out.json" >/dev/null 2>&1; then break; fi
    if grep -qiE 'usage limit|rate limit|limit reached|resets' "$out.json" "$out.err" 2>/dev/null && [ "$waits" -lt "$MAX_WAITS" ]; then
      waits=$((waits + 1)); say "usage limit on $name -- waiting ${WAIT_MIN} min ($waits/$MAX_WAITS)"
      sleep $((WAIT_MIN * 60)); continue
    fi
    say "task $name FAILED (rc=$rc) -- stopping the queue; see $out.json / $out.err"; exit 1
  done
  # A -p session that stops to "wait for a notification" exits with rc 0 and
  # kills its background jobs (2026-10-01) -- unfinished, not done.
  if [ "$(stat -f %m "$DIR/HANDOFF.md" 2>/dev/null || echo 0)" = "$hmt0" ]; then
    say "task $name INCOMPLETE (HANDOFF.md not written) -- left queued; stopping. Result: $(jq -r '.result // ""' "$out.json" 2>/dev/null | head -c 200)"; exit 1
  fi
  mv "$task" "$D/${ts}_${name}.md"
  say "done $name -- $(jq -r '.result // "no result text"' "$out.json" 2>/dev/null | head -c 300)"
  n=$((n + 1))
  if grep -q 'STOP_FOR_OWNER' "$DIR/HANDOFF.md" 2>/dev/null; then say "task $name asked for the owner (STOP_FOR_OWNER) -- stopping"; break; fi
done
say "queue runner end ($n task(s) run). Read $DIR/HANDOFF.md first."
