# 82b-engine-15-generation-speed

Approval: `ENGINE_PHASE_PLAN.md` (owner, Q36, 2026-10-09) step 15; owner 2026-10-12: "move faster generation up to be
the next task. That's the one optimization I want done asap." Runs after 82 (13b), before 83 (shell cave fill).
Queued by the supervisor 2026-10-12. Branch `engine-phase`. This IS a performance task (PREAMBLE rule 10 allows a
timing check; rule 9 applies to any timed run).

## Why
Generation is the streaming bottleneck (KNOWN_ISSUES "STREAMING BASELINE v3": chunk ~350 ms on a worker vs 62 ms
alone; warm capacity 23-37 chunks/s at 4 workers, task 28). Shell chunks cost ~440-640 ms each with 2 workers beside
tier 0 (task 77), so the far shells fill slowly. Task 30 already shares the column pass (1.76x isolated).

## Change: plan step 15
Whole 8x8x8 bricks skip per-voxel work when the column data PROVES them uniform:
- all AIR: the brick is entirely above every covered column's ground height h (and above any feature that can rise
  above h: trees/overhangs/arches if the generator has them -- check), and no water/fluid level reaches into it.
- all SOLID one material: entirely below every covered column's h by more than the deepest per-voxel surface layer
  (grass/dirt/sand/snow/strata bands) AND the cave/cavern/ore functions provably cannot carve or change it there
  (e.g. cave noise has a known depth window or a bound; ores are sparse per-voxel -- if ores make the brick mixed,
  only skip where the ore function's own cheap bound says "no ore here"). If no cheap proof exists for caves, skip
  only the all-air case plus the provably cave-free depths, and say so.
- A skipped brick is written directly as a uniform brick (the same storage a uniform brick gets today).
Apply it to BOTH `GenerateChunk` (0.1 m) and `GenerateShellChunk` (shell k samples one fine voxel per coarse voxel,
so the same proofs hold at its sample positions). Burst, on the one compiled generator path. Workers write scratch
they own; no change to output bytes.

## Acceptance
- **Byte-identical test:** a few hundred chunks over seed 42 map v4 (plains, mountains, coast, lake, river, caves,
  ores; surface and deep) and shell chunks for shells 1-7: output equal byte for byte with the skip on vs off.
- **Isolated EditMode timing check, as task 30 did:** ms per chunk (0.1 m) and per shell chunk, skip off vs on, same
  chunk set, interleaved (ABBA), plus the share of bricks skipped (air / solid). Report it as an isolated figure, not a
  streaming or frame-time claim.
- Fast EditMode green; `SUITE=all` once at the end (generator path). `run-phase7-rig.sh` result unchanged and shots
  byte-identical.
- No stream probe / acceptance rig (the streaming check is step 16, later).
- HANDOFF `## For the doc`: ms per chunk before -> after (0.1 m and per shell), % bricks skipped, the byte-identical
  count. No images needed (nothing changes on screen) -- say so.
