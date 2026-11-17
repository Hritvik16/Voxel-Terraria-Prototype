#!/usr/bin/env python3
# Task 231: why does Lay of the Land look "cinematic" and ours not? OFFLINE factor ablation on stills.
# No engine change. Run from the repo root:  python3 -I Logs/overnight/231/ablate.py
# Inputs (looked at first): Screenshots/226/day/{forest,jungle,walk_near}_eye_full_clean.png (ours, 1920x1080, HUD minimap
#   top-right left untouched), Screenshots/222/side_by_side.png (right half = their three official Steam shots, 640x360).
#   Optional: Screenshots/231/lowsun/*.png (this task's low-sun launch) get the same treatment.
# Factors (each a SIMPLE change we could make in the engine):
#   a warm sun / cool shade split   b pale hazy sky + soft clouds   c colour grade (greens, warm mids, S-curve, vignette)
#   d lifted shade (darkest 20 % toward a coloured bounce)   e fine grain at ~1/4 voxel
# Reverse test: THEIR shots with the grade flattened, the sky made our blue, the warm/cool split killed, the shade
#   crushed, and the fine texture smoothed.
# Metrics (all at 640x360, sky and HUD excluded where named): see METRICS below. "gap closed" = how far a variant moves
#   each metric from ours toward theirs (1 = all the way, 0 = not at all, < 0 = the wrong way).
import os, sys, glob
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = os.getcwd()
OUT = os.path.join(ROOT, 'Screenshots/231/sheets'); os.makedirs(OUT, exist_ok=True)
REPORT = os.path.join(ROOT, 'Logs/overnight/231/ablation.txt')
rng = np.random.default_rng(231)

def load(p): return np.asarray(Image.open(p).convert('RGB')).astype(np.float32) / 255.0
def save(a, p): Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)).save(p)
def luma(a): return a[..., 0] * 0.2126 + a[..., 1] * 0.7152 + a[..., 2] * 0.0722
def sstep(e0, e1, x): t = np.clip((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t)
def blur(m, r):
    im = Image.fromarray((np.clip(m, 0, 1) * 255).astype(np.uint8)); return np.asarray(im.filter(ImageFilter.GaussianBlur(r))).astype(np.float32) / 255.0
def blur3(a, r): return np.stack([blur(a[..., i], r) for i in range(3)], -1)
def mix(a, b, w): w = w[..., None] if np.ndim(w) == 2 else w; return a + (b - a) * w

def hud_mask(a):
    h, w, _ = a.shape; m = np.zeros((h, w), np.float32)
    if w == 1920: m[0:285, 1655:1915] = 1          # 226's minimap + its label line
    return m

def sky_mask_ours(a):
    # pure-sky pixels: our sky is a saturated blue gradient; restrict to the upper 62 % so rivers never count
    r, g, b = a[..., 0], a[..., 1], a[..., 2]; h = a.shape[0]
    m = ((b > 0.55) & (b - r > 0.30) & (b - g > 0.17)).astype(np.float32)
    m[int(h * 0.62):] = 0
    return m

def sky_mask_theirs(a):
    # their sky: pale blue or white cloud, bright, low local detail, upper 55 %
    r, g, b = a[..., 0], a[..., 1], a[..., 2]; L = luma(a); h = a.shape[0]
    bright = (L > 0.55) & (b >= r - 0.02) & (b >= g - 0.04)
    detail = np.abs(L - blur(L, 2))
    m = (bright & (detail < 0.035)).astype(np.float32)
    m[int(h * 0.55):] = 0
    m = (blur(m, 1.5) > 0.6).astype(np.float32)
    return m

def value_noise(h, w, cells_y, cells_x, seed, aniso=1.0):
    r = np.random.default_rng(seed).random((cells_y, int(cells_x * aniso) + 1)).astype(np.float32)
    return np.asarray(Image.fromarray((r * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)).astype(np.float32) / 255.0

# ---------------- the five factors (ours) ----------------
def f_a(a, sky):
    # a: warm lit / cool lifted shade, split by a luma mask (engine: sun colour warmer + ambient/sky-fill colour cooler & a bit
    #    brighter -- constants in the lighting config, no new pass)
    L = luma(a); lit = sstep(0.22, 0.55, L) * (1 - sky)
    warm = a * np.array([1.10, 1.01, 0.84], np.float32)
    cool = a * np.array([0.90, 0.98, 1.12], np.float32) + np.array([0.020, 0.030, 0.055], np.float32)
    shade = (1 - lit) * (1 - sky)
    return mix(mix(a, warm, lit), cool, shade)

def f_b(a, sky, seed=7):
    # b: paler hazy sky, soft clouds, sky pixels only (engine: sky colour constants + a 2-octave cloud noise in the sky shader)
    h, w, _ = a.shape; y = np.linspace(0, 1, h, dtype=np.float32)[:, None]
    zen = np.array([0.50, 0.66, 0.86], np.float32); hor = np.array([0.82, 0.86, 0.89], np.float32)
    t = sstep(0.0, 0.62, y)[..., None]
    base = zen + (hor - zen) * t
    base = np.broadcast_to(base, (h, w, 3)).copy()
    n = 0.55 * value_noise(h, w, 5, 9, seed, 1.6) + 0.30 * value_noise(h, w, 11, 19, seed + 1, 1.6) + 0.15 * value_noise(h, w, 24, 40, seed + 2)
    cloud = sstep(0.52, 0.74, n) * (0.35 + 0.65 * (1 - sstep(0.15, 0.6, y)))
    cshade = 0.88 + 0.12 * sstep(0.45, 0.8, n)
    cloudcol = np.array([1.0, 0.98, 0.95], np.float32)[None, None] * cshade[..., None]
    base = a * 0.35 + base * 0.65                 # keep 35 % of the engine's own sky (its sun-side glow, its low-sun tint)
    sk = mix(base, cloudcol, cloud * 0.9)
    # SOFT sky mask from blueness, so the change fades out where the sky turns pale / peach at the horizon (a low sun) instead
    # of ending at a hard edge; distant blue-grey hills (b-r < 0.1) and water (below 62 %) stay out
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    m = sstep(0.10, 0.30, b - r) * sstep(0.05, 0.17, b - g) * sstep(0.40, 0.55, b)
    m[int(h * 0.62):] = 0
    m = np.maximum(blur(m, 1.2), blur(sky, 1.2))  # soft edge so leaf silhouettes are not cut
    return mix(a, sk, m)

def grade(a, keep=None):
    # c: colour grade at the end of the frame (engine: constants in the existing final/tonemap pass, per pixel ALU only)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]; L = luma(a)
    green = sstep(0.0, 0.12, g - np.maximum(r, b))                   # how "green" a pixel is
    sat = 1.0 - 0.28 * green - 0.08                                  # greens -28 %, all -8 %
    o = L[..., None] + (a - L[..., None]) * sat[..., None]
    mid = (4 * L * (1 - L))[..., None]
    o = o + mid * np.array([0.035, 0.012, -0.030], np.float32)      # warm midtones
    s = o * o * (3 - 2 * o); o = o + (np.clip(s, 0, 1) - o) * 0.30   # filmic-ish S-curve, 30 %
    o = o * 0.96 + 0.025                                             # lifted toe, soft shoulder
    h, w, _ = a.shape; yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    rr = ((xx - w / 2) / (w / 2)) ** 2 * 0.8 + ((yy - h / 2) / (h / 2)) ** 2 * 0.8
    o = o * (1 - 0.22 * np.clip(rr, 0, 1.6) / 1.6)[..., None]       # slight vignette
    return o

def f_c(a, sky): return grade(a)

def f_d(a, sky):
    # d: lift the shade toward a coloured bounce = the mean colour of the lit pixels. Engine form = an ABSOLUTE coloured
    #    ambient floor (a percentile needs a histogram; a floor is one constant or the sky fill's existing ground-bounce
    #    share; no new pass), so here: pixels under luma 0.18 (the darkest ~20 % at forest_eye, ~95 % at jungle_eye).
    L = luma(a); ns = (1 - sky) > 0.5
    litm = L > np.percentile(L[ns], 60)
    bounce = a[litm & ns].mean(0); bounce = bounce / max(luma(bounce[None, None])[0, 0], 1e-3)   # unit-luma tint
    w = (1 - sstep(0.0, 0.18, L)) * (1 - sky)
    target = a * 1.8 + bounce * 0.07
    return mix(a, target, w)

def f_e(a, sky, horizon=None):
    # e: per-voxel grain at ~1/4 voxel. We have no voxel ids in a still, so the cell size follows screen depth: ~2 px at
    #    the horizon row, ~10 px at the bottom (a 0.1 m voxel there is ~40 px). Engine: a 2nd hash at 4x the voxel
    #    frequency in the existing LotlVoxelVary (per pixel ALU only, no memory, world-anchored).
    h, w, _ = a.shape
    hz = int(h * (horizon if horizon else 0.36))
    out = np.zeros((h, w), np.float32)
    scales = [(2, 0.0), (3, 0.2), (5, 0.4), (7, 0.6), (10, 0.8)]
    rows = np.clip((np.arange(h) - hz) / max(1, h - hz), 0, 1)
    for i, (s, start) in enumerate(scales):
        nb = rng.random((h // s + 2, w // s + 2)).astype(np.float32) * 2 - 1
        n = np.repeat(np.repeat(nb, s, 0), s, 1)[:h, :w]
        end = scales[i + 1][1] if i + 1 < len(scales) else 1.01
        band = ((rows >= start) & (rows < end)).astype(np.float32)
        if start == 0.0: band = (rows < end).astype(np.float32)
        out += n * band[:, None]
    # grain only on geometry: above the horizon row, smooth pixels (sky the mask missed, e.g. a low-sun horizon band) get none
    L = luma(a); det = np.abs(L - blur(L, 2.0))
    geo = np.where(np.arange(h)[:, None] >= hz, 1.0, sstep(0.004, 0.015, det)).astype(np.float32)
    amp = 0.07 * (1 - sky) * geo
    return a * (1 + out * amp)[..., None]

FACTORS = [('a', 'warm sun / cool shade', f_a), ('b', 'pale hazy sky + clouds', f_b), ('c', 'colour grade', f_c),
           ('d', 'lifted shade', f_d), ('e', 'fine grain 1/4 voxel', f_e)]

def all_five(a, sky):
    o = f_e(a, sky); o = f_d(o, sky); o = f_a(o, sky); o = f_b(o, sky); return grade(o)

# ---------------- reverse (theirs) ----------------
def r_flat(a, sky):
    # flatten the grade: white balance on the NEAR-NEUTRAL pixels (stone, path, cloud: chroma < 0.10; grey-world on a green
    # frame would push it magenta), saturation x1.45, undo the S-curve (linear contrast)
    L0 = luma(a); ch = a.max(-1) - a.min(-1); nn = (ch < 0.10) & (L0 > 0.15) & (L0 < 0.92)
    m = a[nn].mean(0) if nn.sum() > 200 else a.reshape(-1, 3).mean(0); o = a * (m.mean() / m)
    L = luma(o); o = L[..., None] + (o - L[..., None]) * 1.45
    s = o * o * (3 - 2 * o); o = o - (np.clip(s, 0, 1) - o) * 0.30
    return o

def r_bluesky(a, sky):
    h, w, _ = a.shape; y = np.linspace(0, 1, h, dtype=np.float32)[:, None, None]
    zen = np.array([50, 91, 175], np.float32) / 255; hor = np.array([90, 131, 193], np.float32) / 255
    base = zen + (hor - zen) * sstep(0.0, 0.55, y)
    return mix(a, np.broadcast_to(base, a.shape), blur(sky, 1.0))

def r_nosplit(a, sky):
    # kill the warm/cool split: shift the lit band's and the shade band's mean chroma both to the image mean chroma
    L = luma(a); ns = sky < 0.5
    lit = sstep(0.22, 0.55, L); sh = 1 - lit
    chroma = a - L[..., None]
    cl = (chroma[ns] * lit[ns][:, None]).sum(0) / lit[ns].sum()
    cs = (chroma[ns] * sh[ns][:, None]).sum(0) / sh[ns].sum()
    cm = chroma[ns].mean(0)
    o = a + lit[..., None] * (cm - cl) + sh[..., None] * (cm - cs)
    return mix(a, o, 1 - sky)

def r_crush(a, sky):
    # the reverse of d: our shade goes near black
    L = luma(a); ns = sky < 0.5; p30 = np.percentile(L[ns], 30)
    k = 0.30 + 0.70 * sstep(0.0, p30, L)
    return mix(a, a * k[..., None], 1 - sky)

def r_smooth(a, sky):
    # the reverse of e: smooth the texture INSIDE a voxel away (median 3 px at 640 wide; a near voxel is 6-15 px there, so
    # voxel edges survive, the sub-voxel texture does not)
    im = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.MedianFilter(3))
    return np.asarray(im).astype(np.float32) / 255

REVERSE = [('flat', 'grade flattened', r_flat), ('bluesky', 'sky made our blue', r_bluesky), ('nosplit', 'warm/cool split killed', r_nosplit),
           ('crush', 'shade crushed', r_crush), ('smooth', 'fine texture smoothed', r_smooth)]

def all_removed(a, sky):
    o = r_smooth(a, sky); o = r_crush(o, sky); o = r_nosplit(o, sky); o = r_bluesky(o, sky); return r_flat(o, sky)

# ---------------- metrics ----------------
METRICS = ['sky_chroma', 'sky_luma', 'split_RB', 'shade_p10', 'chroma', 'green_chr', 'contrast', 'detail']
def metrics(a, sky, hud):
    im = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8)).resize((640, 360), Image.BILINEAR)
    a = np.asarray(im).astype(np.float32) / 255
    sky = np.asarray(Image.fromarray((sky * 255).astype(np.uint8)).resize((640, 360), Image.NEAREST)) > 127
    hud = np.asarray(Image.fromarray((hud * 255).astype(np.uint8)).resize((640, 360), Image.NEAREST)) > 127
    ns = ~sky & ~hud; L = luma(a)
    mx, mn = a.max(-1), a.min(-1); sat = mx - mn          # chroma (sRGB max-min): how colourful, not HSV saturation
    lo, hi = np.percentile(L[ns], 30), np.percentile(L[ns], 70)
    rb = a[..., 0] - a[..., 2]
    green = (a[..., 1] > a[..., 0]) & (a[..., 1] > a[..., 2]) & ns
    lap = np.abs(4 * L[1:-1, 1:-1] - L[:-2, 1:-1] - L[2:, 1:-1] - L[1:-1, :-2] - L[1:-1, 2:])
    return {
        'sky_chroma': float(sat[sky & ~hud].mean()) if (sky & ~hud).sum() > 50 else float('nan'),
        'sky_luma': float(L[sky & ~hud].mean()) if (sky & ~hud).sum() > 50 else float('nan'),
        'split_RB': float(rb[ns & (L >= hi)].mean() - rb[ns & (L <= lo)].mean()),
        'shade_p10': float(np.percentile(L[ns], 10)),
        'chroma': float(sat[ns].mean()),
        'green_chr': float(sat[green].mean()) if green.sum() > 50 else float('nan'),
        'contrast': float(np.percentile(L[ns], 90) - np.percentile(L[ns], 10)),
        'detail': float(lap[ns[1:-1, 1:-1]].mean()),
    }

# ---------------- sheets ----------------
try: FONT = ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial.ttf', 28)
except Exception: FONT = ImageFont.load_default()
def label(img, text):
    d = ImageDraw.Draw(img); w = d.textlength(text, font=FONT)
    d.rectangle([0, 0, w + 16, 40], fill=(0, 0, 0)); d.text((8, 4), text, fill=(255, 235, 60), font=FONT); return img
def tile(a, text, size=(960, 540)):
    return label(Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)).resize(size, Image.LANCZOS), text)
def sheet(tiles, cols, size):
    rows = (len(tiles) + cols - 1) // cols; s = Image.new('RGB', (cols * size[0], rows * size[1]), (20, 20, 20))
    for i, t in enumerate(tiles): s.paste(t, ((i % cols) * size[0], (i // cols) * size[1]))
    return s

def main():
    lines = []
    sbs = load('Screenshots/222/side_by_side.png')
    theirs = {'canyon': sbs[40:400, 640:1280], 'forest': sbs[400:760, 640:1280], 'swamp': sbs[760:1120, 640:1280]}
    ours = {'forest_eye': 'Screenshots/226/day/forest_eye_full_clean.png', 'jungle_eye': 'Screenshots/226/day/jungle_eye_full_clean.png',
            'walk_near': 'Screenshots/226/day/walk_near_full_clean.png'}
    for p in sorted(glob.glob('Screenshots/231/lowsun/*.png')):
        ours['lowsun_' + os.path.splitext(os.path.basename(p))[0]] = p
    horizon = {'forest_eye': 0.36, 'jungle_eye': 0.55, 'walk_near': 0.25, 'lowsun_forest_eye': 0.36, 'lowsun_jungle_eye': 0.55,
               'lowsun_redwood_far1': 0.66}
    ours_d = {}

    # theirs: metrics of the originals (the target)
    tm = {}
    for k, a in theirs.items():
        sk = sky_mask_theirs(a); tm[k] = metrics(a, sk, np.zeros(sk.shape, np.float32))
    target = {m: float(np.nanmean([tm[k][m] for k in tm])) for m in METRICS}

    # each metric's scale = its spread (std) over the six ORIGINAL shots (our 3 + their 3), so a tiny gap cannot blow up
    orig = list(tm.values())
    for name, path in ours.items():
        if name.startswith('lowsun_'): continue
        a = load(path); hud = hud_mask(a); orig.append(metrics(a, sky_mask_ours(a) * (1 - hud), hud))
    scale = {m: max(float(np.nanstd([o[m] for o in orig])), 1e-3) for m in METRICS}
    def zdist(mm, ref): return float(np.nanmean([abs(ref[m] - mm[m]) / scale[m] for m in METRICS if np.isfinite(mm[m]) and np.isfinite(ref[m])]))
    def row(name, mm): return f'{name:<26}' + ''.join(f'{mm[m]:>10.3f}' for m in METRICS)
    lines.append('TASK 231 -- offline factor ablation (no engine change). Metrics at 640x360; sky = the pure-sky mask; HUD box excluded.')
    lines.append('sky_chroma/sky_luma: mean chroma (max-min) / luma of sky pixels; split_RB: (R-B) of the brightest 30 % minus (R-B) of the darkest 30 %')
    lines.append('  (warm lit vs cool shade, > 0 = warm sun / cool shade); shade_p10: 10th-percentile luma (how dark the shade gets);')
    lines.append('  chroma / green_chr: mean chroma of all / green-dominant non-sky pixels; contrast: luma p90-p10; detail: mean |Laplacian| of luma.')
    lines.append('')
    lines.append(f'{"THEIRS (target)":<26}' + ''.join(f'{m:>10}' for m in METRICS))
    for k in tm: lines.append(row('  ' + k, tm[k]))
    lines.append(row('  mean = TARGET', target))
    lines.append(row('  metric scale (std of 6)', scale))
    lines.append('')

    # ours: factors
    gap_rows = {}
    for name, path in ours.items():
        a = load(path); hud = hud_mask(a); sky = sky_mask_ours(a) * (1 - hud)
        hz = horizon.get(name, 0.36)
        res = {'ours': a}
        for key, _, f in FACTORS:
            o = f(a, sky) if key != 'e' else f_e(a, sky, hz)
            res[key] = mix(o, a, hud)
        o = f_e(a, sky, hz); o = f_d(o, sky); o = f_a(o, sky); o = f_b(o, sky); o = grade(o)
        res['all'] = mix(o, a, hud)
        skym = {k: (sky if k not in ('b', 'all') else sky) for k in res}
        mm = {k: metrics(v, skym[k], hud) for k, v in res.items()}
        ours_d[name] = zdist(mm['ours'], target)
        lines.append(f'OURS {name}  ({path})  sky pixels {sky.mean() * 100:.1f} %   raw distance to theirs {ours_d[name]:.3f}')
        lines.append(f'{"":<26}' + ''.join(f'{m:>10}' for m in METRICS))
        for k in res: lines.append(row('  ' + k, mm[k]))
        # gap closed per metric (toward the TARGET), averaged over the metrics where ours and theirs differ clearly
        lines.append('  gap closed toward theirs (1 = all the way, 0 = none, <0 = wrong way; nan = ours already within 0.3 scale of theirs):')
        for k in res:
            if k == 'ours': continue
            gs = []
            for m in METRICS:
                d0 = target[m] - mm['ours'][m]
                if not np.isfinite(d0) or not np.isfinite(mm[k][m]) or abs(d0) < 0.3 * scale[m]: gs.append(np.nan); continue   # gap too small to judge
                gs.append((mm[k][m] - mm['ours'][m]) / d0)
            # distance to target, each metric scaled by the ours-theirs gap
            dist = zdist(mm[k], target) / zdist(mm['ours'], target)
            gap_rows.setdefault(('low sun 12 deg (231 launch)' if name.startswith('lowsun_') else 'noon (226 stills)', k), []).append(dist)
            lines.append(f'    {k:<5}' + ''.join(f'{m[:9]:>10}' for m in []) + ''.join(f'{g:>10.2f}' for g in gs) + f'   distance to theirs {dist:.2f} (ours = 1.00)')
        lines.append('')
        # sheet (i): 2 rows x 4, tiles 960x540; the 8th tile is their closest-content shot for reference
        ref = theirs['forest' if 'jungle' not in name else 'swamp']
        lab = {'ours': f'OURS {name} (226)', 'a': '+a warm sun / cool shade', 'b': '+b pale hazy sky + clouds', 'c': '+c colour grade',
               'd': '+d lifted shade', 'e': '+e fine grain 1/4 voxel', 'all': 'ALL FIVE (a-e)'}
        tiles = [tile(res[k], lab[k]) for k in ['ours', 'a', 'b', 'c', 'd', 'e', 'all']] + [tile(ref, 'THEIRS (reference, 640 px upscaled)')]
        sheet(tiles, 4, (960, 540)).save(os.path.join(OUT, f'grid_{name}.png'))
        for k in res: save(res[k], os.path.join(OUT, f'full_{name}_{k}.png'))
    for grp in ['noon (226 stills)', 'low sun 12 deg (231 launch)']:
        lines.append(f'MEAN distance to theirs, {grp} (lower = closer; that still = 1.00):')
        for (g, k), v in sorted(gap_rows.items(), key=lambda kv: np.mean(kv[1])):
            if g == grp: lines.append(f'  {k:<5} {np.mean(v):.3f}   per pose ' + ' '.join(f'{x:.2f}' for x in v))
    lines.append('LOW SUN ALONE (no post factor): raw distance to theirs, noon -> 12 deg:')
    for n in ('forest_eye', 'jungle_eye'):
        if 'lowsun_' + n in ours_d: lines.append(f'  {n:<12} {ours_d[n]:.3f} -> {ours_d["lowsun_" + n]:.3f}  ({ours_d["lowsun_" + n] / ours_d[n]:.2f}x)')
    lines.append('')

    # reverse test on theirs
    ourmean = None
    om = []
    for name, path in ours.items():
        if name.startswith('lowsun_'): continue
        a = load(path); hud = hud_mask(a); om.append(metrics(a, sky_mask_ours(a) * (1 - hud), hud))
    ourmean = {m: float(np.nanmean([x[m] for x in om])) for m in METRICS}
    lines.append(row('OURS mean (226 poses)', ourmean))
    lines.append('REVERSE: their shot with one factor removed; "toward ours" = distance to OUR mean, each metric scaled by its spread over the 6 originals (theirs = 1.00)')
    lines.append(f'{"":<26}' + ''.join(f'{m:>10}' for m in METRICS) + '   toward-ours distance')
    rev_d = {}
    for k, a in theirs.items():
        sk = sky_mask_theirs(a); z = np.zeros(sk.shape, np.float32)
        res = {'theirs': a}
        for key, _, f in REVERSE: res[key] = f(a, sk)
        res['all_removed'] = all_removed(a, sk)
        base = metrics(a, sk, z)
        for kk, v in res.items():
            skk = sk
            mm = metrics(v, skk, z)
            dist = zdist(mm, ourmean) / zdist(base, ourmean)
            rev_d.setdefault(kk, []).append(dist)
            lines.append(row(f'  {k}:{kk}', mm) + f'   {dist:.2f}')
        lab = {'theirs': f'THEIRS {k}', 'flat': '- grade flattened', 'bluesky': '- sky made our blue', 'nosplit': '- warm/cool split killed',
               'crush': '- shade crushed (our dark shade)', 'smooth': '- fine texture smoothed', 'all_removed': 'ALL REMOVED'}
        tiles = [tile(res[x], lab[x], (640, 360)) for x in ['theirs', 'flat', 'bluesky', 'nosplit', 'crush', 'smooth', 'all_removed']]
        sheet(tiles, 4, (640, 360)).save(os.path.join(OUT, f'reverse_{k}.png'))
        for kk in res: save(res[kk], os.path.join(OUT, f'rev_{k}_{kk}.png'))
    lines.append('MEAN toward-ours distance over their 3 shots (lower = looks more like ours; theirs = 1.00):')
    for k, v in sorted(rev_d.items(), key=lambda kv: np.mean(kv[1])):
        lines.append(f'  {k:<12} {np.mean(v):.3f}   per shot ' + ' '.join(f'{x:.2f}' for x in v))

    # the sky masks, for checking
    for name, path in list(ours.items())[:3]:
        a = load(path); m = sky_mask_ours(a) * (1 - hud_mask(a))
        save(mix(a * 0.4, np.array([1, 0, 1], np.float32) * np.ones_like(a), m * 0.8), os.path.join('/tmp/t231', f'skymask_{name}.png'))
    for k, a in theirs.items():
        m = sky_mask_theirs(a); save(mix(a * 0.4, np.array([1, 0, 1], np.float32) * np.ones_like(a), m * 0.8), os.path.join('/tmp/t231', f'skymask_th_{k}.png'))

    open(REPORT, 'w').write('\n'.join(lines) + '\n'); print('\n'.join(lines))
    # viewer
    poses = [n for n in ours]
    html = """<html><body style="background:#111;color:#ddd;font-family:sans-serif">
<h3>Task 231: arrows = pose; Space = flip ours / ALL FIVE; 1-7 = ours a b c d e all; Z = full size</h3>
<div id=t></div><img id=i style="max-width:100%"><script>
var p=__POSES__,a=['ours','a','b','c','d','e','all'],k=0,m=0,z=0;
function s(){var f='full_'+p[k]+'_'+a[m]+'.png';var i=document.getElementById('i');i.src=f;i.style.maxWidth=z?'none':'100%%';
document.getElementById('t').textContent=(k+1)+'/'+p.length+'  '+p[k]+'  --  '+a[m];}
document.onkeydown=function(e){if(e.key==' '){m=(m==0)?6:0;}else if(e.key>='1'&&e.key<='7'){m=+e.key-1;}else if(e.key=='ArrowRight'){k=(k+1)%%p.length;}else if(e.key=='ArrowLeft'){k=(k+p.length-1)%%p.length;}else if(e.key=='z'||e.key=='Z'){z=1-z;}s();e.preventDefault();};s();
</script></body></html>""".replace('%%','%').replace('__POSES__', str(poses))
    open(os.path.join(OUT, 'viewer.html'), 'w').write(html)

if __name__ == '__main__':
    main()
