#!/usr/bin/env python3
"""Task 133: before/after of near sun rays through the shells, from one task-131 capture run
with -cliffbefore 1 (A/C shots taken with RaymarchFeature.NearShadowShells OFF first: *_play0,
*_native0; then ON: *_play, *_native).

  python3 -I tools/nearshadow133.py <LookReview dir> [out dir]

Per spot: a sheet, row 1 A play look OFF | ON, row 2 C (250 m up, window outline yellow, player
red) OFF | ON; and numbers: C luma just inside vs just outside the window edge (all edges, and the
toward-sun edge = the top one), A mean luma of tier-0 pixels vs shell pixels, and how many pixels
changed between OFF and ON (A native, C play).
"""
import json, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

src = sys.argv[1]
out = sys.argv[2] if len(sys.argv) > 2 else "Screenshots/133"
os.makedirs(out, exist_ok=True)
meta = json.load(open(os.path.join(src, "cliffshadow.json")))
W, H = meta["screen"]
pw, ph = W // 2, H // 2
font = ImageFont.load_default(size=26)

def decode(path):
    """Debug view 14 (task 129): R = what drew the pixel (0 sky, 1 tier 0, 1 + k shell k, 12 far band)."""
    img = np.asarray(Image.open(path).convert("RGB")).astype(np.float32)
    for rows in (slice(0, 4), slice(img.shape[0] - 4, img.shape[0])):
        lv = np.array([np.median(img[rows][:, 16 * c + 4:16 * c + 12, :], axis=(0, 1)) for c in range(64)])
        if np.all(np.diff(lv, axis=0) > 0.5):
            break
    code = np.argmin(np.abs(img[:, :, 0][..., None] - lv[None, None, :, 0]), axis=2)
    code[rows] = -1
    return code


def load(name):
    p = os.path.join(src, name + ".png")
    return np.asarray(Image.open(p).convert("RGB")).astype(np.float32) if os.path.exists(p) else None


def luma(a):
    return 0.2126 * a[:, :, 0] + 0.7152 * a[:, :, 1] + 0.0722 * a[:, :, 2]


def edge_bands(corners, band=16):
    """inside / outside masks of a band of `band` px each side of the window outline; also the top edge only."""
    pts = [tuple(c) for c in corners]
    inside = Image.new("L", (W, H), 0); ImageDraw.Draw(inside).polygon(pts, fill=255)
    ring = Image.new("L", (W, H), 0); ImageDraw.Draw(ring).line(pts + [pts[0]], fill=255, width=2 * band)
    top = Image.new("L", (W, H), 0)
    srt = sorted(pts, key=lambda p: p[1])[:2]   # the two corners highest on screen = the toward-sun edge
    ImageDraw.Draw(top).line(srt, fill=255, width=2 * band)
    i, r, t = (np.asarray(m) > 0 for m in (inside, ring, top))
    # keep the middle 80% of the top edge (corners mix two edges)
    xs = sorted(p[0] for p in srt); x0 = xs[0] + 0.1 * (xs[1] - xs[0]); x1 = xs[1] - 0.1 * (xs[1] - xs[0])
    cols = np.zeros((H, W), bool); cols[:, int(max(0, x0)):int(min(W, x1))] = True
    t &= cols
    return r & i, r & ~i, t & i, t & ~i


def panel(a, label, overlay=None):
    im = Image.fromarray(a.astype(np.uint8)) if a is not None else Image.new("RGB", (W, H), (60, 0, 60))
    d = ImageDraw.Draw(im)
    if overlay:
        corners, q = overlay
        pts = [tuple(c) for c in corners]
        d.line(pts + [pts[0]], fill=(255, 230, 0), width=4)
        d.ellipse([q[0] - 14, q[1] - 14, q[0] + 14, q[1] + 14], outline=(255, 40, 40), width=5)
    im = im.resize((pw, ph), Image.LANCZOS)
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, pw, 36], fill=(0, 0, 0))
    d.text((8, 4), label, fill=(255, 255, 255), font=font)
    return im


lines = []
for s in meta["spots"]:
    t = s["tag"]
    ov = (s["C"]["windowPx"], s["C"]["qPx"])
    A0, A1 = load(f"{t}_A_play0"), load(f"{t}_A_play")
    C0, C1 = load(f"{t}_C_play0"), load(f"{t}_C_play")
    sheet = Image.new("RGB", (W, 2 * ph + 44), (0, 0, 0))
    ImageDraw.Draw(sheet).text((8, 8), f"task 133 {t}: sun {s['elev']:.0f} deg, shadow cast {s['casterM']:.0f} m away by terrain up to "
                               f"{s['cliffH']:.0f} m above. Left = before (OFF), right = after (-nearshadowshells 1)", fill=(255, 255, 255), font=font)
    sheet.paste(panel(A0, "A before: eye 1.6 m in the shadow (play look)"), (0, 44))
    sheet.paste(panel(A1, "A after"), (pw, 44))
    sheet.paste(panel(C0, "C before: 250 m up; yellow = 0.1 m window", ov), (0, 44 + ph))
    sheet.paste(panel(C1, "C after", ov), (pw, 44 + ph))
    path = os.path.join(out, f"{t}_before_after.png"); sheet.save(path); lines.append(path)

    ri, ro, ti, to = edge_bands(s["C"]["windowPx"])
    for nm, C in (("before", C0), ("after", C1)):
        if C is None: continue
        l = luma(C)
        lines.append(f"{t} C {nm}: luma just inside vs just outside the window edge: all edges {l[ri].mean():.0f} vs {l[ro].mean():.0f}; "
                     f"toward-sun (top) edge {l[ti].mean():.0f} vs {l[to].mean():.0f}")
    hp = os.path.join(src, f"{t}_A_hit.png")
    if os.path.exists(hp):
        code = decode(hp); t0, sh = code == 1, (code >= 2) & (code <= 8)
        for nm, A in (("before", A0), ("after", A1)):
            if A is None: continue
            l = luma(A)
            lines.append(f"{t} A {nm}: mean luma tier-0 pixels {l[t0].mean():.0f} ({100 * t0.mean():.0f}% of frame) vs shell pixels {l[sh].mean():.0f}")
    for v, a, b in (("A native", load(f"{t}_A_native0"), load(f"{t}_A_native")), ("C play", C0, C1)):
        if a is None or b is None: continue
        d = np.abs(a - b).max(axis=2)
        lines.append(f"{t} {v}: pixels changed OFF -> ON {100 * (d > 0).mean():.2f}% (> 8 levels {100 * (d > 8).mean():.2f}%); "
                     f"mean luma {luma(a).mean():.1f} -> {luma(b).mean():.1f}")
print("\n".join(lines))
open(os.path.join(out, "nearshadow133.txt"), "w").write("\n".join(lines) + "\n")
