#!/usr/bin/env python3
"""Task 138: the tilted sky probe look check. python3 -I tools/skytilt138.py <LookReview dir> <out dir>
Prints pure-black pixel counts (max channel <= 4, task 136's metric) and mean luma per shot, and writes
skytilt138.png: rows = cliff2 A (ledge), stone arches, cave mouth (sun 20, open face in sun), tunnel, sealed room;
columns = native (vertical probe) | tilt 1 | tilt 2 (side faces only) | changed pixels (red: tilt 1 vs native)."""
import os, sys, glob
import numpy as np
from PIL import Image, ImageDraw, ImageFont

src, outd = sys.argv[1], sys.argv[2]
font = ImageFont.load_default(size=26)
W, H = 640, 360


def load(n):
    p = os.path.join(src, n + ".png")
    return np.asarray(Image.open(p).convert("RGB")) if os.path.exists(p) else None


def black(a):
    return int((a.max(axis=2) <= 4).sum())


def luma(a):
    return float((a.astype(np.float32) @ np.array([0.2126, 0.7152, 0.0722])).mean())


lines = []
names = sorted(os.path.basename(f)[:-4] for f in glob.glob(os.path.join(src, "bs_*.png")))
for n in names:
    a = load(n)
    lines.append(f"{n:32s} black {black(a):>9,d}   mean luma {luma(a):6.1f}")
for base in sorted({n.rsplit('_', 1)[0] for n in names}):
    nat = load(base + "_native")
    if nat is None:
        continue
    for arm in ("tilt", "tilt2", "nosky"):
        b = load(base + "_" + arm)
        if b is None:
            continue
        d = np.abs(b.astype(np.int16) - nat.astype(np.int16)).max(axis=2) > 8
        lines.append(f"{base:26s} {arm:6s} vs native: black {black(nat):>9,d} -> {black(b):>9,d}, changed px (>8) {int(d.sum()):>9,d}, "
                     f"luma {luma(nat):.1f} -> {luma(b):.1f}")
txt = "\n".join(lines)
print(txt)
os.makedirs(outd, exist_ok=True)
open(os.path.join(outd, "skytilt138.txt"), "w").write(txt + "\n")


def panel(a, text):
    im = Image.fromarray(a).resize((W, H), Image.LANCZOS)
    d = ImageDraw.Draw(im)
    d.rectangle((0, 0, W, 36), fill=(0, 0, 0))
    d.text((8, 4), text, font=font, fill=(255, 255, 255))
    return im


rows = [("bs_A", "cliff2 A (ledge)"), ("bs_other", "stone arches"), ("bs_st_mouth20", "mouth, sun 20"),
        ("bs_st_tunnel", "in the tunnel"), ("bs_st_sealed", "sealed room")]
rows = [r for r in rows if load(r[0] + "_native") is not None]
sheet = Image.new("RGB", (4 * W + 12, len(rows) * (H + 4)), (30, 30, 30))
for i, (b, title) in enumerate(rows):
    nat, t1, t2 = load(b + "_native"), load(b + "_tilt"), load(b + "_tilt2")
    cells = [panel(nat, f"{title}: before ({black(nat):,} black)")]
    for arm, a in (("tilt 1", t1), ("tilt 2 sides", t2)):
        cells.append(panel(a, f"{arm} ({black(a):,} black)") if a is not None else Image.new("RGB", (W, H)))
    if t1 is not None:
        diff = (np.abs(t1.astype(np.int16) - nat.astype(np.int16)).max(axis=2) > 8)
        g = (nat.astype(np.float32) * 0.35).astype(np.uint8)
        g[diff] = (255, 40, 40)
        cells.append(panel(g, "red = changed by tilt 1"))
    for j, c in enumerate(cells):
        sheet.paste(c, (j * (W + 4), i * (H + 4)))
sheet.save(os.path.join(outd, "skytilt138.png"))
print("wrote", os.path.join(outd, "skytilt138.png"))
