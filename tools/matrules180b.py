#!/usr/bin/env python3
"""tools/matrules180b.py -- task 180b (material rules prototype): ON | OFF comparisons.

  python3 -I tools/matrules180b.py <out dir> <ON LookReview dir> <OFF LookReview dir>
      per pose: <pose>_on.png, <pose>_off.png (full size, labelled), <pose>_compare.png (ON left | OFF right,
      full size side by side), and viewer.html (Space flips ON/OFF, arrows change pose, Z zoom; paths relative).
      The dug-pit cut-aways (MaterialRuleChunkTests.CutAway_Pits, already ON left | OFF right) are added as
      extra viewer rows when <out dir>/pit_*.png exist.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from PIL import Image  # noqa: E402

POSES = [
    ("fixed_cliff_forest", "Forest cliff, centre face: ON is one granite brown (OFF's stripes and blue patches gone); the grey faces behind are the next biome's schist."),
    ("fixed_cliff_desert", "Desert cliff: ON orange sandstone top to bottom; OFF's red lower half and tan stripes are gone. Grey-blue spots on ledges = gravel rubble."),
    ("fixed_cliff_snow", "Snow cliff: ON face is silver schist. The black box left of centre is NOT a hole (0 air voxels differ): deep blue slate in shade, in brick steps -- a defect."),
    ("fixed_bank", "River bank: ON has a dark mud collar hugging the water only; OFF had grey gravel patches."),
    ("fixed_border_bank", "River in a canyon at a biome border: ON green / mud collar on the near bank, bank material changes in patches, not one step."),
    ("fixed_beach", "Beach: sand ring round the water in both. Far hills left: ON shows a brick-sized red / yellow / blue mosaic in the shells (soil, subsoil, deep stone exposed) -- a defect."),
    ("fixed_oasis_or_swamp", "Desert river: ON grows a green grass + mud collar (the oasis rule); OFF is sand to the water."),
    ("fixed_scree", "Scree: ON pale gravel apron at the cliff foot (centre). The dark-blue / black boxes on the face above are deep blue slate in brick steps (same defect as the snow cliff)."),
]
ARMS = [("on", "ON  (-matrules 1)"), ("off", "OFF (today, -matrules 0)")]
PITS = "pit_"

VIEWER = """<!doctype html><html><head><meta charset="utf-8"><title>Task 180b: keep the new material rules?</title>
<style>body{margin:0;background:#111;color:#eee;font:15px -apple-system,Helvetica,sans-serif}
#bar{padding:8px 12px;background:#222}#wrap{overflow:auto;height:calc(100vh - 84px)}
img{display:block;image-rendering:pixelated}b{color:#ffcc55}#err{color:#ff6666}</style></head><body>
<div id="bar"><b id="t"></b> &nbsp; <span id="c"></span> <span id="err"></span><br>
Keys: left/right = pose, Space = flip ON / OFF, 1 / 2 = ON / OFF, Z = zoom (fit width | 100 %%).
Question: <b>keep the new material rules as the default?</b></div>
<div id="wrap"><img id="im"></div>
<script>
const S=%s;let i=0,a=0,fit=true;
const im=document.getElementById('im');
im.onerror=()=>{document.getElementById('err').textContent=' MISSING FILE: '+im.getAttribute('src');};
function show(){const s=S[i];document.getElementById('err').textContent='';const k=Math.min(a,s.files.length-1);
im.src=s.files[k];document.getElementById('t').textContent=(i+1)+'/'+S.length+'  '+s.name+'  ['+s.arms[k]+']';
document.getElementById('c').textContent=s.caption;im.style.width=fit?'100%%':'auto';}
document.onkeydown=e=>{const k=e.key.toLowerCase();
if(k=='arrowright')i=(i+1)%%S.length;else if(k=='arrowleft')i=(i+S.length-1)%%S.length;
else if(k==' '){a=1-a;e.preventDefault();}else if(k=='1')a=0;else if(k=='2')a=1;
else if(k=='z')fit=!fit;show();};show();
</script></body></html>"""


def labelled(src, text):
    import cliff178
    img = Image.open(src).convert("RGB")
    cliff178.label(img, text, (14, 12), 30)
    return img


def main(out, on_dir, off_dir):
    os.makedirs(out, exist_ok=True)
    rows = []
    for pose, cap in POSES:
        imgs, files = [], []
        for (tag, name), d in zip(ARMS, (on_dir, off_dir)):
            src = os.path.join(d, f"ws_{pose}_native.png")
            if not os.path.exists(src):
                print("missing", src)
                imgs = None
                break
            img = labelled(src, f"{pose[6:]}: {name}")
            fn = f"{pose[6:]}_{tag}.png"
            img.save(os.path.join(out, fn))
            imgs.append(img)
            files.append(fn)
        if imgs is None:
            continue
        w, h = imgs[0].size
        sheet = Image.new("RGB", (2 * w + 10, h), (17, 17, 17))
        sheet.paste(imgs[0], (0, 0))
        sheet.paste(imgs[1], (w + 10, 0))
        sheet.save(os.path.join(out, f"{pose[6:]}_compare.png"))
        rows.append({"name": pose[6:], "caption": cap, "files": files, "arms": [n for _, n in ARMS]})
    for fn in sorted(os.listdir(out)):
        if fn.startswith(PITS) and fn.endswith(".png"):
            rows.append({"name": fn[:-4], "files": [fn], "arms": ["cut-away: ON left | OFF right"],
                         "caption": "Dug pit cut-away (generator, 32 m wide, 4 m above to 26 m under the ground): ON left shows "
                                    "skin, soil, subsoil, stone, deep stone of the ORIGINAL surface; OFF right is today's layered rock."})
    for r in rows:
        for f in r["files"]:
            assert os.path.exists(os.path.join(out, f)), f
    with open(os.path.join(out, "viewer.html"), "w") as f:
        f.write(VIEWER % json.dumps(rows))
    print("wrote", os.path.join(out, "viewer.html"), len(rows), "rows")


if __name__ == "__main__":
    main(*sys.argv[1:4])
