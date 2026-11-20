# Task 248 (copy of 246's parse.py + the primary-hit material table): parse the [raystats] tables + [mem] lines of the 8 cost-split reports -> rows (counts only, no time).
import re, sys, os, json
D = os.path.dirname(os.path.abspath(__file__))
ARMS = ["off", "today", "sparse", "dense"]; SUNS = ["16"]; POSES = ["walk_near", "forest_eye", "walk_open"]
def num(s): return float(s.replace(",", "").rstrip("%")) if s not in ("-",) else None
def parse(path):
    out = {}; pose = None; mode = None
    for ln in open(path, errors="replace"):
        m = re.match(r"== (\w+):", ln)
        if m: pose = m.group(1); out.setdefault(pose, {"rs": {}, "mat": {}, "mem": None, "shells": None, "sunfull": None}); continue
        if pose is None: continue
        if "[raystats]" in ln: mode = "rs"; continue
        if "primary hit material" in ln: mode = "mat"; continue
        if mode == "mat":
            m = re.match(r"\s+(0-10 m|10-40 m|40-82 m|82 m\+|all)" + r"\s+([\d.]+)%" * 9, ln)
            if m:
                g = m.groups(); out[pose]["mat"][g[0]] = dict(zip(["screen", "leaf", "wood", "ground", "other", "none", "porous", "solid", "solidng"], map(float, g[1:])))
                continue
            if ln.strip().startswith("(") or ln.strip().startswith("band"): continue
            mode = None
        if mode == "rs":
            m = re.match(r"\s+(primary|sun|sky)\s+(0-10 m|10-40 m|40-82 m|82 m\+|all)\s+([\d,]+)\s+([\d.]+)%\s+([\d,]+)\s+([\d.]+)\s+(\S+)\s+(\S+)\s+(\d+)%\s+(\d+)%\s+(\d+)%", ln)
            if m:
                g = m.groups(); out[pose]["rs"][(g[0], g[1])] = dict(rays=num(g[2]), px=num(g[3]), steps=num(g[4]), avg=num(g[5]), p50=g[6], p95=g[7], t0=num(g[8]), blk=num(g[9]), leaf=num(g[10]))
                continue
            if ln.strip().startswith("(") or ln.strip().startswith("kind"): continue
            mode = None
        if "[mem] STEP 14" in ln:
            m = re.search(r"CPU brick pool cap [\d,]+ peak ([\d,]+).*GPU body pool cap [\d,]+ peak ([\d,]+) refused (\d+).*in use ([\d,]+), sealed ([\d,]+).*process now ([\d.]+) MB, peak ([\d.]+) MB", ln)
            if m: out[pose]["mem"] = [num(x) for x in m.groups()]
        if "[shells] fill wait" in ln:
            m = re.search(r"total stored (\d+), missing (\d+)", ln); out[pose]["shells"] = (int(m.group(1)), int(m.group(2))) if m else None
        if "[sun full]" in ln:
            m = re.search(r"traced/frame ([\d,]+), steps/frame ([\d,]+)", ln); out[pose]["sunfull"] = (num(m.group(1)), num(m.group(2))) if m else None
    return out
R = {}
for a in ARMS:
    for s in SUNS:
        p = os.path.join(D, f"costsplit_{a}_{s}.txt")
        if os.path.exists(p): R[(a, s)] = parse(p)
BANDS = ["0-10 m", "10-40 m", "40-82 m", "82 m+", "all"]
if __name__ == "__main__":
    for s in SUNS:
        for pose in POSES[1:]:
            print(f"\n== {pose}  sun {s}  (per frame, 432 render rows, 331,776 primary px)")
            print(f"{'arm':7} {'band':8} {'prim steps':>11} {'/ray':>6} {'sun rays':>9} {'sun steps':>10} {'/ray':>6} {'sky steps':>10} {'leaf px%':>8} {'%px':>6} {'tot steps/px':>12}")
            for a in ARMS:
                r = R.get((a, s), {}).get(pose)
                if not r: print(f"{a:7} (missing)"); continue
                for b in BANDS:
                    P = r["rs"].get(("primary", b)); S = r["rs"].get(("sun", b)); K = r["rs"].get(("sky", b))
                    if not P: continue
                    leafpx = P["rays"] * P["leaf"] / 100 / 331776 * 100
                    tot = (P["steps"] + S["steps"] + K["steps"]) / 331776
                    print(f"{a:7} {b:8} {P['steps']/1e6:10.2f}M {P['avg']:6.1f} {S['rays']/1e3:8.1f}k {S['steps']/1e6:9.2f}M {S['avg']:6.1f} {K['steps']/1e6:9.2f}M {leafpx:7.1f}% {P['px']:5.1f}% {tot:12.1f}")
        print()
    print("== memory (forest_eye line; walk_open line): CPU pool peak, GPU pool peak, refused, process peak MB; shells stored/missing")
    for s in SUNS:
        for a in ARMS:
            r = R.get((a, s))
            if not r: continue
            for pose in POSES:
                m = r[pose]["mem"]; sh = r[pose]["shells"]
                if m: print(f"{a:7} {s:5} {pose:10} cpu peak {m[0]:9,.0f}  gpu peak {m[1]:9,.0f} refused {m[2]:3.0f} in use {m[3]:9,.0f} sealed {m[4]:8,.0f}  process now {m[5]:7.1f} peak {m[6]:7.1f} MB  shells {sh}")
