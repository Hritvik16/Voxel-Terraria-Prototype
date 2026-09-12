#!/bin/bash
# run-phase6-player.sh
#
# PLAYER CONTROLLER ACCEPTANCE RIG (§13 Phase 6, file 1): regenerate the scene,
# build the standalone, run it, print the report.
#
# WHAT THIS COVERS THAT EDITMODE CANNOT. PlayerMotorTests proves the movement
# mechanism against a synthetic HashSet world -- exact positions, no frames, no
# GPU. This rig proves the parts that only exist in a real build:
#   step 1  spawn resolves onto GENERATED terrain without ending up inside rock
#   step 2  a 6 s walk across genuinely uneven ground: never inside terrain,
#           never out of the world, never blocked by an unstreamed chunk
#   step 3  a jump on real terrain reaches ~v^2/2g and lands
#   step 4  §13's "3-voxel steps climbable un-jumped", built in the REAL
#           ChunkStore -- plus a 4-voxel control that must NOT be climbable
#   step 5  §8.1's HOT RELOAD, end to end: the rig rewrites PlayerConfig.json
#           mid-run exactly as a human would, and the NEXT jump is higher, with
#           no recompile and no restart. Then puts the file back.
#   step 6  a 1-voxel wall is never tunneled at run speed
#
# WHAT IS STILL MANUAL, AND ONLY THIS: whether the movement FEELS good. §8.1
# says no automated test captures that. The rig proves the mechanism and proves
# the tuning loop works; choosing the numbers is the human's job, which is
# precisely what step 5 exists to make cheap.
#
# NO TIMING. Performance stays with run-acceptance-rig.sh.
set -uo pipefail

UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
# Must agree with CommandLineBuild.BuildPhase6PlayerStandalone and with
# Phase6PlayerRig's _outputRootFolderName.
APP_PATH="Builds/Phase6Player.app"
RIG_OUTPUT_DIR="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Phase6Player"

echo "== Regenerating the scene =="
"$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
  -executeMethod Phase5aSceneBuilder.GeneratePhase6Player -logFile phase6_player_scene.log || {
    echo "SCENE GEN FAILED"; tail -n 40 phase6_player_scene.log; exit 1; }

# Delete first, so a failed build cannot leave the previous .app in place and
# have the rig report on a stale binary while looking healthy.
rm -rf "$APP_PATH"

echo "== Building (release) =="
"$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
  -executeMethod CommandLineBuild.BuildPhase6PlayerStandalone -logFile phase6_player_build.log

if grep -q "Shader error" phase6_player_build.log; then
  echo "SHADER ERROR in the build:"
  grep -E "Shader error|Shader warning" phase6_player_build.log
  exit 1
fi

if [ ! -d "$APP_PATH" ]; then
  echo "BUILD FAILED. Tail of phase6_player_build.log:"
  tail -n 60 phase6_player_build.log; exit 1
fi

echo "== Running (blocks until the rig quits itself) =="
open -n -W "$APP_PATH" --args -phase6player

LATEST=$(ls -td "$RIG_OUTPUT_DIR"/*/ 2>/dev/null | head -n 1)
if [ -z "$LATEST" ]; then echo "No run folder under $RIG_OUTPUT_DIR"; exit 1; fi

echo "Run folder: $LATEST"
echo
echo "============== phase6_player_report.txt =============="
cat "${LATEST}phase6_player_report.txt"
echo "======================================================"
echo
echo "Screenshots:"
ls "${LATEST}"*.png 2>/dev/null || echo "  (none)"

grep -q "RESULT: PASSED" "${LATEST}phase6_player_report.txt" || exit 1
exit 0
