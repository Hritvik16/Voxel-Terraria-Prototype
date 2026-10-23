#!/usr/bin/env python3
"""tools/rim126-sheet.py <dir> -- task 126: OLD 121 (<pose>_sky_<t>_rim121) | NEW 126 (<pose>_sky_<t>) sheets per pose,
rows noon, golden away/toward, sunset, dusk toward/away; and the px diff new vs 121 per time (rim126_diff.txt)."""
import sys, os
from PIL import Image, ImageDraw, ImageChops
d = sys.argv[1]
times = ["noon", "golden_away", "golden_toward", "sunset_toward", "dusk_toward", "dusk_away"]
out = []
for pose in ["high", "walk_open"]:
    tiles = []
    for t in times:
        a = Image.open(os.path.join(d, f"{pose}_sky_{t}_rim121.png")).convert("RGB")
        b = Image.open(os.path.join(d, f"{pose}_sky_{t}.png")).convert("RGB")
        diff = ImageChops.difference(a, b)
        px = list(diff.getdata())
        n = sum(1 for p in px if max(p) > 0); n2 = sum(1 for p in px if max(p) > 2)
        out.append(f"{pose} {t}: px differing {n} ({100.0*n/len(px):.3f}%), >2 levels {n2}, max {max(max(p) for p in px)}")
        tiles.append((t, a, b))
    w, h = 800, 450
    sheet = Image.new("RGB", (2 * w, h * len(tiles)))
    dr = ImageDraw.Draw(sheet)
    for i, (t, a, b) in enumerate(tiles):
        sheet.paste(a.resize((w, h)), (0, i * h)); sheet.paste(b.resize((w, h)), (w, i * h))
        dr.text((8, i * h + 6), f"OLD 121 {t}", fill=(255, 255, 0)); dr.text((w + 8, i * h + 6), f"NEW 126 {t}", fill=(255, 255, 0))
    sheet.save(os.path.join(d, f"{pose}_rim_sheet.png"))
open(os.path.join(d, "rim126_diff.txt"), "w").write("\n".join(out) + "\n")
print("\n".join(out))
