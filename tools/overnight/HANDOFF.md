# 143-look-torch-reach: DONE (capture only, nothing shipped). Sheet `Screenshots/143/torchreach143.png`; the owner picks a reach.

## What changed (commit below)
- `Assets/Game/PlaygroundCapture.TorchReach.cs` (new, `-torchreach 1`, `-trarms`, `-trlift`), hook in `PlaygroundCapture.BlackShape.cs`;
  `tools/torchreach143.py` (sheet + counts); `Screenshots/143/torchreach143.txt` (+ `_floor.txt`); TASKS.md "Task 143". No default change.
- Arms use ONLY the three existing C.11 knobs on LightingConfig (`atten = 1/(1 + k d^2)`, colour x intensity, hard cutoff at range):
  | column | pointFalloffK | lightIntensity | lightRangeM | model reach (0.4 / 0.2 colour units) |
  |---|---|---|---|---|
  | today (HighContrast preset) | 0.85 | 0.90 | 8.5 | 1.2 / 2.0 m |
  | small room (= LightingConfig's own C.11 defaults) | 0.35 | 1.6 | 8 | 2.9 / 4.5 m |
  | half a tunnel | 0.15 | 2.4 | 14 | 5.8 / 8.6 m |
  | 10-15 m tunnel section | 0.06 | 3.0 | 24 | 10.4 / 15.3 m |
  These are GLOBAL: k and intensity also apply to lava's aggregate light and coloured torches; range scales lava's range too.

## Evidence
- Fast suite PASS 935 FAIL 0 SKIP 15 (`Logs/overnight/143-tests.out`).
- Launch 1 `Logs/lookreview_20261025_083830.txt` (torches ON the floor): the floor the torch stands on stays dark in EVERY arm (light
  0.05 m above it = grazing N.L, `PointLightNdotL` on; same for the wall a torch touches). Kept as a finding: `Screenshots/143/torchreach143_floor.png`
  / `.txt`. Relaunched once with the torches 1.5 m up (wall height / stone post), `-trlift 15`: `Logs/lookreview_20261025_084941.txt`
  (shots `LookReview/2026-10-25_085216_high`, ~4 min incl. load). Both launches: lights registered 0 -> 3, all torches placed.
- Looked at both sheets. Lifted: today = a dot in the tunnel and on open ground, a ~1 m glow in the room. Small room = the room's floor
  and corner walls lit, tunnel end wall + ~3 m. Half = the whole room lit, the tunnel lit about halfway back. Tunnel arm = the room bright,
  the tunnel walls lit nearly to the mouth (11 m), on open ground a ~6-8 m ground disc and the block face 6 m behind. The wall the torch is
  mounted on stays dark (grazing), in every arm. No cutoff ring visible in any frame (the 24 m ring lies outside these views).

## Phase 7 caps / budgets (stated, NOT changed)
- MAX_ACTIVE_LIGHTS = 32 (§0.2/§3.8): no conflict; selection is by distance to the player, not by range.
- lightRangeM is §3.8's per-pixel cost bound (LightingConfig: "not merely cosmetic"). 24 m vs 8.5 m = ~8x the area each light touches;
  every in-range tier-0 pixel traces up to 4 torch shadow rays (task 46), now up to 24 m long. Phase 7 measured 32 lights +0.33 ms
  (ranges 8 m, before shadow rays; PHASE_7_COMPLETION ~l.1110). The cost of longer range is NOT measured -- a perf task should measure
  it (32 torches, 24 m) before shipping the 14 m or 24 m arm.
- Shadow rays exist only for tier-0 hits (`Raymarch.compute:5180`); tier >= 1 hits use the unshadowed sum, so a longer range leaks
  through walls for torches within range of the tier-0 box edge.
- Hard cutoff: value at the cutoff is 0.014 today, 0.07-0.08 for the three new arms -- a faint ring may show where the full 14/24 m
  circle is in view on open ground (not seen in these frames).

## Next
- Owner picks a column (or asks for a value between). Shipping it = changing the preset's three numbers (+ a perf check of the range).
- Floor/wall grazing (a placed torch barely lights the block it sits on) is a separate look question for the owner.

## For the doc
- Lit px at native 1080 (>= 10 luma above the same pose with the light off; `Screenshots/143/torchreach143.txt`):
  tunnel 4,404 -> 107,664 -> 323,075 -> 902,933; sealed room 83,475 -> 384,248 -> 840,783 -> 1,207,843;
  open ground 2,942 -> 80,085 -> 354,290 -> 680,800 (today -> small room -> half tunnel -> 10-15 m).
- Model reach (light down to 0.4 of full colour): 1.2 m today -> 2.9 / 5.8 / 10.4 m.
- `Screenshots/143/torchreach143.png`: rows = built tunnel (from its mouth), sealed 5 m room, open ground; columns = today ~1-2 m |
  small room | half a tunnel | 10-15 m section, values under each heading. Look at row 1: how far back toward the camera the tunnel walls
  light; row 3: the lit ground disc and the block face behind the torch.
- `Screenshots/143/torchreach143_floor.png`: the same with the torch ON the floor: the floor stays black in every column (grazing light).
