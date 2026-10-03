#!/bin/bash
# tools/heavy.sh -- give the Mac back without stopping the agent (owner, 2026-10-03, task 14).
#
#   tools/heavy.sh pause          freeze (SIGSTOP) running Unity / il2cpp / bee workers /
#                                 built players, and block new ones (HOLD file)
#   tools/heavy.sh pause --kill   same, but terminate them (TERM, KILL after 10 s) to free RAM
#   tools/heavy.sh resume         unfreeze (SIGCONT) what pause froze, lift the HOLD
#   tools/heavy.sh status         PAUSED/RUNNING, frozen pids, RSS, lock owner
#   tools/heavy.sh overlap START [END]
#                                 for run scripts: warn if a pause overlapped [START, END]
#                                 (epoch s; END defaults to now). Exit 0 none, 1 paused, 3 killed.
#
# HOLD = /tmp/voxel-unity.hold, next to the shared lock, so the sibling repo can honour it.
# While it exists, unity_lock_acquire (tools/unity-lock.sh) waits. The runner and the task
# agent are NOT blocked: only the next Unity launch waits. claude / shell processes are never
# signalled. A frozen process KEEPS its RAM (macOS compresses or swaps it); only --kill frees it.
# Pauses are logged as "pause <epoch> [kill]" / "resume <epoch>" in Logs/heavy-pauses.log.
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

show() {  # pid stat %cpu rss(MB) name
  [ -n "$*" ] || { echo "  (none)"; return; }
  ps -o pid=,stat=,%cpu=,rss=,comm= -p "$(echo "$*" | tr ' ' ',')" 2>/dev/null |
    awk '{ n = $5; for (i = 6; i <= NF; i++) n = n " " $i; sub(".*/", "", n); t += $4
           printf "  pid %-6s stat %-4s cpu %5s%%  rss %6.0f MB  %s\n", $1, $2, $3, $4 / 1024, n }
         END { printf "  total rss %.0f MB\n", t / 1024 }'
}

recorded_pids() { [ -f "$UNITY_HOLD" ] && sed -n 's/^pids //p' "$UNITY_HOLD" | tr '\n' ' '; }

cmd_pause() {
  local kill=0 now pids p
  [ "${1:-}" = "--kill" ] && kill=1
  now=$(date +%s)
  if [ ! -f "$UNITY_HOLD" ]; then
    echo "owner-pause $now $(date '+%Y-%m-%d %H:%M:%S')" > "$UNITY_HOLD"
    echo "pause $now$([ $kill = 1 ] && echo ' kill')" >> "$PLOG"
  elif [ $kill = 1 ]; then
    echo "pause $now kill" >> "$PLOG"   # upgrade: the open pause now also killed the run
  else
    echo "already PAUSED since $(awk 'NR==1{print $3, $4}' "$UNITY_HOLD") -- freezing any new heavy process"
  fi
  [ -f "$UNITY_LOCK" ] && touch "$UNITY_LOCK"
  pids=$(heavy_pids | tr '\n' ' ')
  if [ -z "${pids// /}" ]; then echo "PAUSED: no heavy process running; new Unity launches will wait"; return 0; fi
  if [ $kill = 1 ]; then
    echo "PAUSED --kill: terminating $pids"
    kill -TERM $pids 2>/dev/null; kill -CONT $pids 2>/dev/null   # a stopped process only acts on TERM once continued
    for _ in $(seq 10); do
      [ -z "$(ps -o pid= -p "$(echo $pids | tr ' ' ',')" 2>/dev/null)" ] && break; sleep 1
    done
    left=$(ps -o pid= -p "$(echo $pids | tr ' ' ',')" 2>/dev/null | tr '\n' ' ')
    if [ -n "${left// /}" ]; then echo "SIGKILL after 10 s: $left"; kill -KILL $left 2>/dev/null; sleep 1; fi
    echo "killed $pids" >> "$UNITY_HOLD"
    left=$(ps -o pid= -p "$(echo $pids | tr ' ' ',')" 2>/dev/null | tr '\n' ' ')
    if [ -n "${left// /}" ]; then echo "STILL ALIVE: $left"; return 1; fi
    echo "all terminated; their runs are lost (their run scripts will report KILLED); RAM freed"
  else
    for p in $pids; do kill -STOP "$p" 2>/dev/null; done
    echo "pids $pids" >> "$UNITY_HOLD"
    echo "PAUSED: froze $pids (they keep their RAM; use 'pause --kill' to free it)"
    show $pids
  fi
}

cmd_resume() {
  local pids now
  if [ ! -f "$UNITY_HOLD" ]; then echo "not paused -- nothing to do"; return 0; fi
  pids=$(recorded_pids)
  [ -n "${pids// /}" ] && kill -CONT $pids 2>/dev/null
  now=$(date +%s)
  # The pause kept the lock's mtime old: refresh it so nobody's 2 h stale rule fires on it.
  [ -f "$UNITY_LOCK" ] && touch "$UNITY_LOCK"
  rm -f "$UNITY_HOLD"
  echo "resume $now" >> "$PLOG"
  echo "RESUMED: continued ${pids:-nothing}; Unity launches may proceed"
}

cmd_status() {
  if [ -f "$UNITY_HOLD" ]; then
    echo "PAUSED since $(awk 'NR==1{print $3, $4}' "$UNITY_HOLD") ($(( $(date +%s) - $(awk 'NR==1{print $2}' "$UNITY_HOLD") )) s)"
    echo "recorded (frozen) pids:"; show $(recorded_pids)
    grep '^killed' "$UNITY_HOLD" | sed 's/^/  /'
  else
    echo "RUNNING (no owner pause)"
  fi
  echo "heavy processes now:"; show $(heavy_pids | tr '\n' ' ')
  if [ -f "$UNITY_LOCK" ]; then
    echo "lock: $(cat "$UNITY_LOCK")  (age $(( $(date +%s) - $(stat -f %m "$UNITY_LOCK") )) s)"
  else
    echo "lock: free"
  fi
}

# Seconds of pause inside [START, END]; prints the warning lines. An open pause runs to END.
cmd_overlap() {
  local start=$1 end=${2:-$(date +%s)} out
  [ -f "$PLOG" ] || return 0
  out=$(awk -v s="$start" -v e="$end" '
    # +0 everywhere: macOS awk compares -v values as strings ("200" < "99").
    BEGIN { s += 0; e += 0 }
    function add(a, b) { if (a < s) a = s; if (b > e) b = e; if (b > a) tot += b - a }
    $1 == "pause"  { if (!open) { open = 1; ps = $2 + 0; k = 0 } if ($3 == "kill") k = 1 }
    $1 == "resume" { if (open) { add(ps, $2 + 0); if (k && $2 + 0 > s && ps < e) kill = 1; open = 0 } }
    END { if (open) { add(ps, e); if (k && ps < e) kill = 1 } printf "%d %d", tot, kill }' "$PLOG")
  local secs=${out% *} killed=${out#* }
  if [ "$killed" = 1 ]; then
    echo "WARNING: RUN KILLED BY OWNER PAUSE (heavy.sh pause --kill) -- results are incomplete"
  fi
  if [ "$secs" -gt 0 ] || [ "$killed" = 1 ]; then
    echo "WARNING: PAUSED DURING RUN ($secs s) -- timing numbers from this run are invalid"
  fi
  [ "$killed" = 1 ] && return 3
  [ "$secs" -gt 0 ] && return 1
  return 0
}

case "${1:-status}" in
  pause)   shift; cmd_pause "$@" ;;
  resume)  cmd_resume ;;
  status)  cmd_status ;;
  overlap) shift; cmd_overlap "$@" ;;
  *) sed -n '2,12p' "$0"; exit 2 ;;
esac
