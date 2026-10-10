import sys; sys.path.insert(0,'.')
from biomefield import *

def comp_sizes(mask_land,b):
    n=b.shape[0]; N=n*n
    par=np.arange(N)
    def find(x):
        r=x
        while par[r]!=r: r=par[r]
        while par[x]!=r: par[x],x=r,par[x]
        return r
    L=mask_land.ravel(); B=b.ravel()
    idx=np.arange(N).reshape(n,n)
    eh=(mask_land[:,:-1]&mask_land[:,1:]&(b[:,:-1]==b[:,1:]))
    ev=(mask_land[:-1,:]&mask_land[1:,:]&(b[:-1,:]==b[1:,:]))
    A=np.concatenate([idx[:,:-1][eh],idx[:-1,:][ev]]); C=np.concatenate([idx[:,1:][eh],idx[1:,:][ev]])
    par=list(range(N))
    for a,c in zip(A.tolist(),C.tolist()):
        ra=a
        while par[ra]!=ra: ra=par[ra]
        rc=c
        while par[rc]!=rc: rc=par[rc]
        if ra!=rc: par[ra]=rc
        # path compress
        while par[a]!=ra if False else False: pass
        par[a]=rc if ra!=rc else ra; par[c]=rc
    roots=[0]*N
    land_idx=np.nonzero(L)[0].tolist()
    from collections import Counter
    cnt=Counter()
    for i in land_idx:
        r=i
        while par[r]!=r: r=par[r]
        par[i]=r
        cnt[r]+=1
    return list(cnt.values())
P='../../TerrainGen/playmetrics/'
NAMES=['forest','desert','snow','jungle','savanna','badlands']
MAPS={}
def stats(seed,scale,centres,oct=3,gain=0.5):
    if seed not in MAPS:
        n,h,reg,wc,bio=loadmap(P+f'seed{seed}_v4_map.bin'); MAPS[seed]=h>0
    land=MAPS[seed]
    t,m=climate(seed,scale,oct=oct,gain=gain)
    b=nearest(t,m,centres)
    rows=np.arange(0,1024,4)
    L=land[rows]; B=b[rows]
    both=L[:,:-1]&L[:,1:]
    walked=both.sum(); ch=(both&(B[:,:-1]!=B[:,1:])).sum()
    changeM=walked*8/ch if ch else 1e9
    sizes=[]
    sizes=[c*0.0064 for c in comp_sizes(land,b)]
    sizes=sorted(sizes); tot=sum(sizes); acc=0; awm=0
    for a in sizes:
        acc+=a
        if acc>=tot*0.5: awm=a; break
    shares=[float((b[land]==k).mean()) for k in range(len(centres))]
    return changeM,awm,shares,b
if __name__=='__main__':
    C4=[(0,0),(0.8,-0.8),(-0.9,0.1),(0.8,0.8)]
    for seed in (42,7,2026,31337):
        c,a,s,_=stats(seed,3.0,C4); print(seed,round(c),round(a),[round(x,2) for x in s])
