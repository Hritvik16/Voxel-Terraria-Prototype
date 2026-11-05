# 173-plan-world-gen-look

Model: opus

Approval: owner, 2026-11-05, chat: new order "look fantastic first" (lighting -> world gen -> visuals), see ROADMAP.md
"CURRENT ORDER". PLAN + CAPTURE only: no generator, shader or default change. No frame-time arms.

## Question
Where does today's island (tg2, map v4) fall short of "looks fantastic", and what are the candidate world-gen changes, in order?

## Do
1. Read `DESIGN_LARGE_WORLD_AND_TERRAIN_OVERHAUL.md` §2 and §12-15, the design brief's "Decided" rows (link in CLAUDE.md),
   and the memory rule `no-terrain-specific-engineering` (fixes must be general and data-derived, never tuned to this seed).
2. Capture a survey with the existing look-review tooling (midday, sun behind the camera, full-size 1080 shots): every biome x
   landform pair that exists, a coast, a mountain range from 200 m, a river valley, a cave interior, a cliff face, and the
   start area. Use flags exactly as play does today.
3. For each shot, name what looks weak (flat or repetitive shapes, noise artefacts, bland or wrong ground materials, caves,
   rivers, transitions between biomes, scale). Be concrete and honest; also name what already looks good.
4. Write `WORLD_GEN_LOOK_PLAN.md`: the findings, then 4-8 candidate changes in a proposed order, each with what it fixes
   (with picture), how it stays general (not seed-tuned), rough effort, and any expected render cost.

## End
STOP_FOR_OWNER with a full-size viewer of the survey (as 162's) and the plan's candidates as owner questions, each with
its own picture and a recommended option.
