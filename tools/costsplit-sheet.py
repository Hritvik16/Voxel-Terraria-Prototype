#!/usr/bin/env python3
"""Task 57: contact sheet of the -costsplit stills (one per arm), 3x3, 640x360 tiles, labelled. Args: <stills dir> <out.png> [path prefix, e.g. walk_near]."""
import sys, os
from PIL import Image, ImageDraw
src, out = sys.argv[1], sys.argv[2]
pre = sys.argv[3] + "_" if len(sys.argv) > 3 else ""
arms = ["full", "sun_off", "torch_off", "far_off", "tiers_off", "fluid_off", "ups_none", "hud_off", "tiny_gate"]
W, H = 640, 360
sheet = Image.new("RGB", (3 * W, 3 * H), (0, 0, 0))
d = ImageDraw.Draw(sheet)
for i, a in enumerate(arms):
    p = os.path.join(src, pre + a + ".png")
    if not os.path.exists(p): continue
    x, y = (i % 3) * W, (i // 3) * H
    sheet.paste(Image.open(p).convert("RGB").resize((W, H), Image.LANCZOS), (x, y))
    d.rectangle([x, y, x + 130, y + 22], fill=(0, 0, 0)); d.text((x + 6, y + 5), a, fill=(255, 255, 0))
sheet.save(out)
print(out)
