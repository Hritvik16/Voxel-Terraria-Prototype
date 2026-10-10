#!/bin/bash
# tools/heavy.sh -- give the Mac back without stopping the agent (owner, 2026-10-03,
# tasks 14 / 14b). A pause DRAINS: whatever is running finishes, nothing new starts.
#
#   tools/heavy.sh pause     block NEW Unity builds / EditMode runs / player launches
#                            (HOLD file); anything already running is left to finish
#   tools/heavy.sh resume    lift the HOLD; waiting launches proceed (~50 s: 45 s clear)
#   tools/heavy.sh status    PAUSED/RUNNING, what is still finishing (lock owner, its
#                            command, run time), what waits on the pause, drain state
#   tools/heavy.sh overlap START [END]
#                            for run scripts: print a NOTE if a pause overlapped
#                            [START, END] (epoch s; END defaults to now). Exit 0 none, 1 paused.
#
# HOLD = /tmp/voxel-unity.hold, next to the shared lock, so the sibling repo can honour it.
# While it exists, unity_lock_acquire (tools/unity-lock.sh) waits, with no timeout. Nothing
# is ever signalled. Every script stops at its next launch boundary: per-launch callers in
# unity_lock_acquire, lock-once loop scripts in unity_hold_wait before each launch (task 16).
# Pauses are logged as "pause <epoch>" / "resume <epoch>" in Logs/heavy-pauses.log.
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
source "$ROOT/tools/unity-lock.sh"
PLOG="${HEAVY_PAUSE_LOG:-$ROOT/Logs/heavy-pauses.log}"   # override for tests only
mkdir -p "$ROOT/Logs"

# unity_core_pids (shared with unity_busy) + all their descendants (bee_backend, il2cpp,
# shader compilers, ILPP runner) + any orphaned bee_backend. Never a claude or shell process.
heavy_pids() {
  local core
  core=$( { unity_core_pids; pgrep -x bee_backend 2>/dev/null; } | sort -un | tr '\n' ' ')
  [ -n "${core// /}" ] || return 0
  ps -A -o pid=,ppid= | awk -v core="$core" '
    { par[$1] = $2; pids[NR] = $1 }
    END {
      n = split(core, c, " "); for (i = 1; i <= n; i++) keep[c[i]] = 1
      changed = 1
      while (changed) { changed = 0
        for (i in pids) { p = pids[i]; if (!(p in keep) && (par[p] in keep)) { keep[p] = 1; changed = 1 } } }
      for (p in keep) print p
    }' | sort -n | while read -r p; do
      [ "$p" = "$$" ] && continue
      comm=$(ps -o comm= -p "$p" 2>/dev/null) || continue
      case "$(basename "$comm")" in claude*|node|bash|zsh|sh|-zsh|-bash|login|tmux*) continue;; esac
      echo "$p"
    done
}

show() {  # pid %cpu rss(MB) name
  [ -n "$*" ] || { echo "  (none)"; return; }
  ps -o pid=,%cpu=,rss=,comm= -p "$(echo "$*" | tr ' ' ',')" 2>/dev/null |
    awk '{ n = $4; for (i = 5; i <= NF; i++) n = n " " $i; sub(".*/", "", n); t += $3
           printf "  pid %-6s cpu %5s%%  rss %6.0f MB  %s\n", $1, $2, $3 / 1024, n }
         END { printf "  total rss %.0f MB\n", t / 1024 }'
}

dur() { local s=$1; printf '%dh%02dm%02ds' $((s / 3600)) $((s % 3600 / 60)) $((s % 60)); }

cmd_pause() {
  local now
  now=$(date +%s)
  if [ -f "$UNITY_HOLD" ]; then
    echo "already PAUSED since $(awk 'NR==1{print $3, $4}' "$UNITY_HOLD")"
  else
    echo "owner-pause $now $(date '+%Y-%m-%d %H:%M:%S')" > "$UNITY_HOLD"
    echo "pause $now" >> "$PLOG"
    echo "PAUSED: new Unity builds / tests / player launches will wait until 'resume'."
    echo "Anything already running is left to finish -- see 'tools/heavy.sh status'."
  fi
  # Belt and braces: the stale-lock rule is skipped while HOLD exists, and the lock is fresh.
  [ -f "$UNITY_LOCK" ] && touch "$UNITY_LOCK"
  return 0
}

cmd_resume() {
  if [ ! -f "$UNITY_HOLD" ]; then echo "not paused -- nothing to do"; return 0; fi
  # A waiter that already owned the lock (nested acquire) kept it old during the pause:
  # refresh it so nobody's 2 h stale rule fires on it.
  [ -f "$UNITY_LOCK" ] && touch "$UNITY_LOCK"
  rm -f "$UNITY_HOLD"
  echo "resume $(date +%s)" >> "$PLOG"
  echo "RESUMED: waiting launches proceed after the 45 s clear check"
}

cmd_status() {
  local paused=0 pids n
  if [ -f "$UNITY_HOLD" ]; then
    paused=1
    echo "PAUSED since $(awk 'NR==1{print $3, $4}' "$UNITY_HOLD") ($(dur $(( $(date +%s) - $(awk 'NR==1{print $2}' "$UNITY_HOLD") ))))"
  else
    echo "RUNNING (no owner pause)"
  fi
  # Still finishing: the lock owner. Lock line = "<owner> <YYYY-mm-dd HH:MM:SS> [pid <pid>]".
  if [ -f "$UNITY_LOCK" ]; then
    local line owner since start lpid cmd
    line=$(head -n 1 "$UNITY_LOCK")
    owner=$(echo "$line" | cut -d' ' -f1)
    since=$(echo "$line" | cut -d' ' -f2,3)
    start=$(date -j -f '%Y-%m-%d %H:%M:%S' "$since" +%s 2>/dev/null || stat -f %m "$UNITY_LOCK")
    lpid=$(echo "$line" | sed -n 's/.* pid \([0-9]*\).*/\1/p')
    echo "lock owner: $owner, since $since ($(dur $(( $(date +%s) - start ))))"
    if [ -n "$lpid" ]; then
      cmd=$(ps -o command= -p "$lpid" 2>/dev/null)
      echo "  pid $lpid: ${cmd:-(not running -- lock left behind?)}"
    fi
  else
    echo "lock: free"
  fi
  # Waiting on the pause: unity_hold_wait registers each waiter in $UNITY_WAITERS.
  echo "waiting on the pause:"
  local w found=0
  for w in "$UNITY_WAITERS"/*; do
    [ -f "$w" ] || continue
    kill -0 "$(basename "$w")" 2>/dev/null || { rm -f "$w"; continue; }
    echo "  pid $(basename "$w"): $(cat "$w")"; found=1
  done
  [ $found = 1 ] || echo "  (none)"
  pids=$(heavy_pids | tr '\n' ' ')
  n=$(echo $pids | wc -w | tr -d ' ')
  if [ $paused = 1 ]; then
    if [ "$n" -gt 0 ]; then echo "draining: $n heavy processes still running"; show $pids
    else echo "idle -- safe to use the Mac"; fi
  else
    echo "heavy processes now: $n"; [ "$n" -gt 0 ] && show $pids
  fi
  return 0
}

# Seconds of pause inside [START, END]; prints the NOTE. An open pause runs to END.
cmd_overlap() {
  local start=$1 end=${2:-$(date +%s)} secs
  [ -f "$PLOG" ] || return 0
  secs=$(awk -v s="$start" -v e="$end" '
    # +0 everywhere: macOS awk compares -v values as strings ("200" < "99").
    BEGIN { s += 0; e += 0 }
    function add(a, b) { if (a < s) a = s; if (b > e) b = e; if (b > a) tot += b - a }
    $1 == "pause"  { if (!open) { open = 1; ps = $2 + 0 } }
    $1 == "resume" { if (open) { add(ps, $2 + 0); open = 0 } }
    END { if (open) add(ps, e); printf "%d", tot }' "$PLOG")
  if [ "$secs" -gt 0 ]; then
    echo "NOTE: owner paused during this run (Mac likely in use) -- timing numbers may be contaminated; rerun this arm (paused $secs s of the run)"
    return 1
  fi
  return 0
}

case "${1:-status}" in
  pause)   cmd_pause ;;
  resume)  cmd_resume ;;
  status)  cmd_status ;;
  overlap) shift; cmd_overlap "$@" ;;
  *) sed -n '2,12p' "$0"; exit 2 ;;
esac
