#!/bin/bash
# run-phase6-edit.sh
#
# EDIT SERVICE ACCEPTANCE RIG (§13 Phase 6, file 3 -- §8.3): regenerate the
# scene, build the standalone, run it, print the report.
#
#   step 1  mine one block and build one, through the REAL ChunkStore
#   step 2  the three tiers remove rock at 10 / 40 / 200 vox/s over a real second
#   step 3  sustained drilling at the top tier keeps pace with its rate
#   step 4  digging outside the loaded window is REFUSED and counted (§9.4)
#   step 5  §13's headline for this file: breach a water body and the wake-scan
#           makes the fluid actually move (§10.4's M-E)
#   step 6  a prefab with a non-box silhouette places, skipping its Air cells
#
# WHAT THIS ADDS OVER EditServiceTests, which proves the orchestration against a
# fake writer: the same path against the REAL ChunkStore -- real uniform-chunk
# expansion, real brick densification -- and the real Phase-5 fluid pool.
#
# NOT COVERED, SAID PLAINLY: §13's "persists through save/reload, coalesces on
# fill-in" for a drilled region. That is §10.4's M-G, an edit x persistence
# integration that belongs with the Phase 4 persistence rig.
#
# NO TIMING. Voxel RATES are reported (that is what a tool tier is); no ms
# figures. Performance stays with run-acceptance-rig.sh.
set -uo pipefail

UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
# Must agree with CommandLineBuild.BuildPhase6EditStandalone and with
# Phase6PlayerRig's _outputRootFolderName.
APP_PATH="Builds/Phase6Edit.app"
RIG_OUTPUT_DIR="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Phase6Edit"

echo "== Regenerating the scene =="
"$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
  -executeMethod Phase5aSceneBuilder.GeneratePhase6Edit -logFile phase6_edit_scene.log || {
    echo "SCENE GEN FAILED"; tail -n 40 phase6_edit_scene.log; exit 1; }

# Delete first, so a failed build cannot leave the previous .app in place and
# have the rig report on a stale binary while looking healthy.
rm -rf "$APP_PATH"

echo "== Building (release) =="
"$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
  -executeMethod CommandLineBuild.BuildPhase6EditStandalone -logFile phase6_edit_build.log

if grep -q "Shader error" phase6_edit_build.log; then
  echo "SHADER ERROR in the build:"
  grep -E "Shader error|Shader warning" phase6_edit_build.log
  exit 1
fi

if [ ! -d "$APP_PATH" ]; then
  echo "BUILD FAILED. Tail of phase6_edit_build.log:"
  tail -n 60 phase6_edit_build.log; exit 1
fi

echo "== Running (blocks until the rig quits itself) =="
open -n -W "$APP_PATH" --args -phase6edit

LATEST=$(ls -td "$RIG_OUTPUT_DIR"/*/ 2>/dev/null | head -n 1)
if [ -z "$LATEST" ]; then echo "No run folder under $RIG_OUTPUT_DIR"; exit 1; fi

echo "Run folder: $LATEST"
echo
echo "============== phase6_edit_report.txt =============="
cat "${LATEST}phase6_edit_report.txt"
echo "======================================================"
echo
echo "Screenshots:"
ls "${LATEST}"*.png 2>/dev/null || echo "  (none)"

grep -q "RESULT: PASSED" "${LATEST}phase6_edit_report.txt" || exit 1
exit 0
