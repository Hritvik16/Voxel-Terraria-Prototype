# Task 248 (WORLD_FILL_BUDGET sec. 5 E1): direct porous / solid coverage per band and arm, and sec. 1b.2/1b.3's rates re-derived
# from it (counts only, no time). Run: python3 -I Screenshots/248/coverage.py
import os, sys; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from parse import R, ARMS, BANDS
P = "forest_eye"; S = "16"
def t0all(r):
    return sum(r["rs"][(k, "all")]["steps"] * r["rs"][(k, "all")]["t0"] / 100 for k in ("primary", "sun", "sky")) / 1e6
print("== forest_eye 16 deg: primary hit material, % of the SCREEN per band (leaf = porous; wood + other = solid content on the ground)")
print(f"{'arm':7} {'band':8} {'screen':>7} {'leaf':>6} {'wood':>6} {'ground':>7} {'other':>6} {'no hit':>7} {'porous':>7} {'solid':>6} {'solid-nogr':>10}")
for a in ARMS:
    r = R.get((a, S), {}).get(P)
    if not r or not r["mat"]: print(f"{a:7} (missing)"); continue
    for b in BANDS:
        m = r["mat"].get(b)
        if m: print(f"{a:7} {b:8} {m['screen']:6.1f}% {m['leaf']:5.1f}% {m['wood']:5.1f}% {m['ground']:6.1f}% {m['other']:5.1f}% {m['none']:6.1f}% {m['porous']:6.1f}% {m['solid']:5.1f}% {m['solidng']:9.1f}%")
print("\n== tier-0 steps (primary + sun + sky, M/frame) vs coverage; d = change vs the off arm")
rows = []
base = R[("off", S)][P]
b0 = t0all(base); bl = base["mat"]["all"]["leaf"]; bw = base["mat"]["all"]["wood"] + base["mat"]["all"]["other"]
for a in ARMS:
    r = R[(a, S)][P]; t = t0all(r); m = r["mat"]["all"]
    dl, dw, dt = m["leaf"] - bl, m["wood"] + m["other"] - bw, t - b0
    print(f"{a:7} t0 {t:6.2f} M  leaf {m['leaf']:5.1f}%  wood+other {m['wood'] + m['other']:5.1f}%  ground {m['ground']:5.1f}%  no hit {m['none']:5.1f}%   d: t0 {dt:+5.2f} M, leaf {dl:+5.1f}, solid {dw:+5.1f}")
    if a != "off": rows.append((dl, dw, dt, a))
# Two-term fit through the off arm: dt = a * dleaf + b * dsolid (3 tree arms, 2 unknowns, least squares; no intercept).
Sll = sum(r[0] * r[0] for r in rows); Sww = sum(r[1] * r[1] for r in rows); Slw = sum(r[0] * r[1] for r in rows)
Slt = sum(r[0] * r[2] for r in rows); Swt = sum(r[1] * r[2] for r in rows)
det = Sll * Sww - Slw * Slw
A = (Slt * Sww - Swt * Slw) / det; B = (Swt * Sll - Slt * Slw) / det
print(f"\nfit (3 tree arms, through off): tier-0 steps per 1 % of screen: porous (leaf) {A * 1e3:+.0f} k, solid (wood + other) {B * 1e3:+.0f} k")
for dl, dw, dt, a in rows: print(f"  {a:7} measured {dt:+.2f} M, fit {A * dl + B * dw:+.2f} M, residual {dt - A * dl - B * dw:+.2f} M")
# Each arm alone with 246's one-term view (all of the change put on leaves) for comparison.
for dl, dw, dt, a in rows: print(f"  {a:7} one-term (all on leaf) {dt / dl * 1e3:+.0f} k per 1 % leaf" if dl > 0 else f"  {a:7} no leaf")
for lo, hi in ((5.7, 7.5),):
    print(f"\nms (est., 246's calibration {lo}-{hi} ms per M tier-0 steps): porous {A * lo:.2f}-{A * hi:.2f} ms per 1 %, solid {B * lo:+.2f}..{B * hi:+.2f} ms per 1 %")
print("\n== memory (forest_eye): CPU pool peak, GPU pool peak, refused, process peak MB; shells stored/missing")
for a in ARMS:
    r = R[(a, S)][P]; m = r["mem"]
    if m: print(f"{a:7} cpu peak {m[0]:9,.0f}  gpu peak {m[1]:9,.0f} refused {m[2]:3.0f}  process peak {m[6]:7.1f} MB  shells {r['shells']}")
# Sensitivity: each PAIR of tree arms solved exactly (2 equations, 2 unknowns), so the spread shows how firm the fit is.
print("\n== sensitivity: each pair of tree arms solved exactly (tier-0 steps per 1 % of screen)")
import itertools
for (l1, w1, t1, a1), (l2, w2, t2, a2) in itertools.combinations(rows, 2):
    dd = l1 * w2 - l2 * w1
    a_ = (t1 * w2 - t2 * w1) / dd; b_ = (l1 * t2 - l2 * t1) / dd
    print(f"  {a1}+{a2}: porous {a_ * 1e3:+.0f} k, solid {b_ * 1e3:+.0f} k  ->  porous {a_ * 5.7:.2f}-{a_ * 7.5:.2f} ms per 1 %, solid {b_ * 5.7:+.2f}..{b_ * 7.5:+.2f} ms per 1 %")
