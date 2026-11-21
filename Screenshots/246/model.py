# Task 246: derived numbers for WORLD_FILL_BUDGET.md sec. 1 (counts only). Run: python3 -I model.py
import os, sys; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from parse import R, ARMS, SUNS, BANDS
PX = 331776
for s in SUNS:
    print(f"\n== forest_eye sun {s}: per frame totals; t0 = steps taken in the tier-0 window (0.1 m bricks: where leaves are marched)")
    print(f"{'arm':7} {'prim M':>7} {'t0 M':>6} {'sun M':>6} {'t0 M':>6} {'sun/ray':>7} {'sky M':>6} {'all M':>6} {'all t0 M':>8} {'leaf px%':>8} {'far px%':>7} | p95 prim 10-40 / sun 10-40 / sky all")
    base = None
    for a in ARMS:
        r = R[(a, s)]["forest_eye"]["rs"]
        P, S, K = r[("primary","all")], r[("sun","all")], r[("sky","all")]
        t0 = lambda x: x["steps"] * x["t0"] / 100 / 1e6
        leaf = r[("primary","all")]["rays"] * r[("primary","all")]["leaf"] / 100 / PX * 100
        far = r[("primary","82 m+")]["px"]
        tot = (P["steps"] + S["steps"] + K["steps"]) / 1e6; tt0 = t0(P) + t0(S) + t0(K)
        print(f"{a:7} {P['steps']/1e6:7.2f} {t0(P):6.2f} {S['steps']/1e6:6.2f} {t0(S):6.2f} {S['avg']:7.1f} {K['steps']/1e6:6.2f} {tot:6.2f} {tt0:8.2f} {leaf:7.1f}% {far:6.1f}% | {r[('primary','10-40 m')]['p95']} / {r[('sun','10-40 m')]['p95']} / {K['p95']}")
        if a == "off": base = (P["steps"], S["steps"], K["steps"], tt0)
        else:
            dl = leaf
            print(f"        vs OFF: primary {100*(P['steps']/base[0]-1):+.0f} %, sun {100*(S['steps']/base[1]-1):+.0f} %, sky {100*(K['steps']/base[2]-1):+.0f} %, tier-0 steps {100*(tt0/base[3]-1):+.0f} %;"
                  f" light (sun+sky) steps per 1 % of screen in leaves: {((S['steps']+K['steps'])-(base[1]+base[2]))/1e3/dl:+.1f} k/frame;"
                  f" tier-0 steps per 1 %: {(tt0-base[3])*1e3/dl:+.0f} k/frame")
