import sys
from PIL import Image
# The sea/sky line's step: the largest change of mean green between rows 3 apart, in the band
# rows r0..r1 around the far band's square, averaged over columns 40-200 (left) and 1720-1880 (right).
def g(im,y,x0,x1): return sum(im.getpixel((x,y))[1] for x in range(x0,x1,8))/len(range(x0,x1,8))
def step(f,r0,r1):
    im=Image.open(f).convert('RGB'); out=[]
    for x0,x1 in ((40,200),(1720,1880)):
        best=max(((abs(g(im,y-1,x0,x1)-g(im,y+2,x0,x1)),y) for y in range(r0,r1)))
        out.append(best)
    return out
print("pose_heading                 old: step(green levels)@row L / R       new: L / R")
for n,r0,r1 in [('edge_x_edge_px',560,640),('edge_x_edge_pz',560,640),('high_edge_px',560,640),('high_edge_nx',560,640),
                ('high_edge_pz',560,640),('high_edge_golden_away',555,640),('high_edge_golden_toward',555,640),('edge_x_edge_golden_away',555,640)]:
    o=step(n+'_old.png',r0,r1); w=step(n+'.png',r0,r1)
    print(f"{n:28s} " + "  ".join(f"{s:5.1f}@{y}" for s,y in o) + "     " + "  ".join(f"{s:5.1f}@{y}" for s,y in w))
