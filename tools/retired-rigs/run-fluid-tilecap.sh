#!/bin/bash
# run-fluid-tilecap.sh -- the frozen-cube repro, and the tile-pool cap ladder.
#
# ONE BUILD, MANY CAPS. The cap is read from the command line rather than
# baked into the scene, so every cap value runs the SAME binary against the
# SAME scenario. Rebuilding between caps would put a fresh IL2CPP compile
# between two numbers that are supposed to differ only by a pool size.
#
# 300s cooldown before EVERY run (this machine throttles ~183% back-to-back),
# and CAPS is walked twice so each cap carries a driftcheck twin.
#
# Usage:  ./run-fluid-tilecap.sh                 # 512 1024 2048, 2 passes
#         CAPS="512" PASSES=1 ./run-fluid-tilecap.sh
#         SLOTCAP=1000000 CAPS="2048" ./run-fluid-tilecap.sh   # step 3
set -uo pipefail
UNITY="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/FluidTileCap.app"
RD="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/FluidTileCap"
COOL="${COOL:-300}"
CAPS="${CAPS:-512 1024 2048}"
PASSES="${PASSES:-2}"
SECS="${SECS:-60}"
SPREAD="${SPREAD:-384}"
SLOTCAP="${SLOTCAP:-500000}"

if [ "${SKIP_BUILD:-0}" != "1" ]; then
  "$UNITY" -batchmode -quit -nographics -projectPath "$(pwd)" \
    -executeMethod Phase5aSceneBuilder.GenerateFluidTileCap -logFile tilecap_scene.log || {
      echo "SCENE GEN FAILED"; tail -30 tilecap_scene.log; exit 1; }
  rm -rf "$APP"
  "$UNITY" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildFluidTileCapStandalone -logFile tilecap_build.log
  [ -d "$APP" ] || { echo "BUILD FAILED"; tail -40 tilecap_build.log; exit 1; }
fi

for p in $(seq 1 "$PASSES"); do
  for cap in $CAPS; do
    FREE=$(df -g . | tail -1 | awk '{print $4}')
    [ "$FREE" -lt 5 ] && { echo "!! ${FREE}GB free, below the 5GB floor -- stopping"; exit 2; }
    echo; echo "########## CAP $cap  pass $p/$PASSES  (cooling ${COOL}s, ${FREE}GB free) ##########"
    sleep "$COOL"
    open -n -W "$APP" --args -cleardeltas -tilecap "$cap" -slotcap "$SLOTCAP" \
         -spread "$SPREAD" -holdseconds "$SECS"
    D=$(ls -1d "$RD"/*/ 2>/dev/null | sort | tail -1)
    [ -z "$D" ] && { echo "  NO RUN FOLDER"; continue; }
    echo "  folder $(basename "$D")"
    grep -E "^  (tile pool cap|PEAK TILE DEMAND|peak resident|refusals|live slots peak|GPU |managed heap|MARKER CUBE|VERDICT|frames )" \
         "${D}tilecap_report.txt" | sed 's/^/  /'
    grep -E "^    (PASS|FAIL|note)" "${D}tilecap_report.txt" | sed 's/^/  /'
    grep -oE "^PASS [0-9]+  FAIL [0-9]+" "${D}tilecap_report.txt" | sed 's/^/  >>> /'
  done
done
echo; echo "TILECAP LADDER DONE"
