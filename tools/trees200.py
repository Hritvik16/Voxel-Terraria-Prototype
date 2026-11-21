#!/usr/bin/env python3
"""Task 200: contact sheets + viewer.html for the tree gallery stills (Screenshots/200/engine/*.png).
   python3 -I tools/trees200.py   (run from the repo root)"""
import os
from PIL import Image, ImageDraw
D = "Screenshots/200"; E = D + "/engine"
W, H = 960, 540

def tile(name, label):
    p = f"{E}/{name}.png"
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

M = [("spacecol", "(a) space colonization"), ("lsystem", "(b) L-system / rule branching"), ("template", "(c) template + variation")]
sheet("sheet_methods.jpg",
      [[(f"m_oak_{m}", f"oak -- {l}") for m, l in M],
       [(f"m_banyan_{m}", f"banyan -- {l}") for m, l in M],
       [("m_today", "TODAY's tree (Trees.cs, task 192) -- same framing")]],
      "Task 200 METHOD sheet: same camera per tile (75 m back, 6 m up), 3 trees each, noon, sun behind; red post = 1.8 m person")
sheet("sheet_species.jpg",
      [[("s_oak_spacecol", "oak (forest)"), ("s_banyan_spacecol", "banyan (jungle)"), ("s_redwood_spacecol", "redwood (temperate rain forest)")],
       [("s_birch_spacecol", "birch (forest / snow edge)"), ("s_spruce_spacecol", "spruce (snow)"), ("s_acacia_spacecol", "acacia (savanna)")],
       [("s_today", "TODAY's tree -- same framing")]],
      "Task 200 SPECIES sheet, method (a) space colonization: same camera per tile (95 m back, 8 m up), 3 unique trees each; red post = 1.8 m")
sheet("sheet_closeup.jpg", [[("c_oak_spacecol", "oak crown, space colonization: leaf clumps with air between"), ("c_today", "TODAY's crown, same framing")]],
      "Task 200 crown close-up: eye 21 m from the trunk, 9 m up")

shots = ["sheet_methods.jpg", "sheet_species.jpg", "sheet_closeup.jpg"] + \
        [f"engine/{n}.png" for n in ["m_oak_spacecol", "m_oak_lsystem", "m_oak_template", "m_banyan_spacecol", "m_banyan_lsystem", "m_banyan_template", "m_today",
                                     "s_oak_spacecol", "s_banyan_spacecol", "s_redwood_spacecol", "s_birch_spacecol", "s_spruce_spacecol", "s_acacia_spacecol", "s_today",
                                     "c_oak_spacecol", "c_today"]] + \
        [f"proto_{s}.png" for s in ["oak", "banyan", "redwood", "birch", "spruce", "acacia"]]
pair = {"engine/c_oak_spacecol.png": "engine/c_today.png", "engine/m_oak_spacecol.png": "engine/m_today.png", "engine/s_oak_spacecol.png": "engine/s_today.png"}
for a, b in list(pair.items()): pair[b] = a
js_list = ",".join(f"'{s}'" for s in shots)
js_pair = ",".join(f"'{a}':'{b}'" for a, b in pair.items())
html = f"""<html><body style="background:#111;color:#ddd;font-family:sans-serif">
<h3>Task 200 tree examples: arrows = next / previous, Space = flip to TODAY's tree at the same framing (where there is one), Z = full size</h3>
<div id=t></div><img id=i style="max-width:100%"><script>
var p=[{js_list}],q={{{js_pair}}},k=0,z=0,alt=0;
function s(){{var f=p[k];if(alt&&q[f])f=q[f];document.getElementById('i').src=f;document.getElementById('i').style.maxWidth=z?'none':'100%';
document.getElementById('t').textContent=(k+1)+'/'+p.length+'  '+f+(q[p[k]]?'   (Space: flip with '+q[p[k]]+')':'');}}
document.onkeydown=function(e){{if(e.key==' '){{alt=1-alt;}}else if(e.key=='ArrowRight'){{k=(k+1)%p.length;alt=0;}}else if(e.key=='ArrowLeft'){{k=(k+p.length-1)%p.length;alt=0;}}else if(e.key=='z'||e.key=='Z'){{z=1-z;}}s();e.preventDefault();}};s();
</script></body></html>"""
open(f"{D}/viewer.html", "w").write(html)
print("wrote", f"{D}/viewer.html")
