# TORCH_LIGHT_PLAN -- torches that light 10-15 m without today's cost

Task 155 (owner, 2026-10-30, doc Q99 (a)): keep the shipped 2.9 m torch now; plan a cheaper torch light. PLAN ONLY:
no code, no default change. The cost numbers below come from tasks 143/151. Nothing new was measured. Every "est." is an
estimate that its step must measure.

## 1. Why today's torch is expensive (151, `Screenshots/151/torchcost151.txt`)

`Raymarch.compute` `SumPointLightsShadowed`: every tier-0 pixel loops over all `_ActiveLightCount` (<= `MAX_ACTIVE_LIGHTS`
= 32) lights, then traces a 0.1 m occlusion ray (`TorchRayBlocked`, up to 256 steps) to its `_TorchShadowRays` = 4 nearest
lights in range. The cost follows the RANGE, not k or the intensity: a longer range puts more pixels within reach of a torch,
and each ray is longer.

| 32 torches, 540 rows, night | p50 vs no torch light |
|---|---|
| 22-31 torches at 8.5 m (play before 151) | +5.7 / +8.3 ms |
| 2.9 m "room" arm (shipped; range 8 m) | the same as 8.5 m within noise (-2.1 to +2.0 ms) |
| range 10 m, vs 8.5 m | +3.7 ms |
| range 14 m (the 10-15 m look needs >= 14), vs 8.5 m | +2.6 to +23.6 ms (noisy) |
| range 24 m, vs 8.5 m | +15.3 to +23.6 ms |

Two more limits (ROADMAP E9): the 32-light global cap (§3.8: "a handful of placed torches, not a lit city"), and no light
going round corners. §3.8 and §6.5 already name the scaling forms as **v1.5**: per-brick light clustering, and "the sparse
BFS light pool ... returns in v1.5 for around-corner light, behind the same shading interface so terrain traversal is
untouched". Candidates A and B below are those two forms. Candidate C is a cheaper version of A.

## 2. Candidates

Scale: 540 render rows = 518k pixels. The pool sizes assume a 12 m reach. "Mixed" brick = an 8^3 brick holding both air
and solid (the faces you see). "Air" brick = all air.

### A. Light flood (Terraria-style), exact at 0.1 m, sparse per brick, sampled once per pixel (RECOMMENDED)
- **What:** each torch seeds a flood through AIR voxels. Air is passable 6-connected. Diagonal moves are allowed only when
  both face neighbours they cut past are air ("no corner cutting"). Each move costs its chamfer weight (1 / 1.41 / 1.73
  voxels), so the light stays round instead of Terraria's diamond. Light per channel (RGB) decays multiplicatively per
  0.1 m of path. Floods combine by per-channel MAX, which is the monotone rule that makes the Minecraft-style two-queue
  removal exact. Mixed bricks store light per voxel. An air brick has nothing inside it to occlude, so it stores 2^3 cells
  at 0.4 m. A pixel reads the light of `airCell` (the air voxel in front of the hit face, already computed for the
  torch rays): a chunk-table read, a brick-slot read and a value read. No loop over lights, no rays.
- **Look:** soft. Light flows round corners and through doorways, fading with WALKING distance. A bent tunnel is lit round
  the bend, dimmer. Sealed walls leak nothing, by construction, at any thickness down to one 0.1 m voxel, including
  diagonal and corner-touching walls (corner-cutting is refused). This exactly fixes Phase 7 §10 flag 1
  (torch-through-wall) without rays. **No crisp torch shadows.** A pillar leaves only a faint dim patch, because the path
  round it is slightly longer. Faces still shade by direction: a face turned away from the torch reads the air voxel on
  ITS side, and the light reached that voxel by a longer path. Colour: TorchBlue/Green/Purple (materials 15-17) mix by
  per-channel max. Values change per 0.1 m voxel, the same grain as the blocks (`_UniformFaceHue` stays).
- **Memory (GPU; the CPU copy is the same size again):** 4 B per voxel (8-bit log RGB + 8 flag bits) = 2 KB per mixed
  brick, 32 B per air brick, and a 16 KB slot table per lit chunk. Est.: room torch 0.6-1.2 MB, isolated outdoor torch
  ~3-4 MB, a 32-torch base 8-12 MB, a 200-torch base over 80 x 80 m ~40 MB. The worst case, 200 isolated outdoor torches,
  is ~780 MB, so the pool is CAPPED (proposed 48 MB GPU + 48 MB CPU). Past the cap, the furthest torches drop to today's
  analytic glow, the way §3.8's furthest-first rule works. Storing mixed bricks at 0.2 m would make this 8x smaller,
  but a 0.2 m cell can straddle a corner-touching diagonal 0.1 m wall, and that leaks. Step L1 counts how often such
  "split" cells happen and decides between 0.1 m, and 0.2 m with a per-voxel escape for split cells. The tg2 world is
  already over the 3 GB cap (KNOWN_ISSUES), so these MB must come from somewhere: L1 reports them against the measured
  pool peaks.
- **Frame cost:** est. +0.3 to +1.0 ms at 540 rows, flat in both torch count and reach (3 dependent reads per pixel). It
  REMOVES the 32-light loop and the 4 rays from tier-0 hits. Net vs today: est. -5 to -8 ms at 32 torches, and the same
  sample cost at 200.
- **Update cost:** a Burst flood job runs on a worker into scratch that the job owns. The main thread commits slots and
  uploads dirty bricks under the existing upload budget (Gate C). This is the chunk-gen pattern, so the single-writer
  rule holds. Pure-air bricks flood as a unit, so the cost goes with mixed bricks. Est.: room torch 1-4 ms, outdoor torch
  5-15 ms of worker time. Removal (breaking a torch, or a block cutting a light path) costs about 2x that. Edits are
  coalesced per frame and are local: one block placed in lit air changes a small region, while sealing a doorway
  re-darkens one room. Light reaches the screen 1-3 frames after the edit. A 200-torch base that streams in fills over
  ~1 s of worker time; workers are the streaming bottleneck, so these jobs queue behind chunk gen.
- **Streaming and the window edge:** when a chunk is admitted, the light on its neighbours' shared faces continues into it.
  When a chunk is evicted, its light slots are freed and get rebuilt from the torch registry on re-admission. Light is
  derived data and never saved, so the D.1 delta stays untouched. The only seam is the 0.1 m window edge (59-116 m by
  preset). There, shell (tier >= 1) hits keep today's cheap unshadowed `SumPointLights` for the 32 nearest registered
  torches (Q6), so a torch's glow does not pop off at the hand-over.
- **Lava and other emissives:** mobile emissives must not seed per voxel (KNOWN_ISSUES "EMISSIVE + MOBILE MATERIALS"). For
  L1-L4, lava keeps its tile aggregates (`lavaLightMaxSlots` 8, analytic). In L5 each aggregate tile becomes a flood seed,
  re-seeded only when its count changes past a threshold or at a fixed rate. That bounds the work by tiles, not voxels.
  `_EmissiveBleed` stays as it is.
- **Frozen APIs and layouts:** `IWorldQuery.GetVoxel`, `IEditService.SetVoxel`, D.1 and the §4.4 table are unchanged.
  Torches still register through `LightSourceBuffer.Register`. **FLAG (design change under §0):** a new derived GPU+CPU
  structure (the light pool and chunk slot table) and the §3.8/§6.5 text ("no propagation, no BFS") need an amendment.
  The spec itself names this as the v1.5 path, so it is the planned swap, not a new direction. `MAX_ACTIVE_LIGHTS` stops
  bounding torch count for tier-0 hits; it still bounds the shell-hit analytic term. The light pool is NOT `ChunkStore`
  and not terrain, so "CPU is the sole terrain writer" still holds.
- **Bonus:** a CPU `LightAt(voxel)` query comes for free, so gameplay can later spawn things in darkness. Rays cannot
  give that.

### B. Per-brick light lists + 1 temporally reused shadow ray per pixel
- **What:** today's model kept. A coarse 3.2 m cluster grid lists the <= 8 lights that reach each cluster. Each frame, each
  pixel traces ONE torch ray to one of its lights in rotation, and the result is reused across frames (task 56's
  history key).
- **Look:** today's, at a longer reach: Euclidean falloff, crisp torch shadows, no light round corners, no leaks. Moving
  shadows lag about 4 frames (ghosting while walking). Where more than 8 lights reach one cluster, the extras drop (a
  visible step in dense bases).
- **Memory:** ~1-4 MB. **Frame cost:** est. +2 to +8 ms at 12-15 m with 32 torches (a quarter of today's rays, but each
  still ~120-150 steps), and roughly the same at 200 (capped at 8 per cluster). It is still over 151's +1 ms budget
  because the cost still goes with reach x lit pixels.
- **Updates:** clusters are rebuilt on the CPU when a torch is placed (trivial). Edits cost nothing (the rays see the
  mirror). No pops. **APIs:** a small new buffer, `MAX_ACTIVE_LIGHTS` raised (a §0.2 constant: FLAG). **Verdict:**
  cheapest to build, keeps today's look, but misses the cost goal at 10-15 m and E9's "many lights".

### C. Coarse 0.8 m brick flood (from occupancy / the shells)
- **What:** A's flood, one value per brick, passability taken from the existing occupancy data. Memory ~4 B per lit
  brick (a few hundred KB for 200 torches). Sampled trilinearly, est. ~0.2 ms. Floods cost < 0.5 ms per torch.
- **Look:** blobby 0.8 m light. A 0.1 m wall inside a brick either LEAKS (mixed bricks pass light) or darkens the floor
  next to it (mixed bricks block). That is the same reason SOFT_SKY_PLAN rejected its candidate C, and it breaks the
  generality rule for player builds. **Verdict:** not for the near field. It is the right shape later for FAR light
  (shell hits at 0.4 / 1.6 m, "lights visible across the island", L7), as the coarse level of A's own hierarchy.

## 3. Recommendation: A (C later as its far level)

A is the only candidate that meets all three goals: (1) 10-15 m reach at a flat per-pixel cost, (2) hundreds of torches
in a bounded memory pool, (3) exact for any geometry (generated, edited, 0.1 m player walls, diagonal walls), with no
terrain constant. The trade the owner has to accept is Q1: soft light round corners instead of crisp torch shadows.
SOFT_SKY_PLAN's sky ray (its A) is separate. The two only meet in the final combine (C.9), so neither blocks the other.

### Step list (one change per task; each with a look check and ONE measurement)
| step | change | look check | measurement |
|---|---|---|---|
| L1 | CPU oracle `LightFloodReference` + incremental add/remove + tests 1-6. No GPU, no product change. | oracle slice PNGs: top-down light of 138's tunnel / sealed room / an L-bend / a diagonal 0.1 m wall | metrics report: lit / mixed / air bricks and MB per torch, split-cell share at 0.2 m (decides storage), worker ms per flood (room, outdoor) |
| L2 | light pool + chunk slot table + upload; shader samples it behind `-torchflood 1`, OFF by default | 143's sheet rows (tunnel / room / ground) with an added "flood 12 m" column, plus one pillar shot | one warm cost-split torch scene (151's 32 torches): flood vs today |
| L3 | edits and streaming incremental (worker job, per-frame budget), tests 4-6 on the GPU path | dig through / seal a doorway sequence; walk to the window edge with a lit cave in view | upload bytes / frame p99 during edits (report) |
| L4 | 200-torch scene, pool cap and the furthest-first fallback | a 200-torch base sheet (night) | one cost-split arm, 200 torches |
| L5 | lava tiles as flood seeds; coloured torches checked | lava cavern + coloured torch stills | `run-acceptance-fluid.sh` frame time (lava live) |
| L6 | owner: `-torchflood 1` becomes the play default; tier-0 torch rays retired; amendment 8.17 adopted | final sheet | the next perf milestone (no separate arm) |
| later L7 | C as far light on shell hits | night island view | -- |

### Tests and oracle changes
New (L1, CPU; L3 repeats them on GPU readback): (1) sealed walls give exactly 0 light on the far side: axis-aligned 0.1 m,
staircase, corner-touching diagonal, a closed box around a torch; (2) open-air falloff within 8% of Euclidean (chamfer
check); (3) an L-bend is lit with the path-length falloff; (4) fuzz: random edit / torch sequences, incremental ==
from-scratch, byte for byte (§0.3 sibling pattern); (5) removing every torch brings every slot back to 0 and frees it;
(6) admit / evict / re-admit gives the same light as from-scratch inside the window. GPU sample == oracle at chosen
pixels (L2). **Rebased (FLAG):** Phase7CycleRig Gate 1 falloff poses and `RaymarchLightingTests` point-light expectations
assert C.11's `1/(1+kd^2)`. Under `-torchflood 1` they need flood expectations; the OFF path keeps today's assertions
unchanged. `04_torch_through_wall` becomes a real pass check (0 light behind the wall). `LightingTests`' registry and the
lava-refusal tests stay.

## 4. Questions for the owner (recommended answer first)
1. **May torch light go round corners, at the price of no crisp torch shadows?** (a) Recommended: yes, Terraria-style:
   light flows through doorways and round tunnel bends, fading with walking distance; pillars do not cast sharp torch
   shadows (the sun keeps its sharp shadows). (b) No: straight-line light with crisp shadows (candidate B), est. +2 to
   +8 ms at 12-15 m and still capped at 32 lights.
2. **Reach.** (a) Recommended: 12 m, the middle of 143's 10-15 m column (memory grows with reach squared). (b) 15 m.
   (c) 10 m.
3. **How many torches must one base hold, all lit?** (a) Recommended: 200 within the 0.1 m window, with the pool capped at
   48 MB GPU + 48 MB CPU; past the cap, the furthest torches fall back to today's 2.9 m glow. (b) 32 (today's cap, ~12 MB).
   (c) 500+ (~100+ MB each side, which needs memory freed elsewhere first: the tg2 world is already over 3 GB).
4. **Coloured light?** (a) Recommended: yes, RGB, where two colours overlap the brighter of each channel wins (a red and a
   blue torch together read magenta-ish). The blue/green/purple torch materials already exist. (b) One warm colour only
   (saves ~1/3 of the pool).
5. **Light fill-in delay.** (a) Recommended: a placed or broken torch (or a sealed doorway) updates within 1-3 frames
   (~0.1 s), and a large base that streams in lights up over ~1 s. (b) Always the same frame: the flood runs on the main
   thread, which hitches 5-15 ms on a big outdoor placement.
6. **Torch light beyond the 0.1 m window (59-116 m away, the shells)?** (a) Recommended: keep today's cheap unshadowed
   glow there for the 32 nearest torches (it can leak through walls, but at that distance nobody sees it), and add real
   far light later (L7). (b) None until L7: a torch's glow appears when you come within ~60-115 m.

Pictures: `Screenshots/143/torchreach143.png` (rows tunnel / sealed room / open ground; columns today ~1-2 m, small room,
half a tunnel ~6 m, 10-15 m). Its right-hand column is the reach Q2 asks about, and its open-ground row shows the
outdoor spread that sets Q3's memory.
