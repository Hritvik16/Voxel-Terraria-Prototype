#!/usr/bin/env python3
"""Task 225: decode the SHADE TERMS stills (debug views 15-18, Raymarch.compute ShadeProbeColor).

usage: python3 -I Logs/overnight/225/probe.py <stills dir> <pose> <arm> [<arm> ...]
Reads <dir>/<pose>_<arm>_shade15..18.png (+ <pose>_<arm>_clean.png for the beauty bytes) and prints,
per pixel group, the median of each term. Codes: byte = 4 * code + 2 written by the kernel; the
calibration ramp (rows 0-3 of the dispatch, 16 dispatch px per code) maps output bytes back to codes.
Groups: trunk side faces in shade (sunVis < 0.5) / in sun, trunk tops, leaves, other up faces in
shade / in sun, other side faces in shade. Only tier-0 hits (shell bit clear). The minimap corner
(x > 1640, y < 290) and code edges (a 3 px neighbourhood with another class) are left out.
"""
import os, sys
import numpy as np
from PIL import Image

def load(p): return np.asarray(Image.open(p).convert("RGB")).astype(np.int32)

def calib(img):
    """Output byte -> code table. The kernel's byte (4 code + 2) / 255 is stored linear and sRGB-encoded on
    output (measured on task 225's stills: code 0 -> 22, 3 -> 66, 5 -> 83, 6 -> 90); the ramp itself sits
    under the HUD in cost-split stills, so the table is the sRGB curve."""
    x = (np.arange(64) * 4 + 2) / 255.0
    y = np.where(x <= 0.0031308, 12.92 * x, 1.055 * x ** (1 / 2.4) - 0.055)
    return y * 255.0, 64, 1 / 2.4

def decode(img, table):
    lut = np.argmin(np.abs(np.arange(256)[:, None] - table[None, :]), axis=1)
    return lut[img]   # h, w, 3 codes

def L(code): return np.where(code == 0, 0.0, 2.0 ** (code * 12.0 / 63.0 - 10.0))
def Q(code): return code / 63.0

def main():
    d, pose, arms = sys.argv[1], sys.argv[2], sys.argv[3:]
    for arm in arms:
        base = os.path.join(d, f"{pose}_{arm}")
        ims = {v: load(f"{base}_shade{v}.png") for v in (15, 16, 17, 18)}
        beauty = load(f"{base}_clean.png") if os.path.exists(f"{base}_clean.png") else None
        table, n, g = calib(ims[15])
        c = {v: decode(ims[v], table) for v in ims}
        cls = c[15][..., 0]
        h, w = cls.shape
        ok = np.ones_like(cls, bool)
        ok[:95, :] = False             # HUD: top text rows
        ok[:290, 1640:] = False         # minimap
        ok[505:880, :965] = False       # the edit / fluid panel
        ok[935:, :] = False             # hotbar
        for dy, dx in ((3, 0), (-3, 0), (0, 3), (0, -3)):
            ok &= np.roll(cls, (dy, dx), axis=(0, 1)) == cls
        mat, face, shell = cls & 3, (cls >> 2) & 3, (cls >> 4) & 1
        sky, sun = Q(c[15][..., 1]), Q(c[15][..., 2])
        S, B, vao = Q(c[16][..., 0]), Q(c[16][..., 1]), Q(c[16][..., 2])
        alb, dire, amb = L(c[17][..., 0]), L(c[17][..., 1]), L(c[17][..., 2])
        sop, lit, fin = Q(c[18][..., 0]), L(c[18][..., 1]), L(c[18][..., 2])
        t0 = ok & (shell == 0)
        groups = [("trunk side shade", t0 & (mat == 1) & (face == 1) & (sun < 0.5)),
                  ("trunk side sun", t0 & (mat == 1) & (face == 1) & (sun >= 0.5)),
                  ("leaf any", t0 & (mat == 2)),
                  ("other up shade", t0 & (mat == 3) & (face == 0) & (sun < 0.5)),
                  ("other up sun", t0 & (mat == 3) & (face == 0) & (sun >= 0.5)),
                  ("other side shade", t0 & (mat == 3) & (face == 1) & (sun < 0.5))]
        print(f"== {pose} {arm}: ramp codes {n}, out = in^{g:.3f}; tier-0 px {int(t0.sum())}, shell px {int((ok & (shell == 1) & (mat > 0)).sum())}")
        print(f"   {'group':18s} {'px':>7s} {'skyVis':>6s} {'sunVis':>6s} {'S':>5s} {'B':>5s} {'VAO':>5s} {'skyOpn':>6s} {'albedo':>6s} {'direct':>7s} {'ambient':>7s} {'lit':>7s} {'final':>7s} {'beauty':>6s}")
        for name, m in groups:
            k = int(m.sum())
            if k < 50: print(f"   {name:18s} {k:7d}  (too few)"); continue
            med = lambda a: float(np.median(a[m]))
            bt = float(np.median(beauty[m].mean(axis=1))) if beauty is not None else -1
            print(f"   {name:18s} {k:7d} {med(sky):6.3f} {med(sun):6.3f} {med(S):5.2f} {med(B):5.2f} {med(vao):5.2f} {med(sop):6.3f} "
                  f"{med(alb):6.3f} {med(dire):7.4f} {med(amb):7.4f} {med(lit):7.4f} {med(fin):7.4f} {bt:6.1f}")

if __name__ == "__main__":
    main()
