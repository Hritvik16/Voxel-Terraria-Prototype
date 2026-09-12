#!/bin/bash
# run-acceptance-fluid.sh
#
# STEP 3 -- THE COMBINED LOAD. The Phase 4 acceptance rig, with its own gates
# and its own methodology, running a LIVE TILED FLUID LOAD at the same time as
# the camera flies and the streaming window slides.
#
# WHY THIS EXISTS. Every Phase 4 number on record is TERRAIN ONLY --
# run-acceptance-rig.sh has never had a drop of fluid in it. Every fluid number
# on record comes from a fluid-only rig with a parked or scripted camera. Real
# play is both at once, and nothing in this project has ever measured that.
#
# HOW IT AVOIDS BREAKING THE BASELINE, which matters more than the new number:
#
#   * The rig's fluid load is OPT-IN, default 0. run-acceptance-rig.sh passes
#     no flag and behaves exactly as before.
#   * The scene is a CLONE of "Phase 4 Streaming.unity" with one asset
#     reference added (FluidCA.compute), generated fresh each run by
#     Phase5aSceneBuilder.GeneratePhase4FluidScene. The original scene is never
#     modified, and the terrain-only build does not contain the shader at all.
#   * It builds to Phase4AcceptanceFluid.app, a SEPARATE app. Phase4Acceptance.app
#     is untouched.
#
# So the comparison is: this run's report vs the most recent terrain-only
# phase4_report.txt, same gates, same legs, same machine.
#
# WHAT MAY AND MAY NOT BE QUOTED. 'frame total p50/p99' is wall clock for the
# WHOLE frame. §2.2's <=3.5 ms fluid budget is a GPU-LANE budget and this
# workflow cannot attribute GPU stages (no Metal capture; Amendment 8.10
# measured gpuFrameTime inflated ~2.6-2.7x here, and it is read nowhere). The
# report's fluid section therefore separates only the CPU-side costs it can
# actually measure -- CA submit and PumpAndApply -- and explicitly does not
# claim the CA's GPU share against §2.2.
#
# No Xcode, no Instruments (Amendment 8.9 Rule 1). Fully automated: build,
# launch, run, self-quit, print.
set -uo pipefail

UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP_PATH="Builds/Phase4AcceptanceFluid.app"
RIG_OUTPUT_DIR="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Phase4Acceptance"
FLUID_VOXELS="${FLUID_VOXELS:-8000}"

echo "== Generating the combined-load scene (clone of Phase 4 Streaming + FluidCA) =="
"$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
  -executeMethod Phase5aSceneBuilder.GeneratePhase4FluidScene \
  -logFile phase4_fluid_scene.log || {
    echo "SCENE GEN FAILED"; tail -n 40 phase4_fluid_scene.log; exit 1; }

echo "== Building (release) =="
rm -rf "$APP_PATH"
"$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
  -executeMethod CommandLineBuild.BuildPhase4FluidStandalone \
  -logFile phase4_fluid_build.log

if [ ! -d "$APP_PATH" ]; then
  echo "BUILD FAILED. Tail of phase4_fluid_build.log:"
  tail -n 60 phase4_fluid_build.log
  exit 1
fi

BEFORE=$(ls -td "$RIG_OUTPUT_DIR"/*/ 2>/dev/null | head -n 1)

echo "== Running with a ${FLUID_VOXELS}-voxel live fluid load (blocks; several minutes) =="
open -n -W "$APP_PATH" --args -cleardeltas -fluidload "$FLUID_VOXELS"

LATEST=$(ls -td "$RIG_OUTPUT_DIR"/*/ 2>/dev/null | head -n 1)
if [ -z "$LATEST" ] || [ "$LATEST" = "$BEFORE" ]; then
  echo "No NEW run folder appeared under $RIG_OUTPUT_DIR -- the player wrote nothing."
  exit 1
fi

echo "Run folder: $LATEST"
echo
echo "=================== phase4_report.txt (COMBINED LOAD) ==================="
cat "${LATEST}phase4_report.txt"
echo "========================================================================="
echo
echo "Screenshots from this run:"
ls "${LATEST}"*.png 2>/dev/null
echo
echo "Compare against the most recent TERRAIN-ONLY run for the same gates."
