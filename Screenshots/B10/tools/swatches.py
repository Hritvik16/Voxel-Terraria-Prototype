import sys, os
sys.path.insert(0, 'tools')
from palette_defs import B10_CANDIDATES, B10_SHIPPED, NAMES, PALETTES
from PIL import Image, ImageDraw, ImageFont
D = PALETTES["D"][2]
rows = [15, 24, 35, 36, 37, 16, 25, 45, 46, 47, 48, 49]
ctx = [10, 12, 11, 13, 41, 42]   # neighbours for comparison: grass, sand, jungle grass, snow, red/orange sandstone
W, H, CW, LW = 150, 34, 150, 170
img = Image.new("RGB", (LW + CW * 3 + 30 + CW, 60 + H * len(rows)), (245, 245, 245))
d = ImageDraw.Draw(img)
try: f = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 15)
except Exception: f = ImageFont.load_default()
for j, k in enumerate("abc"):
    title = B10_CANDIDATES[k][0]
    d.text((LW + j * CW + 4, 8), k + ": " + title.split(" / ")[0] + (" *" if k == B10_SHIPPED else ""), fill=(0, 0, 0), font=f)
    d.text((LW + j * CW + 4, 28), "   " + title.split(" / ")[1], fill=(0, 0, 0), font=f)
d.text((LW + 3 * CW + 34, 8), "existing D,", fill=(0, 0, 0), font=f); d.text((LW + 3 * CW + 34, 28), "for comparison", fill=(0, 0, 0), font=f)
for i, g in enumerate(rows):
    y = 60 + i * H
    d.text((6, y + 9), f"{'SAV' if i < 5 else 'BAD'} {g} {NAMES[g]}", fill=(0, 0, 0), font=f)
    for j, k in enumerate("abc"):
        c = tuple(int(round(v * 255)) for v in B10_CANDIDATES[k][1][g])
        d.rectangle([LW + j * CW + 4, y + 2, LW + j * CW + CW - 4, y + H - 2], fill=c)
        d.text((LW + j * CW + 10, y + 9), "#%02x%02x%02x" % c, fill=(255, 255, 255) if sum(c) < 380 else (0, 0, 0), font=f)
    if i < len(ctx):
        c = tuple(int(round(v * 255)) for v in D[ctx[i]])
        d.rectangle([LW + 3 * CW + 34, y + 2, LW + 4 * CW + 26, y + H - 2], fill=c)
        d.text((LW + 3 * CW + 40, y + 9), NAMES[ctx[i]], fill=(255, 255, 255) if sum(c) < 380 else (0, 0, 0), font=f)
d.text((6, 8), "* = shipped", fill=(0, 0, 0), font=f)
img.save("Screenshots/B10/palette_candidates_swatches.png")
print("ok")
