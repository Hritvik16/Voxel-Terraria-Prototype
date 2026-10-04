# task 20: per-launch gen stats + chunk-paired comparison across launches.
import sys, glob, os, statistics as st
DATA=os.path.expanduser("~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/StreamProbe")
def load(run):
    d=sorted(glob.glob(f"{DATA}/*_{run}"))[-1]
    rows=[l.rstrip("\n").split("\t") for l in open(d+"/streamprobe_chunks.tsv")]
    h=rows[0]; ix={k:i for i,k in enumerate(h)}
    m={}
    for r in rows[1:]:
        if r[ix["uniform"]]!="0": continue
        m[(r[0],r[1],r[2])]=(float(r[ix["genMs"]]),float(r[ix["downMs"]]))
    return m
def pct(v,p): v=sorted(v); return v[min(len(v)-1,int(p*(len(v)-1)+0.5))]
runs=[a.split("=") for a in sys.argv[1:]]   # label=RUNID
data={lab:load(r) for lab,r in runs}
for lab,m in data.items():
    g=[a+b for a,b in m.values()]
    print(f"{lab:10s} nonuniform n {len(g):5d} gen+down p50 {pct(g,.5):6.1f} p99 {pct(g,.99):7.1f}")
common=set.intersection(*[set(m) for m in data.values()])
print(f"chunks common to all {len(runs)} launches: {len(common)}")
for lab,m in data.items():
    g=[m[k][0] for k in common]
    print(f"{lab:10s} paired gen p50 {pct(g,.5):6.1f}  mean {st.mean(g):6.1f}" if g else lab)
