#!/usr/bin/env python3
"""palette-render.py <palette3d dir> <out dir> [W H] -- round 2 of the palette
question (owner, 2026-09-25: "I'd need to see the actual terrain and numerous
seeds to decide").

Input: Logs/TerrainGen/palette3d/seed_*/ (PaletteSceneExport: real generated
volumes, DetailField.Material). For every scene it runs ONE geometry pass
(tools/vxrender: exact voxel DDA, sun shadows, ambient occlusion, water depth)
and then colours that same buffer with each palette in palette_defs.ROUND2, so
the palettes differ in colour and nothing else: same voxels, same camera, same
sun. Writes <out>/<seed>/<scene>_<palette>.jpg, swatch boards, and index.json.

Lighting is a neutral mid-morning sun (40 degrees, from the south-east) with sky
fill -- chosen so colour is judged under ordinary light, not the game's golden
hour. It is not the game's renderer; it is the same voxels, lit simply.
"""
import json
import os
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor

import numpy as np
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from palette_defs import PALETTES, ROUND2, EMISSIVE, BIOMES, NAMES  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
VXR = os.path.join(HERE, "vxrender")
PX = np.dtype([("mat", "u1"), ("floor", "u1"), ("face", "u1"), ("ao", "u1"),
               ("sun", "<f4"), ("t", "<f4"), ("wd", "<f4"), ("hash", "<u4")])

SUN_AZ, SUN_EL = np.radians(135.0), np.radians(40.0)
SUN = np.array([np.cos(SUN_EL) * np.sin(SUN_AZ), np.sin(SUN_EL), np.cos(SUN_EL) * np.cos(SUN_AZ)])
SUN_COL = np.array([1.0, 0.95, 0.87]) * 2.2
SKY_COL = np.array([0.50, 0.60, 0.78]) * 0.85


def ensure_renderer():
    src = os.path.join(HERE, "vxrender.c")
    if not os.path.exists(VXR) or os.path.getmtime(VXR) < os.path.getmtime(src):
        subprocess.check_call(["clang", "-O3", "-o", VXR, src, "-lpthread", "-lm"])


def load_vxv(path):
    with open(path, "rb") as f:
        assert f.read(4) == b"VXV1"
        nx, ny, nz, sxz, sy, x0, y0, z0 = np.frombuffer(f.read(32), dtype="<i4")
        vol = np.frombuffer(f.read(), dtype=np.uint8).reshape(ny, nz, nx)
    return vol, dict(nx=int(nx), ny=int(ny), nz=int(nz), sxz=int(sxz), sy=int(sy), x0=int(x0), y0=int(y0), z0=int(z0))


def tops(vol):
    """Index of the highest solid (non-air, non-water) sample per column, -1 if none."""
    solid = (vol != 0) & (vol != 1)
    any_ = solid.any(axis=0)
    idx = vol.shape[0] - 1 - np.argmax(solid[::-1], axis=0)
    return np.where(any_, idx, -1)


def camera_for(kind, vol, h, man):
    """(cam, target, fov, cut, cutA, cutB, yaw) in metres from the volume corner."""
    sx, sy = h["sxz"] * 0.1, h["sy"] * 0.1
    Lx, Ly, Lz = h["nx"] * sx, h["ny"] * sy, h["nz"] * sx
    top = tops(vol)
    land = top >= 0
    ground = float(np.median(top[land])) * sy if land.any() else Ly * 0.3
    sea = (man["seaLevelY"] - h["y0"]) * 0.1
    yaw, pitch, dist, fov, cut, ca, cb = 45.0, 30.0, 1.5, 40.0, 0, 0, 0
    tgt_y = ground
    if kind == "island":
        pitch, dist, fov, tgt_y = 34.0, 1.05, 38.0, sea
    elif kind == "block":
        pitch, dist = 30.0, 1.45
    elif kind == "close":
        # High enough that the GROUND fills the frame: at 24 degrees the box's
        # cut sides dominated and the surface a player stands on was a strip.
        pitch, dist, fov = 46.0, 1.5, 42.0
        tgt_y = float(np.median(top[land])) * sy if land.any() else Ly * 0.5
    elif kind == "volcano":
        pitch, dist, tgt_y = 26.0, 1.25, float(np.percentile(top[land], 90)) * sy * 0.6
    elif kind == "coast":
        # Look from the sea toward the land: camera on the water side.
        water_cols = ~land | (top * sy < sea)
        zz, xx = np.mgrid[0:h["nz"], 0:h["nx"]]
        if water_cols.any() and (~water_cols).any():
            wx, wz = xx[water_cols].mean(), zz[water_cols].mean()
            lx, lz = xx[~water_cols].mean(), zz[~water_cols].mean()
            yaw = float(np.degrees(np.arctan2(lx - wx, lz - wz)))
        pitch, dist, tgt_y = 24.0, 1.4, sea + 3.0
    elif kind == "cutaway":
        cut, ca, cb = 1, h["nx"] // 2, h["nz"] // 2
        pitch, dist, tgt_y = 18.0, 1.9, Ly * 0.55
    elif kind == "underworld":
        # The box starts at the world floor; the lava sea rests 300 voxels above
        # it (DetailField.Carve). Cutting 12 m above the lava removes the
        # chamber's roof AND the rock hanging from it -- a higher cut leaves
        # stalactite stubs floating in the picture.
        cut, ca = 2, max(1, (300 + 120) // h["sy"])
        pitch, dist, tgt_y = 42.0, 1.35, ca * sy * 0.35
    L = max(Lx, Lz)
    tgt = np.array([Lx * 0.5, tgt_y, Lz * 0.5])
    yr, pr = np.radians(yaw), np.radians(pitch)
    back = np.array([-np.sin(yr) * np.cos(pr), np.sin(pr), -np.cos(yr) * np.cos(pr)])
    cam = tgt + back * L * dist
    return cam, tgt, fov, cut, ca, cb, yaw


def geometry(vxv, gbuf, W, H, cam, tgt, fov, cut, ca, cb, h):
    sx, sy = h["sxz"] * 0.1, h["sy"] * 0.1
    args = [VXR, vxv, gbuf, str(W), str(H), *map(str, cam), *map(str, tgt), str(fov), *map(str, SUN),
            str(cut), str(ca), str(cb), str(sx), str(sy), str(sx)]
    subprocess.check_call(args)
    return np.fromfile(gbuf, dtype=PX).reshape(H, W)


def srgb_to_lin(c):
    c = np.asarray(c)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def lin_to_srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def tonemap(x):
    """Tonemaps LUMINANCE only and rescales the colour, so a bright colour keeps
    its hue and saturation. A per-channel curve drifts saturated orange toward
    yellow as it brightens -- which would misreport the very palettes being
    judged."""
    lum = x @ np.array([0.2126, 0.7152, 0.0722])
    L = lum * 0.62
    Lt = (L * (2.51 * L + 0.03)) / (L * (2.43 * L + 0.59) + 0.14)
    scale = np.where(lum > 1e-6, Lt / np.maximum(lum, 1e-6), 0.0)
    y = x * scale[..., None]
    # A colour pushed past 1 in one channel is scaled back as a whole, so it
    # keeps its hue instead of clipping to a new one.
    m = y.max(axis=-1, keepdims=True)
    return np.where(m > 1.0, y / np.maximum(m, 1e-6), y)


def lut(pal):
    t = np.zeros((256, 3))
    t[:] = (1.0, 0.0, 1.0)
    for k, c in pal.items():
        t[k] = srgb_to_lin(c)
    e = np.zeros(256)
    for k, v in EMISSIVE.items():
        e[k] = v
    return t, e


def light(mat, g, t_alb, t_em):
    alb = t_alb[mat]
    jitter = 1.0 + 0.07 * ((g["hash"].astype(np.float64) / 4294967295.0) - 0.5)
    alb = alb * jitter[..., None]
    face = g["face"]
    amb_face = np.where(face == 2, 1.0, np.where(face == 3, 0.45, 0.72))   # +y, -y, sides
    ao = 0.35 + 0.65 * (g["ao"].astype(np.float64) / 255.0)
    lit = alb * (SKY_COL[None, None, :] * (amb_face * ao)[..., None] + SUN_COL[None, None, :] * g["sun"][..., None])
    return lit + alb * t_em[mat][..., None]


def compose(g, pal, kind, H, W):
    t_alb, t_em = lut(pal)
    rows = np.linspace(0, 1, H)[:, None]
    sky = (np.array([0.50, 0.64, 0.84]) * (1 - rows) + np.array([0.82, 0.87, 0.93]) * rows)   # display space
    sky = np.broadcast_to(sky[:, None, :], (H, W, 3))
    mat = g["mat"]
    out = np.zeros((H, W, 3))
    solid = (mat != 0) & (mat != 1)
    col = light(mat, g, t_alb, t_em)
    out[solid] = col[solid]
    water = mat == 1
    if water.any():
        floor = g["floor"]
        fl = light(floor, g, t_alb, t_em)
        wcol = t_alb[1] * (SKY_COL * 1.1 + SUN_COL * max(SUN[1], 0) * 0.55)
        a = 1.0 - np.exp(-g["wd"] / 2.5)
        has_floor = floor != 0
        mixed = np.where(has_floor[..., None], fl * (1 - a)[..., None] * np.array([0.55, 0.8, 0.9]) + wcol * a[..., None], wcol)
        out[water] = mixed[water]
    disp = lin_to_srgb(tonemap(out))
    hit = (mat != 0)[..., None]
    if kind == "island":
        f = (1.0 - np.exp(-np.maximum(g["t"], 0) / 9000.0))[..., None] * 0.45
        disp = disp * (1 - f) + sky * f
    disp = np.where(hit, disp, sky)
    return (np.clip(disp, 0, 1) * 255).astype(np.uint8)


def swatches(pal, font):
    w, row = 1560, 44
    im = Image.new("RGB", (w, row * len(BIOMES) + 8), (24, 25, 30))
    d = ImageDraw.Draw(im)
    for r, (bname, codes) in enumerate(BIOMES):
        y = 4 + r * row
        d.text((8, y + 12), bname, fill=(230, 226, 220), font=font)
        sw = (w - 120) // max(len(c) for _, c in BIOMES)
        for i, k in enumerate(codes):
            c = tuple(int(v * 255) for v in pal[k])
            x = 120 + i * sw
            d.rectangle([x, y, x + sw - 4, y + row - 8], fill=c)
            lum = 0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2]
            d.text((x + 4, y + row - 22), NAMES[k][:16], fill=(20, 20, 20) if lum > 140 else (235, 235, 235), font=font)
    return im


def main():
    src, out = sys.argv[1], sys.argv[2]
    W = int(sys.argv[3]) if len(sys.argv) > 3 else 1280
    H = int(sys.argv[4]) if len(sys.argv) > 4 else 720
    ensure_renderer()
    os.makedirs(out, exist_ok=True)
    try:
        font = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 12)
    except Exception:
        font = ImageFont.load_default()
    for k in ROUND2:
        swatches(PALETTES[k][2], font).save(os.path.join(out, f"swatches_{k}.png"))
    index = {"palettes": {k: {"name": PALETTES[k][0], "desc": PALETTES[k][1]} for k in ROUND2}, "seeds": []}
    seed_dirs = sorted(d for d in os.listdir(src) if d.startswith("seed_"))
    jobs = []
    for sd in seed_dirs:
        man = json.load(open(os.path.join(src, sd, "manifest.json")))
        entry = {"seed": man["seed"], "scenes": []}
        index["seeds"].append(entry)
        os.makedirs(os.path.join(out, sd), exist_ok=True)
        for sc in man["scenes"]:
            entry["scenes"].append({"name": sc["name"], "title": sc["title"], "kind": sc["kind"]})
            jobs.append((sd, man, sc))

    def run(job):
        sd, man, sc = job
        vxv = os.path.join(src, sd, sc["name"] + ".vxv")
        vol, h = load_vxv(vxv)
        cam, tgt, fov, cut, ca, cb, yaw = camera_for(sc["kind"], vol, h, man)
        gbuf = os.path.join(out, sd, sc["name"] + ".gbuf")
        g = geometry(vxv, gbuf, W, H, cam, tgt, fov, cut, ca, cb, h)
        os.remove(gbuf)
        for k in ROUND2:
            img = compose(g, PALETTES[k][2], sc["kind"], H, W)
            Image.fromarray(img).save(os.path.join(out, sd, f"{sc['name']}_{k}.jpg"), quality=86)
        hit = float((g["mat"] != 0).mean())
        return f"{sd}/{sc['name']}: {h['nx']}x{h['ny']}x{h['nz']} yaw {yaw:.0f}, {hit:.0%} of pixels hit terrain"

    # vxrender is itself 8-threaded; two at a time keeps the CPU busy without
    # thrashing an 8 GB machine.
    with ThreadPoolExecutor(max_workers=2) as ex:
        for line in ex.map(run, jobs):
            print(line, flush=True)
    json.dump(index, open(os.path.join(out, "index.json"), "w"), indent=1)


if __name__ == "__main__":
    main()
