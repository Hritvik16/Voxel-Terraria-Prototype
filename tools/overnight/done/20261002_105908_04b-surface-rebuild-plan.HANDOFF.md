STOP_FOR_OWNER -- task 04b: the surface rebuild plan is written. It needs your approval and
11 answers before any B step is queued. Plan: https://claude.ai/code/artifact/f6222d44-1457-4a92-9460-3390d44e75fd

Questions (verbatim from the plan's section 7; answer like "1a 2a 3b"):
1. How many easy areas per world size? (a) Small 3, Medium 4, Large 6 · (b) 2 / 3 / 4, bigger areas · (c) 4 / 6 / 8, smaller areas
2. Does biome count in "easy"? The brief is Leaning on land only. (a) Easy is land only, but the start prefers forest or savanna when scores are close · (b) Land only everywhere; the start ignores biome too · (c) Easy areas must also be a mild biome (forest or savanna)
3. Which easy areas does a route join? (a) A spanning tree plus extra links, so every area has two ways out · (b) A spanning tree only: one way between any two areas · (c) Every area to its two nearest neighbours
4. How much safer is a route? (a) A 60 m corridor, one danger tier lower, eased to Scramble at worst · (b) A 120 m corridor, one tier lower · (c) A 40 m corridor, two tiers lower
5. How does a player find a route? (a) Routes follow valleys and passes with the next waypoint in sight. The record flags them so placement can add cairns or ruins later. · (b) A visible landmark at each end only · (c) A worn trail on the ground now (a surface-material change along the centre line)
6. What replaces the "landmark in every direction within 1.5 km" rule (§2.5 rule 1, kept 2026-09-25)? (a) A signature feature visible within 3 km from 80% of land · (b) Keep the every-direction rule (needs a massif about every km) · (c) Report only, no gate
7. The 40 inselbergs? (a) Remove them in v4; the pool replaces them · (b) Keep about 10 as minor landmarks · (c) Keep all 40
8. Town and village sites on a Medium world (Small half, Large double)? (a) 12 towns and 24 villages · (b) 8 and 16 · (c) 20 and 40
9. How gentle overall? (a) 50% of land in gentle landforms; under 15° at least 50%, steeper than 45° under 10% (the review's proposed targets) · (b) Gentler: 60%, at least 60%, under 5% · (c) Rougher: 40%, at least 40%, under 15%
10. Savanna and badlands biomes (Decided as the next two)? (a) In this rebuild as B10, after B9, with palette colours for your sign-off · (b) After the rebuild, in their own plan · (c) Early, in B1, so easy land gets savanna from the start
11. Approve and order? (a) Approve the plan; queue B0 now (EditMode only, no build), then B1 onward after the TAA tasks 05-08 · (b) Approve; all of B after TAA · (c) Approve; B before TAA · (d) Changes first (comment on the doc)

# Overnight handoff

**04b-surface-rebuild-plan: DONE (planning only). One new Claude Doc, "Surface rebuild plan".**

## What changed
- New Claude Doc (link above). No code, no Unity, no builds, nothing under Assets/.
- `tools/overnight/HANDOFF.md` (this file), committed on design-brief. No implementation tasks queued.

## The plan in brief
- B rebuilds the surface layout-first as `TerrainSpec.mapVersion = 4` (an opt-in `BuildV4`).
  v3 stays byte-identical. Nine rules, one number each: biome scale x3, gentle-landform
  share 50%, easy land 10%, start (1 ha flat + water within 300 m), erosion 240 -> 60
  iterations, the signature pool (S3/M5/L7), towns (M 12 + 24 villages), base danger
  (altitude thirds + landform class + biome tier, 0-7), routes (corridor danger -1 tier, 60 m).
- No walls and no distance zones (owner corrections). B6 gates on the danger-vs-distance
  correlation being at most 0.3 in size.
- World record: about 6 MB of CPU byte arrays per 8 m cell (landform + depth, biome,
  water distance, danger + band, flags, site id) plus site lists. It is never saved and
  regenerates from the seed.
- Steps B0-B9: B0 = a report-only metrics harness that must reproduce the review's seed-42
  numbers. B1-B8 = one rule each, gated on numbers over seeds 42/7/2026/31337 (+ a 20-seed
  sweep at B4/B8). B9 = an in-engine walk/poses evidence run against v3.
- "Do now" items: all three are done (abilities in the brief; memledger + thick layers + 750k
  pool; walk measured). Before B1 there is one evidence run: a quiet-machine v3 `MODE=poses`
  baseline (memory peak, dense bricks per pose).

## Evidence
Sources read: the review's §3-7, §11, §13 and §14; the whole brief; the design doc §2 and
§12-15; the KNOWN_ISSUES tg2 entries; `WorldMap.BuildV3` and `Regions`/`ShapeField.Biome`/
`TerrainSpec` (grep). Not measured: every acceptance number in the plan is a target, not
a result.

## Next step
Owner answers the 11 questions and approves. The supervisor then queues B0 (and the v3
poses baseline) as separate tasks.
