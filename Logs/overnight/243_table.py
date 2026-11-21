#!/usr/bin/env python3
"""Task 243: per-pose GPU / memory table, crown cores OFF vs -crowncore 2, from Screenshots/243/lookreview_<arm>.txt.
   python3 -I Logs/overnight/243_table.py off_noon c2_noon c2_16 off_16 > Screenshots/243/table243.txt   (run from the repo root)"""
import re, sys

def parse(arm):
    rows, cur = [], {}
    for ln in open(f"Screenshots/243/lookreview_{arm}.txt", errors="replace"):
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

arms = sys.argv[1:] or ["off_noon", "c2_noon", "c2_16", "off_16"]
names = {"off_noon": "OFF noon", "c2_noon": "-crowncore 2 noon", "c2_16": "-crowncore 2 16 deg", "off_16": "OFF 16 deg"}
data = {a: {r["pose"]: r for r in parse(a)} for a in arms}
print("Task 243: per pose, " + " vs ".join(names.get(a, a) for a in arms) + "; seed 42, one build, lookwait 30 s, arms run A B B A.")
print("GPU body pool cap 254,400. 'tree' / 'terrain' = mapped bodies by source; 'ref now' = refused chunks at the shot; 'ref total' = launch total so far; peaks are launch peaks so far.")
print(f"{'pose':<15} {'arm':<9} {'in use':>8} {'tree':>8} {'terrain':>8} {'dupes':>7} {'ref now':>7} {'ref total':>9} {'gpu peak':>8} {'mem peak':>9} {'CPU peak':>8} {'sh miss':>7} {'sh ms':>5}")
for pose in data[arms[0]]:
    for arm in arms:
        x = data[arm].get(pose, {})
        print(f"{pose:<15} {arm:<9} {n(x.get('inUse')):>8,} {n(x.get('tree')):>8,} {n(x.get('terrain')):>8,} {n(x.get('dupes')):>7,} {n(x.get('refNow')):>7,} "
              f"{n(x.get('refTot')):>9,} {n(x.get('gpuPeak')):>8,} {x.get('memPeak', '-'):>9} {n(x.get('cpuPeak')):>8,} {n(x.get('shMiss')):>7,} {x.get('shMs', '-'):>5}")
