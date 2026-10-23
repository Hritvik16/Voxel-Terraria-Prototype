#!/bin/bash
# task 129: 0.1 m reference vs the game at distance (-lodref 1). Capture-only, no timing.
# One launch per scene (shells refill per pose). High preset, play's look flags (run-look-review.sh).
#   Screenshots/129/129-driver.sh [scenes...]     e.g. ridge coast plain (default all three)
# Env: SKIP_BUILD=1, LODREF="extra -lodref flags" (e.g. -lodrefd 50,200).
cd "$(dirname "$0")/../.."
F="ERROR|shots:|^log:|waited|== |===|LOD REF|\[ref\]|skipped|BLOCKED"
B="${SKIP_BUILD:-1}"
for s in ${@:-ridge coast plain}; do
  echo "=== $(date +%T) $s"
  SKIP_BUILD=$B PRESETS=high EXTRA="-lodref 1 -lodrefscenes $s -shellworkers 6 ${LODREF:-}" ./run-look-review.sh 2>&1 | grep -E "$F" | tail -60
  B=1
done
echo "=== $(date +%T) ALLDONE"
