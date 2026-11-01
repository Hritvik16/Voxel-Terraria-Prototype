#!/usr/bin/env python3
"""Task 162: native 1080 vs FSR 3 at 720 / 648 / 540 / 432 internal rows (same frame per pose).

python3 -I tools/ladder162.py <LookReview dir> <out dir>

Per pose: <out>/ladder162_<pose>.png = the 5 arms side by side (each 1920x1080 shot scaled to 640x360),
labelled, and under each tile the mean |dlum| vs native (0-255) and the % of pixels whose luminance
differs from native by > 8 levels. Magenta boxes = the pose's crop windows.
<out>/ladder162_crops.png: the 3 160x90 windows (of every pose's 3 non-overlapping top windows) where the FSR arms
differ most from native (mean over the 4 FSR arms), each 3x nearest neighbour, all 5 arms in a row.
<out>/ladder162_motion.png: walk_open's still / walk / turn bursts (4 consecutive frames after 60 lead)
for native / 540 / 432, frame 0 and the flicker image (max - min over the 4 frames, x4), with 159's metric
(mean flicker 0-255 and by horizontal band, top -> bottom).
Writes ladder162.txt with every number.
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

POSES = ["walk_near", "walk_open", "high", "cliff", "torch"]
ARMS = [("native", "native 1080"), ("fsr3_720", "720"), ("fsr3_648", "648"), ("fsr3_540", "540"),
        ("fsr3_432", "432 (now in play)")]
BURST_ARMS = [("native", "native 1080"), ("fsr3_540", "FSR 3 540"), ("fsr3_432", "FSR 3 432 (now in play)")]
MOTIONS = ["still", "walk", "turn"]
CW, CH, SCALE = 160, 90, 3
TW, TH = 640, 360


def load(p):
    return np.asarray(Image.open(p).convert("RGB"), np.float32)


def lum(a):
    return a[..., 0] * 0.299 + a[..., 1] * 0.587 + a[..., 2] * 0.114


def tag(img, text, xy=(6, 4), col=(255, 255, 0)):
    d = ImageDraw.Draw(img)
    x, y = xy
    d.rectangle([x - 3, y - 2, x + 6 * len(text) + 5, y + 12], fill=(0, 0, 0))
    d.text((x, y), text, fill=col)


def top_windows(diff, n=3):
    h, w = diff.shape
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


def pose_sheets(src, dst, rep):
    crop_rows = []
    for p in POSES:
        files = [os.path.join(src, f"{p}_{a}.png") for a, _ in ARMS]
        miss = [os.path.basename(f) for f in files if not os.path.exists(f)]
        if miss:
            rep.append(f"{p}: missing {', '.join(miss)}")
            continue
        ims = [Image.open(f).convert("RGB") for f in files]
        L = [lum(np.asarray(im, np.float32)) for im in ims]
        diffs = [np.abs(l - L[0]) for l in L]
        wins = top_windows(sum(diffs[1:]) / (len(diffs) - 1))
        stats = [(d.mean(), 100.0 * (d > 8).mean()) for d in diffs]
        sheet = Image.new("RGB", (5 * TW + 4 * 6, 22 + TH + 20), (30, 30, 30))
        d = ImageDraw.Draw(sheet)
        d.text((6, 5), f"{p}: native 1080 (no upscaler, the reference) | FSR 3 at 720 / 648 / 540 / 432 internal rows, "
                       f"same frame; magenta = crop windows (ladder162_crops.png); under each: vs native", fill=(255, 255, 255))
        for i, ((arm, lab), im, (m, pc)) in enumerate(zip(ARMS, ims, stats)):
            t = im.resize((TW, TH), Image.LANCZOS)
            for k, (_, x, y) in enumerate(wins):
                ImageDraw.Draw(t).rectangle([x // 3, y // 3, (x + CW) // 3, (y + CH) // 3], outline=(255, 0, 255), width=2)
                ImageDraw.Draw(t).text((x // 3 + 3, y // 3 + 2), str(k + 1), fill=(255, 0, 255))
            tag(t, lab)
            x0 = i * (TW + 6)
            sheet.paste(t, (x0, 22))
            d.text((x0 + 6, 22 + TH + 4), "reference" if i == 0 else f"mean |dlum| {m:.2f}   >8 levels {pc:.1f} %",
                   fill=(255, 255, 255))
        sheet.save(os.path.join(dst, f"ladder162_{p}.png"))
        rep.append(f"{p}: vs native mean |dlum| / % px > 8 levels: " +
                   "  ".join(f"{lab.split()[0]} {m:.2f} / {pc:.1f}%" for (a, lab), (m, pc) in list(zip(ARMS, stats))[1:]) +
                   "; crops " + ", ".join(f"#{k + 1} x{x} y{y} {s:.1f}" for k, (s, x, y) in enumerate(wins)))
        for k, (sc, x, y) in enumerate(wins):
            row = []
            for (arm, lab), im in zip(ARMS, ims):
                c = im.crop((x, y, x + CW, y + CH)).resize((CW * SCALE, CH * SCALE), Image.NEAREST)
                tag(c, f"{p} #{k + 1} {'native 1080' if arm == 'native' else 'FSR 3 ' + lab}")
                row.append(c)
            crop_rows.append((sc, row))
    if crop_rows:
        # the 3 spots (of every pose's top 3) that differ most from native, mean over the 4 FSR arms
        crop_rows.sort(key=lambda r: -r[0])
        crop_rows = [r for _, r in crop_rows[:3]]
        w, h = CW * SCALE, CH * SCALE
        sheet = Image.new("RGB", (5 * (w + 4), len(crop_rows) * (h + 6)), (30, 30, 30))
        for r, row in enumerate(crop_rows):
            for i, c in enumerate(row):
                sheet.paste(c, (i * (w + 4), r * (h + 6)))
        sheet.save(os.path.join(dst, "ladder162_crops.png"))


def motion_sheet(src, dst, rep):
    rows = []
    for arm, lab in BURST_ARMS:
        for mo in MOTIONS:
            fs = [os.path.join(src, f"walk_open_{arm}_{mo}_{i}.png") for i in range(4)]
            if not all(os.path.exists(f) for f in fs):
                rep.append(f"motion {arm} {mo}: missing")
                continue
            fr = [lum(load(f)) for f in fs]
            st = np.stack(fr)
            fl = st.max(0) - st.min(0)
            d01 = np.abs(fr[1] - fr[0])
            h = fl.shape[0]
            bands = " ".join(f"{fl[h * k // 5:h * (k + 1) // 5].mean():.2f}" for k in range(5))
            rep.append(f"motion {lab:24s} {mo:5s} diff01 {d01.mean():5.2f}  flicker {fl.mean():5.2f}  by band top->bottom {bands}")
            f0 = Image.open(fs[0]).convert("RGB").resize((TW, TH), Image.LANCZOS)
            tag(f0, f"{lab}  {mo}  frame 0")
            fli = Image.fromarray(np.clip(fl * 4, 0, 255).astype(np.uint8)).convert("RGB").resize((TW, TH), Image.LANCZOS)
            tag(fli, f"{lab}  {mo}  flicker x4 (mean {fl.mean():.2f})")
            rows.append((f0, fli))
    if rows:
        # columns: still | walk | turn, each a frame-0 / flicker pair; one row per arm
        S = Image.new("RGB", (3 * (2 * TW + 12), len(BURST_ARMS) * (TH + 6)), (30, 30, 30))
        for k, (f0, fli) in enumerate(rows):
            r, c = k // 3, k % 3
            S.paste(f0, (c * (2 * TW + 12), r * (TH + 6)))
            S.paste(fli, (c * (2 * TW + 12) + TW + 4, r * (TH + 6)))
        S.save(os.path.join(dst, "ladder162_motion.png"))


def main():
    src, dst = sys.argv[1], sys.argv[2]
    os.makedirs(dst, exist_ok=True)
    rep = []
    pose_sheets(src, dst, rep)
    motion_sheet(src, dst, rep)
    open(os.path.join(dst, "ladder162.txt"), "w").write("\n".join(rep) + "\n")
    print("\n".join(rep))


if __name__ == "__main__":
    main()
