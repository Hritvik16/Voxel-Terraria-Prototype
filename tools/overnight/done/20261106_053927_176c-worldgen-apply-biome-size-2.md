# 176c-worldgen-apply-biome-size-2

Model: opus

Approval: owner, 2026-11-05, chat: "size 2" (176b's recommended option). WORLD GEN, look stage. Read
`tools/overnight/blocked/176b-worldgen-biome-size-choice.HANDOFF.md` (its "Applying the size" notes) and `Screenshots/176b/sizes_176b.txt`.

## Do
- Apply Size 2 as the generator default: `CLIMATE_WAVE_R` 0.75 -> 1.5 and the 4th climate octave (amplitude 0.12) in `ShapeField.Climate3`,
  with the matching bound in `BlendClimateReach`. `-biomevary 0` stays B10 bit for bit.
- `BiomeLayoutTests` gates stay as they are (Size 2 passes them per 176b; if one fails, report, do NOT loosen).
- Prove the generator now produces 176b's Size 2 plane (seeds 42/7/2026/31337 vs `Logs/TerrainGen/c1/sizes/`, Burst-vs-Mono residue only).
- Re-pin goldens that legitimately move (ShellFill GOLDEN, WorldMapBurstTests player hash read from a PLAYER log as 176 did) and say why.
- WHOLE suite (`SUITE=all`). The Slow route test may stay red for the KNOWN reason (river ford bank, until C5); any OTHER new red = stop.
- One `-worldsurvey` capture of 176's poses for a before (176) | after (Size 2) sheet. Rough cost note; no timing arms.
- If 177 (biome cover palettes) has already landed, rebase cleanly on it; its acceptance shares may shift slightly -- report, don't retune.

## End
Done (no owner stop needed) unless a gate fails: HANDOFF with the 4 seed maps (after) and the pose sheet for the doc.
