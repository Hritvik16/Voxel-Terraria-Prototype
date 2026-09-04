#!/usr/bin/env python3
"""Parse an Instruments 'Metal System Trace' export into GPU timings.

Reads the XML that `xctrace export --xpath ...` produces and reports, for each
named encoder, the per-encoder GPU duration distribution -- plus the device
thermal state and GPU performance state over the capture window.

NOTHING HERE IS A FRAME TIME. Per-encoder GPU duration is not frame time and is
not comparable to ./run-acceptance-rig.sh's numbers, which remain the only
trusted frame-time source (CLAUDE.md).

Instruments' XML interns values: an element carries id="N" the first time and
ref="N" afterwards, so every row must be resolved against everything seen
before it. That is what _resolve does; without it most rows look empty.
"""
import argparse, os, subprocess, sys
import xml.etree.ElementTree as ET

TABLES = {
    "gpu":     "metal-gpu-intervals",
    "thermal": "device-thermal-state-intervals",
    "perf":    "gpu-performance-state-intervals",
}


def export(xctrace, trace, schema, out):
    xpath = '/trace-toc/run[@number="1"]/data/table[@schema="%s"]' % schema
    r = subprocess.run([xctrace, "export", "--input", trace, "--xpath", xpath,
                        "--output", out], capture_output=True, text=True)
    return r.returncode == 0 and os.path.exists(out) and os.path.getsize(out) > 0


def rows(path):
    """Yield each <row> as a list of (tag, text, fmt), with id/ref resolved."""
    seen = {}
    for _, el in ET.iterparse(path, events=("end",)):
        if el.tag != "row":
            continue
        out = []
        for child in list(el):
            rid = child.get("id")
            ref = child.get("ref")
            if ref is not None:
                out.append(seen.get(ref, (child.tag, None, None)))
                continue
            rec = (child.tag, (child.text or "").strip(), child.get("fmt"))
            if rid is not None:
                seen[rid] = rec
            out.append(rec)
        yield out
        el.clear()


def pct(sorted_vals, p):
    if not sorted_vals:
        return 0.0
    k = min(len(sorted_vals) - 1, max(0, int(round((p / 100.0) * (len(sorted_vals) - 1)))))
    return sorted_vals[k]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--trace", required=True)
    ap.add_argument("--xctrace", required=True)
    ap.add_argument("--label-prefix", default="VE.")
    a = ap.parse_args()

    workdir = a.trace + ".export"
    os.makedirs(workdir, exist_ok=True)

    # ---- GPU encoder intervals -------------------------------------------
    gpu_xml = os.path.join(workdir, "gpu.xml")
    if not export(a.xctrace, a.trace, TABLES["gpu"], gpu_xml):
        print("FAIL: could not export %s" % TABLES["gpu"])
        return 1

    # Per-frame aggregation. A single dispatch shows up as MANY GPU intervals
    # (the raymarch averaged ~13 depth-0 intervals per frame), so per-interval
    # statistics describe scheduler slices, not the cost of a pass. Summing
    # within a frame gives "GPU time this pass cost that frame", which is the
    # question actually being asked.
    #
    # Depth is reported both ways on purpose. Instruments nests debug groups,
    # and a label can appear at depth 0 in one frame and depth 1 in another
    # (nested inside another pass's group). Depth-0-only UNDERCOUNTS; all-depth
    # can double-count where a nested interval sits inside a parent of a
    # different label. Both are printed so a conclusion that depends on which
    # one you pick is visibly not safe to draw.
    per_frame_d0 = {}
    per_frame_all = {}
    total_ns = 0
    for r in rows(gpu_xml):
        dur = label = depth = frame = chan = None
        for tag, text, fmt in r:
            if tag == "duration" and dur is None:
                try:
                    dur = int(text)
                except (TypeError, ValueError):
                    pass
            elif tag == "formatted-label" and label is None:
                label = fmt or ""
            elif tag == "metal-nesting-level" and depth is None:
                depth = text
            elif tag == "gpu-frame-number" and frame is None:
                frame = text
            elif tag == "gpu-channel-name" and chan is None:
                chan = fmt or ""
        if dur is None or not label:
            continue
        total_ns += dur
        if a.label_prefix not in label:
            continue
        name = label[label.find(a.label_prefix):].split()[0].strip()
        per_frame_all.setdefault(name, {}).setdefault(frame, 0)
        per_frame_all[name][frame] += dur
        if depth == "0":
            per_frame_d0.setdefault(name, {}).setdefault(frame, 0)
            per_frame_d0[name][frame] += dur

    def report(title, table):
        print()
        print("=" * 78)
        print(title)
        print("=" * 78)
        if not table:
            print("  NO LABELLED ENCODERS FOUND.")
            print("  The usual cause is a NON-DEVELOPMENT build: CommandBuffer.BeginSample")
            print("  is a profiler marker and is compiled out of a release player, so no")
            print("  Metal debug group is emitted. Build with")
            print("  CommandLineBuild.BuildPlaygroundTraceStandalone.")
            return
        print("  %-26s %7s %9s %9s %9s %9s %10s" %
              ("pass", "frames", "p50 ms", "p99 ms", "max ms", "mean ms", "total ms"))
        grand = sum(sum(f.values()) for f in table.values())
        for name, fr in sorted(table.items(), key=lambda kv: -sum(kv[1].values())):
            v = sorted(fr.values())
            print("  %-26s %7d %9.3f %9.3f %9.3f %9.3f %10.1f" % (
                name, len(v), pct(v, 50) / 1e6, pct(v, 99) / 1e6,
                v[-1] / 1e6, sum(v) / len(v) / 1e6, sum(v) / 1e6))
        print("  ---")
        for name, fr in sorted(table.items(), key=lambda kv: -sum(kv[1].values())):
            print("  %-26s %5.1f%% of labelled GPU work" %
                  (name, 100.0 * sum(fr.values()) / grand if grand else 0.0))

    report("PER-FRAME GPU TIME  -- depth-0 intervals only (conservative)", per_frame_d0)
    report("PER-FRAME GPU TIME  -- all nesting depths (upper bound)", per_frame_all)
    print()
    print("  all GPU intervals in trace, every process: %.1f ms" % (total_ns / 1e6))
    print()
    print("  NOT A FRAME TIME. Per-encoder GPU duration is not comparable to")
    print("  ./run-acceptance-rig.sh, which stays the only trusted frame-time source.")

    # ---- thermal + performance state -------------------------------------
    for key, title in (("thermal", "DEVICE THERMAL STATE"),
                       ("perf", "GPU PERFORMANCE STATE")):
        xml = os.path.join(workdir, key + ".xml")
        print()
        print("=" * 78)
        print("%s  (%s)" % (title, TABLES[key]))
        print("=" * 78)
        if not export(a.xctrace, a.trace, TABLES[key], xml):
            print("  table not present in this trace")
            continue
        states = {}
        for r in rows(xml):
            dur = None
            vals = []
            for tag, text, fmt in r:
                if tag == "duration" and dur is None:
                    try:
                        dur = int(text)
                    except (TypeError, ValueError):
                        pass
                elif fmt and tag not in ("start-time", "duration"):
                    vals.append(fmt)
            if vals:
                states.setdefault(" | ".join(vals[:2]), [0, 0])
                states[" | ".join(vals[:2])][0] += 1
                states[" | ".join(vals[:2])][1] += (dur or 0)
        if not states:
            print("  (no rows)")
        for k, (n, ns) in sorted(states.items(), key=lambda kv: -kv[1][1]):
            print("  %-52s n=%-5d %8.1f ms" % (k[:52], n, ns / 1e6))
    return 0


if __name__ == "__main__":
    sys.exit(main())
