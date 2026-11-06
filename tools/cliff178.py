#!/usr/bin/env python3
"""tools/cliff178.py -- task 178 (C4 cliffs): render CliffTests.Report_Cliffs' dumps.

  python3 -I tools/cliff178.py sheets <dump dir> <out dir>   section + top view, before | after, per pose
  python3 -I tools/cliff178.py viewer <out dir> <shot pairs file>   full-size flip viewer (Space = before|after)

Sections: one voxel per pixel, palette D colours, sky pale blue; the face is
in the middle (x = 500), uphill to the right. Top views: 0.4 m per pixel,
top material x hillshade (sun from the north-west), north up.
"""
import json
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import palette_defs  # noqa: E402
from PIL import Image, ImageDraw, ImageFont  # noqa: E402

COL = {k: tuple(int(round(c * 255)) for c in v) for k, v in palette_defs.PALETTES["D"][2].items()}
SKY = (200, 222, 240)
WATER = (47, 110, 170)
POSES = ["cliff", "coast_cliff", "forest_breaks"]


def colour(m):
    if m == 0:
        return SKY
    if 1 <= m <= 8:
        return COL.get(m, WATER)
    return COL.get(m, (255, 0, 255))


def font(sz):
    for f in ("/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Helvetica.ttc"):
        if os.path.exists(f):
            return ImageFont.truetype(f, sz)
    return ImageFont.load_default()


def section(path):
    with open(path, "rb") as f:
        w, h, ylo = struct.unpack("<iii", f.read(12))
        data = f.read(w * h)
    lut = [colour(m) for m in range(256)]
    img = Image.new("RGB", (w, h))
    img.putdata([lut[b] for b in data])
    return img, ylo, h


def top(path):
    with open(path, "rb") as f:
        n, step = struct.unpack("<ii", f.read(8))
        hts = struct.unpack("<%di" % (n * n), f.read(4 * n * n))
        mats = f.read(n * n)
    px = []
    for j in range(n):
        for i in range(n):
            k = j * n + i
            # hillshade: sun from the north-west (row 0 = north), 45 deg up
            hx = hts[k + 1] - hts[k - 1] if 0 < i < n - 1 else 0
            hz = hts[k - n] - hts[k + n] if 0 < j < n - 1 else 0     # +z is up the image
            dx, dz = hx / (2 * step), hz / (2 * step)
            nx, ny, nz = -dx, 1.0, -dz
            ln = (nx * nx + ny * ny + nz * nz) ** 0.5
            sh = max(0.0, (nx * -0.5 + ny * 0.7071 + nz * 0.5) / ln)
            sh = 0.35 + 0.65 * sh
            c = colour(mats[k])
            px.append(tuple(min(255, int(v * sh)) for v in c))
    img = Image.new("RGB", (n, n))
    img.putdata(px)
    return img.resize((n * 2, n * 2), Image.NEAREST)


def label(img, text, xy=(10, 8), sz=22):
    d = ImageDraw.Draw(img)
    x, y = xy
    for ox, oy in ((-1, -1), (1, 1), (-1, 1), (1, -1)):
        d.text((x + ox, y + oy), text, fill=(0, 0, 0), font=font(sz))
    d.text(xy, text, fill=(255, 255, 255), font=font(sz))


def sheets(dump, out):
    os.makedirs(out, exist_ok=True)
    for p in POSES:
        b, ylo_b, hb = section(os.path.join(dump, f"{p}_before_section.bin"))
        a, ylo_a, ha = section(os.path.join(dump, f"{p}_after_section.bin"))
        # Same vertical frame for both: align on absolute Y.
        top_y = max(ylo_b + hb, ylo_a + ha)
        lo_y = min(ylo_b, ylo_a)
        H = top_y - lo_y
        sheet = Image.new("RGB", (b.width * 2 + 20, H), (255, 255, 255))
        sheet.paste(b, (0, top_y - (ylo_b + hb)))
        sheet.paste(a, (b.width + 20, top_y - (ylo_a + ha)))
        label(sheet, f"{p}: BEFORE (-cliffs 0)", (10, 8))
        label(sheet, f"{p}: AFTER (task 178)", (b.width + 30, 8))
        sheet.save(os.path.join(out, f"section_{p}.png"))
        tb = top(os.path.join(dump, f"{p}_before_top.bin"))
        ta = top(os.path.join(dump, f"{p}_after_top.bin"))
        sheet = Image.new("RGB", (tb.width * 2 + 20, tb.height), (255, 255, 255))
        sheet.paste(tb, (0, 0))
        sheet.paste(ta, (tb.width + 20, 0))
        label(sheet, f"{p} from above, 160 m: BEFORE", (10, 8))
        label(sheet, "AFTER", (tb.width + 30, 8))
        sheet.save(os.path.join(out, f"top_{p}.png"))
        print("wrote", p)


VIEWER = """<!doctype html><html><head><meta charset="utf-8"><title>Task 178: keep these cliffs?</title>
<style>body{margin:0;background:#111;color:#eee;font:15px -apple-system,Helvetica,sans-serif}
#bar{padding:8px 12px;background:#222}#wrap{overflow:auto;height:calc(100vh - 80px)}
img{display:block;image-rendering:pixelated}b{color:#ffcc55}</style></head><body>
<div id="bar"><b id="t"></b> &nbsp; <span id="c"></span><br>
Keys: left/right = image, Space = BEFORE | AFTER, 1-4 = zoom (1 = fit width). Question: <b>keep these cliffs?</b></div>
<div id="wrap"><img id="im"></div>
<script>
const S=%s;let i=0,after=true,zoom=1;
function show(){const s=S[i];const im=document.getElementById('im');im.src=after?s.after:s.before;
document.getElementById('t').textContent=(i+1)+'/'+S.length+'  '+s.name+'  ['+(after?'AFTER':'BEFORE')+']';
document.getElementById('c').textContent=s.caption;
im.style.width=zoom==1?'100%%':(zoom*100)+'%%';}
document.onkeydown=e=>{if(e.key=='ArrowRight')i=(i+1)%%S.length;else if(e.key=='ArrowLeft')i=(i+S.length-1)%%S.length;
else if(e.key==' '){after=!after;e.preventDefault();}else if('1234'.includes(e.key))zoom=+e.key;show();};show();
</script></body></html>"""


def viewer(out, pairs_file):
    with open(pairs_file) as f:
        shots = json.load(f)
    with open(os.path.join(out, "viewer.html"), "w") as f:
        f.write(VIEWER % json.dumps(shots))
    print("wrote", os.path.join(out, "viewer.html"))


if __name__ == "__main__":
    if sys.argv[1] == "sheets":
        sheets(sys.argv[2], sys.argv[3])
    elif sys.argv[1] == "viewer":
        viewer(sys.argv[2], sys.argv[3])
