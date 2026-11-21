#!/bin/bash
# run-editmode-tests.sh
#
# Headless EditMode test run + summary, so Claude Code (or you) doesn't have
# to parse raw XML by eye every time. Run from the project root.
#
# Usage: ./run-editmode-tests.sh             FAST suite (<= 3 min): everything
#                                            except [Category("Slow")] -- every change
#        SUITE=all ./run-editmode-tests.sh   (<= 15 min target) everything except
#                                            [Category("Nightly")]: every generator /
#                                            rule test at least once -- on every
#                                            terrain-generator change
#        SUITE=nightly ./run-editmode-tests.sh  EVERYTHING (~1 h): before a merge
#                                            to main only (task 184b's third tier:
#                                            the big shell fills, long fuzzers, cost
#                                            reports, engine tests >= 2 s, seeds 7 /
#                                            2026 of the per-seed generator tests)
#        FILTER='<testFilter>' ...           only those tests (regex: escape "(42)"
#                                            as "\(42\)"); overrides SUITE
#        REPORTS=1 ...                       also the report-only tests
#                                            (TerrainGen.Tests/ReportOnly.cs)
# Tiers (task 184b): a Nightly test is ALSO Slow, so fast = !Slow, all = !Nightly.
# Exit code: 0 if every test passed, 1 otherwise (including compile failure).
# A SUITE=all run also leaves a copy in TestResults_all.xml (nightly: TestResults_nightly.xml);
# the fast suite compares against it to say how many cases it left out.

set -uo pipefail
# Task 148: the shared one-Unity-at-a-time lock (CLAUDE.md); a no-op wait when an outer wrapper already holds it.
source tools/unity-lock.sh
unity_lock_acquire

UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
PROJECT_PATH="$(pwd)"
RESULTS_XML="TestResults.xml"
ALL_XML="TestResults_all.xml"
LOG_FILE="unity_test.log"
SUITE="${SUITE:-fast}"

case "$SUITE" in
  fast)    CATEGORY_ARGS=(-testCategory "!Slow") ;;
  all)     CATEGORY_ARGS=(-testCategory "!Nightly") ;;
  nightly) CATEGORY_ARGS=() ;;
  *)       echo "SUITE must be 'fast' (default), 'all' or 'nightly', not '$SUITE'"; exit 1 ;;
esac
# FILTER=<-testFilter value>: run only those tests (overrides SUITE's category).
FILTER="${FILTER:-}"
[ -n "$FILTER" ] && CATEGORY_ARGS=(-testFilter "$FILTER")
# EXTRA = more Unity args (task 184b). The Editor already runs managed code in Release
# (EditorPrefs ScriptDebugInfoEnabled = 0); -releaseCodeOptimization measured no gain.
read -r -a EXTRA_ARGS <<< "${EXTRA:-}"

rm -f "$RESULTS_XML" "$LOG_FILE"
START=$SECONDS

# NOTE: do NOT add -quit here. With -runTests, -quit makes the editor shut down
# as soon as the initial asset refresh finishes, BEFORE the test runner starts:
# you get a clean "Exiting batchmode successfully" log, no TestResults.xml, and
# zero tests run. The test runner terminates the editor itself when it is done.
"$UNITY_BIN" \
  -batchmode -nographics \
  -projectPath "$PROJECT_PATH" \
  -runTests -testPlatform EditMode \
  ${CATEGORY_ARGS[@]+"${CATEGORY_ARGS[@]}"} \
  ${EXTRA_ARGS[@]+"${EXTRA_ARGS[@]}"} \
  -testResults "$RESULTS_XML" \
  -logFile "$LOG_FILE"

WALL=$((SECONDS - START))

if [ ! -f "$RESULTS_XML" ]; then
  echo "FAIL: no TestResults.xml produced — this is almost always a compile error,"
  echo "not a failing test. Check the tail of $LOG_FILE:"
  echo "---"
  tail -n 60 "$LOG_FILE"
  exit 1
fi

[ "$SUITE" = "all" ] && [ -z "$FILTER" ] && cp "$RESULTS_XML" "$ALL_XML"
[ "$SUITE" = "nightly" ] && [ -z "$FILTER" ] && cp "$RESULTS_XML" TestResults_nightly.xml

python3 - "$RESULTS_XML" "$SUITE" "$WALL" "$ALL_XML" <<'PY'
import sys, os, time, xml.etree.ElementTree as ET
xml_path, suite, wall, all_path = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4]
root = ET.parse(xml_path).getroot()
passed = failed = skipped = 0
failures = []
names = set()
for tc in root.iter('test-case'):
    names.add(tc.get('fullname'))
    r = tc.get('result')
    if r == 'Passed': passed += 1
    elif r == 'Failed':
        failed += 1
        f = tc.find('failure')
        msg = f.find('message').text.strip() if f is not None and f.find('message') is not None else ''
        failures.append(f"{tc.get('name')}: {msg.splitlines()[0] if msg else '(no message)'}")
    elif r in ('Skipped', 'Ignored'): skipped += 1

print(f"PASS {passed}  FAIL {failed}  SKIP {skipped}  suite={suite}  wall={wall // 60}m{wall % 60:02d}s")
if suite == 'fast':
    if os.path.exists(all_path):
        full = {tc.get('fullname') for tc in ET.parse(all_path).getroot().iter('test-case')}
        when = time.strftime('%Y-%m-%d %H:%M', time.localtime(os.path.getmtime(all_path)))
        print(f"  fast suite left out {len(full - names)} of {len(full)} cases (Category Slow; vs {all_path} of {when})")
    else:
        print(f"  fast suite: Category Slow left out (no {all_path} yet to count them against; run SUITE=all once)")
for line in failures:
    print("  FAILED:", line)

sys.exit(1 if failed > 0 else 0)
PY
