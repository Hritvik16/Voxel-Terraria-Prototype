#!/usr/bin/env python3
"""tools/river179.py -- task 179 (C5 rivers that meander and carve): before | after shots.

  python3 -I tools/river179.py <out dir> <before LookReview dir (-rivershape 0)> <after LookReview dir>

Per pose: <pose>_1_before.png, <pose>_2_after.png (full size, labelled), <pose>_compare.png
(before above after, full width -- one file Preview opens directly) and viewer.html
(Space flips before/after, left/right = pose, Z = fit width | 100 %; paths relative,
rows are OBJECTS; every file is checked before the page is written).
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from PIL import Image  # noqa: E402
import cliff178  # noqa: E402  (label())

POSES = [
    ("pair_snow_river-plains", "snow river plains: the channel in the middle -- was an aqueduct between raised levee walls; "
                               "now look for water below the snow, carved banks, a curving course"),
    ("river", "the biggest river valley: look along the river -- straight D8 runs with 45/90 deg kinks before, "
              "a curving course after; the water should sit in the ground, not on it"),
    ("pair_desert_canyonlands", "desert canyonlands: the channels on the canyon floor -- straight canals before; "
                                "after they bend and keep to the floor (no climbing the walls)"),
    ("feature_mesa-4", "mesa: the 'canal grid' on the flats -- after, the channels curve and sit below the ground"),
    ("pair_badlands_high-plateau", "badlands high plateau: the segment that rose out of the ground like a bent pipe -- "
                                   "after, the water should be in a cut channel"),
]
ARMS = [("1_before", "BEFORE (-rivershape 0)"), ("2_after", "AFTER (task 179 rivers)")]

VIEWER = """<!doctype html><html><head><meta charset="utf-8"><title>Task 179: rivers that meander and carve</title>
<style>body{margin:0;background:#111;color:#eee;font:15px -apple-system,Helvetica,sans-serif}
#bar{padding:8px 12px;background:#222}#wrap{overflow:auto;height:calc(100vh - 84px)}
img{display:block;image-rendering:pixelated}b{color:#ffcc55}#err{color:#ff6666}</style></head><body>
<div id="bar"><b id="t"></b> &nbsp; <span id="c"></span> <span id="err"></span><br>
Keys: left/right = pose, Space = flip before/after, B / A = before / after, Z = zoom (fit width | 100 %%).</div>
<div id="wrap"><img id="im"></div>
<script>
const S=%s, A=%s;let i=0,a=1,fit=true;
const im=document.getElementById('im');
im.onerror=()=>{document.getElementById('err').textContent=' MISSING FILE: '+im.getAttribute('src');};
function show(){const s=S[i];document.getElementById('err').textContent='';im.src=s.files[a];
document.getElementById('t').textContent=(i+1)+'/'+S.length+'  '+s.name+'  ['+A[a]+']';
document.getElementById('c').textContent=s.caption;im.style.width=fit?'100%%':'auto';}
document.onkeydown=e=>{const k=e.key.toLowerCase();
if(k=='arrowright')i=(i+1)%%S.length;else if(k=='arrowleft')i=(i+S.length-1)%%S.length;
else if(k==' '){a=1-a;e.preventDefault();}else if(k=='b')a=0;else if(k=='a')a=1;
else if(k=='z')fit=!fit;show();};show();
</script></body></html>"""


def main():
    out, dirs = sys.argv[1], sys.argv[2:4]
    os.makedirs(out, exist_ok=True)
    rows = []
    for pose, cap in POSES:
        files, imgs = [], []
        for (tag, name), d in zip(ARMS, dirs):
            src = os.path.join(d, f"ws_{pose}_native.png")
            if not os.path.exists(src):
                print("missing", src)
                files = None
                break
            img = Image.open(src).convert("RGB")
            cliff178.label(img, f"{pose}: {name}", (14, 12), 30)
            fn = f"{pose}_{tag}.png"
            img.save(os.path.join(out, fn))
            files.append(fn)
            imgs.append(img)
        if files is None:
            continue
        w, h = imgs[0].size
        sheet = Image.new("RGB", (w, h * len(imgs) + 10 * (len(imgs) - 1)), (17, 17, 17))
        for k, img in enumerate(imgs):
            sheet.paste(img, (0, k * (h + 10)))
        sheet.save(os.path.join(out, f"{pose}_compare.png"))
        rows.append({"name": pose, "caption": cap, "files": files})
    for r in rows:
        for f in r["files"]:
            assert os.path.exists(os.path.join(out, f)), f
    with open(os.path.join(out, "viewer.html"), "w") as f:
        f.write(VIEWER % (json.dumps(rows), json.dumps([n for _, n in ARMS])))
    print("wrote", os.path.join(out, "viewer.html"), len(rows), "poses")


if __name__ == "__main__":
    main()
