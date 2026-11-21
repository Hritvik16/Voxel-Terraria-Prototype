# 177b-worldgen-palette-tweaks

Model: opus

Approval: owner, 2026-11-06, chat on 177: Q1 tweaks ALL THREE: "Desert cliffs: one colour", "Savanna: softer red earth", "Badlands: no stripes";
Q2 high ground stays the top third (as shipped); Q3 snow flecks kept (snow slides like sand). Read `tools/overnight/blocked/177-worldgen-c2c3-biome-cover-slots.HANDOFF.md`,
`Assets/TerrainGen/Cover.cs` PALETTE, and 178's result if it has landed (178 does cliff layers; "no thin stripes in any biome").

## Do (palette DATA only where possible; the rules stay general)
- Desert `steep`: drop the cream-sandstone patch variant -> one colour (orange sandstone).
- Savanna `flat`: the red-earth patch variant becomes subtler (lower share and/or a less saturated earth tone that still maps in
  `FarField.GenToEngine`); report the share before -> after.
- Badlands: no thin stripes anywhere it shows bare rock (`steep`, `high` variant). Use plain thick rock, consistent with 178's thick
  layers (owner disliked thin striped strata, 2026-10-01). If the stripes come from the badlands depth layers rather than the palette,
  remove them there, and say so.
- WHOLE suite (TerrainGen). Known route red allowed. Re-pin goldens that move.
- Before | after shots of the affected poses (desert peak `great-peak-0`, `forest_river-plains`/savanna pose, a badlands face), full size,
  plus the updated palette swatch picture. Rough cost note; no timing arms.

## End
Done (no owner stop) -- HANDOFF with the before|after pictures for the doc.


## Supervisor note (2026-11-06 08:xx)
First run (07:00-07:38) was aborted mid-task (tool aborted, no handoff); its uncommitted edits are saved in tools/overnight/blocked/177b-worldgen-palette-tweaks.patch (Cover.cs, DetailField.cs, CliffTests.cs, tools/cover177.py) -- reuse if useful, Screenshots/177b/ has its partial renders.
