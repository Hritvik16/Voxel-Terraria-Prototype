#!/bin/bash
# run-fluid-stagger.sh -- DIAGNOSIS ONLY for the staggered-drop effect.
# Changes nothing, tunes nothing. Runs three variants and prints the spreads.
set -uo pipefail
UNITY="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/FluidStagger.app"
RD="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/FluidStagger"
COOL="${COOL:-120}"
if [ "${SKIP_BUILD:-0}" != "1" ]; then
  "$UNITY" -batchmode -quit -nographics -projectPath "$(pwd)" \
    -executeMethod Phase5aSceneBuilder.GenerateFluidStagger -logFile fluid_stagger_scene.log || exit 1
  rm -rf "$APP"
  "$UNITY" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildFluidStaggerStandalone -logFile fluid_stagger_build.log
  [ -d "$APP" ] || { echo "BUILD FAILED"; tail -40 fluid_stagger_build.log; exit 1; }
fi
run () {
  echo; echo "########## $1 ##########"; shift
  sleep "$COOL"
  open -n -W "$APP" --args -cleardeltas "$@"
  D=$(ls -1d "$RD"/*/ | sort | tail -1)
  grep -E "VARIANT|ACQUIRED |1st MOTION|motion-after-acquire|budget .* ops/frame|never acquired|never moved|peak live" "${D}fluid_stagger_report.txt"
}
run "BASELINE      refresh=20 budget=4096"
run "VARIANT A     refresh=1  budget=4096"  -refreshevery 1
run "VARIANT B     refresh=20 budget=65536" -applybudget 65536
echo; echo "done"
