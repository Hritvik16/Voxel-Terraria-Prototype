# 176d-worldgen-ship-biome-size-2

Model: opus

Approval: owner, 2026-11-06, chat on 176c Q1: "Allow one big area" = (a): the C1 gate "every biome in 2+ separate regions of >= 25 ha"
becomes "every biome PRESENT (>= 1 region of >= 25 ha)"; Size 2 ships. 176c Q2: the known route test stays red until C5 (owner, again).
Read `tools/overnight/blocked/176c-worldgen-apply-biome-size-2.HANDOFF.md` and its patch (`Screenshots/176c/size2_176c.patch`, also
`tools/overnight/blocked/176c-worldgen-apply-biome-size-2.patch`). 177 (cover palettes, `c89f7de`) and possibly 178 (cliffs) landed since;
apply on top of them.

## Do
1. Apply 176c's patch (Size 2: `CLIMATE_WAVE_R` 1.5 + `CLIMATE_OCT4` 0.12 + the `BlendClimateReach` bound). Change ONLY that one gate in
   `BiomeLayoutTests` as the owner said, with a comment citing this decision. Every other C1 gate unchanged.
2. Re-pin what legitimately moves (ShellFill GOLDEN; WorldMapBurstTests player hash from a PLAYER log, as 176 did) and say why.
3. WHOLE suite. Allowed red: the Slow route test for the known river-ford reason (176c: now seed 31337, 1.5 m Sheer); update its
   KNOWN_ISSUES line (seed / numbers). Any other red = stop.
4. One `-worldsurvey -wsfixed` capture of 176/177's poses: before (176 size) | after (Size 2), full size, plus the 4 seed maps.
   Rough cost note; no timing arms.

## End
Done (no owner stop) unless a gate other than the changed one fails. HANDOFF with the maps + pose sheet "for the doc".
