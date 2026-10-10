# Review (Claude, 2026-10-09) -- read this first; it supersedes section 1 where they differ

Nothing was compiled or run (no Unity during task 37e). Reviewed against CLAUDE.md and the SVO
reference (`RayTracer.compute` SvoMaterialAt / SvoEmitColor / bleed, lines ~315-335, ~1595-1615).

Sound as written: `VoxelMaterialAt` copies `AoSolidAt` (bitwise brick/voxel addressing, window bounds
test, uniform-brick handle); the bleed is a faithful port of the SVO's four-edge-neighbour blend;
`_EmissiveBleed` is set every frame next to `_LookGrade`; the LightingConfig field + Validate clamp
match their siblings; the TAA light signature resets history on a toggle. `RaymarchStripped.compute`
has no LOOK 1 shading, so it needs no edit. The palette tables ("change a colour in BOTH places")
were not touched, so PaletteSyncTests is unaffected.

Fixed:
1. **The new form applied to EVERY emitter, not only lava.** Torches (14-17) went from
   `base*light + tint*1.0` to `base*light + albedo*2`, and sludge (20) lost its self-lit cheat.
   The brief was lava only, and bleed 0 must leave everything but lava unchanged. Now the SVO form
   is `lm == 11u` only; every other material keeps the old combine byte for byte.
2. **Bleed restructured** as an additive term after the combine (still SVO's `emission <= 0` and
   tier-0 gate, `_EmissiveBleed != 0`); int normal tests (`normal.x != 0`) instead of `abs(int) > 0.5`.
3. **`-emissivebleed`** now sets only `RaymarchFeature.EmissiveBleedOverride` (clamped to 0/1), like
   `-lookgrade` sets only its override; Gemini also wrote `LightingConfig.Active`, which a config
   reload would discard anyway. `ArgInt` still takes the last match.

Watch in the shots: lava faces still receive `faceLight` (including point lights from the lava
aggregate lights, as in the SVO). If the CAVERN lava looks pale again while the night pool is deep
orange, that is why; the fix would be to drop `pointTerm` from lava faces. Far-band lava is not
shaded by this block and keeps its old colour.

---

# GEMINI HANDOFF: Lava Appearance & Emissive Bleed Parity

Branch: `gemini/lava-look`  
Task: Make our lava look like the SVO reference (`glowColor`, `SvoEmitColor`, emissive bleed) and wire emissive bleed through configuration, render feature, and playground capture.

---

## 1. Summary of Changes (file:line)

### `Assets/CoreEngine/Rendering/LightingConfig.cs`
- **Line 315**: Added `public int emissiveBleed = 0;`
  - *Why*: Adds the emissive bleed toggle to the persistent lighting configuration (default 0 / off).
- **Line 437**: Added validation:
  ```csharp
  if (emissiveBleed < 0 || emissiveBleed > 1)
  { notes.Append($"{nameof(emissiveBleed)} {emissiveBleed}->0; "); emissiveBleed = 0; }
  ```
  - *Why*: Ensures runtime JSON reloads or manual editing clamp `emissiveBleed` safely to `[0, 1]` with fallback 0.

### `Assets/CoreEngine/Rendering/RaymarchFeature.cs`
- **Line 196**: Added `public static int EmissiveBleedOverride = -1;`
  - *Why*: Provides static CLI/harness override mechanism without mutating persistent JSON configuration.
- **Line 404**: Added `public int emissiveBleed;` to `PassData`
  - *Why*: Carries emissive bleed setting through URP RenderGraph pass data.
- **Line 576**: Updated TAA `lightSig`:
  ```csharp
  int bleedSig = EmissiveBleedOverride >= 0 ? EmissiveBleedOverride : cfgT.emissiveBleed;
  int lightSig = ShadowQualityLevel * 31 + (LookOverride >= 0 ? LookOverride : cfgT.look) * 7
               + (TonemapOverride >= 0 ? TonemapOverride : cfgT.tonemap) + bleedSig * 13;
  ```
  - *Why*: Resets TAA history when emissive bleed is toggled to prevent temporal ghosting.
- **Line 702**: Recorded active setting in `RecordRenderGraph`:
  ```csharp
  passData.emissiveBleed = EmissiveBleedOverride >= 0 ? EmissiveBleedOverride : cfg.emissiveBleed;
  ```
  - *Why*: Forwards active config or override value to pass data for shader dispatch.
- **Line 991**: Bound compute uniform:
  ```csharp
  cmd.SetComputeIntParam(data.compute, "_EmissiveBleed", data.emissiveBleed);
  ```
  - *Why*: Binds `_EmissiveBleed` on the primary raymarch compute shader before dispatch.

### `Assets/Game/PlaygroundCapture.cs`
- **Lines 1827–1834**: Added CLI argument parsing:
  ```csharp
  // -emissivebleed 0|1: faces next to lava/torches pick up their glow.
  int emissiveBleed = ArgInt("-emissivebleed", -1);
  if (emissiveBleed >= 0)
  {
      RaymarchFeature.EmissiveBleedOverride = emissiveBleed;
      LightingConfig.Active.emissiveBleed = emissiveBleed;
  }
  ```
  - *Why*: Supports runtime `-emissivebleed 0|1` flag on any player launch, following the `-lookgrade` pattern.
- **Line 1912**: Updated startup log banner to include active `emissive bleed` state.
  - *Why*: Ensures runs clearly report in their logs whether bleed was enabled or disabled.

### `Assets/CoreEngine/Rendering/Raymarch.compute`
- **Line 872**: Added `int _EmissiveBleed;` uniform declaration.
  - *Why*: Declares the GPU uniform controlling emissive bleed injection.
- **Lines 1102–1127**: Added `VoxelMaterialAt` and `LookEmitColor` helpers:
  ```hlsl
  /// Read material id at voxel. Tier-0 clipmap only.
  uint VoxelMaterialAt(int3 voxel, uint3 windowDimsBricks)
  {
      int3 rel = VoxelToBrick(voxel) - _WindowOriginBricksPacked.xyz;
      if (rel.x < 0 || rel.y < 0 || rel.z < 0 ||
          rel.x >= (int)windowDimsBricks.x ||
          rel.y >= (int)windowDimsBricks.y ||
          rel.z >= (int)windowDimsBricks.z) return 0u;

      uint handle = ReadClipmap(VoxelToBrick(voxel), windowDimsBricks);
      if ((handle & 0x80000000u) == 0u) return handle & 0xFFu;   // uniform brick
      uint poolIdx = handle & 0x3FFFFFFFu;
      uint vIdx = (uint)LocalVoxelIndex(LocalVoxelIndex3D(voxel));
      uint packed = BrickDataBuffer[(poolIdx * 128) + (vIdx >> 2)];
      return (packed >> ((vIdx & 3u) * 8u)) & 0xFFu;
  }

  /// SVO/Brickmap emissive color: an emitting material glows in its own look albedo.
  float3 LookEmitColor(int3 voxel, uint3 windowDimsBricks)
  {
      uint m = VoxelMaterialAt(voxel, windowDimsBricks);
      return MaterialEmissive(m) > 0.0 ? MaterialAlbedoLook(m) : float3(0.0, 0.0, 0.0);
  }
  ```
  - *Why*: Provides fast clipmap material lookup for neighbor checks using integer bitwise shifts and masks (`>>`, `&`) per CLAUDE.md guidelines.
- **Line 3144**: Precomputed `float3 hitP = rayStart + rayDir * currentDist;` once.
  - *Why*: Clean reuse across FaceAO, point lights, and emissive bleed without redundant multiplications.
- **Lines 3163–3197**: Replaced LOOK 1 emissive combining logic:
  ```hlsl
  // SVO emissive form: baseColor * light + albedo * emission * 2.
  // For lava, stays deep orange (not pale gold) with slight per-voxel hot/cool variation.
  // For non-emitting surfaces next to lava/torches, emissive bleed injects their glow.
  float3 glowColor = float3(0.0, 0.0, 0.0);
  float emission = MaterialEmissive(lm);
  if (emission > 0.0)
  {
      float3 albedo = MaterialAlbedoLook(lm);
      if (lm == 11u)
      {
          // Slight per-voxel hot/cool variation with existing VoxelHash:
          // Hot spots shift slightly toward amber; cool spots toward deep red.
          float hot = VoxelHash(voxel) - 0.5;
          albedo += float3(hot * 0.10, hot * 0.05, -hot * 0.02);
      }
      glowColor = albedo * (emission * 2.0);
  }
  else if (_EmissiveBleed != 0 && tierAtHit == 0)
  {
      int3 fa = voxel + normal;
      int3 e1, e2;
      float3 lh = saturate(hitP - float3(voxel));
      float2 uv;
      if (abs(normal.x) > 0.5)      { e1 = int3(0, 1, 0); e2 = int3(0, 0, 1); uv = lh.yz; }
      else if (abs(normal.y) > 0.5) { e1 = int3(1, 0, 0); e2 = int3(0, 0, 1); uv = lh.xz; }
      else                          { e1 = int3(1, 0, 0); e2 = int3(0, 1, 0); uv = lh.xy; }
      float3 bleed = max(max(LookEmitColor(fa - e1, _WindowDimsBricks) * (1.0 - uv.x),
                             LookEmitColor(fa + e1, _WindowDimsBricks) * uv.x),
                         max(LookEmitColor(fa - e2, _WindowDimsBricks) * (1.0 - uv.y),
                             LookEmitColor(fa + e2, _WindowDimsBricks) * uv.y));
      glowColor = bleed * 2.0;
  }

  float3 lit = baseColor * faceLight + glowColor;
  ```
  - *Why*:
    1. Replaced the old `selfLit ? saturate(baseColor * 1.35 + 0.06)` and `MaterialEmissiveTint * MaterialEmissive` add which clamped red at 1.0 while allowing green to rise to ~0.76 (turning lava into washed-out pale yellow-gold).
    2. Uses the SVO formula `(baseColor * light + albedo * emission * 2)` so lava maintains a strong red-to-green ratio (~2.7:1), keeping it deep saturated orange.
    3. Adds slight per-voxel hot/cool temperature variation using `VoxelHash(voxel)` so lava pools exhibit subtle hot embers and cooler crusts rather than flat uniformity.
    4. Replicates SVO's 4-neighbor tangent plane emissive bleed across adjacent faces (`fa ± e1`, `fa ± e2`) bilinearly blended with hit UVs, gated behind `_EmissiveBleed != 0 && tierAtHit == 0`.

---

## 2. Screenshot Verification Checklist

When the supervisor builds and captures screenshots, check:

1. **Lava Colour (`-emissivebleed 0` & `1`)**:
   - Compare lava surface colour against SVO reference (`results/final_look/SVO_shots_...` / `-lavalight` / chaos burst shots).
   - Confirm lava appears deep molten orange, not pale yellow/gold.
   - Confirm subtle per-voxel hot/cool tonal variation is visible across lava pools without noisy moiré.
2. **Emissive Bleed Off (`-emissivebleed 0`, default)**:
   - Terrain surfaces adjacent to lava pools and torches have standard shading without halo bleed.
   - Renders byte-for-byte consistent with baseline except for the deeper lava body.
3. **Emissive Bleed On (`-emissivebleed 1`)**:
   - Walls bordering lava pools catch the warm orange lava bleed, fading smoothly up the wall away from the fluid.
   - Floors beneath or adjacent to torches (warm, blue, green, purple) catch the emitter's halo glow, fading outward across the voxel face.
4. **Torches & Other Emissives**:
   - Verify torches (materials 14–17) and sludge (material 20) glow cleanly in their look albedos without clipping or colour shifts.
