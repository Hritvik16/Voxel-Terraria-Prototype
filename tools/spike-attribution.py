#!/usr/bin/env python3
"""Task 115: exclusive attribution of -spikelog spikes (> 50 ms and > local median + 15 ms), first match wins:
GC -> main-thread streaming (evicted > 0 or streamer + shells >= 8 ms) -> fluid tick issued -> next frame short
(pacing) -> unexplained wait. Also p50/p95/p99 and > 33 ms with every spike set to its local median.
  python3 -I tools/spike-attribution.py <spikes.csv> [...]"""
import csv, statistics as st, sys
for path in sys.argv[1:]:
    rows=list(csv.DictReader(open(path)))
    end=len(rows)
    while end>2 and (rows[end-1]['cam_x'],rows[end-1]['cam_z'])==(rows[end-2]['cam_x'],rows[end-2]['cam_z']): end-=1
    rows=rows[:end-1]
    f=lambda k:[float(r[k]) for r in rows]
    fm=f('frame_ms'); n=len(fm); upd=f('upd_ms'); stm=f('streamer_ms'); shm=f('shells_ms'); fl=f('fluid_ticks'); sh=f('shell_gen_total'); shb=f('shell_bytes'); g=[a+b+c for a,b,c in zip(f('gc0'),f('gc1'),f('gc2'))]; ev=f('evicted'); pre=f('pre_ms')
    loc=[st.median([fm[j] for j in range(max(0,i-15),min(n,i+16)) if j!=i]) for i in range(n)]
    idx=range(30,n)
    spikes=[i for i in idx if fm[i]>50 and fm[i]-loc[i]>15]
    cats={}
    for i in spikes:
        own=stm[i]+shm[i]
        if g[i]-g[i-1]>0: c='GC'
        elif ev[i]>0 or own>8: c='main-thread streaming (streamer/evict/shells >= 8 ms)'
        elif fl[i]-fl[i-1]>0: c='fluid tick issued (pacing: next frame short)'
        elif fm[i+1 if i+1<n else i]<loc[i]-8: c='other, next frame short (pacing)'
        else: c='unexplained wait (preUpdate, little own work)'
        x=cats.setdefault(c,[0,0.0]); x[0]+=1; x[1]+=fm[i]-loc[i]
    tot=sum(fm[i] for i in idx)
    print(path, "spikes", len(spikes))
    for c,(k,e) in sorted(cats.items(), key=lambda kv:-kv[1][0]):
        print(f"  {c:<55}{k:5d} {100*k/len(spikes):5.1f}%   excess {e/1000:5.2f} s ({100*e/tot:4.1f}% of wall)")
    cf=[loc[i] if i in set(spikes) else fm[i] for i in idx]; a=sorted(fm[i] for i in idx); b=sorted(cf); m=len(a)
    print(f"  spikes -> local median: p50 {a[m//2]:.1f}->{b[m//2]:.1f}  p95 {a[int(m*.95)]:.1f}->{b[int(m*.95)]:.1f}  p99 {a[int(m*.99)]:.1f}->{b[int(m*.99)]:.1f}  >33 {100*sum(x>33 for x in a)/m:.1f}->{100*sum(x>33 for x in b)/m:.1f}%  >50 {100*sum(x>50 for x in a)/m:.1f}->{100*sum(x>50 for x in b)/m:.1f}%")
    big=[i for i in idx if fm[i]>100]
    print(f"  >100 ms frames {len(big)}: mean pre {sum(pre[i] for i in big)/len(big):.0f} ms, own streamer+shells {sum(stm[i]+shm[i] for i in big)/len(big):.1f} ms, shell bytes>0 {sum(shb[i]>0 for i in big)}, evict {sum(ev[i]>0 for i in big)}")
