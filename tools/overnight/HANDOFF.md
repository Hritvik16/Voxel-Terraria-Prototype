STOP_FOR_OWNER -- gameplay plan updated (task 92); four questions, recommendation first:
1. **Plane stroke (BLD2):** (a) one drag: the box from where you pressed to where you are now, one brush thick (flat
   = floor/roof, upright = wall, thin = line); (b) drag a line, release, sweep it into a plane, click; (c) hold Shift
   to turn the line into a plane.
2. **Hardcore death (9b):** (a) the world locks: you can fly and look, not play (Minecraft); (b) the world is deleted;
   (c) a new empty-handed character starts in the same world (Terraria).
3. **Classic death (9b):** (a) everything you carry drops in one bag at the death spot, kept until fetched; (b) only
   the backpack drops; (c) half of every stack drops.
4. **Gameplay vs the engine block (8.15 E/D/C/Q, then lighting):** (a) alternate, one gameplay task after each engine
   task; (b) finish the engine block first; (c) gameplay first.

# 92-gameplay-plan-update: DONE (planning only)

## What changed
- `GAMEPLAY_PHASE_PLAN.md` rewritten in place (same shape), commit `7107b4f`. `## For the owner` on top (12 lines);
  "Owner may change" list: P6, P13, P22-P29 now "owner, 2026-10-15"; P7 replaced (stroke cost: place until the slot runs
  out; all-or-nothing suited single clicks); P22 "keep inventory on death" replaced by death per world.
- New step groups: BLD1-6 (building), 9a/9b (death split), RX1-7 (reactions: A = today's `MaterialRules` pair table /
  `IsFallingSolid`; B = new sim), CB1-3 (combat; CB3 spells WAITING, OPEN), CR1-2 + V1 (creatures), BS1-2 (bosses),
  STG1-2 (stages). Old ids 1-20 kept so 67-72 still point at the right rows; old 17 (question batch) marked done.
- Far-away rules cited, not restated: rows say "Requires 8.15 <row>" (E1/E2, C1, N1, B1, V1, Q1, F1, R1, L1).
- 67-71 **still valid**; 72 **needs rewrite** (step 9 -> 9a health/respawn + 9b death mode). Files not edited.

## Picks of mine to check (in the plan)
- STG1 (= 8.15 S1) built with a dev trigger (`-stage N`) BEFORE bosses exist; 8.15's table says it needs bosses (G).
- Dig also uses strokes (LMB); prefab cap 64^3 voxels; heat field per brick; growth = CPU random-tick, not the CA.
- RX1 changes today's water + lava products (obsidian + air -> stone + steam), per the owner's batch-1 wording.
- The death bag (classic) is a static saved marker, so P2 (no ground drops / no entity physics) still holds.

## Evidence
Planning only: no code, Unity, builds, launches or tests. Read: the design round file, the old plan, 8.15 §8/§11/§14/
§15, `MaterialRules.cs` (pair reactions water+lava, acid; steam `IsGas`; sand falling, snow static on purpose).

## Next step
Owner answers Q1-Q4. Then the supervisor rewrites `later/72` as 9a, writes 9b, BLD1, BLD2, RX1 task files, and queues
per Q4. Not done here: no queue files written (the task said plan only).

## For the doc
No images: planning only. New step list (gameplay run order):
- BLD1 drag line (brush stamped along a straight drag; preview, cost per cell) -- S
- BLD2 plane stroke (walls/roofs in one drag; shape per Q1) -- S-M
- 4 start on the best easy area (task 67) -- S
- 9a health, fall/lava/acid damage, oxygen, respawn (task 72 rewritten) -- M
- 9b death mode per world: soft / classic / hardcore (Q2, Q3) -- M
- RX1 adjacency reactions: lava + water -> stone + steam, mud, glass, melting (table rows) -- S
- 5 climb (68), 6 glide (69), 7 grapple (70), 8 wall-run (71)
- BLD4 ground paths, BLD5 sculpt raise/lower, BLD3 cylinder + cone roof, BLD6 your build as a brush
- 10 tool tiers, 11 crafting, 13 bucket, RX2 powders, CB1 gear combat v1, CB2 armor
- 14 trees [measure] -> 15 trees -> RX4 growth; 18 entity physics [measure] -> CR1 spawner -> CR2 behaviour
- After 8.15 rows: RX3 fire (N1), RX5 heat, RX6 gas/explosives (nukes: B1), RX7 electricity; STG1 stage flip (C1, E2)
  -> BS1 landmark boss -> STG2 stage content -> BS2; V1 far creatures; 12 ore sources; 16 hazards; 19 towns; 20 travel
- CB3 spells: waiting on the owner (OPEN)
Questions: Q1-Q4 at the top of this file.
