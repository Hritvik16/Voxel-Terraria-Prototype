# 114-visual-look-review

Model: opus

Approval: owner, 2026-10-22 (chat): "the game visually just does not look good when I see builds running ... sure we can do the
visual task". Owner also reported that the Low preset looks bad, with moire. DIAGNOSIS ONLY: no change to any default, no
performance-changing code, no timing run (rule 10). Debug views or capture flags are allowed if a cause can't be shown without them.

## Goal
The owner can't name what looks wrong, so this task names it. Produce a look-defect catalogue: each visible defect, where it
shows (preset, distance, shell or tier, upscaler), and its most likely cause in the code, plus ONE proposed fix each with its risk.
Then STOP_FOR_OWNER: which fixes to make is a look decision.

## Shots (all in ONE player launch where possible; well lit per memory: midday ~12 h, sun behind the camera)
- Poses: 110's walk_open and walk_near, plus at least one ridge or valley view at 100-500 m and one long view from high ground
  (shells and far band both in frame). Use existing poses or capture flags (`grep -n 'shots\|-poses\|-tod' TASKS.md`).
- Matrix per pose: `-quality low | medium | high` x upscaler {FSR 3 at play rows, `none` at the same internal rows}.
  The `none` arm separates render aliasing from upscaler artifacts.
- Motion: moire and shimmer are temporal, so take 2-4 consecutive frames during a slow walk or turn at Low and High (FSR 3),
  and make a difference or flicker image of them.
- One golden-hour still (`-tod 17`) at walk_open, High, FSR 3 (lighting flatness or darkness).
- Crops at 2-4x of every defect you list.

## Look for (not exhaustive)
Moire or shimmer on distant voxel faces; shell-to-shell and tier-to-far-band seams (a visible change in detail, colour or
brightness); where voxels go below 1 px before the next shell starts (check against 8.14 rule A and `ShellLayout` sizing rows vs
the rows actually rendered, e.g. Low sizes for 480); FSR 3 ghosting or smear; flat or washed lighting, AO, fog or sky; palette
colours; anything floating or missing. For each defect: preset/rows it appears at, and whether `none` shows it too.

## Output
- `VISUAL_REVIEW_114.md` in the repo: the catalogue table (defect, crop path, where, cause with file:line, proposed fix, risk,
  owner decision needed?), ranked by how much it hurts the look. Captions say what to look at.
- HANDOFF: `## For the doc` with the top 5 defects, 3-6 images (captioned), and a batch of owner questions (recommendation first),
  incl. "is this what you meant by 'doesn't look good'?" with the images to point at.
- TASKS.md: one paragraph (Task 114). No test changes expected; if you add a capture flag, run the fast suite once at the end.
- End with STOP_FOR_OWNER.
