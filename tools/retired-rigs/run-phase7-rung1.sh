#!/bin/bash
# run-phase7-rung1.sh -- STAGE E. The single most valuable number AMENDMENT_8_12
# §4's matrix still owes: does §6.5's rung 1 (half-res beyond LOD1, with
# neighbour reuse) cost little enough to ship, measured against rung 3 (off)?
#
# PHASE_7_COMPLETION §6 already established, as an UNCOOLED diagnostic, that
# rung 0 (52.06 ms) and rung 2 (40.77 ms) cannot ship against a 16.67 ms frame,
# while rung 3 sits at 15.31 ms. Rung 1 is the only rung left that could carry
# shadows at all, and it had never been run.
#
# THIS ONE IS DONE PROPERLY, unlike §6's diagnostic:
#   - 300 s cooldown before EVERY arm (the same config measured 4.172 / 4.751 /
#     11.812 ms uncooled -- a 183% spread)
#   - COUNTERBALANCED A/B/B/A, so a monotone thermal/position decay across the
#     sequence cannot be read as an arm effect (FPS §0.3 -- a one-directional
#     sweep already produced a clean, convincing, wrong result here)
#   - same session, same binary, rung is a config number (FPS §3.2 -- a
#     cross-session A/B was wrong because the UNTOUCHED arm had drifted)
#   - fullscreen (FPS §0.1), frames / elapsed as the headline (FPS §0.2)
#
# Usage:  ./run-phase7-rung1.sh                 # golden hour, no fluid
#         SCEN=combined ./run-phase7-rung1.sh   # §13's true combined gate
set -uo pipefail
APP="Builds/Playground.app"
OUT="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/LightingAB"
COOL="${COOL:-300}"; SECS="${SECS:-40}"; GATE="${GATE:-960}"
SCEN="${SCEN:-golden}"
ORDER="${ORDER:-1 3 3 1}"     # A/B/B/A
mkdir -p Logs
REPORT="Logs/phase7_rung1_$(date +%Y-%m-%d_%H%M%S).txt"
say(){ echo "$@" | tee -a "$REPORT"; }

say "STAGE E -- rung 1 vs rung 3, cooled + counterbalanced"
say "scenario=$SCEN  gate=$GATE  ${SECS}s per arm  ${COOL}s cooldown  order: $ORDER"
say "Headline is TRUE SUSTAINED RATE (frames / elapsed). p99 is NEVER a headline"
say "here: 120 Hz ProMotion means ~1 frame in 3 lands in the tail by PACING."
say ""
[ -d "$APP" ] || { say "no $APP -- build the Playground first"; exit 1; }

FLUID="-nofluid"; [ "$SCEN" = "combined" ] && FLUID=""
i=0
for RUNG in $ORDER; do
  i=$((i+1))
  FREE=$(df -g . | tail -1 | awk '{print $4}')
  [ "$FREE" -lt 5 ] && { say "!! ${FREE}GB free, below the 5GB floor -- stopping"; exit 2; }
  say ""
  say "---- arm $i/4: rung $RUNG  ($SCEN, cooling ${COOL}s, ${FREE}GB free) ----"
  sleep "$COOL"
  ( open -n -W "$APP" --args -lightingab -goldenhour -fullscreen -gateres "$GATE" \
      -abseconds "$SECS" -shadowlevel "$RUNG" $FLUID ) &
  pid=$!; w=0
  while kill -0 $pid 2>/dev/null && [ $w -lt 480 ]; do sleep 5; w=$((w+5)); done
  if kill -0 $pid 2>/dev/null; then
    say "  !! arm did not quit in 480s -- killed. NO NUMBER FROM THIS ARM."
    pkill -f "Playground" 2>/dev/null; kill $pid 2>/dev/null; continue
  fi
  D=$(ls -1d "$OUT"/*/ 2>/dev/null | sort | tail -1)
  [ -f "${D}lighting_ab.txt" ] || { say "  no report from this arm"; continue; }
  grep -E "shadow rung|TRUE SUSTAINED|fluid " "${D}lighting_ab.txt" | sed 's/^/  /' | tee -a "$REPORT"
done

say ""
say "=========== DONE -- report: $REPORT ==========="
say "Read the two rung-1 arms against the two rung-3 arms. If the WITHIN-rung"
say "spread is comparable to the BETWEEN-rung difference, the difference is not"
say "established and should be reported as such rather than as a delta."
