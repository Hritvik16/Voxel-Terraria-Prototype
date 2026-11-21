# task 232: low-res sun (copy of 228's). Sources Screenshots/232/day/<pose>_<arm>_{clean,sunvis}.png (cost split stills).
# Diff vs before (sunres_off = today's path): mean |d| (RGB 0-255) and % px whose largest channel differs by > 32, for the
# whole frame and per horizontal band (top / middle / bottom third: bottom = nearest ground at these eye-level poses).
# Sheets: rows poses; columns before | coarse >= 32 m | coarse >= 64 m | sun off, for the clean picture and the sun-visibility view.
# Zooms: 3x crop around the strongest sunvis |d| (32 m vs before) per pose: before | coarse 32 | |d| x4.
import os, sys, numpy as np
from PIL import Image, ImageDraw, ImageFont
S = "Screenshots/232"; L = "day"; OUT = os.path.join(S, "sheets"); os.makedirs(OUT, exist_ok=True)
ARMS = [("sunres_off", "before: a ray per pixel"), ("full", "low-res sun 4x4"), ("sun_off", "sun shadows off")]
POSES = ["walk_near", "forest_eye", "jungle_eye"]
FLOOR = {"walk_near": (0.90, 0.03), "forest_eye": (2.06, 0.36), "jungle_eye": (0.67, 0.08)}
font = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 28)
def load(pose, arm, kind):
    p = os.path.join(S, L, f"{pose}_{arm}_{kind}.png")
    return Image.open(p).convert("RGB") if os.path.exists(p) else None
def label(im, text):
    d = ImageDraw.Draw(im); w = d.textlength(text, font=font); d.rectangle([0, 0, w + 20, 40], fill=(0, 0, 0)); d.text((10, 4), text, fill=(255, 255, 0), font=font); return im
def stats(d):
    return d.mean(), 100.0 * (d.max(axis=2) > 32).mean()
W, H = 640, 360
lines = ["pose        view    arm         whole: mean|d|  px>32%   | top third      | middle third   | bottom third (near)   floor (clean)"]
for kind in ("clean", "sunvis"):
    sheet = Image.new("RGB", (W * len(ARMS), H * len(POSES)), (30, 30, 30))
    for r, pose in enumerate(POSES):
        ref = load(pose, "sunres_off", kind)
        for c, (arm, name) in enumerate(ARMS):
            im = load(pose, arm, kind)
            if im is None:
                sheet.paste(label(Image.new("RGB", (W, H), (40, 40, 40)), f"{pose}: {name} (not shot)"), (c * W, r * H)); continue
            if ref is not None and arm != "sunres_off":
                d = np.abs(np.asarray(im).astype(np.int16) - np.asarray(ref).astype(np.int16)); h = d.shape[0]
                parts = [stats(d)] + [stats(d[i * h // 3:(i + 1) * h // 3]) for i in range(3)]
                f = FLOOR[pose]
                lines.append(f"{pose:11s} {kind:7s} {arm:10s} " + " | ".join(f"{m:6.2f} {p:6.2f}" for m, p in parts) + f"   {f[0]:.2f} / {f[1]:.2f}")
                if kind == "sunvis" and arm == "full":
                    g = d.max(axis=2).astype(np.float32); k = 48
                    cs = np.cumsum(np.cumsum(np.pad(g, ((1, 0), (1, 0))), 0), 1)
                    box = cs[k:, k:] - cs[:-k, k:] - cs[k:, :-k] + cs[:-k, :-k]
                    y, x = np.unravel_index(np.argmax(box), box.shape); x0, y0 = max(0, x - 24), max(0, y - 24); cw, ch = 160, 90
                    x0 = min(x0, im.width - cw); y0 = min(y0, im.height - ch)
                    crop = lambda a: a.crop((x0, y0, x0 + cw, y0 + ch)).resize((cw * 3, ch * 3), Image.NEAREST)
                    dimg = Image.fromarray(np.clip(d * 4, 0, 255).astype(np.uint8))
                    for kk in ("clean", "sunvis"):
                        a, b = load(pose, "sunres_off", kk), load(pose, "full", kk)
                        if a is None or b is None: continue
                        z = Image.new("RGB", (cw * 9, ch * 3))
                        z.paste(label(crop(a), "before"), (0, 0)); z.paste(label(crop(b), "low-res 4x4"), (cw * 3, 0))
                        dd = np.abs(np.asarray(b).astype(np.int16) - np.asarray(a).astype(np.int16))
                        z.paste(label(crop(Image.fromarray(np.clip(dd * 4, 0, 255).astype(np.uint8))), "|d| x4"), (cw * 6, 0))
                        z.save(os.path.join(OUT, f"zoom_{pose}_{kk}.png"))
                    lines.append(f"            zoom at x {x0}..{x0 + cw}, y {y0}..{y0 + ch} of {im.width}x{im.height}")
            sheet.paste(label(im.resize((W, H)), f"{pose}: {name}"), (c * W, r * H))
    sheet.save(os.path.join(OUT, f"sheet_{kind}.png"))
open("Logs/overnight/232/diff.txt", "w").write("\n".join(lines) + "\n"); print("\n".join(lines))

# 228's defect place: walk_near's far bare hillside (228 zoom x 1106..1266, y 596..686 of 1920x1080), 3x: before | low-res | |d| x4.
for kk in ("clean", "sunvis"):
    a, b = load("walk_near", "sunres_off", kk), load("walk_near", "full", kk)
    if a is None or b is None: continue
    sx = a.width / 1920.0; x0, y0, cw, ch = int(1106 * sx), int(596 * sx), int(160 * sx), int(90 * sx)
    crop = lambda im: im.crop((x0, y0, x0 + cw, y0 + ch)).resize((480, 270), Image.NEAREST)
    z = Image.new("RGB", (480 * 3, 270))
    z.paste(label(crop(a), "before"), (0, 0)); z.paste(label(crop(b), "low-res 4x4"), (480, 0))
    dd = np.abs(np.asarray(b).astype(np.int16) - np.asarray(a).astype(np.int16))
    z.paste(label(crop(Image.fromarray(np.clip(dd * 4, 0, 255).astype(np.uint8))), "|d| x4"), (960, 0))
    z.save(os.path.join(OUT, f"walk_far_hill_{kk}.png"))
