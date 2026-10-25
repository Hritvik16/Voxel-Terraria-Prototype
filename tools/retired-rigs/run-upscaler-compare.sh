#!/bin/bash
# run-upscaler-compare.sh -- task 48 (engine plan step 3, the comparison half):
# -upscalercompare at the tg2 world, quality preset + far shadows (as
# MODE=shimmer). One launch: the shimmer protocol per arm, then the stills.
# Copies the stills + log to Screenshots/Engine03/compare/ and writes the
# per-arm shimmer table (tools/nearshimmer-report.py) to Logs/upscalercompare_<ts>.txt.
# The shimmer frames stay in the player's data folder (path printed).
#   ./run-upscaler-compare.sh          build Playground + run
#   SKIP_BUILD=1 ./run-upscaler-compare.sh
set -uo pipefail
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Playground.app"
DATA="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap"
mkdir -p Logs
source tools/unity-lock.sh
unity_lock_acquire
if [ "${SKIP_BUILD:-0}" != "1" ]; then
  rm -rf "$APP"
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
    -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/upscalercompare_scene.log
  unity_hold_wait
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/upscalercompare_build.log
  if grep -q "Shader error\|error CS" Logs/upscalercompare_build.log; then
    echo "BUILD ERROR:"; grep -E "Shader error|error CS" Logs/upscalercompare_build.log | head; exit 1
  fi
  [ -d "$APP" ] || { echo "BUILD FAILED"; tail -n 40 Logs/upscalercompare_build.log; exit 1; }
fi
unity_hold_wait
caffeinate -dims open -n -W "$APP" --args -world tg2 -upscalercompare -renderpreset quality -farshadow 1 -cleardeltas ${EXTRA:-}
R=$(ls -td "$DATA/UpscalerCompare"/*/ | head -n 1)
TS=$(basename "$R")
OUT="Logs/upscalercompare_${TS}.txt"
{ cat "${R}upscalercompare.txt"; echo; echo "=== SHIMMER per arm (tools/nearshimmer-report.py) ==="
  for a in native none_540 taa_540 fsr1_540 stp_540 fsr3_540 taa_720 fsr1_720 stp_720 fsr3_720; do
    [ -d "${R}shimmer/$a" ] || continue; echo; echo "### $a"; python3 tools/nearshimmer-report.py "${R}shimmer/$a"; done; } > "$OUT" 2>&1
mkdir -p Screenshots/Engine03/compare
cp "${R}"stills/*.png Screenshots/Engine03/compare/ 2>/dev/null
cp "$OUT" Screenshots/Engine03/compare/upscalercompare.txt
echo "report: $OUT   shots: $R"
echo DONE
