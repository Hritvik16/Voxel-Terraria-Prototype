# Screenshot archive (owner, 2026-10-13)
`Screenshots/` images are git-ignored; every task's images live in the claude.ai page **Screenshot Archive**
https://claude.ai/artifact/9y7VxxX8FmsDMtVbSyFd2d (contact sheets per task folder; text reports stay in git).
After a task finishes, the supervisor adds its folder:
    W=<scratchpad>/archive; mkdir -p $W; cp tools/archive/index.json $W/
    python3 -I tools/archive/make_sheets.py Screenshots $W <task dir>
    python3 -I tools/archive/make_page.py $W && cp $W/index.json tools/archive/index.json
    Artifact publish: url = the page above, file_path = $W/index.html, root = $W, files = only the new sheets/<task dir>_*.jpg
    (files left out are kept); then commit tools/archive/index.json.
