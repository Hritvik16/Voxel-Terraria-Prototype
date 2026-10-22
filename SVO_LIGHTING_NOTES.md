> **Review (Claude supervisor, 2026-10-09).** Part 1 (lava ignores point light, `Raymarch.compute`) is
> correct and minimal; LOOK 2 really does take the self-lit path for lava. These notes: the SVO line
> numbers are WRONG throughout (e.g. :1279-1294 is texture blending; the real places are sky
> occlusion :1467, ambient :1524, direct sun :1552, capped per-torch shadow rays ~:1557-1590, bleed
> :1615). The main finding checks out: the SVO traces shadow rays for the nearest torches
> (`TORCH_RAYS`, the rest scaled by the traced share), and ours has NO point-light occlusion
> (`Raymarch.compute:1856`: "a torch behind a wall" lights through it) -- Phase 7 §10's open
> "torch-through-wall" flag. Second: the SVO has bleed ON; ours is off by default. "Same" rows for
> sky occlusion / sun shadows / AO were not verified. Use as a pointer list, not as a source.

# SVO_LIGHTING_NOTES.md

## 1. SVO Lighting Effects
* **Sun Shading / Face Shade:** Calculates base surface lighting via `saturate(dot(normal, _SunDir.xyz) * 0.8 + 0.2)` (`SVO_REF/SVO_RayTracer.compute:1249`). Cost: 0 rays.
* **Ambient:** Colors shadowed areas using sky-dome bounce: `_HorizonColor.rgb * _AmbientStrength * skyVis` (`SVO_REF/SVO_RayTracer.compute:1253`). Cost: 0 extra rays (reuses `skyVis`).
* **Direct Sun:** Combines sun exposure, face shade, and sun color: `(sun_light * 1.5 * sunExposure) * faceShade * SUN_COLOR` (`SVO_REF/SVO_RayTracer.compute:1269`). Cost: 0 extra rays (reuses `sunExposure` / shadow ray).
* **Point Lights (Dynamic Light):** Computes attenuation and casts shadow rays to check occlusion for up to 4 nearby point lights (`SVO_REF/SVO_RayTracer.compute:1279-1294`). Cost: Up to 4 extra voxel rays per pixel.
* **Ambient Occlusion (VAO):** Darkens corners and crevices by sampling 8 neighboring blocks (`SVO_REF/SVO_RayTracer.compute:1172-1188`). Cost: 8 tree descents (up to 48 node reads) per pixel.
* **Self-Emission:** Makes glowing materials (e.g., lava) emit their own light: `matData.albedo.rgb * (matData.emission * 2.0)` (`SVO_REF/SVO_RayTracer.compute:1303`). Cost: 0 extra rays.
* **Emissive Bleed:** Spreads the glow of emitting blocks to non-emitting edge neighbors (`SVO_REF/SVO_RayTracer.compute:1315-1319`). Cost: 4 tree descents per pixel.
* **Sun Shadows (sunVis):** Traces 1 or 4 rays toward the sun to compute hard or soft shadows (`SVO_REF/SVO_RayTracer.compute:1210-1224`). Cost: 1 to 4 extra voxel rays per pixel.
* **Sky Occlusion (skyVis):** Traces a ray upward from non-sunlit surfaces to detect enclosed spaces (`SVO_REF/SVO_RayTracer.compute:1228-1234`). Cost: 1 extra voxel ray per pixel.
* **Dynamic Fog:** Blends the final color into the sky gradient based on view distance (`SVO_REF/SVO_RayTracer.compute:1324-1329`). Cost: 0 extra rays.
* **Sun Highlight (Haze):** Adds a bright atmospheric halo around the sun in the sky (`SVO_REF/SVO_RayTracer.compute:1332-1335`). Cost: 0 extra rays.

## 2. Our Engine's Counterparts
* **Sun Shading / Face Shade:** Different. Uses an upward-tilted normal `nF + float3(0.0, 0.65, 0.0)` for top brightness (`Assets/CoreEngine/Rendering/Raymarch.compute:3131`).
* **Ambient:** Same. Uses `_LookAmbient.rgb * skyOpen * vao` (`Assets/CoreEngine/Rendering/Raymarch.compute:3149`).
* **Direct Sun:** Same formula. Uses `_LookSun.rgb * sunLight * sunExposure * softened` (`Assets/CoreEngine/Rendering/Raymarch.compute:3138`).
* **Point Lights:** Different. Sums light contributions (`SumPointLights`) but does *not* trace shadow rays, meaning our point lights shine through walls (`Assets/CoreEngine/Rendering/Raymarch.compute:3152`).
* **Ambient Occlusion (VAO):** Different method. Samples neighbors using 4 flat window bounds reads instead of 8 tree descents (`Assets/CoreEngine/Rendering/Raymarch.compute:3146`).
* **Self-Emission:** Same (as of Part 1 update for Lava).
* **Emissive Bleed:** Different method. Same visual effect, but reads 4 edge neighbors from the flat brickmap window rather than tree descents (`Assets/CoreEngine/Rendering/Raymarch.compute:3180`).
* **Sun Shadows (sunVis):** Same.
* **Sky Occlusion (skyVis):** Same.
* **Dynamic Fog:** Different. Uses a custom `HazeAmount` function and `LookAtmosphere` (`Assets/CoreEngine/Rendering/Raymarch.compute:3192`).
* **Sun Highlight:** Different. Integrated into `LookAtmosphere` instead of a separate lerp.
* **Tone Mapping:** Missing in SVO (or uses HDR output directly). We use `LookToLinear(lit)` (`Assets/CoreEngine/Rendering/Raymarch.compute:3195`).

## 3. Why the SVO Light "Reacts" More
1. **Dynamic Point Light Shadows:** In the SVO, point lights cast genuine shadows by tracing occlusion rays (`SVO_REF/SVO_RayTracer.compute:1294`, `TraceVoxelRay(tr, tMax...)`). This prevents light from bleeding through solid walls, making interiors react correctly to localized light sources.
2. **Emissive Bleed:** The SVO injects the color of glowing blocks (like torches or lava) onto adjacent surfaces, creating a warm, localized halo effect (`SVO_REF/SVO_RayTracer.compute:1318-1319`, `glowColor += bleed * 2.0;`).
3. **Sky Occlusion:** By tracing an upward ray (`SVO_REF/SVO_RayTracer.compute:1234`, `TraceVoxelRay(occRay, 24.0...)`), the SVO accurately identifies enclosed spaces (like caves) and crushes their ambient sky bounce to `0.04`, creating deep, realistic darkness away from the surface.