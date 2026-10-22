# 43-v4-default-merge-main: DONE. Map v4 is the default tg2 world; `surface-rebuild` merged into LOCAL `main` (nothing pushed). SUITE=all PASS 662 FAIL 0 SKIP 18.

## Heads
- Merge commit **`884e993`** "Merge surface-rebuild: terrain overhaul B1-B10, map v4 default (owner Q35 6a, 2026-10-09)", `--no-ff` on `main`.
- `main` = `884e993`. `surface-rebuild` = `884e993` too.
  - After the merge I fast-forwarded `surface-rebuild` to `main` (`--ff-only`, same tree). Without that, the next commit on `surface-rebuild` would fail a future "main is an ancestor" check.
- Before the merge, `main` (`c05bfd7`) was an ancestor of `surface-rebuild`. No divergence, no conflicts. `git diff surface-rebuild main` is empty.

## What changed (`6316ccf`, on surface-rebuild, inside the merge)
- `Assets/TerrainGen/TerrainSpec.cs`: the `SizeClass2` default `mapVersion` changed 3 -> 4 (comment cites owner Q35 6a).
  - So `-mapversion` absent or 0 now gives v4, through both `TerrainGenChunkSource.MapVersionArg()` and `FarField`.
  - `-mapversion 3` / `MAPV=3` still gives v3. v3's code path is untouched.
- Tests: 23 `TerrainSpec.SizeClass2(seed)` calls that meant v3 now say `SizeClass2(seed, 3)`. They are in 14 files under `Assets/TerrainGen/Tests/`.
  - So the v3 hash/determinism tests are unchanged and still pass.
  - `WorldRecordTests` now asserts "mapVersion 0 keeps the default (v4)" and "SizeClass2(seed).mapVersion == 4".
- `TerrainGenChunkSource.cs`: doc comment only.
- `run-tg2world.sh`: comment only. Unset MAPV adds no flag, so it gets the code default, now v4. It was not running when I edited it.
- `CLAUDE.md` "Current state": new short paragraph. It says the surface rebuild is done, v4 is the default, the merge to main happened on 2026-10-09, the suite line, and next = ROADMAP.md step 3 (engine phase plan doc). It also marks the 2026-10-01 paragraph as history.

## Scripts and defaults
- `run-play.sh` already had `MAPV=4`, unchanged. `run-tg2world.sh`, `run-demo.sh`, `run-farband.sh`, `run-devmap.sh` and `run-overhaul-batch.sh` pass no `-mapversion`, so they now run v4.
- **Still v3, on purpose:** only explicit `MAPV=3` / `-mapversion 3` and the tests that name v3 (the v3 baseline/hash and v3-vs-v4 comparisons).
- Playground/arena worlds that are not tg2 are unaffected.

## Evidence
- `SUITE=all ./run-editmode-tests.sh` under the lock: **PASS 662 FAIL 0 SKIP 18, wall 14m18s** (`Logs/overnight/43/suite_all.out`). This is the same count as task 42, so no test pinned the old default without saying so.
- Fresh release build of `6316ccf` (DIRTY only because of the usual `Playground.unity` churn), launched with `-mapversion 0` (= the default path). Player.log: `[FarField] tg2 WORLD (aligned): world map v4 (biome x3.25, 6 biomes, record 6291456 B)`. Logs: `Logs/overnight/43/play.out`, `Logs/play_build.log`.
- `Screenshots/43/default_world_start_noon.png`: the start view at clock 13:30, F2 help overlay open in the centre.
  - Look at the bottom-right and centre-right: ochre/yellow savanna ground (B10 palette) on the hills.
  - Lower left: green bands and a pale flat patch (a lake bed/sand flat); sea at the right horizon.
  - Top-right minimap: mostly savanna yellow. The help overlay hides the middle of the frame. The log line above is the real proof that the world is v4.
- No performance measurement (task file + rule 10).

## Things the owner should know
- **Working-tree hiccup, fixed.** The uncommitted runner bookkeeping (17 deleted `tools/overnight/queue/*.md` files, already archived in `done/`) came back when I switched to `main` and back.
  - Deleting them was refused by the auto-mode guard, so I MOVED them to `tools/overnight/requeued_by_43_checkout/`. The queue now holds only `43-...md`, as before.
  - Every one of them has a copy in `done/`. The holding folder is safe to delete by hand.
- The `Playground.unity` churn and `HANDOFF.md` were stashed for the checkout and popped back. Stash list is empty, and no untracked file was touched.
- **To push:** `main` is 137+ commits ahead of `origin/main` and NOT pushed (task rule). Pushing is the owner's call.

## Next step
ROADMAP.md step 3: write the ONE engine capability + performance phase plan doc (backlog E1..). The milestone performance check belongs to that phase.
