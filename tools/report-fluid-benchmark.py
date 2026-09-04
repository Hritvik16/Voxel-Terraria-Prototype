#!/usr/bin/env python3
"""Format the fluid benchmark CSV into the §13 gate table.

Wall clock only. No GPU-timer figure appears anywhere by design.
"""
import argparse, csv, sys

BASE_PHASE2 = 11.95   # PHASE_2_COMPLETION.md §5, GroundHorizon mode 4 cascade ON


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--csv", required=True)
    a = ap.parse_args()

    rows = {}
    order = []
    with open(a.csv) as f:
        for r in csv.reader(f):
            if len(r) < 8:
                continue
            label = r[0]
            rows[label] = dict(n=int(r[1]), p50=float(r[2]), p99=float(r[3]),
                               mx=float(r[4]), mean=float(r[5]),
                               render=r[6], window=r[7])
            order.append(label)

    if not rows:
        print("FAIL: no results parsed from " + a.csv)
        return 1

    any_row = rows[order[0]]
    print()
    print("=" * 78)
    print("§13 PHASE 5B PERFORMANCE GATE -- wall-clock substitute methodology")
    print("=" * 78)
    print("RELEASE standalone, wall clock (Time.unscaledDeltaTime), one config per")
    print("process launch, 240 samples after 600 warmup + 180 settle frames.")
    print("NOT Xcode, NOT Instruments. gpuFrameTime read nowhere (Amdt 8.10).")
    print("render %s   window %s" % (any_row["render"], any_row["window"]))
    print()
    print("  %-30s %5s %9s %9s %9s %9s" % ("config", "n", "p50 ms", "p99 ms", "max ms", "mean ms"))
    for k in order:
        v = rows[k]
        print("  %-30s %5d %9.3f %9.3f %9.3f %9.3f" %
              (k, v["n"], v["p50"], v["p99"], v["mx"], v["mean"]))

    def drift(base, rep):
        if base in rows and rep in rows:
            b, r = rows[base]["p50"], rows[rep]["p50"]
            spread = abs(r - b) / b * 100.0 if b else 0.0
            print("  driftcheck %-18s p50 %.3f vs %.3f  -> %.1f%% spread" % (base, b, r, spread))
            return spread
        return None

    print()
    print("DRIFT (same config, later in the sweep -- Phase 2's REPEAT_driftcheck shape)")
    d_idle = drift("idle", "idle_REPEAT_driftcheck")
    d_prim = drift("primed", "primed_REPEAT_driftcheck")

    print()
    print("THE GATE NUMBER -- cost of running fluid, on the shipped release build")
    for a_lab, b_lab, tag in (("idle", "primed", "first pass"),
                              ("idle_REPEAT_driftcheck", "primed_REPEAT_driftcheck", "repeat pass")):
        if a_lab in rows and b_lab in rows:
            for stat in ("p50", "p99"):
                d = rows[b_lab][stat] - rows[a_lab][stat]
                print("  %-12s delta %s = %+.3f ms   (%.3f primed - %.3f idle)" %
                      (tag, stat, d, rows[b_lab][stat], rows[a_lab][stat]))

    print()
    print("AGAINST PHASE 2's OWN BASELINE (same methodology, same rig family)")
    print("  PHASE_2_COMPLETION.md §5: GroundHorizon mode 4 cascade ON = %.2f ms" % BASE_PHASE2)
    print("  (that is raymarch-alone on the Phase 2 world; this scene is the")
    print("   Playground on sizeClass-1 terrain, so it is a REFERENCE POINT for")
    print("   magnitude and methodology, not a like-for-like control.)")
    if "idle" in rows:
        print("  idle here            = %.3f ms p50" % rows["idle"]["p50"])
    print()
    print("  60 fps budget = 16.67 ms/frame.")
    for k in order:
        if k.startswith("primed") and "REPEAT" not in k:
            v = rows[k]
            head = 16.67 - v["p50"]
            print("  primed p50 %.3f ms -> %+.3f ms against 16.67 ms  (%s)" %
                  (v["p50"], head, "PASS" if head > 0 else "FAIL"))
            head99 = 16.67 - v["p99"]
            print("  primed p99 %.3f ms -> %+.3f ms against 16.67 ms  (%s)" %
                  (v["p99"], head99, "PASS" if head99 > 0 else "FAIL"))

    if (d_idle is not None and d_idle > 10) or (d_prim is not None and d_prim > 10):
        print()
        print("  *** DRIFT OVER 10% -- treat the delta as indicative only. ***")
    return 0


if __name__ == "__main__":
    sys.exit(main())
