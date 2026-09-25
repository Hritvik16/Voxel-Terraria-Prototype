#!/usr/bin/env python3
"""palette-board.py <out dir> [A,C,D] -- colour Logs/TerrainGen/palette/*.bin (written by
PaletteSampleTests) with three candidate palettes, so the owner can compare
palette DIRECTIONS on identical generated content (question (ii), 2026-09-25).

Per palette: the island from above (hillshaded), a section through the peak,
a surface-to-floor section, a voxel-scale cliff, and a swatch board per biome.
Colours are sRGB 0..1. Underground air is drawn as cave darkness, open air as sky.
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

SRC = os.path.join("Logs", "TerrainGen", "palette")

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from palette_defs import NAMES, BIOMES, PALETTES, ROUND2  # noqa: E402

SKY = np.array([0.62, 0.74, 0.88])
CAVE = np.array([0.04, 0.04, 0.05])


def load(name):
    with open(os.path.join(SRC, name), "rb") as f:
        w = int.from_bytes(f.read(4), "little"); h = int.from_bytes(f.read(4), "little")
        return np.frombuffer(f.read(w * h), dtype=np.uint8).reshape(h, w)


def lut(pal):
    t = np.zeros((256, 3))
    t[:] = (1.0, 0.0, 1.0)            # unmapped: magenta, obvious
    for k, c in pal.items():
        t[k] = c
    return t


def colour_section(ids, pal):
    t = lut(pal)
    img = t[ids]
    air = ids == 0
    solid_above = np.maximum.accumulate(~air & (ids > 7), axis=0)   # any solid higher in the column
    img[air & solid_above] = CAVE
    img[air & ~solid_above] = SKY
    return img


def colour_top(ids, hgt, pal):
    t = lut(pal)
    img = t[ids]
    h = hgt.astype(np.float32)
    gx = np.gradient(h, axis=1) / 80.0; gz = np.gradient(h, axis=0) / 80.0
    n = np.stack([-gx, np.ones_like(h) * 1.2, -gz], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    light = np.array([-0.5, 0.75, -0.45]); light /= np.linalg.norm(light)
    shade = np.clip(0.35 + 0.85 * (n @ light), 0.25, 1.15)[..., None]
    ocean = ids == 1
    out = img * shade
    out[ocean] = img[ocean] * (0.75 + 0.25 * np.clip((h[ocean] - 400) / 700, 0, 1))[..., None]
    return out


def to_img(a, scale=1):
    im = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8))
    if scale != 1:
        im = im.resize((int(im.width * scale), int(im.height * scale)), Image.NEAREST if scale > 1 else Image.LANCZOS)
    return im


def swatches(pal, font):
    w, row = 1560, 44
    im = Image.new("RGB", (w, row * len(BIOMES) + 8), (24, 25, 30))
    d = ImageDraw.Draw(im)
    for r, (bname, codes) in enumerate(BIOMES):
        y = 4 + r * row
        d.text((8, y + 12), bname, fill=(230, 226, 220), font=font)
        sw = (w - 120) // max(len(c) for _, c in BIOMES)
        for i, k in enumerate(codes):
            c = tuple(int(v * 255) for v in pal[k])
            x = 120 + i * sw
            d.rectangle([x, y, x + sw - 4, y + row - 8], fill=c)
            lum = 0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2]
            d.text((x + 4, y + row - 22), NAMES[k][:16], fill=(20, 20, 20) if lum > 140 else (235, 235, 235), font=font)
    return im


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    try:
        font = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 12)
    except Exception:
        font = ImageFont.load_default()
    top = load("top.bin")
    hraw = np.frombuffer(open(os.path.join(SRC, "top_height.bin"), "rb").read(), dtype=np.uint16).reshape(top.shape)
    peak, deep, cliff = load("peak.bin"), load("deep.bin"), load("cliff.bin")
    keys = sys.argv[2].split(",") if len(sys.argv) > 2 else ROUND2
    for key in keys:
        name, desc, pal = PALETTES[key]
        crop = slice(150, 874)
        to_img(colour_top(top[crop, crop], hraw[crop, crop], pal)).resize((900, 900), Image.LANCZOS).save(os.path.join(out, f"{key}_top.jpg"), quality=86)
        to_img(colour_section(peak, pal), 0.6).save(os.path.join(out, f"{key}_peak.jpg"), quality=86)
        to_img(colour_section(deep, pal), 0.8).save(os.path.join(out, f"{key}_deep.jpg"), quality=86)
        to_img(colour_section(cliff, pal), 1.6).save(os.path.join(out, f"{key}_cliff.png"))
        swatches(pal, font).save(os.path.join(out, f"{key}_swatches.png"))
        print(key, name)


if __name__ == "__main__":
    main()
