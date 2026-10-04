# 15-B1-mapv4-record-biome-scale

Requires: 14-heavy-pause-toggle 14b-pause-drain-only

Surface rebuild step B1. Approval:
- The plan was approved by the owner 2026-10-02 (rebuild plan Q11 (a)).
- The B1 pass line is the owner's Queue-decisions answer Q5 (a), 2026-10-03.
- The generation pass line is Q8 (a), 2026-10-03.

Branch `surface-rebuild` (made from `main` 2026-10-03). This is a terrain-generator
change, so run `SUITE=all`.

## The one change
Add map version 4 as an OPT-IN copy of v3, plus the world record, plus larger biomes.
- **v3 stays the default and byte-identical.** Existing v3 hashes and determinism
  tests must not change.
- `TerrainSpec.mapVersion = 4` -> `WorldMap.Build` dispatches to a new `BuildV4`. It
  is v3's path plus only what B1 adds.
- Note the existing `mapVersion >= 3` checks (`ShapeField.cs:125`, `WorldMap.cs:91`).
  v4 must keep v3's behaviour everywhere B1 does not change it.
- **Biome scale.** Multiply the two climate-field wavelengths (today 15 km and
  12 km), which drive `ShapeField.Biome`, by a `biomeScale` spec field.
  - v4 default ×3. v3 is effectively ×1 and unaffected.
  - Biomes stay independent of landform (brief, owner 2026-10-01).
- **World record** (`WorldRecord`, CPU only, built once with the map, never saved;
  it regenerates from the seed). One entry per 8 m map cell:
  - landform archetype (4 bits) + depth into its region 0-15 (4 bits), from today's `Region`;
  - biome (4 bits; 8 biomes, room for 16);
  - distance to fresh water, one byte in 16 m steps up to 4 km. The water class is
    kept from today's `WaterClass`.
  - Allocate but leave zero for later steps: base danger 3 bits + movement band 2 bits
    (B6), flags 8 bits (B4-B8), site id 8 bits (B5-B8).
  - Query: `WorldRecord.At(x, z)`.
  - It must be a pure function of the seed (co-op guardrail).
- **In the player:** a way to run v4, e.g. `-mapversion 4` with `-world tg2`, and
  `run-tg2world.sh` passing it through (`MAPV=4`). The default stays v3.

## Acceptance
Seeds 42, 7, 2026 and 31337, all from the EditMode `PlayMetricsReport` (`REPORTS=1`)
plus new tests:
- **Biome change walking east-west: at least 2.5× that seed's v3 value** (owner Q5).
  The v3 values are in `Logs/TerrainGen/playmetrics/playmetrics.txt`; seed 42 is
  297 m, so its target is >= 740 m.
- Median biome patch >= 50 ha. Report it for v3 too.
- Record <= 7 MB (report the exact bytes). Map build time <= that seed's v3 time + 2 s.
- v4 + record hash the same twice and differ across seeds. v3 hashes are unchanged.
- `SUITE=all` 0 FAIL.
- **Maps for the owner's pick:**
  - biome PNGs of seed 42 at ×2, ×3 and ×4;
  - the four seeds at ×3;
  - v3 beside them for comparison.
  Every image needs a caption in HANDOFF saying what to look at, e.g. "left v3:
  patches ~300 m; right ×3: patches ~1 km; look at the north coast". Owner, 2026-10-03.
- **Streaming re-measure (heavy, player builds; owner Q8).**
  - Use task 10's `MODE=stream` probe: walk ×2 and fly 14 m/s ×2 on v4, back to back
    on the SAME build with the same arms on v3.
  - Report every stage against v3 as better / worse / unchanged: worker gen p50/p95,
    isolated gen, chunks/s, request->on-GPU, deficit, upload.
  - **Pass line: worker chunk-gen p50 on v4 is no worse than v3 on the same build by
    more than 5%.** If it is worse, say by how much and STOP_FOR_OWNER.
  - Then one `MODE=poses` run on v4. Read the screenshots for holes, floating geometry
    or a 90%-sea frame, and caption the ones you cite.
  - Check `pmset -g batt` (task 12) and the owner-paused NOTE (tasks 14/14b). Rerun
    any arm that was on battery or paused.
- Do not stop for the owner over the scale pick. Ship ×3, and put "Owner pick: biome
  scale ×2/×3/×4" with the image paths at the top of HANDOFF (no STOP_FOR_OWNER).

## Owner answer (2026-10-03, chat, after the STOP_FOR_OWNER at 17:49)
"[The 5% rule] is a bad rule. Remove it. How fast chunks load and unload should be
mathematically derived, not set arbitrarily like this. And we want to ensure that
this metric is met."
-> The relative 5% pass line is removed, so B1's only blocker is gone. B1's code
(d1e5587) stands; its streaming numbers are recorded. Task 16b derives the absolute
streaming requirement and the gap; B2 (17) reports against it. Q11 (patch-median
reading) and Q12 (biome scale pick) stay open in the Queue decisions doc; neither blocks.
Closed by the supervisor; moved to done/.
