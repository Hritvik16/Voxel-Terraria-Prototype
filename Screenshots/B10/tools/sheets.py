import os
from PIL import Image, ImageDraw, ImageFont
B = "Screenshots/B10"
f = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 22)
TW, TH = 640, 360
def sheet(rows, cols, path, title):
    img = Image.new("RGB", (160 + TW * len(cols), 50 + TH * len(rows)), (30, 30, 30))
    d = ImageDraw.Draw(img); d.text((8, 2), title.replace(", ", "\n", 1), fill=(255, 255, 255), font=ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 16))
    for j, (cl, _) in enumerate(cols): d.text((170 + j * TW, 12), cl, fill=(255, 255, 255), font=f)
    for i, (rl, name) in enumerate(rows):
        d.text((8, 50 + i * TH + TH // 2 - 30), rl, fill=(255, 255, 255), font=f)
        for j, (_, cand) in enumerate(cols):
            p = f"{B}/cand_{cand}/{name}.png"
            if os.path.exists(p): img.paste(Image.open(p).convert("RGB").resize((TW, TH)), (160 + j * TW, 50 + i * TH))
            else: d.text((170 + j * TW, 50 + i * TH + 150), "missing", fill=(255, 80, 80), font=f)
    img.save(path)
cols = [("a: Golden straw / Terracotta (SHIPPED)", "a"), ("b: Olive savanna / Painted hills", "b"), ("c: Tawny / Pale clay", "c")]
for tod in ("noon", "golden"):
    rows = [("savanna\ngentle", f"sav_gentle_{tod}"), ("savanna\nbroken", f"sav_broken_{tod}"),
            ("badlands\ngentle", f"bad_gentle_{tod}"), ("badlands\nbroken", f"bad_broken_{tod}")]
    sheet(rows, cols, f"{B}/candidates_{tod}.png", f"seed 42, 2 m eye, {tod}")
sheet([("savanna\nfar 60 m", "sav_far_noon"), ("badlands\nfar 60 m", "bad_far_noon")],
      [("noon", "a")], f"{B}/_tmp.png", "")
# far views: shipped a, noon | golden
img = Image.new("RGB", (160 + TW * 2, 50 + TH * 2), (30, 30, 30)); d = ImageDraw.Draw(img)
d.text((8, 2), "shipped a,\nfar band", fill=(255, 255, 255), font=f)
for j, tod in enumerate(("noon", "golden")):
    d.text((170 + j * TW, 12), tod, fill=(255, 255, 255), font=f)
    for i, (rl, nm) in enumerate((("savanna\n60 m up", "sav_far"), ("badlands\n60 m up", "bad_far"))):
        if j == 0: d.text((8, 50 + i * TH + TH // 2 - 30), rl, fill=(255, 255, 255), font=f)
        p = f"{B}/cand_a/{nm}_{tod}.png"
        if os.path.exists(p): img.paste(Image.open(p).convert("RGB").resize((TW, TH)), (160 + j * TW, 50 + i * TH))
img.save(f"{B}/far_views_a.png"); os.remove(f"{B}/_tmp.png")
print("ok")
