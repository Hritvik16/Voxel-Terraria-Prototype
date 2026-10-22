#!/usr/bin/env python3
"""shells80-sheet.py -- task 80's contact sheets from a -shells 1 -farpreview run.

  python3 tools/shells80-sheet.py <FarBand/..._world folder> Screenshots/80

Every pose was shot at noon three ways (PlaygroundCapture.ShellPoseShots):
  *_noon_A_off.png   shells OFF (the pre-11b frame: cascade tiers 1-2 + far band)
  *_noon_B_on.png    shells ON
  *_noon_C_tint.png  shells ON, tinted by what drew each pixel
Writes into the second directory:
  sheet_shells_<n>.jpg   rows = poses (5 per sheet); columns = OFF, ON, tint
and copies every shot there.
"""
import glob
import os
import shutil
import sys
from PIL import Image, ImageDraw, ImageFont

W, H = 640, 360
src_dir, out_dir = sys.argv[1], sys.argv[2]
os.makedirs(out_dir, exist_ok=True)
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


poses = sorted({os.path.basename(p)[: -len("_noon_A_off.png")]
                for p in glob.glob(os.path.join(src_dir, "*_noon_A_off.png"))},
               key=lambda n: os.path.getmtime(os.path.join(src_dir, n + "_noon_A_off.png")))
for p in glob.glob(os.path.join(src_dir, "*_noon_[ABC]_*.png")):
    shutil.copy2(p, out_dir)
for txt in glob.glob(os.path.join(src_dir, "*.txt")):
    shutil.copy2(txt, out_dir)

PER = 5
cols = [("A_off", "OFF (cascades + far band)"), ("B_on", "shells ON"), ("C_tint", "ON, tint by shell")]
for s in range(0, len(poses), PER):
    chunk = poses[s:s + PER]
    sheet = Image.new("RGB", (W * len(cols), H * len(chunk)), (0, 0, 0))
    for r, pose in enumerate(chunk):
        for c, (suffix, name) in enumerate(cols):
            sheet.paste(tile(os.path.join(src_dir, f"{pose}_noon_{suffix}.png"), f"{pose}: {name}"), (c * W, r * H))
    out = os.path.join(out_dir, f"sheet_shells_{s // PER + 1}.jpg")
    sheet.save(out, quality=85)
    print(out, "rows:", ", ".join(chunk))
