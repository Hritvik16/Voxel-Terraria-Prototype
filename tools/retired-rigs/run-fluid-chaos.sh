#!/bin/bash
# run-fluid-chaos.sh -- the large-scale chaos LADDER.
#
# Escalating rungs of placed fluid volume under full chaos: continuous
# multi-material vents, overlapping detonations scattering material into the
# fluid, and the player moving and digging throughout.
#
# THE LADDER STOPS AT THE FIRST FAILING RUNG, ON PURPOSE. A found ceiling is
# the result this exercise exists for; forcing a higher rung after a clean
# failure destroys the evidence for where the ceiling actually is.
#
# COOLDOWNS: 300 s idle before every rung. This machine throttles ~183% across
# back-to-back runs (measured) while being internally stable within one cooled
# run, so a ladder without cooldowns would measure the machine heating up and
# call it a scaling limit.
#
# Two ceilings are already known and are NOT failures when reported cleanly:
#   MAX_ACTIVE_FLUID = 500,000 clamps LIVE slots however much is placed.
#   The 512-tile pool binds on SPREAD, not volume; a clean §7.7 refusal is a
#   pass, an unhandled exhaustion is not.
#
# No gpuFrameTime against a budget (Amdt 8.10), no Xcode/Instruments (8.9 R1).
set -uo pipefail

UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/FluidChaos.app"
RD="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/FluidChaos"
COOL="${COOL:-300}"
RUNGS="${RUNGS:-50000 150000 400000 1000000}"
DISK_FLOOR_GB=5

if [ "${SKIP_BUILD:-0}" != "1" ]; then
  echo "== Generating the scene =="
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
    -executeMethod Phase5aSceneBuilder.GenerateFluidChaos \
    -logFile fluid_chaos_scene.log || {
      echo "SCENE GEN FAILED"; tail -n 40 fluid_chaos_scene.log; exit 1; }
  echo "== Building (release) =="
  rm -rf "$APP"
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildFluidChaosStandalone \
    -logFile fluid_chaos_build.log
  [ -d "$APP" ] || { echo "BUILD FAILED"; tail -n 60 fluid_chaos_build.log; exit 1; }
fi

for N in $RUNGS; do
  FREE_GB=$(df -g . | tail -1 | awk '{print $4}')
  if [ "$FREE_GB" -lt "$DISK_FLOOR_GB" ]; then
    echo "!! STOPPING LADDER: ${FREE_GB}GB free is below the ${DISK_FLOOR_GB}GB floor"
    exit 2
  fi

  echo
  echo "########## RUNG: $N placed voxels   (cooling ${COOL}s, ${FREE_GB}GB free) ##########"
  sleep "$COOL"
  open -n -W "$APP" --args -cleardeltas -chaosvoxels "$N"
  D=$(ls -1d "$RD"/*/ 2>/dev/null | sort | tail -1)
  if [ -z "$D" ]; then echo "!! NO RUN FOLDER -- stopping ladder"; exit 1; fi

  echo "   folder $(basename "$D")"
  grep -E "^  placed|^  LIVE slots|^  tiles |^  op-list|^  GPU active set|^  managed heap" \
       "${D}fluid_chaos_report.txt" | sed 's/^/  /'
  grep -E "^  frames " "${D}fluid_chaos_report.txt" | sed 's/^/  /'
  grep -E "    (PASS|FAIL|note)" "${D}fluid_chaos_report.txt" | sed 's/^/  /'
  PF=$(grep -oE "^PASS [0-9]+  FAIL [0-9]+" "${D}fluid_chaos_report.txt")
  echo "   >>> $PF"

  if ! grep -q "^RESULT: PASSED" "${D}fluid_chaos_report.txt"; then
    echo
    echo "!! RUNG $N FAILED -- LADDER STOPS HERE. This is the ceiling; the"
    echo "!! evidence above is the result. Not forcing a higher rung."
    exit 0
  fi
done

echo
echo "All rungs passed: $RUNGS"
echo "No ceiling found up to the highest rung -- the ladder was not exhausted."
