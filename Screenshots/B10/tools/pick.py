import sys; sys.path.insert(0,'.')
from explore import *
C=[(0,0),(0.8,-0.8),(-0.9,0.1),(0.8,0.8),(0.8,0.0),(-0.3,-0.8)]
import explore
def b6(seed):
    t,m=climate(seed,3.25,oct=1); f=f32(1.6/2.4); return nearest(t*f,m*f,C)
if __name__=='__main__':
  for seed in (42,7,2026,31337):
    n,h,reg,wc,bio=loadmap(P+f'seed{seed}_v4_map.bin'); land=h>0
    b=b6(seed)
    L=land[::4];B=b[::4];both=L[:,:-1]&L[:,1:]; ch=(both&(B[:,:-1]!=B[:,1:])).sum(); c=both.sum()*8/ch
    sizes=sorted(x*0.0064 for x in comp_sizes(land,b)); tot=sum(sizes); acc=0
    for a in sizes:
        acc+=a
        if acc>=tot/2: break
    s=np.bincount(b[land],minlength=6)/land.sum()
    big=[max(comp_sizes(land&(b==k),b))*0.0064 if (land&(b==k)).any() else 0 for k in (4,5)]
    np.save(f'pick_{seed}.npy',b)
    print(f"{seed}: change {c:.0f} m, area-wtd median {a:.0f} ha, "+" ".join(f"{NAMES[k]} {s[k]*100:.0f}%" for k in range(6))+f"; largest savanna {big[0]:.0f} ha, badlands {big[1]:.0f} ha")
