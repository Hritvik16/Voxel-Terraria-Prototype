#!/bin/bash
# task 225: shade-term probe launches (cost split, stills only). usage: run.sh <launch> [extra flags]
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
D=Logs/overnight/225; L=$1; shift
P="$(tr ';' '\n' < Logs/TerrainGen/trees210/poses.txt | grep -E '^(forest_eye|jungle_eye)\|' | paste -sd';' -);cave_mouth|4306.0,854.0,3892.0|4328.0,852.0,3882.0"
source tools/play-flags.sh
BASE="$PLAY_SETTINGS_FLAGS -quality high -renderrows 432 -shellwait 60 -costtimed 1 -shadeprobe 1"
case $L in
  day*)   X="-costfixed $P";;
  night*) X="-costpaths torch";;
esac
echo "== $L start $(date +%T)"
SKIP_BUILD=1 EXTRA="$BASE $X $*" ./run-cost-split.sh > $D/launch_$L.out 2>&1
echo "== $L end $(date +%T)"; tail -2 $D/launch_$L.out
R=$(grep -o 'data: .*' $D/launch_$L.out | sed 's/^data: //')
[ -n "$R" ] && { mkdir -p Screenshots/225/$L; cp "$R"/stills/*.png Screenshots/225/$L/ 2>/dev/null; cp "$R/costsplit.txt" $D/costsplit_$L.txt; }
cp "$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log" $D/player_$L.log
echo RUN_DONE
