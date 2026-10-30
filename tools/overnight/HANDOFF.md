STOP_FOR_OWNER -- task 155 (torch light plan) needs 6 answers before step L1 can be queued. Recommended answer first.

1. **May torch light go round corners, with no crisp torch shadows?** (a) REC: yes, Terraria-style. Light flows through
   doorways and round tunnel bends and fades with walking distance. Pillars cast no sharp torch shadow; the sun keeps its sharp
   shadows. (b) No: straight-line light with crisp shadows (candidate B). Est. +2 to +8 ms at 12-15 m, still capped at 32 lights.
   Picture: `Screenshots/143/torchreach143.png`, top row (built tunnel), right column: the torch lights the straight tunnel.
   Under (a) a bend in that tunnel would also be lit round the corner.
2. **Reach?** (a) REC: 12 m (memory grows with reach squared). (b) 15 m. (c) 10 m.
   Picture: the same sheet, columns "half a tunnel (~6 m)" vs "10-15 m tunnel section". Compare how much of each row is lit.
3. **How many torches must one base hold, all lit?** (a) REC: 200 inside the 0.1 m window, pool capped at 48 MB GPU + 48 MB CPU.
   Past the cap, the furthest fall back to today's 2.9 m glow. (b) 32 (today, ~12 MB). (c) 500+ (~100+ MB per side; tg2 is
   already over the 3 GB cap). Picture: the same sheet, bottom row (open ground), right column: how far one outdoor torch
   spreads. Outdoor torches are what fill the pool.
4. **Coloured light?** (a) REC: yes, RGB; where two colours overlap, the brighter of each channel wins (red + blue reads
   magenta-ish). The blue/green/purple torch materials already exist. (b) Warm only (saves ~1/3 of the pool). No picture yet.
5. **Fill-in delay?** (a) REC: a placed/broken torch or a sealed doorway updates in 1-3 frames (~0.1 s), and a large base that
   streams in lights up over ~1 s. (b) Always the same frame: main-thread hitches of 5-15 ms on big outdoor placements. No picture.
6. **Torch light beyond the 0.1 m window (59-116 m, the shells)?** (a) REC: keep today's cheap unshadowed glow there for the 32
   nearest torches; real far light later (step L7). (b) None until L7: a torch's glow appears only once you are within ~60-115 m.

# 155-plan-cheap-torch-light: DONE (plan only) -- recommends A, a light flood exact at 0.1 m, sampled once per pixel

## What changed (branch engine-phase)
- `46e4c4f`: new `TORCH_LIGHT_PLAN.md`; `TASKS.md` gains a "Task 155" paragraph. No product code, no default/look change, no launch.
- Candidates: **A** light flood (6-connected air, no corner cutting, chamfer distance, per-channel max RGB, sparse per brick: 0.1 m
  in mixed bricks, 0.4 m in air bricks; worker job + main-thread commit, the same pattern as chunk gen). It is the spec's own
  §3.8/§6.5 "v1.5 sparse BFS light pool". **B**: per-brick light lists + 1 temporally reused torch ray per pixel (today's look,
  but its cost still goes with reach). **C**: a 0.8 m brick flood (leaks through 0.1 m walls; kept as the later far/shell light).
- FLAGGED in the plan (design change under §0): A adds a derived light pool + per-chunk slot table (GPU + CPU) and needs an
  amendment for §3.8/§6.5's "no propagation, no BFS". No frozen API changes (GetVoxel / SetVoxel / D.1 / §4.4), and light is
  never saved. Phase7CycleRig Gate 1 falloff and `RaymarchLightingTests`' point-light expectations need flood versions under
  the flag; the OFF path keeps today's assertions.

## Evidence
- Inputs only (no new runs): `Screenshots/151/torchcost151.txt` (8.5 m torches +5.7 / +8.3 ms vs none; 14 m +2.6..+23.6 and
  24 m +15.3..+23.6 ms over 8.5 m), `Screenshots/143/torchreach143.txt` (model reach per arm), `Raymarch.compute`
  `SumPointLightsShadowed` / `TorchRayBlocked`, `LightSourceBuffer` (32 cap, lava tile aggregates), SOFT_SKY_PLAN §2C.
- Looked at `Screenshots/143/torchreach143.png`: one torch at night. The right column lights the whole 5 m room and the
  straight tunnel; today's column is a pin-prick.
- EditMode suite NOT run: no code changed (doc + TASKS.md only), so every outcome of a run leads to the same action.

## Next step
After the answers: queue L1 (CPU oracle `LightFloodReference` + tests 1-6 + a metrics report: MB per torch, split-cell share
at 0.2 m, worker ms per flood). No GPU change and no perf arm in L1.

## For the doc
- Today's torch cost (151): 22-31 torches at 8.5 m cost +5.7 / +8.3 ms p50 vs none; a 14 m range costs +2.6..+23.6 ms more.
- Plan A estimate (unmeasured): +0.3..+1.0 ms per frame, flat in torch count and reach; net est. -5..-8 ms vs today at 32 torches.
- Plan A pool est.: 32-torch base 8-12 MB; 200-torch base ~40 MB; capped at 48 MB GPU + 48 MB CPU.
- Update est.: 1-4 ms (room) / 5-15 ms (outdoor) of worker time per torch placement.
- Image: `Screenshots/143/torchreach143.png`. Caption: "One torch at night, 4 reaches (columns) x tunnel / sealed room / open
  ground (rows). The right column is the 10-15 m look the plan aims for, at a flat per-pixel cost."
