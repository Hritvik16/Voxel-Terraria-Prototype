Owner: to fix the coast black bar, approve task 22. Delete `OWNER-APPROVES-22` from its Requires line in `tools/overnight/queue/22-farstartt-integer-dda.md`. Until then the runner holds it.

# 19-coast-bar-pixel-probe: DONE. Mechanism pinned: `FarStartT`'s chunk DDA stalls after a -x crossing. Residency bits are correct. NOT fixed.
## What changed (surface-rebuild b128e1f)
- `Raymarch.compute`: a diagnostic dump at the ONE debug pixel, in DebugOut 46-87 (0-45 untouched). It holds currentDist, steps, maxDist, FarStartT as used and unlimited, fh.t/normal/level/cell/texel, the strip column and its `FarChunkResident`, `FarResident` words 27-31, and the shader's window origin, dims and band. Its only effect is writes to DebugOut; no shading changes.
- `FarField.cs`: `ResidentWord(i)` plus the last origin/dims/layers given to `UpdateResidency` (read-only).
- `PlaygroundCapture.FarBand.cs`: `-coastprobe` keeps three poses: the coast pose, the coast pose +12.8 m in x, and the coast pose yawed 180 deg. Each probes (500,720) and (1400,720), checks the pixel mapping against the GPU rayDir, and logs the CPU bits and the store per row.
- KNOWN_ISSUES bullet rewritten; CLAUDE.md index line updated; queued `22-farstartt-integer-dda` (owner-gated).
## Evidence
- Run: `BUILD=1 MODE=poses MAPV=4 EXTRA="-coastprobe" ./run-tg2world.sh`, log `Logs/tg2world_20261004_001514.txt` (also `Logs/overnight/19_coastprobe.out`). AC power, no owner pause.
- Fast suite after the change: **PASS 611 FAIL 0 SKIP 12** (`Logs/overnight/19_suite_fast.out`).
- **Pixel mapping:** validated. Y-flipped matches, rayDir dot 0.999999 (Y-as-is 0.94). Dispatch 960x540, so (500,720) maps to dispatch (250,180).
- **Bar pixel, rayDir (-0.434,-0.157,0.887):**
  - Voxel miss; currentDist 230.9 m (maxDist 290).
  - FarStartT returns **230.9 m = tLimit**. With an unlimited limit it returns 1e8, i.e. it never finds a non-resident column.
  - The far march starts at ray Y 5057 vox, under the sea texel top (5121). It hits in 6 steps with normal **(0,0,-1)**: the water side face.
- **Control pixel, D.x = +0.420:** FarStartT returns **154.0 m** (the strip entry), at ray Y 5176. Far hit at 189.0 m, normal (0,1,0).
- **Residency is right:**
  - The CPU words 27..31 and the shader's words are all 00000000. The store has 0 resident chunks in rows 0-5 and 27-31.
  - The column the bar ray crosses at z chunk 44 (x chunk 306) reads `FarChunkResident` 0.
  - The CPU `UpdateResidency` origin (296,38,15) and dims (32,8,32) equal the shader's `_WindowOriginBricksPacked` (4736,608,240) >> 4. The store ring is (296,34,15) x16, which is expected (mirror vs ring).
- **Why it stalls:** a float32 replay of the shader's loop for this ray sticks at t = 147.40 with p.x = 39936.0 for all 96 iterations.
  - At x ~ 40000 a float step is 0.0039 voxels. The `t + 1e-3` nudge moves x by only 0.00043, so the point rounds back onto the -x face and floor() keeps the same chunk.
  - For D.x > 0 the point lands on the next chunk's minimum, so those rays work.
- **The extent matches:** the bar's edge is where rays leave the camera's chunk through its -x face before the strip (|D.x/D.z| > 64/1376).
  - Predicted edge x 916. Measured 915-916 in the coast pose and in the +1-chunk pose (whose camera is again mid-chunk).
  - Yawed 180: the bar moves to screen RIGHT, i.e. it follows **world -x**. It starts at x 1006-1007 (predicted 1006).
- Images (all noon):
  - Shots: `~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/FarBand/2026-10-04_002102_world/`.
  - `Logs/overnight/19_coast/coast_three_poses_noon.png`, left to right coast | +1 chunk | yaw 180. Look at the dark bar about 2/3 down. It is left of centre in the first two panels, ending at the same column. In the third panel it is on the RIGHT side, over open sea. The thin dark arcs lower down are the known tier seams.
  - `coast_from_sea_30m_noon.png`: the island on the horizon, with the bar lower left over the sea. No other holes and nothing floating.
- **Side note:** this run logged `MIRROR lost 16399` (coast) and `9416` (+1 chunk); stale 0. That is the same symptom as queued task 21 (poses mirror-lost check). Not investigated here.
## Smallest fix (queued as 22, NOT applied)
- In `FarStartT` only: step the INTEGER chunk cell (Amanatides-Woo). Compute `cc` once at window entry, then `cc[a] += sign` on the exiting axis, and return that cell's entry t. This also follows the integer coordinate rule.
- `FarMarch`'s `tExit + 1e-3*cs` uses the same nudge pattern, scaled by the cell size, so it is less exposed. It is not measured; 22 checks it and does not change it.
## Next
- Owner approves 22 (or not). Queue order is unchanged: 20, 21 next.
- `Assets/Scenes/Playground.unity` (a build side effect) is left as found.
