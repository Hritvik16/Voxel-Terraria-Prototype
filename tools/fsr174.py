#!/usr/bin/env python3
"""Task 174: FSR 3 with OUR reactive mask (-fsr3reactive 1) -- today / sc10 / sc10 + mask, bursts vs native.

python3 -I tools/fsr174.py <LookReview dir> <out dir>

Shots are <scene>_<arm>_<k>.png (PlaygroundCapture.FsrLive.cs, -fsrlivescenes task174). Every scene is stepped per
frame, so native frame k is the truth for arm frame k (the _f scenes, flood ON, only nearly: the light pool rebuilds
asynchronously).
  err   mean |dlum| vs native over the frame (0-255) / % px off by > 24 levels
  chg   the same inside the CHANGE mask: px where native itself changes by > 12 levels across the burst
        (the moving light / lava) -- where a ghost trail or a lagging light sits
  grad  sharpness: mean |gradient| of the arm / native's (< 1 = softer); still scenes only
  vs sc10  mean |dlum| between the sc10 arm and the sc10 + mask arm: on a STILL scene the mask must change nothing
For torch_on / torch_off frame 0 is the frame BEFORE the change and 1-6 after: chg per frame shows the lag.
Outputs in <out>: fsr174.txt; fsr174_<scene>.png (4x crops, rows 432 / 540: native | today | sc10 | sc10 + mask);
full/<scene>_<arm>.png (full-size frames) and viewer.html (arrows = scene, Space = arm, 1-4 = zoom), captions from
captions174.json in <out> when present.
"""
import json
import os
import shutil
import sys

import numpy as np
from PIL import Image, ImageDraw

CW, CH, Z = 160, 90, 4
SCENES = [("sun_still", 4), ("torch_still", 4), ("torch_move", 4), ("torch_on", 7), ("torch_off", 7),
          ("lava_near", 4), ("lava_far", 4), ("torch_move_f", 4), ("lava_near_f", 4), ("lava_far_f", 4)]
ROWS = [["native", "fsr3_432", "fsr3_432_sc10", "fsr3_432_sc10_rm"],
        ["native", "fsr3_540", "fsr3_540_sc10", "fsr3_540_sc10_rm"]]
ARMS = ["native", "fsr3_432", "fsr3_432_sc10", "fsr3_432_sc10_rm", "fsr3_540", "fsr3_540_sc10", "fsr3_540_sc10_rm"]
NAMES = {"native": "native 1080", "fsr3_432": "today 432", "fsr3_432_sc10": "sc10 432", "fsr3_432_sc10_rm": "sc10+mask 432",
         "fsr3_540": "today 540", "fsr3_540_sc10": "sc10 540", "fsr3_540_sc10_rm": "sc10+mask 540",
         "fsr3_432_sc10_rm_dbg": "FSR debug view, sc10+mask 432"}


def load(p):
    return np.asarray(Image.open(p).convert("RGB"), np.float32)


def lum(a):
    return a[..., 0] * 0.299 + a[..., 1] * 0.587 + a[..., 2] * 0.114


def grad(l):
    return (np.abs(np.diff(l, axis=1))[:-1, :] + np.abs(np.diff(l, axis=0))[:, :-1]).mean()


def tag(img, text, xy=(6, 4), col=(255, 255, 0)):
    d = ImageDraw.Draw(img)
    x, y = xy
    d.rectangle([x - 3, y - 2, x + 6 * len(text) + 5, y + 12], fill=(0, 0, 0))
    d.text((x, y), text, fill=col)


def best_window(score):
    h, w = score.shape
    ii = np.pad(score, ((1, 0), (1, 0))).cumsum(0).cumsum(1)
    best = (-1.0, 0, 0)
    for y in range(0, h - CH + 1, 10):
        for x in range(0, w - CW + 1, 10):
            s = ii[y + CH, x + CW] - ii[y, x + CW] - ii[y + CH, x] + ii[y, x]
            if s / (CW * CH) > best[0]:
                best = (s / (CW * CH), x, y)
    return best


def frames(src, scene, arm, n):
    fs = [os.path.join(src, f"{scene}_{arm}_{k}.png") for k in range(n)]
    return fs if all(os.path.exists(f) for f in fs) else None


def sheet(dst, name, imgs, x, y, label):
    rows = [r for r in ROWS if all(a in imgs for a in r)]
    if not rows:
        return
    w, h = CW * Z, CH * Z
    S = Image.new("RGB", (4 * (w + 4), len(rows) * (h + 4)), (30, 30, 30))
    for r, row in enumerate(rows):
        for c, a in enumerate(row):
            t = imgs[a].crop((x, y, x + CW, y + CH)).resize((w, h), Image.NEAREST)
            tag(t, f"{label} {NAMES[a]}  ({x},{y}) 4x")
            S.paste(t, (c * (w + 4), r * (h + 4)))
    S.save(os.path.join(dst, f"fsr174_{name}.png"))


def main():
    src, dst = sys.argv[1], sys.argv[2]
    os.makedirs(os.path.join(dst, "full"), exist_ok=True)
    rep = [f"task 174: FSR 3 today / sc10 / sc10 + reactive mask; source {src}",
           "per arm, mean over the kept frames: err = mean|dlum| vs native / %px > 24;  chg = the same inside the change mask;",
           "grad = sharpness vs native (still scenes);  vs sc10 = mean|dlum| sc10 vs sc10+mask (same frame)"]
    viewer = []
    caps = {}
    cp = os.path.join(dst, "captions174.json")
    if os.path.exists(cp):
        caps = json.load(open(cp))
    for scene, n in SCENES:
        fsx = {a: frames(src, scene, a, n) for a in ARMS + ["fsr3_432_sc10_rm_dbg"]}
        if fsx.get("native") is None:
            rep.append(f"== {scene}: no native burst -- skipped")
            continue
        nat = [lum(load(f)) for f in fsx["native"]]
        st = np.stack(nat)
        mask = (st.max(0) - st.min(0)) > 12
        still = scene in ("sun_still", "torch_still")
        rep.append(f"== {scene}: change mask {100 * mask.mean():.2f}% of the frame")
        post = range(1, n) if scene in ("torch_on", "torch_off") else range(n)
        L = {}
        gn = np.mean([grad(nat[k]) for k in range(n)])
        for a in ARMS[1:]:
            if fsx[a] is None:
                rep.append(f"  {a:<18} missing")
                continue
            L[a] = [lum(load(f)) for f in fsx[a]]
            d = [np.abs(L[a][k] - nat[k]) for k in range(n)]
            err = np.mean([d[k].mean() for k in post])
            big = np.mean([100 * (d[k] > 24).mean() for k in post])
            line = f"  {a:<18} err {err:5.2f} / {big:5.2f}%"
            if mask.any() and not still:
                cm = np.mean([d[k][mask].mean() for k in post])
                cb = np.mean([100 * (d[k][mask] > 24).mean() for k in post])
                line += f"   chg {cm:5.2f} / {cb:5.2f}%"
                if scene in ("torch_on", "torch_off"):
                    line += "   chg per frame (0 = before) " + " ".join(f"{d[k][mask].mean():5.2f}" for k in range(n))
            if still:
                line += f"   grad {np.mean([grad(L[a][k]) for k in range(n)]) / gn:5.3f}"
            if a.endswith("_rm") and a[:-3] in L:
                line += f"   vs sc10 {np.mean([np.abs(L[a][k] - L[a[:-3]][k]).mean() for k in range(n)]):5.3f}"
            rep.append(line)
        kk = 1 if scene in ("torch_on", "torch_off") else n - 1
        if not L:
            continue
        if still:
            score = np.abs(np.diff(nat[kk], axis=1))[:-1, :] + np.abs(np.diff(nat[kk], axis=0))[:, :-1]
            why = "native's most detailed window"
        else:
            ref = "fsr3_432_sc10" if "fsr3_432_sc10" in L else list(L)[-1]
            score = np.abs(L[ref][kk] - nat[kk]) * (mask if mask.any() else 1)
            why = f"where {ref} is furthest from native in the change mask"
        s, x, y = best_window(score)
        imgs = {a: Image.open(fsx[a][kk]).convert("RGB") for a in ARMS if fsx.get(a)}
        sheet(dst, scene, imgs, x, y, f"{scene} f{kk}")
        rep.append(f"  4x window ({x},{y}) frame {kk}: {why} ({s:.2f})")
        shots = []
        for a in ARMS + ["fsr3_432_sc10_rm_dbg"]:
            if fsx.get(a):
                fn = f"{scene}_{a}.png"
                shutil.copyfile(fsx[a][kk], os.path.join(dst, "full", fn))
                shots.append([NAMES[a], "full/" + fn])
        viewer.append(dict(n=f"{scene} (frame {kk})", cap=caps.get(scene, ""), win=[x, y], s=shots))

    page = """<!doctype html><html><head><meta charset="utf-8"><title>Task 174 FSR 3 reactive mask</title><style>
body{margin:0;background:#111;color:#ddd;font:14px -apple-system,Helvetica,sans-serif}
#bar{position:fixed;top:0;left:0;right:0;background:#000c;padding:6px 10px;z-index:2}
#bar b{color:#fff;font-size:16px} #cap{color:#ffd27a;margin-top:3px}
#wrap{position:absolute;top:76px;left:0;right:0;bottom:0;overflow:auto;cursor:grab}
#img{display:block;image-rendering:pixelated} #arm{color:#7fd;font-weight:bold}
</style></head><body><div id="bar"><b id="t"></b> &nbsp; <span id="arm"></span> &nbsp;
<span style="color:#888">&larr;/&rarr; scene &middot; Space / Shift+Space next / previous arm &middot; 1 fit &middot; 2 100% &middot; 3 200% &middot; 4 400% (centred on the 4x sheet's window)</span>
<div id="cap"></div></div><div id="wrap"><img id="img"></div>
<script>
const D=__DATA__;let i=0,arm=0,zoom=0;const img=document.getElementById('img'),wrap=document.getElementById('wrap');
function show(){const p=D[i];const k=((arm%p.s.length)+p.s.length)%p.s.length;img.src=p.s[k][1];
document.getElementById('t').textContent=p.n;document.getElementById('arm').textContent=p.s[k][0]+'  ('+(k+1)+'/'+p.s.length+')';
document.getElementById('cap').textContent=p.cap;const z=[0,1,2,4][zoom];img.style.width=z?(1920*z)+'px':'100%';
if(z){wrap.scrollLeft=(p.win[0]+80)*z-wrap.clientWidth/2;wrap.scrollTop=(p.win[1]+45)*z-wrap.clientHeight/2}}
document.onkeydown=e=>{if(e.key=='ArrowRight'){i=(i+1)%D.length;arm=0}else if(e.key=='ArrowLeft'){i=(i+D.length-1)%D.length;arm=0}
else if(e.key==' '){arm+=e.shiftKey?-1:1;e.preventDefault()}else if('1234'.includes(e.key)){zoom=+e.key-1}else return;show()};
let dr=null;wrap.onmousedown=e=>{dr=[e.clientX,e.clientY,wrap.scrollLeft,wrap.scrollTop];e.preventDefault()};
onmouseup=()=>dr=null;onmousemove=e=>{if(dr){wrap.scrollLeft=dr[2]-(e.clientX-dr[0]);wrap.scrollTop=dr[3]-(e.clientY-dr[1])}};show();
</script></body></html>"""
    with open(os.path.join(dst, "viewer.html"), "w") as f:
        f.write(page.replace("__DATA__", json.dumps(viewer)))
    open(os.path.join(dst, "fsr174.txt"), "w").write("\n".join(rep) + "\n")
    print("\n".join(rep))


if __name__ == "__main__":
    main()
