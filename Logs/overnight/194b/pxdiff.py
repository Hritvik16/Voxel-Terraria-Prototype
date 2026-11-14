# Task 194b: pixel diff of two look-review folders' ws_*.png stills. usage: pxdiff.py <dirA> <dirB> <outdir>
import sys, os
from PIL import Image, ImageChops
a, b, out = sys.argv[1:4]
os.makedirs(out, exist_ok=True)
for n in sorted(x for x in os.listdir(a) if x.startswith('ws_') and x.endswith('.png')):
    if not os.path.exists(os.path.join(b, n)): print(f"{n}: missing in B"); continue
    A = Image.open(os.path.join(a, n)).convert('RGB'); B = Image.open(os.path.join(b, n)).convert('RGB')
    d = ImageChops.difference(A, B)
    px = d.load(); w, h = d.size
    diff = big = 0; mx = 0; bb = None
    g = d.convert('L').point(lambda v: 255 if v > 0 else 0)
    bb = g.getbbox()
    hist = d.convert('L').histogram()
    diff = sum(hist[1:]); big = sum(hist[9:])
    mx = max(max(c) for c in [d.getextrema()[i] for i in range(3)])
    print(f"{n}: differing px {diff} of {w*h} ({100*diff/(w*h):.3f} %), > 8 levels {big}, max channel diff {mx}, bbox {bb}")
    if diff:
        m = Image.blend(A, Image.new('RGB', A.size, (0, 0, 0)), 0.6)
        m.paste(Image.new('RGB', A.size, (255, 0, 255)), mask=g)
        m.save(os.path.join(out, 'diff_' + n))
