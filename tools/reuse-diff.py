#!/usr/bin/env python3
"""Task 124: each *_reuse<N>*.png vs its *_reuse1* reference (trace every frame).
Prints pixels differing (any channel > 8 of 255) and mean abs diff; writes <name>_vs1.png (diff x4) for N != 1."""
import sys, os, re, glob
import numpy as np
from PIL import Image
d = sys.argv[1]
rows = []
for p in sorted(glob.glob(os.path.join(d, '*_reuse*.png'))):
    b = os.path.basename(p)
    if b.endswith('_vs1.png'): continue
    m = re.search(r'_reuse(\d+)', b)
    if m.group(1) == '1': continue
    ref = os.path.join(d, b.replace(m.group(0), '_reuse1'))
    if not os.path.exists(ref): continue
    a = np.asarray(Image.open(p).convert('RGB')).astype(np.int16)
    r = np.asarray(Image.open(ref).convert('RGB')).astype(np.int16)
    df = np.abs(a - r)
    bad = (df.max(axis=2) > 8)
    rows.append((b, int(bad.sum()), bad.mean() * 100, df.mean()))
    Image.fromarray(np.clip(df * 4, 0, 255).astype(np.uint8)).save(os.path.join(d, b[:-4] + '_vs1.png'))
for b, n, pc, md in rows:
    print(f'{b:60s} {n:8d} px  {pc:6.3f}%  mean {md:.3f}')
