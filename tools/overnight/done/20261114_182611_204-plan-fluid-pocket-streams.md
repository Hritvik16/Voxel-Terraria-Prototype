# 204-plan-fluid-pocket-streams

Model: opus

Approval: OWNER, chat 2026-11-13, W1 (e): ponds FED BY STREAMS -- "for all the fluid pockets" (water, lava, acid, oil, ...).

**PLAN ONLY, no code.** Streams are FLOWING fluid, unlike today's pockets (at rest by construction, 183): that touches the
fluid simulation (tick 6.5 Hz since 91/97, sim reach 8.15, fluid fall 8.16, GPU fluid CA) and world gen (`Rivers.cs`,
`Hydrology.cs`). Write the options into HANDOFF (no doc) for the supervisor to turn into an owner question:
1. How rivers are made and drawn today, and whether a small stream can be the same thing scaled down (a carved bed with
   water at rest in steps, the river way) vs truly flowing in the sim (cost: awake tiles, tick budget, memory).
2. Where a stream comes from for each fluid (a spring upslope, the catchment on the 8 m map; lava from a vent; ...) and
   where the pond's surplus goes (outflow, seep, none) so the pond stays at rest.
3. For each option: what the player sees (cite existing river / lake screenshots by path, look at them first), the gameplay
   effect (sim reach, can the player divert it), the cost (memory, sim tick, generation ms est.), and the risk.
4. Recommendation first. Frozen APIs / amendments the options would touch, named.

## End
HANDOFF with `## For the doc` (one-line result + the question with options). No STOP_FOR_OWNER.
