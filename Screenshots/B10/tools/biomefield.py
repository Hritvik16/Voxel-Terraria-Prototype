# Offline port of ShapeField.Biome (B10 exploration). float32 numpy.
import numpy as np, sys
f32=np.float32
def offsets(seed):
    s=(seed*747796405+2891336453)&0xFFFFFFFF
    out=[]
    for _ in range(22):
        s^=(s<<13)&0xFFFFFFFF; s^=s>>17; s^=(s<<5)&0xFFFFFFFF
        out.append(f32(f32((s&0xFFFFF))/f32(0xFFFFF))*f32(2000)-f32(1000))
    return (out[18],out[19]),(out[20],out[21])
def mod289(x): return x-np.floor(x*f32(1/289.0))*f32(289)
def permute(x): return mod289((f32(34)*x+f32(1))*x)
def snoise(vx,vy):
    Cx,Cy,Cz,Cw=f32(0.211324865405187),f32(0.366025403784439),f32(-0.577350269189626),f32(0.024390243902439)
    d=(vx+vy)*Cy
    ix=np.floor(vx+d); iy=np.floor(vy+d)
    d2=(ix+iy)*Cx
    x0x=vx-ix+d2; x0y=vy-iy+d2
    i1x=np.where(x0x>x0y,f32(1),f32(0)); i1y=f32(1)-i1x
    x12x=x0x+Cx-i1x; x12y=x0y+Cx-i1y; x12z=x0x+Cz; x12w=x0y+Cz
    ix=mod289(ix); iy=mod289(iy)
    ps=[permute(permute(iy+a)+ix+b) for a,b in ((f32(0),f32(0)),(i1y,i1x),(f32(1),f32(1)))]
    ms=[np.maximum(f32(0.5)-(x0x*x0x+x0y*x0y),f32(0)),np.maximum(f32(0.5)-(x12x*x12x+x12y*x12y),f32(0)),np.maximum(f32(0.5)-(x12z*x12z+x12w*x12w),f32(0))]
    gx=[x0x,x12x,x12z]; gy=[x0y,x12y,x12w]
    tot=f32(0)
    for p,m,ax,ay in zip(ps,ms,gx,gy):
        m=m*m; m=m*m
        x=f32(2)*((p*Cw)-np.floor(p*Cw))-f32(1)
        h=np.abs(x)-f32(0.5); ox=np.floor(x+f32(0.5)); a0=x-ox
        m=m*(f32(1.79284291400159)-f32(0.85373472095314)*(a0*a0+h*h))
        tot=tot+m*(a0*ax+h*ay)
    return f32(130)*tot
def fbm(px,py,oct,gain=0.5):
    a=f32(0); amp=f32(0.5); norm=f32(0)
    for _ in range(oct):
        a=a+amp*snoise(px,py); norm+=amp
        px=px*f32(2.03)+f32(17.3); py=py*f32(2.03)+f32(-9.1); amp*=f32(gain)
    return a/norm
def climate(seed,scale=3.0,n=1024,cell=80,oct=3,gain=0.5):
    tA,mA=offsets(seed)
    c=(np.arange(n,dtype=f32)+f32(0.5))*f32(cell)
    X,Z=np.meshgrid(c,c)   # [z,x]
    s=f32(scale)
    t=fbm(X/(f32(15000)*s)+tA[0],Z/(f32(15000)*s)+tA[1],oct,gain)*f32(2.4)
    m=fbm(X/(f32(12000)*s)+mA[0],Z/(f32(12000)*s)+mA[1],oct,gain)*f32(2.4)
    return t,m
def nearest(t,m,centres):
    best=None;b=None
    for k,(ct,cm) in enumerate(centres):
        d=(t-f32(ct))**2+(m-f32(cm))**2
        if best is None: best=d; b=np.zeros(t.shape,np.uint8)
        else:
            sel=d<best; best=np.where(sel,d,best); b=np.where(sel,k,b).astype(np.uint8)
    return b
def loadmap(path):
    with open(path,'rb') as f:
        n=np.frombuffer(f.read(4),np.int32)[0]
        h=np.frombuffer(f.read(4*n*n),np.float32).reshape(n,n)
        reg=np.frombuffer(f.read(n*n),np.uint8).reshape(n,n)
        wc=np.frombuffer(f.read(n*n),np.uint8).reshape(n,n)
        bio=np.frombuffer(f.read(n*n),np.uint8).reshape(n,n)
    return n,h,reg,wc,bio
