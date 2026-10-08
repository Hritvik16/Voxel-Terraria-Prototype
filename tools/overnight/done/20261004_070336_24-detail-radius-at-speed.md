# 24-detail-radius-at-speed

Requires: 22-farstartt-integer-dda

Approval: owner, 2026-10-04 ~00:00, Queue decisions doc Q13, answer (a). Q13 asked
how the game should buy time to load chunks at speed. Option (a), as approved:
"Shrink the voxel-detail radius at speed. For example 7 chunks (~90 m) at 60 m/s,
still loading out to 10 (128 m). Each chunk gets 0.62 s to build; the rate needed
stays 2,285 chunks/s. Cost: while flying fast, voxel detail ends closer and the far
band starts nearer. The far-band hand-over must be separated from residency (an
engine change, its own task)." This is that task (first numbered 22). Queued by the supervisor.

## Why
Read KNOWN_ISSUES "STREAMING REQUIREMENT" first (task 16b). Voxels hand over to the
far band at the first NON-resident chunk column (`FarStartT`, `Raymarch.compute`
~1977-2003), so the hand-over radius R_d equals the load radius r x E = 128 m.
A chunk is requested in the frame its near face reaches 128 m, which is already R_d:
the prefetch margin M is 0 and the latency budget is 0 s at ANY speed. Separating
R_d from residency, and pulling R_d in as speed rises, makes M > 0.

## Do (one change)
1. A pure function for the hand-over radius at speed, in `StreamingRequirement.cs`
   (or beside it), pinned by EditMode tests on hand-worked cases:
   - From 16b's formula: margin M(v) >= v x (L_target + 1 frame), L_target = 0.62 s,
     R_d(v) = r x E - M(v), clamped to [a floor you justify, r x E]. Check: 60 m/s
     gives about 7 chunks (~90 m), as approved.
   - At v = 0 it MUST equal today's R_d exactly.
   - Prefer a continuous radius in metres over whole chunks, so walking (5.2 m/s,
     M ~3.2 m) barely moves it. If the code forces whole chunks, say what walking
     then loses, and do not hide it.
   - Use a smoothed speed (say what filter), so the radius does not pump with
     frame-time jitter or a single fast frame.
2. `FarStartT` hands the ray over at min(first non-resident column, R_d(v)). Voxel
   residency, requests and eviction are UNCHANGED.
3. Opt-in, OFF by default: `-detailspeed 1` (and a Playground key if one is free).
   Off must be byte-identical to today: run `MODE=poses` with BUILD=1 and compare
   every shot with the previous poses run.
4. Task 22 (owner-gated) rewrites `FarStartT`'s chunk walk as an integer DDA first;
   build on that loop, do not reintroduce the float re-derivation. Renumbered from 22
   on 2026-10-04 because task 19 queued 22. Report whether the coast pose changes with
   the flag on.

## Measure (one player build, AC power, check pmset and the owner-pause NOTE)
- `MODE=stream` on map v4, flag off vs on, 2 launches each: walk, fly 14 m/s, and
  the fastest motion the probe supports (56 m/s fly).
- Report section 7 per arm: the latency line against the NEW budget (it is no longer
  0 s), the deficit INSIDE R_d(v) (not inside 128 m), capacity, and frame p50/p95.
  Expect "NOT MET" on throughput; the point is a non-zero budget and how far off it is.
- Pictures (owner rule: every image gets a caption naming what to look at):
  - The fly-56 horizon strip (StreamProbe crops_horizon), off on top, on below.
  - One walk still, off vs on: is the hand-over visible at walking speed?
  - Open them and describe what is in frame before concluding anything.
- Fast EditMode; `SUITE=all` is not needed (no generator change).

## Do not
- Change the load radius, request radius, eviction, worker count or the generator.
- Turn the flag on by default. That is the owner's call after the pictures.
- Chain a second performance change. Measure this one, report, stop (CLAUDE.md).
