#!/usr/bin/env python3
"""budget_compare.py -- the comparison half of tools/overnight/run-budget-check.sh (PERF_BUDGET_GUARD.md sec. 3).

Task 230 wrote it for noon walk_near; task 235 (owner 2026-11-17, "I want 1 for now") made it judge WORST-CASE poses:
a low sun and torches both occur in play, so a gain that only shows at noon / with no torch near is not a gain for the
30 fps target. The poses and launches come from PERF_BUDGET.json ("poses", "launches"):
  worst:   walk_near_16, forest_eye_16 (16 deg morning sun), jungle_eye_noon (GPU pool full), torch_camp_night (28 torches)
  control: walk_near_noon, forest_eye_noon (startsun_off arm in the same launch): reported, never decide the verdict.
Prints ONE pose x step table (FULL / sun / sky fill / flood / rest, p50 ms and % of 33.3 ms), the worst pose per step,
the ratchet rows and BUDGET PASS | WARN | FAIL | NOT CLEAN. Exit 1 on FAIL, 3 on NOT CLEAN, 2 on a refused rebaseline.

  python3 -I tools/overnight/budget_compare.py --log walk=W.txt --log forest=F.txt --log jungle=J.txt --log night=N.txt
          [--budget PERF_BUDGET.json] [--dirty "<why>"] [--rebaseline "<who approved>"] [--stricter 0.8 --write-copy out.json]

Rows: higher is worse; FAIL when worse than the baseline by more than the tolerance, WARN at half. A row whose own noise
(steps: |FULL before - after|; FULL: the range of its FULL passes) is larger than its tolerance is downgraded FAIL -> WARN
("noisy"). Only WORST-CASE rows (and the hard limits) decide the verdict; control rows are info. Day flood is not timed:
since task 229 a FULL line `flood skip` means the plain kernel drew = 0 ms by construction (`twin` by day = PROBLEM).
rest = FULL - sun - sky fill - flood (shells, tier-0 march, FSR 3, CPU, overlap).
"""
import argparse, datetime, json, re, sys

NUM = r'(-?\d+(?:\.\d+)?)'
FRAME_MS = 33.3
STEP_COLS = ('sun_shadow', 'sky_fill', 'torch_flood', 'rest')
STEP_LABEL = {'sun_shadow': 'sun', 'sky_fill': 'sky fill', 'torch_flood': 'flood', 'rest': 'rest', 'shells': 'shells'}


def parse_costsplit(path, arms):
    """-> {path_name: {'full': {...}, 'arms': {arm: {...}}, 'mem': {...}, 'flood_full': set, 'deficit': int}}, notes"""
    txt = open(path, encoding='utf-8', errors='replace').read()
    paths, cur, in_tables, tcur = {}, None, False, None
    notes = []
    if 'NOTE: owner paused during this run' in txt:
        notes.append('owner paused during this run')
    for line in txt.splitlines():
        if line.startswith('COST SPLIT TABLES'):
            in_tables = True; continue
        if not in_tables:
            m = re.match(r'^== (\S+):', line)
            if m:
                cur = paths.setdefault(m.group(1), {'full': None, 'arms': {}, 'mem': None, 'flood_full': set(), 'deficit': None})
                continue
            if cur is None:
                continue
            m = re.search(r'streaming pinned at the middle pose .*deficit (\d+)', line)
            if m:
                cur['deficit'] = int(m.group(1))
            m = re.match(r'^\s+\d+ full\s+pass \d+ .* flood (\S+)$', line)
            if m:
                cur['flood_full'].add(m.group(1))
            if '[mem]' in line:
                g = lambda rx: re.search(rx, line)
                cpu, gpu, proc = g(r'CPU brick pool cap [\d,]+ peak ([\d,]+) \(valve ([\d,]+)'), \
                    g(r'GPU body pool cap [\d,]+ peak ([\d,]+) refused ([\d,]+)'), g(r'process now ([\d.]+) MB, peak ([\d.]+) MB')
                n = lambda s: int(s.replace(',', ''))
                cur['mem'] = {'cpu_pool_peak': n(cpu.group(1)) if cpu else None, 'cpu_valve': n(cpu.group(2)) if cpu else None,
                              'gpu_body_pool_peak': n(gpu.group(1)) if gpu else None, 'gpu_refusals': n(gpu.group(2)) if gpu else None,
                              'process_peak_mb': float(proc.group(2)) if proc else None}
            continue
        # tables
        s = line.rstrip()
        if not s or s.startswith('arm '):
            continue
        if s in paths or re.match(r'^[A-Za-z0-9_]+$', s):
            tcur = paths.setdefault(s, {'full': None, 'arms': {}, 'mem': None, 'flood_full': set(), 'deficit': None}); continue
        if tcur is None:
            continue
        m = re.match(r'^full\s+' + r'\s+'.join([NUM] * 4) + r'\s+(\d+) runs:(.*?)\(range ' + NUM + r'\)', s)
        if m:
            tcur['full'] = {'p50': float(m.group(1)), 'p95': float(m.group(2)), 'p99': float(m.group(3)),
                            'over33': float(m.group(4)), 'runs': int(m.group(5)), 'range': float(m.group(7))}
            continue
        for a in sorted(arms, key=len, reverse=True):
            if s.startswith(a) and (len(s) == len(a) or not (s[len(a)].isalnum() or s[len(a)] == '_')):
                t = s[len(a):].replace('/', ' ').split()
                try:
                    v = [float(x) for x in t[:12]]
                except ValueError:
                    break
                tcur['arms'][a] = {'p50': v[0], 'p95': v[1], 'p99': v[2], 'over33': v[3], 'x1': v[4], 'x2': v[5],
                                   'fb': v[6], 'fa': v[7], 'saved': v[8], 'noise': v[9]}
                break
    return paths, notes


def tol_of(spec, base):
    """'max(5 ms, 5 %)' / 'max(20, 10 %)' / 50 -> absolute tolerance at this baseline."""
    if isinstance(spec, (int, float)):
        return float(spec)
    m = re.match(r'max\(\s*' + NUM + r'[^,]*,\s*' + NUM + r'\s*%\s*\)', spec)
    if not m:
        raise ValueError('tolerance not understood: %r' % spec)
    return max(float(m.group(1)), float(m.group(2)) / 100.0 * abs(base or 0.0))


def get(d, *ks):
    for k in ks:
        if not isinstance(d, dict) or k not in d:
            return None
        d = d[k]
    return d


def all_arms(bud):
    arms = set()
    for L in bud['launches'].values():
        arms.update(L['arms'])
    return sorted(arms)


def build_current(logs, bud):
    """logs: {launch: parsed costsplit}. -> cur {'poses': {key: {...}}, 'memory': {...}}, problems"""
    cur, problems = {'poses': {}}, []
    for pd in bud['poses']:
        key, L = pd['key'], pd['launch']
        lg = logs.get(L)
        if lg is None:
            problems.append('%s: no %s log' % (key, L)); continue
        p = lg.get(pd['path'])
        if not p:
            problems.append('%s: %s log has no %s path' % (key, L, pd['path'])); continue
        src = pd.get('full', 'full')
        if src == 'full':
            if not p['full']:
                problems.append('%s: no FULL row' % key); continue
            f = p['full']; rng = f['range']
        else:
            f = p['arms'].get(src)
            if f is None:
                problems.append('%s: no %s row (its FULL)' % (key, src)); continue
            rng = f['noise']
        d = {'role': pd['role'], 'full_p50_ms': f['p50'], 'full_p95_ms': f['p95'], 'frames_over_33ms_pct': f['over33'],
             'full_noise_ms': rng, 'deficit': p['deficit'], 'step_ms': {}, 'step_noise_ms': {}}
        for step, arm in (pd.get('steps') or {}).items():
            a = p['arms'].get(arm)
            if a is None:
                problems.append('%s: no %s row' % (key, arm)); continue
            d['step_ms'][step] = a['saved']; d['step_noise_ms'][step] = a['noise']
        if pd.get('ceiling'):
            a = p['arms'].get(pd['ceiling'])
            if a is None:
                problems.append('%s: no %s row' % (key, pd['ceiling']))
            else:
                d['ceiling_p50_ms'] = a['p50']; d['ceiling_noise_ms'] = a['noise']
        fl = pd.get('flood')
        if fl == 'day':
            d['flood_state'] = sorted(p['flood_full'])
            if p['flood_full'] and p['flood_full'] <= {'skip', '-'}:
                d['step_ms']['torch_flood'] = 0.0   # 229: the plain kernel drew, flood step 0 by construction
            else:
                problems.append('%s: FULL flood state %s by day (229 expects skip): time a flood_off arm' % (key, d['flood_state']))
        elif fl:   # "tf_on-tl_none"
            a, b = (p['arms'].get(x) for x in fl.split('-'))
            if a is None or b is None:
                problems.append('%s: %s rows missing' % (key, fl))
            else:
                d['step_ms']['torch_flood'] = round(a['p50'] - b['p50'], 1)
                d['step_noise_ms']['torch_flood'] = max(a['noise'], b['noise'])
                d['points_full_p50_ms'] = p['full']['p50'] if p['full'] else None
        if pd['role'] == 'worst' and all(s in d['step_ms'] for s in ('sun_shadow', 'sky_fill', 'torch_flood')):
            d['step_ms']['rest'] = round(d['full_p50_ms'] - sum(d['step_ms'][s] for s in ('sun_shadow', 'sky_fill', 'torch_flood')), 1)
        cur['poses'][key] = d
    mem = {}
    for L, lg in logs.items():
        ms = [p['mem'] for p in lg.values() if p['mem']]
        if not ms:
            continue
        mem['%s_peak_mb' % L] = max(m['process_peak_mb'] for m in ms)
        mem['%s_gpu_refusals' % L] = max(m['gpu_refusals'] for m in ms)
        mem['%s_gpu_body_pool_peak' % L] = max(m['gpu_body_pool_peak'] for m in ms)
        mem['%s_cpu_pool_peak' % L] = max(m['cpu_pool_peak'] for m in ms)
        mem['cpu_valve'] = ms[-1]['cpu_valve']
    wn = get(logs, 'walk', 'walk_near', 'mem')
    if wn:
        mem['gpu_refusals_walk_near'] = wn['gpu_refusals']
    pk = [v for k, v in mem.items() if k.endswith('_peak_mb')]
    if pk:
        mem['process_peak_mb'] = max(pk)
    for k in ('gpu_refusals', 'gpu_body_pool_peak', 'cpu_pool_peak'):
        v = [x for kk, x in mem.items() if kk.endswith('_' + k) and kk != 'gpu_refusals_walk_near']
        if v:
            mem[k + '_max'] = max(v)
    cur['memory'] = mem
    return cur, problems


def compare(cur, bud, noise_downgrade=True):
    base, tol, tg, hl = bud.get('baseline', {}), bud['tolerance'], bud['targets'], bud['hard_limits']
    shares = {s['name']: s.get('share_ms') for s in bud['steps']}
    rows = []   # (name, value, base, delta, tol, share/target, status, note)

    def ratchet(name, val, b, tspec, extra='', noise=None, info=False):
        if val is None:
            rows.append((name, None, b, None, None, extra, 'info' if info else 'MISSING', '')); return
        if b is None:
            rows.append((name, val, None, None, None, extra, 'info' if info else 'NEW', 'no baseline yet')); return
        t = tol_of(tspec, b)
        dlt = val - b
        st = 'FAIL' if dlt > t else 'WARN' if dlt > 0.5 * t else 'PASS'
        note = ''
        if info:
            st, note = 'info', ('would be ' + ('FAIL' if dlt > t else 'WARN' if dlt > 0.5 * t else 'ok'))
        elif st != 'PASS' and noise_downgrade and noise is not None and noise > t:
            note = 'noisy: noise %.1f > tol %.1f' % (noise, t)
            st = 'WARN'
        elif dlt < -t:
            note = 'better than baseline (a perf task may rebaseline)'
        rows.append((name, val, b, dlt, t, extra, st, note))

    def hard(name, val, limit, extra=''):
        if val is None:
            rows.append((name, None, limit, None, None, extra, 'MISSING', '')); return
        rows.append((name, val, limit, val - limit, 0, extra, 'FAIL' if val > limit else 'PASS', 'hard limit %g' % limit))

    for pd in bud['poses']:
        k = pd['key']; d = get(cur, 'poses', k) or {}; b = get(base, 'poses', k) or {}
        info = pd['role'] != 'worst'
        tag = '%s%s' % (k, '' if not info else ' (control)')
        v = d.get('full_p50_ms')
        ratchet('%s FULL p50' % tag, v, b.get('full_p50_ms'), tol['full_p50_ms'],
                '' if v is None else 'target %.1f (%+.1f)' % (tg['frame_p50_ms'], v - tg['frame_p50_ms']), noise=d.get('full_noise_ms'), info=info)
        if info:
            continue
        v = d.get('full_p95_ms')
        ratchet('%s FULL p95' % tag, v, b.get('full_p95_ms'), tol['full_p95_ms'],
                'target %.1f (%+.1f)' % (tg['frame_p95_ms'], (v or 0) - tg['frame_p95_ms']), noise=d.get('full_noise_ms'))
        for s in ('sun_shadow', 'sky_fill', 'torch_flood', 'shells'):
            if s not in (d.get('step_ms') or {}) and s not in (pd.get('steps') or {}):
                continue
            val = get(d, 'step_ms', s); sh = shares.get(s)
            ex = '' if sh is None else 'share %.1f%s' % (sh, ' OVER SHARE' if val is not None and val > sh + 1e-9 else '')
            if s == 'torch_flood' and pd.get('flood') == 'day':
                ex = 'skip by day (229): 0 by construction'
            ratchet('%s step %s' % (tag, STEP_LABEL[s]), val, get(b, 'step_ms', s), tol['step_delta_ms'], ex,
                    noise=get(d, 'step_noise_ms', s))
        if 'ceiling_p50_ms' in d or pd.get('ceiling'):
            ratchet('%s ceiling p50' % tag, d.get('ceiling_p50_ms'), b.get('ceiling_p50_ms'), tol['full_p50_ms'],
                    '(sky probe + sun + shells off)', noise=d.get('ceiling_noise_ms'))
    m, bm = cur.get('memory', {}), base.get('memory', {})
    pk = ' / '.join('%s %s' % (L, m.get('%s_peak_mb' % L)) for L in bud['launches'])
    ratchet('memory process peak MB', m.get('process_peak_mb'), bm.get('process_peak_mb'), tol['process_peak_mb'], pk)
    hard('memory process peak MB (hard)', m.get('process_peak_mb'), hl['process_peak_mb'])
    hard('GPU body refusals at walk_near (hard)', m.get('gpu_refusals_walk_near'), hl['gpu_body_refusals_at_walk_near'])
    ratchet('GPU body refusals (max of launches)', m.get('gpu_refusals_max'), bm.get('gpu_refusals_max'), tol['gpu_refusals'],
            ' / '.join('%s %s' % (L, m.get('%s_gpu_refusals' % L)) for L in bud['launches']))
    rows.append(('GPU body pool peak (max)', m.get('gpu_body_pool_peak_max'), bm.get('gpu_body_pool_peak_max'), None, None, 'cap 254,400', 'info', ''))
    rows.append(('CPU brick pool peak (max)', m.get('cpu_pool_peak_max'), bm.get('cpu_pool_peak_max'), None, None, 'valve %s' % m.get('cpu_valve'), 'info', ''))
    return rows


def cell(ms):
    return '-' if ms is None else '%.1f (%d%%)' % (ms, round(100.0 * ms / FRAME_MS))


def print_matrix(cur, bud):
    """The pose x step table, p50 ms and the share of 33.3 ms; then the worst pose per step and the worst pose's top 3."""
    print('POSE x STEP, p50 ms (%% of %.1f ms = 30 fps)' % FRAME_MS)
    print('%-18s %-7s %14s %14s %14s %14s %14s   %6s %5s %7s  %s' % ('pose', 'role', 'FULL', 'sun', 'sky fill', 'flood',
                                                                     'rest', 'p95', '>33%', 'deficit', 'note'))
    for pd in bud['poses']:
        d = get(cur, 'poses', pd['key'])
        if not d:
            print('%-18s %-7s %14s' % (pd['key'], pd['role'], 'MISSING')); continue
        st = d['step_ms']
        note = []
        if 'shells' in st: note.append('of rest: shells %.1f' % st['shells'])
        if 'ceiling_p50_ms' in d: note.append('ceiling %.1f' % d['ceiling_p50_ms'])
        if d.get('points_full_p50_ms') is not None: note.append('FULL = tf_on; sun/sky vs the point-light FULL %.1f' % d['points_full_p50_ms'])
        if d.get('flood_state'): note.append('flood %s' % '/'.join(d['flood_state']))
        print('%-18s %-7s %14s %14s %14s %14s %14s   %6.1f %5.1f %7s  %s' % (
            pd['key'], pd['role'], cell(d['full_p50_ms']), cell(st.get('sun_shadow')), cell(st.get('sky_fill')),
            cell(st.get('torch_flood')), cell(st.get('rest')), d['full_p95_ms'], d['frames_over_33ms_pct'],
            '-' if d['deficit'] is None else d['deficit'], '; '.join(note)))
    shares = {s['name']: s.get('share_ms') for s in bud['steps']}
    worst = [(k, d) for k, d in cur['poses'].items() if d['role'] == 'worst']
    if not worst:
        return
    print()
    print('WORST POSE PER STEP (these rows decide the verdict):')
    k, d = max(worst, key=lambda kd: kd[1]['full_p50_ms'])
    print('  %-10s %-18s %6.1f ms = %.1fx %.1f ms (%+.1f ms from 30 fps)' % ('FULL', k, d['full_p50_ms'], d['full_p50_ms'] / FRAME_MS,
                                                                           FRAME_MS, d['full_p50_ms'] - FRAME_MS))
    for s in STEP_COLS:
        c = [(kk, dd['step_ms'][s]) for kk, dd in worst if s in dd['step_ms']]
        if not c:
            continue
        kk, v = max(c, key=lambda x: x[1])
        sh = shares.get(s)
        print('  %-10s %-18s %6.1f ms = %3d%% of %.1f ms%s' % (STEP_LABEL[s], kk, v, round(100 * v / FRAME_MS), FRAME_MS,
              '' if sh is None else '; share %.1f%s' % (sh, ' OVER SHARE' if v > sh else '')))
    top = sorted(((STEP_LABEL[s], d['step_ms'][s]) for s in STEP_COLS if s in d['step_ms']), key=lambda x: -x[1])[:3]
    print('  top 3 steps at the worst pose (%s): %s' % (k, ', '.join('%s %.1f' % t for t in top)))


def fmt(v, w=8):
    if v is None:
        return '-'.rjust(w)
    if isinstance(v, float) and not v.is_integer() or isinstance(v, float) and abs(v) < 1000:
        return ('%.1f' % v).rjust(w)
    return ('{:,}'.format(int(v))).rjust(w)


def set_count(path, n, why):
    """Line 1 of BUDGET_COUNT = the count; the other lines are kept (notes)."""
    if not path:
        return
    try:
        rest = open(path).read().splitlines()[1:]
    except OSError:
        rest = []
    stamp = datetime.datetime.now().strftime('%Y-%m-%d %H:%M')
    rest = ['# %s: set to %d by run-budget-check.sh (%s)' % (stamp, n, why)] + rest
    open(path, 'w').write('\n'.join([str(n)] + rest[:20]) + '\n')
    print('%s -> %d' % (path, n))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--budget', default='PERF_BUDGET.json')
    ap.add_argument('--log', action='append', default=[], help='LAUNCH=costsplit.txt (walk, forest, jungle, night)')
    ap.add_argument('--dirty', default=None)
    ap.add_argument('--rebaseline', default=None)
    ap.add_argument('--stricter', type=float, default=None); ap.add_argument('--write-copy', default=None)
    ap.add_argument('--settings', default='')
    ap.add_argument('--count-file', default=None, help='BUDGET_COUNT: PASS / rebaseline -> 0, WARN -> 2 (3 more tasks), else unchanged')
    a = ap.parse_args()
    bud = json.load(open(a.budget))
    if a.stricter is not None:
        def scale(x):
            if isinstance(x, bool): return x
            if isinstance(x, int): return int(round(x * a.stricter))
            if isinstance(x, float): return round(x * a.stricter, 1)
            if isinstance(x, dict): return {k: (v if k in ('date', 'sources', 'settings', 'role') else scale(v)) for k, v in x.items()}
            return x
        bud['baseline'] = scale(bud['baseline'])
        bud['status'] = 'TEST COPY: baseline x %.2f (failure-path test)' % a.stricter
        json.dump(bud, open(a.write_copy, 'w'), indent=2)
        print('wrote %s (baseline x %.2f)' % (a.write_copy, a.stricter)); return 0
    arms = all_arms(bud)
    logs, paths, notes = {}, {}, []
    for kv in a.log:
        name, _, path = kv.partition('=')
        if name not in bud['launches']:
            print('unknown launch %r (PERF_BUDGET.json launches: %s)' % (name, ', '.join(bud['launches']))); return 2
        logs[name], n = parse_costsplit(path, arms); paths[name] = path
        notes += ['%s: %s' % (name, x) for x in n]
    cur, problems = build_current(logs, bud)
    rows = compare(cur, bud)
    print('BUDGET CHECK (tasks 230, 235: worst-case poses; PERF_BUDGET_GUARD.md sec. 3) -- %s' % datetime.datetime.now().strftime('%Y-%m-%d %H:%M'))
    print('budget file %s (status: %s; baseline %s)' % (a.budget, bud.get('status', '?')[:70], get(bud, 'baseline', 'date')))
    for L in bud['launches']:
        print('%-6s log %s' % (L, paths.get(L)))
    if a.settings: print('settings: %s' % a.settings)
    print()
    print_matrix(cur, bud)
    print()
    print('%-44s %9s %9s %8s %7s  %-6s  %s' % ('number', 'now', 'baseline', 'delta', 'tol', 'status', 'share / target / note'))
    for (n, v, b, d, t, ex, st, note) in rows:
        print('%-44s %9s %9s %8s %7s  %-6s  %s' % (n, fmt(v).strip(), fmt(b).strip(), '-' if d is None else '%+.1f' % d,
                                                  '-' if t is None else '%.1f' % t, st, '; '.join(x for x in (ex, note) if x)))
    for p in problems:
        print('PROBLEM: ' + p)
    dirty = a.dirty or ('; '.join(notes) if notes else None)
    statuses = [r[6] for r in rows]
    if dirty:
        verdict = 'NOT CLEAN'
    elif 'FAIL' in statuses or 'MISSING' in statuses or problems:
        verdict = 'FAIL'
    elif 'WARN' in statuses:
        verdict = 'WARN'
    else:
        verdict = 'PASS'
    print()
    if dirty: print('not clean: %s' % dirty)
    print('verdict = the WORST-CASE rows + hard limits (owner 2026-11-17); control (noon) rows are info only')
    print('BUDGET %s' % verdict)
    if a.rebaseline:
        hard_fail = any(r[6] == 'FAIL' and '(hard)' in r[0] for r in rows)
        if dirty or problems or hard_fail:
            print('REBASELINE REFUSED: %s' % ('not clean' if dirty else 'log problems' if problems else 'a hard limit failed'))
            return 2
        nb = dict(cur)
        nb['date'] = datetime.date.today().isoformat()
        nb['sources'] = [paths[L] for L in bud['launches'] if L in paths]
        nb['settings'] = a.settings
        bud['baseline'] = nb
        bud['status'] = 'ACCEPTED -- baseline from run-budget-check.sh, %s (%s)' % (nb['date'], a.rebaseline)
        bud.setdefault('history', []).append({'date': nb['date'], 'by': a.rebaseline,
                                              'what': 'rebaseline by run-budget-check.sh; verdict vs the previous baseline: %s' % verdict})
        json.dump(bud, open(a.budget, 'w'), indent=2); open(a.budget, 'a').write('\n')
        print('REBASELINED %s (%s)' % (a.budget, a.rebaseline))
        set_count(a.count_file, 0, 'rebaseline (%s)' % a.rebaseline)
        return 0
    if verdict == 'PASS':
        set_count(a.count_file, 0, 'BUDGET PASS')
    elif verdict == 'WARN':
        set_count(a.count_file, 2, 'BUDGET WARN: next check after 3 more drawing/content tasks (GUARD sec. 6.4)')
    return {'FAIL': 1, 'NOT CLEAN': 3}.get(verdict, 0)


if __name__ == '__main__':
    sys.exit(main())
