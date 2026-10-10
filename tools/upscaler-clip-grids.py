#!/usr/bin/env python3
"""upscaler-clip-grids.py <path dir> <grid out dir> <crop out dir> -- task 50.
<path dir> holds one folder per arm (-upscalerclips: <arm>/f0000.jpg ..., 1920x1080).
Writes, frame by frame (JPEG q95, for tools/frames-to-mp4):
  grid: 3x2 of all arms, each tile scaled to 960x540 (2880x1080), arm label
        top-left, the clip time on the first tile.
  crop: 3x2 of 1:1 pixel crops, 640x360 each (1920x720), NOT rescaled, labelled.
        The window is picked once per path: the 640x360 window with the most
        edge (mean |gradient|) in the native arm, over 10 frames spread across
        the clip -- where the cliff edge, the ridge silhouette or the torch light is.
Writes crop.json (window, arms, frame count) into <path dir>. Exits non-zero if an
arm is missing or the arms disagree on the frame count."""
import json, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ARMS = ["native", "taa_540", "fsr1_540", "stp_540", "fsr3_dyn", "fsr3_720"]
W, H, TW, TH, CW, CH = 1920, 1080, 960, 540, 640, 360
FPS = 30

def font(sz):
    for f in ("/System/Library/Fonts/SFNSMono.ttf", "/System/Library/Fonts/Menlo.ttc", "/System/Library/Fonts/Helvetica.ttc"):
        try: return ImageFont.truetype(f, sz)
        except OSError: pass
    return ImageFont.load_default()

def label(d, x, y, s, f):
    l, t, r, b = d.textbbox((0, 0), s, font=f)
    d.rectangle([x, y, x + r - l + 12, y + b - t + 10], fill=(0, 0, 0))
    d.text((x + 6 - l, y + 5 - t), s, font=f, fill=(255, 230, 0))

def pick_crop(src, n):
    acc = None
    for i in np.linspace(0, n - 1, 10).astype(int):
        g = np.asarray(Image.open(os.path.join(src, "native", f"f{i:04d}.jpg")).convert("L"), dtype=np.float32)
        e = np.zeros_like(g)
        e[:, 1:] += np.abs(np.diff(g, axis=1)); e[1:, :] += np.abs(np.diff(g, axis=0))
        acc = e if acc is None else acc + e
    s = acc.reshape(H // 8, 8, W // 8, 8).sum(axis=(1, 3))          # 240x135 cells of 8 px
    ii = np.pad(s.cumsum(0).cumsum(1), ((1, 0), (1, 0)))
    cw, ch = CW // 8, CH // 8
    best, bx, by = -1.0, 0, 0
    for y in range(0, H // 8 - ch + 1):
        for x in range(0, W // 8 - cw + 1):
            v = ii[y + ch, x + cw] - ii[y, x + cw] - ii[y + ch, x] + ii[y, x]
            if v > best: best, bx, by = v, x, y
    return bx * 8, by * 8

def main():
    src, gdir, cdir = sys.argv[1:4]
    counts = {}
    for a in ARMS:
        d = os.path.join(src, a)
        counts[a] = len([f for f in os.listdir(d) if f.endswith(".jpg")]) if os.path.isdir(d) else 0
    n = min(counts.values())
    if n == 0 or len(set(counts.values())) != 1:
        print(f"frame counts differ or zero: {counts}"); sys.exit(1)
    cx, cy = pick_crop(src, n)
    json.dump({"crop_x": cx, "crop_y": cy, "w": CW, "h": CH, "arms": ARMS, "frames": n}, open(os.path.join(src, "crop.json"), "w"))
    print(f"{os.path.basename(src.rstrip('/'))}: {n} frames per arm, crop window x {cx}..{cx + CW} y {cy}..{cy + CH}")
    os.makedirs(gdir, exist_ok=True); os.makedirs(cdir, exist_ok=True)
    fl, fs = font(30), font(24)
    for i in range(n):
        grid = Image.new("RGB", (3 * TW, 2 * TH))
        crop = Image.new("RGB", (3 * CW, 2 * CH))
        gd, cd = ImageDraw.Draw(grid), ImageDraw.Draw(crop)
        for k, a in enumerate(ARMS):
            im = Image.open(os.path.join(src, a, f"f{i:04d}.jpg")).convert("RGB")
            if im.size != (W, H): print(f"{a} f{i:04d} is {im.size}"); sys.exit(1)
            gx, gy = (k % 3) * TW, (k // 3) * TH
            grid.paste(im.resize((TW, TH), Image.LANCZOS), (gx, gy))
            label(gd, gx + 8, gy + 8, a + (f"   t {i / FPS:4.1f} s" if k == 0 else ""), fl)
            ox, oy = (k % 3) * CW, (k // 3) * CH
            crop.paste(im.crop((cx, cy, cx + CW, cy + CH)), (ox, oy))
            label(cd, ox + 6, oy + 6, a + (f"   t {i / FPS:4.1f} s" if k == 0 else ""), fs)
        grid.save(os.path.join(gdir, f"f{i:04d}.jpg"), quality=95)
        crop.save(os.path.join(cdir, f"f{i:04d}.jpg"), quality=95)

if __name__ == "__main__":
    main()
