#!/bin/bash
# run-look-review.sh -- task 114: the visual look review (PlaygroundCapture.LookReview.cs).
# One launch per -quality preset (shells are sized at start-up only), each:
# walk_near / walk_open / ridge / high at noon x {fsr3, none, native} + shell tint,
# motion bursts at walk_open and ridge, golden hour (High only). Play's look flags
# (PlaySettings defaults: far shading, see-through liquids, river look, shells).
# A LOOK, not a measurement: no frame times. Then tools/look-review-diff.py.
#
#   ./run-look-review.sh                        build + low, medium, high
#   SKIP_BUILD=1 PRESETS="low high" ./run-look-review.sh
set -uo pipefail
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Playground.app"
DATA="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap"
PRESETS="${PRESETS:-low medium high}"
mkdir -p Logs
LOG="Logs/lookreview_$(date +%Y%m%d_%H%M%S).txt"
source tools/unity-lock.sh
unity_lock_acquire
if [ "${SKIP_BUILD:-0}" != "1" ]; then
  rm -rf "$APP"
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
    -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/lookreview_scene.log
  unity_hold_wait
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/lookreview_build.log
  if grep -q "Shader error\|error CS" Logs/lookreview_build.log Logs/lookreview_scene.log; then
    echo "BUILD ERROR:" | tee -a "$LOG"; grep -hE "Shader error|error CS" Logs/lookreview_build.log Logs/lookreview_scene.log | sort -u | head | tee -a "$LOG"; exit 1
  fi
  [ -d "$APP" ] || { echo "BUILD FAILED" | tee -a "$LOG"; exit 1; }
  echo "build OK" | tee -a "$LOG"
fi
for q in $PRESETS; do
  unity_hold_wait
  echo "== quality $q  $(date +%T)" | tee -a "$LOG"
  caffeinate -dims open -n -W "$APP" --args -world tg2 -upscalerclips -lookreview -quality "$q" \
    -screen-fullscreen 1 -screen-width 1920 -screen-height 1080 \
    -renderpreset quality -farband 1 -farshadow 1 -farshade 1 -seethrough 1 -riverlook 1 -shells 1 -shellworkers 3 \
    -upscaler fsr3 -renderrows 540 -cleardeltas ${EXTRA:-}
  # Task 121: a Metal compile error only shows at run time (Player.log), and every frame is black.
  PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
  grep -m1 -A1 'program_source:[0-9]*:[0-9]*: error' "$PL" 2>/dev/null | sed 's/^/SHADER RUNTIME ERROR (frames are black): /' | tee -a "$LOG"
  R=$(ls -td "$DATA/LookReview"/*_"$q"/ 2>/dev/null | head -n 1)
  [ -n "$R" ] && { cat "${R}lookreview.txt" | tee -a "$LOG"; echo "shots: $R" | tee -a "$LOG"; } || echo "no LookReview folder for $q" | tee -a "$LOG"
done
echo "log: $LOG"
