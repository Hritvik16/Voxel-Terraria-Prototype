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
| 1 | Shadow dots on grass slopes / sand lake shores at low sun | owner play notes 2026-10-08, `~/Desktop/Bugs?/` | `later/38-bug-shadow-dots.md` | engine phase, shadow work |
| 2 | Black crescent/oval under the ledge at the `ledge_view` pose | task 34 B5 contact sheet; still there after B8/B10 | `later/40-bug-ledge-view-crescent.md` | engine phase, LOD seam / render work |
| 3 | Coast fills the fluid pool (falling beach sand + sea); sea beyond ~61 m sleeps | task 39, Q31 part 1 (open) | -- | engine phase, fluids (wake on disturbance) |
| 4 | Torch light passes through walls: no point-light occlusion (`Raymarch.compute:1856`); the SVO traces shadow rays for the nearest torches. Owner 2026-10-09: what they liked in the SVO is "how light reacts to its surroundings". Also: merge branch `gemini/lava-torch-light` (bd60a30: lava ignores torch light, Q34 3a; `SVO_LIGHTING_NOTES.md`, line refs wrong) | Gemini SVO lighting notes, Phase 7 §10 flag | -- | engine phase, lighting (first task that builds) |
| 5 | River water steps down into a lake behind grid-edged 2x2 squares (0.2-1 m above the lake; flows in game). Older than 37e/42; rivers are outside the at-rest tests. Also: volcano lava lake never checked for 37e's defect | task 42, `Screenshots/42/seed7_lake_top.png` | -- | terrain follow-up (river/lake design: e.g. end a river at the lake's reach) -- owner decides |
| 6 | Play help panel (F2) prints HTML entities: "BUILD &amp;amp; DIG", "FLUID &amp;amp; DESTRUCTION", "beauty -&amp;gt; sun visibility" (text is HTML-escaped twice, or escaped for a renderer that does not decode) | task 52 still `Screenshots/Engine03/play52/play_fsr3dyn_default.png` | -- | any UI/play task; trivial |
| 7 | Generated sea/lake water is not at rest: stepped surfaces leave water slabs with vertical sides ~1.5 m above the beach. Since task 54 (awake-only tiles) they stand as walls until touched; before, approaching woke and spilled them (filling the pool). Owner 2026-10-10: "This is a bug. World gen should always place all fluids in resting position. So we should start out with 0 in the pool until they are disturbed." Acceptance: every generated water/lava body at rest (one level per body, no side faces above ground), pool 0 tiles on arrival anywhere | task 54 `Screenshots/54/coast_before.png`; task 39 item 2 | -- | owner Q40 (b): engine plan step 18, with #5 (river steps into a lake) |

## Deferred (not bugs, parked by the owner)
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

- **37e's EditMode run ballooned Unity to 22.5 GB footprint (peak 23.1 GB, "Untagged" dirty memory), swap 14.8/15.4 GB** (2026-10-09 15:53, supervisor killed it; the 15:2x hard power-off happened while the same task's first attempt was running, so it was most likely the same run). Suspect: the new untracked `Assets/TerrainGen/Tests/LakeRestTests.cs` (RestJob over 4 seeds' lake cells via `LakeRestHook`) or the generator hook it calls -- NOT proven. 37e is in `tools/overnight/blocked/`, queue PAUSEd. Before re-queueing: find the allocation (one seed, watch `footprint <pid>`), and add a memory watchdog to the runner (kill Unity when swap free < 2 GB).
