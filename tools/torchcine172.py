#!/usr/bin/env python3
"""Task 172: is the RECOMMENDED torch look (flood ON -tfdir 2, FSR 3 432-540 rows, -fsr3sc 10) cinematic next to TODAY's?

python3 -I tools/torchcine172.py <LookReview dir> <out dir>

Shots (PlaygroundCapture.TorchCine.cs): tc_<scene>_<arm>.png stills, tc_<scene>_<arm>_<k>.png bursts.
Arms: fsr3_540 / fsr3_720 = TODAY (play's FSR 3 dyn 540-720, flood OFF, both ends);
      fsr3_432_sc10_flood / fsr3_540_sc10_flood = RECOMMENDED (dyn 432-540, sc10, flood dir 2, both ends);
      native / native_flood = the 1080 raymarch, no upscale (truth for each lighting).
Per still scene:
  tc172_<scene>.png         FULL SIZE, row 1 today 540 | recommended 432 (the floors), row 2 today 720 | recommended 540
  tc172_<scene>_crops.png   4x crops (160 x 90 px windows): A = where recommended differs most from today (the light
                            pool / its edge), B = where recommended 432 differs most from its own native (upscale
                            artefacts); each: today 540 | rec 432 | native+f / today 720 | rec 540 | native
Bursts (camp40pan 4 frames, walk 8 frames; stepped per frame, so native frame k is the truth for arm frame k):
  flicker   mean |d_arm(k) - d_truth(k)| over the frame, d = frame k - frame k-1 (light crawl / flicker / shimmer
            beyond what the true image does), and the same inside the lit mask (truth luma > 40)
  tc172_<scene>_burst.png   the 4x window with the largest recommended flicker, frames in a row: today 540 / rec 432 / truth+f
  tc172_<scene>.gif         today 540 | recommended 432, whole frame at half size, all kept frames
Stats per still: mean luma, % px over 230 (blown), % px under 6 (pitch black), % px 'lit' (> 40).
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

CW, CH, Z = 160, 90, 4
TODAY = ("fsr3_540", "fsr3_720")
REC = ("fsr3_432_sc10_flood", "fsr3_540_sc10_flood")
LABEL = {"fsr3_540": "TODAY  FSR3 540 rows, flood OFF", "fsr3_720": "TODAY  FSR3 720 rows, flood OFF",
         "fsr3_432_sc10_flood": "RECOMMENDED  FSR3 432 rows sc10, flood dir2",
         "fsr3_540_sc10_flood": "RECOMMENDED  FSR3 540 rows sc10, flood dir2",
         "native": "native 1080, flood OFF (truth for today)", "native_flood": "native 1080, flood ON (truth for rec)"}
STILLS = [("camp15", "9-torch camp from 15 m: the light pools on the ground and posts, their edges, dark gaps between"),
          ("camp40", "the same camp from 40 m: do far torches still read as warm pools, any speckle or hard rim"),
          ("cave", "a 3 m tunnel with 2 wall torches: the inside glow, the mouth, dark pockets, light through rock"),
          ("wall", "6 torches along a cliff face: wall and corner lighting, steps, wedges/bands at the 12 m reach"),
          ("water", "a lone torch on the shore: its light on ground and water, halos, reflections")]
BURSTS = [("camp40pan", 4, "camp from 40 m, strafing 0.05 m/frame"), ("walk", 8, "walking 4 m/s through the camp")]

try:
    FONT = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 26)
    FONT_S = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 18)
except Exception:
    FONT = FONT_S = ImageFont.load_default()


def load(p):
    return np.asarray(Image.open(p).convert("RGB"), np.float32)


def lum(a):
    return a[..., 0] * 0.299 + a[..., 1] * 0.587 + a[..., 2] * 0.114


def tag(img, text, xy=(8, 6), font=None, col=(255, 230, 0)):
    d = ImageDraw.Draw(img)
    font = font or FONT
    l, t, r, b = d.textbbox(xy, text, font=font)
    d.rectangle([l - 5, t - 4, r + 5, b + 4], fill=(0, 0, 0))
    d.text(xy, text, fill=col, font=font)


def best_window(score, step=10):
    h, w = score.shape
    ii = np.pad(score, ((1, 0), (1, 0))).cumsum(0).cumsum(1)
    best = (-1.0, 0, 0)
    for y in range(0, h - CH + 1, step):
        for x in range(0, w - CW + 1, step):
            s = ii[y + CH, x + CW] - ii[y, x + CW] - ii[y + CH, x] + ii[y, x]
            if s / (CW * CH) > best[0]:
                best = (s / (CW * CH), x, y)
    return best


def crop4(a, x, y):
    c = Image.fromarray(a[y:y + CH, x:x + CW].astype(np.uint8))
    return c.resize((CW * Z, CH * Z), Image.NEAREST)


def stats(a):
    L = lum(a)
    return f"mean {L.mean():5.1f}  blown {100 * (L > 230).mean():5.2f}%  black {100 * (L < 6).mean():5.1f}%  lit {100 * (L > 40).mean():5.1f}%"


def still(src, dst, scene, note, out):
    p = {a: os.path.join(src, f"tc_{scene}_{a}.png") for a in TODAY + REC + ("native", "native_flood")}
    if not all(os.path.exists(p[a]) for a in TODAY + REC):
        out.append(f"{scene}: shots missing -- skipped")
        return
    im = {a: load(f) for a, f in p.items() if os.path.exists(f)}
    h, w = im[TODAY[0]].shape[:2]
    bar = 50
    S = Image.new("RGB", (2 * w, 2 * h + bar), (20, 20, 20))
    tag(S, f"{scene}: {note}", (10, 10))
    for r, (t, rc) in enumerate(zip(TODAY, REC)):
        for c, a in enumerate((t, rc)):
            tile = Image.fromarray(im[a].astype(np.uint8))
            tag(tile, ("LEFT " if c == 0 else "RIGHT ") + LABEL[a])
            S.paste(tile, (c * w, bar + r * h))
    S.save(os.path.join(dst, f"tc172_{scene}.png"))
    out.append(f"{scene}: {note}")
    for a in im:
        out.append(f"   {a:<22} {stats(im[a])}")
    # crops
    diffA = np.abs(lum(im[REC[0]]) - lum(im[TODAY[0]]))
    nat = im.get("native_flood")
    diffB = np.abs(lum(im[REC[0]]) - lum(nat)) if nat is not None else diffA
    wins = [("A light change", best_window(diffA)), ("B upscale error", best_window(diffB))]
    if abs(wins[0][1][1] - wins[1][1][1]) < CW and abs(wins[0][1][2] - wins[1][1][2]) < CH:
        # B overlaps A: take B's best window outside A instead
        m = diffB.copy()
        x0, y0 = wins[0][1][1], wins[0][1][2]
        m[max(0, y0 - CH):y0 + 2 * CH, max(0, x0 - CW):x0 + 2 * CW] = 0
        wins[1] = ("B upscale error", best_window(m))
    grid = [(TODAY[0], REC[0], "native_flood"), (TODAY[1], REC[1], "native")]
    C = Image.new("RGB", (3 * CW * Z, 2 * 2 * CH * Z + 2 * bar), (20, 20, 20))
    for wi, (wn, (sc, x, y)) in enumerate(wins):
        oy = wi * (2 * CH * Z + bar)
        tag(C, f"{scene} crop {wn}: window ({x},{y}) {CW}x{CH} px of 1920x1080, 4x nearest; mean |dluma| {sc:.1f}", (10, oy + 10))
        for r, row in enumerate(grid):
            for c, a in enumerate(row):
                if a not in im:
                    continue
                t = crop4(im[a], x, y)
                tag(t, LABEL[a], font=FONT_S)
                C.paste(t, (c * CW * Z, oy + bar + r * CH * Z))
        out.append(f"   crop {wn}: ({x},{y}) score {sc:.1f}")
    C.save(os.path.join(dst, f"tc172_{scene}_crops.png"))


def burst(src, dst, scene, n, note, out):
    arms = TODAY + REC + ("native", "native_flood")
    fr = {}
    for a in arms:
        fs = [os.path.join(src, f"tc_{scene}_{a}_{k}.png") for k in range(n)]
        if all(os.path.exists(f) for f in fs):
            fr[a] = [load(f) for f in fs]
    if not all(a in fr for a in (TODAY[0], REC[0])):
        out.append(f"{scene} burst: frames missing -- skipped")
        return
    out.append(f"{scene} burst ({n} frames): {note}")
    out.append(f"   {'arm':<22} {'truth':<13} flicker all / lit   (per frame k=1..{n - 1})")
    flick_rec = None
    for a in arms:
        if a not in fr:
            continue
        tr = "native_flood" if a.endswith("flood") else "native"
        if a.startswith("native") or tr not in fr:
            continue
        L = [lum(f) for f in fr[a]]
        T = [lum(f) for f in fr[tr]]
        lit = T[0] > 40
        per, perl = [], []
        acc = np.zeros_like(L[0])
        for k in range(1, n):
            e = np.abs((L[k] - L[k - 1]) - (T[k] - T[k - 1]))
            acc += e
            per.append(e.mean())
            perl.append(e[lit].mean() if lit.any() else 0.0)
        if a == REC[0]:
            flick_rec = acc
        out.append(f"   {a:<22} {tr:<13} {np.mean(per):5.2f} / {np.mean(perl):5.2f}   " +
                   " ".join(f"{v:.1f}" for v in perl))
    if flick_rec is None:
        flick_rec = sum(np.abs(lum(fr[REC[0]][k]) - lum(fr[REC[0]][k - 1])) for k in range(1, n))
    sc, x, y = best_window(flick_rec)
    rows = [a for a in (TODAY[0], REC[0], "native_flood") if a in fr]
    bar = 50
    B = Image.new("RGB", (n * CW * 2, len(rows) * CH * 2 + bar), (20, 20, 20))   # 2x here: n frames across
    tag(B, f"{scene}: window ({x},{y}) where RECOMMENDED 432 flickers most vs truth, 2x; frames 0..{n - 1} left to right", (10, 10), FONT_S)
    for r, a in enumerate(rows):
        for k in range(n):
            t = Image.fromarray(fr[a][k][y:y + CH, x:x + CW].astype(np.uint8)).resize((CW * 2, CH * 2), Image.NEAREST)
            if k == 0:
                tag(t, a, (4, 4), FONT_S)
            B.paste(t, (k * CW * 2, bar + r * CH * 2))
    B.save(os.path.join(dst, f"tc172_{scene}_burst.png"))
    out.append(f"   burst window ({x},{y}) score {sc:.1f}")
    gif = []
    for k in range(n):
        h, w = fr[TODAY[0]][k].shape[:2]
        G = Image.new("RGB", (w, h // 2))
        for c, a in enumerate((TODAY[0], REC[0])):
            t = Image.fromarray(fr[a][k].astype(np.uint8)).resize((w // 2, h // 2), Image.BILINEAR)
            tag(t, ("LEFT today 540" if c == 0 else "RIGHT rec 432+flood") + f"  frame {k}", font=FONT_S)
            G.paste(t, (c * w // 2, 0))
        gif.append(G)
    gif[0].save(os.path.join(dst, f"tc172_{scene}.gif"), save_all=True, append_images=gif[1:], duration=133, loop=0)


def main():
    src, dst = sys.argv[1], sys.argv[2]
    os.makedirs(dst, exist_ok=True)
    out = [f"task 172 torch cinematic: {src}", ""]
    for s, note in STILLS:
        still(src, dst, s, note, out)
        out.append("")
    for s, n, note in BURSTS:
        burst(src, dst, s, n, note, out)
        out.append("")
    txt = "\n".join(out)
    open(os.path.join(dst, "torchcine172.txt"), "w").write(txt + "\n")
    print(txt)


if __name__ == "__main__":
    main()
