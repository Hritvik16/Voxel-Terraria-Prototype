#!/bin/bash
# run-phase6-brushguard.sh
#
# BRUSH-GUARD END-TO-END RIG: regenerate the scene, build the standalone, run
# it, print the report.
#
# WHY THIS RIG EXISTS. The fix in 19b8ac6 stopped the Playground brush placing
# fluid outside the CA arena, where it could never be simulated (bug.png: a
# frozen sand column in mid-air). That fix was correctness-proven at the unit
# level by FluidRegionBrushGuardTests, but EditMode CANNOT reach the Playground:
# it lives in Assembly-CSharp, which no asmdef test assembly can reference. So
# "does right-click actually call the guard" was untested by construction.
#
# This rig drives Playground.TryPaintBrush -- the exact method HandleMouse calls
# on RMB -- in a real standalone build with no human input, and asserts on what
# reached ChunkStore:
#   step 1  outside the arena, water/sand/lava are refused and NOTHING is
#           written; 60 CA ticks later there is still nothing frozen there
#   step 2  stone (not simulated) outside the arena still places -- the guard
#           must not be over-broad
#   step 3  a blob straddling the arena face is refused ENTIRELY, not clipped
#   step 4  back inside, water places AND the CA actually moves it
# Step 4 is what stops the rig passing vacuously: without it, a guard that
# refused everything everywhere would look perfect.
#
# NO TIMING. This is a correctness rig; it reports no ms figures at all.
# Performance stays with run-acceptance-rig.sh.
set -uo pipefail

UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
# These two MUST agree with CommandLineBuild.BuildPhase6BrushGuardStandalone and
# with Phase6BrushGuard's _outputRootFolderName. run-phase5d-rig.sh shipped with
# these copied from another rig and never corrected: it tested for a path
# nothing wrote, so a build that SUCCEEDED was reported as "BUILD FAILED".
APP_PATH="Builds/Phase6BrushGuard.app"
RIG_OUTPUT_DIR="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Phase6BrushGuard"

echo "== Regenerating the scene =="
"$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
  -executeMethod Phase5aSceneBuilder.GeneratePhase6BrushGuard -logFile phase6_brushguard_scene.log || {
    echo "SCENE GEN FAILED"; tail -n 40 phase6_brushguard_scene.log; exit 1; }

# DELETE THE OLD BUILD FIRST, so a failed build cannot leave the previous .app
# in place and have the rig report on a stale binary while looking healthy.
rm -rf "$APP_PATH"

echo "== Building (release) =="
"$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
  -executeMethod CommandLineBuild.BuildPhase6BrushGuardStandalone -logFile phase6_brushguard_build.log

# Metal-only shader errors only show up in the TARGET build log; a dropped
# kernel does not fail the build, it silently does nothing at runtime -- which
# for this rig would look like "the CA never moved the water" (a step 4 FAIL).
if grep -q "Shader error" phase6_brushguard_build.log; then
  echo "SHADER ERROR in the build (kernel(s) will be silently dropped at runtime):"
  grep -E "Shader error|Shader warning" phase6_brushguard_build.log
  exit 1
fi

if [ ! -d "$APP_PATH" ]; then
  echo "BUILD FAILED. Tail of phase6_brushguard_build.log:"
  tail -n 60 phase6_brushguard_build.log; exit 1
fi

echo "== Running (blocks until the rig quits itself) =="
open -n -W "$APP_PATH" --args -phase6brushguard
RIG_RC=$?

LATEST=$(ls -td "$RIG_OUTPUT_DIR"/*/ 2>/dev/null | head -n 1)
if [ -z "$LATEST" ]; then echo "No run folder under $RIG_OUTPUT_DIR"; exit 1; fi

echo "Run folder: $LATEST"
echo
echo "============= phase6_brushguard_report.txt ============="
cat "${LATEST}phase6_brushguard_report.txt"
echo "========================================================"
echo
echo "Screenshots:"
ls "${LATEST}"*.png 2>/dev/null || echo "  (none)"

grep -q "RESULT: PASSED" "${LATEST}phase6_brushguard_report.txt" || exit 1
exit 0
