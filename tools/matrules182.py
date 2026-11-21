#!/usr/bin/env python3
"""tools/matrules182.py -- task 182 (material rules: deep stone by absolute height, shell exposure): BEFORE | AFTER.

  python3 -I tools/matrules182.py <out dir> <182 ON LookReview dir> <180b ON LookReview dir> <OFF LookReview dir>
      per pose: <pose>_182.png, <pose>_180b.png, <pose>_off.png (full size, labelled), <pose>_compare.png
      (182 ON left | 180b ON right: the fix), and viewer.html (Space / 1-2-3 flip 182 ON | 180b ON | OFF, arrows
      change pose, Z zoom; paths relative). Captions: CAPTIONS below (written after looking at the shots).
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from PIL import Image  # noqa: E402
import matrules180b  # noqa: E402

CAPTIONS = {}  # pose -> caption; filled in after looking
CAP_FILE = "captions182.json"
ARMS = [("182", "AFTER: 182 ON (-matrules 1)"), ("180b", "BEFORE: 180b ON"), ("off", "OFF (today, -matrules 0; 180b's launch)")]


def main(out, *dirs):
    os.makedirs(out, exist_ok=True)
    caps = dict(CAPTIONS)
    cf = os.path.join(out, CAP_FILE)
    if os.path.exists(cf):
        caps.update(json.load(open(cf)))
    rows = []
    for pose, _ in matrules180b.POSES:
        name = pose[6:]
        imgs, files = [], []
        for (tag, label), d in zip(ARMS, dirs):
            src = os.path.join(d, f"ws_{pose}_native.png")
            if not os.path.exists(src):
                print("missing", src)
                continue
            img = matrules180b.labelled(src, f"{name}: {label}")
            fn = f"{name}_{tag}.png"
            img.save(os.path.join(out, fn))
            imgs.append(img)
            files.append(fn)
        if len(imgs) >= 2:
            w, h = imgs[0].size
            sheet = Image.new("RGB", (2 * w + 10, h), (17, 17, 17))
            sheet.paste(imgs[0], (0, 0))
            sheet.paste(imgs[1], (w + 10, 0))
            sheet.save(os.path.join(out, f"{name}_compare.png"))
        if files:
            rows.append({"name": name, "caption": caps.get(name, ""), "files": files,
                         "arms": [l for (t, l) in ARMS if f"{name}_{t}.png" in files]})
    html = matrules180b.VIEWER.replace("Task 180b: keep the new material rules?", "Task 182: material rules, deep stone + shell mosaic fixed")
    html = html.replace("Space = flip ON / OFF, 1 / 2 = ON / OFF", "Space = next arm, 1 / 2 / 3 = 182 ON / 180b ON / OFF")
    html = html.replace("else if(k==' '){a=1-a;e.preventDefault();}else if(k=='1')a=0;else if(k=='2')a=1;",
                        "else if(k==' '){a=(a+1)%%S[i].files.length;e.preventDefault();}else if(k=='1')a=0;else if(k=='2')a=1;else if(k=='3')a=2;")
    with open(os.path.join(out, "viewer.html"), "w") as f:
        f.write(html % json.dumps(rows))
    print("wrote", os.path.join(out, "viewer.html"), len(rows), "rows")


if __name__ == "__main__":
    main(sys.argv[1], *sys.argv[2:5])
