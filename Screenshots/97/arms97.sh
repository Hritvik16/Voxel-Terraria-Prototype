#!/bin/bash
# Task 97 rate arms: arena fluid probe, -fluidstrip, back to back. Usage: arms97.sh OUT name1 "flags1" name2 "flags2" ...
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
OUT="$1"; shift
while [ $# -ge 2 ]; do
  name="$1"; flags="$2"; shift 2
  BUILD="${BUILD:-0}" MODE=fluidprobe PROBEWORLD="" EXTRA="-fluidstrip $flags" ./run-tg2world.sh > "Logs/overnight/97-$name.out" 2>&1
  BUILD=0
  log=$(grep -o 'Logs/tg2world_[0-9_]*\.txt' "Logs/overnight/97-$name.out" | tail -1)
  shots=$(grep '^shots:' "Logs/overnight/97-$name.out" | tail -1)
  {
    echo "== $name ($flags): log: $log"
    grep -A30 -- '---- +20 s wall clock' "$log" | grep -E 'FLUID RATE|DRAIN' | sed 's/^ *//'
    grep -A30 -- '---- +40 s wall clock' "$log" | grep -E 'FLUID RATE|DRAIN' | sed 's/^ */[40s] /'
    grep -E '^PLACED' "$log" | head -1
    grep -q 'owner paused' "$log" && echo "NOTE: owner paused during this run"
    echo "$shots"
  } >> "$OUT"
done
echo ARMS_DONE >> "$OUT"
