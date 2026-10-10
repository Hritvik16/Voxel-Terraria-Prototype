# Gemini task -- 2026-10-09 (owner-approved: Q34 answer 3a, and the SVO lighting question)

Branch `gemini/lava-torch-light` in this worktree. You have no shell: do not try to build, test or run
Unity. Claude compiles, tests and reviews your work afterwards. Do NOT touch any other file than the
ones named here. When done, write `GEMINI_HANDOFF.md` (replace its contents): what you changed, with
file:line, and anything you were unsure about.

## Part 1 -- code: lava ignores torch (point) light
Owner decision 2026-10-09 (Q34 3a): under 40 torches the lava goes pale yellow because
`faceLight` carries the torch point lights. Lava is self-lit, so it must not receive point light.

File: `Assets/CoreEngine/Rendering/Raymarch.compute`, around lines 3149-3167 (LOOK 1 shading):
```
float3 pointTerm = SumPointLights(hitPosM, nF);
float3 faceLight = (direct + ambient) * _LookGrade.x + pointTerm;
if (_UniformFaceHue != 0) faceLight = UniformFaceHue(...);
...
if (lm == 11u) { ... lit = baseColor * faceLight + albedo * (MaterialEmissive(lm) * 2.0); }
```
Change ONLY the `lm == 11u` branch: use `(direct + ambient) * _LookGrade.x` instead of `faceLight`
there (no point lights, no UniformFaceHue). Every other material must stay byte-for-byte identical.
Update the LAVA comment above the branch by one line saying why (owner 2026-10-09, Q34 3a).
Also check the second shading path near line 3297-3315 (`faceLight = sunTerm + pointTerm + ambientTerm`):
if lava (material 11) can be shaded there, apply the same rule in the same minimal way; if not, say so.

## Part 2 -- notes only, NO code: how the SVO build makes light react to its surroundings
Owner, 2026-10-09: "what I liked about the SVO was how light reacts to its surroundings more than
the lava itself." This feeds the engine-phase plan. Reference (read only):
`SVO_REF/SVO_RayTracer.compute` (a copy of the SVO repo's `RayTracer.compute`)
(and `SVO_REF/SVO_ChunkManager.cs.txt`, the C# that sets its parameters). Cite the copies by these paths. Do not edit anything in `SVO_REF/`. Ours: `Assets/CoreEngine/Rendering/Raymarch.compute`.

Write `SVO_LIGHTING_NOTES.md` in this worktree:
1. A list of every lighting effect in the SVO's final shading (sun, sky/ambient, AO, bounce/indirect,
   emissive bleed, point lights, fog, tone mapping, anything else). For EACH: what it does in one
   sentence, the exact file:line, and how many extra reads/rays per pixel it costs.
2. The same list for ours, effect by effect, saying "same", "different (how)" or "missing".
3. Which 2-3 differences most likely make the SVO's light "react to its surroundings" (light picking
   up the colour of nearby surfaces, light spreading from emitters, darkening in enclosed spaces...),
   each with the code lines that cause it.
Rules: every claim cites a file:line you actually read. Write "not sure" rather than guess. No
severity words, no recommendations about performance numbers (nothing was measured). Keep it under
150 lines.
