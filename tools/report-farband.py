#!/usr/bin/env python3
"""report-farband.py <arms.tsv> -- summarise run-farband.sh's sustained arms.

Each arm's number is the MEAN OF ITS 30 s WINDOWS (sustained.tsv), which is the
whole curve, not the first or last window. Arms are grouped by scenario and
label (A = far OFF, B = far ON, C = ON + far shadows); each label ran twice in
A B C C B A order, so position-in-pass drift cancels in the label mean. The
spread column is the gap between a label's two reps: a between-label difference
smaller than the within-label spread is not a result.
"""
import os
import re
import sys
from collections import defaultdict

LABELS = {"A": "far band OFF (shipped)", "B": "far band ON", "C": "far band ON + far shadows"}


def arm_stats(folder):
    tsv = os.path.join(folder, "sustained.tsv")
    txt = os.path.join(folder, "sustained.txt")
    ms = []
    if os.path.exists(tsv):
        for line in open(tsv).read().splitlines()[1:]:
            parts = line.split("\t")
            if len(parts) >= 2:
                try:
                    ms.append(float(parts[1]))
                except ValueError:
                    pass
    far = {}
    if os.path.exists(txt):
        t = open(txt).read()
        m = re.search(r"FAR RAYS/frame ([\d,]+)\s+far hits/frame ([\d,]+)\s+far steps/frame ([\d,]+)\s+steps per far ray ([\d.]+)", t)
        if m:
            far = {"rays": int(m.group(1).replace(",", "")), "hits": int(m.group(2).replace(",", "")),
                   "steps": int(m.group(3).replace(",", "")), "spr": float(m.group(4))}
    return ms, far


def main():
    rows = [l.split("\t") for l in open(sys.argv[1]).read().splitlines() if l.strip()]
    by = defaultdict(list)
    for scen, label, folder in rows:
        ms, far = arm_stats(folder.strip())
        if ms:
            by[(scen, label)].append((sum(ms) / len(ms), max(ms), far))
    print("FAR BAND SUSTAINED ARMS -- mean of 30 s windows per arm (ms/frame), wall clock, RELEASE, fullscreen")
    print(f"{'scenario':<9}{'arm':<30}{'reps':>5}{'mean ms':>9}{'FPS':>7}{'rep spread':>12}{'worst win':>11}{'far rays/f':>12}{'steps/ray':>10}")
    for scen in sorted({k[0] for k in by}):
        base = None
        for label in "ABC":
            v = by.get((scen, label))
            if not v:
                continue
            means = [x[0] for x in v]
            mean = sum(means) / len(means)
            spread = max(means) - min(means)
            worst = max(x[1] for x in v)
            far = [x[2] for x in v if x[2]]
            rays = sum(f["rays"] for f in far) / len(far) if far else 0
            spr = sum(f["spr"] for f in far) / len(far) if far else 0
            print(f"{scen:<9}{label + ' ' + LABELS[label]:<30}{len(v):>5}{mean:>9.2f}{1000 / mean:>7.1f}{spread:>12.2f}{worst:>11.2f}"
                  f"{rays:>12,.0f}{spr:>10.1f}")
            if label == "A":
                base = (mean, spread)
            elif base:
                d = mean - base[0]
                noise = max(base[1], spread)
                verdict = "WITHIN NOISE" if abs(d) <= noise else "REAL"
                print(f"{'':<9}{'  delta vs A':<30}{'':>5}{d:>+9.2f}{'':>7}{'':>12}  ({verdict}: |delta| vs max rep spread {noise:.2f})")
    print()
    print("Arms per label: 2, counterbalanced. A number is only a result if it clears the rep spread.")


if __name__ == "__main__":
    main()
