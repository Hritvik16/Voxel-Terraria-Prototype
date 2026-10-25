#!/bin/bash
# run-playtest-bugs.sh
#
# STEP 0 DIAGNOSTIC for the two bugs found by playtesting. It fixes nothing --
# its whole job is to tell candidate causes apart with evidence, so that a fix
# lands on a confirmed cause rather than a plausible-sounding one.
#
#   A1  floating TERRAIN scanned with NO fluid simulation running
#         -> separates "terrain-generation seam" from anything fluid-related
#   A2  a pour, then the camera walked back to ordinary viewing distances
#         -> reports frozen voxels as INSIDE vs OUTSIDE §7.4's sleep radius
#   A3  five force-demote/re-promote cycles, watching DenseBricksHeld
#         -> catches an orphaned dense brick from CSIntent's demote path
#   B1  altitude vs LODConfig.TIER_OUTER_RANGE_M and the world's real height
#         -> "terrain vanishes up high" is expected to be documented behaviour
#   B2  residency / IsBlocking / ResolveSpawn / actual fall, at four altitudes
#         -> WITH A CONTROL at a height inside the generated world
#
# THIS RIG REPORTS NO TIMINGS. Nothing it prints may be quoted as a §2.2
# number; run-acceptance-rig.sh is the only source for those.
set -uo pipefail

UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP_PATH="Builds/PlaytestBugs.app"
RIG_OUTPUT_DIR="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/PlaytestBugs"

echo "== Regenerating the scene =="
"$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
  -executeMethod Phase5aSceneBuilder.GeneratePlaytestBugs -logFile playtest_bugs_scene.log || {
    echo "SCENE GEN FAILED"; tail -n 40 playtest_bugs_scene.log; exit 1; }

rm -rf "$APP_PATH"

echo "== Building (release) =="
"$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
  -executeMethod CommandLineBuild.BuildPlaytestBugsStandalone -logFile playtest_bugs_build.log

if grep -q "Shader error" playtest_bugs_build.log; then
  echo "SHADER ERROR in the build:"
  grep -E "Shader error|Shader warning" playtest_bugs_build.log
  exit 1
fi

if [ ! -d "$APP_PATH" ]; then
  echo "BUILD FAILED. Tail of playtest_bugs_build.log:"
  tail -n 60 playtest_bugs_build.log; exit 1
fi

echo "== Running (blocks until the rig quits itself) =="
open -n -W "$APP_PATH" --args -playtestbugs

LATEST=$(ls -td "$RIG_OUTPUT_DIR"/*/ 2>/dev/null | head -n 1)
if [ -z "$LATEST" ]; then echo "No run folder under $RIG_OUTPUT_DIR"; exit 1; fi

echo "Run folder: $LATEST"
echo
echo "============== playtest_bugs_report.txt =============="
cat "${LATEST}playtest_bugs_report.txt"
echo "======================================================"
echo
echo "Screenshots:"
ls "${LATEST}"*.png 2>/dev/null || echo "  (none)"

grep -q "RESULT: PASSED" "${LATEST}playtest_bugs_report.txt" || exit 1
exit 0
