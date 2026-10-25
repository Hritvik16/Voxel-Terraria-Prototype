STOP_FOR_OWNER -- Q (task 140): the cascade tier pools are too small since task 122's biome blend. Raise the caps?
  (a) RECOMMENDED: raise to measured peak x 1.25 (task 53's rule): tier 1 104,000 -> 221,000, tier 2 21,000 -> 41,000.
      Cost +137,000 slots x 512 B x 2 (CPU + GPU) = +140 MB, ONLY with shells OFF (rigs, F6 "LOD shells" off). Default play
      (shells ON, task 93c) does not allocate these pools at all: 0 MB there, and it never threw there.
  (b) Keep the caps and drop the dither from ROCK/soil (surface only): look change at biome borders on cliffs; tier 1 back to ~83k.
  (c) Leave it: rigs and shells-off play keep throwing (stale/missing LOD chunks at 64-290 m, see below).

# 140-bug-brickdatapool-exhausted: CAUSE FOUND, ordering bug FIXED, cap question for the owner (caps unchanged)

## What changed (commit f4d8f97)
- `CascadeTierPool.cs`: `SubmitPrecomputed(coord, data, store)` clears pending EVICTED chunks first when the pool cannot take a whole
  chunk (`PressureReliefsTotal/SlotsFreed`); pass 1 factored as `ClearEvictedPending`; diagnostic `ExhaustedTotal` + one warning
  (first 3, then every 512th) splitting held slots into resident / non-resident / unowned chunks (per-ring-slot owner).
- `StreamManager.cs:1248` passes `_store`. `Phase4Bootstrapper.cs`: `-tier1cap N` / `-tier2cap N` (measurement only; default unchanged).
- Stream probe prints `cascade tier N pool: cap / in use / peak / reliefs / still exhausted`. `CascadeTierPoolPressureTests` (3). TASKS.md "Task 140".

## Findings
- WHERE: NOT the tier-0 pool. Stack: `BrickDataPool.Alloc <- CascadeTierPool.WriteChunkFromDownsampled <- SubmitPrecomputed <-
  StreamManager.DrainCompletions <- Update <- Phase4Bootstrapper.LateUpdate`. Only tier 1 at first (tier 2 was hidden, see below).
- "16 times" was the runner's `head -20` on the Player.log; the real count was **2,777 per walk** (`Logs/overnight/140-before-Player.log`).
- WHY THE VALVE DIDN'T HELP: cascade pools have no valve; evicted chunks' slots return only in `UploadDirty` at the END of
  `StreamManager.Update`. A throw in admission escapes Update, so that frame's UploadDirty never ran and the pool stayed dry (bug, fixed:
  18-20 reliefs freed 93,000-160,000 slots per walk).
- BUT the demand itself exceeds the cap: at every exhaustion the diagnostic shows **104,000 of 104,000 slots held by RESIDENT chunks**,
  0 non-resident, 0 unowned, 0 evicted dirty (`Logs/overnight/140-diag-Player.log`). Not a leak.
- ROOT CAUSE: task 122 (2026-10-23 05:20; the first failing walk is 05:53, the previous at 10-22 23:49 had 0). Rock, soil and surface follow
  a per-0.8 m-cell dithered biome field (`ShapeField.cs:346-361`), so every 1.6 m tier-1 brick (2x2 cells) / 3.2 m tier-2 brick in a
  blend band is mixed material, all the way down. `-biomeblend 0` arm: tier 1 peak 82,725 (task 49's 82,641), tier 2 14,434, 0 exceptions.
- Hidden tier 2: when tier 1 threw, tier 2's write (next in the loop) was skipped. With tier 1 uncapped, tier 2 ran dry 823 times.
- WHAT THE PLAYER SAW (shells OFF only): per throw, that chunk's tier-1 entries never reach the GPU (half-updated, stale slot), tier 2 is
  not written, ops caught up for the chunk are skipped (edits recorded while it loaded), and the rest of that frame's streaming update
  (valve, tier-0 upload, edit marks) waits a frame -> stale or missing LOD chunks in the 64-290 m ring. Not visible in the probe's
  shots: `Screenshots/140/before/moving_tier.png` -- the whole frame is blue tier 0 (a near cliff), no cascade ring in view.

## Evidence (all `MODE=stream MOTION=walk`, not timed -- footprint only)
| arm | log | tier-1 peak / cap | tier-2 peak / cap | exceptions (Player.log) | peak footprint |
|---|---|---|---|---|---|
| before (old build) | Logs/tg2world_20261025_073056.txt | (cap) / 104,000 | ? / 21,000 | 2,777 | 2,206 MB |
| relief fix | Logs/tg2world_20261025_074415.txt | 104,000 / 104,000 | 19,815 / 21,000 | 2,586 | 2,281 MB |
| fix + diag | Logs/tg2world_20261025_075251.txt | 104,000 / 104,000 | 19,620 / 21,000 | 1,900 | 2,190 MB |
| tier 1 260k | Logs/tg2world_20261025_080110.txt | 176,516 / 260,000 | 21,000 / 21,000 (823 dry) | (tier 2) | 2,331 MB |
| 260k, -biomeblend 0 | Logs/tg2world_20261025_080759.txt | 82,725 / 260,000 | 14,434 / 21,000 | 0 | 2,323 MB |
| 260k / 80k | Logs/tg2world_20261025_081306.txt | 176,516 / 260,000 | 32,505 / 80,000 | 0 | 2,499 MB |
- Tests: own filter 16/16 (`Logs/overnight/140-filter.log`); fast suite PASS 935 FAIL 0 SKIP 15 (`Logs/overnight/140-tests.out`).

## Left open / next
- The cap decision above (a one-line change in `EngineConfig.Tg2CascadeTierPoolCap` + its comment; then one walk: 0 exceptions).
- Not changed (flag only): an exhaustion still escapes `StreamManager.Update` by design ("the signal to raise the cap"); a catch that
  keeps the frame going would hide that signal.
- run-tg2world.sh's `head -20` hides exception counts; a count line there would be a one-line doc/rig fix.

## For the doc
- Cause: since task 122's biome blend dithered rock under the borders, far LOD tier 1 needs 176,516 brick slots (was 82,725) and tier 2
  32,505 (was 14,434); their pools hold 104,000 / 21,000. Only with LOD shells OFF (rigs, F6) -- default play has shells ON, no such pools.
- Also fixed: one overflow froze the pool for the rest of the walk (cleanup skipped); the fix frees 93,000-160,000 slots per walk.
- Exceptions per walk: 2,777 (`Logs/overnight/140-before-Player.log`) -> 1,900-2,586 with the fix at today's caps -> 0 with caps above
  the peaks (`Logs/tg2world_20261025_081306.txt`). Peak footprint 2,206 MB -> 2,499 MB at 260k/80k (recommended 221k/41k: ~+140 MB).
- Image: `Screenshots/140/before/moving_tier.png` -- tier tint at the walk's pose: all blue (tier 0); the broken ring is not in view.
