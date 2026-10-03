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
RUN_START=$(date +%s)   # task 14: tools/heavy.sh overlap checks owner pauses since here
echo "run $RUN_ID" | tee -a "$LOG"
# 2026-10-03 (task 12): log power source + charge at start and end, and keep
# the Mac awake while this script lives. The player is launched with `open -n`,
# which returns at once, so caffeinate waits on THIS script's pid (-w $$), which
# outlives the player (the footprint loop below waits for it). Battery-critical
# hibernation still wins over caffeinate -- hence the loud warning.
caffeinate -dimsu -w $$ &
echo "caffeinate pid $! (-dimsu -w $$)" | tee -a "$LOG"
tools/power-status.sh start | tee -a "$LOG"
if [ "${PIPESTATUS[0]}" != 0 ] && { [ "${MODE:-poses}" = "walk" ] || [ "${MODE:-poses}" = "orbit" ]; }; then
  echo "WARNING: ON BATTERY -- frame times from this run are not comparable to AC runs (task 08: arm 6 at ~1% ran at half rate)" | tee -a "$LOG"
fi
if [ "${BUILD:-0}" = "1" ]; then
  rm -rf "$APP"
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/tg2world_scene.log
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/tg2world_build.log
  if grep -q "error CS" Logs/tg2world_build.log Logs/tg2world_scene.log; then grep -h "error CS" Logs/tg2world_*.log | sort -u | tee -a "$LOG"; exit 1; fi
  if grep -q "Shader error" Logs/tg2world_build.log; then grep "Shader error" Logs/tg2world_build.log | tee -a "$LOG"; exit 1; fi
  [ -d "$APP" ] || { echo "BUILD FAILED" | tee -a "$LOG"; tools/heavy.sh overlap "$RUN_START" | tee -a "$LOG"; exit 1; }
  echo "build OK (0 error CS, 0 Shader error)" | tee -a "$LOG"
fi
sleep 30
# MODE=poses (default): the far-band preview's poses + eye level, photographed.
# MODE=orbit: -sustained in-air orbit (-orbitheight 8) for MINUTES, Performance
# preset (the default an interactive session gets), frame time + memory.
# MODE=walk (2026-10-01): -sustained -motion walk -- the same circle (WALKR m,
# default 150) at a walking player's eye, 1.6 m above the ground, at 5 m/s,
# Performance preset. What a player on foot sees; no earlier run measured it.
# MODE=shimmer (2026-10-02, TAA task 05): -nearshimmer -- the near-terrain
# quarter-pixel nudge test at the default 960 (Quality) preset, eye-level and
# slope poses; tools/nearshimmer-report.py writes Logs/nearshimmer_<run>.txt.
# MODE=taarisk (2026-10-02, TAA task 07): -taarisk -- edit, fluid, spin, fly
# and sun-step frame sequences; run with EXTRA="-taa 0" and "-taa 1" and
# compare with tools/taarisk-report.py. TAAWORLD= (empty) runs the default
# Playground world instead: the edit and fluid checks at the fluid arena.
# MODE=fluidprobe (2026-10-03, task 09): -fluidprobe -- task 07's tg2 fluid
# placement with every sim stage logged (tiles, mirror, wake, readback, GPU
# counters). PROBEWORLD= (empty) runs the default world's arena as control.
# MODE=stream (2026-10-03, task 10): -streamprobe -- the streaming baseline.
# MOTION=fly (default: straight ping-pong over the island, 56 m/s) or walk
# (the MODE=walk circle); per-stage chunk timings, upload bytes, deficit, and
# still vs moving shots of one pose. Performance preset, like MODE=walk.
if [ "${MODE:-poses}" = "orbit" ]; then
  open -n "$APP" --args -world tg2 -sustained -motion orbit -orbitheight "${ORBITH:-40}" -minutes "${MINUTES:-3}" \
    -gateres 0 -renderpreset performance -farshadow 1 -cleardeltas -tag "tg2orbit_${RUN_ID}" ${EXTRA:-}
elif [ "${MODE:-poses}" = "shimmer" ]; then
  START_MARK=$(mktemp)   # only a report folder written by THIS run counts
  open -n "$APP" --args -world tg2 -nearshimmer -renderpreset quality -farshadow 1 -cleardeltas ${EXTRA:-}
elif [ "${MODE:-poses}" = "taarisk" ]; then
  START_MARK=$(mktemp)
  open -n "$APP" --args ${TAAWORLD--world tg2} -taarisk -renderpreset quality -farshadow 1 -cleardeltas ${EXTRA:-}
elif [ "${MODE:-poses}" = "fluidprobe" ]; then
  START_MARK=$(mktemp)
  open -n "$APP" --args ${PROBEWORLD--world tg2} -fluidprobe -renderpreset quality -farshadow 1 -cleardeltas ${EXTRA:-}
elif [ "${MODE:-poses}" = "stream" ]; then
  START_MARK=$(mktemp)
  open -n "$APP" --args -world tg2 -streamprobe -streammotion "${MOTION:-fly}" -gateres 0 -renderpreset performance \
    -farshadow 1 -cleardeltas -tag "stream${MOTION:-fly}_${RUN_ID}" ${EXTRA:-}
elif [ "${MODE:-poses}" = "flysweep" ]; then
  # 2026-10-03 (task 13): still-pose height x pitch sweep at task 10's fly pose.
  START_MARK=$(mktemp)
  open -n "$APP" --args -world tg2 -flysweep -gateres 0 -renderpreset performance \
    -farshadow 1 -cleardeltas -tag "flysweep_${RUN_ID}" ${EXTRA:-}
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
elif [ "${MODE:-poses}" = "taarisk" ]; then
  R=$(find "$DATA/FarBand" -maxdepth 1 -type d -name '*_taarisk_*' -newer "$START_MARK" 2>/dev/null | sort | tail -n 1)
  if [ -n "$R" ] && [ -f "$R/taarisk.txt" ]; then cat "$R/taarisk.txt" | tee -a "$LOG"; echo "shots: $R" | tee -a "$LOG"; else echo "NO REPORT" | tee -a "$LOG"; fi
elif [ "${MODE:-poses}" = "stream" ]; then
  R=$(find "$DATA/StreamProbe" -maxdepth 1 -type d -name "*_stream*_${RUN_ID}" -newer "$START_MARK" 2>/dev/null | sort | tail -n 1)
  if [ -n "$R" ] && [ -f "$R/streamprobe.txt" ]; then cat "$R/streamprobe.txt" | tee -a "$LOG"; echo "shots: $R" | tee -a "$LOG"; else echo "NO REPORT" | tee -a "$LOG"; fi
elif [ "${MODE:-poses}" = "flysweep" ]; then
  R=$(find "$DATA/FlySweep" -maxdepth 1 -type d -name "*_flysweep_${RUN_ID}" -newer "$START_MARK" 2>/dev/null | sort | tail -n 1)
  if [ -n "$R" ] && [ -f "$R/flysweep.txt" ]; then cat "$R/flysweep.txt" | tee -a "$LOG"; echo "shots: $R" | tee -a "$LOG"; else echo "NO REPORT" | tee -a "$LOG"; fi
elif [ "${MODE:-poses}" = "fluidprobe" ]; then
  R=$(find "$DATA/FarBand" -maxdepth 1 -type d -name '*_fluidprobe_*' -newer "$START_MARK" 2>/dev/null | sort | tail -n 1)
  if [ -n "$R" ] && [ -f "$R/fluidprobe.txt" ]; then cat "$R/fluidprobe.txt" | tee -a "$LOG"; echo "shots: $R" | tee -a "$LOG"; else echo "NO REPORT" | tee -a "$LOG"; fi
elif [ "${MODE:-poses}" = "shimmer" ]; then
  R=$(find "$DATA/FarBand" -maxdepth 1 -type d -name '*_nearshimmer' -newer "$START_MARK" 2>/dev/null | sort | tail -n 1)
  [ -n "$R" ] && R="$R/"
  if [ -n "$R" ] && [ -f "${R}nearshimmer.txt" ]; then
    { cat "${R}nearshimmer.txt"; python3 tools/nearshimmer-report.py "$R"; } > "Logs/nearshimmer_${RUN_ID}.txt" 2>&1
    tee -a "$LOG" < "Logs/nearshimmer_${RUN_ID}.txt"; echo "report: Logs/nearshimmer_${RUN_ID}.txt  shots: $R" | tee -a "$LOG"
  else echo "NO REPORT" | tee -a "$LOG"; fi
else
  R=$(ls -td "$DATA/FarBand"/*_world/ 2>/dev/null | head -n 1)
  if [ -n "$R" ] && [ -f "${R}farpreview.txt" ]; then cat "${R}farpreview.txt" | tee -a "$LOG"; echo "shots: $R" | tee -a "$LOG"; else echo "NO REPORT" | tee -a "$LOG"; fi
fi
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
grep -E "VERTICAL STREAMING|PrimeWindow:|Total startup|tg2 WORLD|Exception|exhausted|error" "$PL" | head -20 | tee -a "$LOG"
tools/power-status.sh end | tee -a "$LOG"
if [ "${PIPESTATUS[0]}" != 0 ] && { [ "${MODE:-poses}" = "walk" ] || [ "${MODE:-poses}" = "orbit" ]; }; then
  echo "WARNING: ON BATTERY at the end of the run" | tee -a "$LOG"
fi
# 2026-10-03 (task 14): an owner pause (tools/heavy.sh pause) that overlapped
# this run voids its timings; a `pause --kill` voids the run (exit 3).
tools/heavy.sh overlap "$RUN_START" | tee -a "$LOG"
PAUSE_RC=${PIPESTATUS[0]}
echo "log: $LOG"
echo TG2WORLD_DONE
[ "$PAUSE_RC" = 3 ] && exit 3
exit 0
