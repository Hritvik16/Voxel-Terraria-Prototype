STOP_FOR_OWNER: Engine phase plan ready for approval.
Doc: https://claude.ai/code/artifact/62d9391e-b05a-42d0-83c2-34d9bfeb04f7
(repo copy for task sessions: `ENGINE_PHASE_PLAN.md`). Answer section 5; "all (a)" approves it as written.

Questions (option (a) is recommended in each):
- Q1 Design top speed, one number: (a) 60 m/s (on record) / (b) 30 / (c) 15.
- Q2 When generation falls behind at top speed: (a) show the next coarser shell until detail arrives / (b) game limits speed later / (c) GPU-built render shells (L + amendment).
- Q3 E1 internal resolution: (a) 540 fixed + upsample to 1080p / (b) dynamic 540-720 (shells sized for 720) / (c) 720 fixed.
- Q4 Upscaler: (a) extend our own TAA to TAA-upsampling / (b) URP STP / (c) MetalFX.
- Q5 Torch-through-wall (E9): (a) torch shadow rays first, then a light-propagation grid; emissive bleed ON / (b) shadow rays only / (c) light grid only.
- Q6 First 3 tasks: (a) generator path + lava merge, torch shadows, TAA-upsampling / (b) memory first / (c) shells first.
- Q7 Coast fills the fluid pool: (a) only awake fluid takes tiles / (b) 4x pool (+1.5 GB) / (c) smaller sim radius.
- Q8 Palette bricks (8.13): (a) stay deferred, decide after the window shrink / (b) do now (~6 tasks) / (c) drop.

# 44-engine-phase-plan-doc: DONE (planning only). Plan written, needs the owner's approval.

## What changed
- New `ENGINE_PHASE_PLAN.md` (repo root): the same text as the Claude Doc. It has 6 sections: targets E1-E10, memory budget, 23 ordered steps, amendments, Q1-Q8, and a usage estimate.
- This HANDOFF. No code, no Unity, no builds, no rigs, nothing measured.

## What the plan says (short)
- Order: 1 one compiled world-map generator path (Burst in Editor + player; the first commit merges `gemini/lava-torch-light` bd60a30). 2 torch shadow rays (BUGS #4). 3 TAA-upsampling. 4-6 memory ledger `PeakUsed`, awake-only fluid tiles (BUGS #3), cascade caps. 7 [measure] frame time. 8 shadow cost / dots (BUGS #1). 9 amendment 8.14 draft (shells + op log, STOP). 10-16 coarse-first gen, shells (BUGS #2 in 11b), hand-over, far edits, window shrink, gen speed, [measure] streaming. 17-19 fluids E6/E8/E7 (BUGS #5 checked after 18; any terrain fix is the owner's call). 20-22 light grid, far lights, GI. 23 milestone.
- Memory: peak today 3,343 MB (`Logs/tg2world_20261009_210348_mem.txt`). The last ledger is old (2026-10-01, 3,259 MB). Reclaim first: fluid tiles (~-300 MB est.), then cascade caps (~-200 MB, 8.13 §8). Shells replace the cascades. The 0.1 m window shrinks after far edits land. Engine target is ~2,000 MB est., leaving ≥ 500 MB for the game.
- Key finding: today's generation does not keep up even at walking pace. Shells cut the full-detail radius to ~52-70 m. At 60 m/s level flight it is still ~3x short (est.), hence Q1/Q2. Step 16 measures it, because the next action depends on the result.
- Every number in the doc carries a date and source. Pre-2026-10-04 frame times are marked old. Estimates are marked (est.).

## Evidence
- Read: ROADMAP.md, BUGS.md, KNOWN_ISSUES (16b, TAA, checker, 3 GB, walk ledger, Burst vs Mono, pool full), SUPERVISOR.md Q26 bullet, 8.13 §8, 8.9 §2, LODConfig, task 30 commit e4fa714, and the gemini branch's SVO_LIGHTING_NOTES review header.
- No screenshots used. No existing image showed a target problem better than words.

## Notes for the supervisor
- The lava fix `bd60a30` is NOT on main yet. Step 1 merges it first.
- The parked task 31 (memledger) becomes step 4. `later/38` and `later/40` are attached to steps 8 and 11b.

## Next step
Owner answers Q1-Q8. Then the supervisor writes step 1's task file (and steps 2-3 per Q6).
