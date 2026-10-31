#!/usr/bin/env python3
"""Task 158: torch flood look check. python3 -I tools/torchflood158.py <LookReview dir> <out dir> [launch log]
Sheet torchflood158.png (play look, FSR 3 dyn): rows = tunnel / sealed room / open ground / pillar at night;
columns = today (shipped 2.9 m) | analytic 14 m | analytic 24 m (task 143) | FLOOD 12 m | FLOOD 12 m brighter map.
Counts torchflood158.txt (native 1080): lit px = luma >= 10 above the same pose with the torch light off ('none');
bright = luma >= 40; plus the OFF check (tr_ground_today_nat vs tr_ground_today_nat_off2, bytes) and the probe lines."""
import os, sys, re
import numpy as np
from PIL import Image, ImageDraw, ImageFont

src, outd = sys.argv[1], sys.argv[2]
logp = sys.argv[3] if len(sys.argv) > 3 else None
font = ImageFont.load_default(size=26)
big = ImageFont.load_default(size=30)
W, H, LW, TH = 640, 360, 250, 104
ROWS = [("tunnel", "built tunnel\nnight, eye at\nthe mouth"), ("room", "sealed room\n5 m cube,\nnight"),
        ("ground", "open ground\nnight, eye\n12 m back"), ("pillar", "open ground\n+ a pillar\n1.5 m beside")]
ARMS = ["today", "half", "tunnel", "flood", "floodb"]
WORDS = {"today": "today (shipped): 2.9 m", "half": "analytic 14 m (143)", "tunnel": "analytic 24 m (143)",
         "flood": "FLOOD 12 m (recommended)", "floodb": "FLOOD 12 m, brighter map"}
SUB = {"today": "k 0.35, I 1.6, range 8 m + 4 rays", "half": "k 0.15, I 2.4, range 14 m + rays",
       "tunnel": "k 0.06, I 3.0, range 24 m + rays", "flood": "I 2.0, k 0.10, wrap 0.3; no rays", "floodb": "I 3.0, k 0.06, wrap 0.3; no rays"}


def load(n):
    p = os.path.join(src, n + ".png")
    return np.asarray(Image.open(p).convert("RGB")).astype(np.float32) if os.path.exists(p) else None


def luma(a):
    return a @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)


lines = ["lit px = native 1080 pixels >= 10 luma (0-255) above the same pose with the torch light off ('none'); bright = luma >= 40",
         f"{'row':8s} {'arm':8s} {'lit px':>10s} {'bright px':>10s} {'mean luma':>10s}"]
stat = {}
for r, _ in ROWS:
    base = load(f"tr_{r}_none_nat")
    for a in ["none"] + ARMS:
        img = load(f"tr_{r}_{a}_nat")
        if img is None:
            continue
        L = luma(img)
        lit = int(((L - luma(base)) >= 10).sum()) if base is not None else -1
        stat[(r, a)] = lit
        lines.append(f"{r:8s} {a:8s} {lit:>10,d} {int((L >= 40).sum()):>10,d} {L.mean():10.1f}")
a0, a1 = os.path.join(src, "tr_ground_today_nat.png"), os.path.join(src, "tr_ground_today_nat_off2.png")
if os.path.exists(a0) and os.path.exists(a1):
    x0 = np.asarray(Image.open(a0).convert("RGB")).astype(np.int16)
    x1 = np.asarray(Image.open(a1).convert("RGB")).astype(np.int16)
    diff = int((np.abs(x0 - x1).max(axis=2) > 0).sum())
    lines.append(f"\nOFF check: tr_ground_today_nat (before any flood) vs _off2 (after the pool existed, flood off): {diff} px differ "
                 f"(max {int(np.abs(x0 - x1).max())} levels)")
if logp and os.path.exists(logp):
    t = open(logp).read()
    lines.append("")
    for m in re.finditer(r"^\s+(flood \(.*|probe \(.*|pillar: .*)$", t, re.M):
        lines.append(m.group(1))
txt = "\n".join(lines)
print(txt)
os.makedirs(outd, exist_ok=True)
open(os.path.join(outd, "torchflood158.txt"), "w").write(txt + "\n")


def cell(r, a):
    p = load(f"tr_{r}_{a}_play")
    if p is None:
        im = Image.new("RGB", (W, H), (40, 40, 40))
        ImageDraw.Draw(im).text((20, H // 2 - 15), "(not shot)", font=big, fill=(160, 160, 160))
        return im
    im = Image.fromarray(p.astype(np.uint8)).resize((W, H), Image.LANCZOS)
    d = ImageDraw.Draw(im)
    if (r, a) in stat:
        t = f"{stat[(r, a)]:,} lit px"
        d.rectangle((0, H - 34, 10 + int(d.textlength(t, font=font)), H), fill=(0, 0, 0))
        d.text((6, H - 32), t, font=font, fill=(255, 220, 80))
    return im


sh = Image.new("RGB", (LW + len(ARMS) * (W + 4), 44 + TH + len(ROWS) * (H + 4)), (30, 30, 30))
d = ImageDraw.Draw(sh)
d.text((8, 6), "One torch at night: today vs the analytic 14 / 24 m arms (task 143) vs the new light FLOOD at 12 m "
               "(play look, FSR 3; yellow = pixels the torch lights, native 1080)", font=big, fill=(255, 255, 255))
for j, a in enumerate(ARMS):
    x = LW + j * (W + 4) + 8
    d.text((x, 44), WORDS[a], font=big, fill=(255, 255, 120) if a.startswith("flood") else (255, 255, 255))
    d.text((x, 80), SUB[a], font=font, fill=(200, 200, 200))
for i, (r, label) in enumerate(ROWS):
    y = 44 + TH + i * (H + 4)
    for k, t in enumerate(label.split("\n")):
        d.text((8, y + 8 + k * 32), t, font=big, fill=(255, 255, 255))
    for j, a in enumerate(ARMS):
        sh.paste(cell(r, a), (LW + j * (W + 4), y))
p = os.path.join(outd, "torchflood158.png")
sh.save(p)
print("wrote", p)
