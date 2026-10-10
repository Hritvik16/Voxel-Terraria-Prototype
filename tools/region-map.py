#!/usr/bin/env python3
"""region-map.py <out dir> -- relief maps of TerrainGen v3's regions (design doc
§13, T1/T2) from Logs/TerrainGen/regions/seed_N.bin (RegionTests): hillshaded
height, each region tinted by its archetype, rivers and sea, labels at each
region's centre. One image per seed plus a plain hillshade without tints.
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

SRC = os.path.join("Logs", "TerrainGen", "regions")
NAMES = ["alpine range", "rolling highlands", "canyonlands", "volcanic", "river plains",
         "breaks", "dune sea", "sea cliffs", "high plateau", "wetland basin"]
TINT = [(0.80, 0.84, 0.92), (0.55, 0.72, 0.42), (0.86, 0.52, 0.30), (0.35, 0.30, 0.32), (0.62, 0.78, 0.45),
        (0.78, 0.55, 0.45), (0.93, 0.80, 0.52), (0.60, 0.66, 0.60), (0.72, 0.66, 0.50), (0.42, 0.60, 0.50)]


def load(path):
    with open(path, "rb") as f:
        n = int.from_bytes(f.read(4), "little")
        h = np.frombuffer(f.read(n * n * 4), dtype="<f4").reshape(n, n)
        reg = np.frombuffer(f.read(n * n), dtype=np.uint8).reshape(n, n)
    return h, reg


def shade(h):
    gx = np.gradient(h, axis=1) / 80.0
    gz = np.gradient(h, axis=0) / 80.0
    nrm = np.stack([-gx, np.ones_like(h) * 0.9, -gz], -1)
    nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
    light = np.array([-0.55, 0.7, -0.45]); light /= np.linalg.norm(light)
    return np.clip(0.25 + 0.95 * (nrm @ light), 0.12, 1.2)


def render(h, reg, tinted, font):
    s = shade(h)[..., None]
    col = np.ones(h.shape + (3,)) * 0.75
    if tinted:
        lut = np.array(TINT + [(0.5, 0.5, 0.5)] * (256 - len(TINT)))
        col = lut[reg]
    alt = np.clip(h / 4000.0, 0, 1)[..., None]
    img = col * s * (0.85 + 0.3 * alt)
    sea = h <= 0
    depth = np.clip(-h / 900.0, 0, 1)
    img[sea] = (np.array([0.20, 0.42, 0.62]) * (1 - 0.5 * depth[sea, None]))
    img = np.flipud(img)                          # z up = north
    im = Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8))
    if tinted:
        d = ImageDraw.Draw(im)
        n = h.shape[0]
        # One label per CONNECTED region (two regions of one archetype get two
        # labels), found by a flood fill on a 4x-downsampled grid.
        step = 4
        g = reg[::step, ::step]
        m = g.shape[0]
        seen = np.zeros(g.shape, bool)
        for sz in range(m):
            for sx in range(m):
                k = g[sz, sx]
                if seen[sz, sx] or k >= len(NAMES):
                    continue
                stack = [(sz, sx)]; seen[sz, sx] = True; cells = []
                while stack:
                    z, x = stack.pop(); cells.append((z, x))
                    for dz, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        zz, xx = z + dz, x + dx
                        if 0 <= zz < m and 0 <= xx < m and not seen[zz, xx] and g[zz, xx] == k:
                            seen[zz, xx] = True; stack.append((zz, xx))
                if len(cells) < 120:
                    continue
                cz = np.array([c[0] for c in cells]); cx = np.array([c[1] for c in cells])
                mz, mx = cz.mean(), cx.mean()
                j = int(np.argmin((cz - mz) ** 2 + (cx - mx) ** 2))     # a cell inside, nearest the centre
                px, pz = int(cx[j] * step), int(n - 1 - cz[j] * step)
                txt = NAMES[k]
                w = d.textlength(txt, font=font)
                d.rectangle([px - w / 2 - 4, pz - 9, px + w / 2 + 4, pz + 9], fill=(0, 0, 0))
                d.text((px - w / 2, pz - 8), txt, fill=(255, 255, 255), font=font)
    return im


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    try:
        font = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial Bold.ttf", 14)
    except Exception:
        font = ImageFont.load_default()
    for f in sorted(os.listdir(SRC)):
        if not f.endswith(".bin"):
            continue
        h, reg = load(os.path.join(SRC, f))
        seed = f[5:-4]
        crop = slice(112, 912)
        render(h[crop, crop], reg[crop, crop], True, font).save(os.path.join(out, f"regions_{seed}.jpg"), quality=88)
        render(h[crop, crop], reg[crop, crop], False, font).save(os.path.join(out, f"relief_{seed}.jpg"), quality=88)
        print("seed", seed)


if __name__ == "__main__":
    main()
