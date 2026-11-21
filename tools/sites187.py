#!/usr/bin/env python3
"""Task 187: label the site planner's map renders.

Reads Logs/TerrainGen/sites187/map_<seed>.png (SiteTests.WriteMap, 1 px = one
8 m cell, north up) and sites_<seed>.txt ("kind id x z radius", voxels), writes
Screenshots/187/sites_<seed>.png (each site labelled T/F/V/O + id, a legend)
and Screenshots/187/sites_sheet.png (the four seeds, 2 x 2, half size).
"""
import os
import sys
from PIL import Image, ImageDraw, ImageFont

SRC = "Logs/TerrainGen/sites187"
DST = "Screenshots/187"
SEEDS = [42, 7, 2026, 31337]
CV = 80  # voxels per map cell
LETTER = {"town": "T", "frontier": "F", "village": "V", "outpost": "O"}
COLOUR = {"town": (255, 140, 0), "frontier": (230, 20, 20), "village": (170, 230, 40), "outpost": (255, 255, 255)}


def font(sz):
    for f in ("/System/Library/Fonts/Supplemental/Arial Bold.ttf", "/System/Library/Fonts/Helvetica.ttc",
              "/Library/Fonts/Arial.ttf"):
        if os.path.exists(f):
            return ImageFont.truetype(f, sz)
    return ImageFont.load_default()


def label(seed):
    im = Image.open(f"{SRC}/map_{seed}.png").convert("RGB")
    n = im.size[0]
    d = ImageDraw.Draw(im)
    f = font(20)
    counts = {}
    for line in open(f"{SRC}/sites_{seed}.txt"):
        kind, sid, x, z, r = line.split()
        counts[kind] = counts.get(kind, 0) + 1
        px, py = int(x) // CV, n - 1 - int(z) // CV
        rr = max(3, int(r) // CV) + 2
        text = f"{LETTER[kind]}{sid}"
        tx, ty = px + rr + 3, py - 11
        w = d.textlength(text, font=f)
        d.rectangle([tx - 2, ty - 1, tx + w + 2, ty + 22], fill=(0, 0, 0))
        d.text((tx, ty), text, font=f, fill=COLOUR[kind])
    # legend
    fl = font(18)
    rows = [f"seed {seed}"] + [f"{LETTER[k]} = {k} ({counts.get(k, 0)})" for k in ("town", "frontier", "village", "outpost")]
    rows += ["cyan = route corridor", "green = easy area", "yellow = start zone", "magenta = feature", "red tint = hard ground"]
    d.rectangle([6, 6, 250, 12 + 22 * len(rows)], fill=(0, 0, 0))
    for k, t in enumerate(rows):
        kind = next((kk for kk in LETTER if t.startswith(LETTER[kk] + " =")), None)
        d.text((12, 10 + 22 * k), t, font=fl, fill=COLOUR[kind] if kind else (235, 235, 235))
    os.makedirs(DST, exist_ok=True)
    im.save(f"{DST}/sites_{seed}.png")
    return im


def main():
    ims = [label(s) for s in SEEDS]
    n = ims[0].size[0] // 2
    sheet = Image.new("RGB", (2 * n, 2 * n))
    for k, im in enumerate(ims):
        sheet.paste(im.resize((n, n), Image.LANCZOS), ((k % 2) * n, (k // 2) * n))
    sheet.save(f"{DST}/sites_sheet.png")
    print("wrote", DST)


if __name__ == "__main__":
    sys.exit(main())
