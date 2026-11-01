#!/usr/bin/env python3
"""Task 161: identity of kernel diet 2 (-diet2), from raw/native stills.

  python3 -I tools/diet2_161.py raw <costsplit stills dir>
      <path>_diet2_on_raw*.png vs <path>_diet2_off_raw*.png, and full_raw* vs diet2_off_raw* (FULL runs -diet2 1).
  python3 -I tools/diet2_161.py pair <costsplit stills dir>
      -costd2pair: raw vs raw2 (same setting, the control), raw2 vs raw_d2flip (diet 2 flipped), d2flip vs d2back.
  python3 -I tools/diet2_161.py look <LookReview dir>
      -cliffbefore 5 pairs: *_native0 / *_play0 = diet 2 OFF, *_native / *_play = ON.
FSR 3 'play' stills are temporal/jittered: listed, not an identity measure.
"""
import glob, os, sys
import numpy as np
from PIL import Image

def load(p): return np.asarray(Image.open(p).convert("RGB")).astype(np.int16)

def diff(a, b):
    d = np.abs(load(a) - load(b))
    return int(np.count_nonzero(d.max(axis=2))), int(d.max())

def raw(src):
    for on in ("diet2_on", "full"):
        for p1 in sorted(glob.glob(os.path.join(src, f"*_{on}_raw*.png"))):
            p0 = p1.replace(f"_{on}_", "_diet2_off_")
            if os.path.exists(p0):
                n, m = diff(p0, p1)
                print(f"{os.path.basename(p1)[:-4]:40s} diet2 OFF vs ON: {n} px differ (max channel {m})")

def pair(src):
    for r in sorted(glob.glob(os.path.join(src, "*_raw_d2flip.png"))):
        b = r[:-len("_raw_d2flip.png")]
        names = [b + s for s in ("_raw.png", "_raw2.png", "_raw_d2flip.png", "_raw_d2back.png")]
        if not all(os.path.exists(n) for n in names): continue
        out = []
        for i, j, t in ((0, 1, "control raw/raw2"), (1, 2, "raw2/flip"), (2, 3, "flip/back"), (1, 3, "raw2/back")):
            n, m = diff(names[i], names[j]); out.append(f"{t} {n} px (max {m})")
        print(f"{os.path.basename(b):24s} " + " | ".join(out))

def look(src):
    for p0 in sorted(glob.glob(os.path.join(src, "*0.png"))):
        p1 = p0[:-5] + ".png"
        if os.path.exists(p1):
            n, m = diff(p0, p1)
            print(f"{os.path.basename(p1)[:-4]:24s} diet2 OFF vs ON: {n} px differ (max channel {m})")

if __name__ == "__main__":
    {"raw": lambda: raw(sys.argv[2]), "pair": lambda: pair(sys.argv[2]), "look": lambda: look(sys.argv[2])}[sys.argv[1]]()
