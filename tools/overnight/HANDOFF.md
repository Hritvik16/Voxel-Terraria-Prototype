# 150-perf-walk-regression-isolate: DONE -- decision row 3 ("not 1 or 3" at >= 2 ms whole-run); 133's near shell shadows are the largest named piece (~1.5 ms walk, ~2.1 ms = half the gap on the regressed leg); (3) ~0; shell held counts NOT higher than 10-22

## What changed (branch engine-phase)
- `run-tg2world.sh` playwalk branch: `NOMINIMAP=1` drops play's bare `-minimap` (cannot be switched off via EXTRA). No C#, no default changed.
- `TASKS.md` "Task 150" paragraph. Driver: `Logs/overnight/150-driver.sh`, output `Logs/overnight/150.out`. No fast suite (no code; `bash -n` OK).

## Runs (warm steady state, AC 100% charged every launch, no owner pause, back to back 13:14-13:47, same build as 146 = 147e0e8)
Warm-up (discarded): `Logs/tg2world_20261025_131447.txt`. Then base, A, B, base (gaps only the lock's 45 s). 5-min walks, `MODE=playwalk`.
| arm | p50 / p95 / p99 ms | > 33 ms | win mean 0.5-3.5 min | leg 1.0-2.0 min | rows | s1/s2/s3 held (start -> end) | log |
|---|---|---|---|---|---|---|---|
| base 1 | 42.40 / 83.97 / 118.5 | 73.5% | 45.26 | 48.04 | 540 100% | 1746/488/0 -> 879/160/0 | `Logs/tg2world_20261025_131957.txt` |
| A `-nearshadowshells 0` | 42.58 / 70.46 / 83.5 | 81.9% | **43.83** | **45.74** | 540 100% | 1696/440/0 -> 802/98/0 | `..._132648.txt` |
| B `-underwater 0`, no minimap | 42.72 / 86.23 / 129.8 | 75.3% | 44.48 | 47.58 | 540 100% | 1672/438/0 -> 982/94/0 | `..._133338.txt` |
| base 2 | 47.20 / 81.99 / 101.5 | 86.6% | 45.48 | 47.67 | 540 100% | 1627/406/0 -> 1409/302/0 | `..._134031.txt` |
| 10-22 walk (110, 3 min) | 41.0 / - / 99 | 72-78% | (0.5-2.5 only) | **43.42** | 540 fixed | 1801/546/0 -> 1031/406/0 | `Logs/tg2world_20261022_133936.txt`, `_134432` |
"win mean" = mean of the report's per-half-minute window means (same circle, same positions each run); "leg" = windows 1.0/1.5/2.0.

## What it says
- **Whole-run p50 cannot resolve 2 ms here:** the identical base arm spread 42.4 -> 47.2 ms (4 runs on this build incl. 146's 45.7 / 43.6).
  Base 2's +4.8 is its last minute only (windows 4.0/4.5 = 69.3 / 65.7 ms vs base 1's 51.7 / 45.5); its 0.5-3.5 windows match base 1 within ~1 ms
  except 3.5. Held counts there (s1+s2 1241 vs 1087) do not explain +18 ms: an unexplained transient, not a cause.
- **Where the regression lives:** vs 10-22, windows 0.5 and 2.5 are unchanged (47.7-48.7 / 36.3-37.3); windows 1.0-2.0 are +3.8..+5.3 ms.
- **(1) task 133:** A vs base mean: -1.5 ms over 0.5-3.5, **-2.1 ms on the 1.0-2.0 leg** (1.8 / 2.6 / 2.0) = about half of the leg's +4.4 ms. A also has the
  best tail (p95 70 vs 82-86, p99 84 vs 101-130). Matches Q87's measured +1.1..+1.9 ms (now ~1.5 walk, ~2.1 at the worst leg): no reason to revisit keep-ON.
- **(3) -underwater / minimap:** -0.9 ms (0.5-3.5), -0.3 ms on the leg: noise (146's hud_off was ~0 too).
- **(2) shell held:** today's base 1 holds the same as 10-22 on the leg (s1 1236-1427 vs 1187-1475, s2 507-631 vs 384-684, s3 0 in all four arms;
  146's s3 17-27 / s2 up to 1,060 was one run's variation). Across the 4 base runs, window ms vs s1+s2 held: r = 0.16-0.39, ~1.5-1.8 ms per 1,000
  held chunks: a weak contributor at most, and not what separates today from 10-22.
- **Unexplained remainder (~2 ms on the leg):** differences between 110's `MODE=walk` arm and playwalk NOT tested here: `-gamemode survival`,
  `-shadowlevel 0 -roofskip 0 -shadowres full`, `-renderrows dyn` (stayed at 540), 3 vs 5 min, `-memledger -frametiming`, and the build delta 10-22 -> 147e0e8.
- Shell fill on workers today 18.8-18.9 ms/chunk (f1) vs 6.3-8.1 in 146/110's logs: the chip was hot; numbers are warm steady state as required.

Screenshots looked at: `99_end_pose.png` of base 1 and A (`~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Sustained/2026-10-25_132136_tg2playwalk_20261025_131957/` and `..._132827_tg2playwalk_20261025_132648/`):
the same close sandstone slope in both, no holes or black bands; A's shading looks the same at this distance.

## Next
- Owner-independent: if the remaining ~2 ms matters, one more ABBA isolating 110-vs-playwalk flags (`-gamemode survival` first) -- a measure-only task,
  so named here, not queued. Otherwise the ranked cuts in 146's HANDOFF stand (1. shell column height bound would also cap 133's cost).

## For the doc
- Task 133 near shell shadows cost on the walk: **~1.5 ms** (position-matched 45.37 -> 43.83 ms), **~2.1 ms** on the regressed leg (47.86 -> 45.74) = half of the +4.4 ms vs 10-22 there (`Logs/tg2world_20261025_{131957,132648,134031}.txt`)
- -underwater 0 + no minimap: **~0.3-0.9 ms** = noise (`Logs/tg2world_20261025_133338.txt`)
- Shell held counts: same as 10-22 on the leg (s2 507-631 vs 384-684, s3 0) -> not the cause
- Run-to-run spread of the same base arm: whole-run p50 **42.4 -> 47.2 ms** (one late transient); window means agree within ~1 ms -> A/Bs under 3 ms need window means, not whole-run p50
- Warm-up log `Logs/tg2world_20261025_131447.txt`; all arms AC, 540 rows 100%, 0 exceptions
- Image: `.../Sustained/2026-10-25_132827_tg2playwalk_20261025_132648/99_end_pose.png` -- arm A's end pose: the close sandstone slope (same as base 1's), no holes; shows arm A rendered normally, nothing else changed visibly.
