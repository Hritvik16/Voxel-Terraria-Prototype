#!/bin/bash
# run-phase6-combined.sh
#
# EVERY PHASE 6 SYSTEM AT ONCE, ON THE TILED FLUID SUBSTRATE.
#
# Two firsts: nothing has run the player, edits, CCD, detonations,
# projectiles and buoyancy CONCURRENTLY, and nothing has run any of them
# against the tiled substrate (Phase6SandboxRig is sequential and still builds
# the old dense simulation).
#
# COOLDOWNS ARE MANDATORY AND LIVE HERE. This machine throttles ~183% across
# back-to-back runs -- measured, last session -- while being internally stable
# within one cooled run. Every config below is preceded by a 300 s idle, and
# the toggle sweep repeats the baseline at the END as a driftcheck twin, so
# drift across the whole sweep is visible rather than assumed absent.
#
# USAGE
#   ./run-phase6-combined.sh            one cooled baseline run
#   ./run-phase6-combined.sh --sweep    the per-system toggle sweep (long)
#
# NO gpuFrameTime against a budget (Amendment 8.10), no Xcode/Instruments
# (8.9 Rule 1), no Performance State field.
set -uo pipefail

UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Phase6Combined.app"
RD="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Phase6Combined"
COOL="${COOL:-300}"
SWEEP=0
[ "${1:-}" = "--sweep" ] && SWEEP=1

if [ "${SKIP_BUILD:-0}" != "1" ]; then
  echo "== Generating the scene =="
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
    -executeMethod Phase5aSceneBuilder.GeneratePhase6Combined \
    -logFile phase6_combined_scene.log || {
      echo "SCENE GEN FAILED"; tail -n 40 phase6_combined_scene.log; exit 1; }

  echo "== Building (release) =="
  rm -rf "$APP"
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildPhase6CombinedStandalone \
    -logFile phase6_combined_build.log
  if [ ! -d "$APP" ]; then
    echo "BUILD FAILED. Tail of phase6_combined_build.log:"
    tail -n 60 phase6_combined_build.log; exit 1
  fi
fi

run_cfg () {
  local label="$1"; shift
  echo "== $label  (cooling ${COOL}s first) =="
  sleep "$COOL"
  open -n -W "$APP" --args -cleardeltas "$@"
  local D
  D=$(ls -1d "$RD"/*/ 2>/dev/null | sort | tail -1)
  if [ -z "$D" ]; then echo "   NO RUN FOLDER"; return 1; fi
  printf "   %-22s %s\n" "$label" "$(basename "$D")"
  grep -oE "^PASS [0-9]+  FAIL [0-9]+" "$D/phase6_combined_report.txt" | sed 's/^/      /'
  grep -E "^  p50 " "$D/phase6_combined_report.txt" | sed 's/^/      /'
  grep -E "band mean frame|SHARE OF BAND" "$D/phase6_combined_report.txt" | head -2 | sed 's/^/      /'
}

if [ "$SWEEP" = "1" ]; then
  echo "===== PER-SYSTEM TOGGLE SWEEP ====="
  run_cfg "all-on"
  run_cfg "no-physics"    -nophysics
  run_cfg "no-edits"      -noedits
  run_cfg "no-boom"       -noboom
  run_cfg "no-proj"       -noproj
  run_cfg "no-buoy"       -nobuoy
  run_cfg "no-fluid"      -nofluid
  # Driftcheck twin LAST, so cross-sweep drift lands in it.
  run_cfg "all-on-REPEAT_driftcheck"
else
  run_cfg "all-on"
  LATEST=$(ls -1d "$RD"/*/ | sort | tail -1)
  echo
  echo "=================== phase6_combined_report.txt ==================="
  cat "${LATEST}phase6_combined_report.txt"
  echo "=================================================================="
  echo
  echo "Screenshots:"; ls "${LATEST}"*.png 2>/dev/null
fi
