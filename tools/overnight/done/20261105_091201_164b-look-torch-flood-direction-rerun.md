# 164-look-torch-flood-direction

Model: opus

Approval: owner, 2026-11-01, doc Q101 part 3 (a): a short look task for a smoother torch-flood direction term BEFORE L3. Read 158's handoff
(`tools/overnight/blocked/158-torch-flood-l2-gpu.HANDOFF.md`) and `TORCH_LIGHT_PLAN.md`. LOOK task: no frame-time arms. Flood stays OFF by default.

## Problem
158's direction term (gradient of the chamfer flood) is piecewise constant -> hard facets: a bright square on the floor under the room torch,
straight wedges along wall corners, vertical stripes on the tunnel's left wall (`Screenshots/158/torchflood158.png`, flood columns).

## Do
Try at most THREE smoother direction terms behind a flag (e.g. `-tfdir 0|1|2|3`, 0 = 158's), for example: central differences over 0.4 m on
3 axes; blend toward the nearest visible torch's direction while the flood path is near-straight (flood value ~ straight-line distance);
the flood value alone with a small wrap (no direction). Exact rule for walls still holds: sealed spaces stay exactly dark (158's tests stay green;
add one per new term if the sampler changes). Fast suite once.

## Deliverable
`Screenshots/164/`: 158's sheet rows (tunnel, sealed room, open ground, pillar) x columns: today (2.9 m), 158's flood, each new term. LARGE: one
image per row, each tile at least 960 px wide (owner, 2026-11-01: 5-across strips were "basically thumbnails"); crops 2x of the facet spots.
Captions name the facet spots to look at.

## End
STOP_FOR_OWNER: one doc question "which direction term (or flat)?" with the sheets, recommendation first; if one term clearly removes the
facets with no new artefact, also ask "flood ON by default after L3?".

## Supervisor note (2026-11-02 00:00)
Requeued as 164b: the owner pause (23:42) cut the look launch short; the code is done and committed (d2b7916). Follow `tools/overnight/blocked/164-look-torch-flood-direction.HANDOFF.md` "Unfinished / next" (SKIP_BUILD=1 if the build still matches), then the sheet, fast suite, and end STOP_FOR_OWNER with the direction-term question.
