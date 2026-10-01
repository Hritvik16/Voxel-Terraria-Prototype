#!/bin/bash
# run-tg2world.sh -- V2 (design doc §15): the TerrainGen island as a REAL,
# vertically streamed world (-world tg2), photographed from the far-band
# preview's poses plus three eye-level poses, with the voxel band streaming
# around each one. Records the player's physical memory footprint every 10 s
# (macOS `footprint`, which counts Metal buffers too) against the owner's 3 GB
# cap. BUILD=1 rebuilds the player first. EXTRA passes flags through. Takes the
# shared Unity lock.
set -uo pipefail
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Playground.app"
DATA="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap"
RUN_ID="$(date +%Y%m%d_%H%M%S)"
mkdir -p Logs
LOG="Logs/tg2world_${RUN_ID}.txt"
MEM="Logs/tg2world_${RUN_ID}_mem.txt"
source tools/unity-lock.sh
unity_lock_acquire
echo "run $RUN_ID" | tee -a "$LOG"
if [ "${BUILD:-0}" = "1" ]; then
  rm -rf "$APP"
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/tg2world_scene.log
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/tg2world_build.log
  if grep -q "error CS" Logs/tg2world_build.log Logs/tg2world_scene.log; then grep -h "error CS" Logs/tg2world_*.log | sort -u | tee -a "$LOG"; exit 1; fi
  if grep -q "Shader error" Logs/tg2world_build.log; then grep "Shader error" Logs/tg2world_build.log | tee -a "$LOG"; exit 1; fi
  [ -d "$APP" ] || { echo "BUILD FAILED" | tee -a "$LOG"; exit 1; }
  echo "build OK (0 error CS, 0 Shader error)" | tee -a "$LOG"
fi
sleep 30
# MODE=poses (default): the far-band preview's poses + eye level, photographed.
# MODE=orbit: -sustained in-air orbit (-orbitheight 8) for MINUTES, Performance
# preset (the default an interactive session gets), frame time + memory.
# MODE=walk (2026-10-01): -sustained -motion walk -- the same circle (WALKR m,
# default 150) at a walking player's eye, 1.6 m above the ground, at 5 m/s,
# Performance preset. What a player on foot sees; no earlier run measured it.
if [ "${MODE:-poses}" = "orbit" ]; then
  open -n "$APP" --args -world tg2 -sustained -motion orbit -orbitheight "${ORBITH:-40}" -minutes "${MINUTES:-3}" \
    -gateres 0 -renderpreset performance -farshadow 1 -cleardeltas -tag "tg2orbit_${RUN_ID}" ${EXTRA:-}
elif [ "${MODE:-poses}" = "walk" ]; then
  open -n "$APP" --args -world tg2 -sustained -motion walk -orbitradius "${WALKR:-150}" -movespeed 5 -minutes "${MINUTES:-5}" \
    -gateres 0 -renderpreset performance -farshadow 1 -cleardeltas -tag "tg2walk_${RUN_ID}" ${EXTRA:-}
else
  open -n "$APP" --args -world tg2 -farpreview -farshadow 1 -cleardeltas ${EXTRA:-}
fi
sleep 5
BIN="$APP/Contents/MacOS/Voxel Terraria 1 Byte BrickMap"
PID=$(pgrep -nf "$BIN" || true)
echo "player pid ${PID:-none}" | tee -a "$LOG"
: > "$MEM"
# 2026-10-01: also keep `footprint`'s per-category breakdown (Malloc,
# IOAccelerator = Metal buffers, VM_ALLOCATE, ...) of the largest sample, so
# the peak can be attributed and not just reported.
MEMPEAK="${MEM%.txt}_peak_breakdown.txt"
PEAKMB=0
while [ -n "$PID" ] && kill -0 "$PID" 2>/dev/null; do
  FULL=$(footprint -p "$PID" 2>/dev/null)
  F=$(echo "$FULL" | grep -i "phys_footprint:" | head -1 | sed 's/.*phys_footprint: *//')
  echo "$(date +%T) $F" >> "$MEM"
  MB=$(echo "$F" | awk '{v=$1; u=$2; if (u=="GB") v*=1024; else if (u=="KB") v/=1024; printf "%.0f", v}')
  if [ -n "$MB" ] && [ "$MB" -gt "$PEAKMB" ] 2>/dev/null; then PEAKMB=$MB; { echo "$(date +%T) phys_footprint $F"; echo "$FULL"; } > "$MEMPEAK"; fi
  sleep 10
done
PEAK=$(awk '{v=$2; u=$3; if (u=="GB") v*=1024; else if (u=="KB") v/=1024; if (v>m) m=v} END{printf "%.0f MB", m}' "$MEM")
echo "peak phys_footprint: $PEAK   (owner cap 3 GB; samples in $MEM; breakdown at peak in $MEMPEAK)" | tee -a "$LOG"
if [ "${MODE:-poses}" = "orbit" ] || [ "${MODE:-poses}" = "walk" ]; then
  S=$(ls -td "$DATA/Sustained"/*_tg2${MODE}_${RUN_ID}/ 2>/dev/null | head -n 1)
  if [ -n "$S" ]; then cat "${S}sustained.txt" | tee -a "$LOG"; echo "shots: $S" | tee -a "$LOG"; else echo "NO REPORT" | tee -a "$LOG"; fi
else
  R=$(ls -td "$DATA/FarBand"/*_world/ 2>/dev/null | head -n 1)
  if [ -n "$R" ] && [ -f "${R}farpreview.txt" ]; then cat "${R}farpreview.txt" | tee -a "$LOG"; echo "shots: $R" | tee -a "$LOG"; else echo "NO REPORT" | tee -a "$LOG"; fi
fi
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
grep -E "VERTICAL STREAMING|PrimeWindow:|Total startup|tg2 WORLD|Exception|exhausted|error" "$PL" | head -20 | tee -a "$LOG"
echo "log: $LOG"
echo TG2WORLD_DONE
