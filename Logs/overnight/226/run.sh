#!/bin/bash
# task 226: look-polish launches, stills only. usage: run.sh <launch>   (day | night)
# day: walk_near + forest_eye, jungle_eye (task 219/221 poses); arms FULL (all four pieces), lotl_off (= before 226),
#      vary_only, crevice_only, aerial_only, bloom_only (one piece each).
# night: task 151's torch scene (torch_camp, night), FULL vs lotl_off, bloom_only, crevice_only.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
D=Logs/overnight/226; L=$1; shift
P="$(tr ';' '\n' < Logs/TerrainGen/trees210/poses.txt | grep -E '^(forest_eye|jungle_eye)\|' | paste -sd';' -)"
source tools/play-flags.sh
BASE="$PLAY_SETTINGS_FLAGS -quality high -renderrows 432 -shellwait 60 -costtimed 1"
case $L in
  day*)   X="-costfixed $P -costarms lotl_off,vary_only,crevice_only,aerial_only,bloom_only";;
  night*) X="-costpaths torch -costarms lotl_off,bloom_only,crevice_only";;
esac
echo "== $L start $(date +%T)"
SKIP_BUILD=1 EXTRA="$BASE $X $*" ./run-cost-split.sh > $D/launch_$L.out 2>&1
echo "== $L end $(date +%T)"; tail -2 $D/launch_$L.out
R=$(grep -o 'data: .*' $D/launch_$L.out | sed 's/^data: //')
[ -n "$R" ] && { mkdir -p Screenshots/226/$L; cp "$R"/stills/*.png Screenshots/226/$L/ 2>/dev/null; cp "$R/costsplit.txt" $D/costsplit_$L.txt; }
cp "$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log" $D/player_$L.log
echo RUN_DONE
