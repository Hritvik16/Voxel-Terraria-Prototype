#!/usr/bin/env python3
"""Task 213: contact sheet + full-size flip viewer, denser forest (d) vs task 210 (s) vs today (t0),
   Screenshots/213/<pose>_<arm>.png from Logs/overnight/213_shots.sh.
   python3 -I tools/trees213.py   (run from the repo root)"""
import os
from PIL import Image, ImageDraw
D = "Screenshots/213"
W, H = 960, 540
POSES = ["forest_eye", "forest_100m", "savanna_100m", "snow_100m"]
ARMS = [("d", "213 dense (-trees2 1)"), ("s", "210 (-trees2dense 0)"), ("t0", "today (-trees2 0)")]

def tile(name, label):
    p = f"{D}/{name}.png"
    im = Image.open(p).convert("RGB").resize((W, H), Image.LANCZOS) if os.path.exists(p) else Image.new("RGB", (W, H), (40, 40, 40))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W, 30], fill=(0, 0, 0))
    d.text((10, 8), label if os.path.exists(p) else label + "  (MISSING)", fill=(255, 255, 255))
    return im

S = Image.new("RGB", (len(ARMS) * W, len(POSES) * H + 40), (20, 20, 20))
ImageDraw.Draw(S).text((10, 12), "Task 213: left = denser forest (new -trees2 1), middle = task 210's placement, right = today's trees; same camera, noon, sun behind", fill=(255, 255, 255))
for j, n in enumerate(POSES):
    for i, (a, lab) in enumerate(ARMS):
        S.paste(tile(f"{n}_{a}", f"{n}  {lab}"), (i * W, 40 + j * H))
S.save(f"{D}/sheet213.jpg", quality=88)
print("wrote", f"{D}/sheet213.jpg")

js = ",".join(f"'{n}'" for n in POSES)
html = f"""<html><body style="background:#111;color:#ddd;font-family:sans-serif">
<h3>Task 213 denser forest: arrows = next / previous pose; Space = cycle 213 dense / 210 / today at the same camera (1 2 3 pick one); Z = full size</h3>
<div id=t></div><img id=i style="max-width:100%"><script>
var p=[{js}],a=['d','s','t0'],nm={{'d':'213 dense (-trees2 1)','s':'210 (-trees2 1 -trees2dense 0)','t0':'today (-trees2 0)'}},k=0,m=0,z=0;
function s(){{var f=p[k]+'_'+a[m]+'.png';var i=document.getElementById('i');i.src=f;i.style.maxWidth=z?'none':'100%';
document.getElementById('t').textContent=(k+1)+'/'+p.length+'  '+p[k]+'  --  '+nm[a[m]];}}
document.onkeydown=function(e){{if(e.key==' '){{m=(m+1)%3;}}else if(e.key>='1'&&e.key<='3'){{m=+e.key-1;}}else if(e.key=='ArrowRight'){{k=(k+1)%p.length;}}else if(e.key=='ArrowLeft'){{k=(k+p.length-1)%p.length;}}else if(e.key=='z'||e.key=='Z'){{z=1-z;}}s();e.preventDefault();}};s();
</script></body></html>"""
open(f"{D}/viewer.html", "w").write(html)
print("wrote", f"{D}/viewer.html")
