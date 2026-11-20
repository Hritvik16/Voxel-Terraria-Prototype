"""task 244: -raystats identity stills. usage: python3 -I tools/rspair244.py <stills dir> ...
For every *_raw_rson.png: md5 of rsoff / rsoff2 (control, counters OFF, 10 frames apart) / rson (counters ON) / rsback (OFF
again), all taken with the temporal frame counters frozen (RaymarchFeature.DebugFreezeTemporal), and the differing pixels.
PASS = all four byte-identical in pixels."""
import glob, hashlib, os, sys
import numpy as np
from PIL import Image
def load(p): return np.asarray(Image.open(p).convert("RGB")).astype(np.int16)
def diff(a, b):
    d = np.abs(load(a) - load(b)); return int(np.count_nonzero(d.max(axis=2))), int(d.max())
bad = 0
for src in sys.argv[1:]:
    for r in sorted(glob.glob(os.path.join(src, "*_raw_rson.png"))):
        b = r[:-len("_raw_rson.png")]
        names = [b + s for s in ("_raw_rsoff.png", "_raw_rsoff2.png", "_raw_rson.png", "_raw_rsback.png")]
        if not all(os.path.exists(n) for n in names): print(os.path.basename(b), "missing"); bad += 1; continue
        h = [hashlib.md5(load(n).tobytes()).hexdigest()[:12] for n in names]
        out = []
        if os.path.exists(b + "_raw_rsdiag.png"):   # the *Diag kernel, counters OFF
            names.append(b + "_raw_rsdiag.png")
        for i, j, t in ((0, 1, "control off/off2"), (1, 2, "off2/on"), (2, 3, "on/back"))[:3] + \
                       (((3, 4, "back/diag (kernel)"), (2, 4, "on/diag (counters)")) if len(names) == 5 else ()):
            n, m = diff(names[i], names[j]); out.append(f"{t} {n} px (max {m})")
        h += [hashlib.md5(load(n).tobytes()).hexdigest()[:12] for n in names[4:]]
        ok = len(set(h)) == 1
        bad += 0 if ok else 1
        print(f"{os.path.basename(b):24s} {'IDENTICAL' if ok else 'DIFFERENT'} pixel md5 {' '.join(h)} | " + " | ".join(out))
print("PASS" if bad == 0 else f"FAIL ({bad})")
