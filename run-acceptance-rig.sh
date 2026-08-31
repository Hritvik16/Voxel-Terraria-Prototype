#!/bin/bash
# run-acceptance-rig.sh
#
# The full loop, one command: build the Phase 4 standalone player, launch it
# (Phase4Bootstrapper + Phase4AcceptanceRig run automatically on Start and the
# rig quits the process itself when done), then print the report directly to
# stdout. Nothing to drag into a chat — the text is right here in the
# terminal output.
#
# Usage: ./run-acceptance-rig.sh
# Exit code: 0 if a report was found, 1 on build failure or missing output.

set -uo pipefail

UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
PROJECT_PATH="$(pwd)"
APP_PATH="Builds/Phase4Acceptance.app"
BUILD_LOG="build.log"

# Where the rig writes its output. Matches Application.persistentDataPath for
# this project's default Company/Product Name. If you ever change those under
# Edit > Project Settings > Player, update this path to match.
RIG_OUTPUT_DIR="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Phase4Acceptance"

echo "== Building (release, not development — see CommandLineBuild.cs) =="
rm -f "$BUILD_LOG"
# No -nographics here: shader compilation for the build target sometimes wants
# a graphics context even in batch mode, and this step needs no window itself
# either way. -nographics stays reserved for the EditMode test runner, which
# genuinely doesn't need a GPU present.
"$UNITY_BIN" -batchmode -quit -projectPath "$PROJECT_PATH" \
  -executeMethod CommandLineBuild.BuildPhase4Standalone \
  -logFile "$BUILD_LOG"

if [ ! -d "$APP_PATH" ]; then
  echo "BUILD FAILED. Tail of $BUILD_LOG:"
  echo "---"
  tail -n 80 "$BUILD_LOG"
  exit 1
fi

echo "== Running (this blocks until the rig quits itself — expect several minutes) =="
# -n forces a fresh instance rather than waiting on one already running.
# -W blocks this script until the app process exits.
# -cleardeltas: the player default is now FALSE (saves persist, §10.1 says the
# auto-cleaner is an Editor tool, not a shipped player behaviour). The rig wants
# a pristine world every run so gate results stay comparable, so it asks for one.
open -n -W "$APP_PATH" --args -cleardeltas

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
echo "=================== phase4_report.txt ==================="
cat "${LATEST}phase4_report.txt"
echo "==========================================================="
echo
echo "Screenshots from this run (read these directly if you need to look):"
ls "${LATEST}"*.png 2>/dev/null