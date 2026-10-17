STOP_FOR_OWNER -- task 93b3: the far band is NOT the shells' extra cost (it costs ~0 after the shells). The +12-15 ms is the SHELL MARCH.
Question for the owner: may the next task (a) split the shell march into the primary ray vs the shell-hit sun-shadow ray (one
launch, 2 new capture-only arms), then fix whichever dominates, or (b) go straight to a shell-march speed-up (design work: e.g.
occupancy/air mips for shells, or ending sky rays at the shells' top)? Recommended: (a). No fix was made in this task; nothing
in the product changed.

# 93b3 far band after shells cost: ISOLATED, premise wrong; no fix (STOP_FOR_OWNER)

## What changed (capture-only diagnostics; commit below)
- `Raymarch.compute`: under `#ifdef VE_FAR_SHADE` ONLY (so CSMain, rigs and shells-off compile from the same code as before):
  the shell march's end type and iteration count are kept, plus counters ShadowStats 40-47 (far starts by shell end type /
  FarStartT, far PRIMARY steps, far shadow rays and steps, far start metres) and two debug views: 12 = FAR START, 13 = SHELL HEAT.
- `RaymarchFeature.cs`: `STATS_LEN` 40 -> 48; `DebugMode.FarStart = 12`, `ShellHeat = 13`.
- `PlaygroundCapture.CostSplit.cs`: arms `farprim_off` (FarField.PrimaryStepCap 0, residency still bound), `fshadow_off`,
  `tiers_farprim`; every arm's first still also logs the far counters and shoots `_farheat/_farstart/_shellheat.png`.
- `KNOWN_ISSUES.md` (SHELLS COST entry: FAR-BAND HALF) and the CLAUDE.md index line corrected.

## Evidence (one ON launch, `-shells 1 -shellworkers 3 -keeptiers 1`, fsr3 540, quality, far band + shadows, farshade/seethrough/riverlook 1, noon)
Warm-up `Logs/tg2world_20261016_194110.txt` (discarded). Split `Logs/costsplit_2026-10-16_194728.txt` (copy + stills in
`Screenshots/93b3/split/`). ALL ON BATTERY (21 -> 13%), so treat absolute ms with care; arms are F X X F inside one launch, noise 0.0-0.9 ms.
| arm (saved p50 ms vs bracketing FULL) | walk_near (FULL ~64.0) | walk_open (FULL ~44.0) |
|---|---|---|
| far_off (93b's arm) | +16.0 | +19.2 |
| farprim_off (far march 0 steps) | -0.1 | +0.9 |
| fshadow_off (far sun shadows) | +0.1 | -0.1 |
| fshade_off (far shading) | -0.1 | +1.2 |
| shells_off (tiers 1-2 drawn) | +15.5 | +12.2 |
| tiers_farprim (tiers, no far march) | +15.8 | +15.3 |
- WHY 93b blamed the far band: with `far_off` RaymarchFeature binds a 1-element dummy as `FarResident`; `ShellMarch` reads it
  through `ShellTier0Holds` / `ShellTier0Missing` (Raymarch.compute ~2670-2690), so every primary shell march ended ABSENT at its
  first tier-0 voxel: `far_off` turned the shells off too. Not a product bug (play never binds the dummy with shells on), a rig artefact.
- Far counters: after the shells every far ray starts at a shell ABSENT end (walk_open mean 51 m, walk_near ~1.1 km: s5-s7 unfilled);
  0.8 far primary steps per far ray (walk_open) vs 19-20 after the tiers; far shadow 1.3 steps/ray vs 5.4. The far band is cheaper after the shells.
- Shells at the pose: walk_near s1-s3 complete, s4 949/33 missing, s5 160/326, s6-s7 0 (120 s wait); walk_open all complete but s3 FULL (30 missing).
- So the line that costs it is the shell march: `ShellMarch(rayStart, rayDir, ...)` in CSMainBody (~3635) and/or the shell-hit
  sun shadow `ShellMarch(so, normalize(_SunDir), ...)` (~3948). Not split (needs one more launch; battery was at 13%).
- Tests: fast suite PASS 802 FAIL 0 SKIP 14 (`Logs/overnight/93b3-fast.out`; includes ShellHandOverTests / ShellOrderTests). No CPU twin test: no rule changed.

## Not done (needs the owner's answer)
- No fix, no ABBA, no before/after stills (nothing to compare). 93b's re-run (queue, Requires 93b3) should wait for the shell-march task.
- Next, if (a): add arms `shellshadow_off` (skip the ~3948 shadow march; a capture-only switch) and a sky-only count, one launch,
  then one fix. Leads: walk_open sky rays take 25-48 shell iterations to end at ~51 m; distant terrain 49-96+.

## For the doc
Task 93b3: the far band is NOT why shells cost more. Drawn after the shells it costs ~0 ms. The shell march itself is the +12-15 ms.
- Far march off (residency kept): saves -0.1 ms (walk_near) / +0.9 ms (walk_open); far shadows 0; far shading 0 / 1.2 (`Logs/costsplit_2026-10-16_194728.txt`).
- Shells -> tiers 1-2: saves 15.5 / 12.2 ms. 93b's far_off arm (16-19 ms) also switched the shell march off (dummy residency buffer).
- Far steps per far ray: 0.8 after the shells vs 19-20 after the tiers (walk_open).
Images (`Screenshots/93b3/split/`):
- `walk_open_full_shellheat.png`: shell-march iterations per pixel at walk_open. Black (lower right) = tier-0 hits, where no shell
  march runs. Blue = <= 24 iterations, high sky. Green = 25-48, the sky near the horizon. Yellow = 49-96 and red = 96+, the distant ridges on the right.
- `walk_open_full_clean.png`: the same frame, beauty (desert ridges, sea and snow at the left, the sky in the upper half).
- `walk_near_full_shellheat.png`: walk_near is almost all black (a near tier-0 wall). Only a top-right patch reaches the shells, in red (96+). The shells still cost +15.5 ms here.
