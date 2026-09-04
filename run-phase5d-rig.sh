#!/bin/bash
# run-phase5d-rig.sh
#
# Phase 5d EDIT-PATH stress rig: regenerate the scene, build the standalone,
# run it, print the report.
#
# WHAT THIS COVERS THAT 5a/5b DO NOT: the paths a PLAYER drives -- placing,
# mining out from under settled fluid, painting one cell repeatedly, running a
# vent -- and, centrally, it runs every case in BOTH frame orderings:
#   MirrorFirst = upload then tick   (what the 5a/5b rigs do)
#   TickFirst   = tick then upload   (what Playground/Phase4Bootstrapper do)
# and requires identical results. That is the invariant the frozen-fluid bug
# violated, and the reason 5b was green while hand-placed fluid froze.
#
# NO TIMING. This rig runs synchronous readback for determinism and reports no
# ms figures at all. Performance stays with run-acceptance-rig.sh.
set -uo pipefail

UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP_PATH="Builds/Phase5dEditStress.app"
RIG_OUTPUT_DIR="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Phase5dEditStress"

echo "== Regenerating the scene =="
"$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
  -executeMethod Phase5aSceneBuilder.GeneratePhase5d -logFile phase5d_scene.log || {
    echo "SCENE GEN FAILED"; tail -n 40 phase5d_scene.log; exit 1; }

# DELETE THE OLD BUILD FIRST. Without this, a build that fails or never runs
# leaves the PREVIOUS .app in place, the "-d $APP_PATH" check below passes, and
# the rig runs a STALE BINARY while looking completely healthy -- it reports on
# code that is not the code on disk. That happened twice while writing this rig
# (once producing a report with a diagnostic line simply missing, which is a
# very confusing thing to debug). Removing it first makes a bad build loud.
rm -rf "$APP_PATH"

echo "== Building (release) =="
"$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
  -executeMethod CommandLineBuild.BuildPhase5dStandalone -logFile phase5d_build.log

# Metal-only shader errors only show up in the TARGET build log; a dropped
# kernel does not fail the build, it just silently does nothing at runtime.
if grep -q "Shader error" phase5d_build.log; then
  echo "SHADER ERROR in the build (kernel(s) will be silently dropped at runtime):"
  grep -E "Shader error|Shader warning" phase5d_build.log
  exit 1
fi

if [ ! -d "$APP_PATH" ]; then
  echo "BUILD FAILED. Tail of phase5d_build.log:"; tail -n 60 phase5d_build.log; exit 1
fi

echo "== Running (blocks until the rig quits itself) =="
open -n -W "$APP_PATH" --args -phase5drig

LATEST=$(ls -td "$RIG_OUTPUT_DIR"/*/ 2>/dev/null | head -n 1)
if [ -z "$LATEST" ]; then echo "No run folder under $RIG_OUTPUT_DIR"; exit 1; fi

echo "Run folder: $LATEST"
echo
echo "=================== phase5d_report.txt ==================="
cat "${LATEST}phase5d_report.txt"
echo "==========================================================="
ls "$LATEST"
grep -q "RESULT: FAILED" "${LATEST}phase5d_report.txt" && { echo; echo "!! RIG FAILED"; exit 1; }
exit 0
