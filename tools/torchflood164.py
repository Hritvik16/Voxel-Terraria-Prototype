#!/usr/bin/env python3
"""Task 164: torch-flood direction terms, LARGE sheets. python3 -I tools/torchflood164.py <LookReview dir> <out dir> [launch log]
Per row (tunnel / room / ground / pillar), two images:
  row_<r>.png        3 x 2 grid of 960 x 540 tiles (play look, FSR 3): today (2.9 m) | 158 flood (-tfdir 0) |
                     -tfdir 1 central differences | -tfdir 2 torch-straight blend | -tfdir 3 flat | (legend)
  row_<r>_crops.png  the same arms, native 1080, the facet spot cropped and scaled 2x (nearest), 960 x 540 each
Counts torchflood164.txt: lit px (native, luma >= 10 above 'none'), mean luma, and the log's flood / probe lines."""
import os, sys, re
import numpy as np
from PIL import Image, ImageDraw, ImageFont

src, outd = sys.argv[1], sys.argv[2]
logp = sys.argv[3] if len(sys.argv) > 3 else None
font = ImageFont.load_default(size=24)
big = ImageFont.load_default(size=30)
TW, TH, HDR = 960, 540, 90
ARMS = ["today", "flood", "flooddir1", "flooddir2", "flooddir3"]
WORDS = {"today": "today (shipped): 2.9 m, 4 shadow rays", "flood": "158 flood: -tfdir 0 (one-step rise)",
         "flooddir1": "-tfdir 1: central differences 0.4 m", "flooddir2": "-tfdir 2: toward the torch on straight paths",
         "flooddir3": "-tfdir 3: flat (no direction)"}
# facet spot per row (native 1920 x 1080 pixels: x0, y0, w, h at 480 x 270 -> 2x = 960 x 540); override with CROP_<ROW>=x,y
CROPS = {"tunnel": (120, 120), "room": (720, 700), "ground": (720, 640), "pillar": (640, 420)}
LABEL = {"tunnel": "built tunnel, night, eye at the mouth", "room": "sealed room (5 m cube), night",
         "ground": "open ground, night, eye 12 m back", "pillar": "open ground + a 0.7 m pillar 1.5 m beside the torch"}
LOOK = {"tunnel": "the LEFT wall: 158 shows vertical stripes", "room": "the floor under the torch (158: a bright square) and the wall corners (straight wedges)",
        "ground": "the ground round the post", "pillar": "the pillar's faces and the ground behind it"}
for r in CROPS:
    e = os.environ.get("CROP_" + r.upper())
    if e:
        CROPS[r] = tuple(int(v) for v in e.split(","))


def load(n):
    p = os.path.join(src, n + ".png")
    return np.asarray(Image.open(p).convert("RGB")) if os.path.exists(p) else None


def luma(a):
    return a.astype(np.float32) @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)


def blank(w, h, t="(not shot)"):
    im = Image.new("RGB", (w, h), (40, 40, 40))
    ImageDraw.Draw(im).text((20, h // 2 - 15), t, font=big, fill=(160, 160, 160))
    return im


lines = ["lit px = native 1080 pixels >= 10 luma (0-255) above the same pose with the torch light off ('none')",
         f"{'row':8s} {'arm':10s} {'lit px':>10s} {'mean luma':>10s}"]
stat = {}
for r in CROPS:
    base = load(f"tr_{r}_none_nat")
    for a in ["none"] + ARMS:
        img = load(f"tr_{r}_{a}_nat")
        if img is None:
            continue
        L = luma(img)
        lit = int(((L - luma(base)) >= 10).sum()) if base is not None else -1
        stat[(r, a)] = (lit, float(L.mean()))
        lines.append(f"{r:8s} {a:10s} {lit:>10,d} {L.mean():10.1f}")
if logp and os.path.exists(logp):
    t = open(logp).read()
    lines.append("")
    for m in re.finditer(r"^\s+(flood \(.*|probe \(.*|pillar: .*)$", t, re.M):
        lines.append(m.group(1))
txt = "\n".join(lines)
print(txt)
os.makedirs(outd, exist_ok=True)
open(os.path.join(outd, "torchflood164.txt"), "w").write(txt + "\n")


def tag(im, a, r, extra=""):
    d = ImageDraw.Draw(im)
    t = WORDS[a] + extra
    d.rectangle((0, 0, 14 + int(d.textlength(t, font=big)), 40), fill=(0, 0, 0))
    d.text((7, 4), t, font=big, fill=(255, 255, 120) if a.startswith("flood") else (255, 255, 255))
    if (r, a) in stat:
        s = f"{stat[(r, a)][0]:,} lit px, mean luma {stat[(r, a)][1]:.1f}"
        d.rectangle((0, im.height - 34, 12 + int(d.textlength(s, font=font)), im.height), fill=(0, 0, 0))
        d.text((6, im.height - 32), s, font=font, fill=(255, 220, 80))
    return im


def sheet(r, crops):
    sh = Image.new("RGB", (3 * TW + 8, HDR + 2 * TH + 4), (30, 30, 30))
    d = ImageDraw.Draw(sh)
    what = "native 1080, 2x crop of the facet spot (nearest)" if crops else "play look (FSR 3 dyn), full frame"
    d.text((8, 6), f"{LABEL[r]} -- one torch, flood direction terms; {what}", font=big, fill=(255, 255, 255))
    d.text((8, 46), "LOOK AT: " + LOOK[r], font=big, fill=(255, 200, 120))
    cx, cy = CROPS[r]
    for j, a in enumerate(ARMS):
        img = load(f"tr_{r}_{a}_{'nat' if crops else 'play'}")
        if img is None:
            im = blank(TW, TH)
        elif crops:
            im = Image.fromarray(img[cy:cy + 270, cx:cx + 480]).resize((TW, TH), Image.NEAREST)
        else:
            im = Image.fromarray(img).resize((TW, TH), Image.LANCZOS)
            if j == 0:
                pass
        tag(im, a, r)
        if not crops:   # outline the crop box on every full tile
            dd = ImageDraw.Draw(im)
            sx = TW / img.shape[1] if img is not None else 0.5
            dd.rectangle((cx * sx, cy * sx, (cx + 480) * sx, (cy + 270) * sx), outline=(0, 220, 255), width=3)
        sh.paste(im, ((j % 3) * (TW + 4), HDR + (j // 3) * (TH + 4)))
    lg = Image.new("RGB", (TW, TH), (24, 24, 24))
    d2 = ImageDraw.Draw(lg)
    legend = ["Direction term = how a face's brightness depends on",
              "where the light comes from (flood level unchanged).",
              "0: rise to the next 0.2 m cell (158) -> facets",
              "1: 3-axis gradient over 0.4 m, normalised",
              "2: Lambert cosine toward the torch whose straight",
              "   distance matches the flood path; 1 where bent",
              "3: flat 0.5 (no direction)",
              "All: I 2.0, k 0.10, reach 12 m, wrap 0.3.",
              "Cyan box (full frames) = the crop sheet's area."]
    for k, t in enumerate(legend):
        d2.text((20, 20 + k * 44), t, font=big, fill=(220, 220, 220))
    sh.paste(lg, (2 * (TW + 4), HDR + TH + 4))
    p = os.path.join(outd, f"row_{r}{'_crops' if crops else ''}.png")
    sh.save(p)
    print("wrote", p)


for r in CROPS:
    sheet(r, False)
    sheet(r, True)
