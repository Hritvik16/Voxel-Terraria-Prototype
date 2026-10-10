#!/bin/bash
# B10 picture pack: shots2.sh <candidate a|b|c> <build 0|1> <pose...>   (each pose at noon and golden hour)
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
CAND=$1; BLD=$2; shift 2
FS="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/FlySweep"
OUT="Screenshots/B10/cand_$CAND"; mkdir -p "$OUT"
pose() { case $1 in
  sav_gentle) echo "3708.0 4772.0 3794.4 4739.7 2 4";;
  sav_broken) echo "4332.0 4252.0 4329.3 4166.6 2 4";;
  bad_gentle) echo "3132.0 2828.0 3500.0 2828.0 2 4";;
  bad_broken) echo "1268.0 4524.0 1700.0 4524.0 2 4";;
  sav_far)    echo "3388.0 5300.0 4428.0 4092.0 60 3";;
  bad_far)    echo "2372.0 4188.0 3428.0 2876.0 60 3";;
esac; }
for TODN in noon golden; do
  for P in "$@"; do
    read X Z LX LZ HT PI <<< "$(pose $P)"
    TOD=""; [ "$TODN" = "noon" ] && TOD="-tod 12"
    T="b10${CAND}_${P}_${TODN}"
    BUILD=$BLD MAPV=4 MODE=flysweep EXTRA="-sweepx $X -sweepz $Z -lookx $LX -lookz $LZ -heights $HT -pitches $PI -renderpreset quality $TOD -tag $T" \
      ./run-tg2world.sh > "Logs/overnight/b10/shot_$T.out" 2>&1
    BLD=0
    D=$(ls -td "$FS"/*_"$T" 2>/dev/null | head -1)
    if [ -n "$D" ] && ls "$D"/*_beauty.png >/dev/null 2>&1; then cp "$(ls "$D"/*_beauty.png | head -1)" "$OUT/${P}_$TODN.png"; cp "$D/flysweep.txt" "$OUT/${P}_$TODN.txt"; echo "shot $T ok"; else echo "shot $T MISSING"; fi
  done
done
echo "SHOTS_DONE $CAND"
