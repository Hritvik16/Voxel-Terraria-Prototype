#!/bin/bash
# Task 209b: pond streams OFF (-pocketstreams 0) vs ON (default), same build, same poses (FluidPocketTests.Streams poses.txt).
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
O=Logs/overnight/209b
P="oil_shore|2858.4,641.9,3428.7|2851.0,632.9,3412.1;water_shore|3303.9,535.3,2163.4|3298.5,525.1,2181.0;water_middle|3308.8,540.7,2194.1|3299.4,531.7,2209.9;acid_shore|5425.7,561.0,4383.4|5443.4,551.5,4378.3;lava_shore|5343.6,562.0,4335.0|5347.4,553.0,4317.1;lava_middle|5317.4,554.7,4375.5|5331.9,545.7,4364.2;honey_shore|5897.0,534.4,5289.1|5911.4,525.3,5300.8;sludge_shore|4566.5,549.4,5212.4|4579.5,540.4,5199.5;aerial_water|3299.4,601.7,2184.8|3299.4,531.7,2209.9"
SB=${SB:-0}
run() {  # name flags
  echo "=== $1  $(date +%T)"
  SKIP_BUILD=$SB PRESETS=high EXTRA="-worldseed 42 -worldsurvey 1 -mapversion 4 -wsarms native -lookwait 35 -wsposes fixed -wsfixed $P -tod 12 -fluidpockets 1 $2" ./run-look-review.sh > $O/look_$1.out 2>&1
  SB=1
  cp "$PL" $O/player_$1.log
  grep -h 'BUILD\|build OK\|shots:' $O/look_$1.out | tail -3
  R=$(grep -h "shots:" $O/look_$1.out | tail -1 | sed "s/^shots: //"); echo "dir: $R"
  for f in "$R"/ws_fixed_*_native.png; do n=$(basename "$f" _native.png); n=${n#ws_fixed_}; cp "$f" "Screenshots/209b/${n}_$1.png"; done
  cp "$R/lookreview.txt" "Screenshots/209b/lookreview_$1.txt"
  grep -h "FluidPockets\] plane" $O/player_$1.log | cut -c1-900 | head -2
  grep -h '\[mem\]\|\[refused\]' $O/look_$1.out | tail -2 | cut -c1-400
  grep -h "WSPOSE\|\[shells\]\|missing" "$R/lookreview.txt" | cut -c1-300
}
run before "-pocketstreams 0"
run after ""
echo "=== ALL DONE $(date +%T)"
