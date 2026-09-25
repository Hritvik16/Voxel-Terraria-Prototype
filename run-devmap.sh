#!/bin/bash
# run-devmap.sh -- photograph the dev map (O in the Playground; see
# Assets/Game/DevMap.cs and PlaygroundCapture.DevMap.cs). Default: the new
# sizeClass 2 island (TerrainGen). LEGACY=1 for today's island. BUILD=1 rebuilds
# Builds/Playground.app first. Takes the shared Unity lock.
set -uo pipefail
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Playground.app"
DATA="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap"
mkdir -p Logs
source tools/unity-lock.sh
unity_lock_acquire
if [ "${BUILD:-0}" = "1" ]; then
  rm -rf "$APP"
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/devmap_scene.log
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/devmap_build.log
  if grep -q "error CS" Logs/devmap_build.log Logs/devmap_scene.log; then grep -h "error CS" Logs/devmap_*.log | sort -u; exit 1; fi
  if grep -q "Shader error" Logs/devmap_build.log; then grep "Shader error" Logs/devmap_build.log; exit 1; fi
  [ -d "$APP" ] || { echo "BUILD FAILED"; exit 1; }
  echo "build OK (0 error CS, 0 Shader error)"
fi
if [ "${LEGACY:-0}" = "1" ]; then
  caffeinate -dims open -n -W "$APP" --args -devmap -farband 1 -farshadow 1 -cleardeltas "$@"
else
  caffeinate -dims open -n -W "$APP" --args -devmap -farband 1 -farshadow 1 -farsource tg2 -cleardeltas "$@"
fi
R=$(ls -td "$DATA/DevMap"/*/ 2>/dev/null | head -n 1)
cat "${R}devmap.txt"
ls "$R"
echo "DEVMAP_DONE"
