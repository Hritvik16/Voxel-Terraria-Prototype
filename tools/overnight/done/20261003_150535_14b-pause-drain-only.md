# 14b-pause-drain-only

Requires: 14-heavy-pause-toggle

Owner correction, 2026-10-03, after task 14 was written: "This is not what I want it
to do. Let whatever is running finish naturally when I ask for a pause but prevent
any builds/tests (anything that hogs CPU and RAM) from starting until I
unpause/resume." Tooling only.

## Change task 14's `tools/heavy.sh` to drain semantics
- **`pause`**: create the HOLD file and nothing else. Never SIGSTOP, SIGTERM or
  SIGKILL anything. Whatever holds the Unity lock runs to completion. Every NEW
  acquisition (`unity_lock_acquire`: builds, EditMode runs, player launches) waits
  until `resume`.
- **Remove the freeze and `--kill` paths entirely** (code, usage text, docs). The
  owner asked for neither.
- **`status`** shows:
  - PAUSED or RUNNING;
  - what is still finishing: the lock owner, its command and how long it has run;
  - what is waiting on the pause;
  - "draining: N heavy processes still running" until they exit, then "idle --
    safe to use the Mac".
- **Multi-launch runs:** each launch takes the lock separately, e.g. the B-step
  streaming A/B arms and task 08's cost driver. A pause therefore takes effect at the
  next launch boundary; the current launch finishes. Check that the scripts that loop
  over launches still behave: they wait and then continue after `resume`, with no
  timeout and no failure.
- **Keep the overlap warning, reworded:** `NOTE: owner paused during this run (Mac
  likely in use) -- timing numbers may be contaminated; rerun this arm`. Nothing is
  frozen any more, but a pause means the owner was probably using the Mac.
- Keep the rule that a pause never makes the stale-lock check (7200 s) fire.

## Acceptance
- During a fast-suite run, `pause`: the suite keeps running and finishes 0 FAIL;
  `status` shows "draining" and then "idle".
- A `tools/with-unity-lock.sh true` started while paused waits, and proceeds
  within ~60 s of `resume`.
- A 2-launch loop (two `MODE=walk MINUTES=1` runs back to back) with a pause during
  launch 1: launch 1 completes, launch 2 waits until resume, both logs are fine, and
  launch 1 carries the NOTE.
- `grep -n "SIGSTOP\|kill -STOP\|--kill" tools/heavy.sh tools/unity-lock.sh` is empty.
- Update the `CLAUDE.md` and `KNOWN_ISSUES.md` lines task 14 wrote. Fast EditMode
  0 FAIL before committing.
