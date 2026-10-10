#!/bin/bash
# tools/mem-watchdog.sh -- kill a runaway Unity before it takes the Mac down.
#
# Owner, 2026-10-09: an EditMode run (task 37e's new test) grew Unity to a 22.5 GB
# footprint on this 8 GB Mac, swap reached 14.8 of 15.4 GB, and an earlier attempt
# of the same task forced a hard power-off. This loop watches every Unity /
# il2cpp / built-player process (unity_core_pids, the lock's own patterns) and,
# when ONE process's physical footprint passes MEMWD_LIMIT_MB (default 8192 = all
# of the RAM) or system swap use passes MEMWD_SWAP_MB (default 10240) while Unity
# runs, it:
#   1. SIGTERMs every Unity-family process, then SIGKILLs survivors after 5 s;
#   2. touches tools/overnight/PAUSE so the queue stops before its next task;
#   3. logs to Logs/overnight/watchdog.log (what, footprint, swap).
# The worker sees its Unity run die and reports it; the supervisor sees the log.
#
#   tools/mem-watchdog.sh            run in the foreground
#   tools/mem-watchdog.sh --ensure   start it detached unless one is already alive
# unity_lock_acquire calls --ensure, so every locked Unity run is covered. It
# exits by itself after MEMWD_IDLE_S (default 1800) with no Unity process.
cd "$(dirname "$0")/.." || exit 1
ROOT=$PWD
PIDF=/tmp/voxel-mem-watchdog.pid
LOG="$ROOT/Logs/overnight/watchdog.log"
LIMIT_MB=${MEMWD_LIMIT_MB:-8192}
SWAP_MB=${MEMWD_SWAP_MB:-10240}
POLL_S=${MEMWD_POLL_S:-5}
IDLE_S=${MEMWD_IDLE_S:-1800}

alive() { [ -f "$PIDF" ] && ps -p "$(cat "$PIDF" 2>/dev/null)" >/dev/null 2>&1; }

if [ "${1:-}" = "--ensure" ]; then
  alive && exit 0
  mkdir -p "$(dirname "$LOG")"
  nohup "$0" >> "$LOG" 2>&1 < /dev/null &
  exit 0
fi
alive && { echo "mem-watchdog already running (pid $(cat "$PIDF"))"; exit 0; }
echo $$ > "$PIDF"
trap 'rm -f "$PIDF"' EXIT

# shellcheck source=tools/unity-lock.sh
source "$ROOT/tools/unity-lock.sh"

# Physical footprint of a pid in MB ("Footprint: 22 GB" / "512 MB" / "2416 KB").
footprint_mb() {
  footprint -p "$1" 2>/dev/null | awk '/Footprint:/ {
    for (i=1;i<=NF;i++) if ($i=="Footprint:") { v=$(i+1); u=$(i+2) }
    if (u=="GB") v*=1024; else if (u=="KB") v/=1024; else if (u=="B") v/=1048576;
    printf "%d", v; exit }'
}
swap_used_mb() { sysctl -n vm.swapusage | sed -E 's/.*used = ([0-9.]+)M.*/\1/' | cut -d. -f1; }

say() { echo "$(date '+%Y-%m-%d %H:%M:%S') $*"; }
say "watchdog start (pid $$): limit ${LIMIT_MB} MB per Unity process, swap ${SWAP_MB} MB"
idle=0
while true; do
  pids=$(unity_core_pids)
  if [ -z "$pids" ]; then
    idle=$((idle + POLL_S))
    [ "$idle" -ge "$IDLE_S" ] && { say "no Unity for ${IDLE_S} s -- exiting"; exit 0; }
    sleep "$POLL_S"; continue
  fi
  idle=0
  reason=""
  for p in $pids; do
    mb=$(footprint_mb "$p"); mb=${mb:-0}
    if [ "$mb" -ge "$LIMIT_MB" ]; then
      reason="pid $p footprint ${mb} MB >= ${LIMIT_MB} MB ($(ps -o command= -p "$p" 2>/dev/null | cut -c1-140))"; break
    fi
  done
  sw=$(swap_used_mb); sw=${sw:-0}
  [ -z "$reason" ] && [ "$sw" -ge "$SWAP_MB" ] && reason="swap used ${sw} MB >= ${SWAP_MB} MB while Unity runs"
  if [ -n "$reason" ]; then
    say "KILL: $reason"
    touch "$ROOT/tools/overnight/PAUSE"
    for p in $pids; do kill "$p" 2>/dev/null; done
    sleep 5
    for p in $(unity_core_pids); do kill -9 "$p" 2>/dev/null; done
    say "killed Unity ($pids); queue PAUSEd (tools/overnight/PAUSE); swap now $(swap_used_mb) MB"
  fi
  sleep "$POLL_S"
done
