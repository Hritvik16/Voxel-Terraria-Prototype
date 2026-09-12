#!/bin/bash
# run-phase5b-rig.sh
#
# Phase 5b GPU-port validation: regenerate the scene, build the standalone,
# run it (the rig drives 5a's five scenarios on the GPU path with the CPU
# oracle alongside, then takes a PROVISIONAL wall-clock reading), print the
# report, open the output folder.
#
# MEASUREMENT NOTE, so nobody has to go digging: the timing this produces is
# PROVISIONAL and NOT verified. It is wall clock from Time.unscaledDeltaTime
# only -- gpuFrameTime is deliberately never read (Amendment 8.10 measured it
# inflated ~2.6-2.7x on this hardware). Per AMENDMENT_8_9 §0 Rule 2 the run
# carries a REPEAT_driftcheck line, which is the only throttle evidence this
# workflow can produce. These numbers cannot close §13 Phase 5b's perf gate.
set -uo pipefail

UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP_PATH="Builds/Phase5bValidation.app"
RIG_OUTPUT_DIR="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Phase5bValidation"

echo "== Regenerating the scene =="
rm -rf "$APP_PATH"   # so the bundle mtime tells the truth (see BuildStamp.cs)
"$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
  -executeMethod Phase5aSceneBuilder.GeneratePhase5b -logFile phase5b_scene.log

echo "== Building (release) =="
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

if [ ! -d "$APP_PATH" ]; then
  echo "BUILD FAILED. Tail of phase5b_build.log:"; tail -n 60 phase5b_build.log; exit 1
fi

echo "== Running (blocks until the rig quits itself) =="
open -n -W "$APP_PATH" --args -phase5brig ${EXTRA_ARGS:-}

LATEST=$(ls -td "$RIG_OUTPUT_DIR"/*/ 2>/dev/null | head -n 1)
if [ -z "$LATEST" ]; then echo "No run folder under $RIG_OUTPUT_DIR"; exit 1; fi

echo "Run folder: $LATEST"
echo
echo "=================== phase5b_report.txt ==================="
cat "${LATEST}phase5b_report.txt"
echo "==========================================================="
if grep -q "RESULT: FAILED" "${LATEST}phase5b_report.txt"; then
  echo; echo "!! RIG FAILED — see the RESULT line above and player_log.txt."
  open "$LATEST"; exit 1
fi
open "$LATEST"
