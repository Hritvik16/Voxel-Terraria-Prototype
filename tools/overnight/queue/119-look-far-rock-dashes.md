# 119-look-far-rock-dashes

Model: opus

Approval: owner, 2026-10-22 (chat, answers to task 114 Q1/Q3/Q4/Q6). LOOK task, not a performance task: no timing run (rule 10). Read `VISUAL_REVIEW_114.md` first (the defect rows named below, causes with file:line). Ship the change ON by default with a flag that restores the old look. Before/after stills with `run-look-review.sh` (114), well lit (midday, sun behind the camera; plus golden hour where named), FSR 3 at play rows, same poses as 114; captions say what to look at. HANDOFF with `## For the doc` (2-4 before/after images). TASKS.md paragraph. Fast suite once at the end.

## Change (114 defect #3, the crawling moire)
Far-band step sides deeper than 1.5 m become rock (`FAR_WALL_LIP` 15, `Raymarch.compute:2469` / :4742), so at coarse far levels
(steps >= 3.2 m) every riser on gentle land is a grey dash that crawls when moving. Choose rock by SLOPE (85b's `FarSlopeNormal`,
e.g. rock above ~40 deg) instead of step depth; cliffs keep their rock. Stills: the pink plain / snow areas of 114's
`crops/B_low_high_dashes_native6x.png` and `crops/A_low_flank_trio.png`, plus a 2-4 frame motion diff showing the crawl gone.
