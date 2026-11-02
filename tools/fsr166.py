#!/usr/bin/env python3
"""Task 166: why FSR 3 softens the picture and draws dark dotted block edges -- stills vs native.

python3 -I tools/fsr166.py <LookReview dir> <out dir> <arm,arm,...> [crop-ref-arm] [burst arms]

Arms are LookReview shot suffixes (native, fsr3_432, fsr3_432_s0, none_432, ...); "native" must be first.
Per pose and arm vs native: mean |dlum| (0-255), % px whose luminance differs by > 8 levels, % px DARKER by > 8
levels (the dotted outlines are darker than both faces), mean signed dlum (arm - native), and an edge-sharpness
ratio (mean |gradient| of the arm / of native; < 1 = softer).
<out>/fsr166_crops_<pose>.png: the 2 240x135 windows where crop-ref-arm (default fsr3_432) is darker than native the
most, 2x nearest neighbour, every arm in a row (2 rows of arms if > 5).
Writes <out>/fsr166.txt (the table) and copies nothing: full-size shots stay in the LookReview dir.
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

POSES = ["walk_near", "walk_open", "high", "cliff", "torch"]
CW, CH, SCALE = 240, 135, 2


def load(p):
    return np.asarray(Image.open(p).convert("RGB"), np.float32)


def lum(a):
    return a[..., 0] * 0.299 + a[..., 1] * 0.587 + a[..., 2] * 0.114


def grad(l):
    return np.abs(np.diff(l, axis=0))[:, :-1].mean() + np.abs(np.diff(l, axis=1))[:-1, :].mean()


def tag(img, text, xy=(6, 4), col=(255, 255, 0)):
    d = ImageDraw.Draw(img)
    x, y = xy
    d.rectangle([x - 3, y - 2, x + 6 * len(text) + 5, y + 12], fill=(0, 0, 0))
    d.text((x, y), text, fill=col)


def top_windows(score, n=2):
    h, w = score.shape
    ii = np.pad(score, ((1, 0), (1, 0))).cumsum(0).cumsum(1)
    cands = []
    for y in range(0, h - CH + 1, 15):
        for x in range(0, w - CW + 1, 15):
            s = ii[y + CH, x + CW] - ii[y, x + CW] - ii[y + CH, x] + ii[y, x]
            cands.append((s / (CW * CH), x, y))
    cands.sort(reverse=True)
    out = []
    for s, x, y in cands:
        if all(abs(x - ox) >= CW or abs(y - oy) >= CH for _, ox, oy in out):
            out.append((s, x, y))
        if len(out) == n:
            break
    return out


def motion(src, dst, arms, rep):
    """walk_open still / walk / turn bursts (4 frames after 60 lead): 159's flicker (max - min over the 4 frames,
    by band top -> bottom) and frame 0 vs native's frame 0 (same camera; under motion a ghost or a smear raises it)."""
    rep.append("motion bursts at walk_open: flicker mean (bands top->bottom) | frame0 vs native frame0 mean|dlum| / %px>8")
    tiles = []
    for mo in ["still", "walk", "turn"]:
        nat = None
        for a in arms:
            fs = [os.path.join(src, f"walk_open_{a}_{mo}_{i}.png") for i in range(4)]
            if not all(os.path.exists(f) for f in fs):
                rep.append(f"  {a} {mo}: missing")
                continue
            fr = [lum(load(f)) for f in fs]
            st = np.stack(fr)
            fl = st.max(0) - st.min(0)
            h = fl.shape[0]
            bands = " ".join(f"{fl[h * k // 5:h * (k + 1) // 5].mean():5.2f}" for k in range(5))
            if a == "native":
                nat = fr[0]
            vs = "" if nat is None or a == "native" else f" | {np.abs(fr[0] - nat).mean():5.2f} / {100 * (np.abs(fr[0] - nat) > 8).mean():4.1f}%"
            rep.append(f"  {a:<16} {mo:5s} flicker {fl.mean():5.2f} ({bands}){vs}")
            if mo != "still":
                c = Image.open(fs[0]).convert("RGB").crop((1200, 560, 1200 + CW * 2, 560 + CH * 2))
                tag(c, f"{a} {mo} frame 0")
                tiles.append(c)
    if tiles:
        w, h = tiles[0].size
        per = len(arms)
        S = Image.new("RGB", (per * (w + 4), ((len(tiles) + per - 1) // per) * (h + 4)), (30, 30, 30))
        for i, t in enumerate(tiles):
            S.paste(t, ((i % per) * (w + 4), (i // per) * (h + 4)))
        S.save(os.path.join(dst, "fsr166_motion.png"))


def main():
    src, dst, arms = sys.argv[1], sys.argv[2], sys.argv[3].split(",")
    ref = sys.argv[4] if len(sys.argv) > 4 else "fsr3_432"
    os.makedirs(dst, exist_ok=True)
    rep = [f"task 166 stills vs native 1080; source {src}",
           "per arm: mean|dlum| / %px>8 / %px darker>8 / mean signed dlum / gradient ratio (arm/native, <1 softer)"]
    for p in POSES:
        files = {a: os.path.join(src, f"{p}_{a}.png") for a in arms}
        have = [a for a in arms if os.path.exists(files[a])]
        if "native" not in have:
            rep.append(f"{p}: no native shot -- skipped")
            continue
        ims = {a: Image.open(files[a]).convert("RGB") for a in have}
        L = {a: lum(np.asarray(ims[a], np.float32)) for a in have}
        g0 = grad(L["native"])
        rep.append(f"== {p}" + (f" (missing: {', '.join(a for a in arms if a not in have)})" if len(have) < len(arms) else ""))
        for a in have[1:]:
            d = L[a] - L["native"]
            rep.append(f"  {a:<18} {np.abs(d).mean():5.2f}  {100 * (np.abs(d) > 8).mean():5.1f}%  "
                       f"{100 * (d < -8).mean():5.1f}%  {d.mean():+6.2f}  {grad(L[a]) / g0:5.2f}")
        r = ref if ref in have else have[-1]
        dark = np.maximum(L["native"] - L[r], 0)
        wins = top_windows(dark)
        per = 5
        nrow = (len(have) + per - 1) // per
        w, h = CW * SCALE, CH * SCALE
        sheet = Image.new("RGB", (per * (w + 4), len(wins) * nrow * (h + 4)), (30, 30, 30))
        for k, (sc, x, y) in enumerate(wins):
            for i, a in enumerate(have):
                c = ims[a].crop((x, y, x + CW, y + CH)).resize((w, h), Image.NEAREST)
                tag(c, f"{p} #{k + 1} ({x},{y}) {a}")
                sheet.paste(c, ((i % per) * (w + 4), (k * nrow + i // per) * (h + 4)))
        sheet.save(os.path.join(dst, f"fsr166_crops_{p}.png"))
        rep.append(f"  crops (darkest vs native in {r}): " + ", ".join(f"#{k + 1} x{x} y{y} {s:.1f}" for k, (s, x, y) in enumerate(wins)))
    if len(sys.argv) > 5:
        motion(src, dst, sys.argv[5].split(","), rep)
    open(os.path.join(dst, "fsr166.txt"), "w").write("\n".join(rep) + "\n")
    print("\n".join(rep))


if __name__ == "__main__":
    main()
