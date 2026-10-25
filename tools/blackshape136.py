#!/usr/bin/env python3
"""Task 136: the cliff2 black-shape sheet. python3 -I tools/blackshape136.py <LookReview dir> <out.png>
Row 1: A play look at native 1080 (red box = the black shape) | the box 2x | debug view 14 decoded (who drew it).
Row 2: A with the vertical sky probe OFF | side view 39 m away | a second overhang (stone arches, 38 m)."""
import os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

src, out = sys.argv[1], sys.argv[2]
font = ImageFont.load_default(size=30)
W, H = 960, 540
BOX = (1590, 280, 1920, 600)


def decode_code(path):
    img = np.asarray(Image.open(path).convert("RGB")).astype(np.float32)
    for rows in (slice(0, 4), slice(img.shape[0] - 4, img.shape[0])):
        lv = np.array([np.median(img[rows][:, 16 * c + 4:16 * c + 12, :], axis=(0, 1)) for c in range(64)])
        if np.all(np.diff(lv, axis=0) > 0.5):
            break
    code = np.argmin(np.abs(img[:, :, 0][..., None] - lv[None, None, :, 0]), axis=2)
    code[rows] = -1
    return code


def drawn_by(path):
    code = decode_code(path)
    rgb = np.zeros(code.shape + (3,), np.uint8)
    rgb[code == 1] = (255, 140, 0)
    for k in range(1, 8):
        rgb[code == 1 + k] = (40, 200 - 22 * k, 255 - 15 * k)
    rgb[code == 12] = (150, 60, 200)
    return Image.fromarray(rgb)


def label(im, text, box=None):
    im = im.convert("RGB")
    if box:
        ImageDraw.Draw(im).rectangle(box, outline=(255, 0, 0), width=6)
    im = im.resize((W, H), Image.LANCZOS)
    d = ImageDraw.Draw(im)
    d.rectangle((0, 0, W, 44), fill=(0, 0, 0))
    d.text((10, 6), text, font=font, fill=(255, 255, 255))
    return im


def P(n):
    return Image.open(os.path.join(src, n + ".png"))


crop = P("bs_A_native").convert("RGB").crop(BOX)
panels = [
    label(P("bs_A_native"), "1 cliff2 A, play look, native 1080 (red box)", BOX),
    label(crop, "2 the box, zoomed: pure black, RGB (1,1,2)"),
    label(drawn_by(os.path.join(src, "bs_A_hit.png")), "3 view 14: orange = tier-0 HIT (85-98 m)", BOX),
    label(P("bs_A_nosky"), "4 same, vertical sky probe OFF: gone", BOX),
    label(P("bs_side_native"), "5 side, 39 m: the undercut below the ledge"),
    label(P("bs_other_native"), "6 another overhang: stone arches, 38 m"),
]
sheet = Image.new("RGB", (3 * W + 8, 2 * H + 4), (30, 30, 30))
for i, p in enumerate(panels):
    sheet.paste(p, ((i % 3) * (W + 4), (i // 3) * (H + 4)))
os.makedirs(os.path.dirname(out) or ".", exist_ok=True)
sheet.save(out)
print("wrote", out)
