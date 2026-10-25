# 151-perf-torch-range-cost: DONE -- shipped the "room" arm (k 0.35 / I 1.6 / 8 m, reach 1.2 -> 2.9 m); every wider reach is over +1.0 ms (half-tunnel +2.6..+24, 10-15 m +15..+23)

## What changed (branch engine-phase)
- `LightingPresets.HighContrast()` torch knobs 0.85 / 0.90 / 8.5 -> 0.35 / 1.60 / 8. **Play reads the owner's `LightingConfig.json`, not the code**, so the same three
  fields were set in `~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/LightingConfig.json` and `LightingConfig_HighContrast.json`
  (backups `*.bak151` beside them; copy back to undo). Every rig/launch on this Mac now uses the new knobs.
- New harness `-costsplit -costpaths torch` (`Assets/Game/PlaygroundCapture.TorchCost.cs`, two hooks in `PlaygroundCapture.CostSplit.cs`): night, 32 torches on
  1.5 m posts, arms `tl_K_I_R` / `tl_none`, lava stills. `tools/torchcost151.py` (lit px + sheet). TASKS.md "Task 151". No preset test pins these numbers.
- Fast suite PASS 946 FAIL 0 SKIP 15 (`Logs/overnight/151-fast.out`).

## Runs (warm: warm-up `Logs/tg2world_20261025_140801.txt`, then 3 cost-split launches back to back 14:13-14:47; F X X F per arm in each)
**Conditions flag:** the Mac was UNPLUGGED at 14:19:55 (mid run 1) and the owner was using it (HID idle 16-58 s); runs 2-3 entirely on battery (100 -> 84%,
low power mode off). Runs 2-3 are noisier (FULL range 9.0 / 4.3 ms vs run 1's clean brackets 0.5-0.7).
"saved" = mean of bracketing FULL p50 - arm p50 (negative = the arm costs more than today). FULL = today 0.85 / 0.9 / 8.5.
| arm (k / I / range) | model reach | run 1 (22 torches) | run 2 (31) | run 3 (31) | lit px run 2 (vs none) |
|---|---|---|---|---|---|
| none (lights off) | - | +5.7 | +8.3 | - | 0 |
| today 0.85/0.9/8.5 | 1.2 m | 0 (FULL 56.3 p50) | 0 (58.6) | 0 (56.6) | 12,546 |
| room 0.35/1.6/8 **SHIPPED** | 2.9 m | +0.8 | +2.0 (noise 9.0) | -2.1 (noise 1.1) | 87,530 |
| half 0.15/2.4/14 | 5.8 m | -2.6 (noise 4.0, the unplug) | -23.6 | -13.7 | 196,173 |
| half variant 0.15/2.4/10 | ~5.8 m, cut at 10 | - | - | -3.7 | (bright px 174,693 vs half 204,546, run 3) |
| tunnel 0.06/3.0/24 | 10.4 m | -15.3 | -23.3 | - | 271,311 |
| tunnel variant 0.06/3.0/14 | 10.4 m, cut at 14 | -3.0 | -6.7 | - | 224,972 |
Logs: `Logs/costsplit_2026-10-25_141409.txt`, `_142910.txt`, `_144035.txt`; summary `Screenshots/151/torchcost151.txt`.

## What it says
- Per-pixel cost depends on the RANGE only (Raymarch.compute SumPointLights*/torch rays: k and intensity change no branch), so room (8 m <= 8.5 m) is no extra
  GPU work: measured +0.8 / +2.0 / -2.1, mean ~0. Same-range arms (half vs tunnel_r14) measured differently in runs 2-3: that gap is noise from battery/owner use.
- Decision table row: neither 10-15 m nor half-tunnel <= +1.0 ms; room is -> budget rule "ship the largest arm under +1.0 ms". Variants tried first: range 14 and 10.
- Bigger finding: today's torch lights themselves cost +5.7 / +8.3 ms in a 22 / 31-torch night scene (32-light loop for every pixel + 4 torch shadow rays).
- Scene caveat: the tg2 tier-0 window is 16^3 chunks (+-102 m); the residency ends near its edge, so "half the torches outside the box" could not be placed
  (run 1: 108 m ring, 10 of 16 refused). Runs 2-3 put the outer ring at 40 m (31 of 32 placed, all inside). The pose stands on a ridge: only 1-3 posts in frame.
- Leak (step 2): run 1's outer torches near the box edge (~100 m) show as a lit ridge-top patch (top-left of each cell); the room arm's patch is the same size
  as today's, the 14/24 m arms enlarge it. Lava: room unchanged, wider arms add a red wash (see below).

## Next (owner)
- Owner: room (2.9 m) is close to the "3 m" called not good enough. A real 10-15 m reach needs the torch cost itself cut first (per-tile light culling, fewer
  shadow rays per pixel, or a shorter ray cap), not a knob. A perf plan item, needs approval; not queued.
- The cost numbers were taken on battery with the owner active; a clean AC rerun of `tl_0.15_2.4_10` alone would firm up the cheapest wider option (measure-only, named here).

## For the doc
- Torch reach 1.2 m -> 2.9 m (k 0.85/I 0.9/R 8.5 -> 0.35/1.6/8); lit px in the 32-torch night still 12,546 -> 87,530 (`Screenshots/151/torchcost151.txt`)
- Cost of shipped arm vs today: +0.8 / +2.0 / -2.1 ms saved in 3 warm ABBA launches (mean ~0; range 8 m does no more work than 8.5 m)
- Half-tunnel (5.8 m): +2.6 / +23.6 / +13.7 ms; 10-15 m tunnel (10.4 m): +15.3 / +23.3 ms; range-14 variant +3.0 / +6.7; range-10 variant +3.7 ms
- Today's torches vs no torch lights: +5.7 ms (22 torches) / +8.3 ms (31) -- the torch system itself is a perf item
- Logs `Logs/costsplit_2026-10-25_141409.txt` / `_142910.txt` / `_144035.txt`; warm-up `Logs/tg2world_20261025_140801.txt`
- `Screenshots/151/run1/torchcost_sheet.png` -- six cells, same night pose: today (top-left) shows one dim post; room (bottom-right) a warm patch round the post;
  14/24 m arms (top-right, bottom-left/middle) flood the slope below the eye and light a ridge-top at the top-left (the outer torch near the box edge).
- `Screenshots/151/run2/lava_sheet.png` -- the lava slab is only at the top-left corner of each cell (poor framing); look at the blocks: grey-blue in "full" and
  "room" (unchanged), red-washed in the 14/24 m arms.
