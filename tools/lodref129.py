#!/usr/bin/env python3
"""tools/lodref129.py <LookReview dir> [more dirs...] -- task 129's numbers and sheets.

Reads what -lookreview -lodref 1 wrote (PlaygroundCapture.LodRef.cs): per pose
<tag>_{game,ref}_{native,hit,fsr540,fsr720,taa}.png and lodref_poses.json.

  MASK   REF pixels are the ones the relocated 0.1 m window (tier 0, code 1 in debug
         view 14) drew, minus (a) 'wall' pixels hit within 0.5 m of the box's entry
         face (terrain cut by the box), (b) 'occluded' pixels, where the GAME's hit is
         nearer than the box entry by more than max(1 m, 1% of D) (something outside
         the patch is in front). The rest of the frame is not compared.
  NUMBERS (inside the mask, vs REF native): mean |d luma| and |d RGB| (0-255),
         % pixels |d luma| > 16, detail kept = gradient energy of X / of REF (%),
         silhouette shift = |patch outline XOR| / REF outline length (px), and the
         on-screen px of one voxel of the shell that drew the GAME pixels vs 0.1 m.
  OUTPUT <dir>/lodref129/<tag>_sheet.png, <scene>_contact.png, summary.txt, summary.csv
"""
import json, math, os, sys
import numpy as np
from PIL import Image, ImageDraw

ARMS = ["native", "fsr540", "fsr720", "taa"]
TINT = {0: (0, 0, 0), 1: (40, 90, 255), 2: (40, 200, 60), 3: (230, 230, 40), 4: (255, 150, 30), 5: (230, 40, 40),
        6: (220, 50, 220), 7: (40, 220, 220), 8: (255, 255, 255), 10: (90, 60, 160), 11: (120, 80, 200), 12: (128, 128, 128)}
CODE_NAME = {0: "sky", 1: "tier0 0.1m", 12: "far band"}
for k in range(1, 8): CODE_NAME[1 + k] = f"shell{k} {0.1 * 2 ** k:g}m"


def load(path):
    return np.asarray(Image.open(path).convert("RGB")).astype(np.float32)


def calib(img):
    """Per-channel LUT from the 4-row grey ramp (16 px per code); returns (img_without_ramp_rows, rows_slice, luts)."""
    H = img.shape[0]
    best = None
    for rows in (slice(0, 4), slice(H - 4, H)):
        strip = img[rows]
        lv = np.array([np.median(strip[:, 16 * c + 4:16 * c + 12, :], axis=(0, 1)) for c in range(64)])
        mono = np.all(np.diff(lv, axis=0) > 0.5, axis=0).all()
        if mono:
            best = (rows, lv)
            break
    if best is None:
        raise RuntimeError("no calibration ramp found")
    return best


def decode(img):
    rows, lv = calib(img)
    codes = np.zeros(img.shape, np.int32)
    for ch in range(3):
        v = img[:, :, ch][..., None]
        codes[:, :, ch] = np.argmin(np.abs(v - lv[None, None, :, ch]), axis=2)
    code = codes[:, :, 0]
    q = codes[:, :, 1] * 64 + codes[:, :, 2]
    dist = np.power(2.0, q / 300.0) - 1.0
    code[rows] = -1
    return code, dist


def rays(pose, W, H, vfov):
    f, r, u = (np.array(pose[k], np.float64) for k in ("fwd", "right", "up"))
    t = math.tan(math.radians(vfov) * 0.5)
    xs = ((np.arange(W) + 0.5) / W * 2 - 1) * t * W / H
    ys = (1 - (np.arange(H) + 0.5) / H * 2) * t
    d = f[None, None, :] + xs[None, :, None] * r[None, None, :] + ys[:, None, None] * u[None, None, :]
    return d / np.linalg.norm(d, axis=2, keepdims=True)


def box_t(eye, d, box):
    lo = np.array(box["lo"], np.float64) * 0.1
    hi = np.array(box["hi"], np.float64) * 0.1
    with np.errstate(divide="ignore", invalid="ignore"):
        inv = 1.0 / d
        ta = (lo - eye) * inv
        tb = (hi - eye) * inv
    tlo = np.nanmax(np.minimum(ta, tb), axis=2)
    thi = np.nanmin(np.maximum(ta, tb), axis=2)
    return np.maximum(tlo, 0.0), thi


def luma(img):
    return img[..., 0] * 0.299 + img[..., 1] * 0.587 + img[..., 2] * 0.114


def erode(m):
    e = m.copy()
    e[1:] &= m[:-1]; e[:-1] &= m[1:]; e[:, 1:] &= m[:, :-1]; e[:, :-1] &= m[:, 1:]
    return e


def outline(m):
    return m & ~erode(m)


def grad_energy(l, m):
    gx = np.zeros_like(l); gy = np.zeros_like(l)
    gx[:, 1:-1] = (l[:, 2:] - l[:, :-2]) * 0.5
    gy[1:-1] = (l[2:] - l[:-2]) * 0.5
    mi = erode(m)
    return float(np.mean((gx * gx + gy * gy)[mi])) if mi.any() else float("nan")


def metrics(ref, x, m):
    lr, lx = luma(ref), luma(x)
    dl = np.abs(lr - lx)[m]
    drgb = np.abs(ref - x)[m].mean()
    ge_r, ge_x = grad_energy(lr, m), grad_energy(lx, m)
    return {"mae_luma": float(dl.mean()), "mae_rgb": float(drgb), "pct_over16": float((dl > 16).mean() * 100),
            "detail_pct": float(ge_x / ge_r * 100) if ge_r > 0 else float("nan")}


def panel(img, bbox, scale, label):
    y0, y1, x0, x1 = bbox
    c = Image.fromarray(np.clip(img[y0:y1, x0:x1], 0, 255).astype(np.uint8))
    c = c.resize((c.width * scale, c.height * scale), Image.NEAREST)
    d = ImageDraw.Draw(c)
    d.rectangle([0, 0, min(c.width, 9 * len(label) + 8), 16], fill=(0, 0, 0))
    d.text((4, 2), label, fill=(255, 255, 255))
    return c


def grid(rows, pad=4):
    w = max(sum(p.width for p in r) + pad * (len(r) - 1) for r in rows)
    h = sum(max(p.height for p in r) for r in rows) + pad * (len(rows) - 1)
    out = Image.new("RGB", (w, h), (24, 24, 24))
    y = 0
    for r in rows:
        x = 0
        for p in r:
            out.paste(p, (x, y)); x += p.width + pad
        y += max(p.height for p in r) + pad
    return out


def analyse(root):
    meta = json.load(open(os.path.join(root, "lodref_poses.json")))
    W, H = meta["screen"]; vfov = meta["vfov"]
    out = os.path.join(root, "lodref129"); os.makedirs(out, exist_ok=True)
    pix_angle = 2 * math.tan(math.radians(vfov) * 0.5) / H
    lines, csv, contact = [], [], {}
    for pose in meta["poses"]:
        tag, D = pose["tag"], pose["D"]
        P = lambda s, a: os.path.join(root, f"{tag}_{s}_{a}.png")
        if not (os.path.exists(P("ref", "hit")) and os.path.exists(P("game", "hit"))):
            lines.append(f"{tag}: missing hit images"); continue
        rc, rd = decode(load(P("ref", "hit")))
        gc, gd = decode(load(P("game", "hit")))
        gd = np.where(gc == 0, np.inf, gd)
        eye = np.array(pose["eye"], np.float64)
        dirs = rays(pose, W, H, vfov)
        te, tx = box_t(eye, dirs, pose["refBox"])
        margin = max(1.0, 0.01 * D)
        t0 = rc == 1
        wall = t0 & (te > 0) & (rd < te + 0.5)
        occl = t0 & ~wall & (gd < te - margin)
        # 'edge': REF hits within 4 m of the box floor or its x/z faces -- their shadow / sky rays leave
        # the patch through terrain the box cut away (black slivers in the first try).
        hp = eye[None, None, :] + dirs * np.where(np.isfinite(rd), rd, 0)[..., None]
        blo = np.array(pose["refBox"]["lo"], np.float64) * 0.1; bhi = np.array(pose["refBox"]["hi"], np.float64) * 0.1
        dface = np.minimum.reduce([hp[..., 1] - blo[1], hp[..., 0] - blo[0], bhi[0] - hp[..., 0], hp[..., 2] - blo[2], bhi[2] - hp[..., 2]])
        edge = t0 & ~wall & ~occl & (dface < 4.0)
        m = t0 & ~wall & ~occl & ~edge
        n = int(m.sum())
        if n < 50:
            lines.append(f"{tag}: mask {n} px (tier-0 {int(t0.sum())}, wall {int(wall.sum())}, occluded {int(occl.sum())}) -- too small, skipped")
            continue
        imgs = {(s, a): load(P(s, a)) for s in ("game", "ref") for a in ARMS if os.path.exists(P(s, a))}
        ref = imgs[("ref", "native")]
        res = {}
        for (s, a), x in imgs.items():
            if (s, a) != ("ref", "native"): res[f"{s}_{a}"] = metrics(ref, x, m)
        # REF-TAA (jittered samples accumulated) = the anti-aliased 0.1 m picture; sub-pixel voxels alias in REF native
        reft = imgs.get(("ref", "taa"))
        res_t = {f"{s}_{a}": metrics(reft, x, m) for (s, a), x in imgs.items() if reft is not None and s == "game"}
        # silhouette: the patch's outline in REF vs where the GAME hits land inside the same box segment
        in_ref = t0 & ~wall
        # compared only where REF's ray crossed the box: tier 0 hit, or passed through it (no hit before the exit)
        # the GAME hit point lies inside the same box, 4 m in from the floor / x / z faces (as the REF mask)
        hg = eye[None, None, :] + dirs * np.where(np.isfinite(gd), gd, 1e7)[..., None]
        dface_g = np.minimum.reduce([hg[..., 1] - blo[1], bhi[1] - hg[..., 1], hg[..., 0] - blo[0], bhi[0] - hg[..., 0], hg[..., 2] - blo[2], bhi[2] - hg[..., 2]])
        in_ref = m
        in_game = (gc != 0) & (dface_g >= 4.0) & ~occl
        crossed = (te < tx) & ((rc == 0) | ((rc != 1) & (rd >= tx - margin)))
        C = (in_ref | crossed) & ~occl & ~edge
        per = max(int((outline(in_ref) & C).sum()), 1)
        sil = float(((in_ref ^ in_game) & C).sum()) / per
        # the shell the game used there, and voxel px
        codes, counts = np.unique(gc[m], return_counts=True)
        dom = int(codes[np.argmax(counts)])
        vox = 0.1 if dom == 1 else (0.1 * 2 ** (dom - 1) if 2 <= dom <= 8 else float("nan"))
        rmed = float(np.median(rd[m]))
        px_dom = vox / (rmed * pix_angle); px_01 = 0.1 / (rmed * pix_angle)
        mix = ", ".join(f"{CODE_NAME.get(int(c), c)} {cnt / n * 100:.0f}%" for c, cnt in sorted(zip(codes, counts), key=lambda z: -z[1]) if cnt / n >= 0.02)
        lines.append(f"{tag}: mask {n} px (excluded: wall {int(wall.sum())}, occluded {int(occl.sum())}, box edge {int(edge.sum())}), REF median distance {rmed:.0f} m; "
                     f"GAME drew it with {mix}; voxel px at 1080: {CODE_NAME.get(dom, dom)} {px_dom:.2f} px vs 0.1 m {px_01:.2f} px; "
                     f"silhouette shift {sil:.2f} px")
        for refname, rr in (("native", res), ("taa", res_t)):
            for k, v in rr.items():
                lines.append(f"    {k:12s} vs REF {refname:6s}: |dLuma| {v['mae_luma']:5.1f}  |dRGB| {v['mae_rgb']:5.1f}  >16: {v['pct_over16']:5.1f}%  detail kept {v['detail_pct']:5.0f}%")
                csv.append(f"{tag},{pose['scene']},{D},{k},{refname},{n},{rmed:.1f},{dom},{px_dom:.3f},{px_01:.3f},{sil:.3f},"
                           f"{v['mae_luma']:.2f},{v['mae_rgb']:.2f},{v['pct_over16']:.2f},{v['detail_pct']:.1f}")

        # ---- sheet ----
        ys, xs = np.nonzero(m)
        y0, y1 = max(0, ys.min() - 12), min(H, ys.max() + 13)
        x0, x1 = max(0, xs.min() - 12), min(W, xs.max() + 13)
        bw, bh = x1 - x0, y1 - y0
        sc = max(1, int(math.ceil(470 / bw)))
        while bh * sc > 900 and sc > 1: sc -= 1
        if bh * sc > 900:   # big near patches: shrink by showing a centred 470 x 900 window at 1x
            cy, cx = (y0 + y1) // 2, (x0 + x1) // 2
            y0, y1 = max(0, cy - 450), min(H, cy + 450)
        bbox = (y0, y1, x0, x1)
        game = imgs[("game", "native")]
        played = imgs.get(("game", "fsr540"), game)
        comp = np.where(m[..., None], ref, game * 0.45)
        diffN = np.where(m[..., None], np.repeat(np.minimum(np.abs(luma(ref) - luma(game)) * 4, 255)[..., None], 3, 2), 0)
        diffP = np.where(m[..., None], np.repeat(np.minimum(np.abs(luma(ref) - luma(played)) * 4, 255)[..., None], 3, 2), 0)
        tint = np.zeros_like(ref)
        for c, col in TINT.items(): tint[gc == c] = col
        tint[outline(m)] = (255, 0, 255)
        row1 = [panel(comp, bbox, sc, "REF 0.1 m (dim = not compared)"), panel(game, bbox, sc, "GAME-NATIVE 1080"),
                panel(played, bbox, sc, "GAME-PLAYED FSR3 540")]
        row2 = [panel(diffN, bbox, sc, "|REF - NATIVE| luma x4"), panel(diffP, bbox, sc, "|REF - PLAYED| luma x4"),
                panel(tint, bbox, sc, "GAME tier (blue t0, green s1, yellow s2, orange s3, red s4, magenta s5)")]
        # worst window, 2x
        err = np.where(m, np.abs(luma(ref) - luma(game)), 0)
        win = int(min(96, bw, bh)); win = max(win, 16)
        cs = np.cumsum(np.cumsum(err, 0), 1)
        best, by, bx = -1, y0, x0
        for yy in range(y0, max(y0 + 1, y1 - win), 4):
            for xx in range(x0, max(x0 + 1, x1 - win), 4):
                a = cs[min(yy + win, H) - 1, min(xx + win, W) - 1]
                s = a - (cs[yy - 1, min(xx + win, W) - 1] if yy > 0 else 0) - (cs[min(yy + win, H) - 1, xx - 1] if xx > 0 else 0) + (cs[yy - 1, xx - 1] if yy > 0 and xx > 0 else 0)
                if s > best: best, by, bx = s, yy, xx
        wb = (by, min(H, by + win), bx, min(W, bx + win))
        ws = max(2, int(470 // win))
        row3 = [panel(ref, wb, ws, f"REF worst {win}px x{ws}"), panel(game, wb, ws, "NATIVE"), panel(played, wb, ws, "PLAYED FSR3 540")]
        grid([row1, row2, row3]).save(os.path.join(out, f"{tag}_sheet.png"))
        contact.setdefault(pose["scene"], []).append((D, [panel(comp, bbox, sc, f"{int(D)} m REF"), panel(game, bbox, sc, "NATIVE"),
                                                        panel(played, bbox, sc, "PLAYED FSR3 540")]))
    for s, rows in contact.items():
        g = grid([r for _, r in sorted(rows, key=lambda z: z[0])])
        if g.height > 6000: g = g.resize((g.width * 6000 // g.height, 6000))
        g.save(os.path.join(out, f"{s}_contact.png"))
    open(os.path.join(out, "summary.txt"), "w").write("\n".join(lines) + "\n")
    open(os.path.join(out, "summary.csv"), "w").write(
        "tag,scene,D,arm,ref,mask_px,ref_median_m,game_code,voxel_px,voxel01_px,sil_px,mae_luma,mae_rgb,pct_over16,detail_pct\n" + "\n".join(csv) + "\n")
    print("\n".join(lines)); print("out:", out)


if __name__ == "__main__":
    for d in sys.argv[1:]: analyse(d)
