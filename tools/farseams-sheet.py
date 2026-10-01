#!/usr/bin/env python3
"""farseams-sheet.py <seams folder> [out folder]

For every <name>_A.png / _C_band.png / _E_stop.png triple written by
-farseams: a side-by-side JPEG (beauty | band | voxel-stop) and exact pixel
fractions per class, counted from the debug images by nearest palette colour
(both linear and sRGB-encoded palettes are tried, so it does not matter which
the blit wrote).
"""
import glob
import os
import sys

import numpy as np
from PIL import Image

BAND = {"sky": (0.02, 0.02, 0.03), "voxel": (0.15, 0.35, 0.90), "F0": (0.15, 0.80, 0.25),
        "F1": (0.90, 0.90, 0.15), "F2": (0.95, 0.55, 0.10), "F3": (0.90, 0.15, 0.15), "F4": (0.85, 0.20, 0.85)}
STOP = {"sky": (0.25, 0.25, 0.27), "voxel hit": (0.15, 0.35, 0.90), "far hit": (0.15, 0.80, 0.25),
        "CAPPED sky hole": (0.95, 0.10, 0.10), "CAPPED far behind": (0.85, 0.20, 0.85)}


def srgb(c):
    c = np.asarray(c)
    return np.where(c <= 0.0031308, 12.92 * c, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def classify(img, pal):
    names = list(pal)
    cols = [np.array(pal[n]) * 255 for n in names] + [srgb(pal[n]) * 255 for n in names]
    cols = np.stack(cols)                                   # (2k, 3)
    px = img.reshape(-1, 3).astype(np.float32)
    d = ((px[:, None, :] - cols[None, :, :]) ** 2).sum(-1)  # (N, 2k)
    idx = d.argmin(1) % len(names)
    counts = np.bincount(idx, minlength=len(names))
    return {n: 100.0 * counts[i] / px.shape[0] for i, n in enumerate(names)}


def main():
    src = sys.argv[1]
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(src, "sheets")
    os.makedirs(out, exist_ok=True)
    for a in sorted(glob.glob(os.path.join(src, "*_A.png"))):
        name = os.path.basename(a)[:-6]
        c = os.path.join(src, name + "_C_band.png")
        e = os.path.join(src, name + "_E_stop.png")
        if not (os.path.exists(c) and os.path.exists(e)):
            continue
        extra = [os.path.join(src, name + sfx) for sfx in ("_B_faroff.png", "_G_nocascade.png", "_F_tier.png")]
        ims = [Image.open(p).convert("RGB") for p in (a, c, e)]
        row2 = [Image.open(p).convert("RGB") for p in extra if os.path.exists(p)]
        # classify at a reduced size (the debug views are flat colours)
        small = [np.asarray(im.resize((480, 270), Image.NEAREST)) for im in ims]
        band = classify(small[1], BAND)
        stop = classify(small[2], STOP)
        w, h = 960, 540
        rows = 2 if len(row2) == 3 else 1
        sheet = Image.new("RGB", (w * 3 + 16, h * rows + 8 * (rows - 1)), (20, 20, 24))
        for i, im in enumerate(ims):
            sheet.paste(im.resize((w, h), Image.LANCZOS), (i * (w + 8), 0))
        if rows == 2:
            for i, im in enumerate(row2):
                sheet.paste(im.resize((w, h), Image.LANCZOS), (i * (w + 8), h + 8))
        sheet.save(os.path.join(out, name + ".jpg"), quality=86)
        bs = " ".join(f"{k} {v:.1f}" for k, v in band.items() if v >= 0.05)
        ss = " ".join(f"{k} {v:.2f}" for k, v in stop.items() if v >= 0.01)
        print(f"{name:<24} band%: {bs}\n{'':<24} stop%: {ss}")


def flicker(src):
    for f0 in sorted(glob.glob(os.path.join(src, "*_flicker0.png"))):
        f1 = f0.replace("flicker0", "flicker1")
        if not os.path.exists(f1):
            continue
        a = np.asarray(Image.open(f0).convert("L"), dtype=np.float32)
        b = np.asarray(Image.open(f1).convert("L"), dtype=np.float32)
        d = np.abs(a - b)
        print(f"{os.path.basename(f0)[:-13]:<10} flicker: mean |dL| {d.mean():.2f}/255, pixels changed > 24: {100.0 * (d > 24).mean():.2f}%")


if __name__ == "__main__":
    main()
    flicker(sys.argv[1])
