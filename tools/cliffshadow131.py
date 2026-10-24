#!/usr/bin/env python3
"""Task 131: side-by-side sheets for Q83 (tier 0 vs a far cliff's shadow).

  python3 -I tools/cliffshadow131.py <LookReview dir> [out dir]

Per spot in <dir>/cliffshadow.json: row 1 A (eye 1.6 m in the shadow, play look) | B (300 m back,
play look); row 2 C (250 m up, play look) | C debug view 14, both with the 0.1 m window's outline
(yellow) and the player (red ring); row 3 A native 1080 | A debug view 14.
"""
import json, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

src = sys.argv[1]
out = sys.argv[2] if len(sys.argv) > 2 else "Screenshots/131"
os.makedirs(out, exist_ok=True)
meta = json.load(open(os.path.join(src, "cliffshadow.json")))
W, H = meta["screen"]
pw, ph = W // 2, H // 2
font = ImageFont.load_default(size=26)


def decode(path):
    """Debug view 14 (task 129): R = what drew the pixel (0 sky, 1 tier 0, 1 + k shell k, 12 far band),
    G/B = log distance. Calibrated on the 4-row grey ramp, as tools/lodref129.py."""
    img = np.asarray(Image.open(path).convert("RGB")).astype(np.float32)
    for rows in (slice(0, 4), slice(img.shape[0] - 4, img.shape[0])):
        lv = np.array([np.median(img[rows][:, 16 * c + 4:16 * c + 12, :], axis=(0, 1)) for c in range(64)])
        if np.all(np.diff(lv, axis=0) > 0.5):
            break
    codes = np.stack([np.argmin(np.abs(img[:, :, ch][..., None] - lv[None, None, :, ch]), axis=2) for ch in range(3)], -1)
    code = codes[:, :, 0]; code[rows] = -1
    return code, np.power(2.0, (codes[:, :, 1] * 64 + codes[:, :, 2]) / 300.0) - 1.0


def drawn_by(code):
    """tier 0 orange, shells 1-7 light to dark blue, far band purple, sky black."""
    rgb = np.zeros(code.shape + (3,), np.uint8)
    rgb[code == 1] = (255, 140, 0)
    for k in range(1, 8):
        rgb[code == 1 + k] = (40, 200 - 22 * k, 255 - 15 * k)
    rgb[code == 12] = (150, 60, 200)
    return Image.fromarray(rgb)


def luma(path):
    a = np.asarray(Image.open(path).convert("RGB")).astype(np.float32)
    return 0.2126 * a[:, :, 0] + 0.7152 * a[:, :, 1] + 0.0722 * a[:, :, 2]


def panel(name, label, overlay=None):
    p = os.path.join(src, name + ".png")
    if name.endswith("_hit") and os.path.exists(p):
        im = drawn_by(decode(p)[0])
    else:
        im = Image.open(p).convert("RGB") if os.path.exists(p) else Image.new("RGB", (W, H), (60, 0, 60))
    d = ImageDraw.Draw(im)
    if overlay:
        corners, q = overlay
        # the window box's corners in order lo-lo, hi-lo, hi-hi, lo-hi
        pts = [tuple(c) for c in corners]
        d.line(pts + [pts[0]], fill=(255, 230, 0), width=4)
        d.ellipse([q[0] - 14, q[1] - 14, q[0] + 14, q[1] + 14], outline=(255, 40, 40), width=5)
    im = im.resize((pw, ph), Image.LANCZOS)
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, pw, 36], fill=(0, 0, 0))
    d.text((8, 4), label, fill=(255, 255, 255), font=font)
    return im


for s in meta["spots"]:
    t = s["tag"]
    ov = (s["C"]["windowPx"], s["C"]["qPx"])
    rows = [
        [(f"{t}_A_play", "A  eye 1.6 m IN the shadow, play look (FSR 3 dyn)", None),
         (f"{t}_B_play", "B  same ground from 300 m back (shells draw it)", None)],
        [(f"{t}_C_play", "C  250 m up; yellow = 0.1 m window, red = player", ov),
         (f"{t}_C_hit", "C  drawn by: orange tier 0 (0.1 m), blue shells", ov)],
        [(f"{t}_A_native", "A  native 1080 rows", None),
         (f"{t}_A_hit", "A  drawn by: orange tier 0 (0.1 m), blue shells", None)],
    ]
    sheet = Image.new("RGB", (W, ph * len(rows) + 44), (0, 0, 0))
    hd = ImageDraw.Draw(sheet)
    hd.text((8, 8), f"task 131 {t}: Q ({s['Q'][0]:.0f}, {s['Q'][1]:.0f}, {s['Q'][2]:.0f}) m, sun {s['elev']:.0f} deg; "
            f"Q's shadow is cast {s['casterM']:.0f} m away by terrain up to {s['cliffH']:.0f} m above Q",
            fill=(255, 255, 255), font=font)
    for r, row in enumerate(rows):
        for c, (name, label, o) in enumerate(row):
            sheet.paste(panel(name, label, o), (c * pw, 44 + r * ph))
    for v in ("A", "C"):
        code, dist = decode(os.path.join(src, f"{t}_{v}_hit.png"))
        l = luma(os.path.join(src, f"{t}_{v}_play.png"))
        t0, sh = code == 1, (code >= 2) & (code <= 8)
        r0 = np.percentile(dist[t0], 99) if t0.any() else 0
        print(f"{t} {v}: tier-0 pixels {100 * t0.mean():.1f}% (99th pct hit distance {r0:.0f} m), mean luma tier 0 {l[t0].mean():.0f} "
              f"vs shells {l[sh].mean():.0f} (play look)")
    path = os.path.join(out, f"{t}_sheet.png")
    sheet.save(path)
    print(path)
