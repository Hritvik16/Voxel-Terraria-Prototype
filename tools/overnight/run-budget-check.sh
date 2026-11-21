#!/bin/bash
# tools/overnight/run-budget-check.sh -- task 230: the milestone budget check (PERF_BUDGET_GUARD.md sec. 3, owner Q-AO (b):
# after every 5 drawing/content tasks, at once after a heavy task, before every merge to main).
#
# WORST-CASE POSES DECIDE THE VERDICT (task 235, owner 2026-11-17, "I want 1 for now"): a low sun and torches both occur in
# play, so a gain that only shows at noon / with no torch near is not a gain for the 30 fps target. The check therefore times
# walk_near and forest_eye at a 16 deg morning sun, jungle_eye at noon (GPU pool full) and torch_camp at night with torches,
# and BUDGET PASS | WARN | FAIL is decided by those rows; the noon rows (startsun_off arms) are a control, info only.
#
#   tools/overnight/run-budget-check.sh                              full check (~1 h 45 min), prints ONE table
#   tools/overnight/run-budget-check.sh --rebaseline "<who approved>" same, then writes the run into PERF_BUDGET.json
#   tools/overnight/run-budget-check.sh --compare walk=W.txt forest=F.txt jungle=J.txt night=N.txt [--budget F.json]
#                                         [--rebaseline "<who>"]     comparison only, on existing cost-split logs
# env: SKIP_BUILD=1 (use Builds/Playground.app as it is), DEV_KERNELS=1 (build with the dev *Diag kernels; default 0 =
#      play's player), BUILD_ARGS="-job-worker-count 1" (passed to the build), SKIPWARM=1 (TESTING ONLY: not warm).
# Ends with BUDGET PASS | WARN | FAIL | NOT CLEAN; exit 1 on FAIL, 3 on NOT CLEAN, 2 on a refused rebaseline or a broken
# launch. It quotes frame time: only the supervisor's check task or a performance task runs it (PREAMBLE rule 10).
#
# What it runs (all through run-cost-split.sh, which it does not duplicate):
#  1. build (BUILD_ONLY=1 ./run-cost-split.sh) unless SKIP_BUILD=1
#  2. warm-up MODE=stream MOTION=walk ./run-tg2world.sh, numbers discarded (PREAMBLE rule 9)
#  3. four cost-split launches back to back, one per PERF_BUDGET.json "launches" entry (flags + -costarms; each arm is
#     timed X X F after one F, i.e. interleaved): walk (walk_near, 16 deg; noon control = startsun_off), forest (forest_eye,
#     16 deg; noon control = startsun_off), jungle (jungle_eye, noon), night (-costpaths torch: tf_on = play's flood,
#     tl_none = no torch light; the scene's FULL is the old point lights). forest_eye / jungle_eye keep a streaming
#     deficit, so each run there waits NearSettle's 180 s cap (~3.7 min a run): those launches time only the arms the
#     table needs. Day flood is not timed: since 229 a FULL line `flood skip` = 0 ms by construction.
#  4. hididle input watch (task 212): a key / mouse / scroll event inside a launch (past its first and last 30 s) WHILE the
#     live report is on one of that launch's TIMED paths makes it dirty; a dirty launch runs again once; still dirty =
#     BUDGET NOT CLEAN (never FAIL). An owner pause, too. Input during a still-only path or loading is reported only.
#  5. tools/overnight/budget_compare.py: the pose x step table, the worst pose per step and the verdict.
# Holds the shared Unity lock for the whole check. Output: Logs/budget/<stamp>/ (check.txt = the table).
set -uo pipefail
cd "$(dirname "$0")/../.."
BUDGET=PERF_BUDGET.json; REBASE=""; CMP=()
while [ $# -gt 0 ]; do
  case "$1" in
    --rebaseline) REBASE="$2"; shift 2;;
    --budget) BUDGET="$2"; shift 2;;
    --compare) shift; while [ $# -gt 0 ] && [ "${1#--}" = "$1" ]; do CMP+=(--log "$1"); shift; done;;
    *) echo "unknown argument $1"; exit 2;;
  esac
done
source tools/play-flags.sh
# The budget settings (PERF_BUDGET.json "settings"): play's flags, High, 432 rows pinned (FSR 3), 60 s shell wait; then
# each launch's own flags and arms (PERF_BUDGET.json "launches"; {name} = a pinned pose from "paths").
COMMON="$PLAY_SETTINGS_FLAGS -quality high -renderrows 432 -shellwait 60"
LAUNCHES="$(python3 -I -c '
import json, sys
b = json.load(open(sys.argv[1])); P = b["paths"]
for n, L in b["launches"].items():
    print("\t".join([n, L["flags"].format(**P) + " -costarms " + ",".join(L["arms"]), " ".join(L["timed"])]))' "$BUDGET")" \
  || { echo "cannot read launches from $BUDGET"; exit 2; }
SETTINGS="[play flags] -quality high -renderrows 432 -shellwait 60; $(echo "$LAUNCHES" | awk -F'\t' '{printf "%s%s: %s", (NR>1?" | ":""), $1, $2}' | sed 's/[0-9.,]*|[0-9.,]*|[0-9.,]*//g')"

compare() {   # $1 dirty note (may be empty); $2 = 1: a full run (BUDGET_COUNT); then --log NAME=PATH ...
  local dirty="$1" full="$2"; shift 2
  local a=(--budget "$BUDGET" --settings "$SETTINGS" "$@")
  [ "$full" = "1" ] && a+=(--count-file tools/overnight/BUDGET_COUNT)
  [ -n "$dirty" ] && a+=(--dirty "$dirty")
  [ -n "$REBASE" ] && a+=(--rebaseline "$REBASE")
  python3 -I tools/overnight/budget_compare.py "${a[@]}"
}
if [ -n "${CMP[*]:-}" ]; then compare "" 0 "${CMP[@]}"; exit $?; fi

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

# launch <tag> <extra flags> <timed paths>: one cost split; sets LOG (the copied report) and DIRTY (empty = clean)
launch() {
  local tag="$1" x="$2" timed="$3" s e r
  touch "$D/.watch"; s=$(date +%s); echo "$tag start $(date +%T)"
  SKIP_BUILD=1 EXTRA="$x" ./run-cost-split.sh > "$D/launch_$tag.out" 2>&1 < /dev/null
  e=$(date +%s); echo "$tag end $(date +%T) ($(( (e - s) / 60 )) min)"
  r=$(sed -n 's/^report: \([^ ]*\) .*/\1/p' "$D/launch_$tag.out")
  LOG="$D/costsplit_$tag.txt"; cp "$r" "$LOG" 2>/dev/null || { echo "no report from $tag:"; tail -5 "$D/launch_$tag.out"; exit 2; }
  # input moments in the window, by the path the report was on (0 = loading); only this launch's TIMED paths count
  local names; names=$(grep '^== ' "$LOG" | sed 's/^== \([^:]*\):.*/\1/' | tr '\n' ' ')
  awk -v a=$((s + 30)) -v b=$((e - 30)) -v names="$names" -v tim="$timed" '
    BEGIN { n = split(names, nm, " ") }
    $2 ~ /^[0-9]+$/ { t = $1 - $2; if (t > a && t < b && !(t in seen)) { seen[t] = 1; p = ($3 > 0 && $3 <= n) ? nm[$3] : "loading"; c[p]++ } }
    END { for (p in c) print p, c[p], (index(" " tim " ", " " p " ") ? "counts" : "untimed") }' "$D/idlewatch.txt" > "$D/input_$tag.txt"
  DIRTY=""
  if [ -s "$D/input_$tag.txt" ]; then
    echo "  input moments by path: $(tr '\n' ';' < "$D/input_$tag.txt")"
    grep -q ' counts$' "$D/input_$tag.txt" && DIRTY="$tag: input during $(grep ' counts$' "$D/input_$tag.txt" | awk '{printf "%s%s (%d)", (NR>1?", ":""), $1, $2}')"
  fi
  grep -q 'NOTE: owner paused during this run' "$LOG" && DIRTY="${DIRTY:+$DIRTY; }$tag: owner paused"
  [ -n "$DIRTY" ] && echo "  dirty: $DIRTY"
  return 0
}
run_clean() {   # <tag> <flags> <timed paths>: one launch, again once if dirty
  launch "$1" "$2" "$3"
  if [ -n "$DIRTY" ]; then echo "  $1 dirty -> once more"; launch "$1_rerun" "$2" "$3"; fi
}
LOGARGS=(); NOTE=""
while IFS=$'\t' read -r -u 3 name flags timed; do
  run_clean "$name" "$COMMON $flags" "$timed"
  LOGARGS+=(--log "$name=$LOG")
  [ -n "$DIRTY" ] && NOTE="${NOTE:+$NOTE; }$DIRTY"
done 3<<< "$LAUNCHES"
[ "${SKIPWARM:-0}" = "1" ] && NOTE="${NOTE:+$NOTE; }SKIPWARM=1 (not warm)"
kill $WPID 2>/dev/null; wait $WPID 2>/dev/null   # no "Terminated" line in the output
compare "$NOTE" 1 "${LOGARGS[@]}" | tee "$D/check.txt"
RC=${PIPESTATUS[0]}
echo "check took $(( ($(date +%s) - T0) / 60 )) min; table: $D/check.txt"
exit $RC
