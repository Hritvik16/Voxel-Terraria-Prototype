#!/usr/bin/env python3
"""farshade85b-sheet.py -- task 85b's contact sheets from a -farpreview -farshadeshots run.

  python3 tools/farshade85b-sheet.py <FarBand/..._world folder> Screenshots/85b

PlaygroundCapture.FarShadeShots names every shot
  <pose>_<lit|backlit|night>_<raw|fsr3>_<A_off|B_on>.png
Writes into the second directory:
  sheet_day_<raw|fsr3>.jpg   rows = pose x (lit, backlit); columns = far shading OFF, ON
  sheet_night.jpg            rows = night pose x (raw, fsr3); columns = OFF, ON
  farshade85b_stats.txt      per shot: mean luma, share of near-black pixels (luma < 20/255)
and copies every shot there.
"""
import glob
import os
import shutil
import sys
from PIL import Image, ImageDraw, ImageFont, ImageStat

W, H = 800, 450
src_dir, out_dir = sys.argv[1], sys.argv[2]
os.makedirs(out_dir, exist_ok=True)
try:
    FONT = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial Bold.ttf", 22)
except OSError:
    FONT = ImageFont.load_default()


def tile(path, label):
    im = Image.new("RGB", (W, H), (40, 40, 40))
    if os.path.exists(path):
        im = Image.open(path).convert("RGB").resize((W, H), Image.LANCZOS)
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W, 32], fill=(0, 0, 0))
    d.text((8, 4), label + ("" if os.path.exists(path) else "  (MISSING)"), fill=(255, 255, 255), font=FONT)
    return im


def sheet(rows, name):
    if not rows:
        return
    out = Image.new("RGB", (2 * W, H * len(rows)), (0, 0, 0))
    for r, (base, label) in enumerate(rows):
        for c, (suffix, col) in enumerate((("A_off", "OFF"), ("B_on", "ON"))):
            out.paste(tile(os.path.join(src_dir, f"{base}_{suffix}.png"), f"{label} -- far shading {col}"), (c * W, r * H))
    out.save(os.path.join(out_dir, name), quality=88)
    print("wrote", os.path.join(out_dir, name))


shots = sorted(glob.glob(os.path.join(src_dir, "*_A_off.png")) + glob.glob(os.path.join(src_dir, "*_B_on.png")))
for s in shots:
    shutil.copy(s, out_dir)
for t in glob.glob(os.path.join(src_dir, "*.txt")):
    shutil.copy(t, out_dir)

bases = sorted({os.path.basename(s).rsplit("_", 2)[0] for s in shots})
poses = []
for b in bases:
    p = b.rsplit("_", 2)[0]
    if p not in poses:
        poses.append(p)
for up in ("raw", "fsr3"):
    rows = [(f"{p}_{l}_{up}", f"{p} {l} {up}") for p in poses for l in ("lit", "backlit") if f"{p}_{l}_{up}" in bases]
    sheet(rows, f"sheet_day_{up}.jpg")
sheet([(f"{p}_night_{up}", f"{p} night {up}") for p in poses for up in ("raw", "fsr3") if f"{p}_night_{up}" in bases],
      "sheet_night.jpg")

with open(os.path.join(out_dir, "farshade85b_stats.txt"), "w") as f:
    f.write("shot                                         mean luma   near-black (<20/255) share\n")
    for s in shots:
        g = Image.open(s).convert("L")
        hist = g.histogram()
        n = sum(hist)
        f.write(f"{os.path.basename(s)[:-4]:44s} {ImageStat.Stat(g).mean[0]:8.1f}   {sum(hist[:20]) / n * 100:6.1f}%\n")
print("wrote", os.path.join(out_dir, "farshade85b_stats.txt"))
