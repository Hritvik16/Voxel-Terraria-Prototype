# task 227: before | 2x2 | 4x4 at 3x, the jungle trunk / ground edge and the forest trunk feet (fixed crops).
import numpy as np
from PIL import Image
S = "Screenshots/227/day"
for pose, box in (("jungle_eye", (900, 700, 1220, 1000)), ("forest_eye", (560, 520, 880, 720))):
    ims = [Image.open(f"{S}/{pose}_{a}_clean.png").convert("RGB").crop(box) for a in ("sfres1", "full", "sfres4")]
    w, h = ims[0].size
    z = Image.new("RGB", (w * 3 * 3, h * 3))
    for i, im in enumerate(ims): z.paste(im.resize((w * 3, h * 3), Image.NEAREST), (i * w * 3, 0))
    z.save(f"Screenshots/227/sheets/zoom4_{pose}.png")
