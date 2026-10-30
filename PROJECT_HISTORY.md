# Project history (state notes moved out of CLAUDE.md)

Moved verbatim from CLAUDE.md's "Current state" section on 2026-10-30 (task 154, owner
2026-10-25: less text re-read every turn). Newest first. These are dated snapshots, not
current requirements; CLAUDE.md keeps a short "Current state" that points here.
The 2026-10-01 paragraph also records owner intent (M1 8 GB, 3 GB cap, long render
distance, independent biome/landform maps, many towns) -- still in force unless a later
decision replaced it.

**engine-phase merged to `main` on 2026-10-22** (task 109, merge `a5f6440`; whole suite `PASS 935 FAIL 0
SKIP 25`). **Next: the owner's quality pass (2026-10-22)** -- performance (stable 30 fps), lighting, world gen,
visuals BEFORE populating the world; crafting / the core loop come after.

**Surface rebuild DONE (2026-10-09).** The terrain overhaul B1-B10 (plus lake
fixes 37e/42) is finished; map v4 is the DEFAULT tg2 world (owner Q35 6a;
`-mapversion 3` / `MAPV=3` still gives the pre-rebuild generator: same shapes, but
since task 45 built with Burst floats, so not byte-identical to the old map), and
`surface-rebuild` was merged to `main` on 2026-10-09 (queue task 43). Whole suite
`PASS 662 FAIL 0 SKIP 18` (2026-10-09). **Next: ROADMAP.md step 3, the
engine capability + performance phase, starting with its ONE plan doc.** The
2026-10-01 paragraph below is history (the direction was picked: B).

**Order of future work: `ROADMAP.md` (owner, 2026-10-08) overrides older future plans.**
Engine capability + performance (lighting, LOD shells, resolution, streaming, fluids) come
BEFORE the player and placement.

**Where the project is (2026-10-01).** Owner review:
https://claude.ai/code/artifact/7443de71-6716-4f99-869b-98d3c6814fd7 . The tg2
terrain looks good but is not shaped for play (seed 42: median slope 30°, 73% of
land "mountainous", 4% open; only the plains are gentle) and the owner wants it
to play well. The recommended next step is a short game-design brief (world
structure, planned movement abilities, progression, settlements, fluids,
signature features, placement), then a rebuild of the surface around play
(that report's "direction B"). **Do not start new terrain-generator work until
the owner picks a direction.** Owner intent recorded so far: minimum spec M1
8 GB; 3 GB process cap; long render distance; natural-looking but NOT bound to
realistic topography (nothing sticking out); biomes and landform regions are
INDEPENDENT maps (snow beside desert is intended), biomes should be LARGER and
not every biome x landform pair need appear in every world; object placement
will later read the pair from a "world record"; many towns; progression as fun
as Terraria's, carried by the overworld; spawn not decided (`Tg2Spawn` is a
Playground placeholder); the player character is a placeholder (don't design
its movement rules yet -- only a list of planned abilities is wanted).

Phases 1-7 are CLOSED and merged (Phase 7: `9b3ee50`, 2026-09-24). The
large-world / terrain-overhaul work -- TerrainGen v3, the far band, and the
walkable sizeClass 2 island (`-world tg2`), design doc §12-15 -- was merged to
`main` on 2026-10-01. EditMode is `PASS 625 FAIL 0 SKIP 2` (2026-10-01; skipped:
the report-only PaletteSceneExport and the world-tree test, parked while the
tree is off). `./run-phase7-rig.sh` last reported `PASS 32 FAIL 0` (2026-09-24).
Phase 7's §10 flags (shadow rung, torch-through-wall, lava light) are still the
owner's calls.

Phase 4 (streaming & persistence) is functionally correct: all EditMode tests
pass, admission/eviction/persistence gates are green. Current work is
throughput and frame-time, not correctness. Don't assume this paragraph is
still accurate — run the EditMode suite before starting anything, and check
`git log` / the latest `Phase4Acceptance/*/phase4_report.txt` for the real
current numbers before reasoning about what's slow.
