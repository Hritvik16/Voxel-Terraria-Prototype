#!/bin/bash
# run-cost-split.sh -- task 57 (engine plan step 7): where a play frame's ms go.
# -upscalerclips -costsplit at the tg2 world, quality preset + far band + far shadows
# (as ./run-play.sh), FSR 3 pinned at 540 rows, fullscreen 1920x1080 (as task 51).
# One launch: FULL, then per arm X X FULL (ABBA), each 3 s settle + 20 s timed on an
# 8 s piece of MODE=walk's circle, streaming pinned (PlaygroundCapture.CostSplit.cs).
# Warm-up first (PREAMBLE rule 9):
#   BUILD_ONLY=1 ./run-cost-split.sh
#   MODE=stream MOTION=walk ./run-tg2world.sh      (warm-up, numbers discarded)
#   SKIP_BUILD=1 ./run-cost-split.sh               (straight after)
# Log: Logs/costsplit_<ts>.txt; stills: <run>/stills/<arm>.png.
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
    -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/costsplit_scene.log
  unity_hold_wait
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/costsplit_build.log
  if grep -q "Shader error\|error CS" Logs/costsplit_build.log Logs/costsplit_scene.log; then
    echo "BUILD ERROR:"; grep -hE "Shader error|error CS" Logs/costsplit_build.log Logs/costsplit_scene.log | sort -u | head; exit 1
  fi
  [ -d "$APP" ] || { echo "BUILD FAILED"; tail -n 40 Logs/costsplit_build.log; exit 1; }
  echo "build ok"
fi
[ "${BUILD_ONLY:-0}" = "1" ] && { echo DONE; exit 0; }
unity_hold_wait
PWR0="$(pmset -g batt | tr '\n' ' ')"; HOLD0=$([ -f /tmp/voxel-unity.hold ] && echo yes || echo no)
STAMP=Logs/.costsplit_start; touch "$STAMP"
caffeinate -dims open -n -W "$APP" --args -world tg2 -upscalerclips -costsplit -screen-fullscreen 1 -screen-width 1920 -screen-height 1080 \
  -renderpreset quality -farband 1 -farshadow 1 -upscaler fsr3 -renderrows 540 -cleardeltas ${EXTRA:-}
PWR1="$(pmset -g batt | tr '\n' ' ')"; HOLD1=$([ -f /tmp/voxel-unity.hold ] && echo yes || echo no)
R=$(find "$DATA/UpscalerClips" -mindepth 1 -maxdepth 1 -type d -newer "$STAMP" | sort | tail -n 1)
OUT="Logs/costsplit_$(basename "$R").txt"
cp "$R/costsplit.txt" "$OUT" 2>/dev/null || { echo "no costsplit.txt in $R" > "$OUT"; cp "$R/upscalerclips.txt" "${OUT%.txt}_partial.txt" 2>/dev/null; }
{ echo; echo "power before: $PWR0"; echo "power after:  $PWR1"; echo "owner hold file before/after: $HOLD0/$HOLD1"; } >> "$OUT"
[ "$HOLD0$HOLD1" != "nono" ] && echo "NOTE: owner paused during this run" >> "$OUT"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
grep -E "Exception|RecreateSurface" "$PL" | head -5 >> "$OUT"
echo "report: $OUT   data: $R"
echo DONE
