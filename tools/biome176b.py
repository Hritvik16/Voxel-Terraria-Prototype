#!/usr/bin/env python3
"""Task 176b: biome REGION SIZE candidates on the real generator (BiomeSizeDump planes).

Reads Logs/TerrainGen/c1/sizes/<plane>/ (b10, c176, w100, w150, w200, w350; same land mask = the current map).
  python3 tools/biome176b.py stats  out.txt         per plane and seed: change, straight, regions, shares, absent / one-region
  python3 tools/biome176b.py sheets outdir          per seed: sheet_seed<S>.png (512 px panels) + full_seed<S>.png (1024 px) + viewer.html
Statistics are tools/biome176.py's (8 m record grid, land = map height > 0).
"""
import sys, os
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import biome176 as B

SEEDS = [42, 7, 2026, 31337]
PLANES = [('b10', 'BEFORE: B10 belts (one octave, ~1.6 x R)'),
          ('c176', 'NOW: task 176, wave 0.75 x R, octaves 1/.35/.2'),
          ('w100', 'SIZE 1: wave 1.0 x R, octaves 1/.35/.2'),
          ('w150', 'SIZE 2: wave 1.5 x R, octaves 1/.35/.2/.12'),
          ('w200', 'SIZE 3: wave 2.0 x R, octaves 1/.35/.2'),
          ('w350', 'SIZE 4: wave 3.5 x R, octaves 1/.35/.2')]
SHORT = ['F', 'D', 'S', 'J', 'Sv', 'B']


def all_stats():
    res = {}
    for p, _ in PLANES:
        M = B.load(os.path.join('sizes', p))
        for s in SEEDS:
            res[(p, s)] = B.stats(M[s]['land'], M[s]['biome'])
    return res


def stats_txt(res):
    L = ['=== Task 176b: biome region sizes, real generator (BiomeSizeDump; 4 seeds; record plane 8 m cells; land = map height > 0)',
         f'regions = 4-connected land patches >= {B.REGION_MIN_HA:.0f} ha per biome (F D S J Sv B); straight = longest border within +-{B.STRAIGHT_TOL_M:.0f} m of a line;',
         'change = land metres walked E-W per biome change; median = area-weighted median patch (ha); one-region / absent = biomes with exactly 1 / 0 regions', '']
    for p, title in PLANES:
        L.append(f'--- {p}: {title}')
        L.append('  seed    change m  straight m  median ha  regions F/D/S/J/Sv/B  shares % F D S J Sv B         one-region  absent')
        ch, stv = [], []
        for s in SEEDS:
            r = res[(p, s)]
            one = [SHORT[b] for b in range(6) if r['regions'][b] == 1]
            ab = [SHORT[b] for b in range(6) if r['regions'][b] == 0]
            ch.append(r['change']); stv.append(r['straight'])
            L.append(f"  {s:<6} {r['change']:8.0f} {r['straight']:10.0f} {r['median']:10.0f}   {'/'.join(str(v) for v in r['regions']):<20}  " +
                     ' '.join(f'{v:4.1f}' for v in r['share']) + f"   {','.join(one) or '-':<10}  {','.join(ab) or '-'}")
        L.append(f"  MEAN change {np.mean(ch):.0f} m; straight {min(stv):.0f}-{max(stv):.0f} m (mean {np.mean(stv):.0f})")
        L.append('')
    return '\n'.join(L)


def sheet(seed, res, P, out):
    from PIL import Image, ImageDraw
    G, H = 10, 34
    cols, rows = 3, 2
    canvas = Image.new('RGB', (cols * P + (cols - 1) * G, rows * (P + H + G)), (255, 255, 255))
    dr = ImageDraw.Draw(canvas)
    for i, (p, title) in enumerate(PLANES):
        M = B.load(os.path.join('sizes', p))
        r = res[(p, seed)]
        img = Image.fromarray(B.render(M[seed], r['seg'], P))
        x = (i % cols) * (P + G); y = (i // cols) * (P + H + G)
        canvas.paste(img, (x, y + H))
        dr.text((x + 4, y + 2), f'seed {seed}  {title}', fill=(0, 0, 0))
        dr.text((x + 4, y + 17), f"change E-W {r['change']:.0f} m | straight {r['straight']:.0f} m (magenta) | regions " +
                '/'.join(str(v) for v in r['regions']), fill=(0, 0, 0))
    canvas.save(out)


def viewer(out_dir, res):
    import json
    items = []
    for s in SEEDS:
        c = ', '.join(f"{p} {res[(p, s)]['change']:.0f} m" for p, _ in PLANES)
        items.append((f'full_seed{s}.png', f'seed {s} FULL SIZE (1 px = 8 m). Top row: BEFORE B10 belts | NOW 176 (0.75) | SIZE 1 (1.0); bottom: SIZE 2 (1.5) | SIZE 3 (2.0) | SIZE 4 (3.5). '
                      f'Look at how big each colour patch gets left->right and whether the borders (dark lines) turn back into long straight belts; magenta = longest straight border. E-W change: {c}.'))
    for s in SEEDS:
        items.append((f'sheet_seed{s}.png', f'seed {s} thumbnail sheet (same panels, 512 px).'))
    tmp = os.path.join(out_dir, 'viewer.html')
    page = '''<!doctype html><meta charset=utf-8><title>Task 176b biome sizes</title>
<style>body{margin:0;background:#222;color:#eee;font:14px sans-serif}#cap{position:fixed;top:0;left:0;right:0;background:#000c;padding:8px;z-index:2}
#box{position:absolute;top:70px;left:0;right:0;bottom:0;overflow:auto}img{display:block;image-rendering:pixelated}</style>
<div id=cap></div><div id=box><img id=im></div><script>
const L=%s;let i=0,z=0;const im=document.getElementById('im'),cap=document.getElementById('cap');
function show(){im.src=L[i][0];cap.textContent=(i+1)+'/'+L.length+'  '+L[i][0]+' -- '+L[i][1]+'   [arrows: image, 1 fit, 2 100%%, 3 200%%, 4 300%%]';
im.onload=()=>{if(z==0){im.style.width='100%%';im.style.height='auto'}else{im.style.width=(im.naturalWidth*z)+'px'}}; im.onload();}
document.onkeydown=e=>{if(e.key=='ArrowRight'||e.key=='ArrowDown')i=(i+1)%%L.length;else if(e.key=='ArrowLeft'||e.key=='ArrowUp')i=(i+L.length-1)%%L.length;
else if('1234'.includes(e.key))z=+e.key-1;else return;e.preventDefault();show()};show();</script>''' % json.dumps(items)
    open(tmp, 'w').write(page)


if __name__ == '__main__':
    res = all_stats()
    if sys.argv[1] == 'stats':
        t = stats_txt(res); print(t); open(sys.argv[2], 'w').write(t + '\n')
    elif sys.argv[1] == 'sheets':
        d = sys.argv[2]; os.makedirs(d, exist_ok=True)
        for s in SEEDS:
            sheet(s, res, 512, os.path.join(d, f'sheet_seed{s}.png'))
            sheet(s, res, 1024, os.path.join(d, f'full_seed{s}.png'))
        viewer(d, res)
        print('wrote', d)
