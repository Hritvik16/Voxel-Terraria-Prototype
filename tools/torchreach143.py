#!/usr/bin/env python3
"""Task 143: torch reach side-by-side. python3 -I tools/torchreach143.py <LookReview dir> <out dir>
Lit-area counts per native 1080 shot -> torchreach143.txt:
  lit px  = pixels whose luma (Rec.709, 0-255) is >= 10 above the same pose with the torch's light unregistered ("none")
  bright  = pixels with luma >= 40 (clearly readable), and the mean luma
Sheet torchreach143.png: cells are the play look (FSR 3 dyn); rows = tunnel / sealed room / open ground at night;
columns = today | small room | half a tunnel | 10-15 m tunnel section. Arm values come from the launch log (argv[3])."""
import os, sys, re
import numpy as np
from PIL import Image, ImageDraw, ImageFont

src, outd = sys.argv[1], sys.argv[2]
logp = sys.argv[3] if len(sys.argv) > 3 else None
font = ImageFont.load_default(size=26)
big = ImageFont.load_default(size=30)
W, H, LW, TH = 640, 360, 250, 104
ROWS = [("tunnel", "built tunnel\nnight, eye at\nthe mouth"), ("room", "sealed room\n5 m cube,\nnight"),
        ("ground", "open ground\nnight, eye\n12 m back")]
ARMS = ["today", "room", "half", "tunnel"]
WORDS = {"today": "today: ~1-2 m glow", "room": "lights a small room", "half": "half a tunnel (~6 m)",
         "tunnel": "10-15 m tunnel section"}
vals = {}
if logp and os.path.exists(logp):
    for m in re.finditer(r"^\s+(today|room|half|tunnel)\s+k ([\d.]+)\s+intensity ([\d.]+)\s+range ([\d.]+)", open(logp).read(), re.M):
        vals[m.group(1)] = (float(m.group(2)), float(m.group(3)), float(m.group(4)))


def load(n):
    p = os.path.join(src, n + ".png")
    return np.asarray(Image.open(p).convert("RGB")).astype(np.float32) if os.path.exists(p) else None


def luma(a):
    return a @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)


def reach(k, i, thr):
    """metres where i / (1 + k d^2) falls to thr (colour units, before albedo / N.L / tonemap)."""
    return ((i / thr - 1.0) / k) ** 0.5 if i > thr else 0.0


lines = ["lit px = native 1080 pixels >= 10 luma (0-255) above the same pose with the torch light off ('none'); "
         "bright = luma >= 40", f"{'row':8s} {'arm':8s} {'lit px':>10s} {'bright px':>10s} {'mean luma':>10s}"]
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
lines.append("")
lines.append("model reach (atten x intensity = 0.4 / 0.2 colour units; range = hard cutoff; value AT the cutoff):")
for a in ARMS:
    if a in vals:
        k, i, rg = vals[a]
        lines.append(f"  {a:7s} k {k:.3f} intensity {i:.2f} range {rg:4.1f} m -> {reach(k, i, 0.4):4.1f} m / {reach(k, i, 0.2):4.1f} m; "
                     f"at cutoff {i / (1 + k * rg * rg):.3f}")
txt = "\n".join(lines)
print(txt)
os.makedirs(outd, exist_ok=True)
open(os.path.join(outd, "torchreach143.txt"), "w").write(txt + "\n")


def cell(r, a):
    p = load(f"tr_{r}_{a}_play")
    if p is None:
        return Image.new("RGB", (W, H), (60, 0, 0))
    im = Image.fromarray(p.astype(np.uint8)).resize((W, H), Image.LANCZOS)
    d = ImageDraw.Draw(im)
    if (r, a) in stat:
        t = f"{stat[(r, a)]:,} lit px"
        d.rectangle((0, H - 34, 10 + int(d.textlength(t, font=font)), H), fill=(0, 0, 0))
        d.text((6, H - 32), t, font=font, fill=(255, 220, 80))
    return im


sh = Image.new("RGB", (LW + len(ARMS) * (W + 4), 44 + TH + len(ROWS) * (H + 4)), (30, 30, 30))
d = ImageDraw.Draw(sh)
d.text((8, 6), "One torch at night: how far should it reach? (play look, FSR 3; yellow = pixels the torch "
               "lights, native 1080)", font=big, fill=(255, 255, 255))
for j, a in enumerate(ARMS):
    x = LW + j * (W + 4) + 8
    d.text((x, 44), WORDS[a], font=big, fill=(255, 255, 255))
    if a in vals:
        k, i, rg = vals[a]
        d.text((x, 80), f"falloff k {k:g}, intensity {i:g}, range {rg:g} m", font=font, fill=(200, 200, 200))
for i, (r, label) in enumerate(ROWS):
    y = 44 + TH + i * (H + 4)
    for k, t in enumerate(label.split("\n")):
        d.text((8, y + 8 + k * 32), t, font=big, fill=(255, 255, 255))
    for j, a in enumerate(ARMS):
        sh.paste(cell(r, a), (LW + j * (W + 4), y))
p = os.path.join(outd, "torchreach143.png")
sh.save(p)
print("wrote", p)
