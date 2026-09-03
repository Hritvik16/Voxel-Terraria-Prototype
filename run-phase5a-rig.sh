#!/bin/bash
# run-phase5a-rig.sh
#
# The Phase 5a basin acceptance pass, one command: regenerate the scene, build
# the standalone player, launch it (Phase5aAcceptanceRig drives all five §13
# scenarios and quits the process itself), print the report, and open the
# output folder in Finder.
#
# WHY A STANDALONE BUILD FOR A CORRECTNESS RIG. §10.2 / CLAUDE.md make the
# standalone player the reference environment. That rule is usually quoted
# about frame time, and this rig deliberately measures none -- but "does this
# actually work" deserves the same environment as "how fast is it", and the
# Editor's OnGUI, screen resolution and asset-serving all differ from a
# player's. The screenshots this produces are of the shipped-shape build.
#
# Usage: ./run-phase5a-rig.sh
# Exit code: 0 if a report was produced, 1 on build failure or missing output.

set -uo pipefail

UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
PROJECT_PATH="$(pwd)"
APP_PATH="Builds/Phase5aAcceptance.app"
BUILD_LOG="phase5a_build.log"
SCENE_LOG="phase5a_scene.log"

# Matches Application.persistentDataPath for this project's Company/Product
# Name, same as run-acceptance-rig.sh. Update both together if those change.
RIG_OUTPUT_DIR="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Phase5aAcceptance"

echo "== Regenerating the scene (so the rig component is guaranteed present) =="
"$UNITY_BIN" -batchmode -quit -nographics -projectPath "$PROJECT_PATH" \
  -executeMethod Phase5aSceneBuilder.Generate \
  -logFile "$SCENE_LOG"

echo "== Building (release, not development — see CommandLineBuild.cs) =="
rm -f "$BUILD_LOG"
# No -nographics: the build step sometimes wants a graphics context for shader
# compilation, exactly as in run-acceptance-rig.sh.
"$UNITY_BIN" -batchmode -quit -projectPath "$PROJECT_PATH" \
  -executeMethod CommandLineBuild.BuildPhase5aStandalone \
  -logFile "$BUILD_LOG"

if [ ! -d "$APP_PATH" ]; then
  echo "BUILD FAILED. Tail of $BUILD_LOG:"
  echo "---"
  tail -n 80 "$BUILD_LOG"
  exit 1
fi

echo "== Running (blocks until the rig quits itself) =="
# -n a fresh instance, -W block until it exits. The rig calls Application.Quit
# when the last scenario is captured, so the app closes on its own.
open -n -W "$APP_PATH" --args -phase5arig

echo "== Locating the run this just produced =="
LATEST=$(ls -td "$RIG_OUTPUT_DIR"/*/ 2>/dev/null | head -n 1)

if [ -z "$LATEST" ]; then
  echo "No run folder found under: $RIG_OUTPUT_DIR"
  echo "Check Edit > Project Settings > Player > Company/Product Name and fix"
  echo "RIG_OUTPUT_DIR above if they differ from 'DefaultCompany' / this project name."
  exit 1
fi

echo "Run folder: $LATEST"
echo
echo "=================== phase5a_report.txt ==================="
cat "${LATEST}phase5a_report.txt"
echo "==========================================================="
echo
echo "Per-scenario CSVs:"
ls "${LATEST}"*.csv 2>/dev/null
echo
echo "Screenshots ($(ls "${LATEST}"*.png 2>/dev/null | wc -l | tr -d ' ') PNGs):"
ls "${LATEST}"*.png 2>/dev/null

# Convenience: the app has already quit itself; put the evidence in front of
# whoever ran this rather than making them go find it.
open "$LATEST"
