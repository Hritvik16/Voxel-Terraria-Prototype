#!/bin/bash
# tools/test-unity-lock.sh -- script test of tools/unity-lock.sh (task 33a). No Unity.
#   tools/test-unity-lock.sh        (~30 s; prints PASS/FAIL per check, exits non-zero on a FAIL)
#
# Uses a private lock/hold/waiters path in a temp dir and a unity_busy that is
# always false, so it neither touches /tmp/voxel-unity.lock nor waits on a real
# Unity process. Checks:
#   1. a fake queue task holds the lock; an UNRELATED shell's unity_lock_acquire
#      prints the waiting line and does not return until the task releases it
#   2. nested: an outer script holds the lock, its child script's acquire returns
#      at once and leaves the outer's lock in place
#   3. a lock of ours whose pid is dead is removed and taken
set -u
LIB="$(cd "$(dirname "$0")" && pwd)/unity-lock.sh"
T=$(mktemp -d /tmp/unity-lock-test.XXXXXX)
unset UNITY_LOCK_HELD_BY
fails=0
ok()  { echo "PASS $*"; }
bad() { echo "FAIL $*"; fails=$((fails + 1)); }

cat > "$T/setup.sh" <<EOF
source "$LIB"
UNITY_LOCK="$T/lock"; UNITY_HOLD="$T/hold"; UNITY_WAITERS="$T/waiters"
UNITY_LOCK_CLEAR_S=2; UNITY_LOCK_POLL_S=1; UNITY_LOCK_SAY_S=3
unity_busy() { false; }
EOF

# wait_for <file> <seconds>: true once the file exists.
wait_for() { local i=0; while [ ! -e "$1" ] && [ $i -lt $(($2 * 5)) ]; do sleep 0.2; i=$((i + 1)); done; [ -e "$1" ]; }

# --- 1. unrelated shell waits for a fake task ---
bash -c "source '$T/setup.sh'; unity_lock_acquire; touch '$T/task.ready'
         while [ ! -f '$T/task.stop' ]; do sleep 0.2; done" > "$T/task.out" 2>&1 &
task=$!
if wait_for "$T/task.ready" 10; then ok "1a fake task took the lock: $(cat "$T/lock")"; else bad "1a fake task never took the lock"; fi
bash -c "source '$T/setup.sh'; unity_lock_acquire; echo ACQUIRED; touch '$T/second.done'
         sleep 1" > "$T/second.out" 2>&1 &
second=$!
sleep 8
if grep -q "waiting for the Unity lock: held by voxel-terraria-1-byte since .* (pid $task: " "$T/second.out"; then
  ok "1b waiting line: $(grep -m1 'waiting for the Unity lock' "$T/second.out")"
else bad "1b no waiting line naming pid $task; got: $(cat "$T/second.out")"; fi
grep -q "still waiting for the Unity lock" "$T/second.out" && ok "1c repeat line after UNITY_LOCK_SAY_S" || bad "1c no repeat line"
[ -e "$T/second.done" ] && bad "1d second shell returned while the task held the lock" || ok "1d second shell still waiting after 8 s"
touch "$T/task.stop"; wait "$task"
if wait_for "$T/second.done" 15; then ok "1e second shell acquired after release: $(cat "$T/lock" 2>/dev/null)"; else bad "1e second shell never acquired"; fi
wait "$second"
[ -e "$T/lock" ] && bad "1f lock left behind after the second shell exited" || ok "1f lock released on exit"

# --- 2. nested acquire returns at once ---
cat > "$T/child.sh" <<EOF
#!/bin/bash
source "$T/setup.sh"
s=\$SECONDS; unity_lock_acquire; echo "CHILD_RETURNED after \$((SECONDS - s)) s, lock: \$(cat "$T/lock")"
EOF
chmod +x "$T/child.sh"
bash -c "source '$T/setup.sh'; unity_lock_acquire; echo OUTER pid \$\$
         '$T/child.sh'                       # direct child: ancestor check
         ( unset UNITY_LOCK_HELD_BY; bash '$T/child.sh' )   # ancestor check alone
         [ -f '$T/lock' ] && echo LOCK_STILL_THERE" > "$T/nested.out" 2>&1
n=$(grep -c "CHILD_RETURNED after [01] s" "$T/nested.out")
[ "$n" = 2 ] && ok "2a nested acquires returned at once (2 of 2)" || bad "2a nested: $(cat "$T/nested.out")"
sed -n "/^OUTER/,\$p" "$T/nested.out" | grep -q "waiting" && bad "2b nested printed a waiting line" || ok "2b nested printed no waiting line"
grep -q LOCK_STILL_THERE "$T/nested.out" && ok "2c outer lock intact after the children" || bad "2c outer lock gone"
[ -e "$T/lock" ] && bad "2d lock left behind" || ok "2d outer released on exit"

# --- 3. our lock with a dead pid is cleared ---
bash -c 'exit 0' & dead=$!; wait $dead
echo "voxel-terraria-1-byte $(date '+%Y-%m-%d %H:%M:%S') pid $dead" > "$T/lock"
bash -c "source '$T/setup.sh'; unity_lock_acquire; echo ACQUIRED pid \$\$" > "$T/dead.out" 2>&1
grep -q "removing a lock left behind by dead pid $dead" "$T/dead.out" && grep -q ACQUIRED "$T/dead.out" \
  && ok "3 dead-pid lock removed and taken" || bad "3 dead pid: $(cat "$T/dead.out")"

echo "--- logs in $T"
[ $fails = 0 ] && { echo "ALL PASS"; exit 0; } || { echo "$fails FAIL"; exit 1; }
