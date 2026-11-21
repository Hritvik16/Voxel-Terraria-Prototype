"""task 229: -costfloodpair identity. usage: python3 -I fnpair.py <stills dir> ...
raw vs raw2 = control (same setting, 10 frames apart); raw2 vs fnflip = the flood-twin rule flipped; fnflip vs fnback; raw2 vs fnback."""
import glob, os, sys
import numpy as np
from PIL import Image
def load(p): return np.asarray(Image.open(p).convert("RGB")).astype(np.int16)
def diff(a, b):
    d = np.abs(load(a) - load(b)); return int(np.count_nonzero(d.max(axis=2))), int(d.max()), float(d.mean())
for src in sys.argv[1:]:
    for r in sorted(glob.glob(os.path.join(src, "*_raw_fnflip.png"))):
        b = r[:-len("_raw_fnflip.png")]
        names = [b + s for s in ("_raw.png", "_raw2.png", "_raw_fnflip.png", "_raw_fnback.png")]
        if not all(os.path.exists(n) for n in names): print(os.path.basename(b), "missing"); continue
        out = []
        for i, j, t in ((0, 1, "control raw/raw2"), (1, 2, "raw2/flip"), (2, 3, "flip/back"), (1, 3, "raw2/back")):
            n, m, mu = diff(names[i], names[j]); out.append(f"{t} {n} px (max {m})")
        print(f"{os.path.basename(b):28s} " + " | ".join(out))
