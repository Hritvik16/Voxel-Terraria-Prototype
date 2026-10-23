#!/usr/bin/env python3
"""tools/cross127-sheet.py -- task 127: large side-by-side comparison images from -lookcross stills.

  pair A.png B.png OUT.png "label A" "label B" [width]
      A | B, each scaled to `width` px wide (default 1280, so the pair is 2560 px), labels on top.
  crop A.png B.png OUT.png "label A" "label B" x y w h [scale]
      the same w x h box (1920x1080 output px) of both, scaled x`scale` (default 2, nearest), side by side,
      plus a px-diff line on stdout (mean abs RGB diff inside the box).
  mark IN.png OUT.png x y w h [x y w h ...]
      IN with yellow boxes drawn (where the crops come from).
"""
import sys
from PIL import Image, ImageDraw, ImageChops, ImageStat, ImageFont

BAR = 34


def labelled(imgs, labels):
    w = sum(i.width for i in imgs)
    h = max(i.height for i in imgs)
    out = Image.new("RGB", (w, h + BAR), (16, 16, 16))
    dr = ImageDraw.Draw(out)
    x = 0
    for i, lab in zip(imgs, labels):
        out.paste(i, (x, BAR))
        dr.text((x + 10, 5), lab, fill=(255, 230, 0), font=ImageFont.load_default(size=22))
        x += i.width
    return out


def main(a):
    cmd = a[0]
    if cmd == "pair":
        A, B, O, la, lb = a[1:6]
        w = int(a[6]) if len(a) > 6 else 1280
        ims = []
        for p in (A, B):
            im = Image.open(p).convert("RGB")
            ims.append(im.resize((w, round(im.height * w / im.width)), Image.LANCZOS))
        labelled(ims, [la, lb]).save(O)
    elif cmd == "crop":
        A, B, O, la, lb = a[1:6]
        x, y, w, h = (int(v) for v in a[6:10])
        s = int(a[10]) if len(a) > 10 else 2
        ims = [Image.open(p).convert("RGB").crop((x, y, x + w, y + h)) for p in (A, B)]
        d = ImageStat.Stat(ImageChops.difference(ims[0], ims[1])).mean
        print(f"{O}: box {x},{y} {w}x{h}: mean abs diff RGB {d[0]:.1f} {d[1]:.1f} {d[2]:.1f}")
        ims = [i.resize((w * s, h * s), Image.NEAREST) for i in ims]
        labelled(ims, [la, lb]).save(O)
    elif cmd == "mark":
        I, O = a[1:3]
        im = Image.open(I).convert("RGB")
        dr = ImageDraw.Draw(im)
        v = [int(t) for t in a[3:]]
        for k in range(0, len(v), 4):
            x, y, w, h = v[k:k + 4]
            dr.rectangle((x, y, x + w, y + h), outline=(255, 230, 0), width=3)
        im.save(O)
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main(sys.argv[1:])
