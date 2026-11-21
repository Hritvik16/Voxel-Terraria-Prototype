#!/bin/bash
# task 225 (after the fix): look launches, stills only. usage: run2.sh <launch>   (day2 | night2)
# day2: walk_near + forest_eye, jungle_eye (task 219/221 poses) + cave_mouth (task 215's survey pose, 25 m out looking in)
#       arms FULL (both fixes), lookfix_off (= before), lookexact_off, skylinear_off.
# night2: task 151's torch scene (32 torches, night), FULL vs lookfix_off.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
D=Logs/overnight/225; L=$1; shift
P="$(tr ';' '\n' < Logs/TerrainGen/trees210/poses.txt | grep -E '^(forest_eye|jungle_eye)\|' | paste -sd';' -);cave_mouth|3130.8,634.6,3435.5|3112.0,635.0,3418.9"
source tools/play-flags.sh
BASE="$PLAY_SETTINGS_FLAGS -quality high -renderrows 432 -shellwait 60 -costtimed 1"
case $L in
  day*)   X="-costfixed $P -costarms lookfix_off,lookexact_off,skylinear_off";;
  night*) X="-costpaths torch -costarms lookfix_off";;
esac
echo "== $L start $(date +%T)"
SKIP_BUILD=1 EXTRA="$BASE $X $*" ./run-cost-split.sh > $D/launch_$L.out 2>&1
echo "== $L end $(date +%T)"; tail -2 $D/launch_$L.out
R=$(grep -o 'data: .*' $D/launch_$L.out | sed 's/^data: //')
[ -n "$R" ] && { mkdir -p Screenshots/225/$L; cp "$R"/stills/*.png Screenshots/225/$L/ 2>/dev/null; cp "$R/costsplit.txt" $D/costsplit_$L.txt; }
cp "$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log" $D/player_$L.log
echo RUN_DONE
