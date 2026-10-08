# 33-B4-easy-areas

Requires: 29-B3-light-erosion

Surface rebuild step B4, EASY AREAS ONLY. Approval:
- Plan approved by the owner 2026-10-02 (rebuild plan Q11 (a)); rule 3 + the B4 row.
- Owner answers (plan section 8): Q1 (a) easy-area COUNT per world size Small 3 / Medium 4 /
  Large 6 (counts of separate areas; together they cover rule 3's 10% of land). Q2 (a) "easy"
  is decided by the LAND only (biome does not count).
- **Owner, 2026-10-04: NO placement until the terrain overhaul is done.** So the START half
  of B4 (choosing the start inside an easy area) is NOT in this task. `Tg2Spawn` stays the
  Playground placeholder. Do not add a start position, start flag or start list entry.
- Owner, 2026-10-08: B steps run back to back; review packs after B5 and after B10. No
  performance arms (PREAMBLE rule 10). Queued by the supervisor 2026-10-08.

Branch `surface-rebuild`. Terrain-generator change: `SUITE=all`. Read B3's report
(`tools/overnight/HANDOFF.md`, task 29) and B1/B2's record code first. v4 only; v3 stays the
default and byte-identical.

## The one change (v4 only) -- plan rule 3
Several easy areas are SEEDED IN THE LAYOUT BEFORE SHAPING (built, not hoped for). Each is a
gentle-class region kept in the LOWEST THIRD of the world's own land height range.
"Easy ground" = Walk band (map slope < 15 deg, PlayMetricsReport's provisional band) AND
gentle landform class AND lowest third of height, computed from the land alone.
- Record: set the `easy` flag on easy-ground cells (record flags byte, plan section 3), and
  fill the easy-area list (centre, area in ha, cells) carrying `mapVersion`.
- Count per world size from Q1 (the tg2 island's size class decides; say which).
- B2's gentle share (50 +- 5%) and B3's slope targets must still hold.

## Acceptance (seeds 42, 7, 2026, 31337; `PlayMetricsReport`, `REPORTS=1`)
- Easy ground **8-12%** of land, every seed.
- Easy-area count = the Q1 count for the size; each area **>= 25 ha**.
- **20-seed map-only sweep passes 20 of 20** (count, sizes, share). A failing seed must FAIL
  loudly, never silently pass.
- Still green from B2/B3: gentle share 50 +- 5%, all three classes, 0 inselbergs, needle guard
  on v4, B3's slope numbers reported beside B3 (land < 15 deg, > 45 deg, median, open, rivers).
- v4 + record hashes stable and seed-dependent; v3 unchanged; map build <= v3 + 2 s.
- `SUITE=all` 0 FAIL.

## Pictures
- Easy-area PNG per seed (easy cells over the region-class map), captioned.
- Seed 42: a walk pose at 2 m eye inside each easy area (eye on the exact ground, not in rock),
  plus one `MODE=poses` run on v4 read for holes, floating geometry, 90%-sea frames.
- A step whose own renders look clearly broken stops (STOP_FOR_OWNER); otherwise no stop.
