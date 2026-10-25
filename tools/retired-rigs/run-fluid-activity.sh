#!/bin/bash
# run-fluid-activity.sh
#
# §13 PHASE 6'S INTEGRATED ACCEPTANCE RIG (`Phase6_Sandbox`).
#
# The four per-file rigs each test ONE Phase 6 file against a world nobody else
# is touching. This runs §13's whole acceptance list as one scenario with the
# player, CCD, edit path, destruction, buoyancy, streaming, persistence and the
# fluid CA all live at once -- which is where interaction failures live, and
# where four green per-file rigs prove nothing.
#
#   step 1  walk and jump on generated terrain, whole stack live
#   step 2  0.2 m wall, grapple at 60 m/s, 20/20 -- and a projectile agrees
#   step 3  adversarial checkerboard against §3.6's LRU valve  <- the memory gate
#   step 4  drill 60 s at 200 vox/s, then save and reload the region
#   step 5  400K detonation: <=3 frames, one Proxy Drop, plausible tally
#   step 6  60 m/s dive into water: buoyancy inside the mitigated window
#   step 7  flood front with the player at the leading edge   <- PARTLY MANUAL
#   step 8  frame time across the run                          <- READ ITS CAVEAT
#
# STEP 7 IS THE ONE GENUINELY MANUAL ITEM IN PHASE 6. §13 asks for a BY-FEEL
# judgement of whether bounded op-list latency is perceptible at the leading
# edge of a flood. The rig stages the moment and measures what a human would be
# judging; the verdict is a person's.
#
# STEP 8 IS NOT run-acceptance-rig.sh, and its numbers must not be quoted as the
# §2.2 gate. Same methodology (release standalone, own unscaledDeltaTime),
# different harness and scenario, and it includes the rig's own overhead.
set -uo pipefail

UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
# Must agree with CommandLineBuild.BuildFluidActivityStandalone and with
# Phase6PlayerRig's _outputRootFolderName.
APP_PATH="Builds/FluidActivity.app"
RIG_OUTPUT_DIR="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/FluidActivity"

echo "== Regenerating the scene =="
"$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
  -executeMethod Phase5aSceneBuilder.GenerateFluidActivity -logFile fluid_activity_scene.log || {
    echo "SCENE GEN FAILED"; tail -n 40 fluid_activity_scene.log; exit 1; }

# Delete first, so a failed build cannot leave the previous .app in place and
# have the rig report on a stale binary while looking healthy.
rm -rf "$APP_PATH"

echo "== Building (release) =="
"$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
  -executeMethod CommandLineBuild.BuildFluidActivityStandalone -logFile fluid_activity_build.log

if grep -q "Shader error" fluid_activity_build.log; then
  echo "SHADER ERROR in the build:"
  grep -E "Shader error|Shader warning" fluid_activity_build.log
  exit 1
fi

if [ ! -d "$APP_PATH" ]; then
  echo "BUILD FAILED. Tail of fluid_activity_build.log:"
  tail -n 60 fluid_activity_build.log; exit 1
fi

echo "== Running (blocks until the rig quits itself) =="
open -n -W "$APP_PATH" --args -fluidactivity

LATEST=$(ls -td "$RIG_OUTPUT_DIR"/*/ 2>/dev/null | head -n 1)
if [ -z "$LATEST" ]; then echo "No run folder under $RIG_OUTPUT_DIR"; exit 1; fi

echo "Run folder: $LATEST"
echo
echo "============== fluid_activity_report.txt =============="
cat "${LATEST}fluid_activity_report.txt"
echo "======================================================"
echo
echo "Screenshots:"
ls "${LATEST}"*.png 2>/dev/null || echo "  (none)"

grep -q "RESULT: PASSED" "${LATEST}fluid_activity_report.txt" || exit 1
exit 0
