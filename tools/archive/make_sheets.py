import os, sys, json
from PIL import Image, ImageDraw, ImageFont
# Usage: make_sheets.py Screenshots OUTDIR [DIR ...]  -- only the named task folders (all if none);
# merges into OUTDIR/index.json (copy tools/archive/index.json there first), so folders deleted locally stay listed.
root, out = sys.argv[1], sys.argv[2]
only = set(sys.argv[3:])
os.makedirs(os.path.join(out, 'sheets'), exist_ok=True)
old = json.load(open(os.path.join(out, 'index.json'))) if os.path.exists(os.path.join(out, 'index.json')) else []
TW, COLS, PER = 560, 2, 6
font = ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial.ttf', 15)
dirs = []
for d in sorted(os.listdir(root)):
    p = os.path.join(root, d)
    if not os.path.isdir(p) or (only and d not in only): continue
    imgs = []
    for dp, _, fs in os.walk(p):
        for f in sorted(fs):
            if f.lower().endswith(('.png', '.jpg', '.jpeg')):
                imgs.append(os.path.join(dp, f))
    if not imgs: continue
    newest = max(os.path.getmtime(i) for i in imgs)
    sheets = []
    big = [i for i in imgs if os.path.basename(i).lower().startswith(('sheet', 'contact', 'grid'))]
    imgs_small = [i for i in imgs if i not in big]
    for n, i in enumerate(big):
        try:
            im = Image.open(i).convert('RGB')
        except Exception:
            continue
        im.thumbnail((1400, 4000))
        fn = f'{d}_big{n + 1:02d}.jpg'
        im.save(os.path.join(out, 'sheets', fn), quality=78, optimize=True)
        sheets.append({'file': 'sheets/' + fn, 'names': [os.path.relpath(i, p)]})
    imgs_all = imgs; imgs = imgs_small
    for k in range(0, len(imgs), PER):
        chunk = imgs[k:k+PER]; thumbs = []
        for i in chunk:
            try:
                im = Image.open(i).convert('RGB')
            except Exception:
                continue
            im.thumbnail((TW, TW))
            thumbs.append((os.path.relpath(i, p), im))
        if not thumbs: continue
        cellh = max(t[1].height for t in thumbs) + 24
        rows = (len(thumbs) + COLS - 1) // COLS
        W = TW * min(COLS, len(thumbs)) + 8 * (min(COLS, len(thumbs)) - 1)
        sheet = Image.new('RGB', (W, rows * cellh + 8 * (rows - 1)), (24, 26, 30))
        dr = ImageDraw.Draw(sheet)
        for n, (name, im) in enumerate(thumbs):
            x = (n % COLS) * (TW + 8); y = (n // COLS) * (cellh + 8)
            sheet.paste(im, (x, y + 22))
            dr.text((x + 2, y + 3), name[:70], fill=(230, 230, 230), font=font)
        fn = f'{d}_{k // PER + 1:02d}.jpg'
        sheet.save(os.path.join(out, 'sheets', fn), quality=72, optimize=True)
        sheets.append({'file': 'sheets/' + fn, 'names': [t[0] for t in thumbs]})
    dirs.append({'dir': d, 'count': len(imgs_all), 'newest': newest, 'sheets': sheets})
names = {x['dir'] for x in dirs}
dirs += [x for x in old if x['dir'] not in names]
dirs.sort(key=lambda x: -x['newest'])
json.dump(dirs, open(os.path.join(out, 'index.json'), 'w'))
print(len(dirs), 'dirs', sum(len(x['sheets']) for x in dirs), 'sheets')
