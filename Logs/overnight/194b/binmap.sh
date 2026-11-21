#!/bin/bash
# usage: binmap.sh <cache dir> -- one line per kernel binary: finish time, size, kernel name(s) found in it
K=$(grep -o '^#pragma kernel [A-Za-z]*' Assets/CoreEngine/Rendering/Raymarch.compute | awk '{print $3}')
for b in "$1"/*.bin; do
  names=$(strings -n 5 "$b" | grep -xF "$K" | sort -u | tr '\n' ' ')
  echo "$(stat -f '%Sm' -t '%H:%M:%S' "$b") $(stat -f '%z' "$b") $names"
done | sort
