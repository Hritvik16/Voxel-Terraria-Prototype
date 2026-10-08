# 18-coast-pose-black-bar-check

QA follow-up from task 15 (B1), diagnosis only -- no fix without owner approval.

`MODE=poses ./run-tg2world.sh` pose `coast_from_sea_30m` (noon and golden) shows a
black horizontal bar over the sea, left of centre about 2/3 down the frame, plus two
thin dark arcs on the water below it. It is in the v3 run of 2026-10-03 07:47
(`FarBand/2026-10-03_074726_world/coast_from_sea_30m_noon.png`) and in the v4 run
(`FarBand/2026-10-03_161645_world/`), so it is NOT from B1. Not in KNOWN_ISSUES.md.

Find out what it is (far-band ring seam? a voxel-band chunk edge at sea level? the
sea surface's own mesh/LOD ring? a shadow?), using the existing pose and its `_band`
/ tier images; one extra poses run at most. Write the finding into KNOWN_ISSUES.md
(one index line in CLAUDE.md) with the image paths captioned. If the fix is obvious
and small, describe it and queue it for owner approval -- do not apply it.
