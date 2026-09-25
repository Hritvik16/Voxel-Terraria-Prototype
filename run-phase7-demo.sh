#!/bin/bash
# run-phase7-demo.sh -- builds the Playground and plays the Phase 7 review
# (PHASE_7_COMPLETION.md §12) by itself, in a window you can watch, saving a
# screenshot at every moment a §10 question turns on. Opens index.html at the end.
#
# NOT a measurement. Windowed, back-to-back: nothing it produces is evidence
# about frame time, and it prints none.
#
#   ./run-phase7-demo.sh            build, then run
#   SKIP_BUILD=1 ./run-phase7-demo.sh   reuse Builds/Playground.app
set -uo pipefail
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Playground.app"
OUT="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Phase7Demo"
mkdir -p Logs

# One Unity process at a time, shared lock with the comparison repo.
source tools/unity-lock.sh
unity_lock_acquire

if [ "${SKIP_BUILD:-0}" != "1" ]; then
  rm -rf "$APP"   # so the bundle mtime tells the truth (see BuildStamp.cs)
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
    -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/phase7demo_scene.log
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/phase7demo_build.log
  # ShaderCompileCheck can report green for a shader the Metal player rejects
  # (CLAUDE.md); the build log is the check that counts.
  if grep -q "Shader error" Logs/phase7demo_build.log; then
    echo "SHADER ERROR:"; grep -E "Shader error" Logs/phase7demo_build.log; exit 1
  fi
  [ -d "$APP" ] || { echo "BUILD FAILED"; tail -n 40 Logs/phase7demo_build.log; exit 1; }
fi

caffeinate -dims open -n -W "$APP" --args -phase7demo -cleardeltas
LATEST=$(ls -td "$OUT"/*/ 2>/dev/null | head -n 1)
[ -z "$LATEST" ] && { echo "no run folder"; exit 1; }
echo "Run folder: $LATEST"
cat "${LATEST}demo_log.txt"
ls "${LATEST}"*.png | wc -l | xargs echo "screenshots:"
[ "${NO_OPEN:-0}" = "1" ] || open "${LATEST}index.html"
