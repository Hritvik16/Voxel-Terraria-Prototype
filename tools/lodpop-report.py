#!/usr/bin/env python3
"""lodpop-report.py <LodPop folder> -- how different is each coarse tier from
tier 0 on the pixels it draws?

For each pose: R = all tier 0, L = legacy bounds, P = pixel-footprint bounds,
and L_t / P_t = the tier view (debug mode 6: tier 0 blue, 1 green, 2 red).
For every tier, over the pixels the tier view assigns to it, reports the mean
absolute luma difference against R and the share of pixels differing by more
than 16/255 -- the part of the image that "pops" when the boundary sweeps past.
Writes <pose>_diff.jpg: |L-R| | |P-R| (x4 gain) above the two tier views.
"""
import glob
import os
import sys

import numpy as np
from PIL import Image

TIERS = {0: (0.1, 0.3, 0.9), 1: (0.15, 0.85, 0.2), 2: (0.9, 0.15, 0.15), -1: (0.05, 0.05, 0.05)}


def srgb(c):
    c = np.asarray(c)
    return np.where(c <= 0.0031308, 12.92 * c, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def tier_map(img):
    keys = list(TIERS)
    cols = np.stack([np.array(TIERS[k]) * 255 for k in keys] + [srgb(TIERS[k]) * 255 for k in keys])
    px = img.reshape(-1, 3).astype(np.float32)
    d = ((px[:, None, :] - cols[None]) ** 2).sum(-1)
    idx = d.argmin(1) % len(keys)
    return np.array(keys)[idx].reshape(img.shape[:2])


def luma(img):
    a = img.astype(np.float32)
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def main():
    src = sys.argv[1]
    tot = {}
    print(f"{'pose':<12}{'bounds':<8}{'tier':>5}{'px %':>7}{'mean |dL|':>11}{'>16/255 %':>11}")
    for r in sorted(glob.glob(os.path.join(src, "*_R.png"))):
        pose = os.path.basename(r)[:-6]
        R = np.asarray(Image.open(r).convert("RGB").resize((960, 540), Image.NEAREST))
        diffs, tviews = [], []
        for tag in ("L", "P"):
            X = np.asarray(Image.open(os.path.join(src, f"{pose}_{tag}.png")).convert("RGB").resize((960, 540), Image.NEAREST))
            T = np.asarray(Image.open(os.path.join(src, f"{pose}_{tag}_t.png")).convert("RGB").resize((960, 540), Image.NEAREST))
            tm = tier_map(T)
            d = np.abs(luma(X) - luma(R))
            diffs.append(d); tviews.append(T)
            for t in (1, 2):
                m = tm == t
                if m.sum() < 50:
                    continue
                share = 100.0 * m.mean()
                md = float(d[m].mean()); big = 100.0 * float((d[m] > 16).mean())
                tot.setdefault((tag, t), []).append((m.sum(), md, big))
                print(f"{pose:<12}{tag:<8}{t:>5}{share:>7.1f}{md:>11.2f}{big:>11.2f}")
        # Shadows-off pair (rung 3), if present: how much of the tier difference
        # is the shadow ray.
        r3p, l3p = os.path.join(src, f"{pose}_R3.png"), os.path.join(src, f"{pose}_L3.png")
        if os.path.exists(r3p) and os.path.exists(l3p):
            R3 = np.asarray(Image.open(r3p).convert("RGB").resize((960, 540), Image.NEAREST))
            L3 = np.asarray(Image.open(l3p).convert("RGB").resize((960, 540), Image.NEAREST))
            tmL = tier_map(tviews[0])
            d3 = np.abs(luma(L3) - luma(R3))
            for t in (1, 2):
                m = tmL == t
                if m.sum() < 50:
                    continue
                md = float(d3[m].mean()); big = 100.0 * float((d3[m] > 16).mean())
                tot.setdefault(("L3", t), []).append((m.sum(), md, big))
                print(f"{pose:<12}{'L rung3':<8}{t:>5}{100.0 * m.mean():>7.1f}{md:>11.2f}{big:>11.2f}")
        top = np.concatenate([np.clip(d * 4, 0, 255) for d in diffs], axis=1).astype(np.uint8)
        top = np.stack([top] * 3, -1)
        bot = np.concatenate(tviews, axis=1)
        Image.fromarray(np.concatenate([top, bot], axis=0)).save(os.path.join(src, f"{pose}_diff.jpg"), quality=85)
    print()
    print("POOLED (pixel-weighted over poses)")
    for (tag, t), v in sorted(tot.items()):
        n = sum(x[0] for x in v)
        md = sum(x[0] * x[1] for x in v) / n
        big = sum(x[0] * x[2] for x in v) / n
        label = {'L': 'legacy 64/128 m', 'P': 'pixel footprint', 'L3': 'legacy, SHADOWS OFF'}[tag]
        print(f"  {label:<19} tier {t}: mean |dL| {md:.2f}/255, "
              f"{big:.2f}% of its pixels differ by > 16/255")


if __name__ == "__main__":
    main()
