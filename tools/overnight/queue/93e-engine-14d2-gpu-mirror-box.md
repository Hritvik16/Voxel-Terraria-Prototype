# 93e-engine-14d2-gpu-mirror-box

Requires: 93a-engine-14e1-edit-summaries-live 93d-engine-14d1-free-cascade-tiers

Approval: owner, 2026-10-15 in the supervisor chat: Amendment 8.15 Q1-Q10 all (a) -- **Q1 (a) NEAR = 128 m on every
machine**, **Q2 (a) shrink only what is DRAWN**, Q10 (a) order -- recorded in
`tools/overnight/done/90a-amendment-8-15-simulation-reach.md` "## Owner answer"; adopted by task 90b. Row D2 of
`AMENDMENT_8_15_SIMULATION_REACH.md` §11 (§2 items 1-2); ENGINE_PHASE_PLAN step 14 redone; Amendment 8.14 row 6's GPU
half (its load-radius half is WITHDRAWN by 8.15 §10 row 1). Branch `engine-phase`.

## SUPERSEDED: task 87
`tools/overnight/blocked/87-engine-14-smaller-window.md` and its `.patch` / `87-SmallWindowTests.LEAKED.cs.txt` are
SUPERSEDED by Amendment 8.15. **Do not apply the patch or copy its load-radius code.** 87 lowered the CPU load radius
10 -> 5/6/7 chunks, i.e. gameplay reach 128 m -> 59-82 m; the owner stopped it for that. You may read it for how the
box was sized per preset, nothing more.

## Change (one change: the GPU mirror box shrinks; CPU reach does not)
1. **CPU load radius stays 10 chunks (128 m) on EVERY preset** (`EngineConfig.TG2_LOAD_RADIUS_CHUNKS`); the CPU ring
   stays 32 x 32 x 16 (`WINDOW_CHUNKS_Y` = 16: CLAUDE.md landmine, oracle tests build 16-tall windows). `GetVoxel`,
   `SetVoxel`, physics and fluid residency keep their 128 m.
2. **GPU mirror box (tier 0 directory) 32 x 32 x 8 -> 16^3 on Low / Medium / High; Ultra keeps 32 x 32 x 8**
   (`EngineConfig.TG2_MIRROR_CHUNKS_Y` = 8 today; 8.14 §2/H and 8.15 §2). Power of two asserted (CLAUDE.md: a non-pow2
   ring aliases silently, §6.2 phantom terrain). Recentring uses `>>` / `&` only.
3. Resident chunks outside the box are already skipped, counted and parked by task 32's rule (`UploadDirty`,
   `skippedOutOfBox`); shells draw there, and edited ones reach the shells via E1 (93a). Check shell 0's hand-over
   (69 m on High, `Raymarch.compute` tTier0) stays inside the box on every preset (+-102 m horizontally at 16^3);
   if a preset's hand-over + 12.8 m (`ShellLayout.SHELL0_MARGIN_M`) does not fit, say so and keep that preset's box.
4. Only valid where shells draw beyond the box: shells OFF keeps today's box, byte-identical. The GPU body buffer is NOT
   changed here (that is 93f, D3): this task saves the directory / mips / managed-copy part only.
5. Frozen APIs and §4.4 untouched.

## Acceptance
- Tests: box dims per preset (pow2 asserted); the CPU load radius is 10 on every preset; a resident chunk outside the
  box is skipped and counted, not aliased (extend `UploadDirtyBoxTests`); an edited resident chunk outside the box shows
  in its shell (E1's test, rerun); `ClipmapValidator.CountStaleSlots` = 0 after a recentre. Shells-off byte-identical
  (`run-phase7-rig.sh` 19/19 shots identical, PASS 37 FAIL 0). Own tests while iterating, `SUITE=all` once at the end.
- `run-acceptance-rig.sh` gates C and D green with shells ON if the rig can pass `-shells 1`; if it cannot, say so and
  report the shells-off run. Pass/fail only, NO frame time (PREAMBLE rule 10). Never loosen a gate.
- Memory (not frame time): peak process MB and Metal MB in play (shells on), before -> after, same poses as 93d; the
  amendment's estimate for the directory part is in 8.15 §2's table (GPU mirror 660 MB today -> ~266 MB after D2 + D3).
- Stills, ONE launch (<= ~10 min, short fixed waits): 3 poses where the box edge is in view (e.g. eye_level_3,
  glide_400m, cliff_foot_1), before vs after, plus `-shellsdebug` tint so the shell-0 / shell-1 boundary shows. Look
  for seams, holes or popping at the boundary; captions say where.
- HANDOFF `## For the doc`: box before -> after per preset, peak MB before -> after, gates C/D, the sheet.

## Owner answer (2026-10-16, Queue decisions doc Q55 = (b))
The 16^3 box is BUILT and committed opt-in (`dc68d34`, `-smallmirror 1`); do NOT redo it. The owner chose **(b): keep
fluid at 128 m on every preset** (8.15: simulation reach must not depend on the preset). This run:
1. Give the GPU fluid CA (`FluidCA.compute` `SampleVoxel`) its own small terrain lookup for chunks that hold AWAKE fluid
   outside the 16^3 mirror box (HANDOFF estimate ~1 MB): a directory of those chunks' bricks, written from the CPU state
   (CPU stays the sole terrain writer), so water 90-128 m away keeps flowing against real terrain, not air.
2. Then switch the small box ON by default with `-shells 1` (Low/Medium/High; Ultra keeps 32x8x32); `-smallmirror 0` = old.
3. Note for 93f (D3): it must map those fluid chunks' bodies too; say what it needs in HANDOFF.
Acceptance: an EditMode test where awake water in a chunk outside the box (100-128 m) flows the same as inside the box
(CPU reference vs GPU, or the CPU's accepted-move count); fluid at 128 m on every preset; the mirror check (lost/lag/stale
0) still holds; shells off byte-identical (`run-phase7-rig.sh` 19/19, PASS 37); memory before -> after (peak + median MB,
shells on, 93e's poses); `SUITE=all` once at the end. No frame time (rule 10). Fluid shots: a pool flowing at ~110 m
seen from the start, before (frozen) vs after. Stop for the owner only if (b) needs more than ~32 MB or a frozen-API change.
