# task 27: when does a chunk-gen worker switch to its slow state (~2.2x thread CPU time)?
# Per launch: non-uniform chunks binned by worker start time (workStartS, s since launch);
# per bin the CPU p50 and the share "slow" (CPU > SLOW ms). Also a content-free speed factor:
# each chunk's CPU / its fastest CPU across the given launches (fast state ~1, slow ~2.2).
# Onset = first bin from which the slow share stays >= 50% for 3 bins in a row.
# Walk position = (onset - walk start) * speed, on the r-m circle (the report's lines).
# usage: python3 tools/streamprobe-onset.py label=RUNID [label=RUNID ...]   [BIN=15 SLOW=100]
import sys, glob, os, re, statistics as st
DATA=os.path.expanduser("~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/StreamProbe")
BIN=float(os.environ.get("BIN",15)); SLOW=float(os.environ.get("SLOW",100))
def load(run):
    d=sorted(glob.glob(f"{DATA}/*_{run}"))[-1]
    rows=[l.rstrip("\n").split("\t") for l in open(d+"/streamprobe_chunks.tsv")]
    ix={k:i for i,k in enumerate(rows[0])}
    rep=open(d+"/streamprobe.txt").read()
    m=re.search(r"walk start ([\d.]+) s since launch, s0 ([\d.-]+) m, ([\d.]+) m/s",rep)
    r=re.search(r"circle r (\d+) m",rep)
    c={}
    for x in rows[1:]:
        if x[ix["uniform"]]!="0" or float(x[ix["genCpuMs"]])<0: continue
        c[(x[0],x[1],x[2])]=(float(x[ix["workStartS"]]),float(x[ix["genCpuMs"]]))
    return c,float(m.group(1)),float(m.group(3)),float(r.group(1)) if r else 150.0
runs=[a.split("=") for a in sys.argv[1:]]
data={lab:load(rid) for lab,rid in runs}
fastest={}
for c,_,_,_ in data.values():
    for k,(t,cpu) in c.items(): fastest[k]=min(fastest.get(k,1e9),cpu)
def pct(v,p): v=sorted(v); return v[min(len(v)-1,int(p*(len(v)-1)+0.5))]
for lab,(c,w0,spd,rad) in data.items():
    bins={}
    for k,(t,cpu) in c.items(): bins.setdefault(int((t-w0)//BIN),[]).append((cpu,cpu/fastest[k]))
    keys=sorted(bins); onset=None
    for i,b in enumerate(keys):
        run=[keys[j] for j in range(i,min(i+3,len(keys)))]
        if len(run)==3 and all(sum(1 for v in bins[q] if v[0]>SLOW)/len(bins[q])>=.5 for q in run): onset=b; break
    print(f"{lab}: walk start {w0:.1f} s after launch, {len(c)} chunks; bins of {BIN:.0f} s from walk start: "
          "t(walk) n CPUp50 slow% factor-p50")
    print("   "+"  ".join(f"{b*BIN:+.0f}:{len(bins[b])}/{pct([v[0] for v in bins[b]],.5):.0f}/{100*sum(1 for v in bins[b] if v[0]>SLOW)/len(bins[b]):.0f}%/{pct([v[1] for v in bins[b]],.5):.2f}" for b in keys))
    if onset is None: print(f"   ONSET: none (slow share never >= 50% for 3 bins)")
    else:
        tw=onset*BIN; s=max(0.0,tw)*spd
        print(f"   ONSET {lab}: {w0+tw:.0f} s since launch = walk t {tw:+.0f} s -> camera at {s:.0f} m along the walk ({s/rad*57.2958:.0f} deg round the r {rad:.0f} m circle)")
common=set.intersection(*[set(c) for c,_,_,_ in data.values()])
print(f"chunks common to all: {len(common)}")
for lab,(c,w0,_,_) in data.items():
    f=[c[k][1]/fastest[k] for k in common]
    print(f"   {lab}: paired speed factor p50 {pct(f,.5):.2f} mean {st.mean(f):.2f}; slow (>1.6x) {100*sum(1 for v in f if v>1.6)/len(f):.0f}%")
