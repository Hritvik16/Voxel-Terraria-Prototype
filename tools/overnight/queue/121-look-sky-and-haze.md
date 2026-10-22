# 121-look-sky-and-haze

Model: opus

Approval: owner, 2026-10-22 (chat, answers to task 114 Q1/Q3/Q4/Q6). LOOK task, not a performance task: no timing run (rule 10). Read `VISUAL_REVIEW_114.md` first (the defect rows named below, causes with file:line). Ship the change ON by default with a flag that restores the old look. Before/after stills with `run-look-review.sh` (114), well lit (midday, sun behind the camera; plus golden hour where named), FSR 3 at play rows, same poses as 114; captions say what to look at. HANDOFF with `## For the doc` (2-4 before/after images). TASKS.md paragraph. Fast suite once at the end.

## Change (114 defects #4 + #6)
Sky: concentrate the zenith-to-horizon gradient near the horizon (e.g. `pow(1-dir.y, 4)` instead of `dir.y*0.5+0.5`,
`LookAtmosphere`, `Raymarch.compute:931-936`), add a sun disc, and a time-of-day tint over the whole horizon rim so golden hour is no
longer orange land under a noon-blue sky. No clouds (a separate feature). Haze: aerial perspective that reads (range ~1500-2000 m
instead of `FarShadeHazeRangeM` 3500, `RaymarchFeature.cs:267`, and/or desaturation with distance), haze colour taken from the new
horizon colour. Stills at noon, golden hour and dusk, from high ground (long view) and walk_open.
