#!/usr/bin/env python3
"""streamprobe-table.py RUN_DIR... -- task 10: one row per stage, p50/p95/max of
each -streamprobe run side by side (spread across repeated launches, not one
number). RUN_DIR is a StreamProbe/<ts>_<tag> folder holding streamprobe.txt."""
import re
import sys

ROWS = [
    ("frame time", "frame time ms"),
    ("isolated generate", "gen isolated (main, idle) ms"),
    ("generate, non-uniform chunks only", "1 gen solid chunk (worker) ms"),
    ("cascade downsample on the worker", "1 downsample (worker) ms"),
    ("request -> dispatch (pending queue)", "1 request->dispatch ms"),
    ("admitted ", "1 chunks/s admitted"),
    ("admit one chunk, total", "2 admit one chunk ms"),
    ("of which cascade tier 1/2 writes", "2  cascade writes ms"),
    ("admitted -> uploaded                         n", "3 admitted->uploaded ms"),
    ("bytes uploaded per frame", "3 bytes/frame"),
    ("upload pass total", "3 upload pass ms"),
    ("request -> uploaded                          n", "4 request->on GPU ms"),
    ("distance when uploaded", "4 distance on GPU m"),
    ("LATE: inside 64 m", "4 late in 64 m, missing ms"),
    ("of which inside 64 m (full-detail ring)", "4 missing in 64 m /frame"),
    ("deficit                                      n", "5 deficit"),
    ("settle at the shot pose", "fill fresh band s"),
    # task 16b: measured vs the worst-case requirement (StreamingRequirement)
    ("throughput (gen p50", "7 cap vs REQUIRED (p50)"),
    ("throughput (gen p99", "7 cap vs REQUIRED (p99)"),
    ("  latency: REQUIRED", "7 latency p99 vs REQUIRED"),
]


def parse(path):
    out = {}
    lines = open(path).read().splitlines()
    for key, label in ROWS:
        for ln in lines:
            if key not in ln:
                continue
            if key == "admitted ":
                m = re.search(r"= ([\d.]+) chunks/s; worker busy (\d+)%", ln)
                if m:
                    out[label] = f"{m.group(1)}/s busy {m.group(2)}%"
                    break
                continue
            if key == "isolated generate":
                m = re.search(r"p50 ([\d.]+) ms\s+max ([\d.]+)", ln)
                out[label] = f"{m.group(1)} / - / {m.group(2)}" if m else "?"
                break
            if key.startswith("throughput (gen"):
                m = re.search(r"REQUIRED ([\d.]+)/s, CAPACITY ([\d.]+)/s -> (MET|NOT MET \(short by ([\d.]+)x\))", ln)
                out[label] = (f"{m.group(2)} of {m.group(1)}/s " + ("MET" if m.group(3) == "MET" else f"short {m.group(4)}x")) if m else "?"
                break
            if key == "  latency: REQUIRED":
                m = re.search(r"<= ([\d.]+) s.*MEASURED p99 ([\d.]+) s -> (MET|NOT MET)", ln)
                out[label] = f"{m.group(2)} s vs {m.group(1)} s {m.group(3)}" if m else "?"
                break
            if key == "settle at the shot pose":
                m = re.search(r"\): ([\d.]+) s", ln)
                out[label] = m.group(1) if m else "?"
                break
            m = re.search(r"p50\s+([\d.]+)\s+p95\s+([\d.]+)\s+max\s+([\d.]+)", ln)
            if m:
                out[label] = f"{m.group(1)} / {m.group(2)} / {m.group(3)}"
                break
    motion = re.search(r"motion (\w+).*?(\d+) m/s", open(path).read())
    return out, (f"{motion.group(1)} {motion.group(2)}" if motion else "?")


runs = [parse(d.rstrip("/") + "/streamprobe.txt") + (d.rstrip("/").split("/")[-1][:17],) for d in sys.argv[1:]]
print(f"{'stage (p50 / p95 / max)':34s}" + "".join(f"{r[1] + ' ' + r[2][11:17]:>30s}" for r in runs))
for _, label in ROWS:
    print(f"{label:34s}" + "".join(f"{r[0].get(label, '-'):>30s}" for r in runs))
