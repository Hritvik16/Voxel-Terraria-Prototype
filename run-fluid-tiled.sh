#!/bin/bash
# run-fluid-tiled.sh
#
# ACCEPTANCE FOR §7.2's SPARSE TILED ACTIVE SET (DESIGN_NOTE_7_2).
#
# THE PROBLEM IT EXISTS TO CLOSE. One dense per-cell buffer set costs 16 B/cell,
# so reaching FLUID_ACTIVE_RADIUS_VOXELS = 1280 needed a 2048^3 region = 137 GB.
# At every size that CAN exist the radius was larger than the region's own
# half-diagonal, so §7.4's gate was inert by construction.
#
#   A  THE SHIPPED RADIUS, MEASURED. 1280 voxels with a real GPU allocation
#      number summed from the buffer objects -- not arithmetic. And the number
#      must not move between r=128, r=640 and r=1280, which is the whole claim.
#   B  THE RADIUS ACTUALLY GATES: walk away, every tile releases; return, it
#      wakes. The wake half is answered by ChunkFluidMask (one ulong per chunk)
#      rather than by the dense cell sweep that no longer exists.
#   C  EXPLOSION SCATTER. Many small disconnected pockets of water/sand/lava
#      appearing near-simultaneously over ~90 m. A single pool is one tile
#      cluster; scatter is what tests concurrent tile count, the pool cap, and
#      -- the one that matters -- whether anything SILENTLY FAILS TO WAKE, which
#      the run audits pocket by pocket rather than assuming.
#
# NO GPU TIMING. §2.2 budgets the fluid CA on the GPU lane and this workflow
# cannot attribute GPU stages (AMENDMENT_8_9 §0 Rules 1-2, AMENDMENT_8_10's
# 2.6-2.7x inflation). Memory here is exact; time would not be. Nothing this
# prints may be quoted against §2.2.
set -uo pipefail

UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP_PATH="Builds/FluidTiled.app"
RIG_OUTPUT_DIR="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/FluidTiled"

echo "== Regenerating the scene =="
"$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
  -executeMethod Phase5aSceneBuilder.GenerateFluidTiled -logFile fluid_tiled_scene.log || {
    echo "SCENE GEN FAILED"; tail -n 40 fluid_tiled_scene.log; exit 1; }

rm -rf "$APP_PATH"

echo "== Building (release) =="
"$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
  -executeMethod CommandLineBuild.BuildFluidTiledStandalone -logFile fluid_tiled_build.log

if grep -q "Shader error" fluid_tiled_build.log; then
  echo "SHADER ERROR in the build:"
  grep -E "Shader error|Shader warning" fluid_tiled_build.log
  exit 1
fi

if [ ! -d "$APP_PATH" ]; then
  echo "BUILD FAILED. Tail of fluid_tiled_build.log:"
  tail -n 60 fluid_tiled_build.log; exit 1
fi

echo "== Running (blocks until the rig quits itself) =="
open -n -W "$APP_PATH" --args -fluidtiled

LATEST=$(ls -td "$RIG_OUTPUT_DIR"/*/ 2>/dev/null | head -n 1)
if [ -z "$LATEST" ]; then echo "No run folder under $RIG_OUTPUT_DIR"; exit 1; fi

echo "Run folder: $LATEST"
echo
echo "============== fluid_tiled_report.txt =============="
cat "${LATEST}fluid_tiled_report.txt"
echo "======================================================"
echo
echo "Screenshots:"
ls "${LATEST}"*.png 2>/dev/null || echo "  (none)"

grep -q "RESULT: PASSED" "${LATEST}fluid_tiled_report.txt" || exit 1
exit 0
