# 107-gameplay-step-11-crafting-v1

Requires: 105-gameplay-step-10-tool-tiers
Model: sonnet

Approval: owner, 2026-10-11 ~00:10 in chat: "keep queuing tasks, ideally moving forward with gameplay features instead of
optimizations, pick your recommendations"; gameplay alternates with engine tasks (Q50 (4)). Queued by the supervisor
2026-10-17. Branch `engine-phase`.

## Change
Implement **step 11** of `GAMEPLAY_PHASE_PLAN.md` ("Crafting v1: recipe table, craft panel, no stations"), nothing else.
Read task 105's HANDOFF (`tools/overnight/done/105-*.HANDOFF.md` or `tools/overnight/HANDOFF.md`) first: crafting is how
the player GETS 105's better tool tiers, so v1's recipes must at least turn task 66's ores (copper/iron/gold/cobalt/
emberstone) plus dug materials into 105's tools, tier by tier. Recipes live in a table under `Assets/ContentModules/`
(guardrail 8); the panel works on the 36-slot inventory (task 64; E bag). No crafting stations (that is a later step).
Obey the plan's guardrails (section 2): frozen APIs, no memory-layout change, rigs/capture modes unchanged, play options
in PlaySettings + F6 + a flag, new keys in F2. Where the step leaves a tuning or look choice (panel layout, key, recipe
numbers), pick the recommended option yourself and list it under "Picks (owner may change)". Smelting / ore -> bar is
your pick (one-step recipes are fine for v1). STOP_FOR_OWNER only for a frozen-API change, a memory-layout change,
anything on `main`, or a step that does not fit one task (then do the first useful half and say what remains).

## Acceptance
- EditMode tests (`CraftingTests`): a recipe consumes exactly its inputs and adds its output; missing input -> refused,
  nothing consumed; full inventory -> refused or overflow rule stated (P8 says digging overflow is lost -- crafting must
  NOT silently lose a crafted item); the recipe table loads and every 105 tool tier has a recipe path from dug materials.
- Fast EditMode suite green (`SUITE=all` only if anything under `Assets/TerrainGen/` changed).
- No frame-time measurement. Screenshots (one launch, few poses, short waits, midday with the sun behind the camera):
  the craft panel open with some recipes craftable and some not, and the inventory after crafting a better tool.
  Captions say what to look at. Update the F2 help panel.
- HANDOFF `## For the doc`: result, what the owner can now do in `./run-play.sh`, the recipe table (short), picks, images.
