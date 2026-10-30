#!/usr/bin/env python3
"""Task 153: identity of the shells' column height bound (-shellheightbound), from raw/native stills.

  python3 -I tools/heightbound153.py look <LookReview dir> [sheet.png]
      -cliffbefore 4 pairs: *_native0 / *_play0 = height bound OFF, *_native / *_play = ON; the edit-op pillar
      (pillar_native0 vs pillar_native) and its control pillar_nonear (near sun rays through the shells OFF).
      With a sheet path, writes OFF | ON | control of the pillar.
  python3 -I tools/heightbound153.py raw <costsplit stills dir>
      <path>_heightbound_on_raw*.png vs <path>_heightbound_off_raw*.png.
FSR 3 'play' stills are temporal/jittered: listed, not an identity measure.
"""
import glob, os, sys
import numpy as np
from PIL import Image, ImageDraw

def load(p): return np.asarray(Image.open(p).convert("RGB")).astype(np.int16)

def diff(a, b):
    d = np.abs(load(a) - load(b))
    return int(np.count_nonzero(d.max(axis=2))), int(d.max())

def look(src, sheet=None):
    for p0 in sorted(glob.glob(os.path.join(src, "*0.png"))):
        p1 = p0[:-5] + ".png"
        if os.path.exists(p1):
            n, m = diff(p0, p1)
            print(f"{os.path.basename(p1)[:-4]:24s} height bound OFF vs ON: {n} px differ (max channel {m})")
    pn, p1, pc = (os.path.join(src, f"pillar_{a}.png") for a in ("native0", "native", "nonear"))
    if all(os.path.exists(p) for p in (pn, p1, pc)):
        n, m = diff(p1, pc)
        print(f"pillar control: near sun rays through the shells OFF vs ON: {n} px differ (max channel {m})")
        if sheet:
            ims = [Image.open(p).convert("RGB") for p in (pn, p1, pc)]
            w, h = ims[0].size; sw, sh = w // 2, h // 2
            out = Image.new("RGB", (3 * sw, sh + 30), (20, 20, 20)); dr = ImageDraw.Draw(out)
            for i, (im, t) in enumerate(zip(ims, ("height bound OFF", "height bound ON", "control: near shell shadows OFF"))):
                out.paste(im.resize((sw, sh)), (i * sw, 30)); dr.text((i * sw + 8, 8), t, fill=(255, 255, 255))
            out.save(sheet); print("sheet:", sheet)

def raw(src):
    for p1 in sorted(glob.glob(os.path.join(src, "*_heightbound_on_raw*.png"))):
        p0 = p1.replace("_heightbound_on_", "_heightbound_off_")
        if os.path.exists(p0):
            n, m = diff(p0, p1)
            print(f"{os.path.basename(p1)[:-4]:40s} OFF vs ON: {n} px differ (max channel {m})")

if __name__ == "__main__":
    {"look": lambda: look(sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else None), "raw": lambda: raw(sys.argv[2])}[sys.argv[1]]()
