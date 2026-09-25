#!/usr/bin/env python3
"""palette-board.py <out dir> -- colour Logs/TerrainGen/palette/*.bin (written by
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

# GenMat codes (Assets/TerrainGen/GenMat.cs)
NAMES = {1: "water", 2: "lava", 3: "honey", 4: "oil", 5: "acid", 6: "sludge", 7: "quicksand",
         10: "grass", 11: "jungle grass", 12: "sand", 13: "snow", 14: "beach sand",
         20: "loam", 21: "packed sand", 22: "packed ice", 23: "mud",
         30: "limestone", 31: "slate", 32: "ochre clay", 33: "granite", 34: "shale",
         40: "cream sandstone", 41: "red sandstone", 42: "orange sandstone", 43: "maroon mudstone", 44: "pink siltstone",
         50: "blue slate", 51: "marble", 52: "basalt", 53: "teal quartzite", 54: "silver schist",
         60: "karst limestone", 61: "black basalt", 62: "laterite", 63: "moss rock", 64: "jade serpentine",
         70: "deepstone", 71: "cavern rock", 72: "ash", 73: "obsidian", 74: "magma crust", 75: "bedrock",
         80: "sinter", 81: "crystal blue", 82: "crystal amber", 83: "crystal rose", 84: "crystal green",
         85: "bone", 86: "meteor ore", 87: "bark", 88: "leaves", 89: "mushroom stem", 90: "mushroom cap",
         91: "volcanic rock", 92: "standing stone"}

BIOMES = [("Forest", [10, 20, 30, 31, 32, 33, 34]), ("Desert", [12, 21, 40, 41, 42, 43, 44]),
          ("Snow", [13, 22, 50, 51, 52, 53, 54]), ("Jungle", [11, 23, 60, 61, 62, 63, 64]),
          ("Depths", [70, 71, 72, 73, 74, 75]), ("Fluids", [1, 2, 3, 4, 5, 6, 7]),
          ("Landmarks", [80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92])]


def hexes(d):
    return {k: tuple(int(v[i:i + 2], 16) / 255 for i in (0, 2, 4)) for k, v in d.items()}


PALETTES = {
    "A": ("Umber & Ember", "Low-chroma earths; biomes differ by the temperature of their neutrals. "
          "Saturation is kept for life, minerals and fluids.", hexes({
        1: "24566a", 2: "f26b1d", 3: "c98f28", 4: "1a1510", 5: "a8d81f", 6: "66cc33", 7: "9e855c",
        10: "6b7f47", 11: "4d7540", 12: "dbc9a1", 13: "eef0f2", 14: "e3d4b0",
        20: "66503a", 21: "b8a37f", 22: "b8ccdb", 23: "4d3e33",
        30: "b8b09e", 31: "545a61", 32: "a88c5c", 33: "948a85", 34: "474545",
        40: "d4c29e", 41: "94543d", 42: "bf8554", 43: "663b36", 44: "c79e94",
        50: "667385", 51: "d9d9d4", 52: "383b40", 53: "789494", 54: "9ea3a8",
        60: "b3b399", 61: "333633", 62: "94593d", 63: "617352", 64: "668c73",
        70: "40404a", 71: "54504d", 72: "6b6661", 73: "141219", 74: "59291a", 75: "1f1f1f",
        80: "e0d6bd", 81: "59a6fa", 82: "faad33", 83: "f273b3", 84: "59eb8c",
        85: "dbd1b3", 86: "f24d8c", 87: "5c422e", 88: "476b38", 89: "d1c7b3", 90: "9e4d8c",
        91: "2e2929", 92: "8f8c85"})),
    "B": ("Pastel Atlas", "Light and painterly. Each biome owns a hue family: sage and lavender "
          "forest, apricot and rose desert, periwinkle and pearl snow, aqua and coral jungle.", hexes({
        1: "5aa9c9", 2: "ff8a4d", 3: "f0c060", 4: "4a4050", 5: "c8f060", 6: "8fe07a", 7: "d6bf94",
        10: "9cc48a", 11: "6fc2a0", 12: "f5d9b8", 13: "f4f6ff", 14: "f7e6cc",
        20: "a88f7a", 21: "e8c9a8", 22: "d0e4f5", 23: "8a7a88",
        30: "d9d4c4", 31: "a39bc2", 32: "d9bf8f", 33: "c4b4b8", 34: "8a8699",
        40: "fbe3c4", 41: "e8978a", 42: "f5b98a", 43: "b8828f", 44: "f2c4c8",
        50: "a3b3e8", 51: "f7f5f0", 52: "7d80a8", 53: "a8dcd9", 54: "c8cfe0",
        60: "d6e8d9", 61: "5e7a80", 62: "f09a7a", 63: "94c9a8", 64: "7cc9b8",
        70: "7a7894", 71: "948a99", 72: "b8b3b0", 73: "3d3552", 74: "b86a5c", 75: "4d4a57",
        80: "fff5e6", 81: "8fc4ff", 82: "ffc97a", 83: "ffa3cf", 84: "8ff0b8",
        85: "fff0d9", 86: "ff7ab3", 87: "a8866e", 88: "8fbf7a", 89: "fff0e0", 90: "d98ac4",
        91: "5c5566", 92: "c4c0cc"})),
    "C": ("Ink & Neon", "Dark, high-contrast rock with bold light bands; vivid keys per biome "
          "(emerald, vermilion, ultramarine, lime) and neon fluids and crystals.", hexes({
        1: "1f8fff", 2: "ff5a00", 3: "ffb000", 4: "0d0a0f", 5: "b6ff1a", 6: "39ff5a", 7: "b3884d",
        10: "1fb35a", 11: "8fd61f", 12: "f2c266", 13: "ffffff", 14: "f5d58f",
        20: "3d2618", 21: "c28a3d", 22: "7fd1ff", 23: "261a14",
        30: "e8e0cc", 31: "1a2433", 32: "d99a2e", 33: "4d4a52", 34: "0f1419",
        40: "ffd9a1", 41: "c7331f", 42: "ff8a1f", 43: "40100f", 44: "ff99a3",
        50: "1f3fa6", 51: "f5f5ff", 52: "0d1026", 53: "1fd1c7", 54: "8a9ec7",
        60: "e6f2cc", 61: "0a1a14", 62: "e0522e", 63: "2e8c4d", 64: "14b38a",
        70: "1a1a2e", 71: "2b2433", 72: "595266", 73: "05030a", 74: "8c1a0a", 75: "000000",
        80: "fff2d9", 81: "33b3ff", 82: "ffb31a", 83: "ff4db3", 84: "33ff99",
        85: "fff5e0", 86: "ff1a8c", 87: "4d2e1a", 88: "1fa64d", 89: "f2e6d9", 90: "b31a8c",
        91: "140f14", 92: "a6a3b3"})),
}

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
    for key, (name, desc, pal) in PALETTES.items():
        crop = slice(150, 874)
        to_img(colour_top(top[crop, crop], hraw[crop, crop], pal)).resize((900, 900), Image.LANCZOS).save(os.path.join(out, f"{key}_top.jpg"), quality=86)
        to_img(colour_section(peak, pal), 0.6).save(os.path.join(out, f"{key}_peak.jpg"), quality=86)
        to_img(colour_section(deep, pal), 0.8).save(os.path.join(out, f"{key}_deep.jpg"), quality=86)
        to_img(colour_section(cliff, pal), 1.6).save(os.path.join(out, f"{key}_cliff.png"))
        swatches(pal, font).save(os.path.join(out, f"{key}_swatches.png"))
        print(key, name)


if __name__ == "__main__":
    main()
