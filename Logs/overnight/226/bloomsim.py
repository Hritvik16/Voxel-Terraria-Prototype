# task 226: offline bloom preview on finished stills (post-FSR 1080p), to pick threshold/strength before a launch.
import sys, numpy as np
from PIL import Image
def s2l(c): return np.where(c<=0.04045, c/12.92, ((c+0.055)/1.055)**2.4)
def l2s(c): c=np.clip(c,0,1); return np.where(c<=0.0031308, c*12.92, 1.055*c**(1/2.4)-0.055)
def gauss(a, sig, axis):
    r=int(3*sig); k=np.exp(-0.5*(np.arange(-r,r+1)/sig)**2); k/=k.sum()
    pad=[(0,0)]*a.ndim; pad[axis]=(r,r); p=np.pad(a,pad,mode='edge')
    out=np.zeros_like(a)
    for i,w in enumerate(k): out+=w*np.take(p, range(i,i+a.shape[axis]), axis=axis)
    return out
def bloom(img, rows=432, t0=0.55, t1=1.0, sig=2.0, k=0.35):
    lin=s2l(img); H,W,_=lin.shape; f=int(round(H/(rows/4)))
    h,w=H//f,W//f; small=lin[:h*f,:w*f].reshape(h,f,w,f,3).mean((1,3))
    l=small@[0.2126,0.7152,0.0722]; x=np.clip((l-t0)/(t1-t0),0,1); wgt=x*x*(3-2*x)
    b=small*wgt[...,None]; b=gauss(gauss(b,sig,0),sig,1)
    up=np.asarray(Image.fromarray((np.clip(b,0,1)*65535).astype(np.uint16)[...,0]).size) # dummy
    bu=np.stack([np.asarray(Image.fromarray(b[...,c].astype(np.float32),'F').resize((W,H),Image.BILINEAR)) for c in range(3)],-1)
    return l2s(lin+k*bu)
src,dst=sys.argv[1],sys.argv[2]; args=[float(v) for v in sys.argv[3:]]
img=np.asarray(Image.open(src).convert('RGB')).astype(float)/255
out=bloom(img,432,*args) if args else bloom(img)
Image.fromarray((np.concatenate([img,out],1)*255+0.5).astype(np.uint8)).resize((1920,540)).save(dst)
