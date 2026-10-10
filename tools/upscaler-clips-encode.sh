#!/bin/bash
# tools/upscaler-clips-encode.sh <path dir> <out dir> -- task 50: one path of
# -upscalerclips frames (<path dir>/<arm>/f0000.jpg ...) -> grid_<path>.mp4,
# crop_<path>.mp4 and <path>__<arm>.mp4 in <out dir>; checks each has the frame
# count of the raw arms, then DELETES <path dir> (disk is ~97% full) unless the
# run folder holds KEEP_RAW (crops still to be checked; delete by hand after).
# <out dir>/crop_<path>.xy ("x y") sets the crop window by hand. NOSINGLES=1: grid + crop only.
# Needs Logs/frames-to-mp4 (swiftc -O tools/frames-to-mp4.swift -o Logs/frames-to-mp4).
set -uo pipefail
SRC="${1%/}"; OUT="$2"; P=$(basename "$SRC")
ENC=Logs/frames-to-mp4
ARMS="native taa_540 fsr1_540 stp_540 fsr3_dyn fsr3_720"
mkdir -p "$OUT"
XY=""; [ -f "$OUT/crop_${P}.xy" ] && XY=$(cat "$OUT/crop_${P}.xy")
python3 tools/upscaler-clip-grids.py "$SRC" "$SRC/_grid" "$SRC/_crop" $XY || exit 1
N=$(python3 -c "import json,sys; print(json.load(open(sys.argv[1]))['frames'])" "$SRC/crop.json")
cp "$SRC/crop.json" "$OUT/crop_${P}.json"
ok=1
enc() {  # <frames dir> <out.mp4> <bits/s>
  "$ENC" encode "$1" "$2" 30 "$3" > /dev/null || { echo "ENCODE FAILED $2"; ok=0; return; }
  local i; i=$("$ENC" info "$2")
  echo "$(basename "$2"): $i  $(du -h "$2" | cut -f1)"
  echo "$i" | grep -q "^frames $N " || { echo "FRAME COUNT WRONG $2 (want $N)"; ok=0; }
}
enc "$SRC/_grid" "$OUT/grid_${P}.mp4" 18000000
enc "$SRC/_crop" "$OUT/crop_${P}.mp4" 16000000
[ "${NOSINGLES:-0}" = "1" ] || for a in $ARMS; do enc "$SRC/$a" "$OUT/${P}__${a}.mp4" 10000000; done
if [ $ok = 1 ] && [ -f "$SRC/../KEEP_RAW" ]; then rm -rf "$SRC/_grid" "$SRC/_crop"; echo "$P: encoded, raw frames KEPT (KEEP_RAW)"
elif [ $ok = 1 ]; then rm -rf "$SRC"; echo "$P: encoded, raw frames deleted"; else echo "$P: FAILED, raw frames kept in $SRC"; exit 1; fi
