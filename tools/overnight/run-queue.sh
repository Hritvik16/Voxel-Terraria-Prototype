#!/bin/bash
# tools/overnight/run-queue.sh -- run queued tasks, EACH IN A FRESH headless
# Claude Code session (owner, 2026-10-01: save plan usage; overnight work).
# ONE task at a time -- never two agents on this project at once.
#
#   tools/overnight/queue/NN-name.md   one task per file, run in name order
#   tools/overnight/PREAMBLE.md        rules every task session gets first
#   tools/overnight/HANDOFF.md         written by each task, read by the next
#   tools/overnight/done/              finished task files (timestamped)
#   tools/overnight/blocked/           tasks that stopped: NAME.md, NAME.HANDOFF.md,
#                                      NAME.patch (uncommitted edits it left)
#   Logs/overnight/                    one JSON result + stderr per task
#
# A task file may declare prerequisites on one line:
#   Requires: 05-taa-shimmer-metric 03-farband-wall-slope
# It runs only once every one of them is in done/. A task whose prerequisite is
# blocked stays queued (it waits for the owner); the runner moves on to the next
# runnable task (owner, 2026-10-02: when one task stops for the owner, start
# another that does not depend on it).
#
# A task is BLOCKED (set aside, queue continues) when it: writes STOP_FOR_OWNER
# at the top of HANDOFF.md; fails (non-zero rc / is_error); or ends without
# writing HANDOFF.md (a -p session that stopped to "wait" -- 2026-10-01).
# Uncommitted edits it made to tracked files are saved as blocked/NAME.patch and
# reverted, so the next task starts from committed code.
#
# Stops when: no runnable task is left; tools/overnight/PAUSE exists (checked
# before every task -- `touch` it to pause, delete it to resume); MAX_TASKS.
# On a usage limit it waits WAIT_MIN minutes and retries, up to MAX_WAITS times.
#
# IDLE_MIN (default 15): before each task, wait until the Mac has had no
# keyboard/mouse input for that long (owner, 2026-10-02: builds must not load
# the machine while it is in use). IDLE_MIN=0 disables the wait.
#
#   tools/overnight/run-queue.sh                 # run everything queued
#   MODEL=sonnet tools/overnight/run-queue.sh    # cheaper model for routine work
#   MAX_TASKS=2 IDLE_MIN=0 tools/overnight/run-queue.sh
#
# Permission mode is `auto`: the session cannot ask anyone, and auto mode blocks
# risky actions instead of prompting. Unity runs still take the shared lock
# (the task's own scripts do that).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)" || exit 1
DIR=tools/overnight; Q=$DIR/queue; D=$DIR/done; B=$DIR/blocked; L=Logs/overnight
mkdir -p "$Q" "$D" "$B" "$L"
MAX_TASKS="${MAX_TASKS:-20}"; WAIT_MIN="${WAIT_MIN:-20}"; MAX_WAITS="${MAX_WAITS:-18}"
IDLE_MIN="${IDLE_MIN:-15}"
CLAUDE_BIN="$(command -v claude)" || { echo "claude CLI not on PATH"; exit 1; }
say() { echo "$(date '+%F %T') $*" | tee -a "$L/runner.log"; }
idle_s() { ioreg -c IOHIDSystem | awk '/HIDIdleTime/ {print int($NF/1000000000); exit}'; }
dirty() { git diff --name-only HEAD -- . ":(exclude)$DIR" | sort; }

# requires_state TASKFILE -> "ok" | "wait" (a prerequisite not done yet) | "blocked:NAME"
requires_state() {
  local r st=ok
  for r in $(sed -n 's/^Requires:[[:space:]]*//p' "$1" | head -n 1); do
    ls "$D"/*_"$r".md >/dev/null 2>&1 && continue
    [ -f "$B/$r.md" ] && { echo "blocked:$r"; return; }
    st=wait
  done
  echo "$st"
}

block() {  # block TASKFILE NAME REASON
  local f
  cp "$DIR/HANDOFF.md" "$B/$2.HANDOFF.md" 2>/dev/null
  f="$(comm -13 <(printf '%s\n' "$pre") <(dirty) | grep -v '^$')"
  if [ -n "$f" ]; then
    # shellcheck disable=SC2086
    git diff HEAD -- $f > "$B/$2.patch"; git checkout HEAD -- $f
    say "  saved and reverted its uncommitted edits: $(echo $f) -> $B/$2.patch"
  fi
  mv "$1" "$B/$2.md"
  say "BLOCKED $2 ($3) -- set aside in $B/; continuing with tasks that do not need it"
}

branch="$(git branch --show-current)"
if [ "$branch" = "main" ]; then say "refusing to run on main -- check out a working branch first"; exit 1; fi
say "queue runner start on branch $branch ($(ls "$Q"/*.md 2>/dev/null | wc -l | tr -d ' ') task(s) queued, IDLE_MIN=$IDLE_MIN)"

n=0
while [ "$n" -lt "$MAX_TASKS" ]; do
  [ -f "$DIR/PAUSE" ] && { say "PAUSE file present -- stopping before the next task"; break; }
  task=""
  for t in $(ls "$Q"/*.md 2>/dev/null | sort); do
    st="$(requires_state "$t")"
    [ "$st" = ok ] && { task="$t"; break; }
    [ "${st%%:*}" = blocked ] && say "skip $(basename "$t" .md) -- needs ${st#blocked:}, which is blocked"
  done
  if [ -z "$task" ]; then
    if ls "$Q"/*.md >/dev/null 2>&1; then say "no runnable task: the rest wait on blocked tasks -- done"
    else say "queue empty -- done"; fi
    break
  fi
  name="$(basename "$task" .md)"

  if [ "$IDLE_MIN" -gt 0 ]; then
    announced=0
    while [ "$(idle_s)" -lt $((IDLE_MIN * 60)) ]; do
      [ "$announced" = 0 ] && { say "waiting for ${IDLE_MIN} min without keyboard/mouse input before $name"; announced=1; }
      sleep 60
      [ -f "$DIR/PAUSE" ] && break
    done
    [ -f "$DIR/PAUSE" ] && continue
  fi

  ts="$(date +%Y%m%d_%H%M%S)"; out="$L/${ts}_${name}"
  prompt="$(cat "$DIR/PREAMBLE.md")

---
## YOUR TASK: $name

$(cat "$task")"
  pre="$(dirty)"
  hmt0="$(stat -f %m "$DIR/HANDOFF.md" 2>/dev/null || echo 0)"
  waits=0; ok=1
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
    ok=0; break
  done
  n=$((n + 1))
  result="$(jq -r '.result // "no result text"' "$out.json" 2>/dev/null | head -c 300)"
  if [ "$ok" = 0 ]; then block "$task" "$name" "FAILED rc=$rc; see $out.json / $out.err"; continue; fi
  # A -p session that stops to "wait for a notification" exits with rc 0 and
  # kills its background jobs (2026-10-01) -- unfinished, not done.
  if [ "$(stat -f %m "$DIR/HANDOFF.md" 2>/dev/null || echo 0)" = "$hmt0" ]; then
    block "$task" "$name" "INCOMPLETE: HANDOFF.md not written. Result: $result"; continue
  fi
  if grep -m1 -v '^[[:space:]]*$' "$DIR/HANDOFF.md" | grep -q 'STOP_FOR_OWNER'; then
    block "$task" "$name" "STOP_FOR_OWNER"; continue
  fi
  mv "$task" "$D/${ts}_${name}.md"
  say "done $name -- $result"
done
say "queue runner end ($n task(s) run; blocked: $(ls "$B" 2>/dev/null | grep -E '^[^.]+\.md$' | sed 's/\.md$//' | tr '\n' ' ')). Read $DIR/HANDOFF.md and $B/."
