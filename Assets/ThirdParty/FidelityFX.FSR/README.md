# FidelityFX FSR 3 upscaler for Unity (vendored, task 47)

- **What:** AMD FidelityFX Super Resolution 3.1.3 upscaler (temporal), ported to Unity C# + HLSL
  compute by Nico de Poel. Unity package `fidelityfx.fsr` **1.0.1**.
- **Source:** https://github.com/ndepoel/FSR3Unity, commit `71879e71aa72465a79bdf009d7d71f9f7ab83066`
  (2024-12-15, "Updated FSR3 Upscaler to version 3.1.3"), folder `Packages/fidelityfx.fsr`.
- **Licence:** MIT, Copyright (c) 2024 Nico de Poel -- `LICENSE.md` here, unchanged. AMD's FidelityFX
  shader sources inside carry their own MIT headers (Copyright (c) 2024 Advanced Micro Devices, Inc.).
- **Metal:** the port supports macOS Metal. Its README notes that on Metal Unity has no atomic texture
  operations, so `InterlockedAdd/Min/Max` are replaced with non-atomic code (no visible artefacts reported).

## Changes from upstream (layout only, no code edits)
- `Runtime/FSR2` and every FSR 2 shader removed (unused; fewer shaders to compile into the player).
- `Shaders/` moved to `Resources/FidelityFX/Shaders/` so the player build includes the compute shaders
  without an `Fsr3UpscalerAssets` asset; `RaymarchFeature.Upscalers.cs` loads them with `Resources.Load`.
  The shaders' `#include`s are relative, so they are unaffected.

Used by `-upscaler fsr3` (Assets/Game/UpscalerSelect.cs). Nothing else references it.
