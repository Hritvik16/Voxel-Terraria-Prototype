#!/usr/bin/env python3
"""tools/taarisk-report.py OFF_DIR ON_DIR [OUT_DIR]

Compares two -taarisk folders (run-tg2world.sh MODE=taarisk, -taa 0 / -taa 1)
frame by frame and writes side-by-side contact sheets (OFF top row, ON bottom).

Numbers (luma 0-255, Rec.709):
  dL(ON,OFF)   mean |L_on - L_off| for the SAME frame index (same camera pose).
               Includes the jitter, so a still frame is not 0 (task 06: still
               floor 0.7-3.2 per pose).
  bias         mean (L_on - L_off), signed: a lagging light shows as a bias.
  edge ON/OFF  ratio of mean gradient magnitude: < 1 = ON softer (smear).
  edit ghost   over the pixels the dig changed (|settled - pre| > 24 in the
               OFF arm, used for both), mean |frame - settled| / mean |pre - settled|:
               1 = still the pre-edit image, 0 = matches the settled hole.
"""
import os
import sys

import numpy as np
from PIL import Image


def luma(path):
    a = np.asarray(Image.open(path).convert("RGB"), dtype=np.float32)
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def edge(L):
    return float(np.mean(np.abs(np.diff(L, axis=0))) + np.mean(np.abs(np.diff(L, axis=1))))


def change_box(off_dir, names, pad=60, min_px=200):
    """Bounding box of the pixels that change across the OFF frames (> 24),
    padded; None if too few change (then the whole frame is shown)."""
    L0 = luma(os.path.join(off_dir, names[0] + ".png"))
    m = np.zeros(L0.shape, bool)
    for n in names[1:]:
        m |= np.abs(luma(os.path.join(off_dir, n + ".png")) - L0) > 24
    if m.sum() < min_px or m.mean() > 0.25:
        return None
    ys, xs = np.nonzero(m)
    h, w = L0.shape
    return (max(0, xs.min() - pad), max(0, ys.min() - pad), min(w, xs.max() + pad), min(h, ys.max() + pad))


def sheet(off_dir, on_dir, names, out, box=None, tile_w=480):
    """OFF top row, ON bottom. Cropped to box (if any) and BRIGHTENED for
    viewing (one gain for the whole sheet, 99th percentile -> 230); the
    numbers above are computed on the raw frames."""
    ims = []
    for d in (off_dir, on_dir):
        row = []
        for n in names:
            im = Image.open(os.path.join(d, n + ".png")).convert("RGB")
            row.append(im.crop(box) if box else im)
        ims.append(row)
    w, h = ims[0][0].size
    tw = min(tile_w, w); th = int(h * tw / w)
    p99 = max(1.0, float(np.percentile(np.asarray(ims[0][0], dtype=np.float32), 99)))
    gain = min(8.0, 230.0 / p99)
    canvas = Image.new("RGB", (tw * len(names), th * 2), (0, 0, 0))
    for r, row in enumerate(ims):
        for c, im in enumerate(row):
            a = np.clip(np.asarray(im.resize((tw, th), Image.BILINEAR), dtype=np.float32) * gain, 0, 255)
            canvas.paste(Image.fromarray(a.astype(np.uint8)), (c * tw, r * th))
    canvas.save(out)
    return gain


def main():
    off_dir, on_dir = sys.argv[1], sys.argv[2]
    out_dir = sys.argv[3] if len(sys.argv) > 3 else os.path.join(on_dir, "compare")
    os.makedirs(out_dir, exist_ok=True)
    have = lambda d, n: os.path.exists(os.path.join(d, n + ".png"))
    seqs = [
        ("edit", ["edit_pre"] + [f"edit_f{k}" for k in range(6)] + ["edit_settled"]),
        ("fluid_a", [f"fluid_a{k}" for k in range(8)]),
        ("fluid_b", [f"fluid_b{k}" for k in range(8)]),
        ("fluid_c", [f"fluid_c{k}" for k in range(8)]),
    ]
    for s in ("spin_slow", "spin_fast"):
        seqs.append((s, [f"{s}_still"] + [f"{s}_m{k}" for k in range(8)] + [f"{s}_stop{k}" for k in range(4)]))
    seqs.append(("fly", [f"fly_m{k}" for k in range(8)]))
    for s in ("light_02", "light_05", "light_09", "light_20"):
        seqs.append((s, [f"{s}_still"] + [f"{s}_m{k}" for k in range(8)]))

    print("TAA RISK FRAMES, OFF vs ON (same binary)")
    print(f"  OFF {off_dir}\n  ON  {on_dir}\n")
    for name, frames in seqs:
        frames = [f for f in frames if have(off_dir, f) and have(on_dir, f)]
        if not frames:
            print(f"{name}: missing"); continue
        print(f"{name}:  frame  dL(ON,OFF)  bias  edge ON/OFF")
        for f in frames:
            a, b = luma(os.path.join(off_dir, f + ".png")), luma(os.path.join(on_dir, f + ".png"))
            print(f"    {f:18s} {np.mean(np.abs(b - a)):7.2f} {np.mean(b - a):+6.2f}  {edge(b) / max(1e-6, edge(a)):5.2f}")
        # sheets of at most 7 frames each so a sheet stays readable
        box = change_box(off_dir, frames) if name.startswith(("edit", "fluid")) else None
        for i in range(0, len(frames), 7):
            g = sheet(off_dir, on_dir, frames[i:i + 7], os.path.join(out_dir, f"{name}_{i // 7}.png"), box)
        print(f"  sheet: crop {box if box else 'full frame'}, display gain x{g:.1f}")
        if name == "edit":
            print("  edit ghost (1 = pre-edit image, 0 = settled hole):")
            # ONE mask for both arms -- the OFF arm's (no jitter): the ON arm's own
            # |settled - pre| also picks up jitter on every edge in frame.
            m = np.abs(luma(os.path.join(off_dir, "edit_settled.png")) - luma(os.path.join(off_dir, "edit_pre.png"))) > 24
            for arm, d in (("OFF", off_dir), ("ON", on_dir)):
                pre, st = luma(os.path.join(d, "edit_pre.png")), luma(os.path.join(d, "edit_settled.png"))
                den = max(1e-6, float(np.mean(np.abs(pre - st)[m]))) if m.any() else 1e-6
                vals = []
                for k in range(6):
                    L = luma(os.path.join(d, f"edit_f{k}.png"))
                    vals.append(float(np.mean(np.abs(L - st)[m])) / den if m.any() else float("nan"))
                print(f"    {arm:3s} mask {int(m.sum()):7d} px   " + "  ".join(f"f{k} {v:.2f}" for k, v in enumerate(vals)))
    print(f"\nsheets: {out_dir}")


if __name__ == "__main__":
    main()
