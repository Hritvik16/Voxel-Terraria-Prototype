#!/bin/bash
# Task 243: solid crown cores. ONE build, arms OFF (default) vs -crowncore 2 (closed crown), noon and 16 deg morning
# (-tod 7.1 = SunCycle.HoursForElevation(16, morning), tilt 0.25), interleaved A B B A. Seed 42, -gpupoolprobe 1 (ledger).
# Poses: forest_eye (outside the crowns), oak_under (1.7 m over the ground under an oak's crown, looking up),
# oak_inside (inside that oak's crown, root 3324.8, 516.8, 4894.4 m: CrownCoreWorldTests' oak).
# Mode 1 (-crowncore 1) is not shot: it changes 1 leaf brick of 13,036 in the footprint (Logs/TerrainGen/trees243/histogram.txt).
# Second pass (243_shots2): -crowncore 2 rebuilt with keepBricks (no new prototype bricks); the first pass (full interior bricks) is in Screenshots/243/full_bricks.
# Third pass (243_shots3): the 16 deg arms again with -wshours 7.1 (the survey ignored -tod: its 16 deg shots were noon; kept in Screenshots/243/not_16deg).
# Fourth pass (243_shots4): off_16 again (the third pass's off_16 player quit after ~27 s with no shot).
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
O=Logs/overnight/243
mkdir -p $O Screenshots/243
P="forest_eye|3366.4,532.5,4950.0|3366.4,529.7,4928.0;oak_under|3327.8,518.5,4897.4|3324.8,527.0,4894.4;oak_inside|3326.0,527.5,4895.6|3320.0,527.0,4890.0"
SB=${SB:-1}
run() {  # tag flags
  echo "=== $1  $(date +%T)"
  SKIP_BUILD=$SB PRESETS=high EXTRA="-worldseed 42 -worldsurvey 1 -mapversion 4 -wsarms native -lookwait ${LW:-30} -wsposes fixed -wsfixed $P -gpupoolprobe 1 $2" ./run-look-review.sh > $O/look_$1.out 2>&1
  SB=1
  cp "$PL" $O/player_$1.log
  grep -h 'BUILD\|build OK\|error CS' $O/look_$1.out | tail -2
  R=$(grep -h "shots:" $O/look_$1.out | tail -1 | sed "s/^shots: //"); echo "dir: $R"
  for f in "$R"/ws_fixed_*.png; do n=$(basename "$f" .png); n=${n#ws_fixed_}; n=${n%_native}; cp "$f" "Screenshots/243/${n}_$1.png"; done
  cp "$R/lookreview.txt" "Screenshots/243/lookreview_$1.txt"
  grep -h "\[Trees2\] library\|crown cores" $O/player_$1.log | cut -c1-400 | head -2
  grep -h '\[mem\]\|\[refused\]' $O/look_$1.out | tail -2 | cut -c1-400
}
run off_16 "-wshours 7.1"
echo "=== ALL DONE $(date +%T)"
