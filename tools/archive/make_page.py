import json, sys, time, html
out = sys.argv[1]
dirs = json.load(open(out + '/index.json'))
total = sum(d['count'] for d in dirs)
def day(t): return time.strftime('%d %b', time.localtime(t))
nav = ''.join(f'<a class="chip" href="#d-{html.escape(d["dir"])}">{html.escape(d["dir"])}<span>{d["count"]}</span></a>' for d in dirs)
secs = []
for d in dirs:
    figs = []
    for s in d['sheets']:
        names = ', '.join(html.escape(n) for n in s['names'])
        figs.append(f'<figure><a href="{s["file"]}" target="_blank" rel="noopener"><img loading="lazy" src="{s["file"]}" alt="{html.escape(d["dir"])}: {names}"></a><figcaption>{names}</figcaption></figure>')
    secs.append(f'<section id="d-{html.escape(d["dir"])}"><header><h2>{html.escape(d["dir"])}</h2><p>{d["count"]} images &middot; newest {day(d["newest"])}</p></header><div class="sheets">{"".join(figs)}</div></section>')
page = f'''<title>Screenshot Archive</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Atkinson+Hyperlegible:wght@400;700&family=JetBrains+Mono:wght@400;600&display=swap">
<style>
/* Layout: one column of task folders, newest first; a wrapping chip index on top jumps to each. */
:root {{
  --bg: #f3f5f8; --surface: #ffffff; --fg: #1b2230; --muted: #5b6577; --line: #d9dee7; --accent: #2f62b8;
  --sans: "Atkinson Hyperlegible", "Helvetica Neue", Arial, sans-serif;
  --mono: "JetBrains Mono", ui-monospace, Menlo, monospace;
}}
@media (prefers-color-scheme: dark) {{ :root:not([data-theme="light"]) {{
  --bg: #12151b; --surface: #1a1f28; --fg: #e4e8ef; --muted: #98a2b3; --line: #2a3140; --accent: #7fa8ee; color-scheme: dark }} }}
:root[data-theme="dark"] {{ --bg: #12151b; --surface: #1a1f28; --fg: #e4e8ef; --muted: #98a2b3; --line: #2a3140; --accent: #7fa8ee; color-scheme: dark }}
body {{ background: var(--bg); color: var(--fg); font-family: var(--sans); font-size: 15px; line-height: 1.5 }}
.wrap {{ max-width: 1240px; margin: 0 auto; padding-inline: 16px; padding-block: 28px 64px; display: grid; gap: 28px }}
h1 {{ font-size: 1.9rem; margin: 0; text-wrap: balance }}
.lead {{ max-width: 68ch; color: var(--muted); margin: 6px 0 0 }}
.lead a, .lead code {{ color: var(--accent) }}
code {{ font-family: var(--mono); font-size: .88em }}
.index {{ display: flex; flex-wrap: wrap; gap: 6px }}
.chip {{ font-family: var(--mono); font-size: .8rem; text-decoration: none; color: var(--fg); background: var(--surface);
  border: 1px solid var(--line); border-radius: 4px; padding: 3px 8px; display: inline-flex; gap: 6px }}
.chip span {{ color: var(--muted); font-variant-numeric: tabular-nums }}
.chip:hover, .chip:focus-visible {{ border-color: var(--accent); outline: none }}
section {{ display: grid; gap: 10px; scroll-margin-top: 12px }}
section header {{ display: flex; align-items: baseline; gap: 12px; flex-wrap: wrap; border-bottom: 1px solid var(--line); padding-bottom: 4px }}
h2 {{ font-family: var(--mono); font-size: 1.15rem; margin: 0 }}
section header p {{ margin: 0; color: var(--muted); font-size: .85rem; font-variant-numeric: tabular-nums }}
.sheets {{ display: grid; grid-template-columns: repeat(auto-fill, minmax(min(100%, 380px), 1fr)); gap: 12px }}
figure {{ margin: 0; min-width: 0; display: grid; gap: 4px }}
figure img {{ width: 100%; height: auto; display: block; border: 1px solid var(--line); background: var(--surface) }}
figcaption {{ font-family: var(--mono); font-size: .72rem; color: var(--muted); overflow-wrap: anywhere }}
</style>
<div class="wrap">
  <div>
    <h1>Screenshot Archive</h1>
    <p class="lead">{total:,} screenshots from {len(dirs)} task folders, {day(min(d["newest"] for d in dirs))} to {day(max(d["newest"] for d in dirs))} 2026, kept here when <code>Screenshots/</code> left git on 13 Oct. Each sheet holds up to six shots, labelled with their file names; tasks' own contact sheets are kept larger. Click a sheet to open it full size. The text reports from the same folders (metrics, test lists) stay in git, and the upscaler videos are in <a href="https://claude.ai/artifact/CDAhSF142yvbD25AVeREWX" target="_blank" rel="noopener">Upscaler Clips</a>.</p>
  </div>
  <nav class="index" aria-label="Task folders, newest first">{nav}</nav>
  {"".join(secs)}
</div>
'''
open(out + '/index.html', 'w').write(page)
print(len(page))
