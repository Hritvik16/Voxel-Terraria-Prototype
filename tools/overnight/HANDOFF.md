# 90b-adopt-amendment-8-15: DONE (planning only). 8.15 ADOPTED 2026-10-15, CLAUDE.md updated, first block queued (93a-93g).

## What changed (one commit on engine-phase, see `git log --grep '^90b'`)
- `AMENDMENT_8_15_SIMULATION_REACH.md`: header DRAFT -> **ADOPTED 2026-10-15**; Questions section -> "Owner answers"
  table (Q1-Q10 all (a) + gaps A/B). New §13 look-ahead + far visuals, §14 stage flip, §15 far reactions; §10 row 10
  (stage is a generator input; D.1 baseline = the chunk's generation stage); §11 rows L1, V1, S1, R1, C1 widened (stage
  bytes), adopted order + the queued block.
- `CLAUDE.md` Ground truth item 2: "→ 8.15 (simulation reach ...; `AMENDMENT_8_15_SIMULATION_REACH.md`, adopted
  2026-10-15)". Nothing else in CLAUDE.md touched.
- Queue files written (run order; each has Approval, one change, acceptance without frame-time claims except 93b):
  - `93a-engine-14e1-edit-summaries-live` -- E1: resident edited chunks redraw into the shells covering them (no Requires).
  - `93b-shells-perf-breakdown` -- old 89, a PERFORMANCE task (rule 9 warm-up); no longer requires 87 (no Requires).
  - `93c-shells-default-in-play` -- old 90, ONLY the play default + F6 row. Requires 93b, 93a.
  - `93d-engine-14d1-free-cascade-tiers` -- D1: shells ON allocates no tiers 1-2 (-193 MB est.). Requires 93c.
  - `93e-engine-14d2-gpu-mirror-box` -- D2: box 32x32x8 -> 16^3 (Low/Med/High), CPU radius stays 10; says 87 is
    SUPERSEDED, do not apply its patch. Requires 93a, 93d.
  - `93f-engine-14d3-gpu-brick-pool` -- D3: own GPU body pool + handle table (§3.7 layout). Requires 93e.
  - `93g-engine-14d4-cpu-brick-cap-ledger` -- D4: `TG2_BRICK_POOL_CAP` from measured peak x 1.5 + one ledger line. Requires 93f.
  - Originals `later/89-*` and `later/90-*` left in place. C1 / Q1 / Q2 / E2 and later NOT written (supervisor's).

## Evidence
Planning only: no code, no Unity, no builds, launches or tests. Code read only to copy names/numbers into the task files
(`EngineConfig.TG2_LOAD_RADIUS_CHUNKS` = 10, `TG2_MIRROR_CHUNKS_Y` = 8, `TG2_BRICK_POOL_CAP` = 750,000 vs
`BRICK_POOL_CAP` = 500,000; `ShellStore.Store(.., replace)`; `ShellStreamer.OrderKey` has a velocity term).

## Ambiguities / choices made (supervisor please check)
1. **Numbering:** 93 and 97 were already taken (fluid bug, option B), so the block is `93a`-`93g` (sorts after 93,
   before 97). Free for C1/Q1/Q2/E2: 94, 95, 96, 98, 99. **Beware: the runner uses `ls | sort`, so a `100-...` file
   would sort BEFORE `92-...`** (Requires still guard order, but the runner would pick it first).
2. **Old 90 contained D1** (its item 2, "do not allocate tiers 1-2"). Split: 93c = default only, 93d = D1 (one change each).
3. **Tiers 1-2 saving:** old 89 said ~480 MB (task 53/55 ledger); 8.15 §2 itemises 193 MB (79.0 GPU + 67.6 native +
   46.1 managed). Files use 193 MB, est.; 93d measures it.
4. **93c Requires 93a (E1)**: with shells the play default, edits 69-128 m away would be live but undrawn otherwise.
   93b has no Requires (its walk has no edits).
5. **16^3 box is TALLER than today's** (16 vs 8 chunks) but half as wide (+-102 m vs +-204 m); kept as 8.14 says.
6. **Stage flip granularity:** the task text says never-touched CHUNKS (no D.1, no ops); the owner said never-touched
   ROCK. §14 pins whole touched chunks (stricter; simplest correct rule) and limits stages to MATERIAL changes (no shape
   change -> no border steps). If the owner wants untouched rock inside an edited chunk to change too, D.1's brick
   baseline allows it (dropped bricks = untouched), but a re-touched-then-restored brick would also change.
7. **Look-ahead (L1) is an ENGINE row** after step 16 (streaming), not gameplay; no hook owed before 16. Far creatures
   (V1), stage flip (S1), far-reaction catch-up (R1) are gameplay-phase rows; engine owes only C1's two stage bytes
   (now part of C1's row) and C2's hook (already a row).

## Next step
Queue runs 93 (fluid bug), then 93a-93g in order (93b is a measured A/B; 97 is another). Supervisor writes C1, Q1, Q2,
E2 when 93g is done; then lighting (steps 20-22) per Q10 (a).

## For the doc
**Adopted summary (8.15, 2026-10-15):**
1. NEAR (fully live: dig, physics, fluid, creatures) = 128 m on every machine; only what is DRAWN shrinks (step 14 redone).
2. GPU: box 16^3 + its own brick pool + tiers 1-2 freed -> ~2,370 MB process (est.) vs 2,999 MB measured today.
3. Your builds show from anywhere: live edit summaries now (E1), saved beside D.1 later (E2).
4. Far away: floods/lava, powders, fire keep running on 0.8 m cells and write their results as ops; everything else freezes.
5. On return: fluids settle, powder falls; growth, melt and freeze computed from time away. Gas/electricity only near.
6. New: exact far query + raycast for NPCs/spells past 128 m; nukes = one carve + budgeted debris.
7. Stage flip: new ores / biome spread only in never-touched chunks; builds never change (gameplay phase; 2 bytes now).
8. Look-ahead loading at speed after step 16; visual-only far herds/birds in the gameplay phase.
**Queue order:** 93 fluid bug -> 93a E1 -> 93b shells perf (89) -> 93c shells default (90) -> 93d D1 -> 93e D2 ->
93f D3 -> 93g D4 (97 fluid option B runs after 93g by name order) -> [supervisor: C1, Q1, Q2, E2] -> lighting 20-22 -> far sims.
No images: planning only.
