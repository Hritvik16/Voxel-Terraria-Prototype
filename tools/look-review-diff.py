#!/usr/bin/env python3
"""Task 114: difference / flicker images for run-look-review.sh's motion bursts, and crops.

  look-review-diff.py bursts DIR            every <name>_0..3.png set in DIR:
        <name>_diff01.png  |frame1 - frame0| x4 (grey)
        <name>_flicker.png max - min over the 4 frames x4 (grey)
        and one line per set: mean |diff| (0-255) over the whole frame and over
        five horizontal bands (top = sky / far, bottom = near ground)
  look-review-diff.py crop SRC X Y W H SCALE OUT   nearest-neighbour crop, SCALE x
  look-review-diff.py sheet OUT COLS SRC...         contact sheet, 1/2 size tiles
"""
import glob
import os
import sys

import numpy as np
from PIL import Image


def load(p):
    return np.asarray(Image.open(p).convert("RGB"), dtype=np.float32)


def grey(a):
    return a.mean(axis=2)


def bursts(d):
    for f0 in sorted(glob.glob(os.path.join(d, "*_0.png"))):
        base = f0[:-6]
        fs = [f"{base}_{i}.png" for i in range(4)]
        if not all(os.path.exists(f) for f in fs):
            continue
        fr = [grey(load(f)) for f in fs]
        d01 = np.abs(fr[1] - fr[0])
        st = np.stack(fr)
        fl = st.max(axis=0) - st.min(axis=0)
        Image.fromarray(np.clip(d01 * 4, 0, 255).astype(np.uint8)).save(base + "_diff01.png")
        Image.fromarray(np.clip(fl * 4, 0, 255).astype(np.uint8)).save(base + "_flicker.png")
        h = fl.shape[0]
        bands = " ".join(f"{fl[h * k // 5:h * (k + 1) // 5].mean():5.2f}" for k in range(5))
        print(f"{os.path.basename(base):34s} diff01 {d01.mean():5.2f}  flicker {fl.mean():5.2f}  flicker by band top->bottom {bands}")


def crop(src, x, y, w, h, s, out):
    im = Image.open(src).convert("RGB").crop((x, y, x + w, y + h))
    im.resize((w * s, h * s), Image.NEAREST).save(out)


def sheet(out, cols, srcs):
    ims = [Image.open(s).convert("RGB") for s in srcs]
    tw, th = ims[0].width // 2, ims[0].height // 2
    rows = (len(ims) + cols - 1) // cols
    S = Image.new("RGB", (tw * cols, th * rows), (0, 0, 0))
    for i, im in enumerate(ims):
        S.paste(im.resize((tw, th), Image.LANCZOS), ((i % cols) * tw, (i // cols) * th))
    S.save(out)


if __name__ == "__main__":
    if sys.argv[1] == "bursts":
        bursts(sys.argv[2])
    elif sys.argv[1] == "crop":
        a = sys.argv
        crop(a[2], int(a[3]), int(a[4]), int(a[5]), int(a[6]), int(a[7]), a[8])
    elif sys.argv[1] == "sheet":
        sheet(sys.argv[2], int(sys.argv[3]), sys.argv[4:])
