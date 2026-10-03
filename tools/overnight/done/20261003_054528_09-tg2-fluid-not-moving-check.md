## YOUR TASK: 09-tg2-fluid-not-moving-check

Follow-up from 07-taa-risk-checks (evidence only, no fix without the owner).

In task 07's `-taarisk` capture (`-world tg2`), fluid placed through Playground's
own EditService did NOT move in 225 frames: `SetBox` of 1029 water voxels
(2-4 m above a dug hole) and 784 lava voxels (1.5-3 m above the ground), at
tg2 eye_level_1 (camera (4600, 695.4, 4000) m, voxel y ~6940), the camera
~10 m away. Both columns still stood in the air at +45 and +225 frames
(`FarBand/2026-10-02_162607_taarisk_off/fluid_b*.png`, `fluid_c*.png`). The same
capture in the default Playground world (the arena chaos burst) does simulate.

Find out, without changing engine code, whether fluid simulates at all in the
tg2 world and if not, which step stops it (tile residency, the CA region's
vertical range, wake radius, readback, ...). Use a diagnostic capture or log
lines in a capture partial; quote the numbers. Write the answer into
KNOWN_ISSUES.md and HANDOFF. If a fix is needed, STOP_FOR_OWNER with the
proposed fix and its blast radius.
