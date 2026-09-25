#!/bin/bash
# run-phase7-rig.sh -- §13 Phase 7's acceptance gates, one command.
#
# Builds the "Phase 7 Cycle" scene as a release standalone, runs it unattended
# (the rig quits itself), and prints phase7_report.txt directly to stdout.
#
# WHAT THIS DOES AND DOES NOT PROVE.
#   DOES: correctness. Torch falloff, cave darkness, sweep continuity, zero
#         acne, zero Peter-Panning, §6.5's silhouette rule as a per-pixel
#         GPU-vs-CPU-oracle comparison, §3.10's same-frame sunlight, and
#         §3.8's furthest-first light-cap degradation.
#   DOES NOT: frame time. §13's two budget gates (raymarch+shadows <= 9.0 ms,
#         combined <= 13.0 ms, both "Performance State checked") CANNOT BE RUN
#         AS WRITTEN here -- no Xcode (Amdt 8.9 Rule 1), Performance State is
#         unreadable (Rule 2), gpuFrameTime is inflated ~2.6-2.7x (Amdt 8.10),
#         and Unity merges dispatches into one Metal encoder. The substitute is
#         ./run-phase7-lighting-ab.sh, formally written up in
#         AMENDMENT_8_12_PHASE7_GATE.md.
#
# Logs go to Logs/, not the repo root.
#
# Usage: ./run-phase7-rig.sh            (build + run)
#        SKIP_BUILD=1 ./run-phase7-rig.sh
# Exit code: 0 if the rig reported 0 FAIL, non-zero otherwise.
set -uo pipefail

UNITY="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Phase7Cycle.app"
RD="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Phase7Cycle"
mkdir -p Logs

if [ "${SKIP_BUILD:-0}" != "1" ]; then
  echo "== Generating the scene =="
  "$UNITY" -batchmode -quit -nographics -projectPath "$(pwd)" \
    -executeMethod Phase5aSceneBuilder.GeneratePhase7Cycle \
    -logFile Logs/phase7_scene.log || {
      echo "SCENE GEN FAILED"; tail -40 Logs/phase7_scene.log; exit 1; }

  echo "== Building (release, not development -- see CommandLineBuild.cs) =="
  rm -rf "$APP"    # so the bundle mtime tells the truth (BuildStamp.cs)
  "$UNITY" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildPhase7CycleStandalone \
    -logFile Logs/phase7_build.log

  # A shader error does NOT fail the C# build, so it has to be grepped for
  # explicitly -- a green build with a broken shader is exactly the false green
  # ShaderCompileCheck.cs was written to stop.
  if grep -q "Shader error" Logs/phase7_build.log; then
    echo "SHADER ERROR:"; grep -E "Shader error" Logs/phase7_build.log; exit 1
  fi
  [ -d "$APP" ] || { echo "BUILD FAILED"; tail -60 Logs/phase7_build.log; exit 1; }
fi

echo "== Running (blocks until the rig quits itself) =="
# -cleardeltas: gates compare against pristine generation, so a previous run's
# carved chamber must not persist into this one.
open -n -W "$APP" --args -cleardeltas
RC=$?

LATEST=$(ls -td "$RD"/*/ 2>/dev/null | head -n 1)
[ -z "$LATEST" ] && { echo "No run folder under: $RD"; exit 1; }

echo "Run folder: $LATEST"
echo
echo "=================== phase7_report.txt ==================="
cat "${LATEST}phase7_report.txt"
echo "========================================================="
echo
echo "Screenshots from this run -- READ THESE, do not just count them:"
ls "${LATEST}"*.png 2>/dev/null

grep -q "^PASS .*  FAIL 0" "${LATEST}phase7_report.txt" && exit 0 || exit 1
