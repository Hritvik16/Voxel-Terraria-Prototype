#!/usr/bin/env python3
"""Task 210: per-pose GPU / memory table, -trees2 1 (t2) vs -trees2 0 (t0), from Screenshots/210/lookreview_<arm>.txt.
   python3 -I Logs/overnight/210_table.py > Screenshots/210/table210.txt   (run from the repo root)"""
import re, sys

def parse(arm):
    rows, cur = [], {}
    for ln in open(f"Screenshots/210/lookreview_{arm}.txt", errors="replace"):
        m = re.search(r"\[mem\].*CPU brick pool cap [\d,]+ peak ([\d,]+).*GPU body pool cap [\d,]+ peak ([\d,]+) refused ([\d,]+).*in use ([\d,]+), sealed ([\d,]+).*peak ([\d.]+) MB", ln)
        if m: cur.update(cpuPeak=m[1], gpuPeak=m[2], refTot=m[3], inUse=m[4], sealed=m[5], memPeak=m[6])
        m = re.search(r"\[gpupool\] src (\w+)\s+([\d,]+) .*refused ([\d,]+)", ln)
        if m: cur[m[1]] = m[2]; cur[m[1] + "Ref"] = m[3]
        m = re.search(r"\[gpupool\] duplicates: .*\(dupes ([\d,]+)", ln)
        if m: cur["dupes"] = m[1]
        m = re.search(r"\[refused\] now ([\d,]+), total", ln)
        if m: cur["refNow"] = m[1]
        m = re.search(r"\[shells\].*missing ([\d,]+), pending.*\(([\d]+) ms each", ln)
        if m: cur["shMiss"] = m[1]; cur["shMs"] = m[2]
        m = re.search(r"WSPOSE fixed_(\S+?)\|", ln)
        if m: cur["pose"] = m[1]; rows.append(cur); cur = {}
    return rows

def n(s): return int(str(s).replace(",", "")) if s not in (None, "") else 0

A, B = (sys.argv[1:3] if len(sys.argv) > 2 else ["t2", "t0"])
a, b = parse(A), parse(B)
print("Task 210: per pose, -trees2 1 (new world trees) vs -trees2 0 (today's trees); seed 42, noon, one build, lookwait 22 s.")
print("GPU body pool cap 254,400. 'tree' / 'terrain' = mapped bodies by source; 'ref now' = refused chunks at the shot; 'ref total' = launch total so far.")
print(f"{'pose':<15} {'arm':<3} {'in use':>8} {'tree':>8} {'terrain':>8} {'dupes':>7} {'ref now':>7} {'ref total':>9} {'mem peak':>9} {'CPU peak':>8} {'sh miss':>7} {'sh ms':>5}")
pb = {r["pose"]: r for r in b}
for r in a:
    for arm, x in ((A[-2:], r), (B[-2:], pb.get(r["pose"], {}))):
        print(f"{r['pose']:<15} {arm:<3} {n(x.get('inUse')):>8,} {n(x.get('tree')):>8,} {n(x.get('terrain')):>8,} {n(x.get('dupes')):>7,} {n(x.get('refNow')):>7,} "
              f"{n(x.get('refTot')):>9,} {x.get('memPeak', '-'):>9} {n(x.get('cpuPeak')):>8,} {n(x.get('shMiss')):>7,} {x.get('shMs', '-'):>5}")
