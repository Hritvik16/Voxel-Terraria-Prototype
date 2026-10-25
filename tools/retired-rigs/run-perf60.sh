#!/bin/bash
# run-perf60.sh -- the owner's target, 2026-09-26: "performance should be 60 fps"
# ("ideally 60 fps at 1080p"). Sustained in-air orbit (-orbitheight 8, the
# arm CLAUDE.md says to quote), far band + far shadows ON (the demo), golden
# hour, 1920x1080 fullscreen RELEASE, MINUTES per arm, counterbalanced:
#   S     as shipped: gate 960x540 + nearest blit
#   N     S with shadows OFF (rung 3): how much of the frame is the sun ray
#   F640  URP FSR 1.0 to 1080p from 640x360 (Native gate at render scale)
#   F480  URP FSR 1.0 to 1080p from 480x270
#   TS / T640 / T480 / T560   the same with TEMPORAL SHADOW REUSE (-tshadow 1)
#   TN480 T480 without the sky-probe ambient ray (-noskyprobe)
#   PP / PB   the Playground's OWN presets (RenderPreset.Apply via
#             -renderpreset performance|balanced): the code path an interactive
#             run takes, not the -upscale measurement path
#   PPd / PBd the same, pitched 12 deg DOWN at the ground (more terrain in frame)
#   PPnf / PPnh  PP without live fluid / without the HUD (isolating the >33 ms tail)
#   PPspin / PPstatic  PP rotating in place / held still (is the tail TRANSLATION?)
# Order S N F640 F480 F480 F640 N S. Uses the existing Builds/Playground.app
# unless BUILD=1. EXTRA="-frametiming" adds flags to every arm. Takes the
# shared Unity lock.
set -uo pipefail
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Playground.app"
DATA="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap"
MINUTES="${MINUTES:-3}"
ORDER="${ORDER:-S N F640 F480 F480 F640 N S}"
RUN_ID="$(date +%Y%m%d_%H%M%S)"
mkdir -p Logs
LOG="Logs/perf60_${RUN_ID}.txt"
ARMS="Logs/perf60_${RUN_ID}_arms.tsv"
: > "$ARMS"
source tools/unity-lock.sh
unity_lock_acquire
echo "run $RUN_ID  order: $ORDER  minutes/arm: $MINUTES" | tee -a "$LOG"
if [ "${BUILD:-0}" = "1" ]; then
  rm -rf "$APP"
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/perf60_scene.log
  unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/perf60_build.log
  if grep -q "error CS" Logs/perf60_build.log Logs/perf60_scene.log; then grep -h "error CS" Logs/perf60_*.log | sort -u | tee -a "$LOG"; exit 1; fi
  if grep -q "Shader error" Logs/perf60_build.log; then grep "Shader error" Logs/perf60_build.log | tee -a "$LOG"; exit 1; fi
  [ -d "$APP" ] || { echo "BUILD FAILED" | tee -a "$LOG"; exit 1; }
  echo "build OK (0 error CS, 0 Shader error)" | tee -a "$LOG"
fi
arm() {  # arm <label> <extra args...>
  local label="$1"; shift
  local tag="p60_${RUN_ID}_${label}"
  unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
  sleep 60
  echo "-- arm $label  load: $(uptime | sed 's/.*load averages*: //')" | tee -a "$LOG"
  caffeinate -dims open -n -W "$APP" --args -sustained -motion orbit -gateres 960 -shadowlevel 0 \
    -minutes "$MINUTES" -orbitheight 8 -farband 1 -farshadow 1 -tag "$tag" ${EXTRA:-} "$@"
  local S; S=$(ls -td "$DATA/Sustained"/*_"$tag"/ 2>/dev/null | head -n 1)
  if [ -n "$S" ]; then
    printf "%s\t%s\t%s\n" "perf60" "$label" "$S" >> "$ARMS"
    grep -E "upscale|FIRST|LAST|WORST|SHADOW RAYS|render |FrameTimingManager" "${S}sustained.txt" | tee -a "$LOG"
  else
    echo "   NO OUTPUT for $tag" | tee -a "$LOG"
  fi
}
for label in $ORDER; do
  case "$label" in
    S) arm S ;;
    N) arm N -shadowlevel 3 ;;
    F640) arm F640 -upscale fsr -internalw 640 ;;
    F480) arm F480 -upscale fsr -internalw 480 ;;
    F560) arm F560 -upscale fsr -internalw 560 ;;
    L640) arm L640 -upscale linear -internalw 640 ;;
    TS) arm TS -tshadow 1 ;;
    T640) arm T640 -upscale fsr -internalw 640 -tshadow 1 ;;
    T480) arm T480 -upscale fsr -internalw 480 -tshadow 1 ;;
    T560) arm T560 -upscale fsr -internalw 560 -tshadow 1 ;;
    PP) arm PP -gateres 0 -renderpreset performance ;;
    PB) arm PB -gateres 0 -renderpreset balanced ;;
    PPspin) arm PPspin -gateres 0 -renderpreset performance -motion spin ;;
    PPstatic) arm PPstatic -gateres 0 -renderpreset performance -motion static ;;
    PPnf) arm PPnf -gateres 0 -renderpreset performance -nofluid ;;
    PPnh) arm PPnh -gateres 0 -renderpreset performance -nohud ;;
    PPd) arm PPd -gateres 0 -renderpreset performance -orbitpitch 12 ;;
    PBd) arm PBd -gateres 0 -renderpreset balanced -orbitpitch 12 ;;
    TN480) arm TN480 -upscale fsr -internalw 480 -tshadow 1 -noskyprobe ;;
    *) echo "unknown arm $label" | tee -a "$LOG" ;;
  esac
done
[ -s "$ARMS" ] && python3 tools/report-farband.py "$ARMS" | tee -a "$LOG"
echo "log: $LOG" | tee -a "$LOG"
echo PERF60_DONE
