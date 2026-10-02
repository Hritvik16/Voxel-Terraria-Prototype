#!/bin/bash
# Readable view of an overnight task session's transcript (read-only; it starts no agent).
#   tools/overnight/watch-task.sh [NAME-prefix]   e.g. 04, 04-cliff-pose-band-ab; none = newest task
# Task sessions are headless `claude -p` runs: nothing is on screen while they work, but
# Claude Code writes their transcript to ~/.claude/projects/<project>/<session>.jsonl as they go.
# Follows the file while that task is the one running (runner.log), then exits; NOFOLLOW=1 prints and exits.
# Do NOT `claude --resume` a task session to look at it: typing there starts a second agent.
cd "$(dirname "$0")/../.." || exit 1
PROJ=~/.claude/projects/$(pwd | sed 's#[^A-Za-z0-9]#-#g')
LOG=Logs/overnight/runner.log
WANT="$1"

exec python3 - "$PROJ" "$LOG" "$WANT" <<'EOF'
import sys, os, json, glob, re, time
from datetime import datetime
proj, log, want = sys.argv[1:4]

def task_of(path):
    # first user message of a task session starts with the runner's preamble
    try:
        with open(path) as f:
            for line in f:
                d = json.loads(line)
                if d.get("type") != "user": continue
                c = d["message"]["content"]
                c = c if isinstance(c, str) else " ".join(x.get("text", "") for x in c if isinstance(x, dict))
                if not c.startswith("# Unattended task session"): return None
                m = re.search(r"YOUR TASK: (\S+)", c)
                return m.group(1) if m else None
    except Exception: return None

found = [(os.path.getmtime(p), task_of(p), p) for p in glob.glob(proj + "/*.jsonl")]
found = sorted(x for x in found if x[1] and x[1].startswith(want))
if not found: sys.exit(f"no task transcript matching '{want}' in {proj}")
_, name, path = found[-1]

def running(name):
    try: lines = open(log).read().splitlines()
    except OSError: return False
    st = [i for i, l in enumerate(lines) if re.search(r"\bstart " + re.escape(name) + r"$", l)]
    if not st: return False
    after = lines[st[-1] + 1:]
    return not any(name in l for l in after) and not any("queue runner end" in l or "stopped" in l for l in after)

B, D, C, R = "\033[1m", "\033[2m", "\033[36m", "\033[0m"
print(f"{B}{name}{R}  {D}{path}{R}\n")

def show(d):
    try: t = datetime.fromisoformat(d["timestamp"].replace("Z", "+00:00")).astimezone().strftime("%H:%M")
    except Exception: t = "     "
    m = d.get("message") or {}
    c = m.get("content")
    if d.get("type") == "assistant" and isinstance(c, list):
        for x in c:
            if x.get("type") == "text" and x["text"].strip():
                print(f"{D}{t}{R} {x['text'].strip()}\n")
            elif x.get("type") == "tool_use":
                i = x.get("input", {})
                what = i.get("description") or i.get("file_path") or i.get("pattern") or ""
                cmd = (i.get("command") or "").strip().splitlines()
                print(f"{D}{t}{R} {C}{x['name']}{R} {what}")
                if cmd: print(f"      {D}$ {cmd[0][:150]}{R}")
    elif d.get("type") == "user" and isinstance(c, list):
        for x in c:
            if x.get("type") == "tool_result":
                r = x.get("content")
                r = r if isinstance(r, str) else " ".join(y.get("text", "") for y in (r or []) if isinstance(y, dict))
                for l in r.strip().splitlines()[:3]: print(f"      {D}{l[:150]}{R}")
    sys.stdout.flush()

follow = running(name) and not os.environ.get("NOFOLLOW")
with open(path) as f:
    while True:
        line = f.readline()
        if line:
            if line.endswith("\n"):
                try: show(json.loads(line))
                except Exception: pass
            else: f.seek(f.tell() - len(line)); time.sleep(1)
            continue
        if not follow: break
        if not running(name):
            for l in f: show(json.loads(l))
            break
        time.sleep(2)
print(f"\n{B}-- {name}: {'finished' if follow or not running(name) else 'end of file'} --{R}")
EOF
