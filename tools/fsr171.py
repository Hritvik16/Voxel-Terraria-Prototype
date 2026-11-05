#!/usr/bin/env python3
"""Task 171: FSR 3 F2 (-fsr3sc 10) under live light and moving water -- bursts vs native.

python3 -I tools/fsr171.py <LookReview dir> <out dir> [arms, default native,fsr3_432,fsr3_432_sc10,fsr3_540,fsr3_540_sc10]

Shots are <scene>_<arm>_<k>.png (PlaygroundCapture.FsrLive.cs). Every scene except "pour" is stepped per frame,
so native frame k is the truth for arm frame k:
  err      mean |dlum| vs native over the frame (0-255), and % px off by > 24 levels (a ghost or a lag is large)
  chg      the same inside the CHANGE mask: px where native itself changes by > 12 levels across the burst
           (the moving shadow / water / torch light) -- where a ghost trail or a lagging light would sit
For torch_on / torch_off, frame 0 is the frame BEFORE the light change and 1-6 after: chg per frame shows the lag.
Sheets (<out>/fsr171_<scene>.png): the 160 x 90 window where fsr3_432_sc10 is furthest from native inside the
change mask, 4x nearest neighbour; row 1 native / fsr3_432 / fsr3_432_sc10, row 2 native / fsr3_540 / fsr3_540_sc10.
<out>/fsr171_<scene>_full.png: today 432 | sc10 432, whole frame at half size, the window boxed.
"pour" (fluid sim on the wall clock, NOT frame-exact): sheets only, window where native's water moves most.
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

CW, CH, Z = 160, 90, 4
SCENES = [("sun_still", 4), ("sun_play", 4), ("sun_fast", 4), ("slide", 4), ("torch_still", 4),
          ("torch_move", 4), ("torch_on", 7), ("torch_off", 7)]


def load(p):
    return np.asarray(Image.open(p).convert("RGB"), np.float32)


def lum(a):
    return a[..., 0] * 0.299 + a[..., 1] * 0.587 + a[..., 2] * 0.114


def tag(img, text, xy=(6, 4), col=(255, 255, 0)):
    d = ImageDraw.Draw(img)
    x, y = xy
    d.rectangle([x - 3, y - 2, x + 6 * len(text) + 5, y + 12], fill=(0, 0, 0))
    d.text((x, y), text, fill=col)


def best_window(score):
    h, w = score.shape
    ii = np.pad(score, ((1, 0), (1, 0))).cumsum(0).cumsum(1)
    best = (-1.0, 0, 0)
    for y in range(0, h - CH + 1, 10):
        for x in range(0, w - CW + 1, 10):
            s = ii[y + CH, x + CW] - ii[y, x + CW] - ii[y + CH, x] + ii[y, x]
            if s / (CW * CH) > best[0]:
                best = (s / (CW * CH), x, y)
    return best


def frames(src, scene, arm, n):
    fs = [os.path.join(src, f"{scene}_{arm}_{k}.png") for k in range(n)]
    return fs if all(os.path.exists(f) for f in fs) else None


def sheet(dst, name, imgs, x, y, label):
    """imgs: dict arm -> PIL image; rows (native, 432, 432_sc10), (native, 540, 540_sc10)."""
    rows = [["native", "fsr3_432", "fsr3_432_sc10"], ["native", "fsr3_540", "fsr3_540_sc10"]]
    rows = [r for r in rows if all(a in imgs for a in r)]
    if not rows:
        return
    w, h = CW * Z, CH * Z
    S = Image.new("RGB", (3 * (w + 4), len(rows) * (h + 4)), (30, 30, 30))
    names = {"native": "native 1080", "fsr3_432": "today 432", "fsr3_432_sc10": "sc10 432",
             "fsr3_540": "today 540", "fsr3_540_sc10": "sc10 540"}
    for r, row in enumerate(rows):
        for c, a in enumerate(row):
            t = imgs[a].crop((x, y, x + CW, y + CH)).resize((w, h), Image.NEAREST)
            tag(t, f"{label} {names[a]}  ({x},{y}) 4x")
            S.paste(t, (c * (w + 4), r * (h + 4)))
    S.save(os.path.join(dst, f"fsr171_{name}.png"))
    if "fsr3_432" in imgs and "fsr3_432_sc10" in imgs:
        F = Image.new("RGB", (2 * 964, 540), (30, 30, 30))
        for c, a in enumerate(["fsr3_432", "fsr3_432_sc10"]):
            t = imgs[a].resize((960, 540), Image.BILINEAR)
            d = ImageDraw.Draw(t)
            d.rectangle([x // 2, y // 2, (x + CW) // 2, (y + CH) // 2], outline=(255, 0, 255), width=2)
            tag(t, f"{label} {names[a]} (magenta box = the 4x window)")
            F.paste(t, (c * 964, 0))
        F.save(os.path.join(dst, f"fsr171_{name}_full.png"))


def main():
    src, dst = sys.argv[1], sys.argv[2]
    arms = (sys.argv[3] if len(sys.argv) > 3 else "native,fsr3_432,fsr3_432_sc10,fsr3_540,fsr3_540_sc10").split(",")
    os.makedirs(dst, exist_ok=True)
    rep = [f"task 171: FSR 3 today vs sc10 under live light / moving water; source {src}",
           "per arm, mean over the kept frames: err = mean|dlum| vs native / %px > 24;  chg = the same inside the change mask"]
    for scene, n in SCENES:
        fsx = {a: frames(src, scene, a, n) for a in arms}
        if fsx.get("native") is None:
            rep.append(f"== {scene}: no native burst -- skipped")
            continue
        nat = [lum(load(f)) for f in fsx["native"]]
        st = np.stack(nat)
        mask = (st.max(0) - st.min(0)) > 12
        rep.append(f"== {scene}: change mask {100 * mask.mean():.2f}% of the frame")
        post = range(1, n) if scene in ("torch_on", "torch_off") else range(n)
        L = {}
        for a in arms[1:]:
            if fsx[a] is None:
                rep.append(f"  {a:<16} missing")
                continue
            L[a] = [lum(load(f)) for f in fsx[a]]
            d = [np.abs(L[a][k] - nat[k]) for k in range(n)]
            err = np.mean([d[k].mean() for k in post])
            big = np.mean([100 * (d[k] > 24).mean() for k in post])
            line = f"  {a:<16} err {err:5.2f} / {big:5.2f}%"
            if mask.any():
                cm = np.mean([d[k][mask].mean() for k in post])
                cb = np.mean([100 * (d[k][mask] > 24).mean() for k in post])
                line += f"   chg {cm:5.2f} / {cb:5.2f}%"
                if scene in ("torch_on", "torch_off"):
                    line += "   chg per frame (0 = before) " + " ".join(f"{d[k][mask].mean():5.2f}" for k in range(n))
            rep.append(line)
        kk = 1 if scene in ("torch_on", "torch_off") else n - 1
        ref = "fsr3_432_sc10" if "fsr3_432_sc10" in L else (list(L)[-1] if L else None)
        if ref is None:
            continue
        score = np.abs(L[ref][kk] - nat[kk]) * (mask if mask.any() else 1)
        s, x, y = best_window(score)
        imgs = {a: Image.open(fsx[a][kk]).convert("RGB") for a in arms if fsx.get(a)}
        sheet(dst, scene, imgs, x, y, f"{scene} f{kk}")
        rep.append(f"  4x window ({x},{y}) frame {kk}: where {ref} is furthest from native in the mask ({s:.2f})")
    for r in range(2):
        scene = f"pour_r{r}"
        fsx = {a: frames(src, scene, a, 4) for a in arms}
        have = {a: f for a, f in fsx.items() if f}
        if not have:
            rep.append(f"== {scene}: no shots")
            continue
        mv = sum(np.abs(lum(load(f[3])) - lum(load(f[0]))) for f in have.values())
        s, x, y = best_window(mv)
        sheet(dst, scene, {a: Image.open(f[3]).convert("RGB") for a, f in have.items()}, x, y, f"{scene} f3")
        fl = []
        for a, f in have.items():
            stk = np.stack([lum(load(p)) for p in f])
            win = stk[:, y:y + CH, x:x + CW]
            fl.append(f"{a} {np.abs(np.diff(win, axis=0)).mean():5.2f}")
        rep.append(f"== {scene} (not frame-exact): window ({x},{y}) where the water moves most; frame-to-frame mean|dlum| "
                   f"in it: " + ", ".join(fl))
    open(os.path.join(dst, "fsr171.txt"), "w").write("\n".join(rep) + "\n")
    print("\n".join(rep))


if __name__ == "__main__":
    main()
