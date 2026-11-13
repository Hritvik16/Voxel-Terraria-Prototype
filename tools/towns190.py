#!/usr/bin/env python3
"""Task 190: label the town maps and site plan sheets.

Reads Logs/TerrainGen/towns190/ (TownTests.FourSeeds_TownsTable_Maps_Poses):
map_<seed>.png + sites_<seed>.txt (1 px = 8 m, north up; buildings dark brown),
map_7_before.png + sites_7_before.txt (task 187's plan, no spacing), plans_<seed>.png
(+ .txt: which site each 200 m tile shows) and table.txt (the spacing per seed).
Writes Screenshots/190/: towns_<seed>.png (sites labelled, every town's spacing
circle dashed orange), spacing_7_before_after.png (side by side), plans_<seed>.png
(tiles labelled) and towns_sheet.png (the four seeds, 2 x 2, half size).
"""
import math
import os
import re
import sys
from PIL import Image, ImageDraw, ImageFont

SRC = "Logs/TerrainGen/towns190"
DST = "Screenshots/190"
SEEDS = [42, 7, 2026, 31337]
CV = 80
LETTER = {"town": "T", "frontier": "F", "village": "V", "outpost": "O"}
COLOUR = {"town": (255, 140, 0), "frontier": (230, 20, 20), "village": (170, 230, 40), "outpost": (255, 255, 255)}


def font(sz):
    for f in ("/System/Library/Fonts/Supplemental/Arial Bold.ttf", "/System/Library/Fonts/Helvetica.ttc", "/Library/Fonts/Arial.ttf"):
        if os.path.exists(f):
            return ImageFont.truetype(f, sz)
    return ImageFont.load_default()


def spacing_m():
    sp = {}
    for line in open(f"{SRC}/table.txt"):
        m = re.match(r"\| (\d+) \|.*\| ([\d\-]+ -> [\d\-]+) \| (\d+) \|", line)
        if m:
            sp[int(m.group(1))] = int(m.group(3))
    return sp


def label(mapfile, sitefile, title, sep_m, buildings=True):
    im = Image.open(mapfile).convert("RGB")
    n = im.size[0]
    d = ImageDraw.Draw(im)
    f = font(20)
    counts = {}
    for line in open(sitefile):
        kind, sid, x, z, r = line.split()
        counts[kind] = counts.get(kind, 0) + 1
        px, py = int(x) // CV, n - 1 - int(z) // CV
        if kind == "town" and sep_m:
            rr = sep_m * 10 / CV
            for a in range(0, 360, 6):   # dashed circle
                a0, a1 = math.radians(a), math.radians(a + 3)
                d.line([(px + rr * math.cos(a0), py + rr * math.sin(a0)), (px + rr * math.cos(a1), py + rr * math.sin(a1))], fill=(255, 140, 0), width=2)
        rr = max(3, int(r) // CV) + 2
        text = f"{LETTER[kind]}{sid}"
        tx, ty = px + rr + 3, py - 11
        w = d.textlength(text, font=f)
        d.rectangle([tx - 2, ty - 1, tx + w + 2, ty + 22], fill=(0, 0, 0))
        d.text((tx, ty), text, font=f, fill=COLOUR[kind])
    fl = font(18)
    rows = [title] + [f"{LETTER[k]} = {k} ({counts.get(k, 0)})" for k in ("town", "frontier", "village", "outpost")]
    if sep_m:
        rows += [f"dashed = town spacing {sep_m} m"]
    if buildings:
        rows += ["dark brown dots = buildings"]
    rows += ["cyan = route corridor", "green = easy area", "yellow = start zone"]
    wbox = max(d.textlength(t, font=fl) for t in rows) + 16
    d.rectangle([6, 6, 6 + wbox, 12 + 22 * len(rows)], fill=(0, 0, 0))
    for k, t in enumerate(rows):
        kind = next((kk for kk in LETTER if t.startswith(LETTER[kk] + " =")), None)
        d.text((12, 10 + 22 * k), t, font=fl, fill=COLOUR[kind] if kind else (235, 235, 235))
    return im


def plans(seed):
    im = Image.open(f"{SRC}/plans_{seed}.png").convert("RGB")
    W, H = im.size
    T = 400
    d = ImageDraw.Draw(im)
    f = font(18)
    for line in open(f"{SRC}/plans_{seed}.png.txt"):
        m = re.match(r"tile (\d+) .*: (\w+) #(\d+)", line)
        if not m:
            continue
        k, kind, sid = int(m.group(1)), m.group(2), m.group(3)
        x0, y0 = (k % 4) * T, H - (k // 4 + 1) * T
        d.rectangle([x0, y0, x0 + T - 1, y0 + T - 1], outline=(0, 0, 0), width=2)
        text = f"{LETTER[kind]}{sid} {kind}"
        d.rectangle([x0 + 4, y0 + 4, x0 + 10 + d.textlength(text, font=f), y0 + 28], fill=(0, 0, 0))
        d.text((x0 + 7, y0 + 6), text, font=f, fill=COLOUR[kind])
    leg = f"seed {seed}: 200 m tiles, north up. Ground = natural height vs the pad (blue lower, red higher, 4 m full). White = disc edge, grey = ring edge. Hall red, house orange, hut yellow, door wall black. Cyan = route, yellow = stamp/start keep-out."
    out = Image.new("RGB", (W, H + 30), (0, 0, 0))
    out.paste(im, (0, 30))
    ImageDraw.Draw(out).text((8, 6), leg, font=font(15), fill=(235, 235, 235))
    out.save(f"{DST}/plans_{seed}.png")


def main():
    os.makedirs(DST, exist_ok=True)
    sp = spacing_m()
    ims = []
    for s in SEEDS:
        im = label(f"{SRC}/map_{s}.png", f"{SRC}/sites_{s}.txt", f"seed {s} (-towns 1)", sp.get(s))
        im.save(f"{DST}/towns_{s}.png")
        ims.append(im)
        plans(s)
    n = ims[0].size[0] // 2
    sheet = Image.new("RGB", (2 * n, 2 * n))
    for k, im in enumerate(ims):
        sheet.paste(im.resize((n, n), Image.LANCZOS), ((k % 2) * n, (k // 2) * n))
    sheet.save(f"{DST}/towns_sheet.png")
    b = label(f"{SRC}/map_7_before.png", f"{SRC}/sites_7_before.txt", "seed 7 BEFORE (task 187, no spacing)", sp.get(7), False)
    a = label(f"{SRC}/map_7.png", f"{SRC}/sites_7.txt", "seed 7 AFTER (task 190 spacing)", sp.get(7))
    pair = Image.new("RGB", (b.size[0] * 2 + 8, b.size[1]), (0, 0, 0))
    pair.paste(b, (0, 0)); pair.paste(a, (b.size[0] + 8, 0))
    pair.save(f"{DST}/spacing_7_before_after.png")
    print("wrote", DST)


if __name__ == "__main__":
    sys.exit(main())
