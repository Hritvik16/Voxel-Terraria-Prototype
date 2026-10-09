#!/bin/bash
# B5b canyon pictures: shots.sh <label before|after> <build 0|1> <pose...>   (noon; 2 m and 120 m eye in one launch)
# Poses are B5's rim shots at seed 42 (Logs/TerrainGen/playmetrics/b5_shots_seed42.txt at HEAD 4c7f368), the same before and after.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
LBL=$1; BLD=$2; shift 2
FS="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/FlySweep"
OUT="Screenshots/B5b/$LBL"; mkdir -p "$OUT" Logs/overnight/b5b
pose() { case $1 in
  f2) echo "2590.8 3756.5 2429.2 3761.6";;
  f3) echo "5102.6 3116.7 4868.8 3238.5";;
  f2b) echo "2657.0 4601.5 2422.0 4599.1";;   # the after report's rim pose (canyon [2] at its deepest, 143 m)
esac; }
for P in "$@"; do
  read X Z LX LZ <<< "$(pose $P)"
  T="b5b${LBL}_${P}"
  BUILD=$BLD MAPV=4 MODE=flysweep EXTRA="-sweepx $X -sweepz $Z -lookx $LX -lookz $LZ -heights 2,120 -pitches 6,30 -renderpreset quality -tod 12 -tag $T" \
    ./run-tg2world.sh > "Logs/overnight/b5b/shot_$T.out" 2>&1
  BLD=0
  D=$(ls -td "$FS"/*_"$T" 2>/dev/null | head -1)
  if [ -n "$D" ] && ls "$D"/*_beauty.png >/dev/null 2>&1; then
    for b in "$D"/*_beauty.png; do cp "$b" "$OUT/${P}_$(basename "$b")"; done
    cp "$D/flysweep.txt" "$OUT/${P}.txt"; echo "shot $T ok"
  else echo "shot $T MISSING"; fi
done
echo "SHOTS_DONE $LBL"
