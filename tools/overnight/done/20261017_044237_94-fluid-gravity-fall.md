# 94-fluid-gravity-fall

Approval: owner, 2026-10-15 in the supervisor chat (a DESIGN CHANGE to ARCHITECTURE §7.4, owner-approved): "water should
fall much faster and all fluids should fall at the same rate". Placement after the 8.15 block, before 97: owner,
2026-10-15 (AskUserQuestion (a)). Queued by the supervisor 2026-10-15. Branch `engine-phase`.

## Why
Today `FluidCA.compute` moves a fluid cell at most ONE voxel (0.1 m) per tick, and viscosity is a tick interval
(`Assets/ContentModules/MaterialRules.cs` ~:70, `TickInterval`: water every tick, lava every 6, honey every 30; quicksand
in the slow tier). At the fixed 4.5 Hz rate (task 91, `FluidTickScheduler`) water falls 0.45 m/s, lava 0.075 m/s, honey
0.015 m/s. Real free fall reaches ~4.4 m/s after 0.45 s. A waterfall or a poured bucket looks frozen.

## Change (one change: gravity fall, identical for every fluid)
0. **Spec note first.** Write `AMENDMENT_8_16_FLUID_FALL.md` (short: one table row + rationale): §7.4's viscosity
   tick interval no longer applies to FREE FALL; a fluid cell with air (or a lighter gas) below it falls under gravity,
   the same for every fluid; viscosity (tick interval + `ViscosityDrag`) only slows sideways spreading and draining.
   Mark it "DESIGN CHANGE, owner-approved in chat 2026-10-15". Add it to CLAUDE.md's amendment order line (after 8.15).
1. **Fall.** A free-falling fluid cell moves several voxels per tick, its speed growing each tick it keeps falling
   (v += g*dt, g = 9.81 m/s^2, dt = 1/rate from `FluidTickScheduler`), capped (worker's pick, e.g. terminal ~10-15 m/s;
   record it), stopping at the first non-air cell (never tunnels through a solid or another fluid). Same numbers for
   water, lava, honey, oil, sludge, acid, quicksand. Sideways moves keep today's per-material interval.
2. **State.** Per-cell fall speed needs somewhere to live. Use spare bits that ALREADY exist in the fluid cell / tile
   data (read Appendix A/B and `FluidCA.compute` first). Do NOT change memory layout (struct sizes, D.1, frozen
   APIs). If there are no spare bits, implement a CONSTANT fast fall instead (same for all fluids, e.g. the cap above
   per tick), note "acceleration needs N bits of per-cell state" in HANDOFF as an owner question, and finish -- not a
   STOP. Do not touch the 4.5 Hz rate (that is task 97).
3. **Conservation + determinism.** Multi-voxel moves in a parallel CA can collide (two cells claiming one target); extend
   the CA's existing claim / tie-break scheme so mass is conserved exactly. `FluidReferenceCPU.cs` gets the same rule
   and must match the GPU (the existing CPU-vs-GPU tests). Sand / mobile solids (`IsFallingSolid`) unchanged unless the
   same code path covers them for free (say which).
4. Surface the gameplay consequence in HANDOFF (CLAUDE.md memory rule): does a faster fall wake more cells / tiles
   (pool pressure, `MODE=poolprobe` counters from a test, not a timed run)?

## Acceptance
- Tests: new `FluidGravityFallTests` (fall distance after 0.5/1/2 s matches the integrated speed within one voxel for
  each fluid; same for all fluids; never passes through a 1-voxel solid slab; cap holds), plus the existing
  `FluidConservationTests`, `FluidAtRestTests`, `FluidSlowViscositySettleTests`, `FluidTieBreakVarianceTests`,
  `FluidSlopedTerrainTests` green. Own tests while iterating (`-testFilter`); fast suite ONCE at the end
  (`tools/with-unity-lock.sh ./run-editmode-tests.sh`).
- `run-fluid-tiled.sh` (FluidTiled rig, conservation assertions) green -- correctness only, quote no frame time.
- Strip, ONE launch: like `Screenshots/91/91-strip.png` (task 91's `-fluidstrip`, see its done file
  `tools/overnight/done/20261015_112329_91-engine-17-fixed-rate-fluid-tick.md`): a water, a lava and a honey column
  released from the same height, frames at +0.5 / +1 / +2 / +4 s, BEFORE (this change off) vs AFTER, into
  `Screenshots/94/`. Well lit (midday, sun behind the camera). Caption says which row is which fluid and where the
  front is. No frame-time numbers (PREAMBLE rule 10).
- HANDOFF `## For the doc`: fall speed m/s at +0.5/+1/+2 s per fluid, before -> after; the cap; conservation result;
  the strip with caption.

## After it lands (supervisor)
Re-ask the owner whether 97 (option B, adaptive drain to raise 4.5 Hz) is still wanted.
