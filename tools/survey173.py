#!/usr/bin/env python3
"""Task 173: world-gen look survey -> annotated map, half-size contact sheet, full-size viewer.

usage: python3 -I tools/survey173.py <out dir> <findings.json|-> <LookReview dir> [<LookReview dir 2> ...]
A later dir's poses REPLACE every earlier pose of the same kind (the name up to its first "_"), so a reshoot
of some kinds merges over the first launch.

Reads <dir>/lookreview.txt (WSPOSE name|eye|target|note lines, from -worldsurvey 1) and the
ws_<pose>_<arm>.png shots. Writes into <out dir>:
  survey173_map.png   worldsurvey_map.png x2 with every pose: eye dot, view line, number
  survey173_sheet_<k>.png   contact sheets, 2 tiles across at 960 px (native arm)
  shots/              copies of every shot (git-ignored images)
  viewer.html         full-size viewer: arrows = next/prev pose, Space = flip native / FSR 3 540,
                      1-4 = zoom 100-300 % / fit, drag to pan; caption + finding per pose
  survey173.txt       pose table
findings.json (optional): {"<pose>": "what to look at"} shown as each pose's caption.
"""
import html
import json
import os
import shutil
import sys

from PIL import Image, ImageDraw, ImageFont


def font(sz):
    for p in ("/System/Library/Fonts/Helvetica.ttc", "/System/Library/Fonts/Supplemental/Arial.ttf"):
        if os.path.exists(p):
            return ImageFont.truetype(p, sz)
    return ImageFont.load_default()


def main():
    out, fj, srcs = sys.argv[1], sys.argv[2], sys.argv[3:]
    findings = {}
    if fj != "-" and os.path.exists(fj):
        with open(fj) as f:
            findings = json.load(f)
    os.makedirs(os.path.join(out, "shots"), exist_ok=True)
    poses = []
    cell_m = 8.0
    arms = ["native", "fsr3_540", "native_lit"]
    for src in srcs:
        new = []
        for line in open(os.path.join(src, "lookreview.txt")):
            line = line.strip()
            if line.startswith("WSPOSE "):
                name, eye, at, note = line[7:].split("|", 3)
                p = dict(name=name, eye=[float(v) for v in eye.split(",")], at=[float(v) for v in at.split(",")], note=note, shots={})
                for a in arms:
                    f = os.path.join(src, f"ws_{name}_{a}.png")
                    if os.path.exists(f):
                        shutil.copy(f, os.path.join(out, "shots", os.path.basename(f)))
                        p["shots"][a] = "shots/" + os.path.basename(f)
                new.append(p)
        kinds = {p["name"].split("_")[0] for p in new}
        poses = [p for p in poses if p["name"].split("_")[0] not in kinds] + new
    order = ["start", "pair", "feature", "coast", "range", "river", "cliff", "cave", "tunnel", "cavern"]
    poses.sort(key=lambda p: order.index(p["name"].split("_")[0]) if p["name"].split("_")[0] in order else 99)
    src = srcs[0]

    # ---- annotated map ----
    mp = os.path.join(srcs[-1], "worldsurvey_map.png")
    if os.path.exists(mp):
        m = Image.open(mp).convert("RGB")
        n = m.width
        m = m.resize((n * 2, n * 2), Image.NEAREST)
        d = ImageDraw.Draw(m)
        fnt = font(20)
        def px(x, z):
            return (x / cell_m * 2, (n - z / cell_m) * 2)
        for i, p in enumerate(poses, 1):
            e = px(p["eye"][0], p["eye"][2]); t = px(p["at"][0], p["at"][2])
            d.line([e, t], fill=(255, 40, 40), width=3)
            d.ellipse([e[0] - 6, e[1] - 6, e[0] + 6, e[1] + 6], fill=(255, 255, 255), outline=(0, 0, 0), width=2)
            d.text((e[0] + 8, e[1] - 10), str(i), fill=(255, 255, 255), font=fnt, stroke_width=3, stroke_fill=(0, 0, 0))
        m.save(os.path.join(out, "survey173_map.png"))

    # ---- contact sheets: 2 across at 960 px, 3 rows per sheet ----
    fnt = font(22)
    tiles = [p for p in poses if "native" in p["shots"]]
    per = 6
    for s in range(0, len(tiles), per):
        chunk = tiles[s:s + per]
        rows = (len(chunk) + 1) // 2
        sheet = Image.new("RGB", (1920, rows * (540 + 34)), (20, 20, 20))
        d = ImageDraw.Draw(sheet)
        for k, p in enumerate(chunk):
            im = Image.open(os.path.join(out, p["shots"]["native"])).convert("RGB").resize((960, 540), Image.LANCZOS)
            x, y = (k % 2) * 960, (k // 2) * 574
            sheet.paste(im, (x, y + 34))
            d.text((x + 8, y + 4), f"{poses.index(p) + 1}. {p['name']}", fill=(255, 255, 255), font=fnt)
        sheet.save(os.path.join(out, f"survey173_sheet_{s // per + 1}.png"))

    # ---- table ----
    with open(os.path.join(out, "survey173.txt"), "w") as f:
        for i, p in enumerate(poses, 1):
            f.write(f"{i:2d} {p['name']:<34} eye ({p['eye'][0]:.0f}, {p['eye'][1]:.0f}, {p['eye'][2]:.0f}) "
                    f"-> ({p['at'][0]:.0f}, {p['at'][1]:.0f}, {p['at'][2]:.0f})  {p['note']}\n")

    # ---- viewer ----
    data = [dict(n=f"{i}. {p['name']}", note=p["note"], find=findings.get(p["name"], ""),
                 s=[[a, p["shots"][a]] for a in arms if a in p["shots"]])
            for i, p in enumerate(poses, 1) if p["shots"]]
    page = """<!doctype html><html><head><meta charset="utf-8"><title>Task 173 world-gen survey</title><style>
body{margin:0;background:#111;color:#ddd;font:14px -apple-system,Helvetica,sans-serif}
#bar{position:fixed;top:0;left:0;right:0;background:#000c;padding:6px 10px;z-index:2}
#bar b{color:#fff;font-size:16px} #find{color:#ffd27a;margin-top:3px} #note{color:#999;font-size:12px}
#wrap{position:absolute;top:92px;left:0;right:0;bottom:0;overflow:auto;cursor:grab}
#img{display:block;image-rendering:pixelated} #arm{color:#7fd;font-weight:bold}
#list{position:fixed;right:6px;top:96px;background:#000b;max-height:80vh;overflow:auto;padding:4px;font-size:12px;z-index:3}
#list div{cursor:pointer;padding:1px 4px} #list div.on{background:#345}
</style></head><body><div id="bar"><b id="t"></b> &nbsp; <span id="arm"></span> &nbsp;
<span style="color:#888">&larr;/&rarr; pose &middot; Space cycle native / FSR 3 540 (/ shape light) &middot; 1 fit &middot; 2 100% &middot; 3 200% &middot; 4 300% &middot; L list</span>
<div id="find"></div><div id="note"></div></div><div id="wrap"><img id="img"></div><div id="list"></div>
<script>
const D=__DATA__;let i=0,arm=0,zoom=0;const img=document.getElementById('img'),wrap=document.getElementById('wrap');
const AN={native:'native 1080 (no upscaler)',fsr3_540:'FSR 3 at 540 rows (play)',native_lit:'native + capture-only SHAPE light (cave ambient floor 1.0)'};
function show(){const p=D[i];const k=arm%p.s.length;img.src=p.s[k][1];
document.getElementById('t').textContent=p.n;document.getElementById('arm').textContent=AN[p.s[k][0]]+'  ('+(k+1)+'/'+p.s.length+')';
document.getElementById('find').textContent=p.find;document.getElementById('note').textContent=p.note;
img.style.width=zoom?(1920*zoom)+'px':'100%';[...document.querySelectorAll('#list div')].forEach((d,k)=>d.className=k==i?'on':'');}
const L=document.getElementById('list');D.forEach((p,k)=>{const d=document.createElement('div');d.textContent=p.n;d.onclick=()=>{i=k;show()};L.appendChild(d)});
document.onkeydown=e=>{if(e.key=='ArrowRight'){i=(i+1)%D.length;arm=0}else if(e.key=='ArrowLeft'){i=(i+D.length-1)%D.length;arm=0}
else if(e.key==' '){arm++;e.preventDefault()}else if('1234'.includes(e.key)){zoom=+e.key-1}
else if(e.key=='l'||e.key=='L'){L.style.display=L.style.display=='none'?'':'none'}else return;show()};
let dr=null;wrap.onmousedown=e=>{dr=[e.clientX,e.clientY,wrap.scrollLeft,wrap.scrollTop];e.preventDefault()};
onmouseup=()=>dr=null;onmousemove=e=>{if(dr){wrap.scrollLeft=dr[2]-(e.clientX-dr[0]);wrap.scrollTop=dr[3]-(e.clientY-dr[1])}};show();
</script></body></html>"""
    with open(os.path.join(out, "viewer.html"), "w") as f:
        f.write(page.replace("__DATA__", json.dumps(data)))
    print(f"{len(poses)} poses, {sum(len(p['shots']) for p in poses)} shots -> {out}")


if __name__ == "__main__":
    main()
