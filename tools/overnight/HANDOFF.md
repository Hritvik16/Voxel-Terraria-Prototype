# 47-engine-3a-upscalers-plugin: DONE. All 5 paths (none, taa, fsr1, stp, fsr3) x 540/720/dyn draw sane frames. Fast 634/0/14.

## What changed (branch `engine-phase`)
- `82b968a` 47a: vendored **AMD FSR 3.1.3 upscaler, MIT Unity port** (`fidelityfx.fsr` 1.0.1, github ndepoel/FSR3Unity
  @ `71879e7`) in `Assets/ThirdParty/FidelityFX.FSR/` with LICENSE.md and a README (FSR 2 removed, shaders under Resources/).
- `f000e2d` 47b: `RaymarchFeature.Upscalers.cs` + `Resources/VoxelUpscaleInputs.compute`: with `UpscaleInputs` = Stp/Fsr3,
  CSMain's TAA block runs (jitter + TaaCur/TaaMetaW), our TAA resolve is skipped, and a SEPARATE kernel turns those buffers
  into URP-convention motion vectors (current - previous, UV) and device depth (reversed Z). CSMain's code is unchanged.
  STP: `StpInputsPass` (BeforeRenderingPostProcessing) overwrites `_MotionVectorTexture` and `_CameraDepthTexture` (D32 here,
  so an SV_Depth shader `VoxelUpscaleDepthWrite`). Rays use URP's jittered projection; the motion offset removes the jitter.
  FSR 3: FSR's own jitter through `_TaaJitter`, an unsafe-pass dispatch to the camera-target size, then the usual blit.
  **Bug found and fixed:** in a player, STP threw an NRE in `STP.Execute` on EVERY frame and the raymarch never ran (0
  dispatches in 600 frames; the stills were stale frames that looked fine). Cause: `STPResourceStripper` removes STP's
  compute shaders unless a build pipeline asset selects STP. Fix: `Assets/Settings/PC_RPAsset_STPInclude.asset` (a PC_RPAsset
  copy with STP selected, never active), pulled into builds by the label `VoxelBuildInclude` (global settings
  `IncludeAdditionalRPAssets`). Unity also re-listed the STP resources entry in the global settings.
- `a14b0f4` 47c: `Assets/Game/UpscalerSelect.cs`: `-upscaler none|taa|fsr1|stp|fsr3`, `-renderrows 540|720|dyn` (last match
  wins). **Neither flag = nothing changes.** fsr1/stp use URP render scale (empty volume profile, as RenderPreset does);
  none/taa/fsr3 use the forced gate. dyn = 36-row steps on smoothed frame time, 15-18 ms dead band. F3 hands control back.
  `-upscalersmoke` (PlaygroundCapture.UpscalerSmoke.cs) + `run-upscaler-smoke.sh`.

## Evidence
- Fast suite PASS 634 FAIL 0 SKIP 14 before commit (`Logs/overnight/47/fast2.out`; also `fast1.out`).
- Player build: 0 `Shader error` (`Logs/upscalersmoke_build.log`). The first build had one (my depth shader lacked URP
  Core.hlsl), fixed. Player.log of the final run: 0 exceptions; FSR 3 contexts 960x540 / 1280x720 / 1280x720 + dynamic.
- Smoke (`Logs/overnight/47/smoke4.out`, `Screenshots/Engine03/smoke/upscalersmoke.txt`): every config traced 89-90 of 90
  frames (1 = the switch frame); dark-pixel share 0.8-1.7%. I looked at all 15 frames plus native-pixel crops: same view in
  every one, nothing black or garbled.
- dyn settled at **540 rows for every upscaler** (smoothed 43-49 ms, far over 16.7 ms; 0 steps). Before the STP fix, STP
  "settled" at 720 with 9-10 ms frames, which is how the dead path showed up.
- Defaults: unchanged by construction (UpscaleInputs Off = same taaOn, same passes). The only new default-path code is a
  C# dispatch counter, and the build now also includes the never-active STP asset. NOT image-diffed against an old build.

## For task 48 (compare) / the owner
- **Tone shift in URP's post path:** fsr1 and stp come out greyer than the others. Mean RGB: none (79,95,115), fsr3
  (77,94,114), URP post + bilinear diagnostic (76,93,106), fsr1/stp (71,88,102). So it is mostly URP's post chain (with
  the empty profile), plus a little from the URP FSR/STP passes. Compare like for like, or fix the tone first.
- FSR 3's motion/jitter signs are derived and look right on still frames. Motion is NOT checked yet (48's motion test).
- The 16 raw stills (46 MB) are on disk in `Screenshots/Engine03/smoke/`, NOT committed; only the sheets and the log are.

## Next step
Task 48: compare the upscalers (quality + motion, then frame time per the milestone rules).

## For the doc
- Upscalers working in the player (yes/no): **none yes, taa yes, fsr1 yes, stp yes** (after the stripping fix), **fsr3
  yes** (FSR 3.1.3, MIT port `fidelityfx.fsr` 1.0.1, ndepoel/FSR3Unity, compiles and runs on Metal here).
- Added for the temporal ones: per-pixel device depth + camera motion vectors from the raymarch's TAA buffers (no change to
  the main kernel), URP STP input pass, FSR 3 dispatch, plus an STP-selected pipeline asset so STP survives the build.
- Raymarch dispatches per frame under STP: **0/600 -> 599/600** (the stripping fix). Every config: 89-90/90.
- dyn settled rows: **540** for all five (smoothed 43-49 ms at this pose; no headroom for 720).
- Mean frame RGB: none (79,95,115) / fsr3 (77,94,114) / fsr1 + stp (71,88,102), a URP post-chain tone shift (above).
- `Screenshots/Engine03/smoke/contact_sheet.png`: rows none, taa, fsr1, stp, fsr3; columns 540 / 720 rows; the tg2 spawn
  at 11:00. Look for the same view in every tile, and the greyer sky in rows 3-4 (fsr1, stp).
- `Screenshots/Engine03/smoke/contact_sheet_crops.png`: the same grid at native pixels (bottom-left of the frame). Look at
  the hill texture and the edges of the near voxels: none aliased, taa soft, fsr1 sharpened speckle, stp and fsr3 cleanest.
