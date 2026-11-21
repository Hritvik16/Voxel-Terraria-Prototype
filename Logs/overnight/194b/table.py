# usage: table.py <binmap.txt> <procs.txt> <compile start HH:MM:SS> -- per-kernel wall (start = previous job's end on that compiler process)
import sys
def sec(t): h, m, s = map(int, t.split(':')); return h * 3600 + m * 60 + s
end = {}; size = {}
for l in open(sys.argv[1]):
    p = l.split()
    if len(p) >= 3: end[p[2]] = sec(p[0]); size[p[2]] = int(p[1])
t0 = sec(sys.argv[3]); rows = []
for l in open(sys.argv[2]):
    proc, ks = l.split(':'); prev = t0
    for k in ks.split():
        e = end.get(k)
        if e is None: rows.append((k, proc, None, None, None)); continue
        rows.append((k, proc, e - prev, e - t0, size[k])); prev = e
rows.sort(key=lambda r: -(r[2] or 0))
print(f"{'kernel':24} {'process':7} {'compile s':>9} {'done at +s':>10} {'binary KB':>9}")
for k, p, w, d, s in rows:
    print(f"{k:24} {p:7} {w if w is not None else '?':>9} {d if d is not None else '?':>10} {s // 1024 if s else '?':>9}")
print(f"sum of kernel compile s: {sum(r[2] or 0 for r in rows)}   last kernel done at +{max(r[3] or 0 for r in rows)} s")
