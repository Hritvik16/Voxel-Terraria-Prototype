#!/bin/bash
# Task 209: fluid pockets ON, 203 lobes (-pocketshore 0) vs the coast-rule shore, same build, same poses.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
O=Logs/overnight/209
# side views from the north (196's offsets: camera = centre + (-6, +8, +18) m), then one from above per fluid
P="side_lava|5478.1,566.0,3560.0|5492.1,560.0,3566.1;side_water|5040.2,535.0,5335.0|5040.2,519.1,5305.9"
P="$P;top_water|5040.3,541.1,5301.0|5040.3,519.1,5306.0;top_lava|5492.1,582.0,3561.1|5492.1,560.0,3566.1;top_oil|3348.1,687.5,2898.0|3348.1,665.5,2903.0;top_acid|5348.5,584.3,3191.2|5348.5,562.3,3196.2"
P="$P;top_sludge|4578.5,562.4,5196.8|4578.5,540.4,5201.8;top_honey|6070.0,538.8,5306.5|6070.0,516.8,5311.5;top_quicksand|2800.8,652.1,3745.8|2800.8,630.1,3750.8"
P="$P;side_oil|3348.1,679.5,2927.0|3348.1,665.5,2903.0;side_sludge|4578.5,554.4,5225.8|4578.5,540.4,5201.8"
SB=${SB:-0}
run() {  # name flags
  echo "=== $1  $(date +%T)"
  SKIP_BUILD=$SB PRESETS=high EXTRA="-worldseed 42 -worldsurvey 1 -mapversion 4 -wsarms native -lookwait 35 -wsposes fixed -wsfixed $P -tod 12 -fluidpockets 1 $2" ./run-look-review.sh > $O/look_$1.out 2>&1
  SB=1
  cp "$PL" $O/player_$1.log
  grep -h 'BUILD\|build OK\|shots:' $O/look_$1.out | tail -3
  R=$(grep -h "shots:" $O/look_$1.out | tail -1 | sed "s/^shots: //"); echo "dir: $R"
  for f in "$R"/ws_fixed_*_native.png; do n=$(basename "$f" _native.png); n=${n#ws_fixed_}; cp "$f" "Screenshots/209/${n}_$1.png"; done
  cp "$R/lookreview.txt" "Screenshots/209/lookreview_$1.txt"
  grep -h "FluidPockets\] plane\|tg2 chunk source" $O/player_$1.log | cut -c1-700 | head -3
  grep -h '\[mem\]\|\[refused\]' $O/look_$1.out | tail -2 | cut -c1-400
  grep -h "WSPOSE\|\[shells\]\|missing" "$R/lookreview.txt" | cut -c1-300
}
run before "-pocketshore 0"
run after ""
echo "=== ALL DONE $(date +%T)"
