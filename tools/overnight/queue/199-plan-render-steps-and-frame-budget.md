# 199-plan-render-steps-and-frame-budget

Model: opus

Approval: OWNER, chat 2026-11-13 (performance talk, `tools/overnight/OWNER_TOPICS.md` topic 3). Answers:
- P1 (a): this M1 Air (8 GB, fanless) is the MINIMUM machine: 30 fps on a lower preset; the full look aims at stronger machines.
- P2 (a): rebuild the drawing into SEPARATE STEPS now, before reflections / more lighting -- written up for the owner's
  approval BEFORE any code (an architecture change).
- P3 (a): a FRAME BUDGET per feature: every look feature reports its cost against its share.
- P4: the "cinematic" look the owner wants: MODDED MINECRAFT (shader packs) and LAY OF THE LAND (Steam game by Tooley,
  same 0.1 m cube look; has a YouTube devlog).

**PLAN ONLY. No code, no default change, no Unity launch.** This task writes ONE Claude Doc for the owner (allowed here
explicitly, SUPERVISOR.md "Planning without code") plus a repo copy `AMENDMENT_8_17_RENDER_STEPS.md` marked
**DRAFT, NOT ADOPTED**. Numbers come from existing logs only (cite each); arithmetic is marked "est.".

## Why (the supervisor's summary the owner agreed to)
`PERF_EXACT_CUTS.md` section 1: work removed INSIDE `CSMainBody` (~5,600 lines) saved ~0 ms (116/93b5/153/137, 124's half
the sun rays), while moving work OUT of it or compiling OFF code out saved 4-7 ms each (132 R1, 134 R6). Its section 3: even
every exact cut leaves p95 ~45-55 ms vs 33. Every coming look feature (water reflections, metallic / ore shine, more light)
would land in the same kernel. So the split is done ONCE, before them, and each later feature is its own step with its own cost.

## The doc must contain
1. **Presets for the two machine classes (P1).** What the M1 Air preset keeps and drops vs the full look, framed as "the
   full look on stronger machines", not as cuts of today's picture (owner, 2026-10-31, rejected half-res shells, temporal
   shell reuse, cheaper upscaler, shells off, far shadows off AS THE DEFAULT LOOK -- check how that answer applies to a
   minimum-machine preset and say so plainly; if it is unclear, it becomes a question). Say honestly that only this Mac
   exists for measuring: how the full preset would be judged (e.g. a scaled GPU estimate) and the risk.
2. **The frame budget (P3).** 33.3 ms split into shares per step for the M1 Air preset (CPU + floor, primary march,
   sun shadows, sky light, shells, far band, upscaler, shading/post, and RESERVES for reflections, characters/NPCs,
   fluids, torches at night). Start from the measured cost splits (146 `Logs/costsplit_*`, 133, 115, 168e) and say
   which shares are over today. Rule for future tasks: how a look task reports its cost against its share with the
   runs it already does (CLAUDE.md "look tasks record a rough cost").
3. **The render steps design (P2).** Today's passes and the target ones (e.g. surface march -> sun shadow -> sky
   light -> shading -> reflections -> post), what each step reads and writes (the in-between screen buffers, their
   formats and MEMORY at 432 / 540 / 1080 rows, against the 3 GB cap), which existing plans are its first steps (C1, C2,
   B in `PERF_EXACT_CUTS.md`), how each step is proven EXACT (raw no-upscaler stills 0 px, kernel-twin tests, as 132/134),
   what it does NOT touch (frozen APIs, CPU sole writer, memory layout of the mirror). An ordered task list, one change
   per task with its checkpoint, and an est. ms range per task with confidence. Where reflections and metallic / ore
   shine (owner topic 4) plug in.
4. **The cinematic look (P4).** Look up modded-Minecraft shader packs (the well-known ones) and Lay of the Land
   (store page, devlog) and list the techniques that make the look (bounce light, soft shadows, haze / aerial
   perspective, volumetric light, reflections, bloom, colour grading, depth of field, ...): which we have, which are
   missing, which render step each belongs to, and its share of the budget. Link reference pictures (do not re-host
   other games' images); put OUR stills from existing `Screenshots/` folders beside the list where they show the gap
   (look at each image first; captions say what to look at).
5. **Questions for the owner** at the end, each specific, recommendation first, what each answer costs.

## End
HANDOFF with the doc link and its `## For the doc` block (one-line result, the questions). No STOP_FOR_OWNER needed:
the questions go to the supervisor through HANDOFF. Commit the repo copy. No tests needed (no code).
