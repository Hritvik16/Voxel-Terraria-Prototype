Requires: 05-taa-shimmer-metric

Approved by the owner 2026-10-02 (TAA proposal step 2 ticked; also ticked:
"accept a slightly softer still image in exchange for no shimmer in motion,
compared side by side before it's switched on").

Goal: temporal anti-aliasing in the raymarcher, behind a switch, OFF by
default. Renderer-only: no memory layout, save format or streaming change.

Design (from the approved proposal):
- Per frame, jitter each primary ray by a sub-pixel offset (Halton 2,3; 8-16
  frame cycle).
- Reproject with the exact world hit point each ray already has (no motion
  vector pass). Reuse the temporal SHADOW reuse machinery in Raymarch.compute:
  `_PrevViewProj`, `ShadowHistR/W`, its "same surface?" depth test. Far-band
  hits reproject the same way.
- Clamp history to the current 3x3 neighbourhood (YCoCg min/max or variance
  clip); reject history where the material id changed; drop history inside
  bricks dirtied this frame (edits already mark chunks dirty); history weight
  falls as reprojected distance grows; reset on large light jumps.
- Blend into a colour history buffer; a light sharpen after the resolve.
- Switch: a player flag (e.g. `-taa`) plus a Playground setting; default OFF.

This is roughly 2-3 days of human work. If it does not fit in this session:
commit what compiles and passes with the switch OFF, then add a queue file
`06b-taa-core-continued.md` (same Requires line, describing exactly what is left)
and finish normally -- do not use STOP_FOR_OWNER for "not finished".

Acceptance:
- EditMode 0 FAIL; build 0 Shader error.
- Switch OFF: `MODE=poses` shots byte-identical (md5) to a run of the previous
  commit -- proves the default is untouched. If they differ, find out why.
- Switch ON: poses captured after the camera has been still >= 16 frames;
  look at eye_level_1..3 ON vs OFF and describe softness, ghosting, holes.
  Shimmer metric from task 05 ON vs OFF.
- No frame-time claim in this task (task 07 measures cost).
Commit with the evidence; HANDOFF must say what works and what does not.
