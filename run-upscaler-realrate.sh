#!/bin/bash
# run-upscaler-realrate.sh -- task 51: -upscalerclips -realrate at the tg2 world,
# quality preset + far shadows, fullscreen 1920x1080 (as task 50's clips). One launch:
# the lit paths are picked, every arm is TIMED per path (warm, ABBA, no capture), then
# each arm is recorded at its own measured p50 step (PlaygroundCapture.UpscalerRealRate.cs).
# A background loop encodes each finished path: rr_<path> -> Screenshots/Engine03/clips_realrate/
# (60 fps sample-and-hold, tools/upscaler-realrate-encode.sh), <path>_lit ->
# Screenshots/Engine03/clips/ (30 fps, grid + crop, tools/upscaler-clips-encode.sh).
# Log: Logs/upscalerrealrate_<ts>.txt (+ _encode.txt). Warm-up first (PREAMBLE rule 9):
#   BUILD_ONLY=1 ./run-upscaler-realrate.sh
#   MODE=stream MOTION=walk ./run-tg2world.sh        (warm-up, numbers discarded)
#   SKIP_BUILD=1 ./run-upscaler-realrate.sh          (straight after)
set -uo pipefail
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Playground.app"
DATA="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap"
OUTR=Screenshots/Engine03/clips_realrate
OUTV=Screenshots/Engine03/clips
DIRS="rr_walk_cliff rr_spin_stop rr_fly walk_cliff_lit pan_horizon_lit"
mkdir -p Logs "$OUTR" "$OUTV"
[ Logs/frames-to-mp4 -nt tools/frames-to-mp4.swift ] || swiftc -O tools/frames-to-mp4.swift -o Logs/frames-to-mp4 2>/dev/null \
  || { echo "swiftc failed"; exit 1; }
source tools/unity-lock.sh
unity_lock_acquire
if [ "${SKIP_BUILD:-0}" != "1" ]; then
  rm -rf "$APP"
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" \
    -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/upscalerrealrate_scene.log
  unity_hold_wait
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/upscalerrealrate_build.log
  if grep -q "Shader error\|error CS" Logs/upscalerrealrate_build.log; then
    echo "BUILD ERROR:"; grep -E "Shader error|error CS" Logs/upscalerrealrate_build.log | head; exit 1
  fi
  [ -d "$APP" ] || { echo "BUILD FAILED"; tail -n 40 Logs/upscalerrealrate_build.log; exit 1; }
  echo "build ok"
fi
[ "${BUILD_ONLY:-0}" = "1" ] && { echo DONE; exit 0; }
unity_hold_wait
PWR0="$(pmset -g batt | tr '\n' ' ')"; HOLD0=$([ -f /tmp/voxel-unity.hold ] && echo yes || echo no)
STAMP=Logs/.upscalerrealrate_start; EXITED=Logs/.upscalerrealrate_exited
touch "$STAMP"; rm -f "$EXITED"
ENCLOG=Logs/upscalerrealrate_encode_tmp.txt; : > "$ENCLOG"
( R=""
  while :; do
    ex=0; [ -f "$EXITED" ] && ex=1
    [ -z "$R" ] && R=$(find "$DATA/UpscalerClips" -mindepth 1 -maxdepth 1 -type d -newer "$STAMP" 2>/dev/null | sort | tail -n 1)
    if [ -n "$R" ]; then
      for p in $DIRS; do
        { [ -f "$R/encoded_$p" ] || [ -f "$R/failed_$p" ] || [ ! -f "$R/$p/DONE" ]; } && continue
        echo "[$(date +%T)] encoding $p (free $(df -h . | awk 'NR==2{print $4}'), raw $(du -sh "$R" | cut -f1))"
        case $p in
          rr_*) tools/upscaler-realrate-encode.sh "$R/$p" "$OUTR" ;;
          *)    NOSINGLES=1 tools/upscaler-clips-encode.sh "$R/$p" "$OUTV" ;;
        esac && touch "$R/encoded_$p" || touch "$R/failed_$p"
      done
    fi
    [ $ex = 1 ] && break
    sleep 5
  done ) >> "$ENCLOG" 2>&1 &
ENCPID=$!
caffeinate -dims open -n -W "$APP" --args -world tg2 -upscalerclips -realrate -screen-fullscreen 1 -screen-width 1920 -screen-height 1080 -renderpreset quality -farshadow 1 -cleardeltas ${EXTRA:-}
touch "$EXITED"
PWR1="$(pmset -g batt | tr '\n' ' ')"; HOLD1=$([ -f /tmp/voxel-unity.hold ] && echo yes || echo no)
wait $ENCPID
R=$(find "$DATA/UpscalerClips" -mindepth 1 -maxdepth 1 -type d -newer "$STAMP" | sort | tail -n 1)
TS=$(basename "$R")
OUT="Logs/upscalerrealrate_${TS}.txt"
cp "$R/upscalerclips.txt" "$OUT" 2>/dev/null || echo "no upscalerclips.txt in $R" > "$OUT"
{ echo; echo "power before: $PWR0"; echo "power after:  $PWR1"; echo "owner hold file before/after: $HOLD0/$HOLD1"; } >> "$OUT"
[ "$HOLD0$HOLD1" != "nono" ] && echo "NOTE: owner paused during this run" >> "$OUT"
mv "$ENCLOG" "Logs/upscalerrealrate_${TS}_encode.txt"
cp "$OUT" "$OUTR/upscalerrealrate.txt"
echo "report: $OUT   encoder: Logs/upscalerrealrate_${TS}_encode.txt   videos: $OUTR $OUTV   data: $R"
ls "$R"
echo DONE
