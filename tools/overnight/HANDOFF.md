# 194-bug-rectangular-pits-near-pool: DONE. The pits ARE refused near-pool chunks. Fixed inside the design (shader hand-over); no pool size, eviction, layout or API change.

## Cause (proven)
- A chunk the full near GPU body pool refuses is held as AIR in tier 0. Its column is marked non-resident so the shells draw it.
- But tier 0 still draws the rest of that column (the uniform rock under a surface chunk needs no GPU body) and the columns around it. Tier 0 marches first.
- So a ray through the refused chunk's air hit that rock: a 12.8 m box pit, with the neighbours' sides as straight walls.
- Probe (`[refused]` lines per shot): the refused chunks are surface chunks (700-1,100 dense bricks: trees). Shell 1 STORES every one (0 in its ±1-chunk hole at High). The chunk below is drawn solid by tier 0 in 13-75 of each pose's refusals.
- A pit shows only when the refused chunk lies within tier 0's real reach (about 60-100 m). Chunks refused at 104-145 m (box edge) never showed.

## Fix (commit 6bab2d5)
- `Raymarch.compute`, REFUSED-CHUNK HAND-OVER block: while `_Tier0Refused` is set (any chunk refused, shells + far band on), a tier-0 hit past the ray's entry into a column holding a REFUSED chunk is dropped. Tier 0 ends there and the shell march draws on from that point.
- Refused columns only: `FarField.UpdateResidency` writes them as a second bitmap after the residency bits in `FarResident`.
- v1 (any non-resident column) was REJECTED after its shots: still-streaming columns near the camera lie in shell 1's hole, where no shell holds anything, so houses vanished at eye level.
- CPU twin `Tier0HandOverReference`. `RefusedHandOverTests` (7): a real TerrainClipmap with a 25-body pool reproduces the pit; the floor hit is dropped at the refused column's entry; earlier hits are kept; a streaming-only column is never handed over; tier 0 owns the column again once the chunk lands; the twin matches a fine walk on 500 rays.
- Capture only: `-refusedhandover 0`, WorldSurvey arm `native_rh0` (OFF for one shot), `PlaygroundCapture.RefusedProbe.cs`. TASKS.md paragraph 194.

## Evidence
- Fast suite **PASS 1000 FAIL 0 SKIP 20** (`Logs/overnight/194/fast.out`); filter run 12/12 (`filter2.out`).
- Repro, seed 7, default pool, 191's pose order (`refused_before7.txt`): 14 refused at town_150m, all 104-145 m out. No pits; 191's pits depended on the refusal history.
- Seed 7, `-gpubrickcap 200000` (adversarial, forces near refusals), town_150m: 85 refused, 22 on screen, 63-98 m in front of the town.
  - OFF: a band of box pits. ON: flat sand. Refused count identical in both arms (85 now, 3,174 total).
  - 7.35 % of pixels differ (`Screenshots/194/diff194.txt`).
- Seed 42, default pool:
  - forest: 40 refused, 10 on screen, 52-72 m out. The OFF arm shows a box wall behind the trees; ON shows trees and grass.
  - town_eye: v2 shows all houses (v1 lost them).
- Shots: one build + one launch per seed (`Logs/overnight/194_proof2.sh`). The first v2 build hit the known CSMainFarShadeDiag compiler timeout (as in 134) while the owner used the Mac; the retry at a 60-minute timeout built. Log: `Logs/overnight/194/build_timeout1.log`.

## Not done / open
- A refused chunk INSIDE shell 1's hole still has no shell data: ABSENT, so the far band draws it. Seen once (4 at seed 42 forest ON, none on screen). The real fix would need shell data in the hole or a pool rule. Owner call if it ever shows.
- Sun shadows across a handed-over column can differ (seed7cap town_150m ON: the tree shadow bottom-left is clipped to a notch).
- The pool itself still overflows with trees on (191: 23 k refusals). Pits are gone, but refused chunks draw at shell 1's 0.2 m until a slot frees.
- No timing (rule 10). The new block runs a ≤20-cell column walk per tier-0 hit, only while chunks are refused. A performance milestone should see it.

## Next step
191's question can now move on: trees / towns ON no longer hinge on the pits. Optional: re-shoot 191's pit poses in play.

## For the doc
- Cause: refused near-pool chunk = air in tier 0 over drawn rock → 12.8 m box pit. Shell 1 stores 100 % of the refused chunks seen (`Screenshots/194/refused_v2_*.txt`).
- Seed 7 small pool, town_150m: on-screen box pits **many → 0**, refused **85 → 85** (unchanged); 7.35 % of pixels change (`Screenshots/194/diff194.txt`).
- Seed 42 forest (default pool): the box wall behind the trees **→ gone**; refused 40 OFF / 15 ON (streaming between arms).
- Tests: RefusedHandOverTests 7/7; fast suite PASS 1000 FAIL 0 SKIP 20.
- Images:
  - `Screenshots/194/seed7cap_town_150m_before.png` vs `..._after.png`: the foreground between the camera and the desert town. BEFORE has a band of sunken rectangular boxes with straight pink-brown walls and pale floors. AFTER is flat unbroken sand. Same pose, same refused chunks.
  - `Screenshots/194/seed42_forest_before.png` vs `..._after.png`: the centre behind the dark trunks (x≈960-1000, y≈500-590). BEFORE shows a yellow-brown vertical box wall and a brown patch at left (x≈520, y≈580). AFTER shows trees and grass there.
  - `Screenshots/194/seed42_town_eye_after_v1_REJECTED.png` vs `seed42_town_eye_after_v2.png`: the left half. v1 lost the houses and treeline (a bare far-band hill shows); v2 keeps them. Why the fix is limited to refused columns.
