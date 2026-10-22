#!/bin/bash
# run-torchshadow-shots.sh -- task 46 (engine plan step 2): BEFORE/AFTER screenshot
# pairs for torch shadow rays + emissive bleed (-torchshadowshots, a LOOK, no timing).
#   ./run-torchshadow-shots.sh          build Playground + shoot
#   SKIP_BUILD=1 ./run-torchshadow-shots.sh
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
    -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/torchshadow_scene.log
  unity_hold_wait
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/torchshadow_build.log
  if grep -q "Shader error" Logs/torchshadow_build.log; then
    echo "SHADER ERROR:"; grep -E "Shader error" Logs/torchshadow_build.log; exit 1
  fi
  [ -d "$APP" ] || { echo "BUILD FAILED"; tail -n 40 Logs/torchshadow_build.log; exit 1; }
fi
unity_hold_wait
caffeinate -dims open -n -W "$APP" --args -torchshadowshots -cleardeltas
R=$(ls -td "$DATA/TorchShadows"/*/ | head -n 1); cat "${R}torchshadows.txt"; echo "shots: $R"
echo DONE
