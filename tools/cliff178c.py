#!/usr/bin/env python3
"""tools/cliff178c.py -- task 178c (cliff look rework): three-way comparisons.

  python3 -I tools/cliff178c.py shots <out dir> <before LookReview dir> <178 dir> <178c dir> [<178c -rockmix 0 dir>]
      per pose: <pose>_1_before.png, <pose>_2_178.png, <pose>_3_rework.png [, <pose>_4_nomix.png] (full size, labelled),
      <pose>_compare.png (the three stacked, full width -- one file Preview opens directly),
      and viewer.html (Space / B,1,2 / arrows; all paths relative, rows are OBJECTS).
  python3 -I tools/cliff178c.py sheets <dump dir> <out dir>
      CliffTests.Report_Cliffs' sections / top views, before | 178c (reuses cliff178.py).
  python3 -I tools/cliff178c.py tones
      CIE dE between each biome's layer rocks, 178 vs 178c rows (palette D).

Why 178's viewer was black: its pairs.json rows were ARRAYS and the page read
s.after / s.before (object fields), so every <img> got src="undefined". This
viewer writes objects and checks every file exists before writing the page.
"""
import json
import math
import os
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import palette_defs  # noqa: E402
from PIL import Image  # noqa: E402

POSES = [
    ("fixed_cliff", "cliff: the 167 m face from 84 m, looking up (forest | desert | badlands border)"),
    ("fixed_coast_cliff", "coast_cliff: the cliff behind the lake from 223 m"),
    ("fixed_forest_breaks", "forest_breaks: canyon wall from 163 m"),
    ("cliff_desert", "desert: the tallest sun-lit desert Sheer face, from 110 m"),
    ("cliff_snow", "snow: the tallest sun-lit snow Sheer face, from 110 m"),
]
ARMS = [("1_before", "BEFORE (-cliffs 0)"), ("2_178", "TASK 178"), ("3_rework", "REWORK (178c)"),
        ("4_nomix", "REWORK WITHOUT THE ROCK MIX (-rockmix 0)")]

VIEWER = """<!doctype html><html><head><meta charset="utf-8"><title>Task 178c: keep the reworked cliffs?</title>
<style>body{margin:0;background:#111;color:#eee;font:15px -apple-system,Helvetica,sans-serif}
#bar{padding:8px 12px;background:#222}#wrap{overflow:auto;height:calc(100vh - 84px)}
img{display:block;image-rendering:pixelated}b{color:#ffcc55}#err{color:#ff6666}</style></head><body>
<div id="bar"><b id="t"></b> &nbsp; <span id="c"></span> <span id="err"></span><br>
Keys: left/right = pose, Space = next arm, B / 1 / 2 / 3 = before / 178 / rework / rework without the mix,
Z = zoom (fit width | 100 %%). Question: <b>keep the reworked cliffs?</b></div>
<div id="wrap"><img id="im"></div>
<script>
const S=%s, A=%s;let i=0,a=2,fit=true;
const im=document.getElementById('im');
im.onerror=()=>{document.getElementById('err').textContent=' MISSING FILE: '+im.getAttribute('src');};
function show(){const s=S[i];document.getElementById('err').textContent='';im.src=s.files[a];
document.getElementById('t').textContent=(i+1)+'/'+S.length+'  '+s.name+'  ['+A[a]+']';
document.getElementById('c').textContent=s.caption;im.style.width=fit?'100%%':'auto';}
document.onkeydown=e=>{const k=e.key.toLowerCase();
if(k=='arrowright')i=(i+1)%%S.length;else if(k=='arrowleft')i=(i+S.length-1)%%S.length;
else if(k==' '){a=(a+1)%%A.length;e.preventDefault();}else if(k=='b')a=0;else if(k=='1')a=1;else if(k=='2')a=2;else if(k=='3'&&A.length>3)a=3;
else if(k=='z')fit=!fit;show();};show();
</script></body></html>"""


def labelled(src, text):
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import cliff178
    img = Image.open(src).convert("RGB")
    cliff178.label(img, text, (14, 12), 30)
    return img


def shots(out, dirs, captions):
    arms = ARMS[:len(dirs)]
    os.makedirs(out, exist_ok=True)
    rows = []
    for pose, cap in POSES:
        files, imgs = [], []
        for (tag, name), d in zip(arms, dirs):
            src = os.path.join(d, f"ws_{pose}_native.png")
            if not os.path.exists(src):
                print("missing", src)
                files = None
                break
            img = labelled(src, f"{pose}: {name}")
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
        rows.append({"name": pose, "caption": captions.get(pose, cap), "files": files})
    for r in rows:
        for f in r["files"]:
            assert os.path.exists(os.path.join(out, f)), f
    with open(os.path.join(out, "viewer.html"), "w") as f:
        f.write(VIEWER % (json.dumps(rows), json.dumps([n for _, n in arms])))
    print("wrote", os.path.join(out, "viewer.html"), len(rows), "poses")


def lab(rgb):
    def lin(c):
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    r, g, b = [lin(c) for c in rgb]
    X = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047
    Y = 0.2126 * r + 0.7152 * g + 0.0722 * b
    Z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883

    def f(t):
        return t ** (1 / 3) if t > 0.008856 else 7.787 * t + 16 / 116
    return 116 * f(Y) - 16, 500 * (f(X) - f(Y)), 200 * (f(Y) - f(Z))


def tones():
    D = palette_defs.PALETTES["D"][2]
    N = palette_defs.NAMES
    old = {"forest": (33, 30, 31), "desert": (42, 40, 41), "snow": (54, 51, 50),
           "jungle": (60, 62, 63), "savanna": (35, 37, 36), "badlands": (45, 46, 47)}
    new = {"forest": (33, 35, 37), "desert": (42, 47, 62), "snow": (54, 51, 33),
           "jungle": (60, 40, 35), "savanna": (35, 30, 44), "badlands": (45, 42, 41)}

    # DetailField.LAYER_PARTNERS (the mixed-brick rock per role): the nearest-tone rock.
    partner = {"forest": (35, 33, 33), "desert": (47, 32, 41), "snow": (33, 46, 54),
               "jungle": (30, 46, 33), "savanna": (44, 46, 35), "badlands": (62, 47, 62)}
    rocks = sorted({r for row in list(old.values()) + list(new.values()) + list(partner.values()) for r in row} | {32, 30, 46, 41})

    def dE(a, b):
        return math.dist(lab(D[a]), lab(D[b]))
    for b in old:
        for tag, row in (("178 ", old[b]), ("178c", new[b])):
            m, l, d = row
            print(f"{b:9s} {tag}: {N[m]} / {N[l]} / {N[d]}  dE main-light {dE(m, l):4.1f}  main-dark {dE(m, d):4.1f}")
        pr = " ".join(f"{N[r]}->{N[p]} {dE(r, p):4.1f}" for r, p in zip(new[b], partner[b]))
        print(f"{'':9s} mix : {pr}")
    print("nearest-tone rock per layer rock (the partner rule):")
    for r in sorted({x for row in new.values() for x in row}):
        best = min((dE(r, q), q) for q in rocks if q != r)
        print(f"  {N[r]:16s} -> {N[best[1]]} {best[0]:4.1f}")


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "shots":
        caps = {}
        cp = os.path.join(sys.argv[2], "captions.json")
        if os.path.exists(cp):
            caps = json.load(open(cp))
        shots(sys.argv[2], sys.argv[3:3 + len(ARMS)], caps)
    elif cmd == "sheets":
        import cliff178
        cliff178.sheets(sys.argv[2], sys.argv[3], "REWORK (task 178c)")
    elif cmd == "tones":
        tones()
