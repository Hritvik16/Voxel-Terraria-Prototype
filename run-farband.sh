#!/bin/bash
# run-farband.sh -- DESIGN_LARGE_WORLD_AND_TERRAIN_OVERHAUL.md Step 1: the far band.
#
#   build   one RELEASE Playground build (shader-error gate on the build log)
#   visual  -farvisual: same frame OFF vs ON at 8 poses x 4 times of day, band
#           and step-heat views, camera-in-air check (Logs + FarBand/<ts>/)
#   ground  -sustained orbit at the arena, golden hour, fullscreen, MINUTES per
#           arm, counterbalanced A B C C B A:
#             A = far band OFF (shipped)   B = far band ON   C = ON + far shadows
#   preview -farpreview: the Step 2 generator's sizeClass 2 island drawn by the
#           far band alone (-farsource tg2) -- a look, not a measurement
#   high    the same three arms orbiting 300 m up, pitched 15 deg down: the
#           pose where most pixels are far pixels (§7 item 1's altitude pose)
#   map     the same three arms orbiting 1 km up, pitched 70 deg down (map view)
#   report  tools/report-farband.py over this run's arms
#
#   ./run-farband.sh                          everything
#   STAGES="build visual" ./run-farband.sh    a subset
#   MINUTES=5 SKIP_BUILD=1 ./run-farband.sh
#
# One build, every switch a command-line flag: all arms run the same binary.
set -uo pipefail
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Playground.app"
DATA="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap"
MINUTES="${MINUTES:-3}"
STAGES="${STAGES:-build visual preview ground high report}"
RUN_ID="$(date +%Y%m%d_%H%M%S)"
mkdir -p Logs
LOG="Logs/farband_${RUN_ID}.txt"
ARMLIST="Logs/farband_${RUN_ID}_arms.tsv"
: > "$ARMLIST"
has() { [[ " $STAGES " == *" $1 "* ]]; }

# One Unity process at a time, shared lock with the comparison repo.
source tools/unity-lock.sh
unity_lock_acquire
echo "run $RUN_ID  stages: $STAGES  minutes/arm: $MINUTES" | tee -a "$LOG"

if has build && [ "${SKIP_BUILD:-0}" != "1" ]; then
  rm -rf "$APP"
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
    -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/farband_scene.log
  unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/farband_build.log
  if grep -q "error CS" Logs/farband_build.log Logs/farband_scene.log; then
    echo "COMPILE ERROR:" | tee -a "$LOG"; grep -h "error CS" Logs/farband_build.log Logs/farband_scene.log | sort -u | tee -a "$LOG"; exit 1
  fi
  if grep -q "Shader error" Logs/farband_build.log; then
    echo "SHADER ERROR:" | tee -a "$LOG"; grep -E "Shader error" Logs/farband_build.log | tee -a "$LOG"; exit 1
  fi
  [ -d "$APP" ] || { echo "BUILD FAILED" | tee -a "$LOG"; tail -n 40 Logs/farband_build.log; exit 1; }
  echo "build OK (0 error CS, 0 Shader error)" | tee -a "$LOG"
fi

if has visual; then
  echo "" | tee -a "$LOG"; echo "== visual A/B ==" | tee -a "$LOG"
  unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
  caffeinate -dims open -n -W "$APP" --args -farvisual -farband 1 -farshadow 1 -cleardeltas
  R=$(ls -td "$DATA/FarBand"/*/ 2>/dev/null | head -n 1)
  if [ -n "$R" ] && [ -f "${R}farvisual.txt" ]; then cat "${R}farvisual.txt" | tee -a "$LOG"; echo "shots: $R" | tee -a "$LOG"
  else echo "VISUAL RUN PRODUCED NO REPORT (see Player.log)" | tee -a "$LOG"; fi
fi

if has preview; then
  echo "" | tee -a "$LOG"; echo "== sizeClass 2 preview (TerrainGen through the far band) ==" | tee -a "$LOG"
  unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
  caffeinate -dims open -n -W "$APP" --args -farpreview -farband 1 -farsource tg2 -farshadow 1 -cleardeltas
  R=$(ls -td "$DATA/FarBand"/*_preview/ 2>/dev/null | head -n 1)
  if [ -n "$R" ] && [ -f "${R}farpreview.txt" ]; then cat "${R}farpreview.txt" | tee -a "$LOG"; echo "shots: $R" | tee -a "$LOG"
  else echo "PREVIEW RUN PRODUCED NO REPORT (see Player.log)" | tee -a "$LOG"; fi
fi

arm() {  # arm <scenario> <label> <extra args...>
  local scen="$1" label="$2"; shift 2
  local tag="fb_${RUN_ID}_${scen}_${label}"
  unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
  sleep 60   # let the machine settle between launches
  echo "-- $scen arm $label  load: $(uptime | sed 's/.*load averages*: //')" | tee -a "$LOG"
  caffeinate -dims open -n -W "$APP" --args -sustained -motion orbit -gateres 960 -shadowlevel 0 \
    -minutes "$MINUTES" -tag "$tag" "$@"
  local S; S=$(ls -td "$DATA/Sustained"/*_"$tag"/ 2>/dev/null | head -n 1)
  if [ -n "$S" ]; then
    printf "%s\t%s\t%s\n" "$scen" "$label" "$S" >> "$ARMLIST"
    grep -E "FAR BAND|clock PINNED|FIRST|LAST|WORST|FAR RAYS|SHADOW RAYS" "${S}sustained.txt" | tee -a "$LOG"
  else
    echo "   NO OUTPUT for $tag" | tee -a "$LOG"
  fi
}

run_scenario() {  # run_scenario <name> <extra args...>
  local scen="$1"; shift
  echo "" | tee -a "$LOG"; echo "== $scen: ${MINUTES} min per arm, A B C C B A ==" | tee -a "$LOG"
  for label in A B C C B A; do
    case "$label" in
      A) arm "$scen" A -farband 0 "$@" ;;
      B) arm "$scen" B -farband 1 -farshadow 0 "$@" ;;
      C) arm "$scen" C -farband 1 -farshadow 1 "$@" ;;
    esac
  done
}

has ground && run_scenario ground
# low8: the ground orbit lifted 8 m (13.7 m, above the tallest terrain, 12.8 m).
# The plain ground orbit skims the terrain at 5.7 m and spends part of every
# arm inside rock (black frame, every ray hits at step 1) -- see the report.
has low8 && run_scenario low8 -orbitheight 8
has high && run_scenario high -orbitheight 300 -orbitpitch 15
has map && run_scenario map -orbitheight 1000 -orbitpitch 70

if has report && [ -s "$ARMLIST" ]; then
  echo "" | tee -a "$LOG"
  python3 tools/report-farband.py "$ARMLIST" | tee -a "$LOG"
fi
echo "" | tee -a "$LOG"; echo "log: $LOG"
