#!/usr/bin/env python3
"""Format the dense-vs-tiled A/B CSV into the four tables the sweep exists for.

Wall clock only. No GPU-timer figure appears anywhere, by design -- Amendment
8.10 measured gpuFrameTime inflated ~2.6-2.7x on this hardware, and per-kernel
Metal attribution is a confirmed dead end on this toolchain.

Every number printed here is PROVISIONAL in this project's standing sense: it
is a RELEASE standalone wall clock, good for magnitude and direction, not a
GPU-stage attribution. The driftcheck spread beside each row is the honest
error bar; a wide one is reported loudly rather than smoothed over.
"""
import argparse, csv, sys

# §2.2's fluid budget. Quoted for orientation only -- see the caveat printed
# in the header. A wall-clock frame total is NOT the same quantity as a
# GPU-lane budget, and the report says so rather than implying a verdict.
FLUID_BUDGET_MS = 3.5

# Above this, the pair is not usable as evidence and the row says so instead
# of being quietly averaged. Amendment 8_9 §0 Rule 2.
DRIFT_WARN_PCT = 15.0


def load(path):
    rows = {}
    with open(path) as f:
        for r in csv.DictReader(f):
            rows[r["label"]] = r
    return rows


def fnum(r, k):
    try:
        return float(r[k])
    except (KeyError, ValueError, TypeError):
        return float("nan")


def inum(r, k):
    try:
        return int(float(r[k]))
    except (KeyError, ValueError, TypeError):
        return 0


def drift(rows, label, field="frame_p50_ms"):
    """(value, repeat, spread_pct) for a config and its driftcheck twin."""
    base = rows.get(label)
    rep = rows.get(label + "_REPEAT_driftcheck")
    if base is None:
        return None, None, None
    v = fnum(base, field)
    if rep is None:
        return v, None, None
    rv = fnum(rep, field)
    if v <= 0:
        return v, rv, None
    return v, rv, abs(rv - v) / v * 100.0


def fmt_drift(pct):
    if pct is None:
        return "   --  "
    flag = " !" if pct >= DRIFT_WARN_PCT else "  "
    return f"{pct:5.1f}%{flag}"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--csv", required=True)
    a = ap.parse_args()

    try:
        rows = load(a.csv)
    except FileNotFoundError:
        print("FAIL: no CSV at " + a.csv)
        return 1
    if not rows:
        print("FAIL: no rows parsed from " + a.csv)
        return 1

    W = 96
    print()
    print("=" * W)
    print("DENSE vs TILED FLUID ADDRESSING -- WALL-CLOCK A/B   [PROVISIONAL]")
    print("=" * W)
    print("RELEASE standalone, launched outside the Editor. vsync off, uncapped.")
    print("Wall clock from Time.unscaledDeltaTime. ONE CONFIG PER PROCESS LAUNCH.")
    print("180 samples after 420 warmup + 45 settle frames, discarded warm-up launch first.")
    print()
    print("NO GPU TIMING ANYWHERE. gpuFrameTime is read nowhere (Amendment 8.10:")
    print("inflated ~2.6-2.7x on this hardware). No Xcode, no Instruments (8.9 Rule 1).")
    print("There is no Performance State field -- FrameTimingManager cannot report it")
    print("on this platform; that is a permanent accepted limitation, not worked around.")
    print()
    print("READING RULE. 'frame' is the WHOLE frame: CA submit, the GPU catching up,")
    print("the op-list readback, the CPU applying it, and the clipmap uploads that")
    print("follow. It is not a fluid-kernel figure and may not be quoted as one.")
    print(f"§2.2's <={FLUID_BUDGET_MS} ms fluid budget is a GPU-LANE budget; a wall-clock frame")
    print("total is a different quantity, so no row below is scored pass/fail against it.")
    print()

    # ---------------------------------------------------------------
    # STEP 1
    # ---------------------------------------------------------------
    print("=" * W)
    print("STEP 1 -- MATCHED VOLUME, DENSE vs TILED")
    print("=" * W)
    print("Same scenario, same pockets, same camera. The only difference is whether")
    print("FluidGpuSimulation was handed a FluidTileMap. The dense region is re-sized")
    print("per config to the SMALLEST power-of-two box that holds the scenario, which")
    print("is the best dense can do -- a tiled win here is therefore conservative.")
    print()
    hdr = (f"{'volume':>8} {'path':>6} {'placed':>7} {'live':>7} {'disp.cells':>11} "
           f"{'p50 ms':>8} {'p99 ms':>8} {'drift':>8} {'region':>14}")
    print(hdr)
    print("-" * len(hdr))

    step1 = []
    for v in (500, 2000, 8000, 32000):
        pair = {}
        for path in ("dense", "tiled"):
            label = f"{path}_v{v}"
            r = rows.get(label)
            if r is None:
                print(f"{v:>8} {path:>6}   (missing)")
                continue
            p50, rep, pct = drift(rows, label)
            p99 = fnum(r, "frame_p99_ms")
            print(f"{v:>8} {path:>6} {inum(r,'placed_voxels'):>7} {inum(r,'live_slots_mean'):>7} "
                  f"{inum(r,'dispatch_cells_mean'):>11,} {p50:>8.3f} {p99:>8.3f} "
                  f"{fmt_drift(pct):>8} {r.get('dense_region_dims','?'):>14}")
            pair[path] = (p50, inum(r, "live_slots_mean"), inum(r, "dispatch_cells_mean"))
        if "dense" in pair and "tiled" in pair:
            step1.append((v, pair["dense"], pair["tiled"]))
        print()

    if step1:
        print("  TILED RELATIVE TO DENSE (negative = tiled faster)")
        print(f"  {'volume':>8} {'dense p50':>10} {'tiled p50':>10} {'delta ms':>9} {'delta %':>8} {'cells x':>8}")
        for v, d, t in step1:
            dm = t[0] - d[0]
            dp = (dm / d[0] * 100.0) if d[0] > 0 else float("nan")
            cx = (t[2] / d[2]) if d[2] else float("nan")
            print(f"  {v:>8} {d[0]:>10.3f} {t[0]:>10.3f} {dm:>+9.3f} {dp:>+7.1f}% {cx:>7.2f}x")
        print()
        print("  'cells x' is tiled dispatch cells / dense dispatch cells -- the")
        print("  mechanism behind whatever the timing column says.")
    print()

    # ---------------------------------------------------------------
    # STEP 2
    # ---------------------------------------------------------------
    print("=" * W)
    print("STEP 2 -- SAME VOLUME, VARYING SCATTER (tiled)")
    print("=" * W)
    print("~8000 voxels split across 1 / 8 / 64 / 512 disconnected pockets, each")
    print("pocket in its own tile. Isolates whether SCATTER costs beyond raw volume.")
    print()
    hdr2 = (f"{'pockets':>8} {'placed':>7} {'live':>7} {'tiles':>6} {'disp.cells':>11} "
            f"{'p50 ms':>8} {'p99 ms':>8} {'drift':>8} {'ms/1k vox':>10}")
    print(hdr2)
    print("-" * len(hdr2))

    base_p50 = None
    for s in (1, 8, 64, 512):
        label = f"tiled_s{s}_v8000"
        r = rows.get(label)
        if r is None:
            print(f"{s:>8}   (missing)")
            continue
        p50, rep, pct = drift(rows, label)
        live = inum(r, "live_slots_mean")
        placed = inum(r, "placed_voxels")
        per1k = (p50 / placed * 1000.0) if placed else float("nan")
        if s == 1:
            base_p50 = p50
        print(f"{s:>8} {placed:>7} {live:>7} {inum(r,'active_tiles_mean'):>6} "
              f"{inum(r,'dispatch_cells_mean'):>11,} {p50:>8.3f} {fnum(r,'frame_p99_ms'):>8.3f} "
              f"{fmt_drift(pct):>8} {per1k:>10.4f}")
    if base_p50:
        print()
        print("  SCATTER COST RELATIVE TO ONE CONTIGUOUS POOL OF THE SAME VOLUME")
        for s in (8, 64, 512):
            r = rows.get(f"tiled_s{s}_v8000")
            if r is None:
                continue
            p50, _, _ = drift(rows, f"tiled_s{s}_v8000")
            print(f"    s{s:<4} {p50 - base_p50:>+8.3f} ms  ({(p50/base_p50 - 1)*100:>+6.1f}%)"
                  f"   tiles {inum(r,'active_tiles_mean'):>4}")
    print()

    # ---------------------------------------------------------------
    # STEP 4
    # ---------------------------------------------------------------
    print("=" * W)
    print("STEP 4 -- CPU-SIDE OP-LIST APPLY (FluidOpListReadback.PumpAndApply)")
    print("=" * W)
    print("Stopwatch bracketed around PumpAndApply and nothing else. This is the CPU")
    print("MAIN-THREAD lane, entirely separate from the GPU CA, and is the cost")
    print("flagged NOT MEASURED since Phase 5b. 'submit' is the CA dispatch call for")
    print("comparison -- also CPU, also main thread.")
    print()
    hdr4 = (f"{'config':>26} {'live':>7} {'writes':>8} "
            f"{'pump p50':>9} {'pump p99':>9} {'submit p50':>11} {'frame p50':>10}")
    print(hdr4)
    print("-" * len(hdr4))
    for label in ([f"{p}_v{v}" for v in (500, 2000, 8000, 32000) for p in ("dense", "tiled")]
                  + [f"tiled_s{s}_v8000" for s in (8, 64, 512)]):
        r = rows.get(label)
        if r is None:
            continue
        print(f"{label:>26} {inum(r,'live_slots_mean'):>7} {inum(r,'voxel_writes'):>8} "
              f"{fnum(r,'pump_p50_ms'):>9.4f} {fnum(r,'pump_p99_ms'):>9.4f} "
              f"{fnum(r,'submit_p50_ms'):>11.4f} {fnum(r,'frame_p50_ms'):>10.3f}")
    print()

    # ---------------------------------------------------------------
    # Integrity
    # ---------------------------------------------------------------
    print("=" * W)
    print("INTEGRITY -- anything here invalidates the rows above")
    print("=" * W)
    bad = 0
    for label, r in sorted(rows.items()):
        errs = inum(r, "readback_errors")
        nonres = inum(r, "nonresident_ops")
        live = inum(r, "live_slots_mean")
        notes = []
        if errs:
            notes.append(f"{errs} readback errors")
        if live == 0:
            notes.append("ZERO live slots -- nothing simulated, the row measures an idle CA")
        if nonres:
            notes.append(f"{nonres} ops dropped non-resident")
        if notes:
            bad += 1
            print(f"  {label}: " + "; ".join(notes))
    if not bad:
        print("  clean: every config sustained live fluid, no readback errors,")
        print("  no ops dropped for non-residency.")

    print()
    widest = []
    for label in rows:
        if label.endswith("_REPEAT_driftcheck"):
            continue
        _, _, pct = drift(rows, label)
        if pct is not None:
            widest.append((pct, label))
    widest.sort(reverse=True)
    if widest:
        print(f"DRIFTCHECK SPREAD, worst first (>{DRIFT_WARN_PCT}% means re-run, not average):")
        for pct, label in widest[:6]:
            mark = "  <-- TOO WIDE TO CONCLUDE FROM" if pct >= DRIFT_WARN_PCT else ""
            print(f"  {label:>34}  {pct:5.1f}%{mark}")
    print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
