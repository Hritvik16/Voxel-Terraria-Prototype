# 222-research-lay-of-the-land: DONE -- research only; findings + table + options in `RESEARCH_LAY_OF_THE_LAND.md`
## What changed: no code, no Unity. `RESEARCH_LAY_OF_THE_LAND.md` (new), `TASKS.md` "Task 222" (commit 4ac1ab7).
Picture `Screenshots/222/side_by_side.png` (git-ignored). No EditMode run: docs-only commit, and the task said no Unity.
## Sources / what failed
- Read: Steam appdetails + 83 announcements (news API), Steam forum threads with dev replies, YouTube watch-page descriptions
  and storyboard frames (sand video `7QZvYfAwgvw`, LOD video `WfxuCEyK16I`), search snippets of the dev's X posts.
- The dev's devlogs have NO caption track (8 checked): INFERRED music-only showcases, so no spoken tech detail exists to transcribe.
  `yt-dlp` not installed (not installed by me); a direct YouTube player-API call was refused by the sandbox (not retried).
- "This is Lay Of The Land (EP 1)" is a third-party let's-play (channel "Zanfer"), not the dev. X itself: 402. PCGamingWiki: 403.
## Findings (QUOTED = dev's words)
- 10 cm voxels (QUOTED, forum) -- same as our 0.1 m shell 0. UE5 + Lumen (QUOTED); HW ray tracing optional (recommended spec).
- Terrain is MESHED (INFERRED from QUOTED "voxel mesh shadow imposters", "voxel nav mesh", "LOD fading"). Mesher type never stated.
- Finite single landmass, pre-generated at world creation (2 min -> 40 s on a 7800X3D, QUOTED); voxel data gen on CPU with ISPC.
- MIN GTX 1660 Ti (5.5 TF, NVIDIA) Low 1080p native 30 fps; M1 8-core 2.6 TF (Apple newsroom) -> their minimum is ~2.1x our GPU.
  Per-TFLOP pixel rate theirs ~11 vs ours ~1.2 Mpx/s/TF: a ~9x gap (INFERRED, rough; scenes differ).
- Memory hurts them too: OOM on 16 GB systems (QUOTED 1.0.4), -1.5 GB at max (1.0.25); a player saw 11 GB RAM.
- Simulation: sand/gravel/snow fall, fire spreads, water = source + flowing voxels (INFERRED not volume-conserving), only inside a
  "simulation update range"; CPU/GPU, tick rate, cell counts NOT stated. The sand video shows collapses of a few metres, NOT
  large-scale -- the owner's recollection is not confirmed by any of the 30 newest titles or that video's frames.
## Left unfinished
- No transcripts (none exist for the dev's videos). Tick rate / CPU-vs-GPU of their sim unknown (INFERRED CPU, reasons in the doc).
## Next step
- Owner reads the options; options 5 (pre-generated finite world) and 6 (meshed raster terrain) are STOP_FOR_OWNER-level redesigns,
  not queued. Option 1 (per-voxel colour noise, crevice AO, haze, bloom) fits the current "look" stage if the owner wants it.

## For the doc
Key facts: (1) 10 cm voxels, same as ours (dev, Steam forum 2026-03-26). (2) UE5 + Lumen GI, optional HW ray tracing. (3) Meshed
terrain with LOD fade and shadow imposters (inferred from patch notes). (4) Finite, pre-generated world (40 s - 2 min at creation).
(5) Minimum GPU GTX 1660 Ti = 5.5 TFLOPS vs M1 2.6: ~2.1x ours, for 1080p native Low 30 fps. (6) Per TFLOP they push ~9x the pixels we
do (rough). (7) Memory is their main launch complaint too (OOM on 16 GB). (8) Their "thousands of voxels" sim = sand/fire/water near the
player, a few metres at a time; not large-scale; CPU/GPU not stated.
Table: see `RESEARCH_LAY_OF_THE_LAND.md` "Table" (topic | what they do | source | ours | could we copy?).
Image: `Screenshots/222/side_by_side.png` -- left column ours (High, task 219: canyon walk_near, forest_eye, jungle_eye), right column
their official Steam shots (canyon village, forest house, swamp statue). Look at the SHADE: theirs is lit by warm/green bounce, ours goes
near-black (bottom row); look at SURFACES: theirs vary voxel by voxel with dark crevices, ours are flat colours; look at the DISTANCE:
theirs fades into haze, ours is a hard blue sky with sharp far hills.
Options in plain words: (1) cheap look polish they have -- per-voxel colour variation, crevice shading, haze, bloom (small, now);
(2) compute sky/bounce light at lower resolution and upscale like Lumen's Low (sky fill costs 40 ms today; perf stage);
(3) far shadows from a coarse copy of the world (sun costs 28 ms; perf stage); (4) simulate only the fluid/sand cells that moved
(medium); (5) pre-generate a finite world instead of streaming generation (owner decision, changes the game); (6) switch near terrain
to meshes like they do (a renderer redesign, owner decision; no evidence yet that it is the cause of the gap).
