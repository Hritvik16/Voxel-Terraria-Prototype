#!/bin/bash
# tools/run-editmode-filter.sh <test-name regex> -- a FILTERED EditMode run for
# fast iteration (one fixture or test). Not a substitute for the full suite:
# every commit still runs ./run-editmode-tests.sh. Takes the shared Unity lock.
set -uo pipefail
source "$(dirname "$0")/unity-lock.sh"
unity_lock_acquire
rm -f TestResults_filter.xml unity_filter.log
/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath "$(pwd)" \
  -runTests -testPlatform EditMode -testFilter "$1" -testResults TestResults_filter.xml -logFile unity_filter.log
[ -f TestResults_filter.xml ] || { echo "no results (compile error?)"; grep -h "error CS" unity_filter.log | sort -u | head; exit 1; }
python3 - <<'PY'
import xml.etree.ElementTree as ET
r = ET.parse('TestResults_filter.xml').getroot()
p = f = 0
for tc in r.iter('test-case'):
    if tc.get('result') == 'Passed': p += 1
    else:
        f += 1; m = tc.find('.//message'); print('FAILED', tc.get('name'), '::', (m.text or '').strip()[:300] if m is not None else '')
print(f'PASS {p} FAIL {f}')
PY
