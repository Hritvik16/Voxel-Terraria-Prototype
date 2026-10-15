# 93d-engine-14d1-free-cascade-tiers

Requires: 93c-shells-default-in-play

Approval: owner, 2026-10-15 in the supervisor chat: Amendment 8.15 Q1-Q10 all (a) -- Q2 (a) step 14 redone, Q10 (a)
order -- recorded in `tools/overnight/done/90a-amendment-8-15-simulation-reach.md` "## Owner answer"; also Q49 (cascade
tiers 1-2 retired when shells draw). Adopted by task 90b. Row D1 of `AMENDMENT_8_15_SIMULATION_REACH.md` §11 (§2 item 6);
first part of ENGINE_PHASE_PLAN step 14 redone. Branch `engine-phase`.

## Why
Since task 80 cascade tiers 1-2 stop DRAWING when shells draw, but stay ALLOCATED. Task 55's ledger: **193 MB**
(79.0 MB GPU + 67.6 MB native + 46.1 MB managed). Task 85 measured a 2,999 MB peak with shells on (cap 3,000 MB).

## Change (one change: shells ON does not allocate cascade tiers 1-2)
1. When shells are ON (play default after 93c, or `-shells 1`), tiers 1-2's pools (`CascadeTierPool`,
   `LODCascadeManager`), GPU buffers and native/managed state are not created at start-up. Check every reader and make
   it skip cleanly: `ShellMarch` / the raymarch bindings (bind a 1-element dummy where a buffer must be bound), validators
   (`ClipmapValidator`), the HUD, memledger, upload paths.
2. Shells OFF (rigs, capture runs, `-shells 0`): byte-identical, tiers allocated as today.
3. Do NOT delete the cascade code, `-detailspeed` or `FarStartT` (the shells-off path uses them). No frozen API changes.
   Note `LODCascadeManager.cs` and `CascadeTierPool.cs` are the only `VoxelEngine.Mirror` namespace files in
   `Assets/CoreEngine/Mirror/` (CLAUDE.md landmine).

## Acceptance
- Tests: with shells ON no tier 1-2 allocation happens (a start-up test or a memledger assertion); shells-off path
  unchanged (`run-phase7-rig.sh` 19/19 shots identical, PASS 37 FAIL 0). Own tests while iterating, the fast suite once
  at the end (`tools/with-unity-lock.sh ./run-editmode-tests.sh`).
- Memory (not frame time, PREAMBLE rule 10): peak process MB and Metal MB in play with shells ON, before -> after, same
  poses as 93c; expected about -193 MB (est.). Report the measured number and the memledger rows that moved.
- Stills, ONE short launch (<= ~10 min): 2 poses, ON before vs after, to show nothing that tiers 1-2 drew went missing
  (captions: horizon, mid-distance slopes). No frame-time numbers.
- HANDOFF `## For the doc`: peak MB before -> after, memledger rows, the pair.
