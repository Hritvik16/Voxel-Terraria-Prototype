#!/bin/bash
# tools/overnight/run-budget-check.sh -- task 230: the milestone budget check (PERF_BUDGET_GUARD.md sec. 3, owner Q-AO (b):
# after every 5 drawing/content tasks, at once after a heavy task, before every merge to main).
#
#   tools/overnight/run-budget-check.sh                              full check (~45-60 min), prints ONE table
#   tools/overnight/run-budget-check.sh --rebaseline "<who approved>" same, then writes the run into PERF_BUDGET.json
#   tools/overnight/run-budget-check.sh --compare DAY.txt NIGHT.txt [FOREST.txt] [--budget F.json] [--rebaseline "<who>"]
#                                                                    comparison only, on two existing cost-split logs
# env: SKIP_BUILD=1 (use Builds/Playground.app as it is), DEV_KERNELS=1 (build with the dev *Diag kernels; default 0 =
#      play's player), BUILD_ARGS="-job-worker-count 1" (passed to the build), SKIPWARM=1 (TESTING ONLY: not warm).
# Ends with BUDGET PASS | WARN | FAIL | NOT CLEAN; exit 1 on FAIL, 3 on NOT CLEAN, 2 on a refused rebaseline or a broken
# launch. It quotes frame time: only the supervisor's check task or a performance task runs it (PREAMBLE rule 10).
#
# What it runs (all through run-cost-split.sh, which it does not duplicate):
#  1. build (BUILD_ONLY=1 ./run-cost-split.sh) unless SKIP_BUILD=1
#  2. warm-up MODE=stream MOTION=walk ./run-tg2world.sh, numbers discarded (PREAMBLE rule 9)
#  3. DAY cost split at noon: walk_near, arms = PERF_BUDGET.json steps' off_arm, F X X F order (-costfixed forest_eye only
#     drops walk_open; -costtimedpaths walk_near leaves forest_eye a still). Then a short FOREST launch, -costarms none:
#     forest_eye FULL timed, secondary (task 230's first run timed every arm there: 60 of the day launch's 73 min).
#  4. NIGHT cost split (-costpaths torch): arms tf_on (play's torch flood) and tl_none (no torch light); the torch scene's
#     FULL is the old point lights (PlaygroundCapture.TorchCost.cs), so night play = tf_on, flood step = tf_on - tl_none.
#     A second launch because -costpaths torch replaces the walk paths (one launch cannot hold both).
#  5. hididle input watch (task 212): a key / mouse / scroll event inside a launch (past its first and last 30 s) makes it
#     dirty; a dirty launch runs again once; still dirty = BUDGET NOT CLEAN (never FAIL). An owner pause, too. The watcher
#     also logs which cost-split path the live report is on, so input during a SECONDARY path (forest_eye: info rows only)
#     is reported but does not make the launch dirty (task 230: the owner used the Mac during the first run's forest_eye).
#  6. tools/overnight/budget_compare.py: the table and the verdict.
# Holds the shared Unity lock for the whole check. Output: Logs/budget/<stamp>/ (check.txt = the table).
set -uo pipefail
cd "$(dirname "$0")/../.."
BUDGET=PERF_BUDGET.json; REBASE=""; CMP_DAY=""; CMP_NIGHT=""; CMP_FOREST=""
while [ $# -gt 0 ]; do
  case "$1" in
    --rebaseline) REBASE="$2"; shift 2;;
    --budget) BUDGET="$2"; shift 2;;
    --compare) CMP_DAY="$2"; CMP_NIGHT="$3"; shift 3
               if [ $# -gt 0 ] && [ "${1#--}" = "$1" ]; then CMP_FOREST="$1"; shift; fi;;
    *) echo "unknown argument $1"; exit 2;;
  esac
done
source tools/play-flags.sh
# The budget settings (PERF_BUDGET.json "settings"): play's flags, High, 432 rows pinned (FSR 3), 60 s shell wait, noon.
FOREST="$(python3 -I -c 'import json,sys; print(json.load(open(sys.argv[1]))["paths"]["forest_eye_pose"])' "$BUDGET")"
ARMS="$(python3 -I -c 'import json,sys; print(",".join(s["off_arm"] for s in json.load(open(sys.argv[1]))["steps"]))' "$BUDGET")"
COMMON="$PLAY_SETTINGS_FLAGS -quality high -renderrows 432 -shellwait 60"
DAYX="$COMMON -startsun 0 -costfixed $FOREST -costarms $ARMS -costtimedpaths walk_near"
FORESTX="$COMMON -startsun 0 -costfixed $FOREST -costarms none"
NIGHTX="$COMMON -costpaths torch -costarms tf_on,tl_none"
SETTINGS="day: [play flags] -quality high -renderrows 432 -shellwait 60 -startsun 0 -costfixed forest_eye -costarms $ARMS -costtimedpaths walk_near; forest: ... -costarms none; night: [play flags] ... -costpaths torch -costarms tf_on,tl_none"

compare() {   # $1 day log, $2 night log, $3 forest log (may be empty), $4 dirty note (may be empty); $5 = 1: a full run (BUDGET_COUNT)
  local a=(--budget "$BUDGET" --day "$1" --night "$2" --settings "$SETTINGS")
  [ -n "$3" ] && a+=(--forest "$3")
  [ "${5:-0}" = "1" ] && a+=(--count-file tools/overnight/BUDGET_COUNT)
  [ -n "$4" ] && a+=(--dirty "$4")
  [ -n "$REBASE" ] && a+=(--rebaseline "$REBASE")
  python3 -I tools/overnight/budget_compare.py "${a[@]}"
}
if [ -n "$CMP_DAY" ]; then compare "$CMP_DAY" "$CMP_NIGHT" "$CMP_FOREST" ""; exit $?; fi

SECONDARY="forest_eye"   # info-only paths: input during them does not make a launch dirty
T0=$(date +%s)
D="Logs/budget/$(date +%Y-%m-%d_%H%M%S)"; mkdir -p "$D"
echo "budget check -> $D  ($(date))"
source tools/unity-lock.sh
unity_lock_acquire
HID=Logs/budget/hididle
[ -x "$HID" ] || cc -O -o "$HID" tools/overnight/hididle.c -framework ApplicationServices || { echo "hididle build failed"; exit 2; }
DATA="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap"
touch "$D/.watch"
# each line: epoch, seconds since the last real input, number of "== path" headers in the live cost-split report
( while true; do
    R=$(find "$DATA/UpscalerClips" -mindepth 1 -maxdepth 1 -type d -newer "$D/.watch" 2>/dev/null | sort | tail -n 1); P=0
    [ -n "$R" ] && [ -f "$R/costsplit.txt" ] && P=$(grep -c '^== ' "$R/costsplit.txt")
    echo "$(date +%s) $("$HID") $P"; sleep 5
  done ) > "$D/idlewatch.txt" 2>&1 &
WPID=$!
trap 'kill $WPID 2>/dev/null; unity_lock_release' EXIT   # keep unity-lock.sh's own EXIT trap (release)

if [ "${SKIP_BUILD:-0}" != "1" ]; then
  echo "build start $(date +%T)"
  DEV_KERNELS="${DEV_KERNELS:-0}" BUILD_ONLY=1 ./run-cost-split.sh > "$D/build.out" 2>&1
  tail -1 "$D/build.out" | grep -q DONE || { echo "BUILD FAILED (see $D/build.out)"; tail -5 "$D/build.out"; exit 2; }
  echo "build end $(date +%T)"
else
  echo "build skipped (SKIP_BUILD=1): $(stat -f '%Sm' Builds/Playground.app)"
fi
if [ "${SKIPWARM:-0}" != "1" ]; then
  echo "warm-up start $(date +%T)"
  MODE=stream MOTION=walk ./run-tg2world.sh > "$D/warmup.out" 2>&1
  echo "warm-up end $(date +%T)  $(grep '^log:' "$D/warmup.out")"
else
  echo "WARNING: SKIPWARM=1 -- not warm steady state; do not rebaseline on this"
fi

# launch <tag> <extra flags>: one cost split; sets LOG (the copied report) and DIRTY (empty = clean)
launch() {
  local tag="$1" x="$2" s e r
  touch "$D/.watch"; s=$(date +%s); echo "$tag start $(date +%T)"
  SKIP_BUILD=1 EXTRA="$x" ./run-cost-split.sh > "$D/launch_$tag.out" 2>&1
  e=$(date +%s); echo "$tag end $(date +%T) ($(( (e - s) / 60 )) min)"
  r=$(sed -n 's/^report: \([^ ]*\) .*/\1/p' "$D/launch_$tag.out")
  LOG="$D/costsplit_$tag.txt"; cp "$r" "$LOG" 2>/dev/null || { echo "no report from $tag:"; tail -5 "$D/launch_$tag.out"; exit 2; }
  # input moments in the window, by the path the report was on (0 = loading); SECONDARY paths do not count
  local names; names=$(grep '^== ' "$LOG" | sed 's/^== \([^:]*\):.*/\1/' | tr '\n' ' ')
  awk -v a=$((s + 30)) -v b=$((e - 30)) -v names="$names" -v sec="$SECONDARY" '
    BEGIN { n = split(names, nm, " ") }
    $2 ~ /^[0-9]+$/ { t = $1 - $2; if (t > a && t < b && !(t in seen)) { seen[t] = 1; p = ($3 > 0 && $3 <= n) ? nm[$3] : "loading"; c[p]++ } }
    END { for (p in c) print p, c[p], (index(" " sec " ", " " p " ") ? "secondary" : "counts") }' "$D/idlewatch.txt" > "$D/input_$tag.txt"
  DIRTY=""
  if [ -s "$D/input_$tag.txt" ]; then
    echo "  input moments by path: $(tr '\n' ';' < "$D/input_$tag.txt")"
    grep -q ' counts$' "$D/input_$tag.txt" && DIRTY="$tag: input during $(grep ' counts$' "$D/input_$tag.txt" | awk '{printf "%s%s (%d)", (NR>1?", ":""), $1, $2}')"
  fi
  grep -q 'NOTE: owner paused during this run' "$LOG" && DIRTY="${DIRTY:+$DIRTY; }$tag: owner paused"
  [ -n "$DIRTY" ] && echo "  dirty: $DIRTY"
  return 0
}
run_clean() {   # <tag> <flags>: one launch, again once if dirty
  launch "$1" "$2"
  if [ -n "$DIRTY" ]; then echo "  $1 dirty -> once more"; launch "$1_rerun" "$2"; fi
}
run_clean day "$DAYX";   DAYLOG="$LOG";   DAYDIRTY="$DIRTY"
launch forest "$FORESTX"; FORESTLOG="$LOG"   # secondary: its input is reported, never makes the check dirty
run_clean night "$NIGHTX"; NIGHTLOG="$LOG"; NIGHTDIRTY="$DIRTY"
NOTE="${DAYDIRTY}${DAYDIRTY:+${NIGHTDIRTY:+; }}${NIGHTDIRTY}"
[ "${SKIPWARM:-0}" = "1" ] && NOTE="${NOTE:+$NOTE; }SKIPWARM=1 (not warm)"
kill $WPID 2>/dev/null
compare "$DAYLOG" "$NIGHTLOG" "$FORESTLOG" "$NOTE" 1 | tee "$D/check.txt"
RC=${PIPESTATUS[0]}
echo "check took $(( ($(date +%s) - T0) / 60 )) min; table: $D/check.txt"
exit $RC
