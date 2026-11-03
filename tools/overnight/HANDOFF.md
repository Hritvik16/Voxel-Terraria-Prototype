# 169-torch-flood-edit-cuts: DONE. Both exact cuts are in and ON by default; the flood itself stays OFF. Output is identical everywhere.

Gain in tg2's camp is real but modest: per torch -13 %, re-floods per edit 10-11 -> 7, edit settle 2.2-2.8 s -> 1.2-2.1 s (not yet "a few frames").

## What changed (`41814bc`, engine-phase)
- `TorchFloodBuild.cs` cut 1, ANCHORED bricks (`AnchorsOn`, `-tfanchor 0|1`, default 1). An all-air brick is also "direct" when every voxel v has a valid
  first move m from the torch (destination and footprint open), with w(m) + C(v-t-m) == C(v-t), and bbox(t+m, v) all open.
  - That path costs the lower bound, so d = C(v-t) exactly, the same value and bit as a 165 direct brick.
  - The bbox check reuses the summed-area pass with origin t+m. It only runs m's sign cases (the equality needs sign(v-t) == m on m's axes), and it is skipped when no in-reach candidate brick lies on m's side.
  - The pass itself is ~4x cheaper: plane flags are precomputed once per origin, with no per-case divisions or mask re-checks. The plain direct phase went 2.36 -> 0.58 ms on open ground.
- `TorchFloodPool.cs` cut 2, LIT-ONLY edits (`EditLitOnly`, `-tfeditlit 0|1`, default 1). Edits are now kept per brick as a voxel bbox. A torch re-floods only if it
  lights a brick of that bbox grown by 1 voxel, or the grown box reaches the torch's own brick.
  - Why this is exact: closing an unlit voxel changes no cost. Opening a voxel can only make a new path if a voxel next to it was lit. The edited brick's kind/split change only matters where the torch lights that brick.
  - The torch-brick rule covers a sealed torch, which lit nothing before its shell was opened.
  - Torches with a flood in flight (snapshot taken before the edit) or with no cache re-flood as before.
  - New counters: `EditRefloods`, `EditSkips` and `AnchoredSum`.
- Capture (`PlaygroundCapture.TorchFloodL3.cs`): the header now shows the cut switches. Each edit line shows "re-flood N, skipped M", and the A line shows the anchored total.

## Evidence (exactness)
- Filtered run, `TorchFloodL3Tests` + `TorchFloodPoolTests`, Slow `Fuzz_Long` included: PASS 23 (`Logs/overnight/169_filter.out`). After the pass rewrite, the L3 fixture with REPORTS=1 gave PASS 13 (`Logs/overnight/169_filter2.out` / `.log`).
  - Scratch == `Encode(oracle)` byte for byte in the 5 cases: axis wall, x==z diagonal, closed box + L-bend, open ground, doorway.
  - Incremental == scratch after every fuzz op (80 + 600, with the oracle checked every 10th op).
  - Fast suite: PASS 994 FAIL 0 SKIP 16 (`Logs/overnight/169_fast.out`; was 991).
- New tests:
  - `Anchored_PostWithObstacles_EqualsOracle_AndMoreDirect`: an adversarial post with a voxel blocking +x, a plate under one side, a pillar and a ledge on the post. Anchors OFF and ON are both == oracle; direct lit bricks 3,462 -> 5,814.
  - `EditCut_SkipsUnlitRefloodsLitAndNewPaths`, two rooms behind a 1.6 m wall:
    - A block in room 2, inside A's box but unlit by A: only C re-floods, and A is counted as skipped.
    - A sealed cavity dug in the wall: 0 re-floods.
    - A tunnel through the wall (a new path): both re-flood.
    - A nook opened at the wall's face: A re-floods.
    - After every step the union == scratch == oracle.
  - `EditCut_SealedTorch_OpeningItsShellRefloods`.
- In-player (report-only, no frame-time arm). One build, the OFF arm (= 165's rules) first, then ON. Logs: `Logs/costsplit_2026-11-02_162529.txt` (OFF) and `_162823.txt` (ON), summarised in `Screenshots/169/tfl3_169.txt`.
  At every step (camp, D1 wall, D2 pit, D3a, D3b): live == fresh rebuild IDENTICAL, GPU readback == CPU PASS, GPU vs twin 0 levels.

| | OFF (165 rules) | ON (169) |
|---|---|---|
| per torch, one thread (camp scratch) | mean 260.6 / median 274.0 / max 323 ms | 225.8 / 231.0 / 280 ms |
| searched voxels per torch; direct lit bricks | 2,408,997; 2,707 | 2,188,544; 3,177 |
| re-floods: D1 / D2 / D3a / D3b | 11 / 10 / 10 / 11 | 7 / 7 / 7 / 8 (3-4 skipped each) |
| settle: D1 / D2 / D3a / D3b | 2.77 / 2.44 / 2.17 / 2.42 s | 2.05 / 2.00 / 1.23 / 1.46 s |
| camp async fill | 13.45 s | 5.31 s (165's 5.85 / 13.66: very noisy, first-flood stall) |

- Open ground in EditMode: flood 61 -> 32 ms, searched 672k -> 308k. Small rooms cost +1.4 ms (anchor passes that find nothing).

## Not done / next (owner or supervisor)
- In tg2, 2.19 M voxels per torch are still searched. About 4,300 brick-equivalents of lit air fail both brick-level tests. The union-of-bbox test is per BRICK,
  so bumpy ground inside the box blocks whole bricks. A further exact cut would need the test at sub-brick or voxel level, which is new design and needs approval.
  The alternative is 165's Q2 (b): a shorter reach.
- 165's Q1 (memory/slack, 1 vs 2 jobs) is still open and untouched, and 164's direction question is too.
- Merge/apply main-thread p99 is unchanged (1.1-1.6 ms); it is 165's "trim slack / smaller batches" item.

## For the doc
- Per torch flood in tg2's camp (one thread): 260.6 -> 225.8 ms mean (-13 %). Searched voxels 2.41 M -> 2.19 M (`Logs/costsplit_2026-11-02_162529.txt` vs `_162823.txt`).
- Re-floods per camp edit 10-11 -> 7. Edit settle 2.2-2.8 s -> 1.2-2.1 s. Each edit now skips 3-4 torches whose box holds it but that it cannot change.
- Exact: live == scratch == oracle encode in every test (incl. 600-op Fuzz_Long) and at all 5 in-player steps. GPU readback PASS, GPU vs twin 0 levels.
- Open-ground EditMode flood 61 -> 32 ms. The direct-brick pass is 4x cheaper (2.36 -> 0.58 ms).
- Image: `Screenshots/169/wall_off_on.png` shows the D1 wall pose at night, cuts OFF (left) and ON (right). Look at the lit slope and posts in the lower left and centre: they look the same, as intended (the cuts change speed, not light).
