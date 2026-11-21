#!/bin/bash
# Task 201: fluid pockets ON by default -- new default vs -fluidpockets 0 (the pre-201 world), 196's water + lava pockets,
# a forest water pocket (FluidPocketTests.ForestSites_Poses201) and 195a's ordinary forest pose, seed 42, midday.
# One build, one launch per arm (the flag is per launch).
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
O=Logs/overnight/201
P="surface_water|5040.2,535.0,5335.0|5040.2,519.1,5305.9;surface_lava|5478.1,566.0,3560.0|5492.1,560.0,3566.1;forest_water|5138.8,549.7,4915.2|5138.8,533.7,4886.9;forest|3366.4,544.2,4973.0|3366.4,530.7,4928.0"
SB=${SB:-0}
mkdir -p Screenshots/201 $O
run() {  # name flags
  echo "=== $1  $(date +%T)"
  SKIP_BUILD=$SB PRESETS=high EXTRA="-worldseed 42 -worldsurvey 1 -mapversion 4 -wsarms native -lookwait 40 -wsposes fixed -wsfixed $P -tod 12 $2" ./run-look-review.sh > $O/look_$1.out 2>&1
  SB=1
  cp "$PL" $O/player_$1.log
  grep -h 'BUILD\|build OK\|shots:' $O/look_$1.out | tail -3
  R=$(grep -h "shots:" $O/look_$1.out | tail -1 | sed "s/^shots: //"); echo "dir: $R"
  for f in "$R"/ws_fixed_*_native.png; do n=$(basename "$f" _native.png); n=${n#ws_fixed_}; cp "$f" "Screenshots/201/${n}_$1.png"; done
  cp "$R/lookreview.txt" "Screenshots/201/lookreview_$1.txt"
  grep -h "tg2 chunk source\|FluidPockets\] plane\|POOL FULL" $O/player_$1.log | cut -c1-700 | head -4
  grep -h "WSPOSE\|\[shells\]\|\[mem\]\|\[fluidtiles\]\|missing" "$R/lookreview.txt" | cut -c1-400
}
run default ""
run off "-fluidpockets 0"
echo "=== ALL DONE $(date +%T)"
