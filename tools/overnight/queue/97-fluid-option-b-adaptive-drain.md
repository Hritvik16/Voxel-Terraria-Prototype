# 97-fluid-option-b-adaptive-drain

Approval: owner, 2026-10-15 in the supervisor chat, on task 91's gameplay consequence (water ~half as fast at 30 FPS):
"Keep 4.5, raise after B". Queued by the supervisor 2026-10-15. Branch `engine-phase`.

## Change (one change)
Implement option B (adaptive GPU drain) from task 91's diagnosis (read `tools/overnight/done/*91-engine-17*` HANDOFF and
`FluidTickScheduler.cs`), so the fixed fluid rate can rise from 4.5 Hz to ~6.5 Hz with the same frame-rate independence.
Then set `FluidTickScheduler.DefaultRateHz` to the highest rate that keeps SHORTFALL <= 0.05/s at a 20 FPS cap (target ~6.5).

## Acceptance
- `FluidTickSchedulerTests` + new tests for the drain; fast suite while iterating, `SUITE=all` once at the end.
- The 91 rate table again (`-fluidstrip -fpscap 30/20`, arena): ticks/s and SHORTFALL at caps 30 and 20, before -> after;
  one strip image like `Screenshots/91/91-strip.png`. This IS a measured A/B (owner-approved): rule 9 warm-up applies.
- HANDOFF `## For the doc`: rate before -> after, shortfall at 20 FPS, the strip with a caption.
