#!/bin/bash
# run-looksweep.sh -- owner decisions 2-4: the cinematic grade over a whole day.
#   1. -looksweep : 3 poses x 11 times (dawn -> noon -> dusk -> midnight), each
#                   shot A (lookGrade 0) and B (lookGrade 1) from the same frame
#                   state; below-ramp pairs are pixel-diffed (must be identical).
#                   Files sort pose -> time -> before/after: select-all in Preview.
#   2. GRADE_AB=1 : the grade's frame cost, -sustained at NOON (-tod 12; at the
#                   default golden-hour pin the grade is inert), orbit, gate 960,
#                   rung 0, live fluid, FULLSCREEN, MINUTES each (default 3),
#                   counterbalanced 1, 0, 0, 1.
#
#   ./run-looksweep.sh                         build + sweep
#   SKIP_BUILD=1 GRADE_AB=1 ./run-looksweep.sh sweep + frame-cost arms
#   SKIP_BUILD=1 SKIP_SWEEP=1 GRADE_AB=1 ./run-looksweep.sh   frame-cost arms only
set -uo pipefail
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Playground.app"
DATA="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap"
MINUTES="${MINUTES:-3}"
mkdir -p Logs
LOG="Logs/looksweep_$(date +%Y%m%d_%H%M%S).txt"

source tools/unity-lock.sh
unity_lock_acquire
if [ "${SKIP_BUILD:-0}" != "1" ]; then
  rm -rf "$APP"
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
    -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/looksweep_scene.log
  unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/looksweep_build.log
  if grep -q "Shader error" Logs/looksweep_build.log; then
    echo "SHADER ERROR:" | tee -a "$LOG"; grep -E "Shader error" Logs/looksweep_build.log | tee -a "$LOG"; exit 1
  fi
  [ -d "$APP" ] || { echo "BUILD FAILED" | tee -a "$LOG"; tail -n 40 Logs/looksweep_build.log; exit 1; }
fi

if [ "${SKIP_SWEEP:-0}" != "1" ]; then
  echo "== 1. day sweep, before/after ==" | tee -a "$LOG"
  unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
  caffeinate -dims open -n -W "$APP" --args -looksweep -cleardeltas
  R=$(ls -td "$DATA/LookSweep"/*/ | head -n 1); cat "${R}looksweep.txt" | tee -a "$LOG"; echo "shots: $R" | tee -a "$LOG"
fi

if [ "${GRADE_AB:-0}" = "1" ]; then
  echo "" | tee -a "$LOG"
  echo "== 2. grade frame cost at noon, ${MINUTES} min per arm, counterbalanced ==" | tee -a "$LOG"
  for arm in 1 0 0 1; do
    unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
    sleep 60
    echo "-- arm lookgrade=$arm  load: $(uptime | sed 's/.*load averages*: //')" | tee -a "$LOG"
    caffeinate -dims open -n -W "$APP" --args -sustained -motion orbit -gateres 960 -shadowlevel 0 \
      -minutes "$MINUTES" -tod 12 -tag "grade$arm" -lookgrade "$arm" -cleardeltas
    S=$(ls -td "$DATA/Sustained"/*_grade$arm/ | head -n 1)
    grep -E "look grade|clock PINNED|^ +[0-9]|FIRST|LAST|WORST" "${S}sustained.txt" | tee -a "$LOG"
  done
fi
echo "" | tee -a "$LOG"; echo "log: $LOG"
