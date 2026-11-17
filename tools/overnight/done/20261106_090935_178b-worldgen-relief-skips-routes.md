# 178b-worldgen-relief-skips-routes

Model: opus

Approval: owner, 2026-11-06, chat on 178 Q2 / 176d Q1: "Keep routes smooth" = (a): cliff relief and the scree apron skip route cells, the
same way they already skip water. Read `tools/overnight/blocked/178-worldgen-c4-cliff-geology.HANDOFF.md` (Q2) and
`tools/overnight/blocked/176d-worldgen-ship-biome-size-2.HANDOFF.md` (Q1, `Logs/overnight/176d_probe_heights.txt`).

## Do
- Expose the world record's route flag to `MapView` and make 178's face relief + scree apron skip route cells (and blend out over a few
  metres so no new step forms at the edge). General: driven by the record, no seed-specific constant.
- Proof: seed 42's scree steps at (3777, 5278) [176d] and (4471, 2390) [178] are no longer Sheer; `-cliffs 0` vs on is unchanged off-route.
- WHOLE suite. The Slow route test may stay red ONLY for the known river-ford bank (seed 31337, until C5); report its exact message
  (incl. 178's note that seed 42's "next waypoint in sight 61.9 % < 65 %" was already failing at 177 -- check whether it is still there,
  and if so explain it, don't fix it here). Update the KNOWN_ISSUES line.
- Do NOT change the cliff LOOK (the owner is still judging 178's pictures). No timing arms.

## End
Done (no owner stop) unless a new red appears.
