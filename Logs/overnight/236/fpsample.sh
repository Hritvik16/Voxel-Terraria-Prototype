#!/bin/bash
# task 236: once the player logs "tf_tick armed" (or "rebuild start"), sample footprint categories for ~20 s.
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
OUT="$1"; : > "$OUT"
until pgrep -x "Voxel Terraria 1 Byte BrickMap" >/dev/null; do sleep 1; done
sleep 10
for i in $(seq 1 900); do
  grep -q "tf_tick armed\|rebuild start" "$PL" 2>/dev/null && break; sleep 0.5
done
PID=$(pgrep -x "Voxel Terraria 1 Byte BrickMap" | head -1)
echo "pid $PID start $(date +%T)" >> "$OUT"
end=$((SECONDS+25))
while [ $SECONDS -lt $end ]; do
  echo "== $(date +%T.%N | cut -c1-12)" >> "$OUT"
  footprint "$PID" 2>/dev/null | sed -n '2p;7,16p' >> "$OUT"
done
echo done >> "$OUT"
