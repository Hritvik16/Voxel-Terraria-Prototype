# 129-test-lod-vs-fullres-reference

Model: opus

Approval: owner, 2026-10-23, in chat ("a test at some point where we build a 0.1m res of some terrain at every [LOD level distance]
away and compare that with what our game looks like with the LOD level at that range. Ideally these two should look the same given we
are using perspective math to determine when we can switch terrain to a different lod level"). Test/capture task: no product default
changes, no frozen-API or memory-layout changes. Queued AFTER 127 and 128.

## Question
The LOD switch distances come from perspective math (a coarser voxel should project to about the same screen size as a 0.1 m voxel at
the near edge of its band). Does the game, at each LOD boundary distance, actually look like a true 0.1 m build of the same terrain?

## Method
For each LOD / shell band start distance D (every tier boundary and shell boundary, including the 3.3 km and 6.6 km seams and the
far-band hand-off):
1. Pick 2-3 terrain scenes (a ridge, a coast with sea, a flat plain/forest edge) with the camera at fixed pose, sun, and time (side-lit,
   see 127), FSR/TAA off, same resolution, looking so the terrain at distance D fills the frame centre.
2. REFERENCE: build that terrain patch at 0.1 m (sizeClass of the near tier) at distance D. It does not have to be the whole view: a
   patch covering the screen footprint of the region around D is enough. Use a capture-only path (e.g. generate those chunks at full
   resolution from the same seed/map into a scratch store and render them as if they were the near tier). Stay under the 3 GB cap and
   leave every runtime default alone; if the reference cannot be built without a layout/frozen-API change or exceeds memory even for a
   small patch, STOP_FOR_OWNER with what blocks it and the smallest alternative (e.g. a patch narrower than the full screen).
3. GAME: the normal game render of the same pose with the real LOD at that range.
4. Compare: side-by-side + a difference image (amplified), per-band numbers: mean absolute luma/colour difference, % of pixels
   differing by more than a threshold, and silhouette/edge shift in pixels. Also report the projected size in px of the coarser voxel
   vs a 0.1 m voxel at D, so the perspective rule can be checked against the picture.
5. Name where they differ and why (colour averaging, silhouette steps, shadow, haze, normals), and which boundary is worst.

## Deliverable
Images: for each boundary, pair + diff, 2x crops of the worst area; captions say which side is the reference. A table of the numbers
per boundary. A one-paragraph verdict: do they look the same? If not, which switch distances are too near and by how much (a
suggestion only, no change). Keep as a repeatable capture flag so it can be re-run after LOD changes; add a TASKS.md paragraph, HANDOFF
with `## For the doc`. No timing measurements. Run the fast suite once if code under Assets/ changes.
