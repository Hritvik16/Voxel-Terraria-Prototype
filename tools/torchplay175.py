#!/usr/bin/env python3
"""Task 175: is the torch flood really ON in play by default, and do survival edits re-flood?
python3 -I tools/torchplay175.py <LookReview dir> <out dir>
Shots (PlaygroundCapture.TorchCine.cs, -tcarms 540_sc10_rm+p,432_sc10_rm+p,native+p,540_sc10_rm,native,540):
  tc_<scene>_fsr3_540_sc10_rm_play.png   PLAY now: FSR 3 540 (dyn top) sc10 + mask, flood as PlaySettings' default gives it
  tc_<scene>_fsr3_432_sc10_rm_play.png   the same at 432 (dyn floor)
  tc_<scene>_native_play.png             native 1080, play's flood (truth for the two above)
  tc_<scene>_fsr3_540_sc10_rm.png        -torchflood 0: play's FSR knobs, point-light torches
  tc_<scene>_native.png                  native 1080, flood OFF (truth for the two OFF arms)
  tc_<scene>_fsr3_540.png                BEFORE task 174: FSR 3 540 sc 0, no mask, flood OFF (174b's 'today')
  tc_edit_<k>_<step>.png                 the edit scene: 0 before, 1 place, 2 digpit, 3 digtorch
Bursts (camp40pan 4, walk 8): flicker = mean |d_arm(k) - d_truth(k)| in the lit mask (truth luma > 40), as 172 / 174b.
Outputs: torchplay175.txt, full/*.png, viewer.html (arrows = scene, Space = arm), tp175_edit.png (the 4 edit steps).
"""
import json
import os
import shutil
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ARMS = [("fsr3_540_sc10_rm_play", "PLAY now 540: flood ON by default, sc10 + mask", "native_play"),
        ("fsr3_432_sc10_rm_play", "PLAY now 432 (dyn floor)", "native_play"),
        ("fsr3_540_sc10_rm", "-torchflood 0: point-light torches, 540 sc10 + mask", "native"),
        ("fsr3_540", "BEFORE 174: 540 sc0, flood OFF (174b 'today')", "native"),
        ("native_play", "native 1080, play's flood (truth)", None),
        ("native", "native 1080, flood OFF (truth)", None)]
STILLS = [("camp15", "9-torch camp from 15 m: the warm pool round each post, its falloff, any yellow clipping"),
          ("camp40", "the same camp from 40 m: does it read as a glow on the ridge"),
          ("cave", "3 m tunnel, 2 wall torches: is the inside lit (flood) or a dim doorway (point lights)"),
          ("wall", "6 torches along a cliff face: is the face lit")]
BURSTS = [("camp40pan", 4), ("walk", 8)]
EDIT = [("0_before", "before: the camp lit by play's default flood"),
        ("1_place", "1 placed a torch on a post 7 m right of the centre: it must light its own pool"),
        ("2_digpit", "2 dug a 1 x 1 x 0.5 m pit 1.2 m in front of the middle post: light must reach into it"),
        ("3_digtorch", "3 dug the middle torch: the centre must darken")]
try:
    FONT = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 22)
except Exception:
    FONT = ImageFont.load_default()


def load(p):
    return np.asarray(Image.open(p).convert("RGB"), np.float32)


def lum(a):
    return a[..., 0] * 0.299 + a[..., 1] * 0.587 + a[..., 2] * 0.114


def tag(img, text, xy=(8, 6)):
    d = ImageDraw.Draw(img)
    l, t, r, b = d.textbbox(xy, text, font=FONT)
    d.rectangle((l - 4, t - 3, r + 4, b + 3), fill=(0, 0, 0))
    d.text(xy, text, font=FONT, fill=(255, 230, 0))


def stats(a):
    L = lum(a)
    return dict(lit=100 * (L > 40).mean(), mean=L.mean(), clip=100 * (a.max(axis=-1) >= 250).mean(), black=100 * (L < 6).mean())


def main():
    src, dst = sys.argv[1], sys.argv[2]
    os.makedirs(os.path.join(dst, "full"), exist_ok=True)
    rep, viewer = [f"Task 175: torch flood ON in play by default; shots {src}", ""], []
    for scene, cap in STILLS:
        shots = []
        rep.append(f"{scene}: {cap}")
        for a, nm, _ in ARMS:
            p = os.path.join(src, f"tc_{scene}_{a}.png")
            if not os.path.exists(p):
                continue
            s = stats(load(p))
            rep.append(f"   {a:<24} lit {s['lit']:5.1f}%  mean luma {s['mean']:5.1f}  clip {s['clip']:5.2f}%  black {s['black']:5.1f}%   {nm}")
            fn = f"{scene}_{a}.png"
            shutil.copyfile(p, os.path.join(dst, "full", fn))
            shots.append([f"{nm} -- lit {s['lit']:.1f}%  black {s['black']:.1f}%", "full/" + fn])
        if shots:
            viewer.append(dict(n=scene, cap=cap, win=[880, 495], s=shots))
        rep.append("")
    for scene, n in BURSTS:
        rep.append(f"{scene} burst ({n} frames): flicker vs its truth, all px / lit px")
        for a, nm, tr in ARMS:
            if tr is None:
                continue
            fa = [os.path.join(src, f"tc_{scene}_{a}_{k}.png") for k in range(n)]
            ft = [os.path.join(src, f"tc_{scene}_{tr}_{k}.png") for k in range(n)]
            if not all(os.path.exists(f) for f in fa + ft):
                rep.append(f"   {a:<24} frames missing")
                continue
            La = [lum(load(f)) for f in fa]
            Lt = [lum(load(f)) for f in ft]
            lit = Lt[0] > 40
            per, perl = [], []
            for k in range(1, n):
                e = np.abs((La[k] - La[k - 1]) - (Lt[k] - Lt[k - 1]))
                per.append(e.mean())
                perl.append(e[lit].mean() if lit.any() else 0.0)
            rep.append(f"   {a:<24} vs {tr:<12} {np.mean(per):5.2f} / {np.mean(perl):5.2f}   lit px {100 * lit.mean():.1f}%   {nm}")
        rep.append("")
    # the edit scene: 4 steps, full size in the viewer, half size in one strip; the change vs the previous step
    ims, shots = [], []
    rep.append("edit (survival-style EditService, play's flood): per step lit %, and |luma change| vs the previous step")
    prev = None
    for k, cap in EDIT:
        p = os.path.join(src, f"tc_edit_{k}.png")
        if not os.path.exists(p):
            rep.append(f"   {k}: missing")
            continue
        a = load(p)
        s = stats(a)
        ch = "" if prev is None else f"  changed px (|d luma| > 8) {100 * (np.abs(lum(a) - lum(prev)) > 8).mean():5.2f}%  mean |d| {np.abs(lum(a) - lum(prev)).mean():.2f}"
        rep.append(f"   {k:<11} lit {s['lit']:5.1f}%  mean luma {s['mean']:5.1f}{ch}")
        fn = f"edit_{k}.png"
        shutil.copyfile(p, os.path.join(dst, "full", fn))
        shots.append([cap, "full/" + fn])
        ims.append((a, cap))
        prev = a
    if shots:
        viewer.append(dict(n="edit", cap="place / dig in survival: each step re-floods (see the log's settle lines)", win=[880, 495], s=shots))
    if ims:
        h, w = ims[0][0].shape[:2]
        tw, th = w // 2, h // 2
        S = Image.new("RGB", (2 * tw, 2 * th), (20, 20, 20))
        for j, (a, cap) in enumerate(ims):
            t = Image.fromarray(a.astype(np.uint8)).resize((tw, th), Image.BILINEAR)
            tag(t, cap)
            S.paste(t, ((j % 2) * tw, (j // 2) * th))
        S.save(os.path.join(dst, "tp175_edit.png"))
    page = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "fsr174.py")).read()
    page = page[page.index('page = """') + 10:]
    page = page[:page.index('"""')].replace("Task 174 FSR 3 reactive mask", "Task 175 torch flood on in play")
    with open(os.path.join(dst, "viewer.html"), "w") as f:
        f.write(page.replace("__DATA__", json.dumps(viewer)))
    open(os.path.join(dst, "torchplay175.txt"), "w").write("\n".join(rep) + "\n")
    print("\n".join(rep))


if __name__ == "__main__":
    main()
