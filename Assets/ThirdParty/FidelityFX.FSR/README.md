# FidelityFX FSR 3 upscaler for Unity (vendored, task 47)

- **What:** AMD FidelityFX Super Resolution 3.1.3 upscaler (temporal), ported to Unity C# + HLSL
  compute by Nico de Poel. Unity package `fidelityfx.fsr` **1.0.1**.
- **Source:** https://github.com/ndepoel/FSR3Unity, commit `71879e71aa72465a79bdf009d7d71f9f7ab83066`
  (2024-12-15, "Updated FSR3 Upscaler to version 3.1.3"), folder `Packages/fidelityfx.fsr`.
- **Licence:** MIT, Copyright (c) 2024 Nico de Poel -- `LICENSE.md` here, unchanged. AMD's FidelityFX
  shader sources inside carry their own MIT headers (Copyright (c) 2024 Advanced Micro Devices, Inc.).
- **Metal:** the port supports macOS Metal. Its README notes that on Metal Unity has no atomic texture
  operations, so `InterlockedAdd/Min/Max` are replaced with non-atomic code (no visible artefacts reported).

## Changes from upstream (layout; code edits below)
- `Runtime/FSR2` and every FSR 2 shader removed (unused; fewer shaders to compile into the player).
- `Shaders/` moved to `Resources/FidelityFX/Shaders/` so the player build includes the compute shaders
  without an `Fsr3UpscalerAssets` asset; `RaymarchFeature.Upscalers.cs` loads them with `Resources.Load`.
  The shaders' `#include`s are relative, so they are unaffected.

## Local code edits (task 166, 2026-11-02)
- `Runtime/FSR3/Fsr3UpscalerContext.cs`, `Fsr3UpscalerPass.cs`: the debug-view pass is compiled into release players
  too (was `#if UNITY_EDITOR || DEVELOPMENT_BUILD`); it runs only when `DispatchFlags.DrawDebugView` is set
  (`RaymarchFeature.Fsr3DebugView`, capture arm `_dbg`).
- `shaders/fsr3upscaler/ffx_fsr3upscaler_prepare_reactivity.h`: `ComputeShadingChange` multiplies the auto-detected
  shading change by `1 - _VeShadingChangeCut` (unset uniform = 0 = upstream exactly). FSR 3.1.4 exposes the same
  idea as `shadingChangeScale`.

Used by `-upscaler fsr3` (Assets/Game/UpscalerSelect.cs). Nothing else references it.
