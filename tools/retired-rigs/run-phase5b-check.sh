#!/bin/bash
# run-phase5b-check.sh -- fast checkpoint: sand_column only, no perf sweep.
# For iterating on one fix at a time without burning a full five-scenario sweep.
set -uo pipefail
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP_PATH="Builds/Phase5bValidation.app"
RIG_OUTPUT_DIR="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Phase5bValidation"
rm -rf "$APP_PATH"   # so the bundle mtime tells the truth (see BuildStamp.cs)
"$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
  -executeMethod Phase5aSceneBuilder.GeneratePhase5b -logFile phase5b_scene.log
"$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
  -executeMethod CommandLineBuild.BuildPhase5bStandalone -logFile phase5b_build.log
# Shader errors for the TARGET platform only appear in the build log --
# Assets/Editor/ShaderCompileCheck.cs compiles for the Editor's platform and
# missed a Metal-only error that silently dropped a kernel and cost several
# diagnostic cycles. A dropped kernel does not fail the build, so grep for it.
if grep -q "Shader error" phase5b_build.log; then
  echo "SHADER ERROR in the build (kernel(s) will be silently dropped at runtime):"
  grep -E "Shader error|Shader warning" phase5b_build.log
  exit 1
fi

if [ ! -d "$APP_PATH" ]; then echo "BUILD FAILED"; tail -n 40 phase5b_build.log; exit 1; fi
open -n -W "$APP_PATH" --args -phase5brig -fastcheck ${EXTRA_ARGS:-}
LATEST=$(ls -td "$RIG_OUTPUT_DIR"/*/ 2>/dev/null | head -n 1)
[ -z "$LATEST" ] && { echo "no run folder"; exit 1; }
cat "${LATEST}phase5b_report.txt"
