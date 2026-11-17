#!/usr/bin/env python3
"""Task 225 after-fix comparison. usage: python3 -I compare.py   (run from the repo root)
Per pose and arm: median displayed luma (byte) per pixel group (groups from the BEFORE build's SHADE TERMS class map at
the same pose: Screenshots/225/day0|day1/<pose>_full_shade15.png), share of 'black' pixels (luma < 16), and the pixel diff
FULL vs lookfix_off. Writes Screenshots/225/sheet_day.png, sheet_lotl.png, diff_*.png."""
import os, sys
import numpy as np
from PIL import Image, ImageDraw
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import probe as P
R = "Screenshots/225"
ARMS = ["lookfix_off", "lookexact_off", "skylinear_off", "full"]
def luma(a): return a[..., 0] * 0.2126 + a[..., 1] * 0.7152 + a[..., 2] * 0.0722
def still(d, pose, arm): return os.path.join(R, d, f"{pose}_{arm}_clean.png")
def masks(pose):
    for d in ("day0", "day1"):
        f = os.path.join(R, d, f"{pose}_full_shade15.png")
        if os.path.exists(f):
            im = P.load(f); t, _, _ = P.calib(im); c = P.decode(im, t); cls = c[..., 0]; sun = P.Q(c[..., 2])
            ok = np.ones_like(cls, bool); ok[:95] = False; ok[:290, 1640:] = False; ok[505:880, :965] = False; ok[935:] = False
            for dy, dx in ((3, 0), (-3, 0), (0, 3), (0, -3)): ok &= np.roll(cls, (dy, dx), axis=(0, 1)) == cls
            mat, face, sh = cls & 3, (cls >> 2) & 3, (cls >> 4) & 1; t0 = ok & (sh == 0)
            return {"trunk side shade": t0 & (mat == 1) & (face == 1) & (sun < 0.5), "trunk side sun": t0 & (mat == 1) & (face == 1) & (sun >= 0.5),
                    "leaf": t0 & (mat == 2), "ground up shade": t0 & (mat == 3) & (face == 0) & (sun < 0.5),
                    "ground up sun": t0 & (mat == 3) & (face == 0) & (sun >= 0.5), "other side shade": t0 & (mat == 3) & (face == 1) & (sun < 0.5)}
    return {}
def main(day="day2", night="night2"):
    poses = ["walk_near", "forest_eye", "jungle_eye", "cave_mouth"]
    view = np.ones((1080, 1920), bool); view[:290, 1640:] = False   # minimap out
    for pose in poses:
        ims = {a: P.load(still(day, pose, a)).astype(float) for a in ARMS if os.path.exists(still(day, pose, a))}
        if not ims: print(f"== {pose}: no stills"); continue
        ms = masks(pose)
        print(f"== {pose}: median displayed luma byte per group (class map from the before build); black = luma < 16")
        print(f"   {'group':18s} {'px':>7s} " + " ".join(f"{a:>13s}" for a in ims))
        for g, m in ms.items():
            if m.sum() < 200: continue
            print(f"   {g:18s} {int(m.sum()):7d} " + " ".join(f"{np.median(luma(v)[m]):13.1f}" for v in ims.values()))
        print(f"   {'black share %':18s} {'':7s} " + " ".join(f"{100 * (luma(v)[view] < 16).mean():13.1f}" for v in ims.values()))
        if "full" in ims and "lookfix_off" in ims:
            d = np.abs(ims["full"] - ims["lookfix_off"]).max(axis=2)
            print(f"   diff FULL vs before: mean {d[view].mean():.1f} levels, px changed > 8: {100 * (d[view] > 8).mean():.1f} %")
            Image.fromarray(np.clip(d * 4, 0, 255).astype(np.uint8)).save(os.path.join(R, f"diff_{pose}.png"))
    # night: whatever the torch path shot
    nd = os.path.join(R, night)
    if os.path.isdir(nd):
        for f in sorted(os.listdir(nd)):
            if f.endswith("_full_clean.png"):
                b = f.replace("_full_clean", "_lookfix_off_clean")
                if not os.path.exists(os.path.join(nd, b)): continue
                A, B = P.load(os.path.join(nd, b)).astype(float), P.load(os.path.join(nd, f)).astype(float)
                d = np.abs(B - A).max(axis=2)
                print(f"== night {f[:-15]}: median luma before {np.median(luma(A)[view]):.1f} after {np.median(luma(B)[view]):.1f}; "
                      f"black share {100 * (luma(A)[view] < 16).mean():.1f} -> {100 * (luma(B)[view] < 16).mean():.1f} %; diff mean {d[view].mean():.1f}, >8 {100 * (d[view] > 8).mean():.1f} %")
                Image.fromarray(np.clip(d * 4, 0, 255).astype(np.uint8)).save(os.path.join(R, f"diff_night_{f[:-15]}.png"))
                poses.append(("night", f[:-15]))
    # sheets
    W, H = 640, 360
    def thumb(p): return Image.open(p).convert("RGB").resize((W, H), Image.LANCZOS)
    rows = []
    for pose in poses:
        if isinstance(pose, tuple): d, n = night, pose[1]
        else: d, n = day, pose
        a, b = still(d, n, "lookfix_off"), still(d, n, "full")
        if os.path.exists(a) and os.path.exists(b): rows.append((n, thumb(a), thumb(b)))
    sheet = Image.new("RGB", (2 * W, 30 + H * len(rows)), (0, 0, 0)); dr = ImageDraw.Draw(sheet)
    dr.text((10, 8), "BEFORE (lookfix_off = pre-225)", fill=(255, 255, 0)); dr.text((W + 10, 8), "AFTER (task 225, both fixes ON)", fill=(255, 255, 0))
    for i, (n, a, b) in enumerate(rows):
        sheet.paste(a, (0, 30 + i * H)); sheet.paste(b, (W, 30 + i * H)); dr.text((10, 36 + i * H), n, fill=(255, 255, 0))
    sheet.save(os.path.join(R, "sheet_before_after.png"))
    ref = Image.open("Screenshots/222/side_by_side.png").convert("RGB")
    lotl = {"forest_eye": ref.crop((640, 400, 1280, 760)), "jungle_eye": ref.crop((640, 760, 1280, 1120))}
    rows = [(n, thumb(still(day, n, "lookfix_off")), thumb(still(day, n, "full")), lotl[n]) for n in lotl if os.path.exists(still(day, n, "full"))]
    s2 = Image.new("RGB", (3 * W, 30 + H * len(rows)), (0, 0, 0)); dr = ImageDraw.Draw(s2)
    for j, t in enumerate(["BEFORE (pre-225)", "AFTER (task 225)", "LAY OF THE LAND (Steam shot, task 222)"]): dr.text((j * W + 10, 8), t, fill=(255, 255, 0))
    for i, (n, a, b, c) in enumerate(rows):
        for j, im in enumerate((a, b, c)): s2.paste(im, (j * W, 30 + i * H))
        dr.text((10, 36 + i * H), n, fill=(255, 255, 0))
    s2.save(os.path.join(R, "sheet_lotl.png"))
    print("sheets:", os.path.join(R, "sheet_before_after.png"), os.path.join(R, "sheet_lotl.png"))
if __name__ == "__main__": main(*sys.argv[1:])
