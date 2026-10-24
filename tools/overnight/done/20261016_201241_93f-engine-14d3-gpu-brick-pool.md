# 93f-engine-14d3-gpu-brick-pool

Requires: 93e-engine-14d2-gpu-mirror-box

Approval: owner, 2026-10-15 in the supervisor chat: Amendment 8.15 Q2 (a) "GPU directory box AND own GPU brick pool
(**§3.7 layout change approved**), -394 MB est.", Q10 (a) order -- recorded in
`tools/overnight/done/90a-amendment-8-15-simulation-reach.md` "## Owner answer"; adopted by task 90b. Row D3 of
`AMENDMENT_8_15_SIMULATION_REACH.md` §11 (§2 item 3, §10 row 2). Branch `engine-phase`.

## Why
The GPU body buffer is sized to the CPU brick pool's capacity and indexed by the CPU handle (ARCHITECTURE §3.7, "a
mirror of the CPU pool's bodies"; `TerrainClipmap.cs:225-241`). At `EngineConfig.TG2_BRICK_POOL_CAP` = 750,000 that is
366 MB of GPU for bricks of which only the drawn 16^3 box (after 93e) needs a GPU copy.

## Change (one change: the GPU gets its own brick-body pool; a layout change, NOT an API change)
1. A GPU body pool with its own free list, cap = measured `PeakUsed` of the drawn box (93e) x 1.25 (8.14's estimate:
   ~242 k bricks on High; Low 130 k / Medium 182 k / Ultra 474 k, est.). Allocated once at start-up (no growth).
2. A CPU-handle -> GPU-slot table (the amendment estimates ~2 MB), filled on upload, freed when a chunk leaves the box or
   is evicted. The directory entries the shader reads carry the GPU slot. Every GPU reader of the body buffer must go
   through it: grep ALL of them (raymarch kernels incl. `ShellMarch` / far shade / shadows, light paths, fluid promote /
   anything indexing `BrickDataBuffer`), not only the raymarch.
3. Full GPU pool -> refused-and-counted (a counter on the HUD/ledger), never a silent overwrite; the chunk falls back to
   its shell (like task 32's out-of-box skip).
4. `ClipmapValidator` compares CPU and GPU bricks through the table (phantom-geometry check stays meaningful).
5. Shells OFF (rigs): keep today's layout (GPU sized to and indexed by the CPU pool), byte-identical. The CPU stays the
   sole terrain writer; the GPU only stores what the CPU uploads.
6. Update ARCHITECTURE §3.7's wording ONLY by a note pointing to 8.15 §10 row 2 (the amendment already records it).

## Acceptance
- Tests: handle table round-trip (allocate, upload, free, reuse) with no slot leak over a long recentre walk; pool-full
  refusal counted; validator through the table finds 0 stale slots and DOES find an injected mismatch; shells-off
  byte-identical (`run-phase7-rig.sh` 19/19 shots identical, PASS 37 FAIL 0). Own tests while iterating, `SUITE=all` once.
- `run-acceptance-rig.sh` gates C and D green (shells ON if the rig can pass it); pass/fail only, no frame time.
- Memory (not frame time, PREAMBLE rule 10): GPU pool `PeakUsed`, Metal MB and peak process MB before -> after, same
  poses as 93e; 8.15 §2 estimates GPU mirror ~660 MB today -> ~266 MB after D2 + D3. Set each preset's cap from what you
  measured (High only is measurable here; keep the estimate for the others and say so).
- Stills, ONE launch (<= ~10 min): 2-3 poses, before vs after, identical apart from noise -- captions say what to check
  (no missing bricks, no wrong materials = a table mix-up).
- HANDOFF `## For the doc`: GPU pool cap / peak, Metal MB before -> after, validator result, the pair.
