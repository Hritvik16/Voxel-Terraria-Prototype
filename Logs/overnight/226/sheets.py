# task 226: look-polish sheets. Sources Screenshots/226/<launch>/<pose>_<arm>_clean.png (cost split stills, HUD-free).
# Columns: before (lotl_off) | one piece each (vary_only, crevice_only, aerial_only, bloom_only) | all four (FULL).
# Diff vs before: mean |d| over RGB (0-255) and % pixels whose largest channel differs by > 16.
import os, numpy as np
from PIL import Image, ImageDraw, ImageFont
S = "Screenshots/226"; OUT = os.path.join(S, "sheets"); os.makedirs(OUT, exist_ok=True)
ARMS = [("lotl_off", "before (all 4 off)"), ("vary_only", "+ voxel colour"), ("crevice_only", "+ crevice shade"),
        ("aerial_only", "+ aerial haze"), ("bloom_only", "+ bloom"), ("full", "all four (after)")]
POSES = [("walk_near", "day"), ("forest_eye", "day"), ("jungle_eye", "day"), ("torch_camp", "night")]
font = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 30)
def path(pose, launch, arm): return os.path.join(S, launch, f"{pose}_{arm}_clean.png")
def load(pose, launch, arm):
    p = path(pose, launch, arm); return Image.open(p).convert("RGB") if os.path.exists(p) else None
def label(im, text):
    d = ImageDraw.Draw(im); w = d.textlength(text, font=font); d.rectangle([0, 0, w + 24, 44], fill=(0, 0, 0)); d.text((12, 5), text, fill=(255, 255, 0), font=font); return im
def stats(a):
    l = np.asarray(a).astype(float) @ [0.2126, 0.7152, 0.0722]
    return np.median(l), 100.0 * (l < 8).mean()
W, H = 640, 360
sheet = Image.new("RGB", (W * len(ARMS), H * len(POSES)), (30, 30, 30))
lines = ["pose        arm            mean|d| vs before  px >16 %   median luma  black(<8) %"]
for r, (pose, launch) in enumerate(POSES):
    ref = load(pose, launch, "lotl_off")
    for c, (arm, name) in enumerate(ARMS):
        im = load(pose, launch, arm)
        if im is None:
            t = label(Image.new("RGB", (W, H), (40, 40, 40)), f"{pose}: {name} (not shot)"); sheet.paste(t, (c * W, r * H)); continue
        if ref is not None:
            d = np.abs(np.asarray(im).astype(np.int16) - np.asarray(ref).astype(np.int16))
            med, blk = stats(im)
            lines.append(f"{pose:11s} {arm:14s} {d.mean():8.2f}          {100.0 * (d.max(axis=2) > 16).mean():6.2f}    {med:7.1f}      {blk:6.2f}")
        sheet.paste(label(im.resize((W, H), Image.LANCZOS), f"{pose}: {name}"), (c * W, r * H))
sheet.save(os.path.join(OUT, "doc_sheet.png"))
# full-size forest pair + before|after|LotL sheet (LotL crops from task 222's side_by_side.png: right column)
for arm, tag in (("lotl_off", "before"), ("full", "after")):
    im = load("forest_eye", "day", arm)
    if im: im.save(os.path.join(OUT, f"forest_eye_full_{tag}.png"))
sbs = Image.open("Screenshots/222/side_by_side.png").convert("RGB")
lotl = [sbs.crop((640, 40 + 360 * i, 1280, 400 + 360 * i)) for i in range(3)]
cmp_ = Image.new("RGB", (W * 3, H * 3))
for r, (pose, _) in enumerate(POSES[:3]):
    for c, (arm, name) in enumerate((("lotl_off", "ours before"), ("full", "ours after"))):
        im = load(pose, "day", arm)
        if im: cmp_.paste(label(im.resize((W, H), Image.LANCZOS), f"{pose}: {name}"), (c * W, r * H))
    cmp_.paste(label(lotl[r].resize((W, H)), "Lay of the Land (Steam shot)"), (2 * W, r * H))
cmp_.save(os.path.join(OUT, "sheet_lotl.png"))
open("Logs/overnight/226/diff.txt", "w").write("\n".join(lines) + "\n"); print("\n".join(lines))
