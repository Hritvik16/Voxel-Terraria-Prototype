#!/usr/bin/env python3
"""Task 137: identity of the shells' hole leap, from captures taken with -holeleap 1.

  python3 -I tools/holeleap137.py look <LookReview dir> [out dir]
      cliff spots (-cliffbefore 3: *_native0 / *_play0 = hole leap OFF, *_native / *_play = ON) and the
      adversarial pillar (pillar_native vs pillar_native0; pillar_nonear = near sun rays through the
      shells OFF, the control). Writes pillar137.png: OFF | ON | control, and the numbers.
  python3 -I tools/holeleap137.py raw <costsplit stills dir> [...]
      cost-split -coststillraw stills: <path>_holeleap_on_raw*.png vs <path>_holeleap_off_raw*.png.
"""
import glob, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

def load(p):
    return np.asarray(Image.open(p).convert("RGB")).astype(np.int16)

def diff(a, b):
    """Pixels whose RGB differs at all, and the largest channel difference."""
    d = np.abs(load(a) - load(b))
    return int(np.count_nonzero(d.max(axis=2))), int(d.max())

def look(src, out):
    os.makedirs(out, exist_ok=True)
    lines = []
    for p0 in sorted(glob.glob(os.path.join(src, "*0.png"))):
        p1 = p0[:-5] + ".png"
        if not os.path.exists(p1): continue
        n, m = diff(p0, p1)
        lines.append(f"{os.path.basename(p1)[:-4]:24s} hole leap OFF vs ON: {n} px differ (max channel {m})")
    pn, p1, pc = (os.path.join(src, f"pillar_{a}.png") for a in ("native0", "native", "nonear"))
    if all(os.path.exists(p) for p in (pn, p1, pc)):
        n, m = diff(p1, pc)
        lines.append(f"pillar control: near sun rays through the shells OFF vs ON: {n} px differ (max channel {m}) "
                     f"-- the pillar's shadow that only the shells' march finds")
        ims = [Image.open(p).convert("RGB") for p in (pn, p1, pc)]
        w, h = ims[0].size
        sw, sh = w // 2, h // 2
        sheet = Image.new("RGB", (3 * sw, sh + 40), (20, 20, 20))
        dr = ImageDraw.Draw(sheet)
        font = ImageFont.load_default(size=24)
        dmask = np.abs(load(p1) - load(pc)).max(axis=2) > 0
        for i, (im, lbl) in enumerate(zip(ims, ("hole leap OFF (before)", "hole leap ON (after)", "control: near shadows OFF"))):
            tile = im.resize((sw, sh))
            if i == 2:   # outline where the control differs (the pillar's shadow the shells add)
                m = Image.fromarray((dmask * 255).astype(np.uint8)).resize((sw, sh))
                red = Image.new("RGB", (sw, sh), (255, 40, 40))
                tile = Image.composite(Image.blend(tile, red, 0.45), tile, m)
            sheet.paste(tile, (i * sw, 40))
            dr.text((i * sw + 10, 8), lbl, fill=(255, 255, 255), font=font)
        sheet.save(os.path.join(out, "pillar137.png"))
        lines.append(f"sheet: {os.path.join(out, 'pillar137.png')}")
    txt = "\n".join(lines)
    print(txt)
    open(os.path.join(out, "identity137.txt"), "a").write(f"== {src}\n{txt}\n")

def raw(dirs):
    for d in dirs:
        for on in sorted(glob.glob(os.path.join(d, "**", "*holeleap_on*_raw*.png"), recursive=True)):
            off = on.replace("holeleap_on", "holeleap_off")
            if os.path.exists(off):
                n, m = diff(on, off)
                print(f"{os.path.relpath(on, d)}: ON vs OFF {n} px differ (max channel {m})")

if __name__ == "__main__":
    if sys.argv[1] == "look": look(sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else "Screenshots/137")
    else: raw(sys.argv[2:])
