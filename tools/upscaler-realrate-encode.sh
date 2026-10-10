#!/bin/bash
# tools/upscaler-realrate-encode.sh <rr_<path> dir> <out dir> -- task 51: one path of
# -upscalerclips -realrate frames (<arm>/f0000.jpg at each arm's own step, rates.json)
# -> grid_<path>.mp4 and crop_<path>.mp4 in <out dir>, 60 fps sample-and-hold
# (tools/upscaler-clip-grids.py's real-rate mode); checks the frame count, then
# DELETES the raw frames. Needs Logs/frames-to-mp4.
set -uo pipefail
SRC="${1%/}"; OUT="$2"; P=$(basename "$SRC"); P="${P#rr_}"
ENC=Logs/frames-to-mp4
mkdir -p "$OUT"
XY=""; [ -f "$OUT/crop_${P}.xy" ] && XY=$(cat "$OUT/crop_${P}.xy")
python3 tools/upscaler-clip-grids.py "$SRC" "$SRC/_grid" "$SRC/_crop" $XY || exit 1
N=$(python3 -c "import json,sys; print(json.load(open(sys.argv[1]))['frames'])" "$SRC/crop.json")
cp "$SRC/crop.json" "$OUT/crop_${P}.json"; cp "$SRC/rates.json" "$OUT/rates_${P}.json"
ok=1
enc() {  # <frames dir> <out.mp4> <bits/s>
  "$ENC" encode "$1" "$2" 60 "$3" > /dev/null || { echo "ENCODE FAILED $2"; ok=0; return; }
  local i; i=$("$ENC" info "$2")
  echo "$(basename "$2"): $i  $(du -h "$2" | cut -f1)"
  echo "$i" | grep -q "^frames $N " || { echo "FRAME COUNT WRONG $2 (want $N)"; ok=0; }
}
enc "$SRC/_grid" "$OUT/grid_${P}.mp4" 24000000
enc "$SRC/_crop" "$OUT/crop_${P}.mp4" 20000000
if [ $ok = 1 ] && [ -f "$SRC/../KEEP_RAW" ]; then rm -rf "$SRC/_grid" "$SRC/_crop"; echo "$P: encoded, raw frames KEPT (KEEP_RAW)"
elif [ $ok = 1 ]; then rm -rf "$SRC"; echo "$P: encoded, raw frames deleted"; else echo "$P: FAILED, raw frames kept in $SRC"; exit 1; fi
