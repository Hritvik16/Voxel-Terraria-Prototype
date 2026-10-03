Requires: 06-taa-core

Approved by the owner 2026-10-02 (TAA proposal: each risk gets its own check
before TAA can be switched on). TAA stays OFF by default after this task: the
owner compares stills side by side before it is switched on.

If `06b-taa-core-continued` exists in the queue or done/, it must be done first:
if it is still queued, write HANDOFF saying so and finish (no work).

Run each check with TAA ON vs OFF, same build, and record the result:
1. Ghosting on moving fluid: a lava flow and a water pour, frame by frame
   (fluid rigs: see KNOWN_ISSUES.md RIG SELECTION RULE; `run-acceptance-fluid.sh`
   or a Playground capture). Look at the frames.
2. Ghosting after an edit: dig with the camera still; the hole must be clean in
   the very next frame.
3. Smearing on fast turns: `-motion spin` and the 60 m/s flight rig.
4. Light changes: the day/night rig at fast clock speed.
5. Far band and seams: the far-seam altitude capture (`-farseams`).
6. Shimmer: task 05's metric ON vs OFF at every pose.
7. Cost: `MODE=walk MINUTES=3 ./run-tg2world.sh`, three balanced reps ON/OFF
   (ABBAAB or similar, machine cool between), mean of 30 s windows. Budget from
   the proposal: <= ~1.5 ms per frame.

Fix what a check exposes only if the fix is local to TAA and small; otherwise
record it. Write a short side-by-side summary for the owner (which stills to
open, and paths) into HANDOFF and KNOWN_ISSUES.md. Use STOP_FOR_OWNER only if a
risk fails in a way that blocks step 3 (checkerboard) or needs a design choice.
Commit.
