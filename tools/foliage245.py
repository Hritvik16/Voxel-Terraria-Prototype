#!/usr/bin/env python3
"""Task 245: foliage light early-out. Copies the cost-split stills (no HUD), makes 4x difference images, a contact sheet,
   a numbers file and a full-size flip viewer.
   python3 -I tools/foliage245.py <stills dir 16 deg> <stills dir noon>   (from the repo root)
   arms: off = foliage_off (shader OFF), on = full (ON, eps 1/8), e2 = foliage_e2 (ON, eps 1/2)."""
import os, sys, shutil
import numpy as np
from PIL import Image

D = "Screenshots/245"
os.makedirs(D, exist_ok=True)
poses = ["walk_near", "forest_eye", "oak_under"]
suns = ["16", "noon"]
arm_src = {"off": "foliage_off", "on": "full", "e2": "foliage_e2"}
for sun, src in zip(suns, sys.argv[1:3]):
    for p in poses:
        for a, s in arm_src.items():
            f = os.path.join(src, f"{p}_{s}_clean.png")
            if os.path.exists(f): shutil.copy(f, f"{D}/{p}_{a}_{sun}.png")

def load(p): return np.asarray(Image.open(p).convert("RGB"), dtype=np.float32)
def luma(x): return 0.2126 * x[..., 0] + 0.7152 * x[..., 1] + 0.0722 * x[..., 2]

lines = []
for sun in suns:
    for p in poses:
        a = f"{D}/{p}_off_{sun}.png"
        if not os.path.exists(a): continue
        A = load(a)
        for arm in ["on", "e2"]:
            b = f"{D}/{p}_{arm}_{sun}.png"
            if not os.path.exists(b): continue
            B = load(b)
            d = np.abs(A - B).mean(axis=2)
            Image.fromarray(np.clip(d * 4, 0, 255).astype(np.uint8)).save(f"{D}/{p}_diff4{arm}_{sun}.png")
            lines.append(f"{p:<11} {sun:>4} {arm:>3}: mean |diff| {d.mean():5.2f} / 255, pixels changed (> 8) {(d > 8).mean() * 100:5.2f} %, "
                         f"mean luma OFF {luma(A).mean():6.2f} ON {luma(B).mean():6.2f} ({(luma(B).mean() / max(1e-6, luma(A).mean()) - 1) * 100:+.1f} %)")
open(f"{D}/diff245.txt", "w").write("Task 245: foliage_off vs ON (on = eps 1/8, e2 = eps 1/2), per pose and sun; 4x diff images <pose>_diff4<arm>_<sun>.png\n"
                                    + "\n".join(lines) + "\n")
print("\n".join(lines))

tiles = []
for sun in suns:
    for p in poses:
        row = [f"{D}/{p}_off_{sun}.png", f"{D}/{p}_on_{sun}.png", f"{D}/{p}_diff4on_{sun}.png", f"{D}/{p}_e2_{sun}.png", f"{D}/{p}_diff4e2_{sun}.png"]
        if all(os.path.exists(x) for x in row): tiles.append(row)
if tiles:
    w, h = 480, 270
    sheet = Image.new("RGB", (w * 5, h * len(tiles)), (17, 17, 17))
    for r, row in enumerate(tiles):
        for c, f in enumerate(row):
            sheet.paste(Image.open(f).convert("RGB").resize((w, h)), (c * w, r * h))
    sheet.save(f"{D}/sheet245.jpg", quality=88)

nm = {}
for sun, sn in [("16", "16 deg"), ("noon", "noon")]:
    nm[f"off_{sun}"] = f"OFF (shader early-out off) {sn}"
    nm[f"on_{sun}"] = f"ON eps 1/8 {sn}"
    nm[f"diff4on_{sun}"] = f"4x difference OFF vs eps 1/8 {sn}"
    nm[f"e2_{sun}"] = f"ON eps 1/2 {sn}"
    nm[f"diff4e2_{sun}"] = f"4x difference OFF vs eps 1/2 {sn}"
html = """<html><body style="background:#111;color:#ddd;font-family:sans-serif">
<h3>Task 245 foliage light early-out: arrows = next / previous pose; Space = cycle OFF / ON 1/8 / diff / ON 1/2 / diff; S = other sun; Z = full size</h3>
<p>Look at: the sunlit dapples on the ground under the crowns (do they go dark?), and the edges of the crown shadows.</p>
<div id=t></div><img id=i style="max-width:100%"><script>
var p=%s,nm=%s,k=0,m=0,s=0,z=0;
function f(){var a=['off','on','diff4on','e2','diff4e2'][m]+'_'+['16','noon'][s];var i=document.getElementById('i');i.src=p[k]+'_'+a+'.png';i.style.maxWidth=z?'none':'100%';
document.getElementById('t').textContent=(k+1)+'/'+p.length+'  '+p[k]+'  --  '+nm[a];}
document.onkeydown=function(e){if(e.key==' '){m=(m+1)%5;}else if(e.key=='s'||e.key=='S'){s=1-s;}else if(e.key=='ArrowRight'){k=(k+1)%p.length;}else if(e.key=='ArrowLeft'){k=(k+p.length-1)%p.length;}else if(e.key=='z'||e.key=='Z'){z=1-z;}f();e.preventDefault();};f();
</script></body></html>""".replace("%s", str(poses), 1).replace("%s", str(nm).replace("'", '"'), 1)
open(f"{D}/viewer.html", "w").write(html)
print("wrote", f"{D}/viewer.html", f"{D}/sheet245.jpg", f"{D}/diff245.txt")
