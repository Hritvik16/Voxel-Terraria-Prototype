Approved by the owner 2026-10-02 (answer "3 yes"): get on-screen evidence for
the terrain-aware vertical band placement (b63f941).

Background (task 01, `0760af2`): at cliff_foot_2 the band moves 46 -> 48
(deficit 308 -> 0) but the framed cliff sits in layers both bands cover, so the
-centredband BEFORE shot is byte-identical to the AFTER shot.

Change: in `Assets/Game/PlaygroundCapture.FarBand.cs` (cliff_foot poses, ~line
282) add ONE new pose, e.g. `cliff_rise_2`, whose frame shows the cliff face
ABOVE the old centred band's top layer but inside the shifted band: same spot
as cliff_foot_2, camera aimed higher, or stepped back so the upper face is in
frame. Do not change existing poses (they feed comparisons). Capture-only code;
no engine change.

Steps:
1. `tools/with-unity-lock.sh ./run-editmode-tests.sh`: 0 FAIL.
2. `BUILD=1 MODE=poses ./run-tg2world.sh` (AFTER), then
   `MODE=poses EXTRA="-centredband" ./run-tg2world.sh` (BEFORE, same build).
3. Record the new pose's band/deficit/dense in both runs. Look at the new pose's
   golden and `_band` shots in both runs: share of far band (green/yellow) vs
   voxels (blue) on the cliff, streaks, stepped edges. If BEFORE and AFTER are
   byte-identical again, adjust the aim ONCE and repeat step 2; if still
   identical, stop with STOP_FOR_OWNER and the numbers.
4. Record the A/B result in the TERRAIN-AWARE BAND PLACEMENT entry of
   KNOWN_ISSUES.md. Commit.
