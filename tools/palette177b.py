#!/usr/bin/env python3
"""palette177b.py -- task 177b (owner's palette tweaks): before|after pairs, a sheet, the viewer.

  python3 -I tools/palette177b.py <out dir>     reads <out>/before and <out>/after (LookReview ws_*_native.png)

BEFORE = the 177 palette (HEAD 176d build); AFTER = 177b. The 5 fixed poses' BEFORE are 176d's
after shots (same build, same camera); the 3 pair poses were shot before|after in 177b.
The palette swatches palette177b_before.png / palette177b.png come from `cover177.py palette`.
"""
import json
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from cover177 import label  # noqa: E402

POSES = [
    ("pair_desert_canyonlands", "desert x canyonlands, 160 m out. Look at the steep ground (the orange patches and the ridge at right): "
                                "BEFORE cream-sandstone blotches inside the orange, AFTER one orange. The grey patches are the hollow slot's gravel (unchanged)."),
    ("pair_savanna_wetland-basin", "savanna x wetland basin, the slope at right. BEFORE dark red-earth blotches on the gold grass; "
                                   "AFTER soft tan packed-earth patches, same places, same share. The striped mountain at left is 178's thick layers (not this task)."),
    ("fixed_great-peak-0", "the great peak from 1.6 km (savanna rock since 176d). Look at the desert flank at the right edge of the peak: "
                           "BEFORE cream blotches on orange, AFTER orange with one cream band (the high slot / a 178 layer)."),
    ("pair_badlands_canyonlands", "a badlands face (canyonlands). Identical BEFORE | AFTER: plain red rock with hardpan patches, no thin stripes "
                                  "(178's thick layers already replaced them; 177b removes them from -cliffs 0 too)."),
    ("fixed_forest_river-plains", "the 'lawn' pose -- forest since 176d, so it looks the same (the arch at right differs by streaming/LOD timing, not palette)."),
    ("fixed_desert_alpine-range", "desert x alpine range camera (savanna rock since 176d): no desert steep or savanna flat in frame, so it looks the same (a check)."),
    ("fixed_range_200up", "the 600 m summit from 1.5 km (savanna rock): the same apart from streaming detail (a check)."),
    ("fixed_start_view", "the play start (forest): unchanged."),
]


def main(out):
    items = []
    for p, cap in POSES:
        pair = []
        for tag in ("after", "before"):
            src = os.path.join(out, tag, f"ws_{p}_native.png")
            if not os.path.exists(src):
                print("missing", src)
                break
            im = Image.open(src).convert("RGB")
            label(im, f"{tag.upper()}  {p}", (12, 10), 26)
            dst = f"shot_{p}_{tag}.png"
            im.save(os.path.join(out, dst))
            pair.append(dst)
        if len(pair) == 2:
            items.append([pair[0], pair[1], cap + " FULL SIZE; Space flips BEFORE | AFTER."])
    items.append(["palette177b.png", "palette177b_before.png", "THE PALETTE (rows biomes, columns slots, each cell main | patch variant; "
                  "hatched = bare rock, drawn as 178's three thick layers). Space flips to BEFORE. Look at desert STEEP (variant now bare = no cream patch) "
                  "and savanna FLAT variant (red earth -> packed earth)."])
    # sheet: the 3 changed poses + the badlands check, before | after at half size
    rows = [p for p, _ in POSES[:4]]
    w, h = 960, 540
    sheet = Image.new("RGB", (2 * w, h * len(rows)), (20, 20, 20))
    for r, p in enumerate(rows):
        for c, tag in enumerate(("before", "after")):
            f = os.path.join(out, f"shot_{p}_{tag}.png")
            if os.path.exists(f):
                sheet.paste(Image.open(f).resize((w, h), Image.LANCZOS), (c * w, r * h))
    sheet.save(os.path.join(out, "poses_sheet.png"))
    page = '''<!doctype html><meta charset=utf-8><title>Task 177b palette tweaks</title>
<style>body{margin:0;background:#222;color:#eee;font:14px sans-serif}#cap{position:fixed;top:0;left:0;right:0;background:#000c;padding:8px;z-index:2}
#box{position:absolute;top:70px;left:0;right:0;bottom:0;overflow:auto}img{display:block;image-rendering:pixelated}</style>
<div id=cap></div><div id=box><img id=im></div><script>
const L=%s;let i=0,z=0,alt=0;const im=document.getElementById('im'),cap=document.getElementById('cap');
function show(){const it=L[i];const src=(alt&&it[1])?it[1]:it[0];im.src=src;cap.textContent=(i+1)+'/'+L.length+'  '+src+(it[1]?(alt?'  [BEFORE]':'  [AFTER]'):'')+' -- '+it[2]+'   [arrows: image, Space: before/after, 1 fit, 2 100%%, 3 200%%, 4 300%%]';
im.onload=()=>{if(z==0){im.style.width='100%%';im.style.height='auto'}else{im.style.width=(im.naturalWidth*z)+'px'}}; im.onload();}
document.onkeydown=e=>{if(e.key=='ArrowRight'||e.key=='ArrowDown'){i=(i+1)%%L.length;alt=0}else if(e.key=='ArrowLeft'||e.key=='ArrowUp'){i=(i+L.length-1)%%L.length;alt=0}
else if(e.key==' ')alt^=1;else if('1234'.includes(e.key))z=+e.key-1;else return;e.preventDefault();show()};show();</script>''' % json.dumps(items)
    open(os.path.join(out, "viewer.html"), "w").write(page)
    json.dump(items, open(os.path.join(out, "pairs.json"), "w"), indent=0)
    print("wrote", os.path.join(out, "viewer.html"), len(items), "items")


if __name__ == "__main__":
    main(sys.argv[1])
