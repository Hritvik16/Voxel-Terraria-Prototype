#!/usr/bin/env python3
"""Task 168: identity of the shadow roof skip from cost-split raw stills.

  python3 -I tools/roof168.py pair <costsplit stills dir>
      -costroofpair: raw vs raw2 (control, 10 frames), raw2 vs roofflip (roof skip flipped), roofflip vs roofback,
      raw2 vs roofback (back to the launch's setting).
  python3 -I tools/roof168.py arms <costsplit stills dir>
      <path>_roof_on_raw*.png vs <path>_full_raw*.png (a minute apart: water may move in between).
"""
import glob, os, sys
import numpy as np
from PIL import Image

def load(p): return np.asarray(Image.open(p).convert("RGB")).astype(np.int16)

def diff(a, b):
    d = np.abs(load(a) - load(b)).max(axis=2)
    return int(np.count_nonzero(d)), int(d.max())

def pair(src):
    for r in sorted(glob.glob(os.path.join(src, "*_raw_roofflip.png"))):
        b = r[:-len("_raw_roofflip.png")]
        names = [b + s for s in ("_raw.png", "_raw2.png", "_raw_roofflip.png", "_raw_roofback.png")]
        if not all(os.path.exists(n) for n in names): continue
        out = []
        for i, j, t in ((0, 1, "control raw/raw2"), (1, 2, "raw2/flip"), (2, 3, "flip/back"), (1, 3, "raw2/back")):
            n, m = diff(names[i], names[j]); out.append(f"{t} {n} px (max {m})")
        print(f"{os.path.basename(b):24s} " + " | ".join(out))

def arms(src):
    for p1 in sorted(glob.glob(os.path.join(src, "*_roof_on_raw*.png"))):
        p0 = p1.replace("_roof_on_", "_full_")
        if os.path.exists(p0):
            n, m = diff(p0, p1)
            print(f"{os.path.basename(p1)[:-4]:36s} vs FULL: {n} px differ (max channel {m})")

if __name__ == "__main__":
    {"pair": lambda: pair(sys.argv[2]), "arms": lambda: arms(sys.argv[2])}[sys.argv[1]]()
