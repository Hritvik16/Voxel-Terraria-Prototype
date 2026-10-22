#!/usr/bin/env python3
"""river76-sheet.py -- task 76's contact sheets from -riverdemo shots.

  python3 tools/river76-sheet.py Screenshots/73 Screenshots/76

Writes into the second directory:
  sheet_near.jpg       rows = sites A-D, near (~12 m); columns = before (task 73's
                       generator), after with the look off (the new shape alone),
                       after with the look on (river look + see-through, as play)
  sheet_mid.jpg        the same from ~100 m
  sheet_distances.jpg  site A at ~20 m, ~150 m, ~800 m, ~2 km; row 1 look on, row 2 off
"""
import os
import sys
from PIL import Image, ImageDraw, ImageFont

W, H = 640, 360
before_dir, after_dir = sys.argv[1], sys.argv[2]
SITES = ["a_A_slope_reach", "b_B_river_into_lake", "c_C_waterfall", "d_D_river_mouth_sea"]
SITE_NAMES = ["A slope reach", "B river into lake", "C waterfall (10 m drop)", "D river mouth at sea"]

try:
    FONT = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial Bold.ttf", 20)
except OSError:
    FONT = ImageFont.load_default()


def tile(path, label):
    im = Image.new("RGB", (W, H), (40, 40, 40))
    if os.path.exists(path):
        src = Image.open(path).convert("RGB")
        src.thumbnail((W, H))
        im.paste(src, ((W - src.width) // 2, (H - src.height) // 2))
    else:
        label += " (missing)"
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W, 28], fill=(0, 0, 0))
    d.text((8, 4), label, fill=(255, 255, 255), font=FONT)
    return im


def sheet(rows, out):
    cols = max(len(r) for r in rows)
    im = Image.new("RGB", (cols * W, len(rows) * H), (0, 0, 0))
    for j, r in enumerate(rows):
        for i, (p, lab) in enumerate(r):
            im.paste(tile(p, lab), (i * W, j * H))
    im.save(out, quality=85)
    print("wrote", out)


for tag in ("near", "mid"):
    rows = []
    for s, name in zip(SITES, SITE_NAMES):
        rows.append([
            (os.path.join(before_dir, f"{s}_{tag}_off.png"), f"{name}: BEFORE (task 73)"),
            (os.path.join(after_dir, f"{s}_{tag}_off.png"), f"{name}: AFTER, look off"),
            (os.path.join(after_dir, f"{s}_{tag}_on.png"), f"{name}: AFTER, look on"),
        ])
    sheet(rows, os.path.join(after_dir, f"sheet_{tag}.jpg"))

dist = [("r020", "~20 m"), ("r150", "~150 m"), ("far", "~800 m"), ("far2km", "~2 km")]
rows = [[(os.path.join(after_dir, f"{SITES[0]}_{t}_{v}.png"), f"A {lab}, look {v}") for t, lab in dist] for v in ("on", "off")]
sheet(rows, os.path.join(after_dir, "sheet_distances.jpg"))
