#!/bin/bash
# run-shadow-dots.sh -- task 56 (engine plan step 8, BUGS #1): sun-shadow "dots" at a
# low sun. -shadowdots: golden-hour stills (temporal shadows on/off, still/moving,
# Quality and FSR 3) + the sunVis view checked pixel by pixel against the CPU oracle
# (ShadowRayReference). A LOOK plus counts, no timing.
#   TAG=before ./run-shadow-dots.sh          build Playground + shoot
#   SKIP_BUILD=1 TAG=after ./run-shadow-dots.sh
set -uo pipefail
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Playground.app"
DATA="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap"
mkdir -p Logs
source tools/unity-lock.sh
unity_lock_acquire
if [ "${SKIP_BUILD:-0}" != "1" ]; then
  rm -rf "$APP"
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
    -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/shadowdots_scene.log
  unity_hold_wait
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/shadowdots_build.log
  if grep -qE "Shader error|error CS" Logs/shadowdots_build.log; then
    echo "BUILD ERROR:"; grep -E "Shader error|error CS" Logs/shadowdots_build.log | head; exit 1
  fi
  [ -d "$APP" ] || { echo "BUILD FAILED"; tail -n 40 Logs/shadowdots_build.log; exit 1; }
fi
unity_hold_wait
caffeinate -dims open -n -W "$APP" --args -world tg2 -farband 1 -farshadow 1 -renderpreset quality \
  -shadowdots -shadowdotstag "${TAG:-run}" -cleardeltas ${EXTRA:-}
R=$(ls -td "$DATA/ShadowDots"/*/ | head -n 1); cat "${R}shadowdots.txt"; echo "shots: $R"
echo DONE
