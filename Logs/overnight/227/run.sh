#!/bin/bash
# task 227: low-res sky fill. usage: run.sh day|night [extra flags]
# day: warm-up first unless SKIPWARM=1; walk_near + forest_eye + jungle_eye; arms FULL (-skyfillres 2), sfres1 (before), sfres4, skyfill0.
# night: torch_camp, stills only (-costtimed 1); arms FULL, sfres1, sfres4.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
D=Logs/overnight/227; L=$1; shift
P="$(tr ';' '\n' < Logs/TerrainGen/trees210/poses.txt | grep -E '^(forest_eye|jungle_eye)\|' | paste -sd';' -)"
source tools/play-flags.sh
BASE="$PLAY_SETTINGS_FLAGS -quality high -renderrows 432 -shellwait 60"
case $L in
  day*)   X="-costfixed $P -costarms sfres1,sfres4,skyfill0";;
  night*) X="-costtimed 1 -costpaths torch -costarms sfres1,sfres4";;
esac
if [ "${L#day}" != "$L" ] && [ "${SKIPWARM:-0}" != "1" ]; then
  echo "warmup start $(date +%T)"; MODE=stream MOTION=walk ./run-tg2world.sh > $D/warmup.out 2>&1; echo "warmup end $(date +%T)"; grep '^log:' $D/warmup.out
fi
echo "== $L start $(date +%T)"
SKIP_BUILD=1 EXTRA="$BASE $X $*" ./run-cost-split.sh > $D/launch_$L.out 2>&1
echo "== $L end $(date +%T)"; tail -2 $D/launch_$L.out
R=$(grep -o 'data: .*' $D/launch_$L.out | sed 's/^data: //')
[ -n "$R" ] && { mkdir -p Screenshots/227/$L; cp "$R"/stills/*.png Screenshots/227/$L/ 2>/dev/null; cp "$R/costsplit.txt" $D/costsplit_$L.txt; }
cp "$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log" $D/player_$L.log
echo RUN_DONE
