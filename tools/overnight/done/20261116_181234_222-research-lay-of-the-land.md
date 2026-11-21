# 222-research-lay-of-the-land

Model: opus

Approval: OWNER, 2026-11-16 (chat): "Isn't Lay of the Land similar visually to my game? Research what GPU they target, their visuals, performance reviews, and see
if we can reverse engineer what he is doing (he has YouTube devlogs)". RESEARCH ONLY: reads public web pages and transcripts, no engine code, no Unity.
Network use is allowed for this task (WebSearch/WebFetch or `yt-dlp --skip-download --write-auto-subs` if installed; do not install anything, say so if missing).

## Already known (supervisor, 2026-11-16; do not redo, verify only if cheap)
Lay of the Land: solo dev Tooley1998 / Southern Cross Interactive, Unreal Engine 5 (moved from UE4 for Lumen, per a devlog title "I've Improved World Generation and
Moved to UE5 for my Voxel Game"), Steam 8 Apr 2026, $19.99, tags Voxel/Physics/Destruction/Procedural. Steam specs: MIN GTX 1660 Ti / RX 5600 XT, Ryzen 5 2600, 16 GB,
1080p Low, 30 fps target; REC RTX 3060 Ti / RX 6700 XT, Ryzen 5 5600, 16 GB, 1080p High with HARDWARE RAYTRACING, 60 fps. Reviews: Mostly Positive (~1,075);
complaints = stutter, crashes, memory leaks, FPS drops; FSR/DLSS supported. Failed to read: the dev's X posts (402), PCGamingWiki (403), video descriptions/transcripts.
Dev channels: YouTube @tooley1998 (linked from the Steam page), X, Patreon, Ko-fi. Posts mention: LOD transitions ("Spot the LOD transition", Aug 2025),
sand simulation (voxels under gravity), "thousands of moving voxels simulated", cliff generation, snowy-mountain biome, procedural building shapes.

## Do
1. Get the transcripts (auto-subtitles) or descriptions of the dev's devlog videos (at least: "This is Lay Of The Land (EP 1)", "I've Improved World Generation and
   Moved to UE5", and any video about LOD, terrain meshing, lighting, voxel size, destruction, performance). Extract, with video title + timestamp, every concrete
   statement about: how terrain is rendered (meshed chunks / marching cubes / greedy meshing / Nanite / ray marching), voxel size, chunk size, LOD scheme and view
   distance, lighting (Lumen, shadow method, how much is baked), how destruction and sand physics run (CPU/GPU), threading, memory, and the dev's own GPU.
   Mark each fact QUOTED (words from the dev) or INFERRED (yours). Never fill a gap from memory.
2. Performance evidence: Steam reviews and any benchmark or YouTube performance test (GPU, fps, settings). Rough GPU-class comparison with the M1 Air (state sources:
   TFLOPs figures from a cited spec page, not memory).
3. Visual comparison: find 2-3 official screenshots of the game and put side by side with our High-preset shots (Screenshots/219/sheets/doc_sheet.png). LOOK at them;
   say in plain words what is alike and what differs (voxel scale, trees, lighting, view distance, water). Do not publish third-party images into the repo (link only).
4. Compare their technique to ours (ARCHITECTURE_v8.6.md sections only as needed; our lighting passes cost from task 219's HANDOFF `Logs/overnight/219/` and task 212):
   what could we copy that is exact and cheaper (e.g. meshed rasterized near terrain + cheaper GI) and what cannot be copied (our raymarching/1-byte-brick design).
   Write findings as options with cost and risk, NOT a redesign. Flag anything that would change a frozen API or memory layout as STOP_FOR_OWNER material.
5. Output: `RESEARCH_LAY_OF_THE_LAND.md` in the repo root (one page of findings + a table: topic | what they do | source | ours | could we copy?).

## End
HANDOFF with `## For the doc`: the 6-10 key facts, the table, the side-by-side picture with captions, and the options in plain words.

## Owner addition (2026-11-16): large-scale simulation
One devlog shows a large-scale falling-sand style simulation (owner's recollection; verify). Find that video, and in the doc say: what is simulated
(sand, water, both), how many cells / how big an area, how often it ticks, on what hardware, how it is stored (separate grid vs the voxel data), and
whether it runs on CPU or GPU if stated (quote or tag INFERRED). Compare with our fluid sim (GPU CA, fixed 6.5 Hz + adaptive drain, task 91/97) and say in
plain words whether they get it cheaper and how we could too. Options only, no redesign.
