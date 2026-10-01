#!/usr/bin/env python3
"""report-flyfast.py <arms.tsv> -- summarise run-flyfast.sh's arms.

Each arm folder holds flyfast_frames.tsv (one row per frame, wall-clock ms plus
the far band's per-frame CPU phases and GPU work counts, the streamer's ms and
pending loads). Per label (A = far OFF, B = far ON as the demo runs it, S = far
ON + mip stress) it reports the frame-time distribution pooled over reps, the
per-rep p50/p99 (the rep spread is the noise floor), and a SPLIT of the far
band's work:

  - mean CPU ms of each far phase per frame, and its p99.9
  - frame time on frames that did far GPU work (a strip scatter + whole-level
    mip rebuild was issued) vs frames that did none, in the same arm. The GPU
    work lands a frame or two after it is issued (the CPU runs ahead), so the
    "event" window is the issuing frame and the two after it.
  - the same split for chunk streaming (streamer ms > 2 ms) so the two causes
    can be told apart.
"""
import os
import sys
from collections import defaultdict

LABELS = {"A": "far band OFF", "B": "far band ON (+far shadows, as the demo)",
          "S": "far ON + mip stress", "H": "far band OFF, HUD hidden (-nohud)"}


def load(folder):
    path = os.path.join(folder, "flyfast_frames.tsv")
    if not os.path.exists(path):
        return None
    lines = open(path).read().splitlines()
    head = lines[0].split("\t")
    rows = []
    for l in lines[1:]:
        p = l.split("\t")
        if len(p) != len(head):
            continue
        rows.append({h: float(v) for h, v in zip(head, p)})
    return rows


def pct(v, q):
    if not v:
        return 0.0
    s = sorted(v)
    return s[min(len(s) - 1, int(q * len(s)))]


def mean(v):
    return sum(v) / len(v) if v else 0.0


def summarise(rows):
    ms = [r["ms"] for r in rows]
    n = len(ms)
    return {
        "n": n, "mean": mean(ms), "p50": pct(ms, .5), "p90": pct(ms, .9), "p99": pct(ms, .99),
        "p999": pct(ms, .999), "max": max(ms) if ms else 0,
        "o33": sum(1 for x in ms if x > 33) * 1000.0 / max(1, n),
        "o50": sum(1 for x in ms if x > 50) * 1000.0 / max(1, n),
        "o100": sum(1 for x in ms if x > 100),
        "rock": sum(1 for r in rows if r["inRock"] > 0),
    }


def split(rows, flag):
    """frame ms where flag(row) fired in this frame or the two before, vs not."""
    ev, quiet = [], []
    for i, r in enumerate(rows):
        hit = any(flag(rows[j]) for j in range(max(0, i - 2), i + 1))
        (ev if hit else quiet).append(r["ms"])
    return ev, quiet


def main():
    arms = [l.split("\t") for l in open(sys.argv[1]).read().splitlines() if l.strip()]
    by = defaultdict(list)
    for scen, label, folder in arms:
        rows = load(folder.strip())
        if rows:
            by[(scen, label)].append(rows)
    print("FLY-FAST ARMS -- per-frame wall clock (Time.unscaledDeltaTime), RELEASE, fullscreen, gate 960")
    print("pooled over reps; 'reps p50/p99' lists each rep so the spread is visible")
    print()
    hdr = f"{'scenario':<9}{'arm':<42}{'frames':>7}{'mean':>7}{'p50':>7}{'p99':>7}{'p99.9':>7}{'max':>8}{'>33/k':>7}{'>50/k':>7}{'>100':>6}  reps p50/p99"
    print(hdr)
    for scen in sorted({k[0] for k in by}):
        for label in "ABSH":
            reps = by.get((scen, label))
            if not reps:
                continue
            pooled = [r for rep in reps for r in rep]
            s = summarise(pooled)
            per = "  ".join(f"{summarise(rep)['p50']:.1f}/{summarise(rep)['p99']:.1f}" for rep in reps)
            print(f"{scen:<9}{label + ' ' + LABELS[label]:<42}{s['n']:>7}{s['mean']:>7.2f}{s['p50']:>7.2f}{s['p99']:>7.2f}"
                  f"{s['p999']:>7.2f}{s['max']:>8.1f}{s['o33']:>7.1f}{s['o50']:>7.1f}{s['o100']:>6}  {per}"
                  + (f"   IN ROCK {s['rock']} frames" if s["rock"] else ""))
    print()
    print("FAR BAND WORK SPLIT (far-ON arms; CPU phases are main-thread ms per frame)")
    for scen in sorted({k[0] for k in by}):
        for label in "BS":
            reps = by.get((scen, label))
            if not reps:
                continue
            pooled = [r for rep in reps for r in rep]
            cols = ["farHarvestMs", "farApplyFullMs", "farStagingMs", "farScheduleMs", "farResidencyMs"]
            parts = "  ".join(f"{c[3:-2]} {mean([r[c] for r in pooled]):.3f}/{pct([r[c] for r in pooled], .999):.2f}" for c in cols)
            print(f"  {scen} {label}: mean/p99.9 ms  {parts}")
            strips = sum(r["farStrips"] for r in pooled)
            fulls = sum(r["farFulls"] for r in pooled)
            mipl = sum(r["farMipLevels"] for r in pooled)
            print(f"      strips {strips:.0f}  full refills {fulls:.0f}  mip-level rebuilds {mipl:.0f}  over {len(pooled)} frames")
            ev, quiet = [], []
            for rep in reps:
                e, q = split(rep, lambda r: r["farStrips"] > 0 or r["farFulls"] > 0)
                ev += e; quiet += q
            print(f"      frames within 2 of a far strip/refill: n {len(ev)}  mean {mean(ev):.2f}  p99 {pct(ev, .99):.2f}   "
                  f"other frames: n {len(quiet)}  mean {mean(quiet):.2f}  p99 {pct(quiet, .99):.2f}")
    print()
    print("STREAMING SPLIT (every arm): frames within 2 of a streamer.Update > 2 ms vs the rest")
    for scen in sorted({k[0] for k in by}):
        for label in "ABSH":
            reps = by.get((scen, label))
            if not reps:
                continue
            ev, quiet = [], []
            for rep in reps:
                e, q = split(rep, lambda r: r["streamMs"] > 2.0)
                ev += e; quiet += q
            pooled = [r for rep in reps for r in rep]
            sm = [r["streamMs"] for r in pooled]
            print(f"  {scen} {label}: streamer ms mean {mean(sm):.2f} p99 {pct(sm, .99):.2f} max {max(sm):.1f}   "
                  f"heavy-stream frames n {len(ev)} mean {mean(ev):.2f} p99 {pct(ev, .99):.2f}   "
                  f"rest n {len(quiet)} mean {mean(quiet):.2f} p99 {pct(quiet, .99):.2f}")
    print()
    print("STREAMER.UPDATE SPLIT (main-thread ms per frame, mean / p99 / max; present in rows from the split build)")
    stcols = ["stSlideMs", "stDispatchMs", "stDrainMs", "stTransferMs", "stValveMs", "stSweepMs", "stClipUploadMs", "stCascadeMs"]
    for scen in sorted({k[0] for k in by}):
        for label in "ABSH":
            reps = by.get((scen, label))
            if not reps or "stSlideMs" not in reps[0][0]:
                continue
            pooled = [r for rep in reps for r in rep]
            print(f"  {scen} {label}: streamMs mean {mean([r['streamMs'] for r in pooled]):.2f}")
            for c in stcols:
                v = [r[c] for r in pooled]
                print(f"      {c[2:-2]:<11} mean {mean(v):6.2f}  p99 {pct(v, .99):6.2f}  max {max(v):7.2f}")
            print(f"      admitted/frame {mean([r['stAdmitted'] for r in pooled]):.2f}  evicted/frame {mean([r['stEvicted'] for r in pooled]):.2f}  "
                  f"upload KB/frame {mean([r['stUploadBytes'] for r in pooled]) / 1024:.0f}")
            spikes = [r for rep in reps for r in rep if r["ms"] > 100]
            if spikes:
                print(f"      on the {len(spikes)} frames > 100 ms: " +
                      "  ".join(f"{c[2:-2]} {mean([r[c] for r in spikes]):.1f}" for c in stcols) +
                      f"  (frame ms {mean([r['ms'] for r in spikes]):.1f}, streamMs {mean([r['streamMs'] for r in spikes]):.1f})")
    print()
    print("GC (present in rows from the GC build): collections per frame, and on spike frames")
    for scen in sorted({k[0] for k in by}):
        for label in "ABSH":
            reps = by.get((scen, label))
            if not reps or "gc0" not in reps[0][0]:
                continue
            pooled = [r for rep in reps for r in rep]
            n = len(pooled)
            gcf = [r for r in pooled if r["gc0"] > 0]
            big = [r for r in pooled if r["ms"] > 100]
            bigGc = [r for r in big if r["gc0"] > 0]
            # the pause lands in the frame whose delta we record, or the next one
            near = 0
            for rep in reps:
                for i, r in enumerate(rep):
                    if r["ms"] > 100 and any(rep[j]["gc0"] > 0 for j in range(max(0, i - 1), min(len(rep), i + 2))):
                        near += 1
            gms = [r["ms"] for r in gcf]
            other = [r["ms"] for r in pooled if r["gc0"] == 0]
            print(f"  {scen} {label}: {len(gcf)} GC frames in {n} ({1000.0 * len(gcf) / n:.1f}/1000, "
                  f"{sum(r['gc0'] for r in pooled):.0f} collections)   frames > 100 ms: {len(big)}, with a collection that frame {len(bigGc)}, "
                  f"within +-1 frame {near}")
            print(f"      GC frames mean {mean(gms):.1f} ms p50 {pct(gms, .5):.1f}   non-GC frames mean {mean(other):.1f} ms p50 {pct(other, .5):.1f} "
                  f"p99 {pct(other, .99):.1f}   heap {min(r['heapMb'] for r in pooled):.0f}-{max(r['heapMb'] for r in pooled):.0f} MB")
    print()
    print("SPIKES (> 33 ms and > 2x the arm's p50): what else happened in that frame or the two before")
    for scen in sorted({k[0] for k in by}):
        for label in "ABSH":
            reps = by.get((scen, label))
            if not reps:
                continue
            tot = far_ = strm = rev = both = 0
            for rep in reps:
                p50 = summarise(rep)["p50"]
                for i, r in enumerate(rep):
                    if r["ms"] <= 33 or r["ms"] <= 2 * p50:
                        continue
                    tot += 1
                    win = rep[max(0, i - 2): i + 1]
                    f = any(w["farStrips"] > 0 or w["farFulls"] > 0 for w in win)
                    s = any(w["streamMs"] > 2.0 for w in win)
                    far_ += f; strm += s; both += f and s
                    rev += any(w["reversal"] > 0 for w in rep[max(0, i - 30): i + 1])
            print(f"  {scen} {label}: {tot} spikes; near far work {far_}, near heavy streaming {strm}, both {both}, "
                  f"within 30 frames of a heading reversal {rev}")


if __name__ == "__main__":
    main()
