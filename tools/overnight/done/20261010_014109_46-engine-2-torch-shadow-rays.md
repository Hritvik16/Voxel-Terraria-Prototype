# 46-engine-2-torch-shadow-rays

Requires: 45-engine-1-one-generator-path

Approval: owner, 2026-10-09 ~23:00 -- engine plan step 2, owner Q5 (a): shadow rays for the nearest
few torches FIRST (this task), a light grid later (plan step 20); emissive bleed ON by default as in
the SVO. Fixes BUGS #4 (torches shine through walls). Queued by the supervisor.

Branch `engine-phase`.

## Goal
Point lights stop leaking through solid terrain. Like the SVO build (reference `../VT_gemini/SVO_REF/SVO_RayTracer.compute`:
its torches cast shadows): for each lit pixel, the nearest N point lights (start N = 4; make it a
`LightingConfig` field and a `-torchshadows N` flag, 0 = old behaviour) trace an occlusion ray
through the voxel data; the remaining lights are scaled by the traced share (the fraction of the N
traced lights that were visible, the SVO's form). Lava stays exempt from torch light (Q34 3a,
merged in 45). Then switch emissive bleed ON by default (`LightingConfig.emissiveBleed`,
`-emissivebleed 0` still turns it off) -- owner Q5 (a) supersedes the Q34 "off until measured".

## Steps
1. Implement the occlusion ray in the raymarch lighting pass (Phase 7 lighting; read
   `ARCHITECTURE_v8.6.md` Phase 7 lighting section and `AMENDMENT_8_12_PHASE7_GATE.md` before touching
   the light loop). Integer/bitwise voxel stepping, no float-precision excuses (CLAUDE.md).
2. New `run-phase7-rig.sh` gate: a torch behind a solid wall leaves the far side dark (luma on the
   far side within a small margin of the no-torch luma; pick and state the margin). Existing Phase 7
   gates stay green.
3. Fast EditMode suite green; player build log free of `Shader error`.
4. Screenshots, BEFORE (`-torchshadows 0 -emissivebleed 0`) vs AFTER (defaults), same poses: a torch
   behind a wall (both sides), a cave lit by 3+ torches, the lava cavern with torches, a surface pool
   of lava at night. Save to `Screenshots/Engine02/`, plus ONE captioned contact sheet
   `Screenshots/Engine02/contact_sheet.png` (rows = poses, columns = before/after).

## Report for the owner (put in HANDOFF under `## For the doc`)
- The new gate's numbers (far-side luma before vs after, margin).
- Phase 7 rig PASS/FAIL counts; suite counts.
- The contact sheet path with a caption saying what to look at per row.
No frame-time claim (plan step 7 measures it).
