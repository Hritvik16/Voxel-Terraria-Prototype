STOP_FOR_OWNER -- task 137 measured: the hole leap is exact but NOT faster, so it stays OFF. Question: build the data-derived local
height bound ("shell column tops", a NEW 28 KB GPU structure; design in `Screenshots/137/height_bound_design.txt`)?
(a) yes, as the next perf task (recommended; estimate -2 to -3 ms of the +6.1 ms noon canyon cost, not measured), (b) no, accept the noon cost.

# 137-perf-near-shadow-hole-leap: DONE. Exact (0 px everywhere tested, incl. an edit-op pillar), within noise at every pose -> OFF (`-holeleap 1` = on).

## What changed (commit 4ea51a3)
- `Raymarch.compute`: `ShellHoleBox` + the leap loop in `ShellMarch`'s hole branch (`_ShellHoleLeap`). After a hole leap, iterations that
  start in shell 1's hole box (in its published box, on the ray's side of tier 0's layers, below the sky exit, farthest corner inside shell 1's
  outer bound) repeat the same tier-0-chunk leap without the distance/resolve/tier-0 tests: same leaps bit for bit, same iteration count and cap.
  Only constants the shader already has: no memory, nothing to keep up to date on edits. Diag counters ShadowStats 80-81 (STATS_LEN 82).
- `RaymarchFeature.cs`: `ShellHoleLeap` (OFF; `-holeleap 1`). CPU twin `ShellRayReference.March(holeLeap)` / `HoleBox`.
- Test `ShellHoleLeapTests` (new): every field equal ON/OFF on ~85k rays: primary, shell-hit and near sun rays, small step caps, and an
  adversarial 300 m pillar just outside the hole (1,238 near rays blocked by it). `ShellMarchCacheTests.TerrainView` made internal for reuse.
- Capture: cost-split arms `holeleap_on/off`; `-cliffbefore 3`; `-holepillar 1` (`PlaygroundCapture.HolePillar.cs`). `tools/holeleap137.py`.
  TASKS.md "Task 137".

## Evidence
- Fast suite PASS 923 FAIL 0 SKIP 15 (`Logs/overnight/137-tests.out`). Filtered run first: 8/8 pass (`Logs/overnight/137-filter.xml`).
- Identity, no-upscaler, ON vs OFF: cost-split raw/raw2 at walk_near and walk_open, noon and sun 20: 0 px (8 pairs).
  cliff1 A native 0 px, cliff2 A native 0 px, pillar native 0 px (`Logs/lookreview_20261024_194339.txt`, `Screenshots/137/identity137.txt`).
  The pillar control (near shadows OFF) differs by 68,392 px, so that pose really does exercise shadow rays through the hole into the shells.
  The pillar: 42 box ops via EditOps.Record, held by shells 1-3, band ceiling 19 m over the ground. FSR 3 stills are temporal, so they are not used.
- Timing (warm, AC, no pause during the arms; warm-up `Logs/tg2world_20261024_211554.txt`; decisions written first). Table in `Screenshots/137/perf137.txt`.
  - A first cost split was launched without `-shells 1 -farshade 1`, so no shells were drawn: VOID (`costsplit_..._212211`). It only kept the chip warm.
- Why it cannot help: at noon walk_near a near sun ray takes 8.0 shell iterations, and only ~1.0 of them are in the hole box. ~6 climb
  uniform-air shell chunks to the shells' GLOBAL top (~1100 m) and find nothing ("blocked by the shells 0").
- An earlier identity launch quit at cliff2 (19:42:13). The owner paused at 19:42:23, so the owner most likely closed the player. The relaunch ran after the pause ended at 21:00.

## Left open
- The local bound (above): needs the owner (new GPU structure; task rule). The hole leap code stays as an exact, harmless OFF switch.
- `Builds/Playground.app` is built from 4ea51a3; it defaults to the same behaviour as before.

## For the doc
| warm, AC, quality, FSR 3 540 rows, p50 ms | OFF | ON | ON - OFF (noise) |
|---|---|---|---|
| noon walk_near | 62.7 / 60.5 | 61.3 / 62.5 | +0.3 (1.2) |
| noon walk_open | 36.0 / 36.1 | 36.0 / 35.9 | -0.1 (0.0) |
| sun 20 walk_near | 53.8 / 53.7 | 53.5 / 53.4 | -0.3 (0.2) |
| sun 20 walk_open | 43.8 / 44.4 | 44.2 / 45.3 | +0.65 (1.6) |
| MODE=walk 3 min, sun ~30 deg | 11.14 | 11.12 | 0 |
(`Logs/costsplit_2026-10-24_213718.txt` noon, `_214843.txt` sun 20; walk `Logs/tg2world_20261024_215912.txt` ON, `_220355.txt` OFF)
- Identity: raw stills 0 px (8 pairs); cliff1 / cliff2 / pillar native 0 px. Memory: none added (peak 2296 MB both walk arms). Default: OFF.
- Noon canyon shell iterations per near sun ray: 8.0, of them in the hole 1.0. This is why the hole leap cannot move the +6.1 ms.
- `Screenshots/137/pillar137.png`: three panels from the side of a 250 m stone pillar built with edit ops.
  - Left = hole leap OFF, middle = ON. Look at the dark diagonal band across the slope, the pillar's shadow: identical in both.
  - Right = the control with near shadows through the shells OFF. The red area is the part of that shadow only the shells' march finds; it turns lit.
- How a bound stays correct for new features and edits: the hole leap uses no terrain data at all. It only uses where shell 1's hole and
  tier 0's layers are, which come from the camera and the layout. So no generator output, structure or player build can make it wrong.
  The proposed column-top bound would be raised in ShellStore.Store, the single door every stored chunk passes through (first load, edit-op
  redraws, player edit summaries, fluid). So a pillar or floating island raises its column the frame it is stored. A stale-high value only costs speed.
