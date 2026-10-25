#!/bin/bash
# run-fluid-benchmark.sh
#
# The §13 Phase 5b performance gate, measured by this project's ESTABLISHED
# substitute methodology -- the same family as RaymarchAutoBenchmark and
# PHASE_2_COMPLETION.md's table:
#
#   RELEASE standalone build, launched outside the Editor, wall clock from
#   Time.unscaledDeltaTime, driftcheck-validated.
#
# NOT Instruments and NOT Xcode. Per-kernel GPU attribution was attempted and is
# a CONFIRMED DEAD END on this toolchain: Unity merges the CA's eight dispatches
# into one Metal encoder, so only the first kernel is ever named, and forcing
# encoder splits requires a development build that distorts what it measures.
# gpuFrameTime is read nowhere (Amendment 8.10: inflated ~2.6-2.7x here).
#
# WHAT IT MEASURES
#   idle    fluid arena present, NO vents open -- raymarch + streaming only
#   primed  water + sand + lava all venting continuously, same camera pose
#   delta   primed - idle = the cost of running fluid, on the shipped build
#
# The delta is the cost of FLUID AS A WHOLE, not of the CA kernels alone: it
# includes the op-list readback, the CPU applying ops through ChunkStore, the
# resulting clipmap uploads, and any extra raymarch work from voxels that are
# now solid. That is the number §13's gate actually cares about -- "does adding
# fluid still fit the frame" -- and it is stated that way rather than being
# passed off as a GPU-kernel figure.
#
# ONE CONFIG PER PROCESS LAUNCH. RaymarchAutoBenchmark's own header says that is
# the next step after back-to-back configs produced an impossible ordering on
# this fanless machine, and fluid persists once poured, so a second idle config
# inside one process would not be idle.
set -uo pipefail

APP="Builds/Playground.app"
BIN="$APP/Contents/MacOS/Voxel Terraria 1 Byte BrickMap"
RESULTS="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/PlaygroundFluidBench/results.csv"
OUT_DIR="fluid-bench"
STAMP=$(date +%Y%m%d_%H%M%S)

if [ "${SKIP_BUILD:-0}" != "1" ]; then
  echo "== Building RELEASE Playground =="
  rm -rf "$APP"
  /Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity -batchmode -quit \
    -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildPlaygroundStandalone \
    -logFile fluid_bench_build.log
  if [ ! -d "$APP" ]; then
    echo "BUILD FAILED. Tail of fluid_bench_build.log:"; tail -n 40 fluid_bench_build.log; exit 1
  fi
fi

mkdir -p "$OUT_DIR"
rm -f "$RESULTS"

# DISCARDED WARM-UP LAUNCH. Measured, not assumed: without this the sweep
# showed 34-40% driftcheck spread and a NEGATIVE p50 delta (primed "faster"
# than idle), because the first launches of the sweep pay costs later ones do
# not -- Metal pipeline cache compilation and OS file cache for the world data
# both persist ACROSS processes. That penalised whichever config ran first and
# is an ordering artefact, not a fluid cost. One throwaway launch puts every
# measured run on the same footing. Its result is deleted, not reported.
echo "== warm-up launch (discarded) =="
"$BIN" -fluidbench warmup_discard -logFile /tmp/fluidbench_warmup.log >/dev/null 2>&1
rm -f "$RESULTS"

# Order is deliberate and ALTERNATING: idle, primed, idle, primed. The repeats
# come last so drift accumulated across the sweep shows up in them (Phase 2's
# REPEAT_driftcheck shape), and alternating means any residual monotonic drift
# pushes BOTH configs the same way, so the paired deltas survive it. Two
# independent idle/primed pairs are reported rather than one.
for CFG in idle primed idle_REPEAT_driftcheck primed_REPEAT_driftcheck; do
  echo "== $CFG =="
  "$BIN" -fluidbench "$CFG" -logFile "/tmp/fluidbench_$CFG.log" >/dev/null 2>&1
  tail -n 1 "$RESULTS" 2>/dev/null | sed 's/^/   /'
done

cp "$RESULTS" "$OUT_DIR/results_$STAMP.csv" 2>/dev/null
python3 tools/report-fluid-benchmark.py --csv "$RESULTS" | tee "$OUT_DIR/report_$STAMP.txt"
