# A much larger world with long render range, and the terrain overhaul that feeds it

**Status: DESIGN PROPOSAL, NOT ADOPTED.** Written 2026-09-24 for the owner (decision 13).
**§2 and §5 rewritten 2026-09-25 from the owner's Step 0 interview.** The owner
then delegated sign-off ("make your own decisions"), so §2/§5 are ADOPTED AS
DELEGATED DEFAULTS, and the owner can overturn any of them. Delegated decisions: the
hellevator uses layout **H3** (§5.2, stays inside §0.2). §2.10 lists where §2
supersedes §4 and §6.
No code was written for this. It builds on `AMENDMENT_8_11_RENDER_RANGE.md` (also a
draft), whose facts it reuses and does not repeat at length.

Every number is tagged **ESTABLISHED** (read from code or a recorded measurement,
cited) or **ESTIMATED** (derived, not measured). Nothing ESTIMATED here should be
quoted as a result. §7 lists what to measure before committing to anything.

---

## 0. Summary

**The request:** a world several kilometres across, with Palworld-like view
distance. That means mountains and coastlines visible to the horizon, not a hard
cutoff at 290 m. The terrain-generation overhaul comes first, and must be able to
produce cheap, coarse, far-away data directly.

**The recommendation is three bands, built in this order:**

| Band | Distance | Representation | Status |
|---|---|---|---|
| Near | 0 – ~290 m | today's voxel window (0.1 m), unchanged | exists |
| Far | ~250 m – 3+ km | **a heightfield clipmap: 2.5D height and colour, 4 nested levels, generated straight from the terrain generator's 2D fields** | new (F) |
| Mid (optional) | ~290 m – 1.2 km | Amendment 8.11 Option B: a true voxel cascade | **deferred**; build only if §7's seam test says the far band isn't enough |

The terrain overhaul (T) is designed around one idea. **Split generation into a 2D
"shape" function, evaluable at any resolution, and a 3D "detail" function run
only near the player.** That split is what makes the far band cheap. It also
fixes the other blocker: a world whose tallest point today is **12 m**.

**Estimated total:** three phase-sized pieces (T, F, V below), about 25–40 MB of
new GPU memory, and a far-band per-frame GPU cost that is **unknown until
prototyped**. §7's first measurement exists to find that number before anything
else is committed.

---

## 1. Facts today

| Fact | Value | Source |
|---|---|---|
| Island (sizeClass 1) | 954.5 m coast radius, ~1.9 km across | ESTABLISHED, `Content.cs` / 8.11 §0 |
| Max render distance | 290 m (the window's corner half-diagonal) | ESTABLISHED, `LODConfig.TIER_OUTER_RANGE_M` |
| Resident window | 32 × 32 chunks = 409.6 m square | ESTABLISHED, `EngineConfig.WINDOW_CHUNKS_XZ` |
| LOD tiers 1/2 | **same extent** as tier 0, coarser sampling: they cut cost, not range | ESTABLISHED, 8.11 §1 |
| Coarse data source | derived only from **resident** chunks | ESTABLISHED, 8.11 §3 |
| Tallest terrain | `MAX_TERRAIN_HEIGHT` 120 voxels = **12 m**; only chunk layer y=0 generates | ESTABLISHED, `Content.cs:133`, `StreamManager.MAX_GENERATED_CHUNK_Y = 0` |
| Camera altitude ceiling | `MIRROR_CHUNKS_Y` 4 = **51.2 m**; above it every ray starts out of bounds and the screen goes black | ESTABLISHED, `EngineConfig.cs` comment on `MIRROR_CHUNKS_Y` |
| Generation cost | column sampling = 5.16 ms of 5.58 ms per chunk (92.6%), measured **before** Burst | ESTABLISHED, `ColumnSampleJob.cs` header |
| Generator shape | per column: `SampleColumn → height + biome`, plus analytic anchor features (`FeatureCarve`: mountains, craters, caves) | ESTABLISHED, `ColumnSampleJob.cs`, `FeatureCarve.cs` |
| Memory now | physical footprint **1.6 GB** steady state | ESTABLISHED, `PHASE_4_COMPLETION.md` (footprint table) |
| Budget | §11.3 ≤ 3,000 MB; 8 GB machine | ESTABLISHED |

**Two blockers besides range:** the world is 12 m tall, and the camera can't fly
above 51 m. Palworld-like terrain has mountains hundreds of metres high, and a
viewpoint you can glide from. Both have to change along with range.

## 2. Targets (owner interview, 2026-09-24/25)

**Rewritten from the owner's answers in the Step 0 interview.** Rows tagged
**OWNER** are the owner's decision. **DEFAULT** means the owner delegated the
choice ("we'll go with your decision") or it's a stated placeholder. Anything
tagged **ESTIMATED** is still arithmetic, not a measurement. This section
supersedes §4 and §6 wherever they conflict. The conflicts are listed in §2.10.

**The one-line brief (OWNER):** Terraria in 3D, with a Minecraft look at 0.1 m
blocks, Noita's fluids, and Palworld's sense of scale. The terrain has to be as
much fun to dig through as to look across.

### 2.1 Gameplay intent

| | Target | Tag |
|---|---|---|
| Core loop | **Not decided yet.** Designed against a Terraria-style loop: explore across, dig down, gather, build. Horizontal and vertical exploration are weighted equally, which is why view distance **and** the hellevator are both hard requirements. | DEFAULT (assumption, revisit) |
| Look | Crisp and blocky: **Minecraft's aesthetic at 0.1 m blocks.** No smoothed normals. Face normals and per-face AO as today. | OWNER |

### 2.2 World shape

| | Target | Tag |
|---|---|---|
| World | New **sizeClass 2**, **8 km × 8 km**. Additive per §D.2; sizeClass 0/1 and their content hashes untouched. | OWNER |
| Island | One **organic island ~6 km across**, surrounded by ocean to the world edge. The coast has bays, headlands and offshore islets. | OWNER |
| Peaks | **Tallest summit ~600 m** above sea level (one dominant peak); typical mountains 150–400 m | OWNER ("500 m+", pinned at 600) |
| Depth | **500 m below sea level**, in four layers | OWNER |
| Vertical span | −500 m to +600 m ≈ **1.1 km ≈ 11,000 voxels ≈ 86 chunk layers** (today: 1 layer, 12.8 m) | ESTIMATED arithmetic |

Depth layers (thicknesses DEFAULT, to tune on the map):

| Layer | Depth below surface | Character |
|---|---|---|
| Surface | 0 – ~30 m | Biome soil and strata, shallow caves, aquifers |
| Underground | ~30 – 150 m | Biome continues downward (snow → ice caves, desert → underground desert); tunnels and fluid pockets |
| Caverns | ~150 – 400 m | Depth-defined, shared by all biomes: large caverns, lava tubes; oil, acid and sludge pockets; giant mushrooms |
| Underworld | ~400 – 500 m | Lava sea, ash, obsidian |

**Underground biome layering is a placeholder** (OWNER: "pick one and leave it
as a placeholder, we'll think about the underground later").

### 2.3 View distance, altitude, and the hellevator

| | Target | Tag |
|---|---|---|
| Surface horizon | **The whole world: terrain drawn to ~6.5 km.** From a peak you see coast to coast. | OWNER |
| Recognisable shapes | ridges, coast, lakes and landmarks to ~1.5–2 km | DEFAULT |
| Near detail | Unchanged: 0.1 m voxels, editable, shadowed, within ~250–290 m | OWNER (unchanged) |
| Far detail | **Stepped flat-topped columns with face normals**, not a smooth mesh, so the far band stays blocky (0.8–12.8 m blocks) and the 290 m seam is blocks meeting blocks. Not editable in place; follows edits that change a column's top. | DEFAULT (derived from the look target) |
| Camera altitude | **Unlimited, including a top-down map view.** Never a black screen. | OWNER |
| Hellevator | **You can see down your own shaft to the bottom of the world** (500 m), and up it from below. | OWNER (hard requirement) |
| Underground horizontal range | Capped at the voxel window (~200 m) for now; caverns are designed to fit inside it. Long horizontal sight lines underground are the deferred mid cascade (M). | DEFAULT (placeholder) |

**Why the hellevator is affordable:** at 960×540, a 0.1 m voxel is smaller
than a pixel beyond ~47 m (ESTIMATED: 2·tan 30° / 540 rad per pixel). A 2 m
shaft's floor at 500 m down covers about 2 pixels. Below ~50 m, only coarse
data is ever visible. §5 costs two ways to provide it.

### 2.4 World map first

**OWNER-approved:** generation starts with a **2D world map built once from the
seed**, before any voxel is generated. It holds the coastline, mountain ranges,
erosion, a river network drained to the sea, lakes filled to their spill level,
biome regions, and every major feature's placement.

- **Why:** the features that make a world interesting aren't local. A river
  must know where downhill goes; a lake fills to the level where its basin
  spills. A per-column generator (today's) can never produce them.
- **Resolution:** 1024 × 1024 cells at 8 m for the 8 km world (DEFAULT). It's
  deterministic, and ESTIMATED at seconds on the workers at world creation.
- **The voxels stay the ground truth for gameplay.** The map is the plan the
  shape field reads. `S(x, z, lod)` = map (interpolated) + band-limited detail.
- **What it gives for free:** the **minimap and world map**; the far band's
  outer levels (F3/F4 are at the map's resolution); and **PNG-based iteration**,
  where a test renders the map to an image in seconds with no Unity build.

### 2.5 Topography

**Design rules (DEFAULT, from the owner's "looking in any direction should give
the player a reason to go there"):**

1. **Always a landmark on the horizon.** Every direction shows a distinctive
   silhouette. This rule is **testable on the map**: from every land cell, each
   90° view sector must contain at least one landmark within ~1.5 km. The
   thresholds are tuned by looking at the PNGs.
2. **Terrain hides, then reveals.** Ridges and hills block the view, so
   cresting one shows something new.
3. **Detail at every scale:** kilometres (ranges), hundreds of metres
   (features), tens of metres (outcrops).
4. **Each landform has its own character**, independent of which biome covers it.

**No domain warping (OWNER).** Organic shapes come from thresholded noise
fields instead. The coastline is a smooth island envelope plus fBm, cut at sea
level. Biome borders are the contour lines of climate fields.

**Overhangs (OWNER):** there is no general 3D overhang generation. Overhangs,
arches and cliff lips come only from **analytic stamps** placed at anchors (as
`FeatureCarve` does today), which run near the player only.

**Catalogue.** Macro landforms live in the map; meso features are anchor
stamps; micro detail is the detail field `D`.

| Scale | Features |
|---|---|
| Macro (in the map) | One dominant peak (~600 m); mountain ranges with ridgelines, passes and saddles; escarpments splitting highland from lowland; plateaus and table-top mountains; valleys and basins; rolling hill country; badlands; highland moors; a volcano |
| Meso (stamps) | Rock spires and hoodoos; natural arches and rock bridges; tors, boulder fields, glacial erratics; canyons, gorges, ravines; **fissures and cave mouths** (surface entrances to the underground); sinkholes and cenotes; crater fields; cliffs with ledges; rock-pinnacle forests; sea cliffs, sea stacks, sea caves |
| Micro (`D`) | Scattered boulders, outcrops, scree below cliffs, hollows and ledges |
| Fluid-bearing (see §2.7) | Volcano caldera and lava tubes; glacial cirques with tarns; plateau waterfalls; river networks with gorges, deltas and oxbows; geyser basin with hot springs and mineral terraces; tar pits and oil seeps; quicksand flats; jungle bogs; hives; crater lakes; aquifers, underground rivers, ceiling waterfalls into cavern lakes; oil under domed rock caps; acid caves; magma chambers; the underworld lava sea |

**Fantasy landmarks (DEFAULT, owner delegated):**

| Landmark | Decision |
|---|---|
| Giant crystal spires (glow at night, biome-coloured; one aggregate light each, never per voxel) | **In** |
| Colossal fossils (ribcage arches, a skull cave leading underground) | **In** |
| World tree (one; ~300 m tall, caves in the roots) | **In** |
| Meteor craters with a glowing ore/crystal core (extends today's craters) | **In** |
| Stone rings and natural hoops | **In** |
| Giant mushrooms (caverns) | **In** |
| Sky islands | **Deferred, its own decision.** The far band is a single height per column and cannot draw land floating over land. |
| Ruins, megaliths, temples | **Deferred** to the structure/object-placement system |

### 2.6 Biomes and palette

| | Target | Tag |
|---|---|---|
| Roster | Forest, Snow, Desert, **Jungle (stays)** | OWNER |
| Independence | **Biomes are independent of topography**, so any biome can cover any landform (snowy mesas, desert pinnacle forests). Object placement will have to cope with that later. | OWNER |
| Borders | Organic, from thresholded climate fields; no Voronoi straight lines, no domain warp | OWNER |
| Colours | **Defined per biome.** Each biome gets its own surface, soil, 4–5 rock strata and a deep rock, as one material ID per stratum (~45 new IDs of 256). Strata are horizontal geology cut by the terrain, with noisy boundaries and a regional tilt, and adjacent bands alternate light/dark. | OWNER (per-biome) / DEFAULT (mechanism) |
| Palette | First proposal at https://claude.ai/artifact/RBvstn6SySedSfDApnNMvh. **OWNER: "all good but seem to copy Terraria 1:1".** The project needs its own colour identity. **OPEN**; revisit in the cinematic-visuals pass. | OPEN |

### 2.7 Fluids

| | Target | Tag |
|---|---|---|
| Roster | Existing Water, Lava, Honey, plus **Oil, Acid, Toxic sludge, Quicksand, Steam** | OWNER ("add all") |
| New engine work | Oil: floats on water (density layering, if the CA doesn't already do it); burning waits for a fire system. Acid: a reaction that dissolves stone. Sludge: emissive, via the aggregate-light path. Quicksand: a slow viscosity tier. **Steam: a rising mode in the CA, the largest item, lands last.** | DEFAULT |
| Placement | **Generated at rest**, in sealed basins and pockets. Settled fluid costs no simulation; the gameplay is breaching it. Every landform should have a natural way to hold fluid. | OWNER (intent) / DEFAULT (mechanism) |
| Flowing water | Rivers and waterfalls are never at rest. **Plan for spring/sink materials, which simulate only within §7.4's wake radius. Generate still water first** (lakes, tarns, stepped pools); the map carries river channels from day one so flow can switch on later. | OWNER ("yes") |
| Sand / snow | **Simulated, as falling solids.** Sand already is (`MaterialRules.cs:75`); powder snow joins it. Generated as a **thin surface layer (a few metres) over rock, slopes clamped to ≤ 45°** so it is stable at rest. Digging causes local slides; biome-wide avalanches cannot happen. Cost is per *awake* tile only (32³, ~0.5 MB, cap 1024 unchanged). | DEFAULT; slide size is unmeasured |

### 2.8 Visuals

| | Target | Tag |
|---|---|---|
| Aesthetic | Minecraft-crisp at 0.1 m; far band as stepped columns (§2.3) | OWNER |
| Cinematic pass | Owner judges today's look "subpar". Candidates discussed: sky gradient and sun disc, clouds and cloud shadows, sun-coloured aerial haze, vegetation stamps (trees), water reflection and depth tint, per-biome strata. **Not yet decided; discussion continues after this sign-off.** | OPEN |

### 2.9 Open items (not blocking Step 1)

1. The gameplay loop (§2.1).
2. Underground biome layering and horizontal underground range (§2.2, §2.3).
3. The palette's own identity (§2.6).
4. The cinematic pass (§2.8).
5. Sky islands (§2.5).
6. ~~Hellevator tier layout vs §0.2's `LOD_TIERS = 3`~~. Decided under delegation:
   **H3** (§5.2), which keeps `LOD_TIERS = 3`.

### 2.10 What this supersedes elsewhere in this document

- §4.2's far band is **4 levels (F0–F3, 3.28 km)**. It is now **5 levels
  (F0–F4, ~6.55 km)**, drawn as stepped columns.
- §4.3's "camera up to ~400 m" and its 8-layer surface-following mirror are
  replaced by **unlimited altitude**, a camera-following mirror, and
  hellevator coarse tiers (§5.2).
- §4.1's T now starts with **the world-map stage** (§2.4) and includes the
  stamp catalogue (§2.5), per-biome strata materials and generated fluids.
- §4.4's M is split. The **vertical** part (hellevator) is now required. The
  **horizontal** part (long underground sight lines) stays deferred.
- §6's order is unchanged for Steps 1–2 (far-band prototype, then generator
  split); the map stage becomes part of Step 2.

## 3. Why the current design can't simply be stretched

This is covered in detail in 8.11 §§1–4. In short:

- Raising `TIER_OUTER_RANGE_M` makes rays march further, then die at the window
  guard. **Cost rises; range doesn't.**
- Growing the window (8.11 Option A) is quadratic. XZ = 64 is ESTIMATED at about
  +1.8 GB, past §11.3.
- Converting to a true voxel cascade (Option B) reaches about 1.16 km with three
  tiers, but still needs a coarse data source beyond residency, and touches §0.3's
  protected layout. That's a phase in itself, and it still stops at about 1.2 km.
- **None of these reaches a 3 km horizon affordably.** Beyond about 1 km a
  0.1 m-voxel world is sub-pixel anyway. A pixel at 960×540 and a 60° field of view
  covers about 2.1 m at 1 km (ESTIMATED: 2·tan(30°)·1000 / 540). That's the
  regime where every large-world engine switches to a 2.5D heightfield or impostors.

## 4. The proposal

### 4.1 T: terrain-generation overhaul (first, and the foundation)

**Split the generator in two:**

1. **Shape field `S(x, z, lod)` → `{height, biome, surface material, water level}`.**
   Deterministic, 2D, and evaluable at any resolution. It's **band-limited**: at a
   level whose texel is `t` metres, it skips noise octaves with wavelength below
   `2t`. Coarse evaluation is therefore both cheaper and alias-free, rather than a
   point-sampled fine field that shimmers as the camera moves. Anchored features
   (mountains, craters, ridgelines, rivers) stay analytic, as `FeatureCarve` already
   is, so they evaluate at any resolution for free.
2. **Detail field `D(x, y, z)` → material.** Caves, overhangs, arches, ore, strata.
   It runs **only for resident near chunks**, on top of `S`. The far band never
   calls it, because caves and overhangs are invisible at a kilometre.

**Taller world:** raise the height range to ~250–300 m. That means
`MAX_GENERATED_CHUNK_Y` goes past 0 and the vertical residency changes (§4.3).
Height noise and anchors are re-authored at the new scale.

**Rules the overhaul must keep:**
- Burst strict-float determinism: the existing Burst-vs-Mono byte-identity
  benchmark extends to `S` at every LOD.
- New content goes in an additive sizeClass 2 (§D.2); content hashes for 0/1 are untouched.
- **`S` at LOD 0 must equal the voxel surface exactly**, meaning the top solid
  voxel of every column `D` produces, wherever `D` doesn't carve the top. That's an
  EditMode oracle; §4.2's seam depends on it.

**Cost (ESTIMATED):** one phase. The generator code is about 1,400 lines today
(`WorldGen/*.cs`). The overhaul mostly restructures it; it doesn't grow it. The
new oracles are LOD consistency, determinism and the band-limit check.

### 4.2 F: far-field heightfield clipmap (the range)

**Data:** four nested, camera-centred rings of 1024 × 1024 texels. Each texel holds
height (u16, 0.1 m units), surface-colour index (u8) and flags (u8: water,
edited), 4 bytes in all.

| Level | Texel | Extent | Covers out to (half-extent) |
|---|---|---|---|
| F0 | 0.8 m | 819 m | 410 m |
| F1 | 1.6 m | 1.64 km | 819 m |
| F2 | 3.2 m | 3.28 km | 1.64 km |
| F3 | 6.4 m | 6.55 km | **3.28 km** |

**Memory:** 4 × 1024² × 4 B = **16 MB**, plus a max-height pyramid per level for
empty-space skipping (+33%), about **21 MB** in total (ESTIMATED arithmetic, not
measured). **It's a separate buffer; the clipmap and brick layouts §0.3 protects
are untouched.**

**Filling it:** straight from `S(x, z, lod)` on the existing generation workers,
toroidally, one strip of rows or columns at a time as the camera crosses a texel.
The rings are power-of-two, with bitwise wrap, per CLAUDE.md's hard invariant.
Cost at 60 m/s traversal: F0 advances about 75 texel rows a second, i.e. about 77 k
columns/s. At the pre-Burst ~0.31 µs/column (5.16 ms / 16,384 columns), that's
**~24 ms of worker CPU per second, ~2.4% of one core** (ESTIMATED; band-limited
coarse levels will be cheaper). Upload is about 300 KB/s, far inside §4.3's
3.1 MB/frame budget. **No chunk residency is involved**, which is exactly what
8.11 §3 said was missing.

**Rendering, and why it's small:** today, a ray that leaves the voxel window ends
with `hit = false` and draws sky (`Raymarch.compute`'s window guard). F turns
that exit into a **hand-off**. The ray continues from the exit point as a
heightfield march in F0 → F3, stepping by the max-height pyramid. **This
project's roof-skip pyramid (commit `5856e2b`) already builds that kind of
structure from the clipmap**, so the technique is in the codebase and measured.
It's roughly 20–60 steps per far pixel (ESTIMATED), and only for pixels whose ray
escaped the window. Today those pixels cost nearly nothing.

- **Shading:** the look-1 path with the far texel's colour index and a normal from
  height differences; distance haze as today. Sun shadows on far terrain use a
  heightfield march toward the sun on the same pyramid. Rung 3 ("off") applies
  per band.
- **The seam at ~250–290 m:** within an overlap band both representations exist.
  Blend by distance, and let haze carry the transition (the look already hazes
  toward 290 m). §4.1's LOD-0 oracle makes the two surfaces agree to within a
  texel; §7 measures whether that's visible.
- **Edits:** a CPU edit that changes a column's top voxel updates that column's
  F0–F3 texels (max/mean by level) from the CPU state, which keeps the invariant
  that the CPU is the only terrain writer. Edits are sparse, so this is cheap and
  bounded. Edited far texels persist with the deltas, so a return visit shows
  them.

**Cost (ESTIMATED):** one phase: the data ring and streamer, the GPU pass, far
shadows, the seam blend, and a CPU heightfield-march oracle (the analogue of
`RaymarchReference`) for GPU-vs-CPU pixel agreement.
**§0.3 impact:** the primary traversal's *exit* behaviour changes, from
`hit = false` to a hand-off. That's a traversal change and needs review, but it
needs no layout or sync-contract change.

### 4.3 V: vertical range and altitude (needed by T and by flight)

- **Advance a ray to the window's entry plane instead of killing it**, the fix
  `EngineConfig.cs` already names for the 51.2 m ceiling. With F in place, a ray
  that never enters the window is simply a far-band ray. That removes the
  altitude ceiling as a black-screen failure.
- **Surface-following vertical residency:** keep the mirror a modest number of
  chunk layers tall (about 8, i.e. 102 m), centred on the camera's height above
  the local surface, rather than growing it to cover 300 m of mountain. Anything
  outside is drawn by F. Mirror memory grows linearly: today's 4 layers → 8 layers
  is about +64 MB of clipmap per copy (ESTIMATED from 64 MB per 4 layers,
  8.11 §1). The dense brick pool tracks surface *area*, which mountains raise
  only modestly (ESTIMATED; measure).
- `WINDOW_CHUNKS_Y` (CPU, 16) versus `MIRROR_CHUNKS_Y` (GPU) stay separate, per
  CLAUDE.md's landmine note. The oracle tests that build tall synthetic windows
  are unaffected.

**Cost (ESTIMATED):** about half a phase, and it can land inside T or F.

### 4.4 Deferred: M, the mid voxel cascade (8.11 Option B)

Build this only if §7's seam test shows the heightfield looks wrong at 290 m –
1.2 km. That would be because overhangs or cliff faces read as mush there, which
at a ~2 m pixel footprint (see §3) is unlikely. It's a phase plus a §0.3
review; 8.11 costs it at about 1.4 GB of pools and clipmaps (ESTIMATED).

## 5. Cost table (updated to §2's targets, 2026-09-25)

**Every figure here is ESTIMATED unless tagged.** The memory arithmetic reuses
8.11 §1's ESTABLISHED tier-0 clipmap figure: 64 MB per copy for a 32 × 32 × 4
chunk window, with two copies.

### 5.1 Summary

| Piece | GPU memory | CPU | GPU per frame | Engineering | Review |
|---|---|---|---|---|---|
| **T** terrain overhaul: world map, `S`/`D` split, stamp catalogue, per-biome strata, generated fluids | ~4 MB (minimap texture) | map build once per world (~seconds, erosion dominates); ~12 MB RAM for a 1024² map at ~12 B/cell; far `S` cheaper per column than today | 0 | **~2 phases** (was ~1: the map stage, erosion/rivers/lakes and the stamp catalogue are new) | generation determinism (§0.3); **content-hash scope**: `ContentVersionHash()` is global today (ESTABLISHED, `Content.cs:215`), so sizeClass 2's new materials need their own scope or they invalidate sizeClass 0/1 worlds |
| **F** far heightfield, **5 levels** (F0–F4, 0.8–12.8 m texels, ~6.55 km reach), stepped columns | **~27 MB** (5 × 1024² × 4 B = 20 MB, +33% max-height pyramid) | ~2–3% of one worker core at 60 m/s (unchanged: F0 dominates); F3/F4 filled from the map | **UNKNOWN.** Far pixels go from ~free to a pyramid-accelerated column march plus a shadow march. **At map-view altitude almost every pixel is a far pixel**, so that pose is the worst case. | ~1 phase | traversal exit (§0.3) |
| **V/H** camera-following mirror + hellevator coarse tiers (§5.2) | **+195 MB** (option H5) or **+302 MB** (option H3) | **coarse generation for everything outside the 204.8 m CPU ring**, with persisted edits applied: a new subsystem, cost unknown | small for the entry-plane advance; shaft rays march coarse tiers (unknown, but only for pixels looking into shafts) | **~1–1.5 phases** (was ~0.5) | clipmap Y dims + tier layout + §3.9 sync (§0.3); **§0.2 `LOD_TIERS` if H5** |
| **Fl** fluids: Oil, Acid, Sludge, Quicksand, then Steam, then springs/sinks | 0 new (tile pool unchanged, 1024 tiles, 520 MB ESTABLISHED) | reactions and densities in the existing CA | small for the first four; Steam adds a rising pass | **~1 phase** (Steam and springs/sinks are most of it) | fluid op-list format unchanged |
| **Materials** ~45 new IDs (strata, soils, ice, fluids) | ~0 (256-entry tables) | 0 | 0 | inside T | `PaletteSyncTests` extended |
| Cinematic pass | TBD | 0 | TBD | TBD (§2.8 open) | — |
| **M-horizontal** (long underground sight lines; 8.11 Option B) | ~+600 MB (8.11) | coarse chunk generation | tier marches | ~1 phase | **deferred** |
| Sky islands | own far representation | — | — | — | **deferred** |

### 5.2 The hellevator: two tier layouts

In both options, the mirror follows the **camera's** height, not the surface.
Tier 0 grows from 4 to **8 chunk layers (102.4 m, ±51 m)**. Tiers 1 and 2 keep
today's voxel sizes (0.2 m, 0.4 m), so Amendment 8.9/8.10's horizontal LOD
behaviour is unchanged. Only their **vertical** extent grows. The XZ footprint
stays the 409.6 m window. A tier's clipmap scales as (vertical extent) / (brick
size)³.

| Tier | Voxel | **H5**: 5 tiers, 2× height per tier | **H3**: 3 tiers, 4× height per tier |
|---|---|---|---|
| 0 | 0.1 m | ±51 m · 128 MB | ±51 m · 128 MB |
| 1 | 0.2 m | ±102 m · 32 MB | ±205 m · 64 MB |
| 2 | 0.4 m | ±205 m · 8 MB | **±819 m** · 32 MB |
| 3 | 0.8 m | ±410 m · 2 MB | — |
| 4 | 1.6 m | **±819 m** · 0.5 MB | — |
| Per copy / ×2 copies | | 170.5 / **341 MB** | 224 / **448 MB** |
| vs today's (64+8+1)×2 = 146 MB | | **+195 MB** | **+302 MB** |

- **H5** is cheaper and grows detail more gradually (0.8 m blocks at 205–410 m
  down). But **§0.2 fixes `LOD_TIERS = 3` "never for v1"** (ESTABLISHED,
  `ARCHITECTURE_v8.6.md:257`), so it needs an explicit spec change.
- **H3** stays inside §0.2 and reaches the same ±819 m. It costs ~107 MB more
  and jumps straight from 0.4 m data to "none" at 819 m.
- Both see the full 500 m depth from the surface. **Neither can be built
  without the coarse-generation source**, because everything beyond the CPU
  ring's ±102 m has no resident data (8.11 §3). That subsystem is the real cost
  of V/H, not the memory.
- **Recommendation: H3.** It avoids a constitution change for ~107 MB, and
  §0.2's rule was written for exactly this kind of temptation. Owner decision
  before V/H is built (§2.9 item 6).

### 5.3 Against the budget

T + F + V/H + minimap adds **~+230 MB (H5) to ~+335 MB (H3)** of GPU memory and
~12 MB of CPU memory to a 1.6 GB footprint (ESTABLISHED, `PHASE_4_COMPLETION.md`).
That's **~1.85–1.95 GB, inside §11.3's 3,000 MB**, leaving roughly 1 GB for the
one term not costed yet:

**NEW RISK, the largest unknown on the memory side: cave-driven dense bricks.**
Today's dense-brick peak (336,731, ESTABLISHED, 8.11 §4) is for a **surface-only**
world: `MAX_GENERATED_CHUNK_Y = 0`, and underground chunks are uniform solid
(sticky-note bricks, near-free). A Terraria-style underground is honeycombed
with caves, and **every cave wall is a dense brick**. Across a CPU ring 16 chunk
layers tall, that could multiply the brick pool well past the surface figure.
Streaming throughput carries the same risk: each column now yields up to 16
generated chunks instead of 1. **Neither can be estimated honestly without a
cave density; both must be measured** (added to §7) once `D` exists. If they
come out too high, cave density or the CPU ring's height becomes the tuning knob.

**Frame time remains the open risk on the GPU side.** Step 1 measures it.

## 6. Order of work, with gates

1. **Prototype F on today's world, before T.** The sizeClass 1 island is already
   1.9 km across, 6.6× wider than what we can draw, so it's a ready-made test bed.
   Fill F from today's `SampleColumn` (not yet band-limited). Measure the
   far-pixel cost with the sustained orbit rig and a high-altitude pose, and look
   at the 290 m seam. **This single measurement decides whether the plan holds.**
2. **T**, the overhaul, with the LOD-0 equality oracle and band-limited `S`.
3. **V** with T, because the taller world needs it.
4. **F** in production form (edits, persistence, shadows, oracle).
5. Re-baseline every rig. This is also the natural Unity-upgrade boundary
   (`UNITY_VERSION_RESEARCH_2026-09.md` §4).
6. **M** only if the step-1 seam evidence demands it.

## 7. Measure before deciding

1. **The far-band GPU cost** (step 1 prototype): sustained golden-hour orbit plus a
   300 m-altitude pose, far band on vs off, with the counterbalanced arms this
   project uses.
2. **Seam visibility at 250–290 m**: before/after stills at three times of day,
   read by eye.
3. **`S` cost per column under Burst** at 4, 6 and 8 octaves. The 0.31 µs figure
   is pre-Burst.
4. **Dense-brick peak with 8 mirror layers over mountainous sizeClass-2 terrain**,
   using `BrickDataPool.PeakUsed`, because the "modest" surface-area growth in §4.3
   is an estimate.
5. ~~Whether the owner's picture of "Palworld-like" matches §2's targets.~~ Done:
   §2 was rewritten from the owner interview, 2026-09-24/25.
6. **Cave-driven dense-brick peak and chunks-per-column streaming throughput**
   (§5.3), with `D` producing caves at the intended density across a CPU ring
   16 layers tall. `BrickDataPool.PeakUsed` plus the Phase 4 rig's load-deficit
   line. This decides cave density and ring height, not the other way round.
7. **Far-band cost at a map-view (top-down, high-altitude) pose**, where
   nearly every pixel is a far pixel (§2.3: unlimited altitude).

## 8. Step 1 result: the far band on today's world (2026-09-25)

**Verdict: the plan holds.** Built as specified in §4.2 with §2.10's changes (5
levels, 0.8-12.8 m texels, ~5.6 km Chebyshev reach, stepped columns), opt-in
with `-farband 1`. Evidence: `run-farband.sh`, logs in `Logs/farband_*`.

**Frame cost (ESTABLISHED, sustained arms: RELEASE, fullscreen, gate 960x540,
3 min per arm, A B C C B A, mean of 30 s windows):**

| scenario | far OFF | far ON | ON + far shadows | notes |
|---|---|---|---|---|
| low orbit, +8 m (13.7 m, always in air) | 49.60 ms | 50.15 (**+0.55**) | 50.43 (**+0.83**) | rep spread <= 0.05 ms; 2.3-3.0 steps per far ray |
| 300 m orbit, pitched 15 deg | 2.59 | 6.01 (**+3.42**) | 6.90 (**+4.31**) | 514 k far rays/frame, 32-37 steps each |
| 1 km orbit, pitched 70 deg (map view) | 2.68 | 11.33 (**+8.66**) | 12.90 (**+10.23**) | 479 k far rays/frame, 38-46 steps each |
| ground orbit (the standard `-sustained` pose) | 36.81 | 38.28 (+1.47, within noise) | 41.67 (+4.86) | **NOT TRUSTED: this orbit passes through rock**, see CLAUDE.md known issues |

The cost lands where it is cheapest to absorb: near the ground the far band is
~1% of a frame dominated by near shadow rays; at altitude, where it is 60-80% of
the frame, the whole frame is 6-13 ms because the voxel band has little to draw.

**Seam at 290 m:** photographed at dawn/noon/golden/night (`-farvisual`). Two
defects found and fixed before measuring: (1) a wall of column faces where the
voxel band's hysteresis ring is not resident -- the hand-off is now per resident
chunk column, not per window; (2) haze saturating high views -- replaced by
exponential-height fog along the ray when the far band is on.

**Memory (ESTABLISHED):** 27.2 MB GPU (rings, mips, staging), matching §5's
estimate. Ring fill: 1.0-1.5 s at startup (5 x 1 M columns, Burst, parallel);
strips of 32 x 1024 columns thereafter; 49 MB uploaded over a 3-minute orbit.

**Known gaps (prototype):** no edits/persistence in the rings; point-sampled
coarse levels (band-limited S lands with Step 2 / TerrainGen); the voxel march's
290 m distance cap can stop a corner ray before the window edge, and the far
march then skips that unmarched corner; far terrain does not shadow near terrain.

Full overnight report with screenshots: https://claude.ai/artifact/NtfmpryEEh6t45twdyeJ3M
