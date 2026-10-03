# 17-B2-landform-by-flatness

Requires: 15-B1-mapv4-record-biome-scale

Surface rebuild step B2. Approval:
- The plan was approved by the owner 2026-10-02 (rebuild plan Q11 (a)).
- Rule 2 and the B2 row are from the plan, sections 1 and 4. Owner answers in section 8:
  Q7 (a) remove the inselbergs in v4; Q9 "not decided, needs testing": make
  gentleness a TUNABLE number, render 40% / 50% / 60% versions with images and
  numbers, and the owner picks. 50% is only the starting value.
- The generation pass line is Q8 (a), 2026-10-03.
- Queued by the supervisor 2026-10-03 after B1, per SUPERVISOR.md's rebuild order.

Branch `surface-rebuild`. This is a terrain-generator change, so run `SUITE=all`.
Read B1's report in `tools/overnight/HANDOFF.md` first: B2 builds on its v4 path
(`BuildV4`, `WorldRecord`). v3 stays the default and byte-identical.

## The one change (v4 only)
Landform by flatness (plan rule 2). The 10 archetypes are grouped into three
classes, and the layout assigns region classes to hit a land share:
- **Gentle:** river plains, rolling highlands, wetland basin, plateau tops.
- **Broken:** eroded breaks (was "badlands"), dunes, canyonlands.
- **Steep:** alpine, sea cliffs, volcanic.
- New spec field `gentleShare` (share of LAND in gentle-class regions), v4 default
  **0.50**. Broken and steep split the rest **3:2**.
- **Inselbergs off in v4** (`WorldMap.Inselberg`, the 40 of them; owner Q7 (a)).
  Leave the pinned peak and volcano sites alone: they become pool draws in B5.
- The record's landform archetype field (B1) must reflect the new assignment.
- Erosion is NOT changed here (that is B3). Biomes stay independent of landform.

v3 baselines from `Logs/TerrainGen/playmetrics/playmetrics.txt` (gentle share of
land): seed 42 26.7%, 7 41.6%, 2026 39.8%, 31337 27.3%.

## Acceptance
Seeds 42, 7, 2026 and 31337, from `PlayMetricsReport` (`REPORTS=1`) plus new tests:
- Gentle share **50 ± 5%** of land at the default, every seed.
- All three classes present on every seed.
- **0 inselbergs** on v4.
- Needle guard green on v4 (`StampTests.NoNeedles_OnTheMap`: no summit falls
  > 25 m within 40 m on every side; run it on v4 maps as well as v3).
- Slopes (median, <15 deg, >45 deg, open, mountainous) REPORTED next to v3, not gated.
- v4 + record hash the same twice and differ across seeds. v3 hashes unchanged.
- Map build time <= that seed's v3 time + 2 s.
- `SUITE=all` 0 FAIL.
- **Renders for the owner's pick (owner Q9):** seed 42 at gentleShare **0.40, 0.50
  and 0.60**, each with its numbers (gentle/broken/steep share, median slope,
  <15 deg, >45 deg, open, easy ground). Region-class PNG + slope-band PNG per
  variant, v3 beside them. The four seeds at 0.50 (region-class PNG).
  Every image gets a caption in HANDOFF saying what to look at.
- **Walk poses in each class:** one screenshot at walking height in a gentle, a
  broken and a steep region on v4 (seed 42), captioned. Read them before concluding.
- **Streaming re-measure (heavy, player builds; owner Q8).**
  - Task 10's `MODE=stream` probe: walk x2 and fly 14 m/s x2 on v4, back to back on
    the SAME build with the same arms on v3 (`MAPV=4` vs default).
  - Report every stage against v3 as better / worse / unchanged: worker gen p50/p95,
    isolated gen, chunks/s, request->on-GPU, deficit, upload.
  - **Pass line: worker chunk-gen p50 on v4 no worse than v3 on the same build by
    more than 5%.** If worse, say by how much and STOP_FOR_OWNER.
  - Then one `MODE=poses` run on v4. Read the screenshots for holes, floating
    geometry or a 90%-sea frame; caption the ones you cite.
  - Check `pmset -g batt` (task 12) and the owner-paused NOTE (tasks 14/14b). Rerun
    any arm that was on battery or paused.
- Do not stop for the owner over the gentleness pick. Ship 0.50, and put
  "Owner pick: gentle share 40/50/60%" with the image paths and the numbers table
  at the top of HANDOFF (no STOP_FOR_OWNER).
