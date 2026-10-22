#!/bin/bash
# run-upscaler-smoke.sh -- task 47 (engine plan step 3): one still per
# -upscaler (none taa fsr1 stp fsr3) x -renderrows (540 720, then dyn) at the
# tg2 spawn, to prove each path draws a sane image (-upscalersmoke; a LOOK, no
# timing -- task 48 compares). Copies the frames to Screenshots/Engine03/smoke/.
#   ./run-upscaler-smoke.sh          build Playground + shoot
#   SKIP_BUILD=1 ./run-upscaler-smoke.sh
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
    -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/upscalersmoke_scene.log
  unity_hold_wait
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/upscalersmoke_build.log
  if grep -q "Shader error" Logs/upscalersmoke_build.log; then
    echo "SHADER ERROR:"; grep -E "Shader error" Logs/upscalersmoke_build.log; exit 1
  fi
  [ -d "$APP" ] || { echo "BUILD FAILED"; tail -n 40 Logs/upscalersmoke_build.log; exit 1; }
fi
unity_hold_wait
caffeinate -dims open -n -W "$APP" --args -world tg2 -upscalersmoke -cleardeltas ${EXTRA:-}
R=$(ls -td "$DATA/UpscalerSmoke"/*/ | head -n 1); cat "${R}upscalersmoke.txt"; echo "shots: $R"
mkdir -p Screenshots/Engine03/smoke
cp "$R"*.png "$R"upscalersmoke.txt Screenshots/Engine03/smoke/
echo DONE
