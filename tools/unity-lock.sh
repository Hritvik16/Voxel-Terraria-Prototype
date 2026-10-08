# tools/unity-lock.sh -- SOURCE this; do not run it.
#
# ONE Unity build/test/player at a time on this 8 GB Mac, shared with the
# sibling ../VoxelTerraria_compare session, which agreed (2026-09-24) to honour
# the same lock file:
#   - wait while /tmp/voxel-unity.lock exists (unless an outer script of this
#     process tree holds it -- task 33a -- or it is > 2 h old)
#   - then wait until no Unity / il2cpp / built player has run for 45 s
#   - write "<owner> <start time>" into it; remove it on exit
#
# Process checks match executable NAMES (pgrep -x) and player paths from inside
# this sourced file, so no caller's own command line can match itself -- an
# inline `pgrep -f '<pattern>'` in a shell command matches that very shell,
# which deadlocked a queue here twice.
#
# OWNER PAUSE (2026-10-03, tasks 14/14b): while /tmp/voxel-unity.hold exists
# (tools/heavy.sh pause), unity_lock_acquire waits -- no timeout -- and never
# takes the lock or clears a "stale" one. Nothing running is signalled: the
# current holder finishes ("drain"). Each waiter registers in UNITY_WAITERS so
# `tools/heavy.sh status` can list it; heavy.sh counts what is still draining
# with unity_core_pids below -- the same patterns unity_busy waits on.
UNITY_LOCK=/tmp/voxel-unity.lock
UNITY_HOLD=/tmp/voxel-unity.hold
UNITY_WAITERS=/tmp/voxel-unity.waiters
UNITY_LOCK_ME="voxel-terraria-1-byte"
# Test knobs (tools/test-unity-lock.sh reassigns them after sourcing).
UNITY_LOCK_CLEAR_S=45   # seconds with no Unity process before taking the lock
UNITY_LOCK_POLL_S=5     # poll step of every wait loop
UNITY_LOCK_SAY_S=60     # a waiting line repeats about this often

# Pids of Unity (editor/batchmode), il2cpp and any built player (Builds/*.app).
unity_core_pids() {
  { pgrep -x Unity; pgrep -x il2cpp; pgrep -f "/Builds/[^/]*\.app/Contents/MacOS/"; } 2>/dev/null | sort -un
}

unity_busy() {
  [ -n "$(unity_core_pids)" ]
}

# "pid N: <command>" of the first running Unity process, for the waiting lines.
unity_busy_what() {
  local p
  p=$(unity_core_pids | head -n 1)
  [ -n "$p" ] && echo "pid $p: $(ps -o command= -p "$p" 2>/dev/null | cut -c1-100)"
}

# Pid from a lock line "<owner> <YYYY-mm-dd HH:MM:SS> [pid <pid>]" (empty if none).
unity_lock_pid() {
  head -n 1 "$UNITY_LOCK" 2>/dev/null | sed -n 's/.* pid \([0-9]*\).*/\1/p'
}

# "<owner> since HH:MM:SS (pid N: <command>)" -- who holds the lock, what it runs.
unity_lock_who() {
  local line lpid cmd
  line=$(head -n 1 "$UNITY_LOCK" 2>/dev/null)
  lpid=$(echo "$line" | sed -n 's/.* pid \([0-9]*\).*/\1/p')
  printf '%s since %s' "$(echo "$line" | cut -d' ' -f1)" "$(echo "$line" | cut -d' ' -f3)"
  if [ -n "$lpid" ]; then
    cmd=$(ps -o command= -p "$lpid" 2>/dev/null | sed 's#^/bin/bash ##' | cut -c1-120)
    printf ' (pid %s: %s)' "$lpid" "${cmd:-not running}"
  fi
}

# True if pid $1 is this shell or one of its ancestors (an outer script of ours).
unity_pid_is_ancestor() {
  local p=$$ n=0
  while [ -n "$p" ] && [ "$p" -gt 1 ] && [ $n -lt 64 ]; do
    [ "$p" = "$1" ] && return 0
    p=$(ps -o ppid= -p "$p" 2>/dev/null | tr -d ' '); n=$((n + 1))
  done
  return 1
}

# Wait while the owner's pause (HOLD) is set; say so at once, then about once a minute.
unity_hold_wait() {
  [ -f "$UNITY_HOLD" ] || return 0
  echo "$(date +%H:%M:%S) waiting for the Unity lock: owner pause is on (tools/heavy.sh resume lifts it) -- your command starts after that"
  mkdir -p "$UNITY_WAITERS" 2>/dev/null
  echo "$(date '+%H:%M:%S') $(ps -o command= -p $$ 2>/dev/null | cut -c1-160)" > "$UNITY_WAITERS/$$" 2>/dev/null
  local said=$SECONDS
  while [ -f "$UNITY_HOLD" ]; do
    sleep "$UNITY_LOCK_POLL_S"
    if [ $((SECONDS - said)) -ge "$UNITY_LOCK_SAY_S" ] && [ -f "$UNITY_HOLD" ]; then
      echo "$(date +%H:%M:%S) still waiting: owner pause is on"; said=$SECONDS
    fi
  done
  rm -f "$UNITY_WAITERS/$$"
  echo "$(date +%H:%M:%S) owner pause lifted"
}

unity_lock_release() {
  if [ "${UNITY_LOCK_OWNED:-0}" = "1" ]; then rm -f "$UNITY_LOCK"; UNITY_LOCK_OWNED=0; fi
}

# Takes the lock, or returns at once when an outer script of THIS process tree
# already holds it (task 33a). The owner name alone does not prove that: queue
# tasks and the owner's ./run-play.sh share UNITY_LOCK_ME, and the old name-only
# shortcut let run-play.sh start a player, silently, while a task held the lock.
# "Ours" = the lock's pid is an ancestor of $$, or is the exported
# UNITY_LOCK_HELD_BY (a detached child keeps it) and still alive.
unity_lock_acquire() {
  local said=-1 last_who=""
  while true; do
    unity_hold_wait
    if [ -f "$UNITY_LOCK" ]; then
      local line owner age lpid who
      line=$(head -n 1 "$UNITY_LOCK" 2>/dev/null)
      owner=$(echo "$line" | cut -d' ' -f1)
      lpid=$(unity_lock_pid)
      age=$(( $(date +%s) - $(stat -f %m "$UNITY_LOCK" 2>/dev/null || date +%s) ))
      if [ "$owner" = "$UNITY_LOCK_ME" ] && [ -n "$lpid" ]; then
        if unity_pid_is_ancestor "$lpid" || { [ "${UNITY_LOCK_HELD_BY:-}" = "$lpid" ] && ps -p "$lpid" >/dev/null 2>&1; }; then
          # Held by an outer script of ours. Still never overlap with a straggler.
          local s2=-1
          while unity_busy || [ -f "$UNITY_HOLD" ]; do
            unity_hold_wait
            if unity_busy; then
              if [ $s2 -lt 0 ] || [ $((SECONDS - s2)) -ge "$UNITY_LOCK_SAY_S" ]; then
                echo "$(date +%H:%M:%S) lock is ours (pid $lpid); waiting for a Unity process to exit ($(unity_busy_what))"; s2=$SECONDS
              fi
              sleep "$UNITY_LOCK_POLL_S"
            fi
          done
          return 0
        fi
        # Ours by name, but its pid is gone: a killed holder left it behind.
        # (Re-read first so a lock taken again meanwhile is not removed.)
        if ! ps -p "$lpid" >/dev/null 2>&1 && [ "$(head -n 1 "$UNITY_LOCK" 2>/dev/null)" = "$line" ]; then
          echo "$(date +%H:%M:%S) removing a lock left behind by dead pid $lpid ($line)"; rm -f "$UNITY_LOCK"; continue
        fi
      fi
      # Never "stale" while paused: the pause, not the holder, kept it old.
      if [ "$age" -gt 7200 ] && [ ! -f "$UNITY_HOLD" ]; then echo "removing stale lock ($owner, ${age}s)"; rm -f "$UNITY_LOCK"; continue; fi
      who=$(unity_lock_who)
      if [ $said -lt 0 ] || [ "$line" != "$last_who" ]; then
        echo "$(date +%H:%M:%S) waiting for the Unity lock: held by $who -- your command starts when it is free"
        said=$SECONDS; last_who=$line
      elif [ $((SECONDS - said)) -ge "$UNITY_LOCK_SAY_S" ]; then
        echo "$(date +%H:%M:%S) still waiting for the Unity lock: held by $who"; said=$SECONDS
      fi
      sleep "$UNITY_LOCK_POLL_S"; continue
    fi
    local clear=0 cs=-1
    while [ $clear -lt "$UNITY_LOCK_CLEAR_S" ]; do
      # Taken or paused meanwhile: back to the outer loop, which says by whom.
      if [ -f "$UNITY_LOCK" ] || [ -f "$UNITY_HOLD" ]; then continue 2; fi
      if unity_busy; then
        clear=0
        if [ $cs -lt 0 ] || [ $((SECONDS - cs)) -ge "$UNITY_LOCK_SAY_S" ]; then
          echo "$(date +%H:%M:%S) lock is free; waiting for the Mac to be clear of Unity for ${UNITY_LOCK_CLEAR_S} s (running: $(unity_busy_what))"; cs=$SECONDS
        fi
      else
        [ $cs -lt 0 ] && { echo "$(date +%H:%M:%S) lock is free; waiting for the Mac to be clear of Unity for ${UNITY_LOCK_CLEAR_S} s"; cs=$SECONDS; }
        clear=$((clear + UNITY_LOCK_POLL_S))
      fi
      sleep "$UNITY_LOCK_POLL_S"
    done
    [ -f "$UNITY_HOLD" ] && continue
    # Atomic take (noclobber = O_EXCL): if the sibling took it in the gap
    # since the check above, the write fails and we go back to waiting.
    if ! ( set -o noclobber; echo "$UNITY_LOCK_ME $(date '+%Y-%m-%d %H:%M:%S') pid $$" > "$UNITY_LOCK" ) 2>/dev/null; then
      continue
    fi
    UNITY_LOCK_OWNED=1
    export UNITY_LOCK_HELD_BY=$$
    trap unity_lock_release EXIT
    return 0
  done
}
