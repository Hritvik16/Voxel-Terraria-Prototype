#!/bin/bash
# run-phase5b-demo.sh -- builds the Phase 5b demo/playable scene, runs the
# scripted demo, prints the report and opens the screenshot folder.
#
# The SAME build is the playable one: launch Builds/Phase5bDemo.app with no
# arguments to fly around it by hand (keys are in the on-screen overlay).
set -uo pipefail
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP_PATH="Builds/Phase5bDemo.app"
OUT="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Phase5bDemo"

echo "== Regenerating the demo scene =="
"$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
  -executeMethod Phase5aSceneBuilder.GeneratePhase5bDemo -logFile phase5bdemo_scene.log

echo "== Building (release) =="
"$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
  -executeMethod CommandLineBuild.BuildPhase5bDemoStandalone -logFile phase5bdemo_build.log

if grep -q "Shader error" phase5bdemo_build.log; then
  echo "SHADER ERROR (kernels would be silently dropped at runtime):"
  grep -E "Shader error" phase5bdemo_build.log; exit 1
fi
if [ ! -d "$APP_PATH" ]; then echo "BUILD FAILED"; tail -n 40 phase5bdemo_build.log; exit 1; fi

echo "== Running the scripted demo =="
open -n -W "$APP_PATH" --args -phase5bdemo

LATEST=$(ls -td "$OUT"/*/ 2>/dev/null | head -n 1)
[ -z "$LATEST" ] && { echo "no run folder under $OUT"; exit 1; }
echo "Run folder: $LATEST"
echo
cat "${LATEST}demo_report.txt"
echo
ls "${LATEST}"*.png
open "$LATEST"
