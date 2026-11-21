# Task 233: tune the light grade OFFLINE on existing stills before the engine version (python3 -I, run from repo root).
# The engine applies the same formula per pixel in display space (Raymarch.compute Grade233). Requirement:
# graded mean luma within +-5 % of the un-graded mean (231's grade went too dark at low sun).
import numpy as np, sys
from PIL import Image
IMGS = ['Screenshots/226/day/forest_eye_full_clean.png', 'Screenshots/226/day/jungle_eye_full_clean.png',
        'Screenshots/226/day/walk_near_full_clean.png', 'Screenshots/231/raw/forest_eye_full_clean.png',
        'Screenshots/231/raw/jungle_eye_full_clean.png', 'Screenshots/231/raw/redwood_far1_full_clean.png']
def luma(a): return a[..., 0] * 0.2126 + a[..., 1] * 0.7152 + a[..., 2] * 0.0722
def sstep(e0, e1, x): t = np.clip((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t)
def grade(a, P):
    c = a.copy(); L = luma(c)
    green = sstep(0.0, 0.12, c[..., 1] - np.maximum(c[..., 0], c[..., 2]))
    sat = 1.0 - P['gs'] * green - P['ga']
    c = L[..., None] + (c - L[..., None]) * sat[..., None]
    c[..., 0] += P['olive'] * green * L                              # greens toward olive (a little red in)
    mid = (4 * L * (1 - L))[..., None]
    c = c + mid * np.array(P['warm'], np.float32)                    # warm midtones
    c = np.clip(c, 0, 1)
    # gentle S-curve pivoted at P['piv']: c + s * (c - piv) * c * (1 - c) * 4 (0 and 1 fixed, piv fixed)
    c = c + P['s'] * (c - P['piv']) * c * (1 - c) * 4
    c = c * (1 - P['lift']) + P['lift']
    h, w, _ = a.shape; yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    rr = ((xx - w / 2) / (w / 2)) ** 2 * 0.5 + ((yy - h / 2) / (h / 2)) ** 2 * 0.5   # 0 centre, 1 at the corners
    c = c * (1 - P['vig'] * rr * rr)[..., None]
    return np.clip(c, 0, 1)
P = dict(gs=0.22, ga=0.05, olive=0.06, warm=[0.025, 0.010, -0.022], s=0.18, piv=0.40, lift=0.02, vig=0.10)
for k, v in [x.split('=') for x in sys.argv[1:]]: P[k] = [float(t) for t in v.split(',')] if ',' in v else float(v)
print('params', P)
for p in IMGS:
    a = np.asarray(Image.open(p).convert('RGB').resize((640, 360))).astype(np.float32) / 255
    a[0:48, 552:640] = a[0:48, 552:640]   # HUD corner (clean stills have none)
    g = grade(a, P); m0, m1 = luma(a).mean(), luma(g).mean()
    ch0 = (a.max(-1) - a.min(-1)).mean(); ch1 = (g.max(-1) - g.min(-1)).mean()
    print(f'{p.split("/")[-3]}/{p.split("/")[-1]:32s} mean luma {m0:.3f} -> {m1:.3f} ({100*(m1/m0-1):+5.1f} %)  p10 {np.percentile(luma(a),10):.3f}->{np.percentile(luma(g),10):.3f}  chroma {ch0:.3f}->{ch1:.3f}')
