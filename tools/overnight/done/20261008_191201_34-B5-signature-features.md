# 34-B5-signature-features

Requires: 33-B4-easy-areas

Surface rebuild step B5. Approval:
- Plan approved by the owner 2026-10-02 (Q11 (a)); rule 6 + the B5 row. Q6 (a): the old
  "landmark in every direction within 1.5 km" rule is replaced by "a signature feature
  visible within 3 km from 80% of land". Q7 (a): inselbergs removed (done in B2).
- Game Design Brief, "The pool" (Decided 2026-10-01): each world draws its big landmarks
  from a pool; natural landforms, exaggerated for drama; not every world gets every feature;
  the volcano is one entry, not a fixture. Per world: Small 3, Medium 5, Large 7 features;
  rare wonders up to 1 / 1 / 2; every world gets at least one WAY-DOWN feature (canyon,
  sinkhole or sky cavern).
- Pool tiers (brief): great peak Common, great canyon Common, giant waterfall Common, table
  mountain / mesa Common, sea arches and stacks Common, volcano Uncommon, crater lake
  Uncommon, sinkhole / cenote Uncommon, underground sky cavern Rare wonder, colossal stone
  arch Rare wonder.
- **This step uses ONLY shapes that exist today: great peak, volcano, mesa, canyon, crater
  lake.** New shapes (waterfall, sea arches, sinkhole, sky cavern, arch) are later tasks. So
  the only way-down feature available is the canyon: every world draws at least one.
- Owner, 2026-10-08: B steps back to back, NO performance arms (PREAMBLE rule 10). After this
  step the supervisor builds the owner's B5 REVIEW PACK from this task's pictures (below).
- Queued by the supervisor 2026-10-08.

Branch `surface-rebuild`. Terrain-generator change: `SUITE=all`. v4 only; v3 byte-identical.

## The one change (v4 only) -- plan rule 6
- Remove v4's pinned peak and volcano sites; instead draw features from the pool (seeded,
  tier-weighted, Common > Uncommon), count by size class (Medium 5).
- Place them far apart (**>= 1.2 km**) and where ridges hide them until you crest a rise,
  while keeping the 3 km visibility share below.
- Record: feature list (kind, centre, footprint radius, way-down flag; carries `mapVersion`),
  `feature footprint` flag and site id on footprint cells (plan section 3).
- Features must not break B4's easy areas (keep them out of easy-area cells) or leave spikes.

## Acceptance (seeds 42, 7, 2026, 31337)
- Medium gets **5 features**, **<= 1 rare wonder** (0 expected with today's shapes),
  **>= 1 way down** (a canyon). Features **>= 1.2 km apart**.
- Share of land that sees a feature within 3 km from a 2 m eye: **>= 80%** (say the method:
  ray samples per cell, grid spacing).
- Needle guard green on v4; signature peaks stay broad.
- B4's easy-ground share and counts still pass; B2/B3 numbers reported.
- Hashes stable and seed-dependent; v3 unchanged; map build <= v3 + 2 s. `SUITE=all` 0 FAIL.

## Pictures (also the owner's B5 review pack -- caption every one)
- A shot of each feature from ~2 km at 2 m eye and one from the far band (seed 42), eye on
  the exact ground.
- Feature map PNG per seed (features over the region-class map).
- Seed 42 side by side **v3 | B2 | current**: region-class PNG and slope-band PNG.
- Walk poses at 2 m eye at B2's three spots (plains 4772,5580; canyonlands 4428,2764;
  alpine 3948,3932), B2 beside current (B2 shots are in B2/B3's evidence).
- A per-class slope table for the four seeds: gentle / broken / steep median slope, land
  < 15 deg, > 45 deg, open land, easy ground -- v3, B2 and current.
- One `MODE=poses` run on v4 read for holes, floating geometry, 90%-sea frames.
- Put "B5 REVIEW PACK" at the top of HANDOFF with every image path and the table. A step
  whose own renders look clearly broken stops (STOP_FOR_OWNER); otherwise no stop.
