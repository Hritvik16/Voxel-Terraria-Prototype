Approved by the owner 2026-10-02 (Queue decisions Q3, answer (c)): drop the
`cliff_rise_2` pose added by task 04 (commit 7805791) and close task 04.
Reason: its BEFORE/AFTER shots are byte-identical (the cliff above the centred
band is not visible from the foot), and it adds ~1 min to every poses run. The
counts (band 46 -> 48, dense 374876 -> 442074, deficit 0) stand as the evidence.

Change (capture-only, no engine change): in `Assets/Game/PlaygroundCapture.FarBand.cs`
remove the cliff_rise_2 pose and the code added only for it in 7805791 (`git show
7805791`). Keep every other pose and its order exactly as before 7805791.
In KNOWN_ISSUES.md, TERRAIN-AWARE BAND PLACEMENT, end the "TASK 04" paragraph
with one line: pose dropped by the owner's call; the counts are the evidence.

Acceptance: EditMode 0 FAIL (`tools/with-unity-lock.sh ./run-editmode-tests.sh`);
`git diff 7805791^ -- Assets/Game/PlaygroundCapture.FarBand.cs` shows no change
left from 7805791 in that file (or explain any line kept). No build or rig run
needed: no other pose changes. Commit.
