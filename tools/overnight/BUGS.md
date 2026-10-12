# Bugs list -- found, not blocking, fix later

Owner, 2026-10-09: "a bugs list so that we can just solve them later as long as they are not an
absolute blocker for development." Rule for the supervisor: a bug that does NOT block the current
phase goes HERE, not into `queue/`. Only a blocker is queued at once. Bugs are picked up in a
batch at a phase boundary (the next one: inside the engine phase, next to the related work), or
when the owner asks. Ready task files wait in `tools/overnight/later/`.

Engine-level issues with long write-ups stay in `KNOWN_ISSUES.md`; this list is the short,
owner-facing to-do. One line each: what, where seen, task file (if written), suggested home.

## Open
| # | Bug | Seen | Task file | Fix with |
|---|---|---|---|---|
| 2 | Black crescent/oval under the ledge at the `ledge_view` pose | task 34 B5 contact sheet; still there after B8/B10 | `later/40-bug-ledge-view-crescent.md` | engine phase, LOD seam / render work |
| 3 | Coast fills the fluid pool (falling beach sand + sea); sea beyond ~61 m sleeps | task 39, Q31 part 1 (open) | -- | engine phase, fluids (wake on disturbance) |
| 4 | Torch light passes through walls: no point-light occlusion (`Raymarch.compute:1856`); the SVO traces shadow rays for the nearest torches. Owner 2026-10-09: what they liked in the SVO is "how light reacts to its surroundings". Also: merge branch `gemini/lava-torch-light` (bd60a30: lava ignores torch light, Q34 3a; `SVO_LIGHTING_NOTES.md`, line refs wrong) | Gemini SVO lighting notes, Phase 7 §10 flag | -- | engine phase, lighting (first task that builds) |
| 8 | River lips are SOLID ground drawn as water (task 76's stopgap): the player can stand on / bump into "water" at every river step, digging a lip is odd; rivers read as visible terraces (look off), and at site B the river's last cells stand 1-4 voxels above the lake as a ribbon edged by lines. Owner 2026-10-11: "bad for gameplay" -- "fix it later" | task 76 `Screenshots/76/sheet_near.jpg` rows A-D; HANDOFF 76 caveats | -- | R3 held water (after engine step 17, `RIVERS_WATERFALLS_PLAN.md`); site B ribbon with it |

## Deferred (not bugs, parked by the owner)
- `later/78-rig-refresh.md` -- many rigs no longer match the game (Playground/v3 world, Creative, legacy motor,
  no upscaler, 1,024 fluid tiles, windowed, stale gates). Owner 2026-10-11: far-future task; audit + plan first,
  before engine step 23 (the milestone runs every rig).
- `later/37c-B3c-highland-variation.md` -- knolls and dells in rolling highlands (B5 review
  follow-up). Deferred 2026-10-09 to end the terrain phase sooner; revisit after placement.

- Straight biome borders: seed 42's diagonal biome stripes and hard straight colour borders in the
  far band (`Screenshots/B10/seed42_b8_b10_biome.png`, `far_views_a.png`). Q35 2 (a) default
  2026-10-09: parked, revisit after placement (border warp + colour blend; recheck the 900 m rule).

## Idea for the bugs batch (owner, 2026-10-09; not queued)
Owner has Google AI Pro (Gemini). Trial: when the runner hits a Claude usage limit, hand a BUGS.md
task to Gemini CLI headless (same task file + PREAMBLE, same Unity lock, one agent at a time), on
its own branch, "made by Gemini" in HANDOFF; the supervisor reviews before merging. Needs the owner
to install + log in to Gemini CLI, and a small runner change. Not for B steps / engine steps.

## Fixed
(move rows here with the commit when done)

- **#5 River steps into a lake behind 2x2 squares; volcano lava lake unchecked -- FIXED task 76 (2026-10-11).** Map v4
  rivers are a per-column profile (`Rivers.cs`) that never rises downstream and ends AT the lake/sea level (its last
  cell stands at it), held by one-voxel lips. The lava lake was NOT at rest (917k lava voxels beside air in the ring
  between the caldera floor and the inner wall, seed 2026): v4 now fills that ring to the lava level. Tests:
  `FluidAtRestTests`. Shots: `Screenshots/76/sheet_near.jpg` row B.
- **#7 Generated water not at rest (slabs with vertical sides) -- FIXED IN GENERATION task 76 (2026-10-11).** Every
  generated fluid is at rest by test (`FluidAtRestTests`: rivers + sea coast 1-in-6 + lakes for seeds 42/7, the lava
  lake of seed 2026, 24 underground pockets of seed 42: 0 voxels beside/over air). Exposed river water walls at the
  73 sites 7.6 / 3.2 / 10.5 / 1.3 m -> 0 / 0 / 0 / 0. NOT yet measured at runtime: "pool 0 tiles on arrival"
  (a `MODE=poolprobe` walk is the check).

- **#6 Help panel (F2) printed HTML entities -- FIXED task 58 (2026-10-10).** IMGUI rich text does not decode
  entities, so `&amp;` / `&lt;-&gt;` printed literally; the help text now uses `&`, `↔`, `→`. Still:
  `Screenshots/58/play_help_fixed.png` ("BUILD & DIG", "FLUID & DESTRUCTION", "LIGHT & TIME").

- **#1 Shadow dots on grass slopes / sand shores at low sun -- FIXED task 56 (`ade1b22`, 2026-10-10).** Not real
  shadows: temporal shadow reuse (ON in play) took each skipped pixel's sun visibility from the NEIGHBOUR voxel's
  pixel, giving 8x8-pixel lit/dark squares. History is now keyed by the shadow ray's origin cell; vs the CPU
  oracle, false-dark 1.1-5.4% -> 0.01-0.53% (same as reuse off). `./run-shadow-dots.sh`; `Screenshots/56/`.

- **37e's EditMode run ballooned Unity to 22.5 GB footprint (peak 23.1 GB, "Untagged" dirty memory), swap 14.8/15.4 GB** (2026-10-09 15:53, supervisor killed it; the 15:2x hard power-off happened while the same task's first attempt was running, so it was most likely the same run). Suspect: the new untracked `Assets/TerrainGen/Tests/LakeRestTests.cs` (RestJob over 4 seeds' lake cells via `LakeRestHook`) or the generator hook it calls -- NOT proven. 37e is in `tools/overnight/blocked/`, queue PAUSEd. Before re-queueing: find the allocation (one seed, watch `footprint <pid>`), and add a memory watchdog to the runner (kill Unity when swap free < 2 GB).
