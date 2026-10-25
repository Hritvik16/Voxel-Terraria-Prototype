#!/bin/bash
# run-fluid-ab.sh
#
# DENSE vs TILED, WALL CLOCK, FULLY AUTOMATED. Build, launch, run, quit,
# report -- no manual step at any point, no Editor, no Xcode, no Instruments.
#
# WHAT IT PRODUCES
#   STEP 1  the same fluid scenario through the dense region path and through
#           the tiled path, at four matched volumes. The single most important
#           missing number in this project: does tiling cost more, less, or
#           the same per voxel than the dense addressing it replaces.
#   STEP 2  one volume, four scatter patterns (1 / 8 / 64 / 512 disconnected
#           pockets). Isolates whether SCATTER costs anything beyond volume.
#
# METHODOLOGY, inherited from run-fluid-benchmark.sh and RaymarchAutoBenchmark
# rather than reinvented:
#
#   ONE CONFIG PER PROCESS LAUNCH. RaymarchAutoBenchmark's header records
#   back-to-back configs producing a physically impossible ordering on this
#   fanless machine, GPU frequency scaling the leading hypothesis, and says
#   the next step is one config per launch. Fluid also persists once poured,
#   so a second config in one process would not start clean.
#
#   A DISCARDED WARM-UP LAUNCH. Measured, not assumed, in run-fluid-benchmark:
#   without it the first launches pay Metal pipeline-cache compilation and OS
#   file-cache costs that later ones do not, which penalises whichever config
#   ran first. Its result is deleted.
#
#   INTERLEAVED ORDER, DRIFTCHECKS LAST. dense and tiled alternate at each
#   volume so any monotonic drift across the sweep pushes BOTH the same way
#   and the paired delta survives it. Every config is then re-run with a
#   _REPEAT_driftcheck suffix at the end, so drift accumulated across the
#   whole sweep is visible in the repeat rather than hidden.
#
#   NO gpuFrameTime. Amendment 8.10 measured it inflated ~2.6-2.7x here.
#   Nothing this script prints is a GPU-stage attribution.
#
#   THERE IS NO PERFORMANCE STATE FIELD -- FrameTimingManager cannot report it
#   on this platform. Permanent accepted limitation, not worked around.
set -uo pipefail

UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/FluidAB.app"
BIN="$APP/Contents/MacOS/Voxel Terraria 1 Byte BrickMap"
RESULTS="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/FluidAB/results.csv"
OUT_DIR="fluid-ab"
STAMP=$(date +%Y%m%d_%H%M%S)
PER_RUN_TIMEOUT=420          # seconds; a hung launch must not stall the sweep

mkdir -p "$OUT_DIR"

if [ "${SKIP_BUILD:-0}" != "1" ]; then
  echo "== Regenerating the scene =="
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
    -executeMethod Phase5aSceneBuilder.GenerateFluidAB -logFile fluid_ab_scene.log || {
      echo "SCENE GEN FAILED"; tail -n 40 fluid_ab_scene.log; exit 1; }

  echo "== Building (release) =="
  rm -rf "$APP"
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildFluidABStandalone -logFile fluid_ab_build.log

  if grep -q "Shader error" fluid_ab_build.log; then
    echo "SHADER ERROR in the build:"; grep -E "Shader error" fluid_ab_build.log; exit 1
  fi
  if [ ! -d "$APP" ]; then
    echo "BUILD FAILED. Tail of fluid_ab_build.log:"; tail -n 60 fluid_ab_build.log; exit 1
  fi
fi

rm -f "$RESULTS"

# One launch per config, with a hard timeout so a hang costs one config
# instead of the night.
run_cfg () {
  local cfg="$1"
  echo "== $cfg =="
  ( "$BIN" -fluidab "$cfg" -logFile "/tmp/fluidab_$cfg.log" >/dev/null 2>&1 ) &
  local pid=$!
  local waited=0
  while kill -0 "$pid" 2>/dev/null; do
    sleep 5; waited=$((waited+5))
    if [ "$waited" -ge "$PER_RUN_TIMEOUT" ]; then
      echo "   TIMEOUT after ${waited}s -- killing $cfg"
      kill -9 "$pid" 2>/dev/null; pkill -9 -f "FluidAB.app" 2>/dev/null
      break
    fi
  done
  wait "$pid" 2>/dev/null
  tail -n 1 "$RESULTS" 2>/dev/null | cut -c1-150 | sed 's/^/   /'
}

echo "== warm-up launch (DISCARDED) =="
run_cfg "tiled_v2000" >/dev/null 2>&1
rm -f "$RESULTS"

# ---- STEP 1: matched volumes, dense and tiled alternating ----
for V in 500 2000 8000 32000; do
  run_cfg "dense_v$V"
  run_cfg "tiled_v$V"
done

# ---- STEP 2: one volume, scatter ladder (tiled only) ----
# The dense path cannot represent the wide patterns at all -- a 512-pocket
# scatter spans ~1100 voxels, and a dense box that size is 1.4 billion cells.
# That inability is itself a step 2 result, reported rather than worked round.
for S in 1 8 64 512; do
  run_cfg "tiled_s${S}_v8000"
done

# ---- driftcheck pass: every config again, same order, at the end ----
for V in 500 2000 8000 32000; do
  run_cfg "dense_v${V}_REPEAT_driftcheck"
  run_cfg "tiled_v${V}_REPEAT_driftcheck"
done
for S in 1 8 64 512; do
  run_cfg "tiled_s${S}_v8000_REPEAT_driftcheck"
done

cp "$RESULTS" "$OUT_DIR/results_$STAMP.csv" 2>/dev/null
python3 tools/report-fluid-ab.py --csv "$RESULTS" | tee "$OUT_DIR/report_$STAMP.txt"
echo
echo "CSV    -> $OUT_DIR/results_$STAMP.csv"
echo "REPORT -> $OUT_DIR/report_$STAMP.txt"
