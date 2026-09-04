#!/bin/bash
# run-playground.sh -- builds the Playground dogfood scene and captures a
# first-pass look. Pass no args to the built app to fly around it yourself.
#
# NOT a diagnostic run. Nothing this produces is evidence about correctness,
# fluid scale, or performance.
set -uo pipefail
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Playground.app"
OUT="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/PlaygroundShots"

"$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
  -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile playground_scene.log
"$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
  -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile playground_build.log

if grep -q "Shader error" playground_build.log; then
  echo "SHADER ERROR:"; grep -E "Shader error" playground_build.log; exit 1
fi
[ -d "$APP" ] || { echo "BUILD FAILED"; tail -n 40 playground_build.log; exit 1; }

open -n -W "$APP" --args -playgroundshots -cleardeltas
LATEST=$(ls -td "$OUT"/*/ 2>/dev/null | head -n 1)
[ -z "$LATEST" ] && { echo "no run folder"; exit 1; }
echo "Run folder: $LATEST"; echo
cat "${LATEST}look_report.txt"; echo
ls "${LATEST}"*.png
open "$LATEST"
