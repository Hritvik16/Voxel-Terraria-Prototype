#!/usr/bin/env python3
"""Task 115: read -spikelog's spikes.csv (one row per frame, FrameGapProbe) and say
what the slow tail is made of.

  python3 tools/spike-report.py <spikes.csv> [<spikes.csv> ...]

Per walk: the frame histogram (<=33, 33-50, 50-100, >100 ms), the split of > 50 ms
frames into SPIKES (well above their own neighbourhood: a hitch) and SLOW STRETCHES
(the neighbourhood is slow too: a heavy view), and, for > 50 ms frames, the
co-occurring work ranked by lift over ordinary frames. With 2+ walks it also
compares the slow sectors of the circle (same sector slow in both = the view).
Counters in the CSV are running totals or this-frame values; this script differences
the totals. FrameTimingManager's fields lag the frame; the lag is found by correlation.
"""
import csv, math, sys, statistics as st

SPIKE_EXCESS_MS = 15.0   # a spike is > 50 ms AND this much above its local median
NEIGH = 15               # local median over +-15 frames, self excluded


def load(path):
    # "file.csv@N" keeps the first N rows: the probe keeps recording after the walk ends
    # (the report, the end shots), so pass the WHOLE RUN line's frame count.
    keep = None
    if '@' in path:
        path, keep = path.rsplit('@', 1)
        keep = int(keep)
    with open(path) as f:
        rows = list(csv.DictReader(f))
    if keep:
        rows = rows[:keep]
    else:
        # the walk ended where the camera stopped moving (the rows after are the report + end shots)
        end = len(rows)
        while end > 2 and (rows[end - 1]['cam_x'], rows[end - 1]['cam_z']) == (rows[end - 2]['cam_x'], rows[end - 2]['cam_z']):
            end -= 1
        rows = rows[:end - 1]
    cols = {k: [float(r[k]) for r in rows] for k in rows[0].keys()}
    return cols


def diff(v):
    return [0.0] + [max(0.0, v[i] - v[i - 1]) for i in range(1, len(v))]


def corr(a, b):
    n = min(len(a), len(b))
    if n < 3:
        return float('nan')
    ma, mb = sum(a[:n]) / n, sum(b[:n]) / n
    num = sum((a[i] - ma) * (b[i] - mb) for i in range(n))
    da = math.sqrt(sum((a[i] - ma) ** 2 for i in range(n)))
    db = math.sqrt(sum((b[i] - mb) ** 2 for i in range(n)))
    return num / (da * db) if da > 0 and db > 0 else float('nan')


def pct(v, p):
    s = sorted(v)
    return s[min(len(s) - 1, int(len(s) * p))]


def report(path, c):
    out = []
    P = out.append
    fm = c['frame_ms']
    n = len(fm)
    # drop the first 30 frames (the walk's first steps, a pose change)
    s0 = min(30, n // 10)
    idx = list(range(s0, n))
    tot = sum(fm[i] for i in idx)
    P(f"== {path}")
    P(f"frames {len(idx)}  wall {tot / 1000:.1f} s  p50 {pct([fm[i] for i in idx], .5):.1f}  "
      f"p95 {pct([fm[i] for i in idx], .95):.1f}  p99 {pct([fm[i] for i in idx], .99):.1f} ms")
    edges = [(0, 33, '<= 33'), (33, 50, '33-50'), (50, 100, '50-100'), (100, 1e9, '> 100')]
    P("histogram          frames      %   share of wall clock")
    for lo, hi, name in edges:
        sel = [i for i in idx if lo < fm[i] <= hi] if lo > 0 else [i for i in idx if fm[i] <= hi]
        P(f"  {name:>8} ms  {len(sel):9d} {100 * len(sel) / len(idx):6.1f}%  {100 * sum(fm[i] for i in sel) / tot:6.1f}%")

    # FTM lag
    best = (float('nan'), 0)
    for k in range(0, 9):
        a = [c['ftm_cpu'][i + k] for i in idx if i + k < n]
        b = [fm[i] for i in idx if i + k < n]
        r = corr(a, b)
        if not math.isnan(r) and (math.isnan(best[0]) or r > best[0]):
            best = (r, k)
    lag = best[1]
    P(f"FrameTimingManager lag {lag} frames (r(ftm cpuFrameTime, frame) = {best[0]:.3f})")

    def ftm(name, i):
        j = i + lag
        return c[name][j] if j < n else float('nan')

    # local median / spikes
    loc = []
    for i in range(n):
        nb = [fm[j] for j in range(max(0, i - NEIGH), min(n, i + NEIGH + 1)) if j != i]
        loc.append(st.median(nb))
    over50 = [i for i in idx if fm[i] > 50]
    spikes = [i for i in over50 if fm[i] - loc[i] > SPIKE_EXCESS_MS]
    slow = [i for i in over50 if i not in set(spikes)]
    excess = sum(fm[i] - loc[i] for i in spikes)
    P(f"> 50 ms frames {len(over50)} ({100 * len(over50) / len(idx):.1f}%): SPIKES (> local median + {SPIKE_EXCESS_MS:.0f} ms) "
      f"{len(spikes)} = {100 * len(spikes) / max(1, len(over50)):.0f}%,  SLOW STRETCH {len(slow)} = {100 * len(slow) / max(1, len(over50)):.0f}%")
    P(f"  spikes' excess over their neighbourhood: {excess / 1000:.2f} s = {100 * excess / tot:.1f}% of wall clock; "
      f"frames > 33 ms if every spike fell to its local median: "
      f"{100 * sum(1 for i in idx if (loc[i] if i in set(spikes) else fm[i]) > 33) / len(idx):.1f}% (now {100 * sum(1 for i in idx if fm[i] > 33) / len(idx):.1f}%)")
    loc33 = [i for i in idx if loc[i] > 33]
    P(f"  frames whose local median is > 33 ms (a slow stretch): {100 * len(loc33) / len(idx):.1f}%")

    # intervals
    base = [i for i in idx if fm[i] <= 50]
    def mean(name, sel, f=None):
        v = [(f(i) if f else c[name][i]) for i in sel]
        v = [x for x in v if not math.isnan(x)]
        return sum(v) / len(v) if v else float('nan')
    P("means (ms)            frame    pre    upd   post  streamer coalesc shells   far  ftm_main ftm_rend ftm_pres ftm_gpu")
    for name, sel in (('<= 50 ms', base), ('spikes', spikes), ('slow > 50', slow)):
        P(f"  {name:<14}" + "".join(f"{mean(k, sel):7.1f}" for k in ('frame_ms', 'pre_ms', 'upd_ms', 'post_ms')) +
          f"{mean('streamer_ms', sel):9.2f}{mean('coalescer_ms', sel):8.2f}{mean('shells_ms', sel):7.2f}{mean('far_ms', sel):6.2f}" +
          "".join(f"{mean(None, sel, lambda i, k=k: ftm(k, i)):9.1f}" for k in ('ftm_main', 'ftm_render', 'ftm_present', 'ftm_gpu')))

    # causes
    d = {k: diff(c[k]) for k in ('gc0', 'gc1', 'gc2', 'saved_total', 'shell_gen_total', 'fluid_ticks', 'rb_landed', 'light_uploads')}
    rows_ch = [0.0] + [1.0 if c['rows'][i] != c['rows'][i - 1] else 0.0 for i in range(1, n)]
    flags = {
        'GC (any gen)': lambda i: d['gc0'][i] + d['gc1'][i] + d['gc2'][i] > 0,
        'chunk admitted': lambda i: c['admitted'][i] > 0,
        'chunk evicted': lambda i: c['evicted'][i] > 0,
        'delta saved': lambda i: d['saved_total'][i] > 0,
        'clipmap upload > 0': lambda i: c['upload_bytes'][i] > 0,
        'shell chunk landed': lambda i: d['shell_gen_total'][i] > 0,
        'shell upload > 0': lambda i: c['shell_bytes'][i] > 0,
        'fluid tick issued': lambda i: d['fluid_ticks'][i] > 0,
        'fluid readback landed': lambda i: d['rb_landed'][i] > 0,
        'light buffer upload': lambda i: d['light_uploads'][i] > 0,
        'upscaler rows changed': lambda i: rows_ch[i] > 0,
        'streamer > 5 ms': lambda i: c['streamer_ms'][i] > 5,
        'shells.Update > 3 ms': lambda i: c['shells_ms'][i] > 3,
        'far band > 3 ms': lambda i: c['far_ms'][i] > 3,
        'our Update interval > 10 ms': lambda i: c['upd_ms'][i] > 10,
    }
    P("co-occurring work      in spikes        in slow>50       in <=50 ms     lift(spike)  (same frame | this or previous frame)")
    res = []
    for name, f in flags.items():
        def rate(sel, prev=False):
            if not sel:
                return 0.0
            return sum(1 for i in sel if f(i) or (prev and i > 0 and f(i - 1))) / len(sel)
        rs, rw, rb = rate(spikes), rate(slow), rate(base)
        rsp, rbp = rate(spikes, True), rate(base, True)
        lift = rs / rb if rb > 0 else (float('inf') if rs > 0 else 0)
        res.append((lift, name, rs, rw, rb, rsp, rbp))
    res.sort(key=lambda r: -(r[0] if r[0] != float('inf') else 1e9))
    for lift, name, rs, rw, rb, rsp, rbp in res:
        P(f"  {name:<26}{100 * rs:5.0f}% ({100 * rsp:3.0f}%)  {100 * rw:5.0f}%        {100 * rb:5.0f}% ({100 * rbp:3.0f}%)   {lift:6.2f}x")
    # lagged excess: an event in frame i -> mean (frame - local median) in frame i+k
    exc = [fm[i] - loc[i] for i in range(n)]
    allx = sum(exc[i] for i in idx) / len(idx)
    P(f"lagged excess (frame - local median, ms) after an event in frame i; all frames {allx:+.2f}")
    P("  event                        n     i+0    i+1    i+2    i+3    i+4   sum(0..4) - 5*all")
    for name in ('fluid tick issued', 'fluid readback landed', 'shell chunk landed', 'chunk admitted', 'chunk evicted',
                 'streamer > 5 ms', 'our Update interval > 10 ms', 'GC (any gen)', 'light buffer upload'):
        f = flags[name]
        ev = [i for i in idx if f(i) and i + 4 < n]
        if not ev:
            continue
        m = [sum(exc[i + k] for i in ev) / len(ev) for k in range(5)]
        P(f"  {name:<26}{len(ev):5d} " + "".join(f"{x:+7.2f}" for x in m) + f"   {sum(m) - 5 * allx:+7.2f}")
    # worst frames
    worst = sorted(idx, key=lambda i: -fm[i])[:12]
    P("worst 12: i frame [pre/upd/post] local | streamer shells far | adm ev upB shellB | gcD fluidD rbD lightD | ftm main/rend/pres/gpu")
    for i in worst:
        P(f"  {int(c['i'][i]):5d} {fm[i]:6.1f} [{c['pre_ms'][i]:.0f}/{c['upd_ms'][i]:.0f}/{c['post_ms'][i]:.0f}] {loc[i]:5.1f} | "
          f"{c['streamer_ms'][i]:.1f} {c['shells_ms'][i]:.1f} {c['far_ms'][i]:.1f} | {c['admitted'][i]:.0f} {c['evicted'][i]:.0f} "
          f"{c['upload_bytes'][i] / 1024:.0f}K {c['shell_bytes'][i] / 1024:.0f}K | {d['gc0'][i] + d['gc1'][i] + d['gc2'][i]:.0f} "
          f"{d['fluid_ticks'][i]:.0f} {d['rb_landed'][i]:.0f} {d['light_uploads'][i]:.0f} | "
          f"{ftm('ftm_main', i):.0f}/{ftm('ftm_render', i):.0f}/{ftm('ftm_present', i):.0f}/{ftm('ftm_gpu', i):.0f}")
    # gaps between spikes (periodic?)
    if len(spikes) > 3:
        gaps = [spikes[k] - spikes[k - 1] for k in range(1, len(spikes))]
        hist = {}
        for g in gaps:
            hist[g] = hist.get(g, 0) + 1
        top = sorted(hist.items(), key=lambda kv: -kv[1])[:8]
        P("frames between spikes (gap:count, top 8): " + "  ".join(f"{g}:{k}" for g, k in top))
    return out, (idx, fm, c, spikes)


def sectors(c, idx, fm, spikes, nsec=24):
    xs = [c['cam_x'][i] for i in idx]
    zs = [c['cam_z'][i] for i in idx]
    cx, cz = (max(xs) + min(xs)) / 2, (max(zs) + min(zs)) / 2
    med = [[] for _ in range(nsec)]
    sp = [0] * nsec
    ss = set(spikes)
    for i in idx:
        a = math.atan2(c['cam_z'][i] - cz, c['cam_x'][i] - cx)
        k = int((a + math.pi) / (2 * math.pi) * nsec) % nsec
        med[k].append(fm[i])
        if i in ss:
            sp[k] += 1
    return [st.median(m) if m else float('nan') for m in med], sp


def main():
    walks = []
    for p in sys.argv[1:]:
        c = load(p)
        out, w = report(p, c)
        print("\n".join(out))
        print()
        walks.append(w)
    if len(walks) >= 2:
        sec = [sectors(c, idx, fm, spk) for idx, fm, c, spk in walks]
        print("circle sectors (24 x 15 deg): median frame ms per walk | spikes per walk")
        for k in range(24):
            print(f"  {k:2d} " + "  ".join(f"{s[0][k]:6.1f}" for s in sec) + "   | " + " ".join(f"{s[1][k]:3d}" for s in sec))
        a, b = sec[0][0], sec[1][0]
        ok = [k for k in range(24) if not (math.isnan(a[k]) or math.isnan(b[k]))]
        print(f"r(sector medians, walk 1 vs 2) = {corr([a[k] for k in ok], [b[k] for k in ok]):.3f}  "
              f"r(sector spike counts) = {corr([float(x) for x in sec[0][1]], [float(x) for x in sec[1][1]]):.3f}")


if __name__ == '__main__':
    main()
