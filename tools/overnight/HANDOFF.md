# 46-engine-2-torch-shadow-rays: DONE. Torches no longer light through walls; emissive bleed ON. Rig 37/0, fast 634/0/14.

## What changed (branch `engine-phase`, commit `cd5266e`)
- `Raymarch.compute`: `SumPointLightsShadowed` + `TorchRayBlocked`. Each lit pixel whose hit is in tier 0 finds its N
  nearest in-range lights (insertion into a sorted list) and traces one occlusion ray to each. The ray reuses the sun
  shadow ray's tier-0 walk (air-mip leaps, uniform-brick leaps, dense DDA). It starts from the air cell in front of the
  face, at its centre (§6.5 origin rule, no bias). It counts as VISIBLE once it enters the light's own voxel (an integer
  int3 compare) or passes the light. Leaving the window or hitting the 256-step cap also counts as visible (the old
  behaviour). The other in-range lights are scaled by seen/traced (the SVO's form). The UniformFaceHue omni term uses the
  same visibility, so a hidden torch can't tint a face either. N = 0, tier >= 1 hits and self-lit (lava) faces use the
  old `SumPointLights`, unchanged. Lava aggregate lights are point lights too, so they are now shadowed as well.
- `LightingConfig.torchShadowRays = 4` (cap `MaxTorchShadowRays = 8`), `-torchshadows N` (PlaygroundCapture),
  `RaymarchFeature.TorchShadowsOverride`, part of the TAA light signature. `emissiveBleed` default 0 -> 1 (owner Q5 (a)).
- `Phase7CycleRig.cs`: new **Gate 1b** replaces the unasserted `04_torch_through_wall_FLAGGED` shot (see below).
- `-torchshadowshots` mode (`PlaygroundCapture.TorchShadows.cs`, added to Playground's capture/fly/hide-UI lists) +
  `run-torchshadow-shots.sh`. `PHASE_7_COMPLETION.md` §10.1 marked DONE.

## Evidence
- Gate 1b design: a 2-voxel stone partition across Gate 1's sealed chamber, 5 voxels from the torch. The camera is on the
  far side with its back to the wall. Point lights use N.L (`PointLightNdotL = true`), so the partition's own far face can
  never show a leak (the first rig run looked at that face: all three readings were 0.0000 and the leak precondition
  FAILED as designed). Pass rules: leak - base >= 0.010 (otherwise the gate proves nothing), and |after - base| <= **0.005**
  mean luma (~1.3/255).
- `run-phase7-rig.sh`: **PASS 37 FAIL 0** (`Logs/overnight/46/p7rig2.out`, run `Phase7Cycle/2026-10-10_051800`). Far-side
  luma: no torch 0.0000 / rays OFF 0.0610 / rays ON 0.0000. Torch side with rays on 0.1701. Gates 0-5 stay green, and
  Gate 1 (falloff) now runs with rays on. Last recorded count was 32/0 (2026-09-24). Gate 1b adds 4 assertions and the
  rest of the difference comes from gates added since then.
- Fast EditMode suite: 634/0/14 before the commit (`Logs/overnight/46/fast2.out`; also 634/0/14 after the shader change,
  `fast1.out`). Player builds: 0 `Shader error` (`Logs/phase7_build.log`, `Logs/torchshadow_build.log`).
- Screenshots I looked at: the rig's 04a-04d (black / far wall lit / black / torch side lit with shadow detail) and every
  row of the contact sheet below.

## Notes / open
- The BEFORE column is `TorchShadowsOverride=0 + EmissiveBleedOverride=0` set inside one process, which is exactly what
  `-torchshadows 0 -emissivebleed 0` select. Doing it in-process keeps the same scene and lights for both columns. The
  rig row (contact sheet row 3) is rays off/on with bleed left at its default.
- Shot sites sit in the Playground arena, which has a low surface (~y 40), so they clamp to y=20. The lava cavern roof is
  only 6 voxels, but it stays sealed.
- For the owner to look at: in the lava cavern AFTER, some wall recesses go fully black. Those faces are hidden from every
  torch and from the lava light, which sits 0.6 m above the lava's centroid. Before this task the lava lit them
  uniformly. The cause is the per-voxel origin (one colour per face). This is not a bug, but worth a look.
- CLAUDE.md still lists "torch-through-wall" among Phase 7's open §10 flags, and ROADMAP E9 says the same. Both can now
  be marked done (supervisor).
- No frame-time claim (plan step 7 measures the cost of 4 rays + bleed).

## Next step
Engine plan step 3 on `engine-phase`.

## For the doc
- Gate 1b far-side mean luma: no torch **0.0000**; torch with rays OFF (the old shader) **0.0610**; torch with rays ON
  (N=4) **0.0000**. Margin 0.005, met with no measurable difference (`Logs/overnight/46/p7rig2.out`).
- Phase 7 rig 32/0 (2026-09-24) -> **37/0**. Fast suite **634/0/14**.
- Mean frame RGB, before -> after: cave with 4 torches (19,16,12) -> (12,10,8), darker because of pillar and bump
  shadows. Lava cavern (120,71,38) -> (117,68,35). Night pool (42,43,76) -> (41,43,76), unchanged
  (`Screenshots/Engine02/torchshadows.txt`).
- `Screenshots/Engine02/contact_sheet.png`: left column BEFORE, right column AFTER. Row 1, torch side of the wall: same
  light, plus the bleed "+" glow on faces touching the torch. Row 2, far side: a faint glow on the end wall BEFORE, black
  AFTER. Row 3, the rig's closer torch: the far wall is clearly lit with rays off and black with rays on. Row 4, the cave:
  look at the pillars and wall bumps, which now cast shadows. Row 5, the lava cavern: a hot rim along the lava edge (bleed)
  and shadowed recesses on the right wall. Row 6, the night lava pool: practically identical.
