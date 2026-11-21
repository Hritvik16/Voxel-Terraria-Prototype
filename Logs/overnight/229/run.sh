#!/bin/bash
# task 229: flood twin only when torches are near. usage: run.sh day|night
# day: warm-up first unless SKIPWARM=1; walk_near; arms FULL (-floodnear 1), floodnear_off (before), flood_off, floodd2_off; raw + fnpair stills.
# night: torch_camp timed; arms tf_on (rule on), tf_twin (rule off); raw + fnpair stills.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
D=Logs/overnight/229; L=$1; shift
source tools/play-flags.sh
BASE="$PLAY_SETTINGS_FLAGS -quality high -renderrows 432 -shellwait 60 -floodnear 1 -coststillraw -costfloodpair"
[ "$L" = day2 ] && BASE="${BASE% -coststillraw -costfloodpair}"   # timing rerun: no raw stills
case $L in
  day2*)  X="-costarms floodnear_off,flood_off,floodd2_off,floodnear_off,flood_off";;
  day*)   X="-costarms floodnear_off,flood_off,floodd2_off";;
  night*) X="-costpaths torch -costarms tf_on,tf_twin";;
esac
if [ "${L#day}" != "$L" ] && [ "${SKIPWARM:-0}" != "1" ]; then
  echo "warmup start $(date +%T)"; MODE=stream MOTION=walk ./run-tg2world.sh > $D/warmup.out 2>&1; echo "warmup end $(date +%T)"; grep '^log:' $D/warmup.out
fi
echo "== $L start $(date +%T)"
SKIP_BUILD=1 EXTRA="$BASE $X $*" ./run-cost-split.sh > $D/launch_$L.out 2>&1
echo "== $L end $(date +%T)"; tail -2 $D/launch_$L.out
R=$(grep -o 'data: .*' $D/launch_$L.out | sed 's/^data: //')
[ -n "$R" ] && { mkdir -p Screenshots/229/$L; cp "$R"/stills/*.png Screenshots/229/$L/ 2>/dev/null; cp "$R/costsplit.txt" $D/costsplit_$L.txt; }
cp "$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log" $D/player_$L.log
echo RUN_DONE
