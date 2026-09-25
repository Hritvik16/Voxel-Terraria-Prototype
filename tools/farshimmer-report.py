#!/usr/bin/env python3
"""farshimmer-report.py <shimmer folder>: for each pose and arm (off/on), the
luma change between the two quarter-pixel-nudged frames, over non-sky pixels.
Lower = more stable under sub-pixel motion (less shimmer)."""
import glob, os, sys
import numpy as np
from PIL import Image
src = sys.argv[1]
def L(p):
    a = np.asarray(Image.open(p).convert("RGB").resize((960, 540), Image.NEAREST)).astype(np.float32)
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
print(f"{'pose':<11}{'arm':<5}{'mean |dL|':>10}{'>24/255 %':>11}{'  (non-sky pixels)':>20}")
for a0 in sorted(glob.glob(os.path.join(src, "*_off_0.png"))):
    n = os.path.basename(a0)[:-10]
    for arm in ("off", "on"):
        x0, x1 = L(os.path.join(src, f"{n}_{arm}_0.png")), L(os.path.join(src, f"{n}_{arm}_1.png"))
        sky = (np.abs(x0 - x1) < 0.5) & (x0 > 40) & (np.arange(540)[:, None] < 200)   # crude: static bright rows up top
        m = ~sky
        d = np.abs(x0 - x1)[m]
        print(f"{n:<11}{arm:<5}{d.mean():>10.2f}{100 * (d > 24).mean():>11.2f}")
