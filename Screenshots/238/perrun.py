# per-run input check: runs = N increments in idlewatch (N counts walk_near's line too: run index 1 = walk_near, k>1 = forest run k-1)
import sys, time
rows=[l.split() for l in open(sys.argv[1])]
rows=[r for r in rows if len(r)>=5 and r[1].isdigit()]
want=sys.argv[2]
rows=[r for r in rows if r[4]==want]
inputs=[]; prev=None
for r in rows:
    t=int(r[0])-int(r[1])
    if prev is None: prev=t; continue
    if abs(t-prev)>3: inputs.append(t)
    prev=t
trans=[]; lastN=0
for r in rows:
    n=int(r[3])
    if n>lastN: trans.append((n,int(r[0]))); lastN=n
start=int(rows[0][0])
for n,t in trans:
    s=start; start=t
    a=[i for i in inputs if s<i<=t]; b=[i for i in a if t-i<=35]
    lab="walk_near" if n==1 else f"forest run {n-1}"
    print(f"{lab:14s} {time.strftime('%H:%M:%S',time.localtime(s))}-{time.strftime('%H:%M:%S',time.localtime(t))} inputs {len(a):2d}  in last 35 s {len(b)}")
open_in=[i for i in inputs if i>start]
print("after last line:", len(open_in), "inputs", [time.strftime('%H:%M:%S',time.localtime(i)) for i in open_in][-3:])
