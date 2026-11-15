#!/usr/bin/env python3
"""Task 210: contact sheet + full-size flip viewer for -trees2 1 vs -trees2 0 (Screenshots/210/<pose>_t2.png / _t0.png).
   python3 -I tools/trees210.py   (run from the repo root)"""
import os
from PIL import Image, ImageDraw
D = "Screenshots/210"
W, H = 960, 540
POSES = ["forest_eye", "forest_100m", "jungle_eye", "jungle_100m", "savanna_eye", "savanna_100m", "snow_eye", "snow_100m",
         "desert_eye", "desert_100m", "redwood_near", "redwood_far1", "redwood_far600", "redwood_far1500",
         "banyan_near", "banyan_far1", "banyan_far600", "town_eye"]

def tile(name, label):
    p = f"{D}/{name}.png"
    im = Image.open(p).convert("RGB").resize((W, H), Image.LANCZOS) if os.path.exists(p) else Image.new("RGB", (W, H), (40, 40, 40))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W, 30], fill=(0, 0, 0))
    d.text((10, 8), label if os.path.exists(p) else label + "  (MISSING)", fill=(255, 255, 255))
    return im

def sheet(out, rows, title):
    cols = max(len(r) for r in rows)
    S = Image.new("RGB", (cols * W, len(rows) * H + 40), (20, 20, 20))
    ImageDraw.Draw(S).text((10, 12), title, fill=(255, 255, 255))
    for j, r in enumerate(rows):
        for i, (n, lab) in enumerate(r):
            S.paste(tile(n, lab), (i * W, 40 + j * H))
    S.save(f"{D}/{out}", quality=88)
    print("wrote", f"{D}/{out}")

for group, names in [("biomes", POSES[:10]), ("giants", POSES[10:])]:
    sheet(f"sheet_{group}.jpg", [[(f"{n}_t2", f"{n}  -trees2 1 (new)"), (f"{n}_t0", f"{n}  -trees2 0 (today)")] for n in names],
          f"Task 210 {group}: left = -trees2 1 (space-colonization world trees), right = -trees2 0 (today's trees); same camera, noon, sun behind")

FINAL = ["jungle_eye", "jungle_100m", "banyan_near"]   # 210_final.sh: the final code (192's jungle trees at 14/16)
FAR = ["final_redwood_far600_farwait_t2", "final_redwood_far1500_farwait_t2"]   # shells given up to 200 s
shots = [f"sheet_biomes.jpg", "sheet_giants.jpg"] + [f"{n}_t2.png" for n in POSES] + [f"final_{n}_t2.png" for n in FINAL] + [f"{n}.png" for n in FAR]
pair = {f"{n}_t2.png": f"{n}_t0.png" for n in POSES}
pair.update({f"final_{n}_t2.png": f"final_{n}_t0.png" for n in FINAL})
for a, b in list(pair.items()): pair[b] = a
js_list = ",".join(f"'{s}'" for s in shots)
js_pair = ",".join(f"'{a}':'{b}'" for a, b in pair.items())
html = f"""<html><body style="background:#111;color:#ddd;font-family:sans-serif">
<h3>Task 210 world trees: arrows = next / previous pose, Space = flip -trees2 1 (new) / -trees2 0 (today) at the same camera, Z = full size</h3>
<div id=t></div><img id=i style="max-width:100%"><script>
var p=[{js_list}],q={{{js_pair}}},k=0,z=0,alt=0;
function s(){{var f=p[k];if(alt&&q[f])f=q[f];document.getElementById('i').src=f;document.getElementById('i').style.maxWidth=z?'none':'100%';
document.getElementById('t').textContent=(k+1)+'/'+p.length+'  '+f+(q[p[k]]?'   (Space: flip with '+q[p[k]]+')':'');}}
document.onkeydown=function(e){{if(e.key==' '){{alt=1-alt;}}else if(e.key=='ArrowRight'){{k=(k+1)%p.length;alt=0;}}else if(e.key=='ArrowLeft'){{k=(k+p.length-1)%p.length;alt=0;}}else if(e.key=='z'||e.key=='Z'){{z=1-z;}}s();e.preventDefault();}};s();
</script></body></html>"""
open(f"{D}/viewer.html", "w").write(html)
print("wrote", f"{D}/viewer.html")
