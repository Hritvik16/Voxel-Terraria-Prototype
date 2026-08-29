#!/bin/bash
# run-editmode-tests.sh
#
# Headless EditMode test run + summary, so Claude Code (or you) doesn't have
# to parse raw XML by eye every time. Run from the project root.
#
# Usage: ./run-editmode-tests.sh
# Exit code: 0 if every test passed, 1 otherwise (including compile failure).

set -uo pipefail

UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
PROJECT_PATH="$(pwd)"
RESULTS_XML="TestResults.xml"
LOG_FILE="unity_test.log"

rm -f "$RESULTS_XML" "$LOG_FILE"

# NOTE: do NOT add -quit here. With -runTests, -quit makes the editor shut down
# as soon as the initial asset refresh finishes, BEFORE the test runner starts:
# you get a clean "Exiting batchmode successfully" log, no TestResults.xml, and
# zero tests run. The test runner terminates the editor itself when it is done.
"$UNITY_BIN" \
  -batchmode -nographics \
  -projectPath "$PROJECT_PATH" \
  -runTests -testPlatform EditMode \
  -testResults "$RESULTS_XML" \
  -logFile "$LOG_FILE"

if [ ! -f "$RESULTS_XML" ]; then
  echo "FAIL: no TestResults.xml produced — this is almost always a compile error,"
  echo "not a failing test. Check the tail of $LOG_FILE:"
  echo "---"
  tail -n 60 "$LOG_FILE"
  exit 1
fi

python3 - "$RESULTS_XML" <<'PY'
import sys, xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
passed = failed = skipped = 0
failures = []
for tc in root.iter('test-case'):
    r = tc.get('result')
    if r == 'Passed': passed += 1
    elif r == 'Failed':
        failed += 1
        f = tc.find('failure')
        msg = f.find('message').text.strip() if f is not None and f.find('message') is not None else ''
        failures.append(f"{tc.get('name')}: {msg.splitlines()[0] if msg else '(no message)'}")
    elif r in ('Skipped', 'Ignored'): skipped += 1

print(f"PASS {passed}  FAIL {failed}  SKIP {skipped}")
for line in failures:
    print("  FAILED:", line)

sys.exit(1 if failed > 0 else 0)
PY