#!/usr/bin/env python3
"""Task 225: what each candidate fix does to the displayed byte, per pixel group, from the SHADE TERMS stills.
usage: python3 -I sim.py <stills dir> <pose> <arm> [floor]
A = today: displayed = sRGB_encode(lit^2.2)               (LookToLinear = pow 2.2, the screen's encode = exact sRGB)
T = exact inverse: displayed = lit                        (LookToLinear = the sRGB EOTF; the stated contract)
S = sky share in linear light: skyOpen = lerp(floor, 1, skyVis^(1/2.2)) (ambient and the shadow floor follow skyOpen)
TS = both. Bytes are luma*255 medians (display)."""
import os, sys
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import probe as P

def enc(x):
    x = np.clip(x, 0, 1); return np.where(x <= 0.0031308, 12.92 * x, 1.055 * np.power(x, 1 / 2.4) - 0.055)

d, pose, arm = sys.argv[1], sys.argv[2], sys.argv[3]
floor = float(sys.argv[4]) if len(sys.argv) > 4 else 0.02
base = os.path.join(d, f"{pose}_{arm}")
ims = {v: P.load(f"{base}_shade{v}.png") for v in (15, 16, 17, 18)}
table, _, _ = P.calib(ims[15]); c = {v: P.decode(ims[v], table) for v in ims}
cls = c[15][..., 0]
ok = np.ones_like(cls, bool); ok[:95] = False; ok[:290, 1640:] = False; ok[505:880, :965] = False; ok[935:] = False
for dy, dx in ((3, 0), (-3, 0), (0, 3), (0, -3)): ok &= np.roll(cls, (dy, dx), axis=(0, 1)) == cls
mat, face, shell = cls & 3, (cls >> 2) & 3, (cls >> 4) & 1
sky, sun = P.Q(c[15][..., 1]), P.Q(c[15][..., 2])
dire, amb = P.L(c[17][..., 1]), P.L(c[17][..., 2])
lit = P.L(c[18][..., 1])
so = floor + (1 - floor) * sky
so2 = floor + (1 - floor) * np.power(sky, 1 / 2.2)
r = np.where(so > 0, so2 / np.maximum(so, 1e-6), 1.0)
dire2 = np.where(sun < 0.5, dire * r, dire)
lit2 = lit * (dire2 + amb * r) / np.maximum(dire + amb, 1e-6)
out = {"A today": enc(lit ** 2.2), "T exact": lit, "S sky lin": enc(lit2 ** 2.2), "TS both": lit2}
t0 = ok & (shell == 0)
groups = [("trunk side shade", t0 & (mat == 1) & (face == 1) & (sun < 0.5)), ("trunk side sun", t0 & (mat == 1) & (face == 1) & (sun >= 0.5)),
          ("leaf any", t0 & (mat == 2)), ("other up shade", t0 & (mat == 3) & (face == 0) & (sun < 0.5)),
          ("other up sun", t0 & (mat == 3) & (face == 0) & (sun >= 0.5)), ("other side shade", t0 & (mat == 3) & (face == 1) & (sun < 0.5)),
          ("other down", t0 & (mat == 3) & (face == 2))]
print(f"== {pose} {arm} (floor {floor}): median displayed byte; skyVis median")
print(f"   {'group':18s} {'px':>7s} {'skyVis':>6s} " + " ".join(f"{k:>10s}" for k in out))
for name, m in groups:
    k = int(m.sum())
    if k < 50: continue
    print(f"   {name:18s} {k:7d} {np.median(sky[m]):6.3f} " + " ".join(f"{255 * np.median(v[m]):10.1f}" for v in out.values()))
