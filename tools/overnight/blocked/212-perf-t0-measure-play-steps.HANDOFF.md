# 212-perf-t0-measure-play-steps: IN PROGRESS (interim note; the final HANDOFF overwrites this)
- 8.17 adopted (874f431). Cost split arms flood_off / skyfill0 / -costpaths cliff (7232a42), fast suite PASS 1049 FAIL 0 SKIP 22.
- Clean walk_near set (warm-up B Logs/tg2world_20261115_142714.txt; Logs/costsplit_2026-11-15_143504.txt): FULL p50 ~107 ms,
  flood_off ~80, skyfill0 ~67, sun_off ~79, shells_off ~98, ceiling 24 (432 rows, warm, AC).
- cliff1 path had owner input (Logs/overnight/212/idlewatch_C.txt) -> discarded, redo pending.
- ioreg HIDIdleTime is reset every ~10 s by a running player; input is read with Logs/overnight/212/hididle (CoreGraphics per event type).
