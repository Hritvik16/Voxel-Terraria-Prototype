#!/usr/bin/env python3
"""tools/trees180.py -- task 180 (generated voxel trees, -trees 1): OFF | ON shots.

  python3 -I tools/trees180.py <out dir> <OFF LookReview dir (-trees 0)> <ON LookReview dir (-trees 1)>

Per pose: <pose>_1_before.png (OFF), <pose>_2_after.png (ON), full size and labelled, <pose>_compare.png
(OFF above ON) and viewer.html (Space flips OFF/ON, left/right = pose, Z = fit width | 100 %).
Poses: -wsfixed at the densest tree spot per biome (TreeTests.Poses_DensestSpots) + the survey's range_200up.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from PIL import Image  # noqa: E402
import cliff178  # noqa: E402  (label())

POSES = [
    ("fixed_forest", "forest, 45 m from the densest tree spot: look at the crowns on the ground in the middle -- oak, poplar, bushy fork"),
    ("fixed_jungle", "jungle: emergent trees with flat umbrella crowns over round crowns and palms; buttressed trunks"),
    ("fixed_savanna", "savanna: sparse flat-topped acacias and fat baobabs"),
    ("fixed_snow", "snow: conifers (stacked cones) -- spruce, tall pine, fir"),
    ("fixed_desert", "desert: a few cacti (saguaro / column / barrel), small at this distance"),
    ("range_200up", "FAR VIEW from 1.5 km, 200 m up: forest texture in shells 2-5; beyond 2.2 km (shell 6+) and in the far band there are no trees"),
]
ARMS = [("1_before", "OFF (-trees 0)"), ("2_after", "ON (-trees 1)")]

VIEWER = """<!doctype html><html><head><meta charset="utf-8"><title>Task 180: voxel trees, OFF | ON</title>
<style>body{margin:0;background:#111;color:#eee;font:15px -apple-system,Helvetica,sans-serif}
#bar{padding:8px 12px;background:#222}#wrap{overflow:auto;height:calc(100vh - 84px)}
img{display:block;image-rendering:pixelated}b{color:#ffcc55}#err{color:#ff6666}</style></head><body>
<div id="bar"><b id="t"></b> &nbsp; <span id="c"></span> <span id="err"></span><br>
Keys: left/right = pose, Space = flip OFF/ON, B / A = OFF / ON, Z = zoom (fit width | 100 %%).</div>
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
