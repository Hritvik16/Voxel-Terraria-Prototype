#!/bin/bash
# run-lodpop.sh -- is the LOD tier switch visible? (-lodpop, see
# Assets/Game/PlaygroundCapture.LodPop.cs). Uses the existing Builds/Playground.app
# unless BUILD=1. Takes the shared Unity lock. Prints tools/lodpop-report.py.
set -uo pipefail
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Playground.app"
DATA="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap"
mkdir -p Logs
source tools/unity-lock.sh
unity_lock_acquire
if [ "${BUILD:-0}" = "1" ]; then
  rm -rf "$APP"
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/lodpop_scene.log
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/lodpop_build.log
  if grep -q "error CS" Logs/lodpop_build.log Logs/lodpop_scene.log; then grep -h "error CS" Logs/lodpop_*.log | sort -u; exit 1; fi
  if grep -q "Shader error" Logs/lodpop_build.log; then grep "Shader error" Logs/lodpop_build.log; exit 1; fi
  [ -d "$APP" ] || { echo "BUILD FAILED"; exit 1; }
  echo "build OK (0 error CS, 0 Shader error)"
fi
caffeinate -dims open -n -W "$APP" --args -lodpop -cleardeltas "$@"
R=$(ls -td "$DATA/LodPop"/*/ 2>/dev/null | head -n 1)
cat "${R}lodpop.txt"
python3 tools/lodpop-report.py "$R"
