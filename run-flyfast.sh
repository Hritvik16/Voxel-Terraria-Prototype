#!/bin/bash
# run-flyfast.sh -- item 1 of the 2026-09-25 owner brief: DIAGNOSE the demo's
# stutter and the high-altitude seams before building anything new.
#
#   build   one RELEASE Playground build (compile + shader-error gates)
#   seams   -farseams: flights at 150/300/1000 m, beauty | band | voxel-stop
#           views at the same pose, settled and in flight
#   fly20   -flyfast at 20 m ABSOLUTE, 60 m/s ping-pong across the island,
#           A B B A:  A = far band OFF   B = far band ON + far shadows (the demo)
#   fly300  the same at 300 m
#   stress  B S S B at 20 m: S = B + -farmipstress 5 (every level's whole mip
#           chain rebuilt EVERY frame) -- the isolation arm for the GPU cost of
#           the upkeep, which this workflow cannot time directly
#   hud     A H H A at 20 m: H = A with -nohud. The HUD's fluid panel does a
#           BLOCKING GraphicsBuffer.GetData every 15 OnGUI calls (~7.5 frames)
#   low8    the in-air sustained orbit (-orbitheight 8), A C C A, MINUTES/arm
#   report  tools/report-flyfast.py + tools/report-farband.py
#
#   ./run-flyfast.sh                          everything
#   STAGES="build seams" ./run-flyfast.sh     a subset
#   SECONDS_PER_ARM=150 MINUTES=3 SKIP_BUILD=1 ./run-flyfast.sh
#
# One build; every switch is a command-line flag, so all arms run one binary.
# Takes the shared Unity lock itself.
set -uo pipefail
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Playground.app"
DATA="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap"
MINUTES="${MINUTES:-3}"
SECS="${SECONDS_PER_ARM:-150}"
STAGES="${STAGES:-build seams fly20 fly300 stress low8 report}"
RUN_ID="$(date +%Y%m%d_%H%M%S)"
mkdir -p Logs
LOG="Logs/flyfast_${RUN_ID}.txt"
FLYARMS="Logs/flyfast_${RUN_ID}_arms.tsv"
ORBARMS="Logs/flyfast_${RUN_ID}_orbit_arms.tsv"
: > "$FLYARMS"; : > "$ORBARMS"
has() { [[ " $STAGES " == *" $1 "* ]]; }

source tools/unity-lock.sh
unity_lock_acquire
echo "run $RUN_ID  stages: $STAGES  fly s/arm: $SECS  orbit min/arm: $MINUTES" | tee -a "$LOG"

if has build && [ "${SKIP_BUILD:-0}" != "1" ]; then
  rm -rf "$APP"
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
    -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/flyfast_scene.log
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/flyfast_build.log
  if grep -q "error CS" Logs/flyfast_build.log Logs/flyfast_scene.log; then
    echo "COMPILE ERROR:" | tee -a "$LOG"; grep -h "error CS" Logs/flyfast_build.log Logs/flyfast_scene.log | sort -u | tee -a "$LOG"; exit 1
  fi
  if grep -q "Shader error" Logs/flyfast_build.log; then
    echo "SHADER ERROR:" | tee -a "$LOG"; grep -E "Shader error" Logs/flyfast_build.log | tee -a "$LOG"; exit 1
  fi
  [ -d "$APP" ] || { echo "BUILD FAILED" | tee -a "$LOG"; tail -n 40 Logs/flyfast_build.log; exit 1; }
  echo "build OK (0 error CS, 0 Shader error)" | tee -a "$LOG"
fi

if has seams; then
  echo "" | tee -a "$LOG"; echo "== seams at altitude ==" | tee -a "$LOG"
  caffeinate -dims open -n -W "$APP" --args -farseams -farband 1 -farshadow 1 -cleardeltas
  R=$(ls -td "$DATA/FarBand"/*_seams/ 2>/dev/null | head -n 1)
  if [ -n "$R" ] && [ -f "${R}farseams.txt" ]; then cat "${R}farseams.txt" | tee -a "$LOG"; echo "shots: $R" | tee -a "$LOG"
  else echo "SEAMS RUN PRODUCED NO REPORT (see Player.log)" | tee -a "$LOG"; fi
fi

fly() {  # fly <scenario> <label> <extra args...>
  local scen="$1" label="$2"; shift 2
  local tag="ff_${RUN_ID}_${scen}_${label}"
  sleep 60
  echo "-- $scen arm $label  load: $(uptime | sed 's/.*load averages*: //')" | tee -a "$LOG"
  caffeinate -dims open -n -W "$APP" --args -flyfast -gateres 960 -shadowlevel 0 -seconds "$SECS" \
    -cleardeltas -tag "$tag" "$@"
  local S; S=$(ls -td "$DATA/FlyFast"/*_"$tag"/ 2>/dev/null | head -n 1)
  if [ -n "$S" ]; then
    printf "%s\t%s\t%s\n" "$scen" "$label" "$S" >> "$FLYARMS"
    grep -E "FAR BAND|motion|^frames|FrameTiming|deficit|far ring" "${S}flyfast.txt" | tee -a "$LOG"
  else
    echo "   NO OUTPUT for $tag" | tee -a "$LOG"
  fi
}

fly_scenario() {  # fly_scenario <name> <order> <extra args...>
  local scen="$1" order="$2"; shift 2
  echo "" | tee -a "$LOG"; echo "== $scen: ${SECS} s per arm, $order ==" | tee -a "$LOG"
  for label in $order; do
    case "$label" in
      A) fly "$scen" A -farband 0 "$@" ;;
      B) fly "$scen" B -farband 1 -farshadow 1 "$@" ;;
      S) fly "$scen" S -farband 1 -farshadow 1 -farmipstress 5 "$@" ;;
      H) fly "$scen" H -farband 0 -nohud "$@" ;;
    esac
  done
}

has fly20 && fly_scenario fly20 "A B B A" -flyheight 20
has fly300 && fly_scenario fly300 "A B B A" -flyheight 300
has stress && fly_scenario stress "B S S B" -flyheight 20
has hud && fly_scenario hud "A H H A" -flyheight 20

orbit() {  # orbit <label> <extra args...>
  local label="$1"; shift
  local tag="ffo_${RUN_ID}_low8_${label}"
  sleep 60
  echo "-- low8 arm $label  load: $(uptime | sed 's/.*load averages*: //')" | tee -a "$LOG"
  caffeinate -dims open -n -W "$APP" --args -sustained -motion orbit -gateres 960 -shadowlevel 0 \
    -minutes "$MINUTES" -orbitheight 8 -tag "$tag" "$@"
  local S; S=$(ls -td "$DATA/Sustained"/*_"$tag"/ 2>/dev/null | head -n 1)
  if [ -n "$S" ]; then
    printf "%s\t%s\t%s\n" "low8" "$label" "$S" >> "$ORBARMS"
    grep -E "FAR BAND|FIRST|LAST|WORST|FAR RAYS|SHADOW RAYS" "${S}sustained.txt" | tee -a "$LOG"
  else
    echo "   NO OUTPUT for $tag" | tee -a "$LOG"
  fi
}
if has low8; then
  echo "" | tee -a "$LOG"; echo "== low8 orbit: ${MINUTES} min per arm, A C C A ==" | tee -a "$LOG"
  for label in A C C A; do
    case "$label" in
      A) orbit A -farband 0 ;;
      C) orbit C -farband 1 -farshadow 1 ;;
    esac
  done
fi

if has report; then
  echo "" | tee -a "$LOG"
  [ -s "$FLYARMS" ] && python3 tools/report-flyfast.py "$FLYARMS" | tee -a "$LOG"
  echo "" | tee -a "$LOG"
  [ -s "$ORBARMS" ] && python3 tools/report-farband.py "$ORBARMS" | tee -a "$LOG"
fi
echo "" | tee -a "$LOG"; echo "log: $LOG"
