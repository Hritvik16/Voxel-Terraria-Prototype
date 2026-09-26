#!/usr/bin/env python3
"""erosion-lab-view.py out.png a.bin [b.bin ...] -- hillshade eroded grids from
tools/erosion-lab side by side (labels = file names), for parameter sweeps."""
import os, sys
import numpy as np
from PIL import Image, ImageDraw

def load(p):
    with open(p, "rb") as f:
        n = int.from_bytes(f.read(4), "little")
        return np.frombuffer(f.read(n * n * 4), dtype="<f4").reshape(n, n)

def shade(h, cell=160.0):
    gx = np.gradient(h, axis=1) / cell; gz = np.gradient(h, axis=0) / cell
    nrm = np.stack([-gx, np.ones_like(h) * 0.6, -gz], -1); nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
    l = np.array([-0.55, 0.7, -0.45]); l /= np.linalg.norm(l)
    s = np.clip(0.2 + 0.95 * (nrm @ l), 0.08, 1.2)
    img = np.stack([s] * 3, -1) * (0.8 + 0.4 * np.clip(h / 4000, 0, 1))[..., None]
    img[h <= 0] = (0.2, 0.4, 0.6)
    return np.flipud(img)

out = sys.argv[1]; files = sys.argv[2:]
tiles = []
for p in files:
    h = load(p)[56:456, 56:456]
    im = Image.fromarray((np.clip(shade(h), 0, 1) * 255).astype(np.uint8)).resize((600, 600), Image.LANCZOS)
    d = ImageDraw.Draw(im); d.rectangle([0, 0, 600, 20], fill=(0, 0, 0)); d.text((6, 4), os.path.basename(p), fill=(255, 255, 255))
    tiles.append(im)
cols = min(3, len(tiles)); rows = (len(tiles) + cols - 1) // cols
sheet = Image.new("RGB", (cols * 600, rows * 600))
for i, t in enumerate(tiles): sheet.paste(t, ((i % cols) * 600, (i // cols) * 600))
sheet.save(out, quality=85)
