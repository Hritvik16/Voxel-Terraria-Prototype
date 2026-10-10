#!/bin/bash
# run-upscaler-clips.sh -- task 50: -upscalerclips at the tg2 world, quality
# preset + far shadows (as task 48). One launch records six camera paths x six
# arms (PlaygroundCapture.UpscalerClips.cs); while it runs, a background loop
# here encodes each finished path (tools/upscaler-clips-encode.sh) into
# Screenshots/Engine03/clips/ and deletes its raw frames. The log goes to
# Logs/upscalerclips_<ts>.txt (+ the encoder's to Logs/upscalerclips_<ts>_encode.txt).
#   ./run-upscaler-clips.sh          build Playground + run
#   SKIP_BUILD=1 ./run-upscaler-clips.sh
set -uo pipefail
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Playground.app"
DATA="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap"
OUTV=Screenshots/Engine03/clips
PATHS="walk_cliff pan_horizon spin_stop fly water night_torches"
mkdir -p Logs "$OUTV"
[ Logs/frames-to-mp4 -nt tools/frames-to-mp4.swift ] || swiftc -O tools/frames-to-mp4.swift -o Logs/frames-to-mp4 2>/dev/null \
  || { echo "swiftc failed"; exit 1; }
source tools/unity-lock.sh
unity_lock_acquire
if [ "${SKIP_BUILD:-0}" != "1" ]; then
  rm -rf "$APP"
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
    -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/upscalerclips_scene.log
  unity_hold_wait
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/upscalerclips_build.log
  if grep -q "Shader error\|error CS" Logs/upscalerclips_build.log; then
    echo "BUILD ERROR:"; grep -E "Shader error|error CS" Logs/upscalerclips_build.log | head; exit 1
  fi
  [ -d "$APP" ] || { echo "BUILD FAILED"; tail -n 40 Logs/upscalerclips_build.log; exit 1; }
fi
unity_hold_wait
STAMP=Logs/.upscalerclips_start; EXITED=Logs/.upscalerclips_exited
touch "$STAMP"; rm -f "$EXITED"
ENCLOG=Logs/upscalerclips_encode_tmp.txt; : > "$ENCLOG"
# the encoder: every path whose DONE marker is up, once; ends after the player exits
( R=""
  while :; do
    ex=0; [ -f "$EXITED" ] && ex=1        # checked BEFORE the pass, so the last pass sees every DONE
    [ -z "$R" ] && R=$(find "$DATA/UpscalerClips" -mindepth 1 -maxdepth 1 -type d -newer "$STAMP" 2>/dev/null | sort | tail -n 1)
    if [ -n "$R" ]; then
      for p in $PATHS; do
        { [ -f "$R/encoded_$p" ] || [ -f "$R/failed_$p" ] || [ ! -f "$R/$p/DONE" ]; } && continue
        echo "[$(date +%T)] encoding $p (free $(df -h . | awk 'NR==2{print $4}'))"
        if tools/upscaler-clips-encode.sh "$R/$p" "$OUTV"; then touch "$R/encoded_$p"; else touch "$R/failed_$p"; fi
      done
    fi
    [ $ex = 1 ] && break
    sleep 5
  done ) >> "$ENCLOG" 2>&1 &
ENCPID=$!
caffeinate -dims open -n -W "$APP" --args -world tg2 -upscalerclips -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -renderpreset quality -farshadow 1 -cleardeltas ${EXTRA:-}
touch "$EXITED"
wait $ENCPID
R=$(find "$DATA/UpscalerClips" -mindepth 1 -maxdepth 1 -type d -newer "$STAMP" | sort | tail -n 1)
TS=$(basename "$R")
OUT="Logs/upscalerclips_${TS}.txt"
cp "$R/upscalerclips.txt" "$OUT" 2>/dev/null || echo "no upscalerclips.txt in $R" > "$OUT"
mv "$ENCLOG" "Logs/upscalerclips_${TS}_encode.txt"
cp "$OUT" "$OUTV/upscalerclips.txt"
echo "report: $OUT   encoder: Logs/upscalerclips_${TS}_encode.txt   videos: $OUTV   data: $R"
ls "$R"
echo DONE
