# task 227: low-res sky fill pictures. Sources Screenshots/227/<launch>/<pose>_<arm>_clean.png (cost split stills, HUD-free).
# Diff vs before (sfres1 = task 193's inline path): mean |d| over RGB (0-255) and % px whose largest channel differs by > 32,
# beside the 219/221 noise floor (same light, two launches). Sheet: rows poses; columns before | 2x2 | 4x4 | sky fill off.
# Zooms: the 3x crop around the strongest |d| (2x2 vs before) of each pose, before | 2x2 | |d| x4.
import os, numpy as np
from PIL import Image, ImageDraw, ImageFont
S = "Screenshots/227"; OUT = os.path.join(S, "sheets"); os.makedirs(OUT, exist_ok=True)
ARMS = [("sfres1", "before: 1 ray / px"), ("full", "after: 1 ray / 2x2"), ("sfres4", "1 ray / 4x4"), ("skyfill0", "sky fill off")]
POSES = [("walk_near", "day"), ("forest_eye", "day"), ("jungle_eye", "day"), ("torch_camp", "night")]
FLOOR = {"walk_near": (0.90, 0.03), "forest_eye": (2.06, 0.36), "jungle_eye": (0.67, 0.08), "torch_camp": (0.80, 0.01)}
font = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 28)
def load(pose, launch, arm):
    p = os.path.join(S, launch, f"{pose}_{arm}_clean.png")
    return Image.open(p).convert("RGB") if os.path.exists(p) else None
def label(im, text):
    d = ImageDraw.Draw(im); w = d.textlength(text, font=font); d.rectangle([0, 0, w + 20, 40], fill=(0, 0, 0)); d.text((10, 4), text, fill=(255, 255, 0), font=font); return im
W, H = 640, 360
sheet = Image.new("RGB", (W * len(ARMS), H * len(POSES)), (30, 30, 30))
lines = ["pose        arm        mean|d| vs before   px >32 %    noise floor (mean|d| / px>32 %)"]
zooms = []
for r, (pose, launch) in enumerate(POSES):
    ref = load(pose, launch, "sfres1")
    for c, (arm, name) in enumerate(ARMS):
        im = load(pose, launch, arm)
        if im is None:
            sheet.paste(label(Image.new("RGB", (W, H), (40, 40, 40)), f"{pose}: {name} (not shot)"), (c * W, r * H)); continue
        if ref is not None and arm != "sfres1":
            d = np.abs(np.asarray(im).astype(np.int16) - np.asarray(ref).astype(np.int16))
            f = FLOOR[pose]
            lines.append(f"{pose:11s} {arm:9s} {d.mean():8.2f}          {100.0 * (d.max(axis=2) > 32).mean():6.2f}      {f[0]:.2f} / {f[1]:.2f}")
            if arm == "full":
                m = d.max(axis=2).astype(float)
                k = 48; ys, xs = m.shape
                # strongest 96x96 window (box sum on a coarse grid)
                best, by, bx = -1, 0, 0
                for y in range(0, ys - 2 * k, 16):
                    for x in range(0, xs - 2 * k, 16):
                        s_ = m[y:y + 2 * k, x:x + 2 * k].sum()
                        if s_ > best: best, by, bx = s_, y, x
                box = (bx - k, by - k, bx + 3 * k, by + 3 * k)
                box = (max(0, box[0]), max(0, box[1]), min(xs, box[2]), min(ys, box[3]))
                cw, ch = box[2] - box[0], box[3] - box[1]
                z = Image.new("RGB", (cw * 3 * 3, ch * 3))
                dimg = Image.fromarray(np.clip(m * 4, 0, 255).astype(np.uint8)).convert("RGB")
                for i, src in enumerate((ref, im, dimg)):
                    z.paste(src.crop(box).resize((cw * 3, ch * 3), Image.NEAREST), (i * cw * 3, 0))
                label(z, f"{pose}: before | 2x2 | |d| x4  (crop {box})")
                z.save(os.path.join(OUT, f"zoom_{pose}.png")); zooms.append(z)
        sheet.paste(label(im.resize((W, H), Image.LANCZOS), f"{pose}: {name}"), (c * W, r * H))
sheet.save(os.path.join(OUT, "doc_sheet.png"))
for arm, tag in (("sfres1", "before"), ("full", "after")):
    for pose, launch in POSES[:3]:
        im = load(pose, launch, arm)
        if im: im.save(os.path.join(OUT, f"{pose}_full_{tag}.png"))
open("Logs/overnight/227/diff.txt", "w").write("\n".join(lines) + "\n")
print("\n".join(lines))
