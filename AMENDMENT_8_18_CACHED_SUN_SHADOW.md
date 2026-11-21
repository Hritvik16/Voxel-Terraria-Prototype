# AMENDMENT 8.18 -- CACHED SUN SHADOW (DRAFT, NOT ADOPTED)

Task 247, 2026-11-20. PLAN ONLY: no engine code, no builds, no timing. Approval for the design work: owner 2026-11-20, "change how
sunlight is computed, keep the leaves" (`PERF_SLICE_BUDGETS.md` sec. 7b alternative (a); `PLAN_PATH_TO_33MS.md` row S7). Owner's
reason: "1 looks better than 2 as 2 doesnt account for the player messing with the leaves potentially" -- so an edit must update the
cached shadow at once (sec. 2.4).

Labels: **est.** = arithmetic on a measured number (source named). **guess** = judgement, no measurement. Nothing here was timed.

Main sources: `ARCHITECTURE_v8.6.md` §6.1-6.5 and C.10; 8.2's camera-distance LOD rule (inside §6.5); `AMENDMENT_8_12_PHASE7_GATE.md`;
`AMENDMENT_8_14_LOD_SHELLS.md` (shell table, presets); `AMENDMENT_8_17_RENDER_STEPS.md` sec. 3-4 (S2 sun step, shares);
`PERF_SLICE_BUDGETS.md` sec. 2, 6, 7; `PLAN_PATH_TO_33MS.md`; task 244's ray counts `Logs/overnight/244/costsplit_16.txt`; task 246's
`WORLD_FILL_BUDGET.md`; TASKS.md tasks 56 (shadow dots), 124, 228 (`-sunfar`, the slope defect), 232 (`-sunres`); code read:
`Raymarch.compute` `TraceShadowRay` (l. 2298), `SunTraceFace` (l. 4643), the sky-fill bounce leg (l. 4795-4815),
`TerrainClipmap.UploadDirty`, `IChunkDirtySink`, `SunCycle` (`-daylength 1200` in `run-play.sh`).

**Numbering:** task 246 called its clutter-layer amendment "8.18 draft" in passing (no file exists). This file takes 8.18 because its
task file names it; the clutter layer becomes 8.19 if it is written.

---

## 0. Short answer (plain words)

Today every lit pixel sends its own ray toward the sun, every frame (or one frame in four with reuse). In a forest at a low sun those
rays creep through leaf crowns 0.1 m at a time and then on through the shells: 54.7 ms of a 135 ms frame (task 235).

The cache turns this around. A few times a second, the GPU traces lines FROM the sun side across the 0.1 m window, one line per
texel of a set of sun-facing maps, and stores where each line first meets solid voxels and where it comes out again. A pixel then
needs only (1) a short exact ray of at most 8 voxels from its face toward the sun, so small detail still shadows itself crisply, and
(2) four reads of the map. The map is derived GPU state, written from the CPU-written mirror exactly like the mirror itself.

- **Edits** (cutting leaves, digging, building) re-trace only the lines through the edited bricks, in the same frame, before the
  frame is lit (sec. 2.4). ~700 lines per changed brick, est. 0.1-0.4 ms (guess).
- **Look:** shadows stay per-voxel-face (§6.1). Edges are placed to within one map texel: 0.05 m near the player, 0.4 m at the
  window's edge (about 1.6-3 render pixels everywhere). Leaf dapples of one voxel survive at full strength within ~13 m, at about half
  strength to ~26 m. Long dawn/dusk shadows are KEPT at full length (no ray cap).
- **Saving (guess):** forest 16 deg sun 54.7 -> 8-16 ms plus 4-10 ms off the sky fill's bounce; walk_near 12.9 -> 6-11 ms (little).
  Night: nothing (no sun rays at night today). Memory est. +13.4 MB (step 1) to +27.0 MB (with the shell maps), minus 8.0 MB of sun
  history it makes unnecessary at 432 rows.
- **It does not finish the forest.** Est. forest 16 deg after the cache: ~79-93 ms; the unsplit "rest" (55 ms) and the sky rays
  remain. It removes the need for two owner give-ups: capping long low-sun shadows (F1/S2) and capping the bounce sun ray (F2/S5).
- **Kill test first** (sec. 7): a CPU twin against `ShadowRayReference` on adversarial slopes, poles and canopies (EditMode, no idle
  Mac needed), then one capture-only arm that times "contact ray only" at forest_eye 16 deg.

```
            sun  (direction s, 16 deg)
              \   \   \   \   \       <- BUILD: one line per map texel, traced from the sun side
               \   \   \   \   \         across the 0.1 m window, a slice of the map per frame
   leaf crown  ##\## #\##  \##          line stores: entry = first solid voxel,
               ###\#####    \#             exit = first air after it (or entry + 16 texels)
                   \  \      \
                    \  \  Q   \        <- LOOKUP for a lit face F:
                     \  \ :    \          1. contact ray from F's origin cell, <= 8 voxels toward the sun (exact)
   ground ___________\__\:_____\___       2. Q = its end; read the 4 map texels around Q:
                          F                  Q beyond a texel's exit  -> that texel says "shadow"
                                             Q before / inside the run -> "lit" (Q is on the receiver's own surface)
                                          3. bilinear weight of the 4 answers = sunVis (edge soft by <= 1 texel)
   FAR: lines that reach the window edge unblocked ask the far map: is anything in the shells / far band up-sun? (sec. 1.3)
```

---

## 1. Representation (what the cache stores)

### 1.1 Why not a classic depth map, and why not a voxel visibility volume

§6.5 rejects shadow maps: "projecting far-cascade texels onto 0.1 m cubes guarantees acne/Peter-Panning". That is true of a map that
stores only the first-hit depth and compares the receiver against it with a bias: on a face lit at a grazing angle the map's texel
sees the receiver's OWN surface a little nearer the sun (lateral offset w gives a depth error w / tan(theta)), and any bias that hides
it also lets light under nearby casters. Task 228 hit the same trap from the other side: counting a coarse brick solid "two bricks
above the origin" put brick-shaped false shadows on steep bare slopes (`Screenshots/228/sheets/walk_far_hill.png`).

A world-space visibility volume (one bit per 0.1 m cell: "this cell sees the sun") would be exact under §6.5's origin rule, but a full
build is one ray per surface-adjacent air cell: est. 245,704 forest bodies (244's GPU pool peak) x tens of surface cells each = tens of
millions of rays, against today's ~154 k sun rays per frame (244). It cannot be refreshed as the sun moves. Rejected.

### 1.2 What it stores: the first solid RUN per sun line ("run map")

Each map is an orthographic grid in sun space (axes u, v perpendicular to the sun direction s). Each texel is a LINE parallel to s
through the texel centre. The build marches that line through the tier-0 mirror from where it enters the mirror box (or crosses the
content ceiling, `_ContentCeilingVoxelY`) and stores, as two 16-bit depths along the line (unit 1/16 voxel = 6.25 mm, range 409.6 m):

- `entry`: where the line meets its first solid voxel;
- `exit`: where it next reaches air, marching on through solid (uniform-solid bricks in one leap) for at most `RUN_CAP` = 16 texels of
  this map; if still solid there, `exit = entry + RUN_CAP` ("buried").
- Special values: `entry = exit = 0` = blocked before the window (set from the far map, 1.3); `0xFFFF/0` = never built (sentinel).

Lookup rule for a point Q on a pixel's sun line (sec. 3): a texel answers **shadow iff depth(Q) > exit + eps**, else lit. That
includes Q lying INSIDE the texel's first run: a line w away being solid at Q's depth means Q sits on the receiver's own surface (Q
itself is air, the contact ray proved it), so it is lit. This is the second-depth idea (Wang and Molnar's "second-depth shadow
mapping") in voxel form. Why it removes the 228 trap: on a steep slope facing the sun, a neighbouring texel's line enters the slope
and stays inside the hill; Q's depth falls inside that run, so the slope is lit, for any slope more than atan(1/16) = 3.6 deg away
from grazing (the run cap is 16 texels and the lateral error is at most one texel). A receiver behind a real occluder (a crown, a
ridge, a wall) is past that occluder's exit -> shadow. A receiver in a cave or behind a ridge thicker than the cap is past
`entry + RUN_CAP` -> shadow. Binary leaves (`-foliageout 0`, the default; 245 recommends keeping F4 off) are ordinary solid voxels,
so a crown gives many short runs and the gaps between them are what make dapples.

Packed as one R32_UINT per texel (entry | exit << 16). All maps of a kind live in one `Texture2DArray` (one binding, no new buffer
slot; the far-shading kernels sit at 29-31 Metal buffer slots, task 244, and <= 8 UAVs, task 198).

### 1.3 Cascades: four near maps, one far map, and (step 2) shell maps

Texel size grows with camera distance so that a texel is ~1.6 render pixels at each map's outer edge and ~3.2 at its inner edge
(432 rows, ~60 deg vertical FOV: one render pixel = 2.42 mrad, the C.5 derivation's arithmetic). All maps are 512 x 512, centred on
the camera's projection, snapped to whole texels (stable while walking: no shimmer), addressed toroidally (`& 511`: power of two,
bitwise, CLAUDE.md invariant; the anchor is an integer voxel coordinate at the camera's chunk so no large floats enter addressing).

| map | texel | covers (receiver distance from camera) | lines | slots | MiB | refresh period (sun moving) | used for |
|---|---|---|---|---|---|---|---|
| N0 | 0.05 m (half a voxel) | 0-12.8 m | 262,144 | 3 | 3.0 | 16 frames | tier-0 faces |
| N1 | 0.1 m | 12.8-25.6 m | 262,144 | 3 | 3.0 | 32 | tier-0 faces |
| N2 | 0.2 m | 25.6-51.2 m | 262,144 | 3 | 3.0 | 64 | tier-0 faces |
| N3 | 0.4 m | 51.2-102.4 m (the whole 82 m High window) | 262,144 | 3 | 3.0 | 64 | tier-0 faces |
| F | 1.6 m, 1 byte (coverage) | lines of N0-N3: blocked up-sun of the window? | 262,144 | 3 | 0.75 | 64 | = task 133's continuation |
| S4 (step 2) | 0.8 m | 102-205 m | 262,144 | 3 | 3.0 | 64 | shell-hit faces (= task 80's ray) |
| S5-S9 (step 2) | 1.6 / 3.2 / 6.4 / 12.8 / 25.6 m | 205 m - 6.6 km | 5 x 262,144 | 2 | 10.0 | 128 | shell-hit faces |

- **Near maps N0-N3** read only the tier-0 mirror (one LOD, 0.1 m), so 8.2's "shadow LOD = camera distance to the sample" holds
  trivially: the lines see the voxels the primary ray drew.
- **Far map F** answers what task 133's continuation answers today: is the line blocked by shells or the far band BEFORE it enters
  the window? Built by `ShellMarch(..., shadow = true, camera)` from each F line's window-entry point toward the sun, so it samples
  the same shell LOD the camera draws (8.2). 1.6 m texels are fine because those occluders are > ~0-80 m outside the receiver's
  window and the soft term (C.10, kSoft 0.03 in play presets ~0.012-0.09) already spreads such shadows over metres
  (0.03 x 80 m = 2.4 m). A line whose F value is "blocked" shadows every receiver on it (entry = exit = 0 in the lookup).
- **Shell maps S4-S9 (step 2 only)** replace task 80's per-pixel shell-hit sun ray (52,579 rays/frame, 3.05 M steps at forest_eye
  16 deg, a third of the sun steps; 244). Built by ShellMarch with the current camera. Until step 2, shell-hit pixels keep today's ray.
- **Slots:** each refresh period builds a NEW slot with the sun direction of that moment while the two previous slots are displayed
  in a crossfade (sec. 2.2). S5-S9 use two slots and swap without a fade (their staleness is far inside their penumbra).
- **Resolution near the camera (the owner's dapples).** Shadows stay one value per voxel face (the lookup starts from the face's origin
  cell, §6.5), so the map only has to say which faces are lit. One-voxel (0.1 m) gaps in a crown: N0 (0.05 m) always has a whole texel
  inside the gap -> **full-strength dapple to 12.8 m**; N1 (0.1 m) catches it partly -> **~25-100 %, about half on average, 12.8-25.6 m**;
  N2 -> ~25 % at 25.6-51 m (a 0.1 m spot there is 1-4 render pixels). Two-voxel gaps are full strength to 25.6 m, three-voxel to 51 m.
- **Edge softening by distance** (cast-shadow edge position error, at most one texel, shown as a bilinear ramp):
  0.05 m to 12.8 m, 0.1 m to 25.6 m, 0.2 m to 51 m, 0.4 m to 102 m; far-band occluders 1.6 m (inside their own penumbra). Within
  0.8 m of the receiver (the contact ray) there is no error at all. On ground lit at 16 deg a texel stretches 1 / sin 16 = 3.6x along
  the sun azimuth, exactly as the voxel faces' shadows do today.

---

## 2. Build

### 2.1 The build kernel

`CSSunCacheBuild` (new pass, a new kernel, own switch): one thread per line, 16 x 16-line tiles (one threadgroup; neighbouring lines
are parallel and close, so the 32 lanes of a SIMD group take nearly the same path -- unlike today's per-pixel sun rays, where 124 and
232 found the slowest lane sets the cost). Body = TraceShadowRay's DDA (air-mip leaps, occupancy, the same voxel test) run along -s from
the line's box entry, plus the run march. No new terrain data: it reads the mirror, the air mips and the occupancy mask that already
exist. It writes only the cache texture. **The CPU stays the sole terrain writer; the cache is derived GPU state, like the mirror**
(CLAUDE.md hard invariant; it is never read back to the CPU and never written from gameplay).

Full build of one 512 x 512 map: 262,144 lines (vs today ~154 k sun rays per frame at forest_eye 16 deg; 244). A full build of all near
maps at once is ~4 frames of today's sun work (guess) and is never done in one frame except after a teleport (2.5).

### 2.2 Refresh in slices as the sun moves

- **Demand tiles.** Each map has 32 x 32 tiles. Every frame the CPU marks the tiles that can hold a receiver: the camera frustum
  clipped to that map's distance band, projected into the map's sun space (a conservative rectangle per map). No GPU feedback, no
  new UAV. Est. ~40 % of a map's tiles for a level view (guess: ~91 deg horizontal FOV over a disc).
- **Period.** The sun moves 360 deg per 1,200 s in play (0.3 deg/s = 5.24 mrad/s). An occluder D metres away moves its shadow edge
  D x 5.24 mrad/s. Today's soft term spreads that edge over ~kSoft x D = 0.03 D. Staleness stays inside that spread while
  5.24 mrad/s x T < 0.03, i.e. T < 5.7 s, independent of D. The periods above (16-64 frames = 0.5-2.1 s at 30 fps) keep staleness under
  ~1/3 of the soft spread everywhere, and under half an N0 texel for occluders up to ~10 m (a crown over the player).
- **Slots and crossfade.** Slot `next` is built over one period (demanded tiles round-robin, nearest the camera first); then
  `prev <- cur <- next`, and the lookup blends prev and cur with alpha = frames since the swap / period. Shadow edges move smoothly
  (a crossfade inside the soft spread) instead of jumping once per period.
- **Lines per frame, steady (est. from the table, guess 40 % demand):** N0 262,144 x 0.4 / 16 = 6.6 k; N1 3.3 k; N2 1.6 k; N3 1.6 k;
  F 1.6 k (ShellMarch). Total ~14.7 k lines/frame (step 2: + ~5-10 k shell-map lines). Walking adds ~1-3 k lines/frame of newly
  covered rows (N0 scrolls 3.4 texels per frame at 5 m/s, 30 fps).
- **Clock stopped** (rig stills, `DebugFreezeTemporal`, a paused game): no slot rotation; only dirty lines and new tiles are built.
  A still converges within one longest period (64 frames); captures already wait 60 s.
- **Fast sun** (a short `-daylength`, a debug clock scrub): if 5.24 mrad/s x (1200 / daylength) x T exceeds 0.03, periods shrink
  to keep it, up to a line budget; past the budget the frame uses today's per-pixel path (the switch's OFF path stays in the build).

### 2.3 Camera motion, window scroll

- New tiles entering a map's demand rectangle are built into `cur` (and `next` if `next` already passed them) the same frame,
  nearest first, under the line budget. A receiver whose texel is not built yet reads the next coarser map (N0 -> N1 -> N2 -> N3),
  whose texels move fewer per metre; N3 and F are kept fully built over the whole window. Result while flying fast: near shadows
  briefly come from a coarser map (softer by one step), never wrong by more than that map's texel.
- **Window scroll** (the tier-0 window moves a chunk): chunks entering or leaving are mirror writes and mark their lines dirty (2.4). F
  lines whose window-entry point lies in the moved slab are re-marched at high priority: a chunk that just left the window is now up-sun
  in shell 1 and must move from "near run" to "far" (today's continuation does this implicitly every frame).

### 2.4 Edits: the owner's reason for this design

- **Hook.** Every write that reaches the tier-0 mirror already goes through `TerrainClipmap` (`MarkDirty` -> `UploadDirty`). The cache
  takes a list of changed VOXEL BOXES from the same place: a player edit passes the edited voxel's box (EditService implementation
  detail beside its existing `MarkDirty` call; `IEditService.SetVoxel`'s signature and contract unchanged, frozen API untouched);
  streaming arrivals/evictions pass their chunk box at low priority. Step 1 checks whether `_dirtyBrickSlots` already holds only the
  changed bricks (then the hook is just that list).
- **Which lines.** For each map, the lines through the box = the box's projection rectangle in that map's sun space, padded one texel.
  One changed 0.8 m brick at a 16 deg sun: N0 ~23 x 22 = ~500 lines, N1 ~130, N2 ~35, N3 ~10: **~700 lines** (est., geometry). Every
  live slot (prev, cur, next) gets them, or the crossfade would blend in the stale shadow.
- **When.** Frame order: CPU edit -> `UploadDirty` (mirror) -> `CSSunCacheBuild` (dirty lines first, then the period slice) -> primary
  kernels (lookup) -> `CSSkyFillTrace` (lookup). So **a cut leaf's sun patch appears on the floor in the same frame**. Budget: dirty
  lines first, capped at 8 k lines per frame (guess); a 2 m blast (~30-60 bricks, ~5-15 k overlapping lines) finishes in 1-2 frames,
  centre first.
- **Cost per edit (guess):** ~700 lines x ~100 steps = 0.07 M coherent steps -> 0.1-0.4 ms (today's divergent sun rays cost ~5.9 ms
  per M steps at forest_eye: 54.7 ms / 9.26 M, 235 + 244, est.; coherent lines should be cheaper, guess).
- **Far edits / op log (8.14):** edits applied to shells change shell maps and F through the shell upload's dirty list (step 2); until
  then F refreshes on its 64-frame period.

### 2.5 Teleport, start-up

Every slot is invalid. Order: N3 + F fully (2 x 262 k lines at a doubled budget, est. 4-8 frames, guess), then the demanded tiles of
N2-N0. Until N3 exists, faces use the contact ray only (lit unless it is blocked within 0.8 m). A teleport already refills the window
over seconds (KNOWN_ISSUES streaming entries), so this is inside an existing fill. Pose captures wait 60 s: unaffected.

### 2.6 Fluids, torches, moon

- **Fluids:** `TraceShadowRay` treats any non-air mirror byte as blocking (no fluid case in its loop: code read), so fluid in the mirror
  shadows the sun today, and the lines do the same. Every fluid write that reaches the mirror marks its boxes dirty (2.4); an awake
  river at the 6.5 Hz tick (task 97) re-traces its lines within the dirty budget, so a moving water shadow lags at most a few frames.
  Fluid that lives only in the GPU fluid CA's tiles is invisible to today's sun ray and stays invisible to the cache: no change.
  Step 1 lists every mirror writer and adds a test that each one emits a box.
- **Torches / point lights:** unchanged. The torch flood (task 175) and point lights are separate systems; no shared data.
- **Night:** §6.5 skips sun rays below the horizon; the moon term is shaded by the face's sky openness (`skyOpen`), with no shadow
  ray of its own (code read, `CubeFaceLight`, l. 4241-4246). The cache idles at night (no build); its memory stays allocated (13-27 MB).

---

## 3. Lookup

### 3.1 What replaces what

| today | with the cache |
|---|---|
| `SunTraceFace` tier-0 branch: `TraceShadowRay` (full length, soft C.10) + task 133's shell continuation | contact ray (<= 8 cells, exact, soft C.10 on those cells) + near-map lookup x far map F |
| task 80's shell-hit sun ray (`ShellMarch` from the face, every frame) | step 1: unchanged; step 2: contact-free lookup in S4-S9 |
| temporal reuse (`-tshadow`, 1 in 4) and task 232's low-res sun records | not used on the cache path (no history read, no `SunRec`): task 56's "shadow dots" class cannot occur, and 8.0 MB of history is freed at 432 rows |
| sky fill mode 2 bounce leg: `TraceShadowRay` from the ground hit's entry face (l. 4811) | near-map lookup x F at that face, no contact ray (one of 16 averaged samples; exactness is not visible there) |
| sky fill's sky-direction rays | unchanged (they go up the hemisphere, not toward the sun) |

### 3.2 The lookup, step by step (per face with N.L > 0; CPU twin `SunCacheReference`, sec. 6)

1. Origin = hit voxel + face normal, cell centre: **§6.5's origin rule, unchanged**, no bias term ("fix the origin, don't add a bias").
2. **Contact ray:** today's DDA toward the sun for at most `L_c` = 8 cells (0.8 m; one brick), soft term tracked as C.10 does. Blocked
   -> done (exact, as today). Small detail (a pebble, a step, a leaf right above a leaf) self-shadows crisply at every distance.
3. Q = the contact ray's end. Map k = by the RECEIVER's camera distance (the 8.2 rule's quantity), with a 10 % blend band at each
   map's outer edge. If Q's texels are the sentinel, use map k + 1.
4. Read the 4 texels around Q (in `cur`, and `prev` while fading: 8 taps), answer per texel with the run rule (1.2), eps = 1/2 voxel;
   weight bilinearly. Multiply by (1 - F at Q) (bilinear, 4 taps).
5. Soft term: the blocker distance b = depth(Q) - exit (texels that said shadow) + L_c. If kSoft > 0, widen the footprint to
   r = clamp(kSoft x b / texel, 1, 3) texels with 4 rotated taps (PCSS-lite), reproducing C.10's "penumbra grows with clearance
   ratio" look. Hard-shadow mode (§6.5 rung 2) = step 4 only.
6. Bounded cost: <= 8 DDA steps + 8-16 taps per face, no loop whose length depends on the scene. The slowest lane is bounded.

### 3.3 Precision traps and how each is handled

| trap | where it came from | handling | proven by (sec. 6) |
|---|---|---|---|
| **Acne** (a lit face shadowed by its own surface) | §6.5's objection to shadow maps | no depth bias at all: the contact ray covers the face's first 0.8 m exactly; beyond it, "inside the first run" = lit (1.2) | slope sweep test, flat ground at sun 2-90 deg |
| **Peter-panning** (light gap between a caster's foot and its shadow) | bias in classic maps | the shadow starts from the contact ray's exact answer; beyond 0.8 m the edge is off by <= 1 texel (0.05 m near) | thin pole + wall-on-floor test |
| **228's triangle / brick field on steep slopes** | a coarse brick counted solid near the slope's own surface | the run rule: a slope's own surface is inside its run -> lit, for slopes > 3.6 deg off grazing; nothing coarse is ever counted solid (the lines are exact 0.1 m DDA) | slope sweep 10-85 deg x suns 8/16/30/60 deg x 8 azimuths, procedural (not today's terrain: owner rule "no terrain-specific engineering") |
| **Residual: grazing slopes < 3.6 deg** off the sun line (beyond 0.8 m) | lateral texel error > run cap | may turn the self-shadow stripes of a voxel staircase on/off by one texel there (today those stripes exist and are exact) | counted in the sweep; budget: < 0.5 % of lit faces outside 1-texel edge bands |
| **Residual: a crack narrower than a texel** at 50-100 m whose line misses the crack | N3 texel 0.4 m | the crack floor reads "inside the run" = lit unless its own contact ray hits the crack wall | adversarial crack test, reported (not hidden) |
| **Camera moving between slices** | slices built at different times | each slot has one sun direction for all its tiles; tiles are built against fixed snapped sun-space anchors; new tiles fall back to a coarser map, never to garbage | moving-camera CPU twin test: no sentinel ever read as "lit" |
| **Sun moving between slices** | slots of different suns | 3 slots + crossfade (2.2); every dirty line goes into every live slot | staleness test: error vs exact <= 1/3 soft spread |
| **Teleport** | every slot invalid | 2.5 | teleport twin test |
| **Ghost shadows (8.2)** | shadow geometry at a different LOD than drawn | N maps read the single tier-0 LOD; F and S maps build with ShellMarch(camera) = the drawn shells; rebuilt on shell uploads | 8.2's mountain-silhouette check in the step-2 pictures |
| **Toroidal aliasing (§6.2 phantom terrain)** | non-power-of-two rings | 512 and 32 tiles, `& 511`, integer anchors | the ring test every new ring gets |

---

## 4. Budget (all guesses unless marked est.; tier A = M1 Air, 432 rows; rule 14)

Today's numbers: 235 (`Logs/budget/2026-11-17_235256/check.txt`), 233 (forest noon), 244 counts. Steps here = loop iterations as
244 counts them.

| pose | sun today ms | sun steps today (244) | cache: lines/frame + per-face | cache steps (guess) | **sun with cache, ms (guess)** | sky fill bounce leg | tier-A slice "sun shadows" 5.0 ms |
|---|---|---|---|---|---|---|---|
| forest_eye 16 deg | **54.7** | 9.26 M (153.6 k rays, 60/ray, 47 % in shells) | ~15 k lines x ~100 + ~0.2 M faces x <= 8 | ~1.5 M coherent + ~1 M bounded | **8-16** (step 1 ~12-22: shell-hit ray stays) | 25.5 ms total sky fill today; bounce sun leg share unknown -> **-4..-10** | over (forest cap 50 ms applies) |
| forest_eye noon | 18.2 (233) | 4.42 M (155.0 k rays, 28.5/ray; `costsplit_noon.txt`) | same, shorter lines | ~1 M + ~1 M | **5-9** | small | about at slice |
| walk_near 16 deg | 12.9 | 2.36 M (83 k rays, 28/ray) | N0 mostly; lines skim open ground from the window edge (long) | ~1-1.5 M + ~0.5-1 M | **6-11** (little gain: short rays today) | ~0 (sky fill ~0 here, 235) | about at / over slice |
| jungle_eye noon | 15.5 | not counted | like forest noon | -- | **5-9** | unknown | about at slice |
| torch night | 0 | 0 | idle | 0 | **0** | 0 | n/a (flood slice 1.0) |

- **Steady cost while the sun moves** = the rows above (the period slices are in them). **Clock stopped:** build ~0 (dirty + new tiles
  only): lookup ~2-5 ms at forest (guess).
- **Per edit:** one brick 0.1-0.4 ms; a 2 m blast 0.5-2 ms in one or two frames (guess, 2.4).
- **Teleport:** 4-8 frames at a doubled line budget, inside the window refill (2.5).
- **GPU memory (est., arithmetic):** step 1 (N0-N3 x 3 slots 12 MiB + F 0.75 MiB) = **13.4 MB**; step 2 adds S4 3 MiB + S5-S9 10 MiB
  = **27.0 MB total**. Freed: the sun history (`SunRec` 12 B x 2 x 331,776 px) **8.0 MB at 432 rows, 12.4 MB at 540**. Net **+5.4 MB**
  (step 1) / **+19.0 MB** (step 2) at 432 rows. CPU: tile tables < 0.1 MB. Rule 11 default (+50 MB): inside.
- **Tier-A slices (rule 14, `PERF_SLICE_BUDGETS.md` sec. 2):** frame -- fits the "sun shadows 5.0 ms" slice on typical poses at the
  optimistic end, over it at the pessimistic end (walk 6-11); takes nothing from other slices (the build is inside 8.17's S2 step;
  the bounce lookup lowers the sky slice). Memory -- a new line "sun cache 13-27 MB" from the 379 MB headroom, less the freed history.
- **Pass card (rule 12, P2), `CSSunCacheBuild`:** resolution FIXED (map texels, not render or output pixels); every frame, a slice
  (1/16-1/64 of demanded tiles + dirty lines); ~15-25 k lines/frame, step cap per line = the mirror box diagonal (2,048) + RUN_CAP;
  0 texture taps (reads mirror, air mips, occupancy); GPU bytes +13.4 / +27.0 MB; 8.17 step **S2**, share 5.0 ms shared with the
  lookup. **Lookup change** (in the far-shading primary kernels and `CSSkyFillTrace`): per face <= 8 DDA steps + 8-16 taps, replacing
  a ray of 28-60 steps average (p95 160-175 at forest). No new pass at OUTPUT resolution; no new per-pixel ray march (the per-pixel
  march gets SHORTER). The new pass is what this amendment asks approval for (rule 12).
- **Why this differs from sec. 7b (a)'s "3-8 ms" guess:** the build is not free -- at a low sun the lines skim open ground across the
  whole window, and the maps must refresh every 0.5-2 s. The honest range is 8-16 at forest 16 deg.
- **Forest 16 deg frame after the cache (guess):** 135.4 - (54.7 - 8..16) - (4..10) = **~79-93 ms**. The forest still needs sec. 6's
  other levers (F3 solid cores, F5 rows) and the "rest" split; the cache replaces only F1 and F2.

---

## 5. Look (what changes vs today, per pose)

| pose | change you could see | change you should not see |
|---|---|---|
| forest_eye 16 deg | dapples farther than ~13 m a little dimmer (1-voxel gaps half strength at 13-26 m); crown-shadow edges 26-102 m off by up to 0.2-0.4 m (soft spread there is already 0.5-2 m) | long low-sun shadows across the valley: KEPT, full length; shade under crowns |
| forest_eye noon | same as above, shorter shadows | -- |
| walk_near 16 deg | nothing expected (all receivers < 13 m: N0 + contact ray); the grazing-staircase residual (sec. 3.3) is the thing to look for | -- |
| jungle_eye | as forest | -- |
| cliff1 / walk_far_hill (228's pose) | the long cliff shadow edge off by <= 1 N2/N3 texel | NO brick-shaped false shadows on the steep bare slope (the 228 defect): this is the acceptance picture |
| torch night | none (sun idle) | -- |
| sun moving | shadow edges glide in a crossfade, a little softer while moving (<= 1/3 of the soft spread) | no popping |
| cutting leaves | the new sun patch on the floor appears the same frame | -- |
| after a teleport | 4-8 frames of contact-only (lit) sun inside the usual refill | -- |

**Owner give-up items this makes unnecessary** (tier A list, `PERF_SLICE_BUDGETS.md` sec. 5 and rule 14):
1. **Long low-sun shadows** (F1 / S2's ray-length cap): not needed -- lines cross the whole window at build time, and F carries the far
   occluders, at any sun angle.
2. **The bounce sun ray at low sun** (F2 / S5): not needed -- the bounce's sun leg becomes a lookup at every sun angle.
3. Not removed: render rows 360 / 288 in heavy views, and tree density last (the forest is still ~80-90 ms, sec. 4).

---

## 6. Prototype plan (queue-ready outlines, in order; each behind a switch, OFF by default)

**247a -- CPU twin + oracle tests (correctness kill test; EditMode only, no player, runs while the owner works). Model: opus.**
`SunCacheReference` (CPU, VoxelEngine.Rendering): build lines over a `ChunkStore` window for a sun direction; the run rule; the lookup
(contact ray via `ShadowRayReference`, map pick, bilinear, F as a stub); slots, demand tiles, dirty lines, crossfade. Tests vs
`ShadowRayReference.Trace` per face: (1) slope sweep 10-85 deg x suns 8/16/30/60 deg x 8 azimuths (procedural); (2) flat ground,
sun 2-90 deg (acne); (3) thin pole and a wall on a floor (peter-panning, edge within 1 texel); (4) canopy slab with 1/2/3-voxel gaps
at 5/20/40 m (dapple strength per map); (5) a ridge thicker than RUN_CAP with a cave behind (no leak); (6) a sub-texel crack at 60 m
(report); (7) edit: remove one leaf voxel -> only its lines change -> match the reference after the dirty pass; (8) moving camera and
moving sun -> never a sentinel read as lit; staleness bound; (9) toroidal wrap at 511/0. Report a per-map error table
(`Logs/overnight/247a/oracle.txt`). **Kill/stop if:** false shadows > 0.5 % of reference-lit faces outside 1-texel edge bands on any
slope > 3.6 deg off grazing, or 1-voxel dapples in N0 below 75 % strength, or any leak behind case (5). Also reports the build's
steps per line vs the reference's steps per face on the same scenes (a step-count proxy, no timing). No memory/pass card (no drawing).

> **Task 249 result (2026-11-20; `Screenshots/249/oracle.txt`):** kill rule (4) FIRES -- 1-voxel dapples in N0 reach 37 % strength (80 deg
> sun 49 %, 60 deg 38 %, 45 deg 13 %; 8 azimuths each), not the 75 % limit; N1 18-32 %, N2 1-12 %. Sec. 1.3's "N0 always has a whole texel
> inside the gap" misses two things: the bilinear taps reach one texel from Q, and a gap in a leaf layer passes less than a voxel of light at any
> sun off vertical. A continuous model agrees (N0 bilinear 66/47/8 %, nearest-texel 74/58/12 %), so no N0 texel-map variant at 0.05 m reaches
> 75 %. Rules (1) and (5) pass: 0 false shadows out of band on 49 non-grazing slope/sun groups x 4 maps; 0 acne down to a 4 deg sun; 0 leaks
> behind 5a. Found and fixed in the twin: line centres on voxel planes (N2/N3, axis-aligned azimuth) -> 1/8-voxel line offset; staleness
> as written is 2 periods (22 mrad = 75 % of the soft spread at 64 frames, not < 1/3) -> build each slot with the sun 2 periods ahead (<= 1.2
> mrad mean). Reported residuals: caves under rock thinner than the cap leak at N2/N3 (5b/5c: up to 116 of 600 faces at N3, 16 deg); a 1-voxel
> crack deeper than the cap reads shadow at N2/N3. Dirty rect of one 0.8 m brick at 16 deg: 650/225/100/49 lines (est. 500/130/35/10).
> Step proxy (raw 0.1 m DDA cells, no leaps, the oracle's scenes): reference 74.0 steps/face, contact ray 12.4 steps/face; line build N0 59.3,
> N1 81.9, N2 125.9, N3 177.2 steps/line.

**247b -- the one timed kill test (performance task, idle Mac, overnight). Model: opus.**
Capture-only arm `-costarms suncontact`: the far-shading kernels cap the tier-0 sun ray at 8 cells and skip task 133's continuation
(the per-face part of the cache, without the map), plus 8 dummy taps of a constant texture. Warm-up launch, then ABBA cost split
(`sun_off`, `suncontact`, today) at forest_eye 16 deg and walk_near 16 deg, 432 rows. **Kill if** forest `suncontact - sun_off`
> 15 ms (the per-face part alone would eat the saving: then the kernel itself, not the ray length, is the cost -- 124/232's warning).
**Go if** < 8 ms. Between: owner call. ~45 min of an idle Mac.

**247c -- GPU step 1: N0-N3 + F, `-suncache 1` (OFF by default). Model: opus. Requires 247a, 247b (go).**
`CSSunCacheBuild` + `SunCache` (C#: slots, anchors, demand tiles, dirty boxes from `TerrainClipmap`, line budget), lookup in the
far-shading primary kernels (uniform branch; the old path stays the OFF twin) and in `CSSkyFillTrace`'s bounce leg; sun history not
allocated when ON. Shader pins + twin test against `SunCacheReference`. Proof (one launch, <= 10 min, rule 3): pictures OFF / ON /
diff at forest_eye 16 deg + noon, walk_near 16 deg, jungle_eye noon, cliff1, walk_far_hill (228's pose); an edit demo (cut 3 leaf
voxels in view: stills at frame +0 / +1 / +8); memory per rule 11 (process peak, GPU body pool peak + refusals, CPU pool + valve,
ON vs OFF); pass card (rule 12); `-raystats` sun steps ON vs OFF (rule 12 P5) plus new build-line counters; capture-only
`-costarms suncache_off`. HEAVY: yes (a new pass) -> the supervisor runs the budget check next.

**247d -- GPU step 2: shell maps S4-S9 replace task 80's shell-hit ray; shell uploads mark F / S dirty. Model: opus. Requires 247c.**
Pictures at summit_view / peak_from_2km_ground (8.2's distant-mountain silhouette check) and forest_eye 16 deg; memory + pass card.

**The one timed check that confirms the saving (idle Mac, after 247c; a performance milestone task):** one warm-up launch (discarded),
then ABBA cost split `-suncache 0 / 1` at forest_eye 16 deg + noon, walk_near 16 deg, jungle_eye noon, torch_camp night, 432 rows,
play flags. Report the sun step, the sky-fill step and the frame p50 / p95. **Default ON proposal to the owner if** forest 16 deg sun
step <= 16 ms and walk_near is not slower than OFF; **stop** if the forest sun step stays > 25 ms.

---

## 7. Risks, and the cheapest test that kills the idea

| risk | why it might happen | cheapest early test |
|---|---|---|
| **The per-face part stays expensive** | 124: halving traced sun rays saved 0 ms; 232: moving rays out of the big kernel saved nothing. If the cost is the kernel's register pressure / occupancy rather than ray length, a short contact ray + taps may not save much | **247b's `suncontact` arm** (one timed capture). This is the cheapest test that can kill the idea, and it needs no cache at all |
| False shadows / leaks the run rule misses | the 3.6 deg residual, sub-texel cracks, runs over tile seams | **247a's oracle sweep** (EditMode, no idle Mac) -- the cheapest correctness kill test |
| Build cost at a low sun on open ground | lines skim the whole window; walk_near may gain little or lose | 247a's steps-per-line proxy; 247c's build-line counters |
| Streaming churn | the forest keeps a 573-chunk deficit (235): many chunk arrivals = many dirty lines | dirty lines at low priority, budgeted; 247c reports dirty lines/frame |
| Memory | 13-27 MB, plus slots if a tier wants 1024-texel maps | rule 11 lines in 247c/247d |
| Metal limits | far-shading kernels at 29-31 buffer slots, <= 8 UAVs | texture array only, no UAV in the lookup kernels; demand marked on the CPU |
| Amendment conflicts | §6.5's "no shadow maps" | this amendment replaces that sentence for the sun leg beyond 0.8 m only; the exact path stays as the OFF twin and the oracle |

---

## 8. What this amendment changes in the spec (if adopted)

| # | status | change | section |
|---|---|---|---|
| 1 | CHANGED | §6.5 "no shadow maps": the sun leg past the first 8 cells may come from the run maps of this amendment; §6.5's origin rule, C.10's soft term and 8.2's LOD rule stay | §6.5 |
| 2 | NEW | derived GPU state "sun cache" (run maps N0-N3, F, S4-S9): written only by `CSSunCacheBuild` from the mirror and shells, never read by the CPU | §3.7 (beside the mirror) |
| 3 | CHANGED | §6.5's shadow ladder gets a rung 0 "cached sun" above rung 1; rungs 1-3 unchanged | §6.5 |
| 4 | CHANGED | 8.17's S2 sun step = contact ray + lookup + the build pass; its 5.0 ms share now covers both | 8.17 sec. 3-4 |
| 5 | UNCHANGED | CPU sole terrain writer; mirror layout (clipmap, air mips, occupancy, shell bricks); the 1-byte voxel; frozen APIs (`IWorldQuery.GetVoxel`, `IEditService.SetVoxel`, D.1, §4.4); FSR 3 inputs; the fluid sim | -- |
| 6 | RETIRED (when ON) | temporal sun reuse (`-tshadow`), 232's low-res sun, 228's `-sunfar`: not used on the cache path; kept for the OFF twin | 56 / 124 / 228 / 232 |

**Question for the owner:** adopt amendment 8.18 and build the prototype (247a, then 247b, then 247c/247d)? **Recommendation: yes, as
a staged trial.** 247a and 247b are cheap and decide it before any GPU code: 247a needs no idle Mac; 247b is one ~45 min overnight
capture. If 247b's contact-only arm does not save, stop there and spend the effort on F3 (solid crown cores) and the "rest" split.
