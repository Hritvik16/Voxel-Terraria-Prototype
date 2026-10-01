#!/usr/bin/env python3
"""landmark-view.py [out.png] -- draws Logs/TerrainGen/landmarks_diag.txt
(TerrainGenTests.Report_LandmarkInEveryDirection) over the 32 m height grid:
relief in grey, every landmark as a ring (size = prominence), every sampled
land point coloured by how many 90-degree sectors see a landmark within 3 km
(red 0 .. green 4)."""
import os, sys
import numpy as np
from PIL import Image, ImageDraw
D = "Logs/TerrainGen"
lines = open(os.path.join(D, "landmarks_diag.txt")).read().split("\n")
n = int(lines[0].split()[1])
g = np.fromfile(os.path.join(D, "landmarks_grid.f32"), dtype=np.float32).reshape(n, n)
S = 3
land = g > 0
rel = np.clip(g / max(1.0, g.max()), 0, 1)
gy, gx = np.gradient(g)
shade = np.clip(0.55 + (gx - gy) * 0.02, 0, 1)
img = np.zeros((n, n, 3))
img[..., 0] = img[..., 1] = img[..., 2] = 0.25 + 0.55 * shade * (0.5 + 0.5 * rel)
img[~land] = (0.12, 0.2, 0.3)
im = Image.fromarray((img * 255).astype(np.uint8)).resize((n * S, n * S), Image.NEAREST)
d = ImageDraw.Draw(im)
cols = [(220, 60, 50), (230, 140, 50), (230, 210, 60), (150, 210, 70), (60, 200, 90)]
for ln in lines[1:]:
    p = ln.split()
    if not p: continue
    if p[0] == "pt":
        x, z, c, c3 = int(p[1]), int(p[2]), int(p[3]), int(p[4])
        d.rectangle([x * S - 1, z * S - 1, x * S + 1, z * S + 1], fill=cols[c3])
for ln in lines[1:]:
    p = ln.split()
    if p and p[0] == "mark":
        x, z, h, pr = int(p[1]), int(p[2]), float(p[3]), float(p[4])
        r = 4 + pr / 20
        d.ellipse([x * S - r, z * S - r, x * S + r, z * S + r], outline=(255, 255, 255), width=2)
im.save(sys.argv[1] if len(sys.argv) > 1 else os.path.join(D, "landmarks_view.png"))
