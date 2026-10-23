#!/usr/bin/env python3
"""Task 123: the chunk-crossing frames of -spikelog CSVs (eviction slicing A/B).

  python3 tools/crossing-report.py <label>=<spikes.csv>[@N] ...

@N keeps the first N rows (the WHOLE RUN line's frame count; the probe keeps
recording after the walk). Per CSV: the whole-run p50 / p95 / p99, % > 33 ms,
> 100 ms count, and for the frames where the camera's xz chunk (12.8 m) changed:
their frame ms (median / p90 / max), slide ms and evictions; plus the frames that
evicted anything (count, max per frame) and the evictions' 0..+15 frame tail.
"""
import csv, math, sys, statistics as st

CHUNK_M = 12.8


def pct(v, p):
    s = sorted(v)
    return s[min(len(s) - 1, int(math.ceil(p / 100 * len(s))) - 1)]


def report(label, path):
    keep = None
    if '@' in path:
        path, keep = path.rsplit('@', 1)
        keep = int(keep)
    rows = list(csv.DictReader(open(path)))
    if keep: rows = rows[:keep]
    import os
    rows = rows[int(os.environ.get("SKIP", "0")):]   # SKIP=N drops the run-start frames
    f = lambda k: [float(r[k] or 0) for r in rows]
    fm, sl, ev, dr = f('frame_ms'), f('slide_ms'), f('evicted'), f('drain_ms')
    cx, cz = f('cam_x'), f('cam_z')
    ch = [(math.floor(x / CHUNK_M), math.floor(z / CHUNK_M)) for x, z in zip(cx, cz)]
    cross = [i for i in range(1, len(rows)) if ch[i] != ch[i - 1]]
    evf = [i for i in range(len(rows)) if ev[i] > 0]
    print(f"{label}: {len(rows)} frames  p50 {st.median(fm):.1f}  p95 {pct(fm, 95):.1f}  p99 {pct(fm, 99):.1f} ms  "
          f">33 {100 * sum(x > 33 for x in fm) / len(fm):.1f}%  >100 {sum(x > 100 for x in fm)}  >50 {sum(x > 50 for x in fm)}")
    if cross:
        cf = [fm[i] for i in cross]
        print(f"  crossings {len(cross)}: frame median {st.median(cf):.1f}  p90 {pct(cf, 90):.1f}  max {max(cf):.1f} ms;  "
              f"slide median {st.median([sl[i] for i in cross]):.2f}  max {max(sl[i] for i in cross):.2f} ms;  "
              f"evicted median {st.median([ev[i] for i in cross]):.0f}  max {max(ev[i] for i in cross):.0f}")
        nxt = [fm[i + 1] for i in cross if i + 1 < len(rows)]
        print(f"  frame after a crossing: median {st.median(nxt):.1f}  p90 {pct(nxt, 90):.1f} ms;  "
              f"crossing frames > 50 ms: {sum(x > 50 for x in cf)}")
    if evf:
        print(f"  frames evicting: {len(evf)}  evictions {sum(ev):.0f}  max/frame {max(ev):.0f}  "
              f"slide on them median {st.median([sl[i] for i in evf]):.2f}  max {max(sl[i] for i in evf):.2f} ms")
        # A BURST = a run of evicting frames (OFF: one frame per crossing; ON: the
        # crossing frame plus its slices). Its first frame is where the slide ran.
        starts = [i for i in evf if i == 0 or ev[i - 1] == 0]
        big = [i for i in starts if sum(ev[j] for j in range(i, min(len(rows), i + 64)) if all(ev[k] > 0 for k in range(i, j + 1))) > 64]
        bf = [fm[i] for i in starts]
        print(f"  bursts {len(starts)}: first-frame ms median {st.median(bf):.1f}  p90 {pct(bf, 90):.1f}  max {max(bf):.1f};  "
              f"> 50 ms {sum(x > 50 for x in bf)}")
        if big:
            gb = [fm[i] for i in big]
            print(f"  big bursts (> 64 evictions) {len(big)}: first-frame ms median {st.median(gb):.1f}  max {max(gb):.1f};  "
                  f"slide median {st.median([sl[i] for i in big]):.2f} ms")
    print(f"  slide ms p99 {pct(sl, 99):.2f}  max {max(sl):.2f};  slide > 2 ms frames: {sum(x > 2 for x in sl)}   > 5 ms: {sum(x > 5 for x in sl)}")


for a in sys.argv[1:]:
    lab, p = a.split('=', 1)
    report(lab, p)
