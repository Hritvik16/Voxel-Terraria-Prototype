# Task 97 strip: rows = arms, columns = +2/+4/+8 s after placement (arena, noon). Crops the pour.
import sys
from PIL import Image, ImageDraw
D = "/Users/hritvikjv/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/FarBand/"
rows = [("before: 4.5 Hz, old drain, cap 30 (29 FPS)", "2026-10-17_084219_fluidprobe_arena"),
        ("after: 6.5 Hz + option B, cap 30 (28 FPS)", "2026-10-17_084434_fluidprobe_arena"),
        ("after: 6.5 Hz + option B, cap 20 (19 FPS)", "2026-10-17_084650_fluidprobe_arena")]
box = (690, 300, 1450, 780)               # the water column + lava block
cw, ch, lw, th = 380, 240, 260, 30
sheet = Image.new("RGB", (lw + 3 * cw, th + len(rows) * ch), "white")
d = ImageDraw.Draw(sheet)
for c, s in enumerate(("+2 s", "+4 s", "+8 s")):
    d.text((lw + c * cw + cw // 2 - 12, 10), s, fill="black")
for r, (label, folder) in enumerate(rows):
    d.text((8, th + r * ch + ch // 2 - 6), label, fill="black")
    for c, k in enumerate((2, 4, 8)):
        im = Image.open(D + folder + f"/strip_{k}s.png").convert("RGB").crop(box).resize((cw, ch))
        sheet.paste(im, (lw + c * cw, th + r * ch))
sheet.save(sys.argv[1])
