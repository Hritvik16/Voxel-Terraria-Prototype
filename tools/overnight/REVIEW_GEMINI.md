# Side session: review Gemini's lava-look change (owner, 2026-10-09)

You are a SHORT-LIVED side session. The owner talks to you directly and you tell the
owner what is next; the supervisor (another Claude session) is not involved. A queue
worker (task 37e) is running in the main repo at the same time, so:
- **No Unity at all** -- no builds, EditMode runs or players (one Unity per Mac; it OOM'd today).
- **Do not edit files in the main repo** (`../Voxel Terraria 1 Byte BrickMap`) except
  the ONE queue file in step 4. Work in this worktree: `../VT_gemini`, branch `gemini/lava-look`.
- Be cheap: diffs, grep, targeted reads. No subagents, no claude.ai docs.

## Background
The owner liked the SVO build's lava (deep orange, glowing). Ours renders pale gold because
LOOK 1 shades self-lit voxels as `saturate(baseColor * 1.35 + 0.06) + tint * 0.55`.
Gemini CLI (no shell, file tools only) was asked to:
1. switch lava to the SVO form `baseColor * light + albedo * emission * 2`, with slight
   per-voxel hot/cool variation (VoxelHash);
2. add an emissive bleed (faces next to lava get its glow) behind a new int uniform
   `_EmissiveBleed`, default 0, wired through LightingConfig.cs / RaymarchFeature.cs and a
   `-emissivebleed 0|1` flag in PlaygroundCapture.cs (like `-lookgrade`).
Reference: `../VoxelTerraria_compare/Voxel-Terraria/Assets/VoxelEngine/Compute/RayTracer.compute`
(glowColor, SvoEmitColor, bleed) and its NewMaterialPalette.asset (lava 0.95,0.38,0.08, emission 0.6).
Comparison images: `Screenshots/SVOLook/` in the main repo (lava_night_svo_vs_ours.jpg).

## Steps
1. Read `GEMINI_HANDOFF.md` and `git diff` here. Gemini could not compile anything.
2. Review like code review, against CLAUDE.md: integer coordinate math (>> and &, no float
   division for cells); the bleed's neighbour lookups use the existing window/brick
   accessors and are out of the default path (`_EmissiveBleed == 0` must leave the frame
   pixel-identical apart from the lava colour change); new uniform set every frame in
   RaymarchFeature like its siblings; LightingConfig field + validation consistent; arg
   parsing matches `-lookgrade` (ArgInt takes the LAST match -- deliberate, keep it);
   HLSL that would not compile (types, undeclared names, missing semicolons); anything
   outside the four files. Also: the colour tables carry "change a colour in BOTH places"
   notes -- keep them consistent; `RaymarchStripped.compute` -- does it need the same edit?
3. Fix what is wrong yourself, in this worktree. Then commit on `gemini/lava-look` (by path,
   NOT `.gemini/`): "Gemini + Claude review: lava look (SVO form) and opt-in emissive bleed",
   ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Keep GEMINI_HANDOFF.md
   in the commit, with a short "Review" section added at its top.
4. Queue the check (the runner picks it up after 37e): in the MAIN repo write ONE file
   `tools/overnight/queue/<next free number>-lava-look-shots.md` (look in queue/, done/,
   later/, blocked/ for used numbers), `Requires: 37e-lake-flat-water`, saying: merge
   `gemini/lava-look` into `surface-rebuild`; fast EditMode suite plus
   `ShaderCompileCheck` (and confirm the player build compiles the kernel -- KNOWN_ISSUES:
   CheckAll can report green for a shader that does not build); take before/after shots
   with the existing lava-light scene (`-lavalight`, see run-lavalight-ab / Screenshots/LavaLight/
   lavalight.txt poses: cavern, surface pool at night and golden hour) at HEAD-before-merge,
   after with `-emissivebleed 0`, after with `-emissivebleed 1`; one captioned contact sheet;
   NO performance arms (PREAMBLE rule 10; bleed's cost is measured at the engine milestone);
   end with STOP_FOR_OWNER and the pictures. Commit that one file by path:
   `git commit -m "..." -- tools/overnight/queue/<file>`.
5. Tell the owner in plain words: what Gemini changed, what you fixed and why, what is
   queued and when it runs, and that this window can be closed. Then stop.
