# WORLD_FILL_BUDGET.md -- what filling the world costs in THIS renderer, and how each kind of fill must be drawn (task 246, 2026-11-20, PLAN)

Owner, 2026-11-20: "its wild to me that trees are causing such a problem ... Does this mean that the rest of the stuff that we want to fill
the world with is only going to cause us more problems? Like smaller foliage, and other items that are necessary to make the world feel
full and alive." This plan is for the placement phase (ROADMAP stage 3). It sits on top of `PERF_SLICE_BUDGETS.md` (tier A = M1 Air 8 GB,
PREAMBLE rule 14).

No engine code changed. No frame time was measured or quoted (rule 10: the owner was using the Mac). Evidence is GPU **ray-step counts**
(task 244's `-raystats 1`), pool occupancy, and stills. **est.** = arithmetic on measured numbers; **guess** = judgement with no
measurement behind it.

## 0. For the owner (one screen)

**Will filling the world keep hurting?** Only if we fill it the way the trees were filled. The counts show that **trees do NOT make the rays
longer overall**. With the dense forest, the total ray work per frame at forest_eye goes DOWN 3-7 %, because trees hide the far valley.
The cost comes from **where** the work happens. Leaves are see-through clumps, so every ray (camera, sun and sky) has to creep through
them 0.1 m at a time instead of jumping over empty space. Fine stepping rises +40-46 %, and sky-light work doubles (+106-115 %).

That gives **three rules** for everything else we add:

1. **Solid things are cheap to draw** (rocks, logs, crates, walls, buildings, the inside of a big bush). A ray stops at the first solid
   voxel, and a solid prop hides whatever was behind it. In the counts, solid near content removes ray work. What solid things cost is
   **memory**. Each 0.8 m block of surface takes one 512-byte slot in the GPU pool. The forest already fills that pool to 96.6 %
   (245,704 of 254,400), and the CPU pool sits at its eviction line.
2. **See-through things are the expensive kind** (leaves, bushes, hedges, grass, fences seen edge-on). Rough rule from the counts:
   **each 1 % of the screen covered by see-through voxels costs about 1.3-1.8 ms on the M1 (est.)** (task 248, measured wood coverage:
   **1.7-2.3 ms, best fit; use ~2 ms**, sec. 1b.4). The dense forest covers 28 % of
   the screen with leaves, which is where its ~36-49 ms comes from (est.). So see-through voxel content needs a budget in % of screen,
   and big bushes should be solid inside.
3. **Grass and small ground clutter must NOT be voxels in the world.** Grass in view would cover ~35 % of the screen in a
   see-through layer: est. +15-60 ms, more than the whole frame. Every shipped open-world game draws clutter as a separate cheap layer.
   That layer holds instanced small meshes (ours would be made of little cubes to keep the look), only near the camera, fading out by
   ~30-40 m, with no shadows of their own. We would draw it after the voxel picture and depth-test it against it. That is a new kind
   of drawing for this engine (an architecture amendment), so it needs your OK. Its budget would be ~2 ms and ~10 MB at tier A
   (guess until experiment E2).

Moving things (creatures, NPCs, falling leaves) are already planned as separate meshes in the spec (§5.4, §6.2) with a 1.5 ms reserve.
Light sources use the torch flood we already have.

**What I recommend:** keep trees as they are (they are the forest's whole porous budget). Make bushes solid-cored. Build props as solid
voxels inside a memory budget. Build ground clutter as a separate near-camera layer. First prove the cheapest version of it with the
experiments in sec. 5, before the placement phase starts.

![forest_eye, trees OFF vs dense, 16 deg](Screenshots/246/forest_eye_off_vs_dense_16.jpg)
*`Screenshots/246/forest_eye_off_vs_dense_16.jpg` (git-ignored): same pose, same 16 deg sun. Left: `-trees 0`, an open valley with far
mountains. Every pixel of that far view costs a long ray, mostly through cheap coarse data. Right: the dense world trees (the default).
About 40 % of the frame changed (`coverage_proxy.txt`), and the far view is hidden. Total ray steps are about the same, but the steps
are now fine 0.1 m steps through the crowns.*

## 1. Counts, not time (rule 10)

Runner `Screenshots/246/run.sh` uses one dev-kernel build, then 8 cost-split launches (4 tree arms x 2 suns). Settings were the same
for every launch: play flags, `-quality high -renderrows 432 -shellwait 60`, `-raystats 1` (32-frame GPU-counted windows, 331,776
primary pixels per frame). Poses were `-costfixed forest_eye` and `walk_open`. Tree arms:

- **off**: `-trees 0`.
- **pre-210**: `-trees2 0`, the trees before task 210.
- **sparse**: `-trees2 1 -trees2dense 0`, task 210's placement.
- **dense**: `-trees2 1`, the default since 214.

Every player log confirms its tree mode and shows no `[kernels] FAILED`. Tables: `Screenshots/246/table_raw.txt` (per band),
`model.txt` (totals). Both are reproduced by `python3 -I Screenshots/246/model.py`.

**walk_open is a control, not a data point.** At the pinned pose it looks over a cliff and the sea into the sun, with no trees in view.
All 4 arms count within 0.8 % of each other (primary 18.00-18.14 M), and only 0.7-0.8 % of pixels change. That is the noise floor.
**walk_near** (always first in a `-costfixed` launch) stares at a surface 0-10 m away (primary 4.9 steps/ray), so it is not used.

### 1a. forest_eye, per frame (M = million steps; t0 = steps in the 0.1 m tier-0 window; leaf = primary pixels whose hit is a leaf voxel)

| sun | arm | primary | sun | sun steps/ray | sky | **all steps** | **tier-0 steps** | leaf % of screen | far (82 m+) % of screen | pixels changed vs off |
|---|---|---|---|---|---|---|---|---|---|---|
| 16 deg | off | 24.38 | 8.80 | 46.2 | 0.93 | **34.11** | **16.42** | 0 | 58.1 | -- |
| 16 deg | pre-210 | 18.05 | 5.48 | 48.6 | 1.35 | 24.88 | 18.79 (+14 %) | 25.0 | 16.1 | 53.5 % |
| 16 deg | sparse | 24.34 | 9.08 | 59.5 | 1.63 (+75 %) | 35.05 | 20.35 (+24 %) | 16.0 | 34.6 | 30.4 % |
| 16 deg | **dense** | 22.06 | 8.87 | 57.5 (+24 %) | **2.00 (+115 %)** | **32.94 (-3 %)** | **22.99 (+40 %)** | 28.0 | 22.9 | 38.5 % |
| noon | off | 26.14 | 3.48 | 18.2 | 0.85 | **30.46** | **13.72** | 0 | 58.1 | -- |
| noon | pre-210 | 18.07 | 2.84 | 25.2 | 1.20 | 22.11 | 16.80 (+22 %) | 25.0 | 16.1 | 54.1 % |
| noon | sparse | 24.21 | 4.59 | 30.0 | 1.46 (+73 %) | 30.27 | 18.00 (+31 %) | 16.0 | 34.6 | 37.7 % |
| noon | **dense** | 22.30 | 4.36 (+25 %) | 28.3 (+55 %) | **1.74 (+106 %)** | **28.41 (-7 %)** | **20.03 (+46 %)** | 28.0 | 22.9 | 39.7 % |

By distance band, 16 deg (`table_raw.txt`): the dense forest's 10-40 m band holds 43.6 % of the pixels (off: 12.9 %), and 53 % of
them hit leaves. Its p95 primary steps there are 160-175 (off 192-207: grazing ground costs the most per ray). Per sun ray the dense
forest takes 48 steps at 10-40 m, 81 at 40-82 m and 55 at 82 m+ (off: 72 / 71 / 37). Pixels changed vs off counts trees plus their
shadows, at a > 24/255 threshold (`coverage_proxy.txt`).

**Material split (task 248, E1 done).** `-raystats 1` now splits the PRIMARY hit into leaf / wood / ground / other / no hit (wood =
task 225's class 1: 147, 204-207, 215, 221; ground = every other generator id that is not leaf; no hit = sky or the far band's
heightfield). Re-run of the 4 arms at forest_eye 16 deg, same runner and pose (`Screenshots/248/run.sh`; table
`Screenshots/248/coverage.txt`, reproduced by `python3 -I Screenshots/248/coverage.py`). % of the WHOLE screen; a band's cells sum to
its share of the screen:

| arm | band | screen | leaf (porous) | wood | ground | no hit | solid on the ground (wood + other) |
|---|---|---|---|---|---|---|---|
| off | 0-10 / 10-40 / 40-82 / 82 m+ | 23.5 / 12.9 / 5.5 / 58.1 | 0 / 0 / 0 / 0 | 0 / 0 / 0 / 0 | 23.5 / 12.9 / 5.5 / 10.9 | 47.0 (82 m+) | 0.1 |
| pre-210 | same | 41.5 / 39.3 / 3.0 / 16.1 | 1.3 / 22.2 / 0.4 / 0.5 | **19.0 / 9.2** / 0.4 / 0.4 | 21.1 / 7.8 / 2.3 / 3.6 | 11.5 | **29.1** |
| sparse | same | 29.9 / 25.9 / 9.6 / 34.6 | 0 / 9.9 / 3.4 / 2.8 | 9.5 / 4.0 / 1.5 / 1.9 | 20.4 / 12.0 / 4.7 / 14.4 | 15.5 | **16.9** |
| dense | same | 23.5 / 43.6 / 9.9 / 22.9 | 0 / **22.9** / 3.4 / 1.7 | 0 / 8.1 / 1.9 / 1.1 | 23.5 / 12.5 / 4.6 / 13.4 | 6.7 | **11.2** |
| **all bands** | off / pre-210 / sparse / dense | | 0 / 24.5 / 16.1 / **28.1** | 0 / 29.1 / 16.9 / **11.2** | 52.8 / 34.8 / 51.5 / 54.0 | 47.0 / 11.5 / 15.5 / 6.7 | |

Leaf coverage repeats 246 (25.0 / 16.0 / 28.0 %). New: **wood**. The pre-210 trees put a trunk right at the camera (19 % of the
screen inside 10 m), so they cover **29.1 %** with wood, not the ~17 % sec. 1b.3 had guessed by decomposition. The dense forest
covers 11.2 % with wood, and nothing of it within 10 m at this pose (its 0-10 m band equals off). Tier-0 steps per frame repeat 246 to
within 1 %: off 16.43 (246: 16.42), pre-210 18.61 (18.79), sparse 20.44 (20.35), dense 23.15 (22.99) M. "Other" (water, placed,
engine ids) is <= 0.1 % in every arm.

### 1b. Cost model

1. **Total steps are not the cost; WHERE the steps happen is.** The dense forest has 3-7 % FEWER steps than no trees, but 40-46 % more
   of them are fine 0.1 m tier-0 steps (dense-brick micro-steps, §6.3) instead of coarse far-band / shell steps or uniform-brick leaps.
   A micro-step reads a voxel byte from a partly-filled brick, which is memory traffic. Neighbouring pixels in a crown take very
   different step counts (wave divergence, §6.5 8.4 note), which a step count cannot show. The measured ratio of +57 % frame for
   trees (210/213, `PERF_SLICE_BUDGETS.md` sec. 6) on flat total steps says the same.
2. **Porous content, per 1 % of screen covered (leaves):** **+0.225-0.267 M tier-0 steps/frame** (sparse and dense, both suns,
   `model.txt`; one term: every step change put on the leaves). **Task 248, two terms (leaf AND measured wood coverage), 16 deg:
   +0.305 M per 1 %** (3-arm least-squares fit through off; the well-conditioned pairs give +0.233 (sparse + dense) and +0.314
   (pre-210 + dense)). It moved UP by ~+20-35 %: the one-term figure hid the wood's negative term inside the leaf rate. Light rays add +41-108 k (sun + sky) steps per 1 %. Sky rays are hit hardest (+106-115 % at 28 % coverage): every
   sky-fill sample has to find a way out of the canopy.
3. **Solid content, per 1 % of screen:** **about -0.15 to -0.20 M tier-0 steps/frame (est.)**. This is a linear decomposition of the
   pre-210 arm: ~17 % of the screen is a near solid trunk (the 0-10 m band grows 23.5 -> 41.5 %, only 1.2 % of it leaf), plus 25 %
   leaves. Its tier-0 rise (+2.4 / +3.1 M) is 2.5-3.4 M below what 25 % of leaves alone would add. That is one arm, so read it as
   "solid content does not add ray work; it removes the work behind it", not as a precise rate.
   **Task 248, from direct coverage (replaces the decomposition): -0.152 M tier-0 steps/frame per 1 % of screen covered by solid
   content on the ground** (3-arm fit, the same fit as point 2; wood + other, 16 deg). That lands inside the -0.15 to -0.20 estimate.
   It is not firm: the fit's residuals are -0.89 / +1.65 / -0.17 M (pre-210 / sparse / dense), and the two well-conditioned pairs
   give -0.190 (pre-210 + dense) and **+0.016** (sparse + dense). The pre-210 + sparse pair is ill-conditioned (leaf : wood 0.84 vs
   0.96, almost the same mix) and is not used (it says +1.44 / -1.15 M). Read it as **"solid content adds no tier-0 ray work: 0 to
   -0.19 M per 1 %"**. The spread likely comes from WHERE the cover sits: a trunk 0-10 m from the camera (pre-210) hides more grazing
   ground than wood at 10-82 m (sparse, dense). A per-band rate would need more arms than 4.
4. **Into ms (est., calibrated, not measured):** trees are est. ~49 ms of forest_eye's 135 ms at 16 deg and ~36 of 99 at noon
   (`PERF_SLICE_BUDGETS.md` sec. 6: 210/213 ratio 1.57). Dividing by the dense arm's +6.57 / +6.31 M tier-0 steps gives
   **~5.7-7.5 ms per extra million tier-0 steps**, so **~1.3-1.8 ms per 1 % of screen covered by porous voxel content** at tier A.
   Solid voxel content is **~0 ms per % (+- shading; guess)**.
   **Task 248 (direct coverage, 16 deg): porous 1.7-2.3 ms per 1 % (fit +0.305 M x 5.7-7.5); the pairs span 1.3-2.4 ms.** The
   estimate MOVED: its low end holds only for the sparse + dense pair; the best fit is ~+30 % higher at the midpoint (1.55 -> 2.0 ms).
   **Budget with ~2 ms per 1 % of see-through screen.** Solid: 0 to -1.4 ms per 1 % (est.); keep budgeting it at **~0 ms** (do not bank
   a saving the sparse + dense pair does not show). The ms-per-M calibration is unchanged (dense +6.72 M now vs 6.57 in 246:
   49 / 6.72 = 7.3 ms per M, inside 5.7-7.5). Both numbers carry the error of the 49 ms estimate. The next forest timing
   milestone (238) should check them.
5. **Memory:** tree content shows up in pool OCCUPANCY, not in process peak. The pools are allocated at fixed caps, so process peak
   moves only by noise: 2,511-2,630 MB across all 8 launches, no order by tree arm.

| forest_eye, 16 deg / noon | GPU body pool peak (cap 254,400) | refused | CPU brick pool peak (valve 484,500) | process peak MB |
|---|---|---|---|---|
| off | 153,364 / 153,364 | 0 / 0 | 436,947 / 436,947 | 2,568 / 2,511 |
| pre-210 | **254,400 / 254,396 (full)** | **27 / 26** | 492,812 / 493,423 | 2,517 / 2,522 |
| sparse | 221,323 / 221,323 | 0 / 0 | 491,974 / 492,082 | 2,578 / 2,630 |
| dense (default) | 245,704 / 245,704 (96.6 %) | 0 / 0 | 493,686 / 493,432 | 2,552 / 2,533 |

Dense trees take **+92,340 GPU bodies (+45 MB of the fixed 124 MB pool)** at forest_eye, and push the CPU pool past the valve.
Out-of-view chunks are then evicted first (task 208). The shell pools (s1+) also store tree bricks, but the ledger line does not print
them (`PERF_SLICE_BUDGETS.md` sec. 2b unknown). Confound: shell fill after the 60 s wait differs by arm (forest_eye stored 1,858 off vs
3,888 dense at 16 deg). That moves the 82 m+ counts, not tier 0.

## 2. Classes of world fill and the render path that fits each

What the spec allows (ARCHITECTURE_v8.6):

- §6.1: raw 0.1 m cubes, flat-shaded, hard edges, no smoothing. This is the look contract; a billboard sprite of a fern breaks it, a
  mesh made of cubes does not.
- §6.2: one compute thread per pixel; "Unity's mesh pipeline renders only Rigidbody entities, composited by depth".
- §6.3: uniform bricks are leapt, dense bricks are micro-stepped.
- §6.5: sun shadows are rays through the same voxel data, so anything not in the voxels casts no voxel shadow.
- §5.4: structures and prefabs are batches of `SetVoxel`, ores are voxels, NPCs and objects are Rigidbodies, "independent of terrain
  storage entirely".

Today the voxel depth already reaches URP's `_CameraDepthTexture` (task 47, `VoxelUpscaleDepthWrite.shader`), so FSR 3 has depth. A mesh
drawn after it can depth-test against the voxels.

| class | examples | render path | est. ms at tier A (typical / forest worst) | est. MB | look trade | amendment? |
|---|---|---|---|---|---|---|
| **Solid props** | rocks, boulders, logs, stumps, crates, walls, buildings, bridges | **voxels in the mirror** (§5.4: SetVoxel batches / generator stamps), interiors sealed (201a: sealed bricks take no GPU body) | ~0 ray cost (sec. 1b.3); +0.1-0.5 shading (guess) | **per prop:** surface bricks x 512 B GPU + 512 B CPU. A 2 m boulder ~16 bricks = 8 KB each pool. A 10 x 10 x 8 m house ~800-1,600 bodies = 0.4-0.8 MB each pool. Shells store them again, coarser | none: they are the world | **no** |
| **Porous large foliage** | tree crowns, bushes, hedges, big ferns | **voxels**, but porosity is budgeted. Bushes and hedges get **solid cores** (task 243's rule: ragged 2-voxel skin over a filled interior). Crowns stay as approved | **1.3-1.8 ms per 1 % of screen** of see-through voxel (est.); the dense forest's 28 % is ~36-49 ms | trees today +45 MB GPU bodies at forest_eye (sec. 1b) | solid-cored bush: no light through it, no see-through gaps | no |
| **Small ground clutter** | grass tufts, flowers, ferns, pebbles, twigs | **NOT voxels.** A separate **near-camera instanced mesh layer**: a few kinds of small meshes built from 0.1 m cubes (§6.1 look kept), placed on the GPU from the terrain each frame (no stored instances), within ~30-40 m, thinned and faded with distance. Depth-tested against the voxel depth, drawn before FSR 3 with depth + motion. **No cast shadows**; lit by the sun visibility the voxel pass already computed for the ground pixel under them, plus sky fill | **~1-3 ms (guess)** until E2 measures it. As voxels instead: grass over the ~35 % of screen that is ground within 40 m = **est. +15-60 ms** (sec. 1b.2 rate x 0.3-1 for a shallow layer) | 2-10 MB (meshes + one instance buffer, e.g. 100 k x 32 B = 3.2 MB) | clutter casts no shadow, cannot be dug or collided, fades out past ~40 m. Under-canopy darkness comes from the ground under it, so it still reads as shaded | **YES, flag:** §6.2 limits mesh rendering to Rigidbody entities. A static clutter layer, its FSR 3 depth/motion input and its lighting rule need an amendment (8.18 draft) before any code |
| **Ground clutter, voxel-native variant** | 1-2 voxel grass fringe, pebbles | voxels, but **only inside surface bricks that are already dense**, so no new bricks or bodies | unknown: a 0.1-0.2 m porous skin at grazing angles; E1 counts it | ~0 bodies by construction (CPU: same bricks) | tiny, stubby clutter only; no tall grass | no |
| **Thin things** | fences, rails, ropes, vines, signposts, ladders | **voxels when static and sparse** (fences in towns, vines on a few walls). Each is porous at brick level but covers little screen. **Moving ropes** go with moving things | fences and vines are porous: count them against the same % budget as foliage (~1.5 ms per 1 % est.). A town fence line is << 1 % | small | 0.1 m minimum thickness (a rope is a 0.1 m bar) | no |
| **Moving things** | creatures, NPCs, the player, falling leaves, birds, dropped items | **meshes / Rigidbodies** (§5.4, §6.2: already the spec's path), composited by depth. Falling leaves and dust as GPU particles | characters: **1.5 ms reserve** (8.17 sec. 3). Particles 0.2-0.5 (guess) | 30-60 (meshes, animation, AI state; guess) | **they cast no voxel shadow** (§6.5 traces voxels only): a blob or contact shadow at most. A real shadow needs an entity term in the sun ray, a separate decision | §6.2 covers entity meshes; **an entity shadow term would need one** |
| **Light sources** | torches, lanterns, glowing plants, campfires | **emissive voxels + the torch flood** (tasks 175/242; §3.8's point lights stay the fallback). Glowing plants must NOT register as lights (KNOWN_ISSUES: emissive + mobile) | night torches inside the **1.0 ms** reserve; night worst today 52.7 ms (235) | flood 38 MB at today's cap (170 parked trim) | lights count against the flood's capacity; far torches are flood-only | no |

## 3. How shipped games handle this (only what applies to us)

- **Teardown:** everything, props included, is a voxel volume. Rays are traced inside each object's rasterized bounding box with a
  coarse-to-fine (mip) traversal, and hidden boxes are culled early. It is a ray-traced voxel game, but a world of solid, destructible
  props, with little or no fine foliage as voxels. It uses ray tracing for AO and soft shadows, not GI. **For us:** solid voxel props are
  the proven path; porous foliage in voxels is our own problem, not one they solved.
  [acko.net frame teardown](https://acko.net/blog/teardown-frame-teardown/),
  [teardown breakdown](https://juandiegomontoya.github.io/teardown_breakdown.html),
  [80.lv on the new engine](https://80.lv/articles/teardown-creator-is-working-on-a-new-voxel-engine).
- **Ghost of Tsushima (grass):** blades are generated on the GPU, not stored. They are grouped into clumps by noise, have two
  vertex-count LODs (15 / 7 verts, per secondary sources), and use one wind field. A reported ~83 k blades in ~2.5 ms on PS4 is
  unverified (secondary source). **For us:** generated-not-stored, near-only, LOD by distance. That is the shape of the clutter layer
  in sec. 2. [GDC Vault talk page](https://gdcvault.com/play/1027214/contactUs),
  [summary](https://hashnode.com/posts/what-we-can-learn-from-grass-in-ghost-of-tsushima-renders/69e130b2ffbb787634eb9ef4).
- **Horizon Zero Dawn (placement):** clutter and vegetation are placed on the GPU at run time from layered density maps (dithered,
  deterministic, locally stable). 100,000+ objects, ~250 µs average GPU load, ~250 objects placed per frame while moving.
  **For us:** clutter placement from our own terrain rules (material, slope, biome; simple-material-rules memory), recomputed per tile.
  No storage per instance, which is rule 11's "nothing stored that is never drawn".
  [GDC 2017 slides (PDF)](https://www.guerrilla-games.com/media/News/Files/GDC2017_VanMuijden_GPUBasedProceduralPlacementInHorizonZeroDawn.pdf).
- **Unreal 5 Nanite foliage:** Epic's docs say alpha-masked foliage adds a lot of overdraw, and masked-out pixels cost nearly as much
  as drawn ones. Community advice: cut real geometry to the leaf shape and keep it opaque. Grass overdraws little, trees much more.
  **For us:** the same law in another renderer. See-through content costs per covered pixel, whether raster overdraw or ray micro-steps.
  [Epic: Nanite foliage](https://dev.epicgames.com/documentation/en-us/unreal-engine/nanite-foliage),
  [forum: Nanite foliage for open world](https://forums.unrealengine.com/t/nanite-foliage-for-open-world/1886164).
- **Minecraft shader packs (Iris / OptiFine):** plants stay simple raster quads. Sway is per vertex (top vertices move, the base is
  anchored), and shadow work is trimmed per vertex and culled by distance. **For us:** clutter motion belongs in the vertex stage of the
  clutter layer, never in the voxels. [Shrimple](https://github.com/Null-MC/Shrimple),
  [Iris block compat](https://www.curseforge.com/minecraft/mc-mods/iris-block-compat/install).
- **Hybrid raymarch + raster:** the common pattern is a voxel pass and a raster pass that share view/projection matrices and one depth
  buffer. Run the voxel pass first and depth-test the triangles against it.
  [itch.io dev note](https://itch.io/post/11063648), [arXiv 2403.06505](https://arxiv.org/pdf/2403.06505).

## 4. Recommended per-class budget at tier A, and the admission rule

Tier A frame: 33.3 ms typical, forest at dawn/dusk 50 ms (D1). Memory: 3,000 MB, with ~250 MB reserved for the placement phase (D2).
Today's 8.17 slices have **no free ms** for clutter: the reserves are reflections 2.0, characters 1.5, torches 1.0, fluids 0.5, spikes
1.3. So the clutter layer's ms is a trade the owner signs (Q-246c).

| class | ms (typical / forest 16 deg) | memory | count limit (P4) | taken from |
|---|---|---|---|---|
| solid voxel props | 0.5 / 0.5 (guess) | **40 MB**: +40 k GPU bodies (20 MB) and the CPU bricks behind them; **0 refusals** at the 4 worst poses | bodies per mirror box (205 m cube) <= the pool's free bodies at forest_eye minus 10 % | D2 placement reserve |
| porous large foliage | **0 new**: today's trees ARE the budget (28 % leaf screen at forest_eye) | 0 new | leaf % of screen at forest_eye <= 28 % (dense today). Bushes count in it unless solid-cored | -- (any new porous cover replaces tree cover) |
| ground clutter (separate layer) | **2.0 / 1.0** (fewer, closer instances in heavy views) | 10 MB | instances in view <= 100 k (guess; E2 sets it) | **owner trade**: reflections reserve, or sun savings from F1/F2 once measured |
| thin things | 0.3 / 0.3 | 5 MB | inside the porous % limit | D2 reserve |
| moving things | 1.5 (8.17 reserve) + 0.3 particles | 60 MB | ~20 characters in view (guess) | characters reserve |
| light sources | 1.0 at night (8.17 reserve) | 0 new (flood 38 MB exists) | flood capacity | torches reserve |
| **total new** | ~4.6 typical | **~115 MB** of the 250 MB reserve (rest = safety) | | |

**Admission rule for any placement task** (extends `PERF_SLICE_BUDGETS.md` sec. 4; same declare-fit-proxy steps):

1. **Name the class** (sec. 2). Clutter that is small, numerous and near the ground goes on the clutter layer, never in the voxels.
2. **Voxel content declares its porosity:** the share of its bricks that are partly filled (porous) vs sealed / solid. Measure it with
   a report-only test on the prototype (task 243's histogram does this). **Porous share > 30 % = it counts against the foliage % budget.**
3. **Report from the launch it already does** (no timing): `-raystats 1` at forest_eye and one pose where the content is dense:
   tier-0 steps vs OFF, leaf % of screen, sky steps. GPU pool peak + refused, CPU pool peak (valve), shell stored counts. Limits:
   tier-0 steps +10 %, sky steps +25 %, refusals not up, process peak +50 MB (rule 11 default).
4. **Est. ms = 1.5 ms x (porous % of screen added)** at tier A (task 248's direct coverage says **2.0 ms**, sec. 1b.4), plus the layer's own measured share for clutter. Over the class budget =
   fix it in the task (solid core, thinner placement, smaller radius) or STOP_FOR_OWNER.
5. **Worst poses decide** (forest 16 deg, jungle noon, night torches, walk 16 deg).

## 5. Cheapest experiments to prove the riskiest class BEFORE the placement phase (queue-ready outlines)

Ground clutter is the riskiest: it covers the most screen and has no slice. E1 and E3 need no approval beyond this plan's capture-only
scope. E2 adds a pass and so needs the owner's approval in its task file (rule 12). E4 is content behind a flag.

**E1 -- `246a-raystats-material-split` (capture only, ~1 session, no look change). DONE as task 248: sec. 1a's material table, sec. 1b.2-4.** Add a primary-hit material class to RayStats
(leaf / wood / ground / other; 4 more counters per band, `VE_SHELL_DIAG` only, like 244). It also prints porous vs solid screen
coverage per band. Re-run 246's 4 arms at forest_eye 16 deg only (4 launches, counts). Done when wood coverage is printed and the
solid-content rate of sec. 1b.3 is re-derived from direct coverage, not decomposition. Tests: `RayStatsReportTests` twin of the new
bins.

**E2 -- `246b-clutter-layer-prototype` (NEEDS OWNER APPROVAL: new pass at render rows; amendment draft first).** (1) Write
`AMENDMENT_8_18_CLUTTER_LAYER.md` (DRAFT): a static near-camera instanced mesh layer, depth-tested against the voxel depth, before
FSR 3 with depth + motion, no cast shadows, lit from the voxel sun visibility under it. (2) Prototype behind `-clutter 1` (DEFAULT
OFF): ONE tuft mesh of ~6-10 cubes, instances generated per frame on the GPU from ground pixels within R m (grass material, slope
rule), density fade to 0 at R. Capture-only arm `-costarms clutter_off`. Report counts (instances, triangles, raster pixels) at
walk_near / forest_eye / a meadow pose. Timing waits for the next performance milestone (one arm at R = 20 / 40 m). Done when stills
show the tufts sitting on the voxel ground with correct occlusion both ways, and the counts are printed.

**E3 -- `246c-voxel-grass-fringe-counts` (content behind a flag, DEFAULT OFF, generator change: SUITE=all).** Generator flag
`-fringe 1`: 1-2 voxel grass stubs on grass-material surface voxels, written ONLY into surface bricks that are already dense (no new
bricks; an assertion counts zero new bodies). `-raystats 1` at forest_eye + a meadow, 16 deg + noon, fringe off/on. Done when tier-0
steps per 1 % of screen covered by fringe are printed and compared with the leaf rate (sec. 1b.2). If it is << the leaf rate, the
voxel-native variant can carry short clutter at ~0 MB.

**E4 -- `246d-solid-prop-memory-card` (content behind a flag, DEFAULT OFF).** Scatter solid voxel boulders and logs (two prototypes,
sealed interiors) at 3 densities with the generator's existing stamp path. Report bodies per prop, sealed share, GPU pool peak +
refusals, CPU valve, shell stored counts and `-raystats` tier-0 steps at forest_eye and walk 16 deg. Done when a "bodies per prop"
table exists. It sets the solid-prop count limit in sec. 4 from data instead of a guess, and checks that solid props add ~0 tier-0
steps.

Order: E1 (cheap, sharpens the model) -> E3 (cheapest clutter answer) -> E4 -> E2 (only if E3 cannot carry clutter, and after the
owner approves the amendment draft).

## 6. Questions for the owner

| # | question | recommendation |
|---|---|---|
| Q-246a | Small ground clutter (grass, flowers, pebbles) on a **separate near-camera layer** of little cube-built meshes that fade out by ~40 m and cast no shadows. This is an architecture amendment. | **Yes**, the only path that fits tier A. Voxel grass is est. +15-60 ms. |
| Q-246b | Bushes and hedges **solid inside** (ragged leaf skin over a filled core), so they cost like rocks, not like crowns. | **Yes.** Crowns stay as you approved them. |
| Q-246c | The clutter layer's ~2 ms has no slice. Take it from the **reflections reserve (2.0 ms)**, or wait for the sun savings (F1/F2) and take it from there? | **Wait for F1/F2**, and keep reflections. If F1/F2 save less than ~2 ms, come back to this question. |
| Q-246d | Moving creatures/NPCs cast **no real shadow** at first (blob/contact shadow only). | **Yes for now.** A real entity shadow is a later, separate decision. |
| Q-246e | Queue E1 (counter) and E3 (voxel grass fringe counts) now; E4 after; E2 only after Q-246a. | **Yes.** |

**Owner answers 2026-11-20 ("go with recommended for all questions"): a yes, b yes, c wait for the sun savings, d yes, e yes.** E1 done
(248), E3 queued (250), E4 queued (253). E2 is now approved in principle; it still follows sec. 5's order (only if E3 cannot carry clutter).

## 7. Method notes and caveats

- Counts are GPU-counted over 32 frames with the temporal counters frozen. They are not time. The ms per % figures are calibrated
  estimates (sec. 1b.4), not measurements.
- Each tree arm is a separate launch, because tree flags are world-gen flags. Streaming and shell fill differ a little per launch.
  walk_open bounds that noise (<= 0.8 %).
- The "pixels changed" proxy includes tree shadows, so it overstates the trees' own coverage. The leaf column counts leaf hits only
  (wood: task 248 counts it directly, sec. 1a).
- Files (text, tracked): `Screenshots/246/` holds the costsplit reports, `table_raw.txt`, `model.txt`, `coverage_proxy.txt`, `run.sh`,
  `parse.py`, `model.py`, `sheet.py` and `run_log.txt`. Images (git-ignored): `sheet246.jpg` and `forest_eye_off_vs_dense_16.jpg`.
