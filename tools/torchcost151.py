#!/usr/bin/env python3
"""Task 151: lit pixels per torch arm. python3 -I tools/torchcost151.py <cost split run dir>/stills [out.png]
Each arm's first-pass still torch_camp_<arm>_clean.png (FSR 3 at the pinned rows, no HUD, pose 0 of the path)
against torch_camp_tl_none_clean.png (the same pose, the 32 torch lights unregistered):
  lit px = pixels whose luma (Rec.709, 0-255) is >= 10 above tl_none's; bright = luma >= 40; mean luma.
Optional sheet: the arms side by side (half size), labelled."""
import os, sys, glob
import numpy as np
from PIL import Image, ImageDraw, ImageFont

src = sys.argv[1]
out = sys.argv[2] if len(sys.argv) > 2 else None
W = np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)


def luma(p):
    return np.asarray(Image.open(p).convert("RGB")).astype(np.float32) @ W


base_p = os.path.join(src, "torch_camp_tl_none_clean.png")
base = luma(base_p) if os.path.exists(base_p) else None
files = sorted(glob.glob(os.path.join(src, "torch_camp_*_clean.png")))
order = lambda f: (0 if "_full_" in f else 1 if "tl_none" in f else 2, f)
files.sort(key=order)
print(f"{'arm':<22}{'lit px':>11}{'bright px':>11}{'mean luma':>11}")
cells = []
for f in files:
    arm = os.path.basename(f)[len("torch_camp_"):-len("_clean.png")]
    y = luma(f)
    lit = int(((y - base) >= 10).sum()) if base is not None and base.shape == y.shape else -1
    print(f"{arm:<22}{lit:>11,}{int((y >= 40).sum()):>11,}{y.mean():>11.1f}")
    cells.append((arm, f))
if out and cells:
    w, h = 960, 540
    font = ImageFont.load_default(size=30)
    sheet = Image.new("RGB", (w * min(3, len(cells)), h * ((len(cells) + 2) // 3)), (0, 0, 0))
    for i, (arm, f) in enumerate(cells):
        im = Image.open(f).convert("RGB").resize((w, h))
        ImageDraw.Draw(im).text((12, 10), arm, fill=(255, 255, 0), font=font)
        sheet.paste(im, ((i % 3) * w, (i // 3) * h))
    sheet.save(out)
    print("sheet:", out)
