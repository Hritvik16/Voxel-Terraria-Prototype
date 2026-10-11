# 63-gameplay-phase-plan: DONE (planning only). `GAMEPLAY_PHASE_PLAN.md` written and committed; owner delegated the picks.

## What changed (branch `engine-phase`)
- 56ba179: `GAMEPLAY_PHASE_PLAN.md` only. Top: "Owner may change" list (22 picks, one line each, how cheap to
  reverse). Then goal, 11 guardrails, a 20-step table (one change per step, acceptance, size, needs), risks.
- Overrides ROADMAP.md's order for now (owner 2026-10-11); ENGINE_PHASE_PLAN steps resume later, nothing cancelled.

## Steps 1-3 (tasks 64-66), one line each
1. **Inventory, hotbar, survival build/dig:** dug material -> 36-slot inventory (9 hotbar + 27, E panel), right-click
   places a grid-snapped cube (Q: 0.1-0.8 m) costing the cells it fills; F6 "Game mode" Survival (play default) /
   Creative (today's brushes, what rigs run); new `EditService` overload reports mined materials. M.
2. **Movement set A in `PlayerMotor`:** swim/dive (3.0 m/s, Space/Left Ctrl), double jump (0.85x), dash (Left Shift,
   14 m/s for 0.18 s, 0.8 s cooldown); tuning in `PlayerConfig.json`; walk/jump unchanged with no new input. M.
3. **Ores by depth and biome x landform (map v4 only):** 5 ores, one per layer (copper/iron/gold/cobalt/emberstone,
   placeholder names), integer-hash blobs 0.3-1.2 m, < 0.5% of rock, pair density table from `WorldRecord`;
   `SUITE=all`, v3 byte-identical, mixed-brick share rise <= 5%. M.

## Evidence
- Planning only: no code, Unity, builds or rigs; EditMode not run (nothing compiled changed).
- Checked against the code: dig = `EditService.MineSphereBudgeted` (returns a count only, hence the new overload);
  place = `Playground.TryPaintBrush` (sphere r=2, wake guard); `EditService.SetBox` exists; hotbar `_brushes`
  slots 0-3 pinned by Phase6BrushGuard; keys E/Q/LeftShift/LeftCtrl free in play (only DevMap uses them, and it
  owns input while open); play is a fresh world unless `KEEP=1` (run-play.sh); GenMat palette is 96 entries,
  codes 17-19, 26-29, 55-59, 65-69, 76-79, 93-95 unused; brief's movement bands (Walk < ~30°) drive step 5.
## Unfinished / notes
- Steps 14, 17, 18 need the owner (trees voxels vs meshes measurement; creatures/combat batch; NPC physics).
- Step 3 changes v4 chunks: engine step 10 (coarse-first) must treat ore as host rock at coarse levels.
## Next step
- Task 64 = plan step 1.

## For the doc
Task 63: gameplay phase plan. Nothing built; `GAMEPLAY_PHASE_PLAN.md` (56ba179).
- **Goal:** the first playable loop on the tg2 island, Terraria-like: dig -> get the exact blocks -> build from a
  hotbar; move with swim/dive, double jump, dash (then climb, glide, grapple, wall-run); better ore deeper and in
  harsher biome x landform pairs, later buying tools that open harder places. Gated by danger and materials.
- **Picks (agent's, owner may change):** Survival default in play, Creative in F6 (P1); auto-pickup, no ground drops
  (P2); item = exact material, grass gives soil (P3); counted in voxels, cap 10,000/slot (P4); 9+27 slots, E panel
  (P5); grid-snapped cube placement 0.1-0.8 m, default 0.4 m (P6); all-or-nothing place (P7); full inventory loses
  overflow (P8); no fluid pickup yet (P9); starter 50 torches (P10); dev keys stay live (P11); inventory saved with
  the world's edits (P12); first moves swim/dive + double jump + dash (P13); walk 5.2, swim 3.0, dash 14 m/s for
  0.18 s (P14); all abilities unlocked (P15); tuning in PlayerConfig.json (P16); 5 ores one per layer (P17); pair
  sets density, biome favours one ore (P18); integer-hash blobs < 0.5% of rock, v4 only (P19); no map v5 (P20);
  spawn at best easy area, play only (P21); keep inventory on death (P22).
- **Step table:** 1 inventory/hotbar/survival (M) · 2 swim/dive, double jump, dash (M) · 3 ores by depth + pair (M) ·
  4 start on best easy area (S) · 5 slope rule + climbing (M) · 6 gliding (S) · 7 grapple (M) · 8 wall-run/jump (M) ·
  9 health/fall/drowning/death (M) · 10 hardness + tool tiers (S-M) · 11 crafting v1 (M) · 12 cliff veins + feature
  deposits (M) · 13 bucket (M) · 14 [measure] trees voxels vs meshes (M, owner) · 15 trees/plants (L) · 16 hazard
  zones (M) · 17 owner batch: creatures/combat/stages (S) · 18 [measure] NPC physics (M, owner) · 19 towns + NPCs (L) ·
  20 boats, fast travel, flight 60 m/s (L, after engine step 16).
- Images: none -- a planning doc with nothing rendered.
