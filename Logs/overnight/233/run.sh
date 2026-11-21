#!/bin/bash
# task 233: look pieces. usage: run.sh day|night [extra flags]
# day: warm-up first unless SKIPWARM=1; walk_near + forest_eye (TIMED, ABBA: startsun_off = noon, startsun25, sun_off) +
#      jungle_eye + redwood_far1 (stills only); still-only arms at every path: look233_off, sun233_only, sky233_only, grade233_only.
#      FULL = all three pieces on (their defaults: -startsun 16, -skypale 1, -grade 1).
# night: torch_camp, stills only (-costtimed 1); arms FULL, look233_off (identity check).
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
D=Logs/overnight/233; L=$1; shift
P="$(tr ';' '\n' < Logs/TerrainGen/trees210/poses.txt | grep -E '^(forest_eye|jungle_eye|redwood_far1)\|' | paste -sd';' -)"
source tools/play-flags.sh
BASE="$PLAY_SETTINGS_FLAGS -quality high -renderrows 432 -shellwait 30"
case $L in
  day*)   X="-costtimed 12 -costfixed $P -costarms startsun_off,startsun25,sun_off -costtimedpaths walk_near,forest_eye -coststillarms look233_off,sun233_only,sky233_only,grade233_only,grade_off";;
  night*) X="-costtimed 1 -costpaths torch -costarms look233_off -coststillraw";;
esac
if [ "${L#day}" != "$L" ] && [ "${SKIPWARM:-0}" != "1" ]; then
  echo "warmup start $(date +%T)"; MODE=stream MOTION=walk ./run-tg2world.sh > $D/warmup.out 2>&1; echo "warmup end $(date +%T)"; grep '^log:' $D/warmup.out
fi
echo "== $L start $(date +%T)"
SKIP_BUILD=1 EXTRA="$BASE $X $*" ./run-cost-split.sh > $D/launch_$L.out 2>&1
echo "== $L end $(date +%T)"; tail -2 $D/launch_$L.out
R=$(grep -o 'data: .*' $D/launch_$L.out | sed 's/^data: //')
[ -n "$R" ] && { mkdir -p Screenshots/233/$L; cp "$R"/stills/*.png Screenshots/233/$L/ 2>/dev/null; cp "$R/costsplit.txt" $D/costsplit_$L.txt; }
cp "$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log" $D/player_$L.log
echo RUN_DONE
