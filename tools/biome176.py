#!/usr/bin/env python3
"""Task 176 (C1): biome layout statistics and maps for the 8 layout seeds.

Reads Logs/TerrainGen/c1/<tag>/ (BiomeLayoutDump: land mask, record biome plane, height shade, offsets).
  python3 tools/biome176.py stats <tag> [<tag> ...]     the layout table per seed (from the dumped planes)
  python3 tools/biome176.py maps <before> <after> out.png [seeds]   before | after maps, seeds as rows
  python3 tools/biome176.py proto <variant>             a Python model of a candidate climate (exploration)

Statistics (all on the 8 m record grid, land = map height > 0, lakes/rivers count as land):
  share     % of land per biome (forest desert snow jungle savanna badlands)
  regions   per biome, 4-connected land patches of >= REGION_MIN_HA (smaller slivers are not a "region")
  straight  the longest straight line (any direction) that stays within STRAIGHT_TOL_M of a land biome border
            along its whole length, in m: a border that runs straight for that far
  change    land metres walked east-west per biome change (WorldRecordTests.Change, rows every 32 m)
  median    area-weighted median patch, ha (WorldRecordTests.AreaMedianHa)
"""
import sys, os, math
import numpy as np

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
C1 = os.path.join(ROOT, 'Logs', 'TerrainGen', 'c1')
NAMES = ['forest', 'desert', 'snow', 'jungle', 'savanna', 'badlands']
COL = np.array([(64, 140, 64), (235, 204, 128), (235, 240, 250), (26, 115, 77), (201, 166, 64), (194, 87, 59)], np.float32)
REGION_MIN_HA = 25.0
STRAIGHT_TOL_M = 30.0
CELL_M = 8.0


def load(tag):
    d = os.path.join(C1, tag)
    meta = {}
    for line in open(os.path.join(d, 'meta.txt')):
        f = line.split()
        if not f: continue
        seed = int(f[0]); n = int(f[8])
        meta[seed] = dict(tempA=(float(f[1]), float(f[2])), moistA=(float(f[3]), float(f[4])), scale=float(f[5]),
                          radius=float(f[6]), cellVoxels=int(f[7]), n=n,
                          land=np.fromfile(os.path.join(d, f'seed{seed}_land.bin'), np.uint8).reshape(n, n),
                          biome=np.fromfile(os.path.join(d, f'seed{seed}_biome.bin'), np.uint8).reshape(n, n),
                          height=np.fromfile(os.path.join(d, f'seed{seed}_height.bin'), np.uint8).reshape(n, n))
    return meta


def components(mask):
    """4-connected labels of a bool mask (iterative union-find on runs, numpy-light)."""
    n0, n1 = mask.shape
    lab = np.zeros(mask.shape, np.int32)
    parent = [0]
    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]; a = parent[a]
        return a
    nxt = 1
    for z in range(n0):
        row = mask[z]
        if not row.any(): continue
        idx = np.flatnonzero(row)
        # runs
        breaks = np.flatnonzero(np.diff(idx) != 1) + 1
        starts = np.concatenate(([0], breaks)); ends = np.concatenate((breaks, [len(idx)]))
        prev = lab[z - 1] if z > 0 else None
        for s, e in zip(starts, ends):
            x0, x1 = idx[s], idx[e - 1] + 1
            l = nxt; parent.append(nxt); nxt += 1
            if prev is not None:
                above = np.unique(prev[x0:x1]); above = above[above > 0]
                for a in above:
                    ra, rl = find(int(a)), find(l)
                    if ra != rl: parent[max(ra, rl)] = min(ra, rl)
            lab[z, x0:x1] = l
    roots = np.array([find(i) for i in range(nxt)], np.int32)
    return roots[lab]


def dilate(mask, r):
    out = mask.copy()
    for dz in range(-r, r + 1):
        for dx in range(-r, r + 1):
            if dx * dx + dz * dz > r * r or (dx == 0 and dz == 0): continue
            out |= np.roll(np.roll(mask, dz, 0), dx, 1)
    return out


def border_mask(land, biome):
    L = land > 0
    b = np.zeros(L.shape, bool)
    for ax in (0, 1):
        a = L & np.roll(L, 1, ax) & (biome != np.roll(biome, 1, ax))
        b |= a | np.roll(a, -1, ax)
    b[0, :] = b[-1, :] = b[:, 0] = b[:, -1] = False
    return b


def straight_run(land, biome, rng_seed=1, starts=2500, dirs=48, max_m=6000.0):
    """Longest straight segment staying within STRAIGHT_TOL_M of a land border; returns (m, (x0,z0,x1,z1) cells)."""
    bm = border_mask(land, biome)
    r = int(round(STRAIGHT_TOL_M / CELL_M))
    band = dilate(bm, r)
    pts = np.argwhere(bm)
    if len(pts) == 0: return 0.0, None
    rng = np.random.default_rng(rng_seed)
    if len(pts) > starts: pts = pts[rng.choice(len(pts), starts, replace=False)]
    n = land.shape[0]
    ang = np.arange(dirs) * (math.pi / dirs)
    dx, dz = np.cos(ang), np.sin(ang)
    steps = int(max_m / 2 / CELL_M)
    best, seg = 0.0, None
    z0 = pts[:, 0][:, None].astype(np.float32); x0 = pts[:, 1][:, None].astype(np.float32)
    lens = np.zeros((len(pts), dirs, 2), np.float32)
    for k, sgn in enumerate((1.0, -1.0)):
        alive = np.ones((len(pts), dirs), bool)
        for s in range(1, steps + 1):
            xs = np.rint(x0 + sgn * s * dx[None, :]).astype(np.int32); zs = np.rint(z0 + sgn * s * dz[None, :]).astype(np.int32)
            ok = (xs >= 0) & (xs < n) & (zs >= 0) & (zs < n)
            inb = np.zeros_like(ok)
            inb[ok] = band[zs[ok], xs[ok]]
            alive &= inb
            lens[:, :, k] += alive
            if not alive.any(): break
    tot = (lens[:, :, 0] + lens[:, :, 1]) * CELL_M
    i, j = np.unravel_index(np.argmax(tot), tot.shape)
    best = float(tot[i, j])
    a, b = lens[i, j, 0], lens[i, j, 1]
    seg = (x0[i, 0] + a * dx[j], z0[i, 0] + a * dz[j], x0[i, 0] - b * dx[j], z0[i, 0] - b * dz[j])
    return best, seg


def stats(land, biome):
    L = land > 0
    nl = L.sum()
    ha = CELL_M * CELL_M / 1e4
    share = [100.0 * (L & (biome == b)).sum() / nl for b in range(6)]
    regions = []
    for b in range(6):
        m = L & (biome == b)
        if not m.any(): regions.append(0); continue
        lab = components(m)
        cnt = np.bincount(lab[m])
        regions.append(int((cnt * ha >= REGION_MIN_HA).sum()))
    # change E-W (rows every 4 cells)
    a = biome[::4, :-1]; b2 = biome[::4, 1:]; la = L[::4, :-1] & L[::4, 1:]
    walked = la.sum(); ch = (la & (a != b2)).sum()
    change = walked * CELL_M / ch if ch else float('inf')
    # area-weighted median patch
    sizes = []
    for b in range(6):
        m = L & (biome == b)
        if not m.any(): continue
        lab = components(m); cnt = np.bincount(lab[m]); sizes += [c for c in cnt if c > 0]
    sizes.sort(); tot = sum(sizes); acc = 0; med = 0
    for v in sizes:
        acc += v
        if acc >= tot * 0.5: med = v * ha; break
    st, seg = straight_run(land, biome)
    return dict(share=share, regions=regions, change=change, median=med, straight=st, seg=seg, land_km2=nl * ha / 100)


def table(tag, meta):
    lines = [f'== {tag}: shares % of land (F D S J Sv B) | regions >= {REGION_MIN_HA:.0f} ha per biome | longest straight border (tol {STRAIGHT_TOL_M:.0f} m) | change E-W m | area-wtd median ha']
    res = {}
    for seed, m in meta.items():
        s = stats(m['land'], m['biome']); res[seed] = s
        lines.append(f"  {seed:<6} land {s['land_km2']:5.1f} km2  " + ' '.join(f'{v:4.1f}' for v in s['share']) +
                     ' | ' + ' '.join(str(r) for r in s['regions']) + f" | {s['straight']:5.0f} m | {s['change']:5.0f} | {s['median']:5.0f}")
    return '\n'.join(lines), res


def render(m, segs=None, px=512):
    n = m['n']; k = n // px
    land = m['land'][::k, ::k]; biome = m['biome'][::k, ::k]; h = m['height'][::k, ::k].astype(np.float32)
    img = COL[np.clip(biome, 0, 5)].copy()
    # hill shade from the height shade (NW light)
    gx = np.roll(h, -1, 1) - np.roll(h, 1, 1); gz = np.roll(h, -1, 0) - np.roll(h, 1, 0)
    sh = np.clip(1.0 + 0.04 * (-gx - gz), 0.55, 1.3)
    img *= sh[:, :, None]
    img[land == 0] = (30, 70, 140)
    img[land == 2] = (90, 150, 220)
    bm = border_mask(m['land'], m['biome'])[::k, ::k]
    img[bm] = img[bm] * 0.45
    img = np.clip(img, 0, 255).astype(np.uint8)
    if segs is not None:
        x0, z0, x1, z1 = [v / k for v in segs]
        for t in np.linspace(0, 1, 400):
            x = int(round(x0 + (x1 - x0) * t)); z = int(round(z0 + (z1 - z0) * t))
            for d in (-1, 0, 1):
                if 0 <= z + d < px and 0 <= x < px: img[z + d, x] = (255, 0, 255)
    return img[::-1]   # z up (north up)


def maps(before, after, out, seeds=None, P=512):
    from PIL import Image, ImageDraw
    A, B = load(before), load(after)
    seeds = seeds or list(A.keys())[:4]
    G, H = 10, 22
    W = 2 * P + G
    canvas = Image.new('RGB', (W, len(seeds) * (P + H + G)), (255, 255, 255))
    dr = ImageDraw.Draw(canvas)
    for r, seed in enumerate(seeds):
        for c, (tag, M) in enumerate(((before, A), (after, B))):
            s = stats(M[seed]['land'], M[seed]['biome'])
            img = Image.fromarray(render(M[seed], s['seg'], P))
            y = r * (P + H + G)
            canvas.paste(img, (c * (P + G), y + H))
            dr.text((c * (P + G) + 4, y + 4), f"seed {seed} {tag}: longest straight border {s['straight']:.0f} m (magenta), regions F/D/S/J/Sv/B " +
                    '/'.join(str(v) for v in s['regions']) + f", change E-W {s['change']:.0f} m", fill=(0, 0, 0))
    canvas.save(out)
    print('wrote', out)


if __name__ == '__main__':
    cmd = sys.argv[1]
    if cmd == 'stats':
        for tag in sys.argv[2:]:
            t, _ = table(tag, load(tag)); print(t)
    elif cmd == 'maps':
        seeds = [int(s) for s in sys.argv[5].split(',')] if len(sys.argv) > 5 else None
        maps(sys.argv[2], sys.argv[3], sys.argv[4], seeds, int(sys.argv[6]) if len(sys.argv) > 6 else 512)


# ---------------------------------------------------------------- prototype climate (exploration only)
def snoise(vx, vz):
    """Unity.Mathematics noise.snoise(float2) (Ashima / stegu), float64."""
    C = (0.211324865405187, 0.366025403784439, -0.577350269189626, 0.024390243902439)
    s = (vx + vz) * C[1]
    ix = np.floor(vx + s); iz = np.floor(vz + s)
    t = (ix + iz) * C[0]
    x0 = vx - ix + t; z0 = vz - iz + t
    i1x = (x0 > z0).astype(np.float64); i1z = 1.0 - i1x
    x1 = x0 + C[0] - i1x; z1 = z0 + C[0] - i1z
    x2 = x0 + C[2]; z2 = z0 + C[2]
    mod = lambda a: a - np.floor(a * (1.0 / 289.0)) * 289.0
    perm = lambda a: mod((a * 34.0 + 1.0) * a)
    ix = mod(ix); iz = mod(iz)
    p0 = perm(perm(iz) + ix); p1 = perm(perm(iz + i1z) + ix + i1x); p2 = perm(perm(iz + 1.0) + ix + 1.0)
    out = 0.0
    for p, x, z in ((p0, x0, z0), (p1, x1, z1), (p2, x2, z2)):
        m = np.maximum(0.5 - (x * x + z * z), 0.0); m = m * m; m = m * m
        gx = 2.0 * (p * C[3] - np.floor(p * C[3])) - 1.0
        h = np.abs(gx) - 0.5
        a0 = gx - np.floor(gx + 0.5)
        m = m * (1.79284291400159 - 0.85373472095314 * (a0 * a0 + h * h))
        out = out + m * (a0 * x + h * z)
    return 130.0 * out


CENTRES = np.array([(0, 0), (0.8, -0.8), (-0.9, 0.1), (0.8, 0.8), (0.8, 0), (-0.3, -0.8)], np.float64)


def climate_biome(t, m):
    d = (t[..., None] - CENTRES[:, 0]) ** 2 + (m[..., None] - CENTRES[:, 1]) ** 2
    return np.argmin(d, axis=-1).astype(np.uint8)


def proto_plane(M, wt, wm, amp, oct2=0.0, oct2mul=2.03, n=1024):
    """Biome plane on the record grid: one octave at (wt, wm) voxels plus an optional 2nd octave of relative amplitude oct2."""
    cv = M['cellVoxels']
    c = (np.arange(n) + 0.5) * cv
    X, Z = np.meshgrid(c, c)
    def field(w, off, k):
        f = snoise(X / w + off[0], Z / w + off[1])
        if oct2 > 0:
            f = f + oct2 * snoise(X / w * oct2mul + off[0] + 17.3 + 31.0 * k, Z / w * oct2mul + off[1] - 9.1 - 23.0 * k)
            f = f / (1.0 + oct2)
        return f
    t = field(wt, M['tempA'], 0) * amp; m = field(wm, M['moistA'], 1) * amp
    return climate_biome(t, m)


def proto_plane2(M, k, oct2, sigma, R=24000.0, oct2mul=2.03, n=1024, disc=1.25, ratio=1.25):
    """Climate at wavelength k*R (temperature) and k*R/ratio... both normalised to mean 0, sd sigma over the island disc."""
    cv = M['cellVoxels']
    c = (np.arange(n) + 0.5) * cv
    X, Z = np.meshgrid(c, c)
    ctr = n * cv * 0.5
    inside = (X - ctr) ** 2 + (Z - ctr) ** 2 <= (disc * R) ** 2
    def field(w, off, k2):
        f = snoise(X / w + off[0], Z / w + off[1])
        if oct2 > 0:
            f = f + oct2 * snoise(X / w * oct2mul + off[0] + 17.3 + 31.0 * k2, Z / w * oct2mul + off[1] - 9.1 - 23.0 * k2)
        mu, sd = f[inside].mean(), f[inside].std()
        return (f - mu) / sd * sigma
    t = field(k * R * ratio, M['tempA'], 0); m = field(k * R, M['moistA'], 1)
    return climate_biome(t, m)


def proto_plane3(M, k, amps, sigma, R=24000.0, n=1024, disc=1.25, ratio=1.25):
    """Fbm-style octaves (lacunarity 2.03, offsets as Noise.Fbm) with amplitudes `amps`, normalised over the island disc."""
    cv = M['cellVoxels']
    c = (np.arange(n) + 0.5) * cv
    X, Z = np.meshgrid(c, c)
    ctr = n * cv * 0.5
    inside = (X - ctr) ** 2 + (Z - ctr) ** 2 <= (disc * R) ** 2
    def field(w, off):
        px, pz = X / w + off[0], Z / w + off[1]
        f = 0.0
        for a in amps:
            f = f + a * snoise(px, pz)
            px, pz = px * 2.03 + 17.3, pz * 2.03 - 9.1
        mu, sd = f[inside].mean(), f[inside].std()
        return (f - mu) / sd * sigma
    t = field(k * R * ratio, M['tempA']); m = field(k * R, M['moistA'])
    return climate_biome(t, m)


def viewer(out_dir):
    """Screenshots/176/viewer.html: full-size, arrows = image, 1-4 = fit / 100 / 200 / 300 %, scroll to pan."""
    import json
    items = [
        ('c1_maps_4seeds.png', 'MAPS, seeds 42 / 7 / 2026 / 31337 (rows): left BEFORE (B10 climate), right AFTER (C1). Look at the straight NW-SE belts on the left vs the patchwork on the right; magenta = each map\'s longest straight border.'),
    ] + [(f'c1_map_seed{s}.png', f'MAP seed {s} at full record resolution (1 px = 8 m): left before, right after. Dark lines = biome borders, magenta = the longest straight border.') for s in (42, 7, 2026, 31337, 1, 2, 3, 99)] + [
        ('c1_optionB_k1.0_4seeds.png', 'OPTION (b), a Python model only: wavelength 1.0 x island radius instead of 0.75. Right column: bigger patches (biome change every ~480 m vs ~375 m), but about 1 seed in 2-3 then has a biome in one region only.'),
        ('pose_great-peak-0_before_after.png', 'great-peak-0, same camera: left the straight vertical ochre|red seam (173); right the seam is now red badlands | grey snow and runs diagonally, still a two-biome split across the peak.'),
        ('pose_range_200up_before_after.png', 'range from 200 m up, same camera: the peak split moves from a vertical line to a diagonal red|grey border; snow ground on the right foot.'),
        ('pose_start_walk_before_after.png', 'the play start at eye height, same camera: the start cell is now snow x alpine range (was desert); red badlands strata on the left slope, grey snow rock up the ridge.'),
        ('pose_start_view_before_after.png', 'the start from 160 m back: left ochre speckle; right red/orange badlands terraces with the snow border above as ragged patches.'),
        ('pose_snow_rolling-highlands_before_after.png', 'a border on flat ground (173: snow meets bright grass): right is now savanna | forest, and the transition is patches of metres instead of dither.'),
        ('crop_border_before_after.png', 'FULL-RES crop of the border above: top BEFORE = per-0.8 m pixel speckle; bottom AFTER = coherent patches of metres, denser toward each side.'),
    ]
    html = '''<!doctype html><meta charset=utf-8><title>Task 176 C1 biome layout</title>
<style>body{margin:0;background:#222;color:#eee;font:14px sans-serif}#cap{position:fixed;top:0;left:0;right:0;background:#000c;padding:8px;z-index:2}
#box{position:absolute;top:60px;left:0;right:0;bottom:0;overflow:auto}img{display:block;image-rendering:pixelated}</style>
<div id=cap></div><div id=box><img id=im></div><script>
const L=%s;let i=0,z=0;const im=document.getElementById('im'),cap=document.getElementById('cap');
function show(){im.src=L[i][0];cap.textContent=(i+1)+'/'+L.length+'  '+L[i][0]+' -- '+L[i][1]+'   [arrows: image, 1 fit, 2 100%%, 3 200%%, 4 300%%]';
im.onload=()=>{if(z==0){im.style.width='100%%';im.style.height='auto'}else{im.style.width=(im.naturalWidth*z)+'px'}}; im.onload();}
document.onkeydown=e=>{if(e.key=='ArrowRight'||e.key=='ArrowDown')i=(i+1)%%L.length;else if(e.key=='ArrowLeft'||e.key=='ArrowUp')i=(i+L.length-1)%%L.length;
else if('1234'.includes(e.key))z=+e.key-1;else return;e.preventDefault();show()};show();</script>''' % json.dumps(items)
    open(os.path.join(out_dir, 'viewer.html'), 'w').write(html)
    print('wrote', os.path.join(out_dir, 'viewer.html'))


if __name__ == '__main__' and sys.argv[1] == 'viewer':
    viewer(sys.argv[2])
