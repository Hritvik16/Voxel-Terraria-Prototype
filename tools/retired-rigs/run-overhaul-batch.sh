#!/bin/bash
# run-overhaul-batch.sh -- the 2026-09-25 owner brief's verification batch,
# under ONE hold of the shared Unity lock. Stages (STAGES="..." for a subset):
#   tests      EditMode suite (stops the batch on red)
#   build      RELEASE Playground build (compile + shader gates)
#   lodpop     -lodpop: tier 0/1/2 vs an all-tier-0 frame + mirror diagnostics
#              (residency map, clipmap/cascade validators: lag vs LOST updates)
#   roster     -fluidroster: the §2.7 fluids on the GPU CA (sealed basins)
#   preview    -farpreview -farsource tg2: TerrainGen's island incl. map v2 + stamps
#   fluidtiled run-fluid-tiled.sh (19/19 before this work)
#   brushguard run-phase6-brushguard.sh (15/15 red before: its premise is stale)
#   accfluid   run-acceptance-fluid.sh (3 identity gates expected red under fluid)
set -uo pipefail
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Playground.app"
DATA="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap"
STAGES="${STAGES:-tests build lodpop roster preview fluidtiled brushguard accfluid}"
RUN_ID="$(date +%Y%m%d_%H%M%S)"
mkdir -p Logs
LOG="Logs/overhaul_${RUN_ID}.txt"
has() { [[ " $STAGES " == *" $1 "* ]]; }
source tools/unity-lock.sh
unity_lock_acquire
echo "run $RUN_ID  stages: $STAGES" | tee -a "$LOG"

if has tests; then
  ./run-editmode-tests.sh > Logs/overhaul_${RUN_ID}_editmode.txt 2>&1; rc=$?
  tail -n 4 Logs/overhaul_${RUN_ID}_editmode.txt | tee -a "$LOG"
  grep -h "^\[\(DetailCull\|Hydrology\|Stamps\|TerrainGen\)\]" unity_test.log 2>/dev/null | head -40 | tee -a "$LOG"
  if [ $rc -ne 0 ]; then echo "EDITMODE RED -- stopping" | tee -a "$LOG"; grep -h "error CS" unity_test.log | sort -u | head -20 | tee -a "$LOG"; exit 1; fi
fi

if has build; then
  rm -rf "$APP"
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/overhaul_scene.log
  unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/overhaul_build.log
  if grep -q "error CS" Logs/overhaul_build.log Logs/overhaul_scene.log; then grep -h "error CS" Logs/overhaul_*.log | sort -u | tee -a "$LOG"; exit 1; fi
  if grep -q "Shader error" Logs/overhaul_build.log; then grep "Shader error" Logs/overhaul_build.log | tee -a "$LOG"; exit 1; fi
  [ -d "$APP" ] || { echo "BUILD FAILED" | tee -a "$LOG"; exit 1; }
  echo "build OK (0 error CS, 0 Shader error)" | tee -a "$LOG"
fi

if has lodpop; then
  echo "== lodpop ==" | tee -a "$LOG"
  unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
  caffeinate -dims open -n -W "$APP" --args -lodpop -cleardeltas
  R=$(ls -td "$DATA/LodPop"/*/ | head -n 1); cat "${R}lodpop.txt" | tee -a "$LOG"
  python3 tools/lodpop-report.py "$R" | tee -a "$LOG"; echo "shots: $R" | tee -a "$LOG"
fi
if has roster; then
  echo "== fluid roster (GPU CA) ==" | tee -a "$LOG"
  unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
  caffeinate -dims open -n -W "$APP" --args -fluidroster -cleardeltas
  R=$(ls -td "$DATA/FluidRoster"/*/ | head -n 1); cat "${R}fluidroster.txt" | tee -a "$LOG"; echo "shots: $R" | tee -a "$LOG"
fi
if has preview; then
  echo "== TerrainGen preview ==" | tee -a "$LOG"
  unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
  caffeinate -dims open -n -W "$APP" --args -farpreview -farband 1 -farsource tg2 -farshadow 1 -cleardeltas
  R=$(ls -td "$DATA/FarBand"/*_preview/ | head -n 1); cat "${R}farpreview.txt" | tee -a "$LOG"; echo "shots: $R" | tee -a "$LOG"
fi
for rig in fluidtiled:run-fluid-tiled.sh brushguard:run-phase6-brushguard.sh accfluid:run-acceptance-fluid.sh; do
  name="${rig%%:*}"; script="${rig#*:}"
  if has "$name"; then
    echo "== $script ==" | tee -a "$LOG"
    unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
    ./"$script" > "Logs/overhaul_${RUN_ID}_${name}.txt" 2>&1; echo "rc=$?" | tee -a "$LOG"
    grep -E "PASS|FAIL|RESULT" "Logs/overhaul_${RUN_ID}_${name}.txt" | tail -n 40 | tee -a "$LOG"
  fi
done
echo "log: $LOG"
