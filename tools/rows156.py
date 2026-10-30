#!/usr/bin/env python3
"""Task 156: FSR 3 at 540 vs 432 internal rows, side-by-side sheets + 3x crops.

python3 -I tools/rows156.py <LookReview dir> <out dir> [rowsB]

Per pose: <out>/rows156_<pose>.png = LEFT <pose>_fsr3_540.png, RIGHT <pose>_fsr3_<rowsB>.png
(each scaled to 960x540 for the sheet). Then <out>/rows156_crops.png: per pose the 3 non-overlapping
160x90 output-pixel windows where the two arms differ most (mean |diff| of luminance, sky excluded by
the diff itself), each shown 3x (nearest neighbour) as 540 | 432 pairs. Writes rows156.txt (per pose:
mean |diff|, the crop windows and their diff).
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

POSES = ["walk_near", "walk_open", "high", "cliff", "torch"]
CW, CH, SCALE = 160, 90, 3


def lum(a):
    return a[..., 0] * 0.299 + a[..., 1] * 0.587 + a[..., 2] * 0.114


def label(img, text, xy=(8, 6)):
    d = ImageDraw.Draw(img)
    x, y = xy
    d.rectangle([x - 4, y - 3, x + 7 * len(text) + 4, y + 13], fill=(0, 0, 0))
    d.text((x, y), text, fill=(255, 255, 0))


def top_windows(diff, n=3):
    h, w = diff.shape
    # integral image -> mean |diff| per 160x90 window on a 20 px grid
    ii = np.pad(diff, ((1, 0), (1, 0))).cumsum(0).cumsum(1)
    cands = []
    for y in range(0, h - CH + 1, 20):
        for x in range(0, w - CW + 1, 20):
            s = ii[y + CH, x + CW] - ii[y, x + CW] - ii[y + CH, x] + ii[y, x]
            cands.append((s / (CW * CH), x, y))
    cands.sort(reverse=True)
    out = []
    for s, x, y in cands:
        if all(abs(x - ox) >= CW or abs(y - oy) >= CH for _, ox, oy in out):
            out.append((s, x, y))
        if len(out) == n:
            break
    return out


def main():
    src, dst = sys.argv[1], sys.argv[2]
    rb = sys.argv[3] if len(sys.argv) > 3 else "432"
    os.makedirs(dst, exist_ok=True)
    rep, crop_rows = [], []
    for p in POSES:
        fa, fb = os.path.join(src, f"{p}_fsr3_540.png"), os.path.join(src, f"{p}_fsr3_{rb}.png")
        if not (os.path.exists(fa) and os.path.exists(fb)):
            rep.append(f"{p}: missing ({os.path.basename(fa)} / {os.path.basename(fb)})")
            continue
        a, b = Image.open(fa).convert("RGB"), Image.open(fb).convert("RGB")
        A, B = np.asarray(a, np.float32), np.asarray(b, np.float32)
        diff = np.abs(lum(A) - lum(B))
        wins = top_windows(diff)
        sheet = Image.new("RGB", (1920 + 8, 540 + 24), (30, 30, 30))
        la, lb = a.resize((960, 540), Image.LANCZOS), b.resize((960, 540), Image.LANCZOS)
        for i, (s, x, y) in enumerate(wins):   # mark the crop windows on both halves
            for im in (la, lb):
                ImageDraw.Draw(im).rectangle([x // 2, y // 2, (x + CW) // 2, (y + CH) // 2], outline=(255, 0, 255), width=2)
                ImageDraw.Draw(im).text((x // 2 + 3, y // 2 + 2), str(i + 1), fill=(255, 0, 255))
        sheet.paste(la, (0, 24)); sheet.paste(lb, (968, 24))
        d = ImageDraw.Draw(sheet)
        d.text((8, 6), f"{p}: LEFT FSR 3 at 540 rows (today)", fill=(255, 255, 255))
        d.text((976, 6), f"RIGHT FSR 3 at {rb} rows (Q1 floor)   magenta = crop windows (rows156_crops.png)", fill=(255, 255, 255))
        sheet.save(os.path.join(dst, f"rows156_{p}.png"))
        rep.append(f"{p}: mean |dlum| {diff.mean():.2f} (0-255); crops " +
                   ", ".join(f"#{i + 1} x{x} y{y} {s:.1f}" for i, (s, x, y) in enumerate(wins)))
        for i, (s, x, y) in enumerate(wins):
            ca = a.crop((x, y, x + CW, y + CH)).resize((CW * SCALE, CH * SCALE), Image.NEAREST)
            cb = b.crop((x, y, x + CW, y + CH)).resize((CW * SCALE, CH * SCALE), Image.NEAREST)
            label(ca, f"{p} #{i + 1} 540")
            label(cb, f"{p} #{i + 1} {rb}")
            crop_rows.append((ca, cb))
    if crop_rows:
        w, h = CW * SCALE, CH * SCALE
        cols = 2  # two pairs per row
        nrow = (len(crop_rows) + cols - 1) // cols
        sheet = Image.new("RGB", (cols * (2 * w + 24), nrow * (h + 8)), (30, 30, 30))
        for k, (ca, cb) in enumerate(crop_rows):
            x0, y0 = (k % cols) * (2 * w + 24), (k // cols) * (h + 8)
            sheet.paste(ca, (x0, y0)); sheet.paste(cb, (x0 + w + 4, y0))
        sheet.save(os.path.join(dst, "rows156_crops.png"))
    open(os.path.join(dst, "rows156.txt"), "w").write("\n".join(rep) + "\n")
    print("\n".join(rep))


if __name__ == "__main__":
    main()
