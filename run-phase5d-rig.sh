#!/bin/bash
# run-phase5d-rig.sh
#
# Phase 5d STREAMING x FLUID rig: regenerate the scene, build the standalone,
# run it, print the report.
#
# WHAT THIS COVERS THAT 5a/5b/5c DO NOT: every earlier fluid rig ran on ONE
# static, always-resident chunk. This one runs the CA while the streamer is
# actually admitting and evicting underneath it:
#   - window-relative position characterised FIRST, before any assertion
#   - eviction and return, checking conservation across the round trip
#   - a pour that STRADDLES a chunk-residency edge, with the guard observed
#     firing (if it never fires, the conservation result is untested, not proven)
#   - a window slide, checking the CPU state is unchanged by streaming alone
#   - honey on the GPU path
# It found one real bug -- silent mass loss when fluid moved across a
# residency edge -- fixed in 1dfcb7f. PHASE_5C_COMPLETION.md §9 is the
# authoritative write-up; §9.7 is the verdict and its caveats.
#
# NOT the 5c edit-path rig. That one is run-phase5c-rig.sh: 14 cases x both
# frame orderings (MirrorFirst / TickFirst). This header used to describe that
# rig instead of this one.
#
# NO TIMING. This rig runs synchronous readback for determinism and reports no
# ms figures at all. Performance stays with run-acceptance-rig.sh.
set -uo pipefail

UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
# These two MUST match what the build and the rig actually use, or the script
# lies about a healthy run. They were copied from run-phase5c-rig.sh and left
# saying "Phase5dEditStress", while CommandLineBuild.BuildPhase5dStandalone
# writes Builds/Phase5dStreamFluid.app and Phase5dStreamFluid.cs writes its
# report under .../Phase5dStreamFluid. The visible effect was a build that
# SUCCEEDED ("result=Succeeded errors=0") being reported as "BUILD FAILED",
# because the -d check below tested a path nothing ever creates. The rm -rf
# above was also clearing the wrong path, which is exactly the stale-binary
# hazard its own comment warns about.
APP_PATH="Builds/Phase5dStreamFluid.app"
RIG_OUTPUT_DIR="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Phase5dStreamFluid"

echo "== Regenerating the scene =="
"$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
  -executeMethod Phase5aSceneBuilder.GeneratePhase5d -logFile phase5d_scene.log || {
    echo "SCENE GEN FAILED"; tail -n 40 phase5d_scene.log; exit 1; }

# DELETE THE OLD BUILD FIRST. Without this, a build that fails or never runs
# leaves the PREVIOUS .app in place, the "-d $APP_PATH" check below passes, and
# the rig runs a STALE BINARY while looking completely healthy -- it reports on
# code that is not the code on disk. That happened twice while writing this rig
# (once producing a report with a diagnostic line simply missing, which is a
# very confusing thing to debug). Removing it first makes a bad build loud.
rm -rf "$APP_PATH"

echo "== Building (release) =="
"$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
  -executeMethod CommandLineBuild.BuildPhase5dStandalone -logFile phase5d_build.log

# Metal-only shader errors only show up in the TARGET build log; a dropped
# kernel does not fail the build, it just silently does nothing at runtime.
if grep -q "Shader error" phase5d_build.log; then
  echo "SHADER ERROR in the build (kernel(s) will be silently dropped at runtime):"
  grep -E "Shader error|Shader warning" phase5d_build.log
  exit 1
fi

if [ ! -d "$APP_PATH" ]; then
  echo "BUILD FAILED. Tail of phase5d_build.log:"; tail -n 60 phase5d_build.log; exit 1
fi

echo "== Running (blocks until the rig quits itself) =="
open -n -W "$APP_PATH" --args -phase5drig

LATEST=$(ls -td "$RIG_OUTPUT_DIR"/*/ 2>/dev/null | head -n 1)
if [ -z "$LATEST" ]; then echo "No run folder under $RIG_OUTPUT_DIR"; exit 1; fi

echo "Run folder: $LATEST"
echo
echo "=================== phase5d_report.txt ==================="
cat "${LATEST}phase5d_report.txt"
echo "==========================================================="
ls "$LATEST"
grep -q "RESULT: FAILED" "${LATEST}phase5d_report.txt" && { echo; echo "!! RIG FAILED"; exit 1; }
exit 0
