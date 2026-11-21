# Task 250 (WORLD_FILL_BUDGET sec. 5 E3): voxel grass fringe off / on, counts only (rule 10).
# Reads Logs/overnight/250/costsplit_<off|on>_<16|noon>.txt (Screenshots/250/run.sh). Run: python3 -I Screenshots/250/fringe.py
import re, os
O = "Logs/overnight/250"
POSES = ["walk_near", "forest_eye", "savanna_eye", "savanna_close"]
LEAF_RATE = 0.305   # M tier-0 steps per 1 % of screen, porous leaves (task 248 fit, 16 deg; sec. 1b.2)
MS_PER_M = (5.7, 7.5)  # sec. 1b.4 calibration (est.)

def parse(path):
    out = {}
    if not os.path.exists(path): return out
    cur = None
    for line in open(path):
        m = re.match(r"== (\S+): still pose", line) or re.match(r"== (\S+):", line)
        if m: cur = m.group(1); out.setdefault(cur, {"rs": {}, "mat": {}, "mem": None}); continue
        if cur is None: continue
        r = out[cur]
        m = re.match(r"\s+(primary|sun|sky)\s+(0-10 m|10-40 m|40-82 m|82 m\+|all)\s+([\d,]+)\s+([\d.]+)%\s+([\d,]+)\s+([\d.]+)\s+\S+\s+\S+\s+(\d+)%", line)
        if m:
            r["rs"][(m.group(1), m.group(2))] = {"rays": int(m.group(3).replace(",", "")), "steps": int(m.group(5).replace(",", "")), "t0": int(m.group(7))}
            continue
        m = re.match(r"\s+(0-10 m|10-40 m|40-82 m|82 m\+|all)\s+((?:[\d.]+%\s*){10})$", line)
        if m:
            v = [float(x) for x in re.findall(r"([\d.]+)%", m.group(2))]
            r["mat"][m.group(1)] = dict(zip(["screen", "leaf", "wood", "ground", "other", "none", "porous", "solid", "solidng", "fringe"], v))
            continue
        m = re.search(r"CPU brick pool cap [\d,]+ peak ([\d,]+).*GPU body pool cap [\d,]+ peak ([\d,]+) refused (\d+).*in use ([\d,]+), sealed ([\d,]+).*peak ([\d.]+) MB", line)
        if m: r["mem"] = [float(x.replace(",", "")) for x in m.groups()]
    return out

def t0(r, kinds=("primary", "sun", "sky")):
    return sum(r["rs"][(k, "all")]["steps"] * r["rs"][(k, "all")]["t0"] / 100 for k in kinds if (k, "all") in r["rs"]) / 1e6

def valve(sun, arm):
    p = f"{O}/player_{arm}_{sun}.log"
    return sum(1 for l in open(p) if "[valve]" in l) if os.path.exists(p) else -1

def fringe_line(sun, arm):
    p = f"{O}/player_{arm}_{sun}.log"
    if not os.path.exists(p): return "-"
    ls = [l.strip() for l in open(p) if "[fringe]" in l]
    return ls[-1] if ls else "(no [fringe] line)"

R = {(a, s): parse(f"{O}/costsplit_{a}_{s}.txt") for a in ("off", "on") for s in ("16", "noon")}
for s in ("16", "noon"):
    print(f"\n==== sun {s} deg" if s == "16" else "\n==== noon")
    print(f"{'pose':14} {'arm':4} {'t0 all M':>9} {'t0 prim M':>9} {'t0 sun M':>8} {'t0 sky M':>8} {'fringe %':>9} {'ground %':>9} {'leaf %':>7} {'none %':>7} {'prim steps M':>12}")
    for p in POSES:
        for a in ("off", "on"):
            r = R[(a, s)].get(p)
            if not r or not r["rs"]: print(f"{p:14} {a:4} (missing)"); continue
            m = r["mat"].get("all", {})
            print(f"{p:14} {a:4} {t0(r):9.2f} {t0(r, ('primary',)):9.2f} {t0(r, ('sun',)):8.2f} {t0(r, ('sky',)):8.2f} {m.get('fringe', 0):8.1f}% {m.get('ground', 0):8.1f}% {m.get('leaf', 0):6.1f}% {m.get('none', 0):6.1f}% {r['rs'][('primary', 'all')]['steps'] / 1e6:12.2f}")
        ro, rn = R[("off", s)].get(p), R[("on", s)].get(p)
        if ro and rn and ro["rs"] and rn["rs"]:
            f = rn["mat"].get("all", {}).get("fringe", 0); d = t0(rn) - t0(ro); dp = t0(rn, ("primary",)) - t0(ro, ("primary",))
            rate = d / f if f > 0 else float("nan")
            print(f"{'':14} d    {d:+9.2f} {dp:+9.2f}   fringe {f:.1f}% of screen -> {rate * 1e3:+.0f} k tier-0 steps per 1 % "
                  f"(leaf {LEAF_RATE * 1e3:.0f} k; ratio {rate / LEAF_RATE:+.2f}); est. {rate * MS_PER_M[0]:+.2f}..{rate * MS_PER_M[1]:+.2f} ms per 1 %")
            for b in ("0-10 m", "10-40 m", "40-82 m", "82 m+"):
                mb = rn["mat"].get(b)
                if mb: print(f"{'':14}      band {b:8} screen {mb['screen']:5.1f}%  fringe {mb['fringe']:5.1f}%  ground {mb['ground']:5.1f}% (off {ro['mat'].get(b, {}).get('ground', 0):5.1f}%)")
print("\n==== memory per pose ([mem] line after each pose; peaks are cumulative over the launch's poses)")
print(f"{'sun':5} {'arm':4} {'pose':14} {'CPU peak':>9} {'GPU peak':>9} {'refused':>7} {'GPU in use':>10} {'sealed':>8} {'proc peak MB':>12}")
for s in ("16", "noon"):
    for a in ("off", "on"):
        for p in POSES:
            r = R[(a, s)].get(p)
            if r and r["mem"]:
                m = r["mem"]; print(f"{s:5} {a:4} {p:14} {m[0]:9,.0f} {m[1]:9,.0f} {m[2]:7.0f} {m[3]:10,.0f} {m[4]:8,.0f} {m[5]:12.1f}")
        print(f"{s:5} {a:4} [valve] in-view evictions logged: {valve(s, a)};  {fringe_line(s, a)[:200]}")
