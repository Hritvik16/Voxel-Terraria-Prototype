# 22-farstartt-integer-dda

Requires: 19-coast-bar-pixel-probe
**APPROVED by the owner 2026-10-04 (Queue decisions Q16 (a)).** It changes rendering code.
Also, doc only (owner Q20 (a), 2026-10-04): add PREAMBLE.md rule 9 (warm steady state) as one
bullet to CLAUDE.md "Measurement discipline", in the same words, in this task's commit.

Fix the coast black bar (KNOWN_ISSUES "BLACK BAR OVER THE SEA", pinned by task 19).

## The bug (measured, task 19)
`Raymarch.compute` `FarStartT` walks the window's chunk columns by re-deriving the
cell from a float position: `t = min(tE) + 1e-3; cc = floor((O.xz + D.xz * t) / 128)`.
The nudge is a fixed 1e-3 in t. At the coast pose (x = 40000 voxels) a float32 step
of x is 0.0039 voxels, and the nudge moves x by only |D.x| * 1e-3 = 0.00043. So after
the ray crosses a chunk's -x face, p.x rounds back to exactly the face (39936.0),
floor() returns the SAME chunk, and the loop repeats until its 96-iteration cap. Then
it returns tLimit. So the far march starts at the voxel march's end (230.9 m), under
the sea, and hits a -z water side face. Rays with D.x > 0 land on the next chunk's
minimum, so they work. The same stall applies to any -x or -z crossing where
|D| * 1e-3 is below half a float step. Reproduced exactly in a float32 Python model
(task 19's HANDOFF).

## The change (one function)
Step the INTEGER cell, Amanatides-Woo style (the coordinate rule: bitwise/integer
cell math). Do not re-derive the cell from a float position:
- `cc = int2(floor((O.xz + D.xz * t0) / 128.0))`, computed once at window entry.
  Clamp it into the window.
- `s = int2(D.x > 0 ? 1 : -1, D.z > 0 ? 1 : -1)`.
- `tEnter = t0`. Then, per cell:
  - if `!FarChunkResident(cc - oc)`, return `tEnter`;
  - `tE` = exit t of each axis for this cell; take the smaller one (`a`);
    `tEnter = tE[a]`; `cc[a] += s[a]`;
  - if `tEnter >= t1`, return tLimit.

No other function. FarMarch's `tn = tExit + 1e-3 * cs` uses the same nudge pattern
(scaled by the cell size, so less exposed). Check whether it can stall, with the
probe below. Report it; do NOT change it in this task.

## Evidence
- Fast EditMode suite before and after.
- `BUILD=1 MODE=poses MAPV=4 EXTRA="-coastprobe" ./run-tg2world.sh`. This is task
  19's probe; it is still in the code. Pass if:
  - the bar pixel's `FarStartT used` is about 150 m (the strip entry; was 230.9 m),
    with `far hit ... normal (0,1,0)`;
  - in all three poses, no dark run remains in rows 700-740. Use task 19's
    PIL check: dark = R<12, G<20, B<35.
- One full `MODE=poses MAPV=4` run, with no other change: look at every shot. This
  rules out new seams or holes anywhere the far band takes over (summit, glide,
  map, eye level).
- Frame time: FarStartT runs once per far-band pixel. A fixed DDA does FEWER
  iterations than the stalled one (96). Do not claim a frame-time change without a
  rig.
