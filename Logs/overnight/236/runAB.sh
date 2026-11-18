#!/bin/bash
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
DEV_KERNELS=0 BUILD_ONLY=1 ./run-cost-split.sh || exit 1
for m in 0 1; do
  Logs/overnight/236/fpsample.sh Logs/overnight/236/fp_up$m.txt > /dev/null 2>&1 &
  SKIP_BUILD=1 EXTRA="-renderpreset quality -upscaler fsr3 -renderrows dyn -dynfloor 432 -shadowlevel 0 -roofskip 0 -shadowres full -gamemode survival -underwater 1 -seethrough 1 -riverlook 1 -farshade 1 -shells 1 -shellworkers 3 -minimap -torchflood 1 -tfdir 2 -tfi 1.6 -tfk 0.35 -skyfill 2 -quality high -renderrows 432 -shellwait 60 -costpaths torch -costarms tf_on,tf_tick -tfmemmarks -tfupload $m" ./run-cost-split.sh
  cp "$PL" Logs/overnight/236/player_up$m.log
  wait
  echo "ARM $m DONE"
done
echo ALLDONE
