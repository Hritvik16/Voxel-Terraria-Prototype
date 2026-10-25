#!/bin/bash
# run-roofskip-ab.sh -- the shadow roof skip (Raymarch.compute HeightMip block):
#   1. -roofskipab : same frame OFF vs ON at 9 poses -> differing pixels + steps/ray
#   2. -sustained  : golden hour, orbit, gate 960, rung 0, live fluid, FULLSCREEN,
#                    MINUTES each (default 3), counterbalanced OFF, ON, ON, OFF.
# One build, the switch is a command-line flag, so both arms run the same binary.
#
#   ./run-roofskip-ab.sh              build + everything
#   SKIP_BUILD=1 MINUTES=5 ./run-roofskip-ab.sh
set -uo pipefail
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Playground.app"
DATA="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap"
MINUTES="${MINUTES:-3}"
mkdir -p Logs
LOG="Logs/roofskip_ab_$(date +%Y%m%d_%H%M%S).txt"

# One Unity process at a time, shared lock with the comparison repo.
source tools/unity-lock.sh
unity_lock_acquire
if [ "${SKIP_BUILD:-0}" != "1" ]; then
  rm -rf "$APP"
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
    -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/roofskip_scene.log
  unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/roofskip_build.log
  if grep -q "Shader error" Logs/roofskip_build.log; then
    echo "SHADER ERROR:" | tee -a "$LOG"; grep -E "Shader error" Logs/roofskip_build.log | tee -a "$LOG"; exit 1
  fi
  [ -d "$APP" ] || { echo "BUILD FAILED" | tee -a "$LOG"; tail -n 40 Logs/roofskip_build.log; exit 1; }
fi

echo "== 1. pixel + counter A/B ==" | tee -a "$LOG"
unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
caffeinate -dims open -n -W "$APP" --args -roofskipab -cleardeltas
R=$(ls -td "$DATA/RoofSkipAB"/*/ | head -n 1); cat "${R}roofskip_ab.txt" | tee -a "$LOG"

echo "" | tee -a "$LOG"
echo "== 2. sustained, ${MINUTES} min per arm, counterbalanced ==" | tee -a "$LOG"
for arm in 0 1 1 0; do
  unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
  sleep 60   # let the machine settle between launches
  echo "-- arm roofskip=$arm  load: $(uptime | sed 's/.*load averages*: //')" | tee -a "$LOG"
  caffeinate -dims open -n -W "$APP" --args -sustained -motion orbit -gateres 960 -shadowlevel 0 \
    -minutes "$MINUTES" -tag "roofskip$arm" -roofskip "$arm"
  S=$(ls -td "$DATA/Sustained"/*_roofskip$arm/ | head -n 1)
  grep -E "roof skip|clock PINNED|^ +[0-9]|FIRST|LAST|WORST|SHADOW RAYS|fluid CA" "${S}sustained.txt" | tee -a "$LOG"
done
echo "" | tee -a "$LOG"; echo "log: $LOG"
