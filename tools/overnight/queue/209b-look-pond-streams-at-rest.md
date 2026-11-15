# 209b-look-pond-streams-at-rest

Model: opus
Requires: 209-look-pond-shore-coast-rules (same files; run after it)

Approval: OWNER, 2026-11-14, Queue decisions doc Q-AG answer "a": still streams for every fluid except quicksand, built like the
rivers. Owner's words: "still stream but looks like it's flowing with some effects maybe but that's a stretch goal. Pond overflow
streams is exactly what I was thinking, just to make the world and ponds feel more natural."

## Do
Build 204's recommendation (read `git show 8a719b1:tools/overnight/HANDOFF.md` first; follow it): narrow (0.6-1.2 m) carved
channels built the way `Rivers.cs` builds rivers, AT REST (no fluid-simulation cost), for every fluid except quicksand. A pond's
overflow leaves as an outlet stream; with no way out it seeps. Inflow from the uphill side as 204 plans. Looks-flowing effects
(foam steps like the rivers have) only if free; no new animation systems (stretch goal, do not build).
Switch `-pocketstreams 0` = previous world byte for byte; default ON if tests and pictures are good. Memory budget per 204
(~128 KB per world); PREAMBLE rule 11 report vs OFF. SUITE=all (TerrainGen). No timing arms.
## Pictures
Full size, `viewer.html`: 4+ ponds with streams (different fluids), OFF vs ON, one aerial view; captions say where to look.
