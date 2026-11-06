#!/usr/bin/env python3
"""Task 174b: torch-flood intensity / falloff sweep (-tfi / -tfk) on task 172's six night scenes. A LOOK, capture only.

python3 -I tools/torchtune174b.py <LookReview dir> <out dir> <lookreview log>

Shots (PlaygroundCapture.TorchCine.cs, -tcarms 540,native,540+f1,native+f1,..., -tcsets I/k,...):
  tc_<scene>_fsr3_540.png          TODAY: play's FSR 3 at 540 rows (no sc10, no mask), flood OFF
  tc_<scene>_native.png            native 1080, flood OFF (truth for today)
  tc_<scene>_fsr3_540_floodN.png   FSR 3 540, flood ON (-tfdir 2) with set N (I, k from the log's 'flood sets' line)
  tc_<scene>_native_floodN.png     native 1080, the same set (truth for set N)
  tc_<scene>_proj.csv              viewport positions: torches, torch + 2 m (the near-post disc), ground profile points
Per still, per arm:
  lit     % px luma > 40 (as 172)          black  % px luma < 6 (as 172)
  clipN   % px with any channel >= 250 inside the near-post discs (2 m round every torch, screen radius from the csv)
  clipF   the same over the whole frame
Radial profile (camp15 round the centre torch; water round the lone torch), from the NATIVE arms (no upscale noise):
  median luma of a 5x5 px patch at ground points r = 0.5..10 m, camp split into rays through a ring post (odd az) and
  rays through the gap between two posts (even az); water split into the land side (az 90..270) only.
  core/rim: luma at r 1 m over r 6 m (a high number = a bright core and a dark rim); gap: camp gap ray at 4 m over
  post ray at 4 m (how dark between two ring torches, 1 = even sheet).
Bursts (camp40pan 4 frames, walk 8): flicker vs that arm's native truth, all px / lit px (truth luma > 40), as 172.
Outputs: torchtune174b.txt, profile_<scene>.png, full/<scene>_<arm>.png + viewer.html (arrows = scene, Space = arm),
tt174b_<scene>.png contact sheets (today + every set, half size).
"""
import csv
import json
import os
import re
import shutil
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

STILLS = [("camp15", "9-torch camp from 15 m: look at the ground between the ring posts (a spot per torch or one sheet?) and the floor right at the posts (clipped yellow?)"),
          ("camp40", "the camp from 40 m: does the camp still read as warm pools, and how far the glow spreads down the ridge"),
          ("cave", "3 m tunnel, 2 wall torches: the inside glow, black pockets between the two torches, the fan onto the outside face"),
          ("wall", "6 torches along a cliff: a bright spot per torch on the face, or one flat warm sheet up to the ridge?"),
          ("water", "lone torch on a level stone terrace: the round pool's shape -- bright core, dark rim -- and the post")]
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


def read_sets(log):
    sets = {}
    for line in open(log, errors="replace"):
        if "flood sets:" in line:
            for m in re.finditer(r"\+f(\d*) = I ([\d.]+) k ([\d.]+) wrap ([\d.]+)", line):
                sets[int(m.group(1) or 0)] = (float(m.group(2)), float(m.group(3)), float(m.group(4)))
    return sets


def read_proj(path):
    rows = []
    if os.path.exists(path):
        with open(path) as f:
            for r in csv.DictReader(f):
                rows.append((r["kind"], int(r["i"]), float(r["r"]), float(r["az"]), float(r["vx"]), float(r["vy"]), float(r["vz"])))
    return rows


def near_mask(proj, w, h):
    t = {i: (vx, vy, vz) for k, i, _, _, vx, vy, vz in proj if k == "torch"}
    tr = {i: (vx, vy) for k, i, _, _, vx, vy, vz in proj if k == "torchR"}
    yy, xx = np.mgrid[0:h, 0:w]
    m = np.zeros((h, w), bool)
    for i, (vx, vy, vz) in t.items():
        if vz <= 0 or i not in tr:
            continue
        cx, cy = vx * w, (1 - vy) * h
        rad = max(6.0, np.hypot((tr[i][0] - vx) * w, (tr[i][1] - vy) * h))
        m |= (xx - cx) ** 2 + (yy - cy) ** 2 <= rad * rad
    return m


def profile(L, proj, w, h, scene):
    """{ray class: {r: median luma}}"""
    acc = {}
    for k, i, r, az, vx, vy, vz in proj:
        if k != "prof" or vz <= 0 or not (0 <= vx < 1 and 0 <= vy < 1):
            continue
        x, y = int(vx * w), int((1 - vy) * h)
        p = L[max(0, y - 2):y + 3, max(0, x - 2):x + 3]
        if p.size == 0:
            continue
        if scene == "camp15":
            cls = "post" if i % 2 == 1 else "gap"
        else:
            if not (4 <= i <= 12):   # land side only (az 90..270; az 0 = toward the water)
                continue
            cls = "land"
        acc.setdefault(cls, {}).setdefault(r, []).append(float(np.median(p)))
    return {c: {r: float(np.median(v)) for r, v in sorted(d.items())} for c, d in acc.items()}


def at(prof, r):
    if not prof:
        return float("nan")
    ks = np.array(sorted(prof))
    j = int(np.argmin(np.abs(ks - r)))
    return prof[ks[j]] if abs(ks[j] - r) <= 0.26 else float("nan")


def plot_profile(dst, scene, profs, sets):
    W, H, M = 900, 520, 60
    img = Image.new("RGB", (W, H), (20, 20, 20))
    d = ImageDraw.Draw(img)
    cols = [(160, 160, 160), (255, 120, 60), (255, 220, 60), (120, 220, 120), (90, 170, 255), (220, 120, 255)]
    d.rectangle([M, 20, W - 20, H - M], outline=(90, 90, 90))
    for v in (0, 50, 100, 150, 200, 250):
        y = H - M - v / 255 * (H - M - 20)
        d.line([M, y, W - 20, y], fill=(45, 45, 45))
        d.text((10, y - 8), str(v), fill=(150, 150, 150), font=FONT_S)
    for r in range(0, 11, 2):
        x = M + r / 10 * (W - 20 - M)
        d.text((x - 5, H - M + 6), f"{r}", fill=(150, 150, 150), font=FONT_S)
    d.text((W // 2 - 80, H - 28), "distance from the torch, m", fill=(150, 150, 150), font=FONT_S)
    cls = "gap" if scene == "camp15" else "land"
    y0 = 24
    for ci, (arm, pr) in enumerate(profs):
        p = pr.get(cls, {})
        pts = [(M + r / 10 * (W - 20 - M), H - M - v / 255 * (H - M - 20)) for r, v in p.items()]
        if len(pts) > 1:
            d.line(pts, fill=cols[ci % len(cols)], width=3)
        d.text((W - 300, y0), arm, fill=cols[ci % len(cols)], font=FONT_S)
        y0 += 22
    tag(img, f"{scene}: native luma along the ground, rays {'between two ring posts' if cls == 'gap' else 'on the land side'}", (M, 0), FONT_S)
    img.save(os.path.join(dst, f"profile_{scene}.png"))


def main():
    src, dst, log = sys.argv[1], sys.argv[2], sys.argv[3]
    os.makedirs(os.path.join(dst, "full"), exist_ok=True)
    sets = read_sets(log)
    nset = sorted(k for k in sets if k > 0)
    name = {"fsr3_540": "TODAY (flood OFF)", "native": "native, flood OFF"}
    for n in nset:
        I, k, wr = sets[n]
        name[f"fsr3_540_flood{n}"] = f"S{n}  I {I:.1f} k {k:.2f}" + ("  (= 172's flood)" if (I, k) == (2.0, 0.1) else "")
        name[f"native_flood{n}"] = f"native S{n}  I {I:.1f} k {k:.2f}"
    fsr = ["fsr3_540"] + [f"fsr3_540_flood{n}" for n in nset]
    truth = {"fsr3_540": "native", **{f"fsr3_540_flood{n}": f"native_flood{n}" for n in nset}}
    rep = [f"task 174b torch flood sweep: {src}", "sets: " + "; ".join(f"S{n} I {sets[n][0]:.2f} k {sets[n][1]:.2f} wrap {sets[n][2]:.2f}" for n in nset), ""]
    viewer, summ = [], {}
    for scene, cap in STILLS:
        p = {a: os.path.join(src, f"tc_{scene}_{a}.png") for a in fsr + list(truth.values())}
        im = {a: load(f) for a, f in p.items() if os.path.exists(f)}
        if "fsr3_540" not in im:
            rep.append(f"{scene}: shots missing")
            continue
        h, w = im["fsr3_540"].shape[:2]
        proj = read_proj(os.path.join(src, f"tc_{scene}_proj.csv"))
        nm = near_mask(proj, w, h)
        rep.append(f"{scene}: {cap}")
        rep.append(f"   near-post discs cover {100 * nm.mean():.1f}% of the frame ({sum(1 for r in proj if r[0] == 'torch')} torches)")
        rep.append(f"   {'arm':<22} {'mean':>5} {'lit%':>6} {'black%':>7} {'clipN%':>7} {'clipF%':>7}")
        for a in fsr + list(truth.values()):
            if a not in im:
                continue
            L = lum(im[a])
            sat = (im[a] >= 250).any(-1)
            clipN = 100 * sat[nm].mean() if nm.any() else float("nan")
            row = dict(mean=L.mean(), lit=100 * (L > 40).mean(), black=100 * (L < 6).mean(), clipN=clipN, clipF=100 * sat.mean())
            summ[(scene, a)] = row
            rep.append(f"   {a:<22} {row['mean']:5.1f} {row['lit']:6.1f} {row['black']:7.1f} {row['clipN']:7.2f} {row['clipF']:7.2f}")
        if scene in ("camp15", "water"):
            profs = []
            for a in ["native"] + [f"native_flood{n}" for n in nset]:
                if a in im:
                    pr = profile(lum(im[a]), proj, w, h, scene)
                    profs.append((name.get(a, a), pr))
                    cls = "gap" if scene == "camp15" else "land"
                    c1, c6 = at(pr.get(cls), 1.0), at(pr.get(cls), 6.0)
                    line = f"   profile {a:<15} " + " ".join(f"{r:g}m {at(pr.get(cls), r):5.1f}" for r in (0.5, 1, 2, 3, 4, 6, 8, 10))
                    line += f"   core/rim {c1 / max(c6, 1):4.1f}"
                    if scene == "camp15":
                        g4, p4 = at(pr.get("gap"), 4.0), at(pr.get("post"), 4.0)
                        line += f"   gap/post@4m {g4 / max(p4, 1):4.2f}"
                        summ[(scene, a, "gp")] = g4 / max(p4, 1)
                    summ[(scene, a, "cr")] = c1 / max(c6, 1)
                    rep.append(line)
            plot_profile(dst, scene, profs, sets)
        # viewer + contact sheet
        shots = []
        for a in fsr + ["native"]:
            if a in im:
                fn = f"{scene}_{a}.png"
                shutil.copyfile(p[a], os.path.join(dst, "full", fn))
                r = summ[(scene, a)]
                shots.append([f"{name.get(a, a)}  -- lit {r['lit']:.1f}%  clip near posts {r['clipN']:.2f}%  black {r['black']:.1f}%", "full/" + fn])
        viewer.append(dict(n=scene, cap=cap, win=[880, 495], s=shots))
        tw, th = w // 2, h // 2
        cols = 3
        rows = (len(fsr) + cols - 1) // cols
        S = Image.new("RGB", (cols * tw, rows * th + 44), (20, 20, 20))
        tag(S, f"{scene}: {cap}", (8, 8), FONT_S)
        for j, a in enumerate(fsr):
            if a not in im:
                continue
            t = Image.fromarray(im[a].astype(np.uint8)).resize((tw, th), Image.BILINEAR)
            tag(t, name[a], font=FONT_S)
            S.paste(t, ((j % cols) * tw, 44 + (j // cols) * th))
        S.save(os.path.join(dst, f"tt174b_{scene}.png"))
        rep.append("")
    for scene, n, note in BURSTS:
        rep.append(f"{scene} burst ({n} frames): {note}")
        rep.append(f"   {'arm':<22} {'truth':<16} flicker all / lit")
        for a in fsr:
            tr = truth[a]
            fa = [os.path.join(src, f"tc_{scene}_{a}_{k}.png") for k in range(n)]
            ft = [os.path.join(src, f"tc_{scene}_{tr}_{k}.png") for k in range(n)]
            if not all(os.path.exists(f) for f in fa + ft):
                rep.append(f"   {a:<22} frames missing")
                continue
            La = [lum(load(f)) for f in fa]
            Lt = [lum(load(f)) for f in ft]
            lit = Lt[0] > 40
            per, perl = [], []
            for k in range(1, n):
                e = np.abs((La[k] - La[k - 1]) - (Lt[k] - Lt[k - 1]))
                per.append(e.mean())
                perl.append(e[lit].mean() if lit.any() else 0.0)
            summ[(scene, a, "fl")] = np.mean(perl)
            rep.append(f"   {a:<22} {tr:<16} {np.mean(per):5.2f} / {np.mean(perl):5.2f}   lit px {100 * lit.mean():.1f}%")
        rep.append("")
    # summary table
    rep.append("SUMMARY (FSR 3 540 arms; profile ratios from native)")
    rep.append(f"   {'arm':<34} {'camp15 lit/clipN':>17} {'wall lit/clipN':>15} {'cave black':>11} {'water core/rim':>15} {'camp gap/post':>14} {'walk fl':>8} {'pan fl':>7}")
    for a in fsr:
        n_ = truth[a]

        def g(*k, f="{:.1f}"):
            v = summ.get(k)
            return f.format(v) if v is not None and v == v else "-"
        c15 = summ.get(("camp15", a), {}); wl = summ.get(("wall", a), {}); cv = summ.get(("cave", a), {})
        rep.append(f"   {name[a]:<34} {c15.get('lit', 0):7.1f} / {c15.get('clipN', 0):5.2f}  {wl.get('lit', 0):6.1f} / {wl.get('clipN', 0):5.2f} "
                   f"{cv.get('black', 0):11.1f} {g('water', n_, 'cr'):>15} {g('camp15', n_, 'gp', f='{:.2f}'):>14} "
                   f"{g('walk', a, 'fl', f='{:.2f}'):>8} {g('camp40pan', a, 'fl', f='{:.2f}'):>7}")
    page = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "fsr174.py")).read()
    page = page[page.index('page = """') + 10:]
    page = page[:page.index('"""')].replace("Task 174 FSR 3 reactive mask", "Task 174b torch flood sweep")
    with open(os.path.join(dst, "viewer.html"), "w") as f:
        f.write(page.replace("__DATA__", json.dumps(viewer)))
    open(os.path.join(dst, "torchtune174b.txt"), "w").write("\n".join(rep) + "\n")
    print("\n".join(rep))


if __name__ == "__main__":
    main()
