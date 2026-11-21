# Screenshot archive (owner, 2026-10-13)
`Screenshots/` images are git-ignored; every task's images live in the claude.ai page **Screenshot Archive**
https://claude.ai/artifact/9y7VxxX8FmsDMtVbSyFd2d (contact sheets per task folder; text reports stay in git).
After a task finishes, the supervisor adds its folder:
    W=<scratchpad>/archive; mkdir -p $W; cp tools/archive/index.json $W/
    python3 -I tools/archive/make_sheets.py Screenshots $W <task dir>
    python3 -I tools/archive/make_page.py $W && cp $W/index.json tools/archive/index.json
    Artifact publish: url = the page above, file_path = $W/index.html, root = $W, files = only the new sheets/<task dir>_*.jpg
    (files left out are kept); then commit tools/archive/index.json.

## VOLUME 2 (from task 197, 2026-11-14): volume 1 is FULL
Volume 1 hit the 512-files-per-version limit (505 published files; deploy 422). `tools/archive/index.json` stays volume 1's
index, frozen at task 196. New folders go to **Screenshot Archive 2** https://claude.ai/artifact/WJup96K4oCyyktm2VhDqsf ,
index `tools/archive/index2.json`:
    W=<scratchpad>/archive2; mkdir -p $W; cp tools/archive/index2.json $W/index.json
    python3 -I tools/archive/make_sheets.py Screenshots $W <task dir>
    python3 -I tools/archive/make_page.py $W && cp $W/index.json tools/archive/index2.json
    then in $W/index.html: <title>/<h1> "Screenshot Archive 2", and the lead starts with the volume-1 link (see the live page)
    Artifact publish: url = volume 2, file_path = $W/index.html, root = $W, files = only the new sheets/<task dir>_*.jpg
It too holds ~500 sheets; start volume 3 the same way when a deploy says "manifest exceeds 512 entries".
