#!/usr/bin/env python3
"""upscaler-compare-sheets.py <stills dir> [crop spec ...] -- task 48's contact
sheets from -upscalercompare's stills (<pose>__<arm>.png, 1920x1080).
  sheet_540.png / sheet_720.png / sheet_dyn.png: rows = poses, columns =
      native / taa / fsr1 / stp / fsr3 at that resolution (native in each).
  sheet_crops.png: crop specs "pose:x:y" (top-left, screen pixels) of a
      160x90 window shown 3x (nearest), rows = pose x {540, 720}.
Writes into the stills dir."""
import os, sys
from PIL import Image, ImageDraw
COLS = ["native", "taa", "fsr1", "stp", "fsr3"]
POSES = ["spawn_day", "near_cliff", "horizon", "slope_detail", "fluid_pools", "night_torches"]
TW, TH, LAB = 480, 270, 22
def arm(c, res): return "native" if c == "native" else f"{c}_{res}"
def label(d, xy, s): d.rectangle([xy[0], xy[1], xy[0] + 8 * len(s) + 8, xy[1] + 16], fill=(0, 0, 0)); d.text((xy[0] + 4, xy[1] + 2), s, fill=(255, 255, 0))
def sheet(src, res, poses):
    im = Image.new("RGB", (TW * len(COLS), LAB + TH * len(poses)), (40, 40, 40))
    d = ImageDraw.Draw(im)
    for ci, c in enumerate(COLS): label(d, (ci * TW + 4, 3), arm(c, res))
    for ri, p in enumerate(poses):
        for ci, c in enumerate(COLS):
            f = os.path.join(src, f"{p}__{arm(c, res)}.png")
            if os.path.exists(f): im.paste(Image.open(f).convert("RGB").resize((TW, TH), Image.LANCZOS), (ci * TW, LAB + ri * TH))
        label(d, (4, LAB + ri * TH + 4), p)
    out = os.path.join(src, f"sheet_{res}.png"); im.save(out); print(out)
def crops(src, specs):
    rows = [(p, int(x), int(y), res) for p, x, y in (s.split(":") for s in specs) for res in ("540", "720")]
    W, H, Z = 160, 90, 3
    im = Image.new("RGB", (W * Z * len(COLS), LAB + H * Z * len(rows)), (40, 40, 40))
    d = ImageDraw.Draw(im)
    for ci, c in enumerate(COLS): label(d, (ci * W * Z + 4, 3), c)
    for ri, (p, x, y, res) in enumerate(rows):
        for ci, c in enumerate(COLS):
            f = os.path.join(src, f"{p}__{arm(c, res)}.png")
            if os.path.exists(f):
                im.paste(Image.open(f).convert("RGB").crop((x, y, x + W, y + H)).resize((W * Z, H * Z), Image.NEAREST), (ci * W * Z, LAB + ri * H * Z))
        label(d, (4, LAB + ri * H * Z + 4), f"{p} {res} rows  crop ({x},{y}) 160x90 x3")
    out = os.path.join(src, "sheet_crops.png"); im.save(out); print(out)
if __name__ == "__main__":
    src = sys.argv[1]
    poses = [p for p in POSES if os.path.exists(os.path.join(src, f"{p}__native.png"))]
    for res in ("540", "720", "dyn"): sheet(src, res, poses)
    if len(sys.argv) > 2: crops(src, sys.argv[2:])
