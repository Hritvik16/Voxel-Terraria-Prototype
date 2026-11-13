# 191-placement-preview-all-on: DONE (pictures only). ALL ON (`-trees 1 -ores 1 -fluidpockets 1 -sites 1 -towns 1`) vs today's defaults, seeds 42 + 7, 16 pose pairs + trees / towns alone. Viewer: `Screenshots/191/viewer.html` (Space = ON / OFF / alone, arrows = pose, Z = full size).

## What changed (commit b182509)
- Capture only: the world survey prints a `[mem]` line per pose (CPU / GPU body pool peak + refused, process peak). No default, generator or engine change.
- Reused the first (auth-killed) run's finished shots (01:00-02:21, same build); reran only the 400 m wide shots with a 90 s wait (35 s left shells 2-3 empty: no distant trees). The seed-7 OFF 90 s shot came out facing a different view with the same logged pose (camera likely moved by input while the owner used the Mac); retaken once (`lookreview_wideoff7b.txt`).
- `TASKS.md` paragraph 191. Fast suite **PASS 987 FAIL 0 SKIP 19** (`Logs/overnight/191/fast.out`).

## Memory / pools from the run logs (no timing arms; `Screenshots/191/lookreview_*.txt`)
| run | process peak MB | GPU body pool (near, cap 254,400) peak / refused | shell body peak (s1 / s2 / s3, % of cap) | POOL FULL |
|---|---|---|---|---|
| seed 42 ALL ON (10 poses) | 2,601.6 | 254,400 / **23,020** | 48 / 60 / 38 % | 0 |
| seed 42 ALL OFF | 2,470.1 | 254,400 / 94 | 31 / 41 / 14 % | 0 |
| seed 7 ALL ON (4 poses) | 2,608.3 | 254,400 / **4,450** | 32 / 44 / 33 % | 0 |
| seed 7 ALL OFF | 2,529.2 | 246,855 / 0 | 27 / 23 / 13 % | 0 |
| seed 42 trees alone (2) | 2,511.1 | 254,400 / 6,199 | 39 / 51 / 36 % | 0 |
| seed 42 towns alone (2) | 2,582.9 | 254,400 / 90 | 26 / 34 / 11 % | 0 |
- Memory: ALL ON +80 to +130 MB process peak, all under the 3,000 MB cap. Shell pools never refuse (r0 everywhere; task 188's packing holds). Wide 90 s: shell 3 pages peak 247/320 ON vs 195/320 OFF.
- **The NEAR GPU body pool is the one that overflows**: already ~98-100 % in OFF, trees push it to thousands of refused chunks. A refused chunk is cleared to air and handed to shell 1 (`TerrainClipmap.GpuPool.cs` header). **Likely visible as rectangular PITS**: `towns42_town_150m.png` (2 pits, front) and `on7_town_150m.png` (3 pits) show sunken boxes with straight walls in front of the towns; never in an OFF shot; they appear right after the refused count rose (seed 7: 4,073 -> 4,450). Correlation only -- NOT proven; needs a look before trees go default.

## What the pictures show (all looked at)
- `on42_forest.png` vs `off42_forest.png`: eye level, meadow becomes dense broadleaf forest. The ground under canopies is PITCH BLACK (no sky light under leaves).
- `on42_forest_far.png` (~200 m): forest blocks on green, savanna trees on yellow, none on rock / sand banks / rivers. Placement reads right.
- `on42_jungle.png`: huge canopies, almost everything under them black (worst case of the lighting point above).
- `on42_snow.png`: conifers on snow, black disc shadows. Looks good.
- `on42_town_eye.png`, `on7_town_eye.png`: towns on flattened pads (OFF = stepped terraces / dunes at the same spot); camera-facing house walls nearly BLACK.
- `on7_village_eye.png`: red roofs close up; shaded walls pure black, windows show sky. No buildings on slopes, none floating, in any shot.
- `on42_tall_cliff.png` (crop right half): ORE WALL VEINS, thin tilted green and orange-brown lenses on the vertical face, as task 181 specifies; none on flat ground.
- `on42_surface_lava.png`: a round lava pool with straight vertical walls in a snow conifer forest (volcanic-landform rule; looks cut, not natural).
- `on42_wide_400m_90s.png` / `on7_wide_400m_90s.png`: the filled-in region. Dense forest on green, sparse dots on savanna, nothing on rock / water. Good.

## Per switch
- **trees**: looks ready on placement; needs work on LIGHTING (black under canopies, jungle worst) and the near GPU pool overflow (pits?).
- **ores**: looks ready (only visible as wall veins; caves not posed).
- **fluidpockets**: needs work on look -- the surface pool has sheer cylinder walls; at rest and in the right place.
- **sites**: invisible by itself (planner only); ready if towns go on.
- **towns**: placement / flatten ready; house walls in shade render black; 90 refused near chunks alone, pits seen once.

## Next step
Owner answers the question below. Suggested follow-ups (named, not queued; need owner OK): (1) find out whether the rectangular pits are refused near-pool chunks (a bug task: count `GpuPoolRefusedNow` at those poses); (2) sky/ambient light on shaded vertical walls and under canopies (a lighting task).

## Question for the owner: which switches become default ON?
- trees: (a) ON now (b) ON after the pits/near-pool check (c) ON after the canopy-lighting fix too -- recommended (b)
- ores: (a) ON now -- recommended (b) stay OFF
- fluidpockets: (a) ON now (b) ON after softening the basin walls -- recommended (c) stay OFF
- sites + towns (together): (a) ON now (b) ON after the pits check -- recommended (c) stay OFF

## For the doc
- Process memory peak, ALL ON vs ALL OFF: seed 42 **2,470 -> 2,602 MB**, seed 7 **2,529 -> 2,608 MB** (cap 3,000) (`Screenshots/191/lookreview_{on,off}{42,7}.txt`).
- Near GPU body pool refused chunks, OFF -> ON: seed 42 **94 -> 23,020**, seed 7 **0 -> 4,450** (cap 254,400, full in both).
- Shell pools: **0 refusals** in every run; peak shell 2 ON 60 % / OFF 41 % of cap (seed 42).
- POOL FULL lines: **0** in all 11 launches.
Images (open `Screenshots/191/viewer.html`; Space flips ON / OFF):
- `Screenshots/191/on42_wide_400m_90s.png` vs `off42_wide_400m_90s.png`: the whole region filled in -- dark forest across the green middle, scattered trees on the yellow plateau at right.
- `Screenshots/191/on42_snow.png` vs `off42_snow.png`: bare snow becomes a conifer forest; the best-looking switch result.
- `Screenshots/191/on7_town_150m.png`: a desert town on the savanna; look at the three rectangular pits in front of it (the open problem).
