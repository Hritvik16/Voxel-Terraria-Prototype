# 21-poses-mirror-lost-check

Queued by task 17 (B2) as its one low-risk follow-up (evidence only, no code change
unless a cause is found and it is a plain bug fix inside the mirror; flag anything
bigger to the owner).

## Why
B2's `MODE=poses MAPV=4` run (`Logs/overnight/b2_arm_poses_v4.out`, run
20261003_212143) printed CPU-vs-GPU mirror mismatches that no earlier poses run had:
- `peak_from_2km_ground golden ... MIRROR lost 12232 lag 0 stale 2 [int3(348, 43, 406) brick (0,0,0) GPU 0x800117F1]`
- `summit_view golden ... MIRROR lost 5249 lag 0 stale 2`
- `glide_400m golden ... MIRROR lost 0 lag 0 stale 1`; every later pose 0 / 0 / 0.
B1's v4 poses run (`Logs/overnight/b1_arm_poses_v4.out`) had 0 on every pose.
B2 changed terrain content only (map v4 regions), no engine code, so this is either
an engine path the new terrain exposes or a timing case. KNOWN_ISSUES "TIER-0
CLIPMAP LOST UPDATES AFTER A TELEPORT" (fixed 2026-09-25) is the nearest precedent.
The screenshots of those two poses showed no visible hole.
The same run's peak footprint was 3763 MB vs B1's 3414 MB (Untagged 1720 -> 2092 MB).

## Do
- One build. `MODE=poses` x2 on `MAPV=4` and x2 on default v3, interleaved.
- Per run: the MIRROR lost / lag / stale numbers per pose, peak footprint and the
  Untagged / IOAccelerator lines of the peak breakdown, `pmset -g batt`, owner-pause NOTE.
- If lost > 0 repeats: name the chunk coordinates (validator example), and say
  whether the first pose after the initial fill is the only one affected. Open the
  `_golden.png` and `_golden_band.png` of each affected pose and say what is in frame.
- Do not change the validator or the poses. Report only; STOP_FOR_OWNER if a fix
  would touch the clipmap upload/eviction contract.

## Closed by the supervisor (2026-10-04 02:55)
Report-only task, complete. STOP_FOR_OWNER was for the fix (26, owner-gated); asked as Q17 in the Queue decisions doc. Moved to done/.
