# Amendment 8.16 — Fluid gravity fall

**DESIGN CHANGE, owner-approved in chat 2026-10-15** ("water should fall much faster and all fluids
should fall at the same rate"). Task 94. Amends ARCHITECTURE_v8.6 §7.4 (fluid behaviour, viscosity tiers).

| § | Was (v8.6 §7.4) | Now (8.16) |
|---|---|---|
| 7.4 viscosity | Every move of a fluid cell, falling included, is gated by its material's tick interval (water 1, oil 2, lava 6, sludge 12, honey 30, quicksand 60) and moves at most one voxel per tick. At task 91's fixed 4.5 Hz: water falls 0.45 m/s, lava 0.075, honey 0.015. | **FREE FALL is not gated and is the same for every fluid.** A fluid cell (not a gas, not a falling solid) with Air directly below it at tick start falls `FluidFall.Step(k)` voxels straight down on EVERY tick, k = consecutive ticks it has fallen: the exact `g t²/2` (g = 9.81 m/s², t = k·dt, dt = the scheduler's period) floored to whole voxels, up to a terminal speed of 12 m/s rounded to whole voxels per tick (27 voxels/tick = 12.15 m/s at 4.5 Hz, reached after 1.24 s / 7.5 m). It stops above the first cell that was not Air at tick start (never through a solid or another fluid; that stop resets k). A lighter GAS directly below (steam) is swapped every tick, also ungated. The tick interval (and `ViscosityDrag`) now only slows **sideways spreading, down-diagonals and draining / density swaps between liquids**. |

## Rationale
Real free fall reaches ~4.4 m/s after 0.45 s; one voxel per tick made a waterfall or a poured bucket look
frozen, and made viscous fluids fall 6-60x slower than water, which is not what viscosity does to a
falling body. Viscosity is about flow under shear (spreading), so it keeps that job.

## How (no layout change)
- State: k (8 bits) in `FluidSlot.stateFlags` bits [15:8] — spare bits of the GPU slot's existing
  32-bit lane (A.5's byte fields end at bit 7; struct size 24 B unchanged). The CPU oracle
  (`FluidReferenceCPU`, A.5's byte layout, no spare byte) keeps the same k in a side array with the
  slot's lifetime. A slot is promoted at rest, so k starts at 0.
- One integer table (`FluidFall.BuildTable`, 256 entries) is built on the CPU and read by both the
  oracle and `FluidCA.compute`: no float in the CA's fall path, CPU and GPU cannot disagree.
- The fall is ONE ordinary claim (§7.3) on a cell further away: Air-only at tick start, plain-store
  claim, destination-driven commit, one bid per slot — conservation is the unchanged §7.3 argument.
- A missing fluid tile below stops a fall without resetting k; a fast fall's applied op admits the
  tiles its next tick can reach (`FluidGpuSimulation.NoteFallAhead`, AwakeOnly).
- Falling solids (sand) and gases (steam rising) are unchanged. `-fluidfall 0` = the v8.6 rule.
