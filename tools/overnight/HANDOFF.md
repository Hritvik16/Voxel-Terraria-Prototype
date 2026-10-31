# 157-torch-flood-l1-oracle: DONE -- CPU light-flood oracle + plan tests 1-6 pass; memory fits the 48 MB cap; next is L2

## What changed (branch engine-phase, commit `da7c13e`; CPU only, no GPU / shader / default / look change)
- `Assets/CoreEngine/Rendering/LightFloodReference.cs`: light lives only in air voxels (resident chunk, material 0). It moves to the 26
  neighbours with NO corner cutting, so it reaches exactly the 6-connected air. Move costs are chamfer 4 / 6 / 7 units, read at 4.2913 units
  per 0.1 m. RGB levels combine by per-channel max, 10 bits per channel in one uint (the plan's 4 B per voxel); 12 m = 515 units.
  Storage is sparse per brick. AddTorch = a Burst single-source flood (Dial's buckets) max-merged into storage. RemoveTorch /
  OnVoxelsChanged / OnChunkEvicted / OnChunkAdmitted = darken + refill (the Minecraft two-queue pattern, generalised to chamfer weights and
  RGB). The oracle reads ChunkStore and writes only its own storage.
- Design choice: the exact weights 1 / 1.41 / 1.73 overestimate Euclid by up to 12.8%, which misses the plan's 8%. Centring the band with
  4 / 6 / 7 gives +-6.8%. 10 bits reach 23.8 m.
- `Assets/CoreEngine/Tests/LightFloodReferenceTests.cs` (12 fast, 2 Slow incl. the report). TASKS.md paragraph.
- Torch colour -> seeds: the brightest channel reaches 12 m and a dimmer channel starts log2(fraction) stops lower (8 stops over the reach).
  This is an L1 default; L2 maps level -> brightness.

## Evidence
- Fast suite PASS 960 FAIL 0 SKIP 15 (was 948 + 12 new; `Logs/overnight/157_tests.out`). Slow `Fuzz_Long` (2 x 300 ops) passed in a
  filtered run, as did the report run.
- (1) Sealed: the axis 0.1 m wall, staircase, edge-touching diagonal x==z, plane x+y+z=150 and a closed 0.1 m box each give EXACTLY 0 on the
  far side, whether built before or after the torch. A doorway opened then sealed returns to exactly 0. (2) Open air at 12 m: 6.49 M lit
  voxels, flood/Euclid 0.932..1.068. (3) L-bend: 10,211 voxels lit round the corner at 0.956..1.016 of the geodesic, darker than straight-line.
  (4) Fuzz: incremental == from-scratch byte for byte after every op (80 + 600 ops). (5) Removing all torches frees every slot and chunk table.
  (6) Evict / edit-while-evicted / re-admit == from-scratch after every step.
- Report `Screenshots/157/torchflood157.txt` (REPORTS=1, Burst, safety checks OFF; EditMode microbenchmark, not a frame-time claim).
  Memory per side in the plan's form (0.1 m mixed 2 KB, air brick 32 B, 16 KB per lit chunk): room 0.66 MB, tunnel 0.79, outdoor torch on
  a post 1.85, 32 torches 5.96, **200 torches over 80 x 80 m open ground 43.8 MB: under 48**. With 0.2 m mixed cells + per-voxel escape:
  14.4 MB.
- Split cells at 0.2 m: 0.00% in the room, tunnel, L-bend, outdoor and both grids (0 of 563k at 200 torches; axis walls never split a cell).
  In the room split by a diagonal 0.1 m wall it is 12.3% (450 cells), and every one of them would leak if stored as a single 0.2 m value.
- Worker ms per flood: room 7.6 ms (mostly clearing the fixed-size 12 m box), tunnel 12.2 ms, outdoor 215 ms (3.9 M voxels reached). The
  managed merge is 10-310 ms. The managed incremental remove is 0.3 / 0.6 / 8.7 s (room / tunnel / outdoor; unoptimised oracle code).
- Looked at all 5 slices: diagonal wall far side pure black; L-bend fades round the bend to black at the reach; tunnel light spills out the
  mouth as a dim glow; sealed room all lit (5.1 m < 12 m); outdoor pool's outer edge is OCTAGONAL (the +-6.8% chamfer shape).

## Decisions / flags (no STOP: memory fits, no frozen API or layout touched)
- **L2 storage (the plan left this to L1): 0.2 m mixed cells + a per-voxel escape for split cells.** It is 3x smaller (14.4 vs 43.8 MB at
  200 torches). Escapes are needed only on thin diagonal walls. The 0.1 m form also fits, but only with 9% headroom, and only on flat ground.
- **FLAG for L3 (ask the owner before L3), CPU copy:** the plan said "the CPU copy is the same size again". An EXACT per-voxel CPU copy (what
  two-queue removal needs) would be 528 MB for the 200-torch grid. Options: the CPU keeps the GPU's compressed form and removals/edits
  re-flood the affected torches; or per-voxel only in mixed bricks, with air bricks rebuilt from their 1-voxel shell.
- **FLAG for L3, flood cost:** outdoor 215 ms vs the plan's est. 5-15 ms. Only 9% of the reached voxels are in mixed bricks, so the plan's
  "pure-air bricks flood as a unit" is required, est. ~10x fewer voxel steps, and a sparse scratch instead of the 21 M-cell box.
  Q5's "1-3 frames" holds for rooms and tunnels today but not for an outdoor placement.
- The octagonal outer edge is a look item for L2 (the falloff is dim there; check it in the sheet).

## Next
L2 (`TORCH_LIGHT_PLAN` §3): GPU light pool (0.2 m + escape) + shader sampling behind `-torchflood 1`, OFF, filled from this oracle; 143's
sheet rows plus a "flood 12 m" column and a pillar shot; one warm cost-split arm vs today (151's 32 torches). No follow-up task queued.

## For the doc
- Exactness: sealed walls (axis, staircase, x==z diagonal, x+y+z plane, closed box) 0 light behind them; open-air distance 0.932..1.068 of
  Euclid; L-bend 0.956..1.016 of the walking path; incremental == from-scratch after 680 fuzz ops (`LightFloodReferenceTests`).
- Memory per side, 200 torches over 80 x 80 m: 43.8 MB at 0.1 m (cap 48) -> 14.4 MB at 0.2 m + escape; 1 outdoor torch 1.85 MB, room 0.66 MB
  (`Screenshots/157/torchflood157.txt`).
- Split 0.2 m cells: 0% in natural/axis geometry, 12.3% only beside a diagonal 0.1 m wall.
- Worker flood (Burst): room 7.6 ms, tunnel 12.2 ms, outdoor 215 ms (plan est. 1-4 / 5-15 ms) -> L3 needs air-brick-as-unit floods.
- `Screenshots/157/slice157_diagonalwall.png`: top-down slice of a 6 m room cut by a 0.1 m diagonal wall (x == z), torch upper right. Look at the
  lower-left triangle: it is pure black right up to the stair-step wall, so no light leaks through the corner-touching voxels.
- `Screenshots/157/slice157_lbend.png`: L-shaped 1 m corridor, torch at the top-left end. Look at the right-hand leg: the light turns the corner
  and fades to black with walking distance; the dark contour lines (1 m of path each) bend round the corner.
- `Screenshots/157/slice157_tunnel138.png`: task 138's tunnel from above, mouth on the right, torch at the lower wall 10.2 m in (bright spot,
  lower left). Look at the right edge: a dim glow spills out of the mouth into the open air.
