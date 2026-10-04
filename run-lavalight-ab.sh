#!/bin/bash
# run-lavalight-ab.sh -- owner decision 8: lava lights its surroundings through
# one aggregate light per 32-voxel tile (LightSourceBuffer LAVA AGGREGATE).
#   1. -lavalight : ON/OFF screenshot pairs from the same eye (a LOOK)
#   2. -sustained : golden hour, orbit, gate 960, rung 0, live fluid (the
#                   arena vents include LAVA), FULLSCREEN, MINUTES each
#                   (default 4), counterbalanced share 8, 0, 0, 8.
# One build; the arm is a command-line flag (-lavaslots), so every arm runs the
# same binary. -lavaslots 0 is exactly pre-decision-8 behaviour.
#
#   ./run-lavalight-ab.sh              build + everything
#   SKIP_BUILD=1 MINUTES=5 ./run-lavalight-ab.sh
#   SKIP_BUILD=1 SUSTAINED=0 ./run-lavalight-ab.sh   (screenshots only)
set -uo pipefail
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Playground.app"
DATA="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap"
MINUTES="${MINUTES:-4}"
SHARE="${SHARE:-8}"
mkdir -p Logs
LOG="Logs/lavalight_ab_$(date +%Y%m%d_%H%M%S).txt"

# One Unity process at a time, shared lock with the comparison repo.
source tools/unity-lock.sh
unity_lock_acquire
if [ "${SKIP_BUILD:-0}" != "1" ]; then
  rm -rf "$APP"
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
    -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/lavalight_scene.log
  unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/lavalight_build.log
  if grep -q "Shader error" Logs/lavalight_build.log; then
    echo "SHADER ERROR:" | tee -a "$LOG"; grep -E "Shader error" Logs/lavalight_build.log | tee -a "$LOG"; exit 1
  fi
  [ -d "$APP" ] || { echo "BUILD FAILED" | tee -a "$LOG"; tail -n 40 Logs/lavalight_build.log; exit 1; }
fi

if [ "${SKIP_SHOTS:-0}" != "1" ]; then
  echo "== 1. ON/OFF screenshot pairs ==" | tee -a "$LOG"
  unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
  caffeinate -dims open -n -W "$APP" --args -lavalight -cleardeltas
  R=$(ls -td "$DATA/LavaLight"/*/ | head -n 1); cat "${R}lavalight.txt" | tee -a "$LOG"; echo "shots: $R" | tee -a "$LOG"
fi

[ "${SUSTAINED:-1}" = "1" ] || { echo "log: $LOG"; exit 0; }
echo "" | tee -a "$LOG"
echo "== 2. sustained, ${MINUTES} min per arm, counterbalanced ==" | tee -a "$LOG"
for arm in "$SHARE" 0 0 "$SHARE"; do
  unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
  sleep 60   # let the machine settle between launches
  echo "-- arm lavaslots=$arm  load: $(uptime | sed 's/.*load averages*: //')" | tee -a "$LOG"
  caffeinate -dims open -n -W "$APP" --args -sustained -motion orbit -gateres 960 -shadowlevel 0 \
    -minutes "$MINUTES" -tag "lava$arm" -lavaslots "$arm" -cleardeltas
  S=$(ls -td "$DATA/Sustained"/*_lava$arm/ | head -n 1)
  grep -E "lava light share|clock PINNED|^ +[0-9]|FIRST|LAST|WORST|fluid CA|lights at end|uploads/s" "${S}sustained.txt" | tee -a "$LOG"
done
echo "" | tee -a "$LOG"; echo "log: $LOG"
