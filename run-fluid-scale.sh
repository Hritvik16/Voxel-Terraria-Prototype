#!/bin/bash
# run-fluid-scale.sh
#
# §7 SLOT / MEMORY SCALE DATA. COUNTERS ONLY -- THERE IS NO TIMING IN THIS RIG,
# and nothing it prints may be quoted against §2.2.
#
# WHY NO TIMING. §2.2 budgets the fluid CA on the GPU lane (<=3.5 ms) and this
# project's workflow cannot attribute GPU stages: AMENDMENT_8_9 §0 Rule 1 rules
# out Xcode, Rule 2 states FrameTimingManager cannot report Performance State,
# and AMENDMENT_8_10 measured gpuFrameTime inflated ~2.6-2.7x. The last
# acceptance run also showed frame total dominated by preUpdate, so a wall-clock
# figure taken here would be mostly engine-frame-start noise with the fluid's
# contribution buried inside it. Counts and byte sizes are exact; frame time
# here would not be, so it is not collected at all.
#
#   step 1  live fluid in ONE region: 500 -> 2,000 -> 8,000 -> 32,000
#             slot high water, everAllocated, free-list reuse, allocations per
#             placed voxel (the metric that caught the pre-free-list bug at 24.5)
#   step 2  simultaneous regions: 2 -> 4 -> 8
#   step 3  the SHIPPED 1280-voxel radius, never measured at all per
#             DESIGN_NOTE_7_4 §6 item 1 -- measures what size a §7.2 region can
#             actually be, since the radius can only bite if the region is bigger
set -uo pipefail

UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP_PATH="Builds/FluidScale.app"
RIG_OUTPUT_DIR="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/FluidScale"

echo "== Regenerating the scene =="
"$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
  -executeMethod Phase5aSceneBuilder.GenerateFluidScale -logFile fluid_scale_scene.log || {
    echo "SCENE GEN FAILED"; tail -n 40 fluid_scale_scene.log; exit 1; }

rm -rf "$APP_PATH"

echo "== Building (release) =="
"$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
  -executeMethod CommandLineBuild.BuildFluidScaleStandalone -logFile fluid_scale_build.log

if grep -q "Shader error" fluid_scale_build.log; then
  echo "SHADER ERROR in the build:"
  grep -E "Shader error|Shader warning" fluid_scale_build.log
  exit 1
fi

if [ ! -d "$APP_PATH" ]; then
  echo "BUILD FAILED. Tail of fluid_scale_build.log:"
  tail -n 60 fluid_scale_build.log; exit 1
fi

echo "== Running (blocks until the rig quits itself) =="
open -n -W "$APP_PATH" --args -fluidscale

LATEST=$(ls -td "$RIG_OUTPUT_DIR"/*/ 2>/dev/null | head -n 1)
if [ -z "$LATEST" ]; then echo "No run folder under $RIG_OUTPUT_DIR"; exit 1; fi

echo "Run folder: $LATEST"
echo
echo "============== fluid_scale_report.txt =============="
cat "${LATEST}fluid_scale_report.txt"
echo "======================================================"
echo
echo "Screenshots:"
ls "${LATEST}"*.png 2>/dev/null || echo "  (none)"

grep -q "RESULT: PASSED" "${LATEST}fluid_scale_report.txt" || exit 1
exit 0
