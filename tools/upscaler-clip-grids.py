#!/usr/bin/env python3
"""upscaler-clip-grids.py <path dir> <grid out dir> <crop out dir> [crop x y] -- task 50.
<path dir> holds one folder per arm (-upscalerclips: <arm>/f0000.jpg ..., 1920x1080).
Writes, frame by frame (JPEG q95, for tools/frames-to-mp4):
  grid: 3x2 of all arms, each tile scaled to 960x540 (2880x1080), arm label
        top-left, the clip time on the first tile.
  crop: 3x2 of 1:1 pixel crops, 640x360 each (1920x720), NOT rescaled, labelled.
        The window is picked once per path (unless given): the 640x360 window with
        the most strong edges (luma step > 24) in the native arm, over 10 frames
        spread across the clip -- where the ridge silhouette or the torch light is.
Writes crop.json (window, arms, frame count) into <path dir>. Exits non-zero if an
arm is missing or the arms disagree on the frame count.
REAL RATE (task 51): if <path dir>/rates.json is there ({"seconds": 8, "arms":
{arm: {"ms": p50, "frames": n}}}), each arm was recorded at its own fixed step;
the output is 60 fps for "seconds", each arm's frame held until its next is due
(sample-and-hold), labelled "<arm>  <fps> fps (<ms> ms)"."""
import json, os, shutil, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ARMS = ["native", "taa_540", "fsr1_540", "stp_540", "fsr3_dyn", "fsr3_720"]
W, H, TW, TH, CW, CH = 1920, 1080, 960, 540, 640, 360
FPS = 30

def font(sz):
    for f in ("/System/Library/Fonts/SFNSMono.ttf", "/System/Library/Fonts/Menlo.ttc", "/System/Library/Fonts/Helvetica.ttc"):
        try: return ImageFont.truetype(f, sz)
        except OSError: pass
    return ImageFont.load_default()

def label(d, x, y, s, f):
    l, t, r, b = d.textbbox((0, 0), s, font=f)
    d.rectangle([x, y, x + r - l + 12, y + b - t + 10], fill=(0, 0, 0))
    d.text((x + 6 - l, y + 5 - t), s, font=f, fill=(255, 230, 0))

def pick_crop(files):
    """The 640x360 window holding the most STRONG edges (luma step > 24 to a
    neighbour), summed over the given frames. Strong edges are silhouettes
    against the sky, block edges in light, torches; a plain sum of |gradient|
    picked dark near ground full of faint texture (task 50, walk_cliff)."""
    acc = None
    for fn in files:
        g = np.asarray(Image.open(fn).convert("L"), dtype=np.float32)
        e = np.zeros_like(g)
        e[:, 1:] = np.maximum(e[:, 1:], np.abs(np.diff(g, axis=1)))
        e[1:, :] = np.maximum(e[1:, :], np.abs(np.diff(g, axis=0)))
        e = (e > 24).astype(np.float32)
        acc = e if acc is None else acc + e
    s = acc.reshape(H // 8, 8, W // 8, 8).sum(axis=(1, 3))          # 135x240 cells of 8 px
    ii = np.pad(s.cumsum(0).cumsum(1), ((1, 0), (1, 0)))
    cw, ch = CW // 8, CH // 8
    best, bx, by = -1.0, 0, 0
    for y in range(0, H // 8 - ch + 1):
        for x in range(0, W // 8 - cw + 1):
            v = ii[y + ch, x + cw] - ii[y, x + cw] - ii[y + ch, x] + ii[y, x]
            if v > best: best, bx, by = v, x, y
    return bx * 8, by * 8

def main():
    src, gdir, cdir = sys.argv[1:4]
    counts = {}
    for a in ARMS:
        d = os.path.join(src, a)
        counts[a] = len([f for f in os.listdir(d) if f.endswith(".jpg")]) if os.path.isdir(d) else 0
    rates = None
    if os.path.exists(os.path.join(src, "rates.json")):
        rates = json.load(open(os.path.join(src, "rates.json")))
        for a in ARMS:
            if counts[a] != rates["arms"][a]["frames"]:
                print(f"{a}: {counts[a]} frames, rates.json says {rates['arms'][a]['frames']}"); sys.exit(1)
        return main_rates(src, gdir, cdir, counts, rates)
    n = min(counts.values())
    if n == 0 or len(set(counts.values())) != 1:
        print(f"frame counts differ or zero: {counts}"); sys.exit(1)
    if len(sys.argv) > 5: cx, cy = int(sys.argv[4]), int(sys.argv[5])      # a window given by hand
    else: cx, cy = pick_crop([os.path.join(src, "native", f"f{i:04d}.jpg") for i in np.linspace(0, n - 1, 10).astype(int)])
    json.dump({"crop_x": cx, "crop_y": cy, "w": CW, "h": CH, "arms": ARMS, "frames": n}, open(os.path.join(src, "crop.json"), "w"))
    print(f"{os.path.basename(src.rstrip('/'))}: {n} frames per arm, crop window x {cx}..{cx + CW} y {cy}..{cy + CH}")
    os.makedirs(gdir, exist_ok=True); os.makedirs(cdir, exist_ok=True)
    fl, fs = font(30), font(24)
    for i in range(n):
        grid = Image.new("RGB", (3 * TW, 2 * TH))
        crop = Image.new("RGB", (3 * CW, 2 * CH))
        gd, cd = ImageDraw.Draw(grid), ImageDraw.Draw(crop)
        for k, a in enumerate(ARMS):
            im = Image.open(os.path.join(src, a, f"f{i:04d}.jpg")).convert("RGB")
            if im.size != (W, H): print(f"{a} f{i:04d} is {im.size}"); sys.exit(1)
            gx, gy = (k % 3) * TW, (k // 3) * TH
            grid.paste(im.resize((TW, TH), Image.LANCZOS), (gx, gy))
            label(gd, gx + 8, gy + 8, a + (f"   t {i / FPS:4.1f} s" if k == 0 else ""), fl)
            ox, oy = (k % 3) * CW, (k // 3) * CH
            crop.paste(im.crop((cx, cy, cx + CW, cy + CH)), (ox, oy))
            label(cd, ox + 6, oy + 6, a + (f"   t {i / FPS:4.1f} s" if k == 0 else ""), fs)
        grid.save(os.path.join(gdir, f"f{i:04d}.jpg"), quality=95)
        crop.save(os.path.join(cdir, f"f{i:04d}.jpg"), quality=95)

OUT_FPS = 60

def main_rates(src, gdir, cdir, counts, rates):
    """Sample-and-hold: output frame j (time j/60) shows each arm's frame
    floor(t / dt), dt its own step. Repeated output frames are file copies."""
    secs = rates["seconds"]
    nout = int(round(secs * OUT_FPS))
    ms = {a: rates["arms"][a]["ms"] for a in ARMS}
    nat = os.path.join(src, "native")
    if len(sys.argv) > 5: cx, cy = int(sys.argv[4]), int(sys.argv[5])
    else: cx, cy = pick_crop([os.path.join(nat, f"f{i:04d}.jpg") for i in np.linspace(0, counts["native"] - 1, 10).astype(int)])
    json.dump({"crop_x": cx, "crop_y": cy, "w": CW, "h": CH, "arms": ARMS, "frames": nout, "fps": OUT_FPS,
               "arm_ms": ms, "arm_frames": counts}, open(os.path.join(src, "crop.json"), "w"))
    print(f"{os.path.basename(src.rstrip('/'))}: real rate, {nout} output frames at {OUT_FPS} fps; " +
          ", ".join(f"{a} {ms[a]:.1f} ms x {counts[a]}" for a in ARMS) + f"; crop window x {cx}..{cx + CW} y {cy}..{cy + CH}")
    os.makedirs(gdir, exist_ok=True); os.makedirs(cdir, exist_ok=True)
    fl, fs = font(30), font(24)
    lab = {a: f"{a}  {1000.0 / ms[a]:.1f} fps ({ms[a]:.1f} ms)" for a in ARMS}
    prev, cache = None, {}
    for j in range(nout):
        t = j / OUT_FPS
        idx = tuple(min(counts[a] - 1, int(t * 1000.0 / ms[a] + 1e-6)) for a in ARMS)
        gname, cname = os.path.join(gdir, f"f{j:04d}.jpg"), os.path.join(cdir, f"f{j:04d}.jpg")
        # the clock label ticks every 0.1 s, so a held frame is only reused within it
        key = (idx, int(t * 10))
        if key == prev:
            shutil.copyfile(os.path.join(gdir, f"f{j - 1:04d}.jpg"), gname)
            shutil.copyfile(os.path.join(cdir, f"f{j - 1:04d}.jpg"), cname)
            continue
        prev = key
        grid = Image.new("RGB", (3 * TW, 2 * TH))
        crop = Image.new("RGB", (3 * CW, 2 * CH))
        gd, cd = ImageDraw.Draw(grid), ImageDraw.Draw(crop)
        for k, a in enumerate(ARMS):
            ck = (a, idx[k])
            if ck not in cache:
                im = Image.open(os.path.join(src, a, f"f{idx[k]:04d}.jpg")).convert("RGB")
                if im.size != (W, H): print(f"{a} f{idx[k]:04d} is {im.size}"); sys.exit(1)
                cache = {kk: v for kk, v in cache.items() if kk[0] != a}       # one frame per arm held
                cache[ck] = (im.resize((TW, TH), Image.LANCZOS), im.crop((cx, cy, cx + CW, cy + CH)))
            tile, cimg = cache[ck]
            gx, gy = (k % 3) * TW, (k // 3) * TH
            grid.paste(tile, (gx, gy))
            label(gd, gx + 8, gy + 8, lab[a] + (f"   t {key[1] / 10:4.1f} s" if k == 0 else ""), fl)
            ox, oy = (k % 3) * CW, (k // 3) * CH
            crop.paste(cimg, (ox, oy))
            label(cd, ox + 6, oy + 6, lab[a] + (f"   t {key[1] / 10:4.1f} s" if k == 0 else ""), fs)
        grid.save(gname, quality=92)
        crop.save(cname, quality=95)

if __name__ == "__main__":
    main()
