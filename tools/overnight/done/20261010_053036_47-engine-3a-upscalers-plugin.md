# 47-engine-3a-upscalers-plugin

Requires: 45-engine-1-one-generator-path

Approval: owner, 2026-10-09 ~23:20 in chat. Engine plan step 3 changed from "extend our own TAA into
upsampling" to PLUG IN READY-MADE UPSCALERS AND COMPARE (owner Q4: "are there no third party or unity
native tools we can use"; supervisor proposal accepted "yes"). Owner Q3: compare all three
resolutions. Queued by the supervisor. Branch `engine-phase`.

## Goal
Render the raymarch at a lower internal resolution and output 1080p through a selectable upscaler.
Flags (PlaygroundCapture-style, last match wins): `-upscaler none|fsr1|stp|fsr3|taa` and
`-renderrows 540|720|dyn`. Defaults stay EXACTLY as today (owner compares before any default changes;
TAA/checkerboard defaults untouched).

The three upscalers (no from-scratch upscaler):
1. `fsr1`: URP 17.3's built-in FidelityFX Super Resolution 1.0 upscaling filter (URP asset / render
   scale). Spatial only. First check whether our compute-raymarch output actually goes through URP's
   render scale; if not, route it so it does (or say why it can't).
2. `stp`: URP's built-in Spatial-Temporal Post-processing. It needs URP depth + motion vectors: write
   them from the raymarch (per-pixel depth, camera reprojection motion; we already own jitter and
   reprojection from task 06's TAA).
3. `fsr3`: the open-source MIT Unity port of AMD's FSR 2/3 upscaler (compute shaders, Metal-capable;
   e.g. the `FSR3Unity` / `FSR2Unity` project). Vendor it under `Assets/ThirdParty/` with its LICENSE.
   FIRST confirm it compiles and runs on Metal here; if it does not, drop it and say why in HANDOFF
   (do not write our own instead).
`taa` = our existing TAA at the internal resolution + a bilinear upscale, as the fallback/reference.
`dyn`: a simple controller that moves internal rows between 540 and 720 by smoothed frame time
(target 16.7 ms); its stills are taken at whatever it settles on -- record that value.

## Steps
1. Implement the flags and the three integrations (one commit each). Fast suite green after each.
2. Player build log: no `Shader error`.
3. Smoke stills at ONE pose (tg2 spawn, daytime) for every upscaler x 540/720 to prove each path
   draws a sane image: `Screenshots/Engine03/smoke/`. Look at every one; a black or garbage frame is
   a failure of that path.
No comparison, no frame time here: task 48 compares.

## Report for the owner (`## For the doc` in HANDOFF)
- Which upscalers work (yes/no + one line each), what had to be added (depth/motion vectors), and the
  third-party package name, version, licence.
- One smoke contact sheet (rows = upscaler, columns = 540/720), captioned.
