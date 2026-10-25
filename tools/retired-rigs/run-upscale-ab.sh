#!/bin/bash
# run-upscale-ab.sh -- URP's out-of-the-box upscalers against this renderer.
#   1. -upscaleab : 16 configs (nearest/bilinear/FSR 1.0/STP x internal 960/640/560/480)
#                   + today's shipped path, each scored against native 1920x1080 at
#                   4 poses + a motion test. Full-res PNGs for side-by-side crops.
#   2. -sustained : golden hour, orbit, rung 0, live fluid, FULLSCREEN, MINUTES each,
#                   for the ARMS below; the shipped arm runs first AND last so drift
#                   across the session is visible rather than hidden in a comparison.
#
#   SKIP_BUILD=1 SKIP_QUALITY=1 MINUTES=3 ./run-upscale-ab.sh
#   ARMS="shipped:960 fsr:640 stp:560 shipped:960" ./run-upscale-ab.sh
set -uo pipefail
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Playground.app"
DATA="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap"
MINUTES="${MINUTES:-3}"
ARMS="${ARMS:-shipped:960 fsr:640 point:640 fsr:560 shipped:960}"
mkdir -p Logs
LOG="Logs/upscale_ab_$(date +%Y%m%d_%H%M%S).txt"

source tools/unity-lock.sh
unity_lock_acquire

if [ "${SKIP_BUILD:-0}" != "1" ]; then
  rm -rf "$APP"
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
    -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/upscale_scene.log
  unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/upscale_build.log
  if grep -q "Shader error" Logs/upscale_build.log; then
    echo "SHADER ERROR:" | tee -a "$LOG"; grep -E "Shader error" Logs/upscale_build.log | tee -a "$LOG"; exit 1
  fi
  [ -d "$APP" ] || { echo "BUILD FAILED" | tee -a "$LOG"; exit 1; }
fi

if [ "${SKIP_QUALITY:-0}" != "1" ]; then
  echo "== 1. quality: 17 configs vs native 1920x1080 ==" | tee -a "$LOG"
  unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
  caffeinate -dims open -n -W "$APP" --args -upscaleab -cleardeltas
  R=$(ls -td "$DATA/UpscaleAB"/*/ | head -n 1); cat "${R}upscale_ab.txt" | tee -a "$LOG"
  echo "images: $R" | tee -a "$LOG"
fi

echo "" | tee -a "$LOG"
echo "== 2. sustained, ${MINUTES} min per arm: $ARMS ==" | tee -a "$LOG"
i=0
for arm in $ARMS; do
  f=${arm%%:*}; w=${arm##*:}; i=$((i + 1))
  unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
  sleep 60
  echo "-- arm $i: $f @ $w   load: $(uptime | sed 's/.*load averages*: //')" | tee -a "$LOG"
  caffeinate -dims open -n -W "$APP" --args -sustained -motion orbit -gateres 0 -shadowlevel 0 \
    -minutes "$MINUTES" -tag "up_${i}_${f}${w}" -upscale "$f" -internalw "$w"
  S=$(ls -td "$DATA/Sustained"/*_up_${i}_${f}${w}/ | head -n 1)
  grep -E "^upscale|clock PINNED|^ +[0-9]|FIRST|LAST|WORST|fluid CA" "${S}sustained.txt" | tee -a "$LOG"
done
echo "" | tee -a "$LOG"; echo "log: $LOG"
