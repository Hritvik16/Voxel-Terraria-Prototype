#!/usr/bin/env python3
"""palette-page.py <render dir> -- writes <render dir>/index.html, the round 2
palette page, from index.json (palette-render.py). Images are referenced
relatively (<seed dir>/<scene>_<palette>.jpg, swatches_<palette>.png), so the
folder publishes as-is.
"""
import html
import json
import os
import sys

CSS = """
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Pixelify+Sans:wght@500;700&family=IBM+Plex+Sans:wght@400;600&family=IBM+Plex+Mono:wght@400;500&display=swap">
<style>
  :root {
    color-scheme: dark;
    --ground: #17181a; --panel: #202226; --line: #34373d; --ink: #e8e6e1; --muted: #a09d97;
    --accent: #e8a95b;
    --display: "Pixelify Sans", "IBM Plex Mono", monospace;
    --body: "IBM Plex Sans", system-ui, sans-serif;
    --mono: "IBM Plex Mono", ui-monospace, monospace;
  }
  body { background: var(--ground); color: var(--ink); font-family: var(--body); font-size: 15.5px; line-height: 1.6; margin: 0; }
  .wrap { max-width: 1500px; margin: 0 auto; padding-inline: 20px; padding-block: 32px 72px; display: grid; gap: 34px; }
  h1 { font-family: var(--display); font-weight: 700; font-size: clamp(28px, 4.2vw, 44px); line-height: 1.1; margin: 0; text-wrap: balance; }
  h2 { font-family: var(--display); font-weight: 500; font-size: 26px; margin: 0; text-wrap: balance; }
  h3 { font-size: 13px; text-transform: uppercase; letter-spacing: .09em; color: var(--muted); margin: 0; font-weight: 600; }
  p { margin: 0 0 10px; max-width: 76ch; }
  .muted { color: var(--muted); }
  .lede { font-size: 17px; max-width: 76ch; }
  code { font-family: var(--mono); font-size: 13px; background: #2a2c31; padding: 1px 5px; border-radius: 3px; }
  .cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 360px), 1fr)); gap: 14px; }
  .card { background: var(--panel); border: 1px solid var(--line); border-radius: 8px; padding: 14px 16px; display: grid; gap: 8px; align-content: start; }
  .card .k { font-family: var(--display); font-size: 22px; }
  .card img { width: 100%; border-radius: 4px; display: block; }
  .bar { position: sticky; top: env(safe-area-inset-top, 0px); z-index: 5; background: rgba(23,24,26,.94); backdrop-filter: blur(6px);
         border-bottom: 1px solid var(--line); padding-block: 10px; display: flex; flex-wrap: wrap; gap: 8px 18px; align-items: center; }
  .seg { display: inline-flex; border: 1px solid var(--line); border-radius: 999px; overflow: hidden; flex-wrap: wrap; }
  .seg button { background: transparent; color: var(--ink); border: 0; padding: 6px 13px; font: 600 13px var(--body); cursor: pointer; }
  .seg button[aria-pressed="true"] { background: var(--accent); color: #1b1408; }
  .seg button:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }
  .seed { display: grid; gap: 22px; }
  .scene { display: grid; gap: 8px; }
  .scene .title { display: flex; gap: 10px; align-items: baseline; flex-wrap: wrap; }
  .row { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 8px; }
  .row.single { grid-template-columns: minmax(0, 1fr); }
  figure { margin: 0; display: grid; gap: 4px; }
  figure img { width: 100%; border-radius: 5px; display: block; border: 1px solid var(--line); background: #000; cursor: zoom-in; aspect-ratio: 16 / 9; object-fit: cover; }
  figcaption { font-size: 12.5px; color: var(--muted); font-family: var(--mono); }
  @media (max-width: 760px) { .row { grid-template-columns: minmax(0, 1fr); } }
  .lb { position: fixed; inset: 0; background: rgba(8,8,9,.96); z-index: 20; display: grid; grid-template-rows: auto 1fr auto; padding: 12px 16px; gap: 8px; }
  .lb img { width: 100%; height: 100%; object-fit: contain; min-height: 0; }
  .lb .top { display: flex; justify-content: space-between; gap: 12px; align-items: center; flex-wrap: wrap; }
  .lb button { background: var(--panel); color: var(--ink); border: 1px solid var(--line); border-radius: 6px; padding: 6px 12px; font: 600 13px var(--body); cursor: pointer; }
  .callout { background: var(--panel); border: 1px solid var(--line); border-left: 3px solid var(--accent); border-radius: 6px; padding: 12px 16px; }
  ul { margin: 0 0 10px; padding-left: 20px; max-width: 78ch; }
  li { margin-bottom: 5px; }
</style>
"""


def main():
    root = sys.argv[1]
    idx = json.load(open(os.path.join(root, "index.json")))
    pals = list(idx["palettes"].keys())
    P = idx["palettes"]
    out = ["<title>Palette Directions</title>", CSS, '<div class="wrap">']
    out.append('<header style="display:grid;gap:12px">'
               '<span class="muted" style="font-family:var(--mono);font-size:13px">branch world-terrain-overhaul · 2026-09-25 · palette question, round 2</span>'
               '<h1>Three palettes on real terrain, four seeds</h1>'
               '<p class="lede">Pastel is gone. <strong>D, Gouache Frontier</strong> replaces it. Every picture below is <em>real generated terrain</em>: the voxels come from '
               'the same generator function a chunk will be built from, for four world seeds, and each one is ray-traced with sun shadows. '
               'In each row the geometry, camera and sun are identical. Only the palette changes.</p></header>')
    out.append('<section class="cards">')
    for k in pals:
        out.append(f'<div class="card"><span class="k">{k} · {html.escape(P[k]["name"])}</span><span>{html.escape(P[k]["desc"])}</span>'
                   f'<img src="swatches_{k}.png" alt="{html.escape(P[k]["name"])}: every material by biome"></div>')
    out.append('</section>')
    out.append('<section class="callout"><p><strong>How to read these.</strong> Close-ups (“at native voxel scale”) are 0.1 m voxels, which is what a player sees standing there. Blocks are 256 m of one biome at 1 m samples. '
               'The island view uses 16 m samples. Box sides are cut sections, so the strata you see there are the real rock under the surface.</p>'
               '<p style="margin:0">Lighting is plain mid-morning sun with sky fill, <em>not</em> the game\'s renderer or golden hour, so you judge the colours under ordinary light. '
               'Click any picture to see it full size; <code>←</code> <code>→</code> flip palettes on the same frame, <code>↑</code> <code>↓</code> change scene.</p></section>')
    # controls
    out.append('<div class="bar"><h3>Show</h3><div class="seg" id="view">'
               '<button aria-pressed="true" data-v="all">side by side</button>' +
               "".join(f'<button aria-pressed="false" data-v="{k}">{k} only</button>' for k in pals) +
               '</div><h3>Seed</h3><div class="seg" id="seeds"><button aria-pressed="true" data-s="all">all</button>' +
               "".join(f'<button aria-pressed="false" data-s="{s["seed"]}">{s["seed"]}</button>' for s in idx["seeds"]) +
               '</div></div>')
    for s in idx["seeds"]:
        sd = f'seed_{s["seed"]}'
        out.append(f'<section class="seed" data-seed="{s["seed"]}"><h2>Seed {s["seed"]}</h2>')
        for sc in s["scenes"]:
            out.append(f'<div class="scene"><div class="title"><strong>{html.escape(sc["title"])}</strong>'
                       f'<span class="muted" style="font-size:13px">seed {s["seed"]} · {sc["name"]}</span></div><div class="row">')
            for k in pals:
                src = f'{sd}/{sc["name"]}_{k}.jpg'
                out.append(f'<figure data-p="{k}"><img loading="lazy" src="{src}" alt="{html.escape(sc["title"])}, seed {s["seed"]}, palette {k}">'
                           f'<figcaption>{k} · {html.escape(P[k]["name"])}</figcaption></figure>')
            out.append('</div></div>')
        out.append('</section>')
    out.append('<section><h2>What I noticed looking at all 52 scenes</h2><ul>'
               '<li><strong>A</strong> reads as geology: the strata stay legible without shouting, and the grass is olive. The sea is dark teal, and from far away the island is the calmest of the three and the least varied.</li>'
               '<li><strong>C</strong> is the most graphic. Its grass is a saturated green over near-black loam, so wherever the surface alternates grass and soil, <em>block tops speckle</em> (look at any forest or jungle block). Its strata become bold stripes.</li>'
               '<li><strong>D</strong> sits between the two. Materials are recognisable at a glance (green grass, red sandstone, blue ice), the stripes are less stark than C\'s, and the water is a mid blue.</li>'
               '<li>Not a colour issue, but every palette shows it: on slopes the generator alternates grass, soil and exposed rock column by column. That salt-and-pepper look comes from the surface rules, and I can smooth it in the generator whichever palette you pick.</li>'
               '</ul></section>')
    out.append('<section><h2>What I\'d look at</h2><ul>'
               '<li><strong>The close-ups first.</strong> They are most of what a player sees. Check whether grass, soil and exposed rock read as three different things at 0.1 m.</li>'
               '<li><strong>The box sides and the cutaway.</strong> Each biome has five strata bands. Check whether they read as geology or as stripes.</li>'
               '<li><strong>The island view.</strong> Check whether the four biomes can be told apart from far away.</li>'
               '<li>Mixing is fine: for example, “D\'s surfaces with A\'s rock”, or “C\'s fluids and crystals on D”.</li></ul>'
               '<p class="muted">Known and not a colour issue: underground rock still switches biome along vertical planes under the surface borders (visible on some box sides). '
               'I\'ll fix that in the generator along with the palette you pick.</p></section>')
    out.append('</div>')
    out.append('<div class="lb" id="lb" hidden><div class="top"><strong id="lbt"></strong><span><button id="lbp">← palette</button> <button id="lbn">palette →</button> <button id="lbx">close (Esc)</button></span></div><img id="lbi" alt=""><div class="muted" id="lbc" style="font-size:13px"></div></div>')
    out.append("""<script>
(function(){
  const PALS = %s;
  const figs = () => Array.from(document.querySelectorAll('.seed:not([hidden]) .scene'));
  let view = 'all', seed = 'all';
  try { view = localStorage.getItem('pal_view') || 'all'; } catch (e) {}
  function apply(){
    document.querySelectorAll('#view button').forEach(b => b.setAttribute('aria-pressed', b.dataset.v === view));
    document.querySelectorAll('#seeds button').forEach(b => b.setAttribute('aria-pressed', b.dataset.s === seed));
    document.querySelectorAll('.seed').forEach(s => s.hidden = !(seed === 'all' || s.dataset.seed === seed));
    document.querySelectorAll('.row').forEach(r => {
      r.classList.toggle('single', view !== 'all');
      r.querySelectorAll('figure').forEach(f => f.hidden = !(view === 'all' || f.dataset.p === view));
    });
  }
  document.getElementById('view').addEventListener('click', e => { const b = e.target.closest('button'); if (!b) return; view = b.dataset.v; try { localStorage.setItem('pal_view', view); } catch (e) {} apply(); });
  document.getElementById('seeds').addEventListener('click', e => { const b = e.target.closest('button'); if (!b) return; seed = b.dataset.s; apply(); });
  apply();
  const lb = document.getElementById('lb'), lbi = document.getElementById('lbi'), lbt = document.getElementById('lbt'), lbc = document.getElementById('lbc');
  let cur = null, pi = 0;
  function show(){
    const f = cur.querySelectorAll('figure')[pi];
    const img = f.querySelector('img');
    lbi.src = img.src; lbi.alt = img.alt;
    lbt.textContent = cur.querySelector('.title strong').textContent + ' — ' + cur.querySelector('.title span').textContent;
    lbc.textContent = f.querySelector('figcaption').textContent;
  }
  document.addEventListener('click', e => {
    const img = e.target.closest('.row img'); if (!img) return;
    cur = img.closest('.scene'); pi = PALS.indexOf(img.closest('figure').dataset.p); lb.hidden = false; show();
  });
  function step(dp, ds){
    if (dp) pi = (pi + dp + PALS.length) %% PALS.length;
    if (ds) { const all = figs(); const i = all.indexOf(cur); cur = all[Math.min(all.length - 1, Math.max(0, i + ds))]; }
    show();
  }
  document.getElementById('lbp').onclick = () => step(-1, 0);
  document.getElementById('lbn').onclick = () => step(1, 0);
  document.getElementById('lbx').onclick = () => lb.hidden = true;
  document.addEventListener('keydown', e => {
    if (lb.hidden) return;
    if (e.key === 'Escape') lb.hidden = true;
    else if (e.key === 'ArrowRight') step(1, 0);
    else if (e.key === 'ArrowLeft') step(-1, 0);
    else if (e.key === 'ArrowDown') { e.preventDefault(); step(0, 1); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); step(0, -1); }
  });
})();
</script>""" % json.dumps(pals))
    open(os.path.join(root, "index.html"), "w").write("\n".join(out))
    print("wrote", os.path.join(root, "index.html"))


if __name__ == "__main__":
    main()
