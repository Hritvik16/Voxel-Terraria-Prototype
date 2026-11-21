#!/usr/bin/env python3
"""Task 243: solid crown cores. 4x difference images, a contact sheet and a full-size flip viewer.
   python3 -I tools/trees243.py   (from the repo root; reads Screenshots/243/<pose>_<arm>.png)
   arms: off_noon c2_noon off_16 c2_16 (-crowncore 2 = keepBricks build); full_bricks/ = the first pass (interior bricks too)."""
import os
import numpy as np
from PIL import Image

D = "Screenshots/243"
poses = ["forest_eye", "oak_under", "oak_inside"]
suns = ["noon", "16"]

def load(p): return np.asarray(Image.open(p).convert("RGB"), dtype=np.float32)

lines = []
for sun in suns:
    for p in poses:
        a, b = f"{D}/{p}_off_{sun}.png", f"{D}/{p}_c2_{sun}.png"
        if not (os.path.exists(a) and os.path.exists(b)): continue
        A, B = load(a), load(b)
        d = np.abs(A - B).mean(axis=2)
        Image.fromarray(np.clip(d * 4, 0, 255).astype(np.uint8)).save(f"{D}/{p}_diff4_{sun}.png")
        changed = (d > 8).mean() * 100
        lines.append(f"{p:<11} {sun:>4}: mean |diff| {d.mean():5.1f} / 255, pixels changed (> 8) {changed:5.1f} %, mean luma OFF {A.mean():5.1f} ON {B.mean():5.1f}")
open(f"{D}/diff243.txt", "w").write("Task 243: OFF vs -crowncore 2, per pose and sun (4x diff images: <pose>_diff4_<sun>.png)\n" + "\n".join(lines) + "\n")
print("\n".join(lines))

# contact sheet: rows = pose x sun, columns OFF | ON | 4x diff, 1/3 size tiles
tiles = []
for sun in suns:
    for p in poses:
        row = [f"{D}/{p}_off_{sun}.png", f"{D}/{p}_c2_{sun}.png", f"{D}/{p}_diff4_{sun}.png"]
        if all(os.path.exists(x) for x in row): tiles.append(row)
if tiles:
    w, h = 640, 360
    sheet = Image.new("RGB", (w * 3, h * len(tiles)), (17, 17, 17))
    for r, row in enumerate(tiles):
        for c, f in enumerate(row):
            sheet.paste(Image.open(f).convert("RGB").resize((w, h)), (c * w, r * h))
    sheet.save(f"{D}/sheet243.jpg", quality=88)

arms = ["off_noon", "c2_noon", "diff4_noon", "off_16", "c2_16", "diff4_16"]
nm = {"off_noon": "OFF noon", "c2_noon": "-crowncore 2 noon", "diff4_noon": "4x difference noon",
      "off_16": "OFF 16 deg", "c2_16": "-crowncore 2 16 deg", "diff4_16": "4x difference 16 deg"}
html = """<html><body style="background:#111;color:#ddd;font-family:sans-serif">
<h3>Task 243 solid crown cores: arrows = next / previous pose; Space = cycle OFF / ON / 4x diff at the same sun; S = other sun; Z = full size</h3>
<div id=t></div><img id=i style="max-width:100%"><script>
var p=%s,nm=%s,k=0,m=0,s=0,z=0;
function f(){var a=['off','c2','diff4'][m]+'_'+['noon','16'][s];var i=document.getElementById('i');i.src=p[k]+'_'+a+'.png';i.style.maxWidth=z?'none':'100%';
document.getElementById('t').textContent=(k+1)+'/'+p.length+'  '+p[k]+'  --  '+nm[a];}
document.onkeydown=function(e){if(e.key==' '){m=(m+1)%3;}else if(e.key=='s'||e.key=='S'){s=1-s;}else if(e.key=='ArrowRight'){k=(k+1)%p.length;}else if(e.key=='ArrowLeft'){k=(k+p.length-1)%p.length;}else if(e.key=='z'||e.key=='Z'){z=1-z;}f();e.preventDefault();};f();
</script></body></html>""".replace("%s", str(poses), 1).replace("%s", str(nm).replace("'", '"'), 1)
open(f"{D}/viewer.html", "w").write(html)
print("wrote", f"{D}/viewer.html", f"{D}/sheet243.jpg", f"{D}/diff243.txt")
