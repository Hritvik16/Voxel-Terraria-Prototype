#!/usr/bin/env python3
"""nearshimmer-report.py <nearshimmer folder>: the NEAR-terrain shimmer number
(-nearshimmer, run-tg2world.sh MODE=shimmer). Per pose and depth band, the luma
change between the base frame and each quarter-gate-pixel nudge (xP, xM, yP, yM:
a sub-pixel offset in the projection, as a TAA jitter), averaged over the four, plus the noise floor (base vs the
unmoved repeat, "still"). Lower = more stable under sub-pixel motion.

Depth bands come from <pose>_depth.f32 (CPU DDA through the ChunkStore, one ray
per 4x4 gate pixels): near < 50 m, mid 50-200 m, far = no voxel hit within
200 m and not sky (sky from <pose>_band.png, debug view 9). Images are compared
at the gate resolution (960x540, nearest), as tools/farshimmer-report.py does.
THRESH is fixed: 24/255, the far-band report's threshold."""
import glob, os, sys
import numpy as np
from PIL import Image

W, H, STEP, THRESH = 960, 540, 4, 24.0
NUDGES = ("xP", "xM", "yP", "yM")
SKY_LIN = np.array([0.02, 0.02, 0.03])
SKY = [SKY_LIN * 255, (1.055 * SKY_LIN ** (1 / 2.4) - 0.055) * 255]   # linear and sRGB-encoded (the band view's sky)


def luma(p):
    a = np.asarray(Image.open(p).convert("RGB").resize((W, H), Image.NEAREST)).astype(np.float32)
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def sky_mask(p):
    a = np.asarray(Image.open(p).convert("RGB").resize((W, H), Image.NEAREST)).astype(np.float32)
    return np.logical_or.reduce([np.abs(a - c).max(-1) < 6 for c in SKY])


def bands(src, n):
    d = np.fromfile(os.path.join(src, f"{n}_depth.f32"), dtype="<f4").reshape(H // STEP, W // STEP)
    d = np.repeat(np.repeat(d, STEP, 0), STEP, 1)
    sky = sky_mask(os.path.join(src, f"{n}_band.png"))
    hit = d >= 0
    return {"near": hit & (d < 50), "mid": hit & (d >= 50), "far": ~hit & ~sky, "all": ~sky}


def main():
    src = sys.argv[1]
    names = sorted(os.path.basename(p)[:-9] for p in glob.glob(os.path.join(src, "*_base.png")))
    print(f"NEAR SHIMMER  {src}")
    print(f"mean |dL| (0-255 luma) and % of pixels with |dL| > {THRESH:.0f}, base vs 4 quarter-pixel nudges (mean of 4);")
    print("floor = base vs the unmoved repeat. share = % of the frame in the band.")
    print(f"{'pose':<13}{'band':<6}{'share%':>7}{'mean|dL|':>10}{'>thr %':>8}{'floor|dL|':>11}{'floor>thr%':>11}")
    tot = {}
    for n in names:
        b = luma(os.path.join(src, f"{n}_base.png"))
        diffs = [np.abs(luma(os.path.join(src, f"{n}_{k}.png")) - b) for k in NUDGES]
        still = np.abs(luma(os.path.join(src, f"{n}_still.png")) - b)
        for band, m in bands(src, n).items():
            share = 100.0 * m.mean()
            if m.sum() < 500:
                print(f"{n:<13}{band:<6}{share:>7.1f}{'(too few pixels)':>40}")
                continue
            md = np.mean([d[m].mean() for d in diffs])
            pt = np.mean([100.0 * (d[m] > THRESH).mean() for d in diffs])
            print(f"{n:<13}{band:<6}{share:>7.1f}{md:>10.2f}{pt:>8.2f}{still[m].mean():>11.2f}{100.0 * (still[m] > THRESH).mean():>11.2f}")
            tot.setdefault(band, []).append((md, pt))
    print("-" * 66)
    for band, v in tot.items():
        print(f"{'MEAN(poses)':<13}{band:<6}{'':>7}{np.mean([x[0] for x in v]):>10.2f}{np.mean([x[1] for x in v]):>8.2f}   ({len(v)} poses)")


if __name__ == "__main__":
    main()
