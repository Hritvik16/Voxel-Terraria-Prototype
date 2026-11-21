# Task 246: contact sheet (forest_eye, 4 tree arms x 2 suns, + walk_open control) and an "image changed" coverage proxy vs trees OFF.
import os, sys
from PIL import Image, ImageDraw, ImageChops
D = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.abspath(os.path.join(D, "../../.."))
OUT = os.path.join(ROOT, "Screenshots/246")
dirs = {}
for ln in open(os.path.join(D, "dirs.txt")):
    k, v = ln.split(": ", 1); dirs[k.replace("data_", "")] = v.strip()
ARMS = ["off", "today", "sparse", "dense"]; LAB = {"off": "trees OFF (-trees 0)", "today": "pre-210 trees (-trees2 0)", "sparse": "210 sparse (-trees2dense 0)", "dense": "dense = default (-trees2 1)"}
W, H = 640, 360
rows = [("forest_eye", "16"), ("forest_eye", "noon"), ("walk_open", "16")]
sheet = Image.new("RGB", (W * 4, (H + 22) * len(rows) + 4), (20, 20, 20)); dr = ImageDraw.Draw(sheet)
lines = []
for ri, (pose, sun) in enumerate(rows):
    base = None
    for ci, a in enumerate(ARMS):
        p = os.path.join(dirs.get(f"{a}_{sun}", ""), "stills", f"{pose}_full.png")
        x, y = ci * W, ri * (H + 22)
        if not os.path.exists(p): dr.text((x + 6, y + 4), f"{pose} {sun} {a}: missing", fill=(255, 80, 80)); continue
        im = Image.open(p).convert("RGB")
        if a == "off": base = im
        elif base is not None and base.size == im.size:
            g = ImageChops.difference(base, im).convert("L").point(lambda v: 255 if v > 24 else 0)
            frac = sum(g.histogram()[255:]) / (g.size[0] * g.size[1])
            lines.append(f"{pose} {sun} {a}: {frac*100:.1f} % of pixels changed by > 24/255 vs trees OFF (trees + their shadows + streaming/temporal noise)")
        sheet.paste(im.resize((W, H)), (x, y + 22))
        dr.text((x + 6, y + 4), f"{pose}  sun {sun}  {LAB[a]}", fill=(230, 230, 230))
sheet.save(os.path.join(OUT, "sheet246.jpg"), quality=88)
pair = Image.new("RGB", (1920, 540), (0, 0, 0))
a = os.path.join(dirs["off_16"], "stills", "forest_eye_full.png"); b = os.path.join(dirs.get("dense_16", ""), "stills", "forest_eye_full.png")
if os.path.exists(a) and os.path.exists(b):
    pair.paste(Image.open(a).convert("RGB").resize((960, 540)), (0, 0)); pair.paste(Image.open(b).convert("RGB").resize((960, 540)), (960, 0))
    pair.save(os.path.join(OUT, "forest_eye_off_vs_dense_16.jpg"), quality=90)
open(os.path.join(D, "coverage_proxy.txt"), "w").write("\n".join(lines) + "\n"); print("\n".join(lines))
