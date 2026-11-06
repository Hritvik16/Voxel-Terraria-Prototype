#!/usr/bin/env python3
"""cover177.py -- task 177 (cover palettes): maps, palette table and viewer.

  python3 -I tools/cover177.py maps    <out dir>                 island maps from Logs/TerrainGen/c2c3/cover_seed*.bin
  python3 -I tools/cover177.py palette <out dir>                 the biome x slot table (swatches), parsed from Cover.cs
  python3 -I tools/cover177.py shots   <out dir> <after dir> <before dir>   before|after pairs of the fixed poses
  python3 -I tools/cover177.py viewer  <out dir>                 viewer.html over everything in <out dir>

The .bin planes (CoverTests.Report_CoverMaps, REPORTS=1): 512^2 x 4 bytes =
cover (slot | 8 variant, 255 = water), surface biome, top material with cover
slots, top material with -coverslots 0.
"""
import json
import os
import re
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from palette_defs import PALETTES, NAMES  # noqa: E402

SEEDS = [42, 7, 2026, 31337]
BIOMES = ["forest", "desert", "snow", "jungle", "savanna", "badlands"]
SLOTS = ["flat", "steep", "hollow", "high", "shore"]
SLOT_RGB = {0: (120, 190, 90), 1: (90, 80, 75), 2: (60, 140, 220), 3: (250, 250, 255), 4: (235, 205, 120)}
# Task 177b: BARE shows 178's thick layers, DetailField.LAYER_ROCKS (main ~50 %, light ~30 %, dark ~20 %).
BARE_ROCK = {0: [33, 30, 31], 1: [42, 40, 41], 2: [54, 51, 50], 3: [60, 62, 63], 4: [35, 37, 36], 5: [45, 46, 47]}
BARE_SHARE = [0.5, 0.3, 0.2]
D = PALETTES["D"][2]
N = 512


def font(sz):
    for f in ("/System/Library/Fonts/Helvetica.ttc", "/System/Library/Fonts/SFNS.ttf"):
        try:
            return ImageFont.truetype(f, sz)
        except OSError:
            pass
    return ImageFont.load_default()


def rgb(g):
    c = D.get(g)
    return (255, 0, 255) if c is None else tuple(int(round(v * 255)) for v in c)


def genmat_ids():
    src = open(os.path.join(ROOT, "Assets/TerrainGen/GenMat.cs")).read()
    src = src[:src.index("class GenBiome")]   # GenBiome.Snow = 2 must not shadow GenMat.Snow
    ids = {}
    for decl in re.findall(r"public const byte ([^;]+);", src):
        for part in decl.split(","):
            m = re.match(r"\s*(\w+)\s*=\s*(\d+)", part)
            if m:
                ids[m.group(1)] = int(m.group(2))
    return ids


def palette_rows():
    """[(biome, [10 values: material id or 'BARE'])] from Cover.PALETTE."""
    src = open(os.path.join(ROOT, "Assets/TerrainGen/Cover.cs")).read()
    body = src[src.index("PALETTE = {"):]
    body = body[:body.index("};")]
    body = re.sub(r"//[^\n]*", "", body)
    toks = re.findall(r"GenMat\.(\w+)|\b(BARE)\b", body)
    ids = genmat_ids()
    vals = [ids[a] if a else "BARE" for a, b in toks]
    assert len(vals) == 60, len(vals)
    return [(BIOMES[b], vals[b * 10:(b + 1) * 10]) for b in range(6)]


def load(seed):
    a = np.fromfile(os.path.join(ROOT, f"Logs/TerrainGen/c2c3/cover_seed{seed}.bin"), dtype=np.uint8).reshape(N, N, 4)
    return a[::-1]   # +z up


def slot_img(a):
    out = np.zeros((N, N, 3), np.uint8)
    out[:] = (30, 50, 90)
    cov = a[:, :, 0]
    land = cov != 255
    for s, c in SLOT_RGB.items():
        m = land & ((cov & 7) == s)
        col = np.array(c, float)
        out[m & ((cov & 8) == 0)] = col.astype(np.uint8)
        out[m & ((cov & 8) != 0)] = (col * 0.78).astype(np.uint8)
    return out


def mat_img(a, k):
    lut = np.zeros((256, 3), np.uint8)
    for g in range(256):
        lut[g] = rgb(g) if g in D else (255, 0, 255)
    out = lut[a[:, :, k]]
    out[a[:, :, 0] == 255] = (30, 50, 90)
    return out


def label(img, text, xy=(8, 6), sz=18):
    d = ImageDraw.Draw(img)
    f = font(sz)
    x, y = xy
    d.rectangle([x - 4, y - 2, x + d.textlength(text, font=f) + 4, y + sz + 4], fill=(0, 0, 0))
    d.text((x, y), text, fill=(255, 255, 255), font=f)


def maps(out):
    os.makedirs(out, exist_ok=True)
    stats = []
    for s in SEEDS:
        a = load(s)
        panels = [("SLOTS (darker = patch variant)", slot_img(a)), ("AFTER: cover material", mat_img(a, 2)), ("BEFORE: -coverslots 0", mat_img(a, 3))]
        sheet = Image.new("RGB", (3 * N + 2 * 6, N + 40), (20, 20, 20))
        for i, (t, im) in enumerate(panels):
            sheet.paste(Image.fromarray(im), (i * (N + 6), 40))
            label(sheet, t, (i * (N + 6) + 8, 10))
        # legend on the slot panel
        lg = ImageDraw.Draw(sheet)
        f = font(14)
        for i, sl in enumerate(SLOTS):
            y = 52 + i * 20
            lg.rectangle([8, y, 24, y + 14], fill=SLOT_RGB[i])
            lg.rectangle([26, y, 42, y + 14], fill=tuple(int(c * 0.78) for c in SLOT_RGB[i]))
            lg.text((48, y - 1), sl, fill=(255, 255, 255), font=f, stroke_width=2, stroke_fill=(0, 0, 0))
        sheet.save(os.path.join(out, f"map_seed{s}.png"))
        big = Image.fromarray(np.kron(slot_img(a), np.ones((2, 2, 1), np.uint8)))
        big.save(os.path.join(out, f"slots_seed{s}_1024.png"))
        cov = a[:, :, 0]
        land = cov != 255
        stats.append(f"seed {s}: " + ", ".join(f"{sl} {100.0 * np.sum(land & ((cov & 7) == i)) / max(1, land.sum()):.1f} %" for i, sl in enumerate(SLOTS)))
    open(os.path.join(out, "map_slot_shares.txt"), "w").write("\n".join(stats) + "\n")
    print("\n".join(stats))


def palette(out):
    os.makedirs(out, exist_ok=True)
    rows = palette_rows()
    cw, ch, lw = 200, 76, 120
    img = Image.new("RGB", (lw + cw * 5, 50 + ch * 6), (24, 24, 24))
    d = ImageDraw.Draw(img)
    f, fs = font(17), font(13)
    for j, sl in enumerate(SLOTS):
        d.text((lw + j * cw + 8, 14), sl.upper() + "  (main | variant)", fill=(255, 255, 255), font=f)
    txt = ["| biome | " + " | ".join(SLOTS) + " |", "|---|" + "---|" * 5]
    for i, (b, v) in enumerate(rows):
        y = 50 + i * ch
        d.text((8, y + 26), b, fill=(255, 255, 255), font=f)
        cells = []
        for j in range(5):
            names = []
            for k in range(2):
                g = v[j * 2 + k]
                x = lw + j * cw + k * (cw // 2)
                if g == "BARE":
                    rocks = BARE_ROCK[BIOMES.index(b)]
                    # horizontal thick layers, heights by role share (main, light, dark)
                    yy = y + 4
                    for r, rg in enumerate(rocks):
                        hgt = round(40 * BARE_SHARE[r]) if r < len(rocks) - 1 else y + 44 - yy
                        d.rectangle([x + 4, yy, x + cw // 2 - 4, yy + hgt], fill=rgb(rg))
                        yy += hgt
                    for t in range(0, 40, 8):
                        d.line([x + 4, y + 4 + t, x + 4 + t, y + 4], fill=(0, 0, 0), width=1)
                    nm = "bare: " + NAMES[rocks[0]] + " +2"
                else:
                    d.rectangle([x + 4, y + 4, x + cw // 2 - 4, y + 44], fill=rgb(g))
                    nm = NAMES[g]
                d.text((x + 4, y + 48), nm[:18], fill=(220, 220, 220), font=fs)
                names.append(nm)
            same = names[0] == names[1]
            cells.append(names[0] if same else f"{names[0]} / {names[1]}")
        txt.append(f"| {b} | " + " | ".join(cells) + " |")
    img.save(os.path.join(out, "palette177.png"))
    open(os.path.join(out, "palette177.md"), "w").write("\n".join(txt) + "\n")
    print("\n".join(txt))


POSES = [("start_view", "the play start from 160 m back"), ("desert_alpine-range", "173's desert x alpine range camera"),
         ("forest_river-plains", "173's forest x river plains camera (the 'lawn')"), ("great-peak-0", "the great peak from 1.6 km"),
         ("range_200up", "the 600 m summit from 1.5 km, 200 m up")]


def shots(out, after, before):
    os.makedirs(out, exist_ok=True)
    for p, _ in POSES:
        for tag, d in (("after", after), ("before", before)):
            src = os.path.join(d, f"ws_fixed_{p}_native.png")
            if os.path.exists(src):
                im = Image.open(src).convert("RGB")
                label(im, f"{tag.upper()}  {p}", (12, 10), 26)
                im.save(os.path.join(out, f"shot_{p}_{tag}.png"))
            else:
                print("missing", src)


def viewer(out):
    items = []
    for p, cap in POSES:
        a, b = f"shot_{p}_after.png", f"shot_{p}_before.png"
        if os.path.exists(os.path.join(out, a)):
            items.append([a, b, f"{p}: {cap}. FULL SIZE; Space flips BEFORE (-coverslots 0) | AFTER. Look at the ground: speckle gone, rock on steep ground, gravel/sand in hollows and by water, high-slot cover on the high ground."])
    items.append(["palette177.png", None, "THE PALETTE: biome rows x slot columns, each cell main | patch variant. Hatched = BARE (the biome's own rock shows). Change any cell?"])
    for s in SEEDS:
        items.append([f"map_seed{s}.png", None, f"seed {s} island (1 px = 16 m): left SLOTS (green flat, dark grey steep, blue hollow, white high, sand shore; darker = variant) | middle AFTER cover | right BEFORE."])
        items.append([f"slots_seed{s}_1024.png", None, f"seed {s} slot map, 2x (1 px = 16 m). Steep follows ridges and valley sides; white high only on the top third of the height range."])
    page = '''<!doctype html><meta charset=utf-8><title>Task 177 cover palettes</title>
<style>body{margin:0;background:#222;color:#eee;font:14px sans-serif}#cap{position:fixed;top:0;left:0;right:0;background:#000c;padding:8px;z-index:2}
#box{position:absolute;top:70px;left:0;right:0;bottom:0;overflow:auto}img{display:block;image-rendering:pixelated}</style>
<div id=cap></div><div id=box><img id=im></div><script>
const L=%s;let i=0,z=0,alt=0;const im=document.getElementById('im'),cap=document.getElementById('cap');
function show(){const it=L[i];const src=(alt&&it[1])?it[1]:it[0];im.src=src;cap.textContent=(i+1)+'/'+L.length+'  '+src+(it[1]?(alt?'  [BEFORE]':'  [AFTER]'):'')+' -- '+it[2]+'   [arrows: image, Space: before/after, 1 fit, 2 100%%, 3 200%%, 4 300%%]';
im.onload=()=>{if(z==0){im.style.width='100%%';im.style.height='auto'}else{im.style.width=(im.naturalWidth*z)+'px'}}; im.onload();}
document.onkeydown=e=>{if(e.key=='ArrowRight'||e.key=='ArrowDown'){i=(i+1)%%L.length;alt=0}else if(e.key=='ArrowLeft'||e.key=='ArrowUp'){i=(i+L.length-1)%%L.length;alt=0}
else if(e.key==' ')alt^=1;else if('1234'.includes(e.key))z=+e.key-1;else return;e.preventDefault();show()};show();</script>''' % json.dumps(items)
    open(os.path.join(out, "viewer.html"), "w").write(page)
    print("wrote", os.path.join(out, "viewer.html"))


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "maps":
        maps(sys.argv[2])
    elif cmd == "palette":
        palette(sys.argv[2])
    elif cmd == "shots":
        shots(sys.argv[2], sys.argv[3], sys.argv[4])
    elif cmd == "viewer":
        viewer(sys.argv[2])
