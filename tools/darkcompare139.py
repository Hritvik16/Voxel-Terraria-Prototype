#!/usr/bin/env python3
"""Task 139: the dark-overhang side-by-side. python3 -I tools/darkcompare139.py <LookReview dir> <out dir> [today_floor]
Pure-black pixel counts (max channel <= 4, task 136's metric) and mean luma per native shot -> darkcompare139.txt.
Sheets (cells are the play look, FSR 3 dyn; the count under each cell is from its native 1080 twin):
  dc139_surface.png  rows = ledge / arches / cave mouth at noon and dusk; columns = the five arms
  dc139_caves.png    rows = inside the tunnel / sealed room at noon and night (no torch)
  dc139_torch.png    rows = tunnel / sealed room at night with ONE torch; columns = today | middle floor | sky check off"""
import os, sys, glob, re
import numpy as np
from PIL import Image, ImageDraw, ImageFont

src, outd = sys.argv[1], sys.argv[2]
today = sys.argv[3] if len(sys.argv) > 3 else "?"
font = ImageFont.load_default(size=26)
big = ImageFont.load_default(size=30)
W, H, LW, TH = 640, 360, 250, 70


def load(n):
    p = os.path.join(src, n + ".png")
    return np.asarray(Image.open(p).convert("RGB")) if os.path.exists(p) else None


def black(a):
    return int((a.max(axis=2) <= 4).sum())


def luma(a):
    return float((a.astype(np.float32) @ np.array([0.2126, 0.7152, 0.0722])).mean())


names = sorted(os.path.basename(f)[:-4] for f in glob.glob(os.path.join(src, "dc_*_nat.png")))
arms = []
for n in names:
    a = n.split("_")[-2]
    if a not in arms:
        arms.append(a)
fl = sorted([a for a in arms if re.fullmatch(r"f\d+", a)], key=lambda a: int(a[1:]))
arms = (["today"] if "today" in arms else []) + fl + (["nosky"] if "nosky" in arms else [])
words = ["barely visible", "dim grey", "grey"]


def header(a):
    if a == "today":
        return [f"today: pitch black", f"(floor {today})"]
    if a == "nosky":
        return ["sky check off:", "all lit as open sky"]
    i = fl.index(a)
    return [f"floor {int(a[1:]) / 100:.2f}:", words[i] if i < len(words) else ""]


lines = [f"{'shot':34s} {'pure-black px':>14s}  mean luma   (native 1920x1080; black = max channel <= 4)"]
counts = {}
for n in names:
    a = load(n)
    counts[n] = black(a)
    lines.append(f"{n:34s} {counts[n]:>14,d}  {luma(a):9.1f}")
txt = "\n".join(lines)
print(txt)
os.makedirs(outd, exist_ok=True)
open(os.path.join(outd, "darkcompare139.txt"), "w").write(txt + "\n")


def cell(row, arm):
    p = load(f"dc_{row}_{arm}_play")
    nat = f"dc_{row}_{arm}_nat"
    if p is None:
        return Image.new("RGB", (W, H), (60, 0, 0))
    im = Image.fromarray(p).resize((W, H), Image.LANCZOS)
    d = ImageDraw.Draw(im)
    if nat in counts:
        t = f"{counts[nat]:,} black px"
        d.rectangle((0, H - 34, 10 + int(d.textlength(t, font=font)), H), fill=(0, 0, 0))
        d.text((6, H - 32), t, font=font, fill=(255, 220, 80))
    return im


def sheet(rows, cols, name, title):
    rows = [r for r in rows if load(f"dc_{r[0]}_{cols[0]}_play") is not None]
    if not rows:
        return
    sh = Image.new("RGB", (LW + len(cols) * (W + 4), 44 + TH + len(rows) * (H + 4)), (30, 30, 30))
    d = ImageDraw.Draw(sh)
    d.text((8, 6), title, font=big, fill=(255, 255, 255))
    for j, a in enumerate(cols):
        for k, t in enumerate(header(a)):
            d.text((LW + j * (W + 4) + 8, 44 + k * 32), t, font=big, fill=(255, 255, 255))
    for i, (r, label) in enumerate(rows):
        y = 44 + TH + i * (H + 4)
        for k, t in enumerate(label.split("\n")):
            d.text((8, y + 8 + k * 32), t, font=big, fill=(255, 255, 255))
        for j, a in enumerate(cols):
            sh.paste(cell(r, a), (LW + j * (W + 4), y))
    p = os.path.join(outd, name)
    sh.save(p)
    print("wrote", p)


sheet([("ledge_noon", "cliff ledge\nnoon"), ("ledge_dusk", "cliff ledge\ndusk (sun 10)"),
       ("arches_noon", "stone arches\nnoon"), ("arches_dusk", "stone arches\ndusk (sun 10)"),
       ("mouth_noon", "built cave\nmouth, noon"), ("mouth_dusk", "built cave\nmouth, dusk")],
      arms, "dc139_surface.png", "Surface overhangs in daylight: which look? (play look, FSR 3; yellow = pure-black pixels at native 1080)")
sheet([("tunnel_noon", "inside the\ntunnel, noon"), ("tunnel_night", "inside the\ntunnel, night"),
       ("sealed_noon", "sealed room\nnoon"), ("sealed_night", "sealed room\nnight")],
      arms, "dc139_caves.png", "Caves with no light source (play look, FSR 3; yellow = pure-black pixels at native 1080)")
mid = fl[len(fl) // 2] if fl else "today"
sheet([("tunnel_torch", "tunnel, night\none torch"), ("sealed_torch", "sealed room\nnight, 1 torch")],
      ["today", mid, "nosky"], "dc139_torch.png", "One torch at night (play look, FSR 3; yellow = pure-black pixels at native 1080)")
