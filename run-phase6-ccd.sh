#!/bin/bash
# run-phase6-ccd.sh
#
# SWEPT CCD ACCEPTANCE RIG (§13 Phase 6, file 2 -- §8.2): regenerate the scene,
# build the standalone, run it, print the report.
#
# §13's acceptance line for this file, clause by clause:
#   step 1  0.2 m wall, grapple in at 60 m/s -> stop at the face, 20/20
#   step 2  disable the sweep -> confirm phasing, so step 1 is not vacuous
#   step 3  (§8.2) 3-voxel water sheet at 60 m/s -> the splash fires even though
#           both endpoints are dry; water does not clamp the sweep
#   step 4  force a missed ray -> depenetration catches it in ONE frame, body
#           ends outside the geometry, offending velocity zeroed
#
# WHAT THIS ADDS OVER SweptCCDTests, which already proves all of the above
# against a synthetic Dictionary world with mutation checks: the same maths
# against the REAL ChunkStore, through real chunk/brick addressing and a real
# streaming window, in an IL2CPP release build rather than the editor's Mono,
# with geometry written through the real edit path.
#
# NO TIMING. Performance stays with run-acceptance-rig.sh.
set -uo pipefail

UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
# Must agree with CommandLineBuild.BuildPhase6CcdStandalone and with
# Phase6PlayerRig's _outputRootFolderName.
APP_PATH="Builds/Phase6Ccd.app"
RIG_OUTPUT_DIR="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Phase6Ccd"

echo "== Regenerating the scene =="
"$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
  -executeMethod Phase5aSceneBuilder.GeneratePhase6Ccd -logFile phase6_ccd_scene.log || {
    echo "SCENE GEN FAILED"; tail -n 40 phase6_ccd_scene.log; exit 1; }

# Delete first, so a failed build cannot leave the previous .app in place and
# have the rig report on a stale binary while looking healthy.
rm -rf "$APP_PATH"

echo "== Building (release) =="
"$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
  -executeMethod CommandLineBuild.BuildPhase6CcdStandalone -logFile phase6_ccd_build.log

if grep -q "Shader error" phase6_ccd_build.log; then
  echo "SHADER ERROR in the build:"
  grep -E "Shader error|Shader warning" phase6_ccd_build.log
  exit 1
fi

if [ ! -d "$APP_PATH" ]; then
  echo "BUILD FAILED. Tail of phase6_ccd_build.log:"
  tail -n 60 phase6_ccd_build.log; exit 1
fi

echo "== Running (blocks until the rig quits itself) =="
open -n -W "$APP_PATH" --args -phase6ccd

LATEST=$(ls -td "$RIG_OUTPUT_DIR"/*/ 2>/dev/null | head -n 1)
if [ -z "$LATEST" ]; then echo "No run folder under $RIG_OUTPUT_DIR"; exit 1; fi

echo "Run folder: $LATEST"
echo
echo "============== phase6_ccd_report.txt =============="
cat "${LATEST}phase6_ccd_report.txt"
echo "======================================================"
echo
echo "Screenshots:"
ls "${LATEST}"*.png 2>/dev/null || echo "  (none)"

grep -q "RESULT: PASSED" "${LATEST}phase6_ccd_report.txt" || exit 1
exit 0
