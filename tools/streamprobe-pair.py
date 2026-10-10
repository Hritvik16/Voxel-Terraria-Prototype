# task 20: per-launch gen stats + chunk-paired comparison across launches.
# task 25: when the TSV has genCpuMs (worker thread CPU time) and gcN, also
# pairs CPU time, wall - CPU (time not running) and wall/CPU, and counts
# chunks whose work spanned a GC collection.
# usage: python3 tools/streamprobe-pair.py label=RUNID [label=RUNID ...]
import sys, glob, os, statistics as st
DATA=os.path.expanduser("~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/StreamProbe")
def load(run):
    d=sorted(glob.glob(f"{DATA}/*_{run}"))[-1]
    rows=[l.rstrip("\n").split("\t") for l in open(d+"/streamprobe_chunks.tsv")]
    h=rows[0]; ix={k:i for i,k in enumerate(h)}
    cpu="genCpuMs" in ix
    m={}
    for r in rows[1:]:
        if r[ix["uniform"]]!="0": continue
        m[(r[0],r[1],r[2])]=(float(r[ix["genMs"]]),float(r[ix["downMs"]]),
            float(r[ix["genCpuMs"]]) if cpu else None, int(r[ix["gcN"]]) if cpu else None)
    return m
def pct(v,p): v=sorted(v); return v[min(len(v)-1,int(p*(len(v)-1)+0.5))]
runs=[a.split("=") for a in sys.argv[1:]]   # label=RUNID
data={lab:load(r) for lab,r in runs}
for lab,m in data.items():
    g=[v[0]+v[1] for v in m.values()]
    print(f"{lab:10s} nonuniform n {len(g):5d} gen+down p50 {pct(g,.5):6.1f} p99 {pct(g,.99):7.1f}")
common=set.intersection(*[set(m) for m in data.values()])
print(f"chunks common to all {len(runs)} launches: {len(common)}")
for lab,m in data.items():
    g=[m[k][0] for k in common]
    line=f"{lab:10s} paired gen p50 {pct(g,.5):6.1f}  mean {st.mean(g):6.1f}" if g else lab
    c=[m[k][2] for k in common if m[k][2] is not None and m[k][2]>=0]
    if g and c and len(c)==len(g):
        w=[m[k][0]-m[k][2] for k in common]; r=[m[k][0]/m[k][2] for k in common if m[k][2]>0]
        gc=sum(1 for k in common if m[k][3]>0)
        line+=(f" | CPU p50 {pct(c,.5):6.1f} mean {st.mean(c):6.1f} | wall-CPU p50 {pct(w,.5):6.1f} mean {st.mean(w):6.1f}"
               f" | wall/CPU p50 {pct(r,.5):4.2f} | CPU share of wall {sum(c)/sum(g)*100:3.0f}% | gc-spanning {gc}")
    print(line)
