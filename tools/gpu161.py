#!/usr/bin/env python3
"""Task 161: per-encoder GPU time from an UNLABELLED (release player) Metal System Trace export.

  python3 -I tools/gpu161.py <trace>.export/gpu.xml [process substring]

A release player emits no debug groups, so encoders are named by position only
("Command Buffer i:Compute Command k"). Intervals are summed per encoder id (one
dispatch is sliced into many intervals), then averaged per position label over the
trace. NOT a frame time (CLAUDE.md): GPU busy time per encoder, Medium perf state.
"""
import re, sys, collections
import xml.etree.ElementTree as ET

def rows(path):
    seen = {}
    for _, el in ET.iterparse(path, events=("end",)):
        if el.tag != "row": continue
        out = {}
        for child in list(el):
            rid, ref = child.get("id"), child.get("ref")
            if ref is not None: rec = seen.get(ref, (None, None))
            else:
                rec = ((child.text or "").strip(), child.get("fmt"))
                if rid is not None: seen[rid] = rec
            out.setdefault(child.tag, []).append(rec)
        yield out
        el.clear()

def main(path, proc="Voxel Terraria"):
    enc = {}   # encoder id -> [label, ns, start, channel]
    for r in rows(path):
        lab = (r.get("formatted-label") or [(None, "")])[0][1] or ""
        if proc not in lab: continue
        depth = (r.get("metal-nesting-level") or [("0", "0")])[0][0]
        if depth != "0": continue
        m = re.match(r"(.*?)\s+\(", lab)
        name = m.group(1) if m else lab
        ids = r.get("metal-command-buffer-id") or []
        eid = ids[1][0] if len(ids) > 1 else (ids[0][0] if ids else None)
        dur = int((r.get("duration") or [("0", "")])[0][0] or 0)
        start = int((r.get("start-time") or [("0", "")])[0][0] or 0)
        ch = (r.get("gpu-channel-name") or [("", "")])[0][0]
        e = enc.setdefault((name, eid), [name, 0, start, set()])
        e[1] += dur; e[2] = min(e[2], start); e[3].add(ch)
    by = collections.defaultdict(list)
    for (name, eid), (n, ns, st, ch) in enc.items(): by[name].append((ns, ch))
    starts = sorted(st for (_, _, st, _) in enc.values())
    span_s = (starts[-1] - starts[0]) / 1e9 if len(starts) > 1 else 1
    frames = max(len(v) for v in by.values())
    print(f"{path}: span {span_s:.2f} s, ~{frames} frames ({frames / span_s:.1f}/s); depth-0 GPU ms per encoder position, per frame")
    tot = 0.0
    for name, v in sorted(by.items(), key=lambda kv: -sum(x[0] for x in kv[1])):
        per = sum(x[0] for x in v) / frames / 1e6; tot += per
        med = sorted(x[0] for x in v)[len(v) // 2] / 1e6
        chans = "/".join(sorted(set().union(*[x[1] for x in v])))
        if per >= 0.05: print(f"  {name:45s} n {len(v):4d}  median {med:6.2f}  per frame {per:6.2f} ms  [{chans}]")
    print(f"  total per frame {tot:.2f} ms")

if __name__ == "__main__":
    main(*sys.argv[1:])
