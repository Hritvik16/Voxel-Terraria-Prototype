# Task 233: sheets + metrics from the day / night cost-split stills (python3 -I, run from the repo root).
# Sources Screenshots/233/{day,night}/<pose>_<arm>_clean.png. Uses task 231's metric + sky-mask functions (ablate.py).
# Day sheet: rows forest_eye / jungle_eye / walk_near / redwood_far1; columns before (look233_off) | +sun (sun233_only) |
#   +sky (sky233_only) | +grade (grade233_only) | all three (full). Metrics per tile + distance to LotL (231's target/scale).
# Pairs: full-size forest_eye and redwood_far1 before / all three, plus viewer.html (flip). LotL side by side.
# Night: torch_camp FULL vs look233_off (raw = no upscaler): mean |d| and px > 8 vs the raw/raw2 floor (same arm, 10 frames).
import os, sys, numpy as np
sys.path.insert(0, 'Logs/overnight/231')
import ablate as A
from PIL import Image
S = 'Screenshots/233'; OUT = os.path.join(S, 'sheets'); os.makedirs(OUT, exist_ok=True)
ARMS = [('look233_off', 'before (noon)'), ('sun233_only', '+ low sun'), ('sky233_only', '+ pale sky'),
        ('grade233_only', '+ grade'), ('full', 'all three')]
POSES = ['forest_eye', 'jungle_eye', 'walk_near', 'redwood_far1']
def path(pose, arm, kind='clean', L='day'): return os.path.join(S, L, f'{pose}_{arm}_{kind}.png')
def load(p): return A.load(p) if os.path.exists(p) else None

lines = []
# --- LotL target, as 231 computed it ---
sbs = A.load('Screenshots/222/side_by_side.png')
theirs = {'canyon': sbs[40:400, 640:1280], 'forest': sbs[400:760, 640:1280], 'swamp': sbs[760:1120, 640:1280]}
tm = {k: A.metrics(a, A.sky_mask_theirs(a), np.zeros(a.shape[:2], np.float32)) for k, a in theirs.items()}
target = {m: float(np.nanmean([tm[k][m] for k in tm])) for m in A.METRICS}
# 231's per-metric scale (std over its six originals), copied from Logs/overnight/231/ablation.txt
scale = dict(zip(A.METRICS, [0.163, 0.190, 0.051, 0.093, 0.062, 0.055, 0.119, 0.023]))
def zdist(mm): return float(np.nanmean([abs(target[m] - mm[m]) / scale[m] for m in A.METRICS if np.isfinite(mm[m])]))
lines.append('TASK 233 -- metrics of the day stills (231\'s definitions, 640x360; sky mask = 231\'s blue-sky mask on the BEFORE still,')
lines.append('  applied to every arm of that pose). mean_L = mean luma of the whole frame. dist = 231\'s distance to LotL (lower = closer).')
lines.append(f'{"pose / arm":<28}{"mean_L":>8}' + ''.join(f'{m:>11}' for m in A.METRICS) + f'{"dist":>8}')
lines.append(f'{"THEIRS (target)":<28}{"":>8}' + ''.join(f'{target[m]:>11.3f}' for m in A.METRICS))
tiles = []
for pose in POSES:
    ref = load(path(pose, 'look233_off'))
    sky = A.sky_mask_ours(ref) if ref is not None else None
    for arm, name in ARMS:
        a = load(path(pose, arm))
        if a is None:
            tiles.append(A.label(Image.new('RGB', (640, 360), (40, 40, 40)), f'{pose}: {name} (not shot)')); continue
        hud = np.zeros(a.shape[:2], np.float32)
        mm = A.metrics(a, sky, hud); mL = float(A.luma(a).mean())
        lines.append(f'{pose + " / " + arm:<28}{mL:>8.3f}' + ''.join(f'{mm[m]:>11.3f}' for m in A.METRICS) + f'{zdist(mm):>8.2f}')
        tiles.append(A.tile(a, f'{pose}: {name}', (640, 360)))
    lines.append('')
A.sheet(tiles, 5, (640, 360)).save(os.path.join(OUT, 'sheet_day.png'))

# --- full-size pairs + viewer ---
pairs = []
for pose in ('forest_eye', 'redwood_far1'):
    for arm, tag in (('look233_off', 'before'), ('full', 'all3')):
        p = path(pose, arm)
        if os.path.exists(p):
            Image.open(p).convert('RGB').save(os.path.join(OUT, f'full_{pose}_{tag}.png')); pairs.append(f'full_{pose}_{tag}.png')
with open(os.path.join(OUT, 'viewer.html'), 'w') as f:
    f.write('<html><body style="background:#111;color:#ddd;font-family:sans-serif;margin:0">'
            '<div id=t style="position:fixed;top:4px;left:8px;background:#000a;padding:4px">Space = flip before / all three; '
            'arrows = pose</div><img id=i style="width:100%"><script>var P=' + str(pairs) + ';var k=0;'
            'function s(){document.getElementById("i").src=P[k];document.getElementById("t").textContent=P[k]+'
            '"  (Space flips, arrows pick the pose)";}document.onkeydown=function(e){if(e.key==" "){k^=1;}'
            'else if(e.key=="ArrowRight"||e.key=="ArrowLeft"){k=(k+2)%P.length;}s();e.preventDefault();};s();</script></body></html>')

# --- LotL side by side: our before | our all three | their forest ---
lt = []
for pose, th in (('forest_eye', 'forest'), ('redwood_far1', 'canyon'), ('jungle_eye', 'swamp')):
    for arm, name in (('look233_off', 'ours before'), ('full', 'ours all three')):
        a = load(path(pose, arm))
        lt.append(A.tile(a, f'{pose}: {name}', (640, 360)) if a is not None else A.label(Image.new('RGB', (640, 360)), 'missing'))
    lt.append(A.tile(theirs[th], f'LotL {th}', (640, 360)))
A.sheet(lt, 3, (640, 360)).save(os.path.join(OUT, 'lotl_side_by_side.png'))

# --- grade luma check: noon = grade233_only vs before; LOW SUN = full vs grade_off (sun + sky, grade off) ---
lines.append('GRADE mean-luma check (whole frame): noon = grade233_only vs before; low sun = full vs grade_off (both with the pale sky)')
for pose in POSES:
    b, g, n, f = (load(path(pose, a)) for a in ('look233_off', 'grade233_only', 'grade_off', 'full'))
    t = f'{pose:<14}'
    if b is not None and g is not None:
        mb, mg = A.luma(b).mean(), A.luma(g).mean(); t += f' noon: {mb:.3f} -> {mg:.3f} ({100 * (mg / mb - 1):+5.1f} %)'
    if n is not None and f is not None:
        mn, mf = A.luma(n).mean(), A.luma(f).mean(); t += f'   low sun: {mn:.3f} -> {mf:.3f} ({100 * (mf / mn - 1):+5.1f} %)'
    lines.append(t)

# --- night identity ---
lines.append('')
lines.append('NIGHT torch_camp: FULL vs look233_off (raw stills, no upscaler); floor = the same arm 10 frames later (raw vs raw2)')
def dif(p, q):
    a, b = load(p), load(q)
    if a is None or b is None: return None
    d = np.abs(a - b) * 255; return d.mean(), 100 * (d.max(-1) > 8).mean()
for kind in ('raw', 'clean'):
    r = dif(path('torch_camp', 'full', kind, 'night'), path('torch_camp', 'look233_off', kind, 'night'))
    if r: lines.append(f'  {kind:5s} FULL vs look233_off: mean|d| {r[0]:.3f}  px>8 {r[1]:.3f} %')
for arm in ('full', 'look233_off'):
    r = dif(path('torch_camp', arm, 'raw', 'night'), path('torch_camp', arm, 'raw2', 'night'))
    if r: lines.append(f'  floor {arm:11s} raw vs raw2:  mean|d| {r[0]:.3f}  px>8 {r[1]:.3f} %')
open('Logs/overnight/233/metrics.txt', 'w').write('\n'.join(lines) + '\n')
print('\n'.join(lines))
