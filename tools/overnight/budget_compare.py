#!/usr/bin/env python3
"""budget_compare.py -- task 230: the comparison half of tools/overnight/run-budget-check.sh (PERF_BUDGET_GUARD.md sec. 3).

Reads one DAY cost split (walk_near + forest_eye, arms = PERF_BUDGET.json steps' off_arm) and one NIGHT cost split
(-costpaths torch, arms tf_on + tl_none), compares them with PERF_BUDGET.json's baseline (a ratchet) and hard limits,
prints ONE table and ends with BUDGET PASS | WARN | FAIL | NOT CLEAN. Exit 1 on FAIL, 3 on NOT CLEAN, 0 otherwise.

  python3 -I tools/overnight/budget_compare.py --day D.txt --night N.txt [--budget PERF_BUDGET.json]
          [--dirty "<why>"] [--rebaseline "<who approved>"] [--stricter 0.8 --write-copy out.json]

--stricter F --write-copy OUT: write a copy of the budget file with every baseline number scaled by F (the failure-path
test: 0.8 = a baseline 20 % stricter), then exit. Row rules (sec. 2): higher is worse; FAIL when the number is worse than
its baseline by more than its tolerance, WARN at half; a step whose own noise (|FULL before - after|) is larger than its
tolerance is downgraded FAIL -> WARN ("noisy"). Targets and shares are reported as distance only. Hard limits fail always.
"""
import argparse, datetime, json, re, sys

NUM = r'(-?\d+(?:\.\d+)?)'


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


def build_current(day, night, steps, forest=None):
    cur, problems = {}, []
    wn = day.get('walk_near')
    if not wn or not wn['full']:
        problems.append('day log: no walk_near FULL row')
    else:
        d = {'full_p50_ms': wn['full']['p50'], 'full_p95_ms': wn['full']['p95'], 'full_p99_ms': wn['full']['p99'],
             'frames_over_33ms_pct': wn['full']['over33'], 'full_range_ms': wn['full']['range'],
             'step_delta_ms': {}, 'step_noise_ms': {}, 'flood_full': sorted(wn['flood_full']), 'deficit': wn['deficit']}
        for st in steps:
            a = wn['arms'].get(st['off_arm'])
            if a is None:
                problems.append('day log: walk_near has no %s row' % st['off_arm']); continue
            if st['name'] == 'ceiling':
                d['ceiling_p50_ms'] = a['p50']; d['ceiling_noise_ms'] = a['noise']
            else:
                d['step_delta_ms'][st['name']] = a['saved']; d['step_noise_ms'][st['name']] = a['noise']
        cur['walk_near'] = d
    fe = (forest or day).get('forest_eye')   # the FOREST launch (FULL only) when given; else the day log's
    if fe and fe['full']:
        cur['forest_eye'] = {'full_p50_ms': fe['full']['p50'], 'full_p95_ms': fe['full']['p95'], 'full_p99_ms': fe['full']['p99'],
                             'frames_over_33ms_pct': fe['full']['over33'], 'deficit': fe['deficit'],
                             'step_delta_ms': {st['name']: fe['arms'][st['off_arm']]['saved'] for st in steps
                                               if st['name'] != 'ceiling' and st['off_arm'] in fe['arms']}}
    tc = night.get('torch_camp') if night else None
    if night is not None:
        if not tc or 'tf_on' not in tc['arms'] or 'tl_none' not in tc['arms']:
            problems.append('night log: torch_camp tf_on / tl_none rows missing')
        else:
            on, none = tc['arms']['tf_on'], tc['arms']['tl_none']
            cur['torch'] = {'play_p50_ms': on['p50'], 'play_p95_ms': on['p95'], 'points_p50_ms': tc['full']['p50'] if tc['full'] else None,
                            'none_p50_ms': none['p50'], 'flood_step_ms': round(on['p50'] - none['p50'], 1),
                            'flood_noise_ms': max(on['noise'], none['noise'])}
    mem = {}
    dm = [p['mem'] for p in day.values() if p['mem']]
    fm = [p['mem'] for p in (forest or {}).values() if p['mem']]
    nm = [p['mem'] for p in (night or {}).values() if p['mem']]
    if wn and wn['mem']:
        mem['gpu_refusals_walk_near'] = wn['mem']['gpu_refusals']
    if dm:
        mem['day_peak_mb'] = max(m['process_peak_mb'] for m in dm)
        mem['gpu_body_pool_peak'] = max(m['gpu_body_pool_peak'] for m in dm)
        mem['gpu_refusals_after_paths'] = max(m['gpu_refusals'] for m in dm)
        mem['cpu_pool_peak'] = max(m['cpu_pool_peak'] for m in dm)
        mem['cpu_valve'] = dm[-1]['cpu_valve']
    if nm:
        mem['night_peak_mb'] = max(m['process_peak_mb'] for m in nm)
    if fm:
        mem['forest_peak_mb'] = max(m['process_peak_mb'] for m in fm)
    pk = [v for v in (mem.get('day_peak_mb'), mem.get('forest_peak_mb'), mem.get('night_peak_mb')) if v is not None]
    if pk:
        mem['process_peak_mb'] = max(pk)
    cur['memory'] = mem
    return cur, problems


def get(d, *ks):
    for k in ks:
        if not isinstance(d, dict) or k not in d:
            return None
        d = d[k]
    return d


def compare(cur, bud, noise_downgrade=True):
    base, tol, tg, hl = bud.get('baseline', {}), bud['tolerance'], bud['targets'], bud['hard_limits']
    shares = {s['name']: s.get('share_ms') for s in bud['steps']}
    rows = []   # (name, value, base, delta, tol, share/target, status, note)

    def ratchet(name, val, b, tspec, extra='', noise=None, info=False, unit=''):
        if val is None:
            rows.append((name, None, b, None, None, extra, 'MISSING', '')); return
        if b is None:
            rows.append((name, val, None, None, None, extra, 'NEW', 'no baseline yet')); return
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

    wn, bwn = cur.get('walk_near', {}), base.get('walk_near', {})
    ratchet('walk_near FULL p50 ms', wn.get('full_p50_ms'), bwn.get('full_p50_ms'), tol['full_p50_ms'],
            'target %.1f (%+.1f)' % (tg['frame_p50_ms'], (wn.get('full_p50_ms') or 0) - tg['frame_p50_ms']))
    ratchet('walk_near FULL p95 ms', wn.get('full_p95_ms'), bwn.get('full_p95_ms'), tol['full_p95_ms'],
            'target %.1f (%+.1f)' % (tg['frame_p95_ms'], (wn.get('full_p95_ms') or 0) - tg['frame_p95_ms']))
    v = wn.get('frames_over_33ms_pct')
    rows.append(('walk_near frames > 33 ms %', v, bwn.get('frames_over_33ms_pct'), None, None,
                 'target %.1f' % tg['frames_over_33ms_pct'], 'info', 'target distance only'))
    for st in bud['steps']:
        n = st['name']
        if n == 'ceiling':
            ratchet('walk_near ceiling p50 ms', wn.get('ceiling_p50_ms'), bwn.get('ceiling_p50_ms'), tol['full_p50_ms'],
                    '(sky probe + sun + shells off)', noise=wn.get('ceiling_noise_ms'))
            continue
        val = get(wn, 'step_delta_ms', n)
        sh = 0.0 if n == 'torch_flood' else shares.get(n)
        ex = '' if sh is None else 'share %.1f%s' % (sh, ' OVER SHARE' if val is not None and val > sh + 1e-9 and sh > 0 else '')
        if n == 'torch_flood':
            ex = 'day share 0 (night row below)'
        ratchet('walk_near step %s ms' % n, val, get(bwn, 'step_delta_ms', n), tol['step_delta_ms'], ex,
                noise=get(wn, 'step_noise_ms', n))
    t, bt = cur.get('torch', {}), base.get('torch', {})
    ratchet('torch night play p50 ms (tf_on)', t.get('play_p50_ms'), bt.get('play_p50_ms'), tol['full_p50_ms'],
            'target %.1f (%+.1f)' % (tg['frame_p50_ms'], (t.get('play_p50_ms') or 0) - tg['frame_p50_ms']))
    fs = t.get('flood_step_ms'); sh = shares.get('torch_flood')
    ratchet('torch night step torch_flood ms', fs, bt.get('flood_step_ms'), tol['step_delta_ms'],
            'share %.1f%s (tf_on - tl_none)' % (sh, ' OVER SHARE' if fs is not None and fs > sh else ''), noise=t.get('flood_noise_ms'))
    fe, bfe = cur.get('forest_eye', {}), base.get('forest_eye', {})
    ratchet('forest_eye FULL p50 ms (secondary)', fe.get('full_p50_ms'), bfe.get('full_p50_ms'), tol['full_p50_ms'],
            'deficit %s' % fe.get('deficit'), info=True)
    for n, val in sorted((fe.get('step_delta_ms') or {}).items()):
        ratchet('forest_eye step %s ms (secondary)' % n, val, get(bfe, 'step_delta_ms', n), tol['step_delta_ms'],
                '' if shares.get(n) is None else 'share %.1f' % shares[n], info=True)
    m, bm = cur.get('memory', {}), base.get('memory', {})
    ratchet('memory process peak MB', m.get('process_peak_mb'), bm.get('process_peak_mb'), tol['process_peak_mb'],
            'day %s / forest %s / night %s' % (m.get('day_peak_mb'), m.get('forest_peak_mb'), m.get('night_peak_mb')))
    hard('memory process peak MB (hard)', m.get('process_peak_mb'), hl['process_peak_mb'])
    hard('GPU body refusals at walk_near (hard)', m.get('gpu_refusals_walk_near'), hl['gpu_body_refusals_at_walk_near'])
    ratchet('GPU body refusals after day paths', m.get('gpu_refusals_after_paths'), bm.get('gpu_refusals_after_paths'), tol['gpu_refusals'])
    rows.append(('GPU body pool peak', m.get('gpu_body_pool_peak'), bm.get('gpu_body_pool_peak'), None, None, 'cap 254,400', 'info', ''))
    rows.append(('CPU brick pool peak', m.get('cpu_pool_peak'), bm.get('cpu_pool_peak'), None, None, 'valve %s' % m.get('cpu_valve'), 'info', ''))
    return rows


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
    ap.add_argument('--day'); ap.add_argument('--night'); ap.add_argument('--forest', default=None)
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
            if isinstance(x, dict): return {k: (v if k in ('date', 'sources', 'settings') else scale(v)) for k, v in x.items()}
            return x
        bud['baseline'] = scale(bud['baseline'])
        bud['status'] = 'TEST COPY: baseline x %.2f (task 230 failure-path test)' % a.stricter
        json.dump(bud, open(a.write_copy, 'w'), indent=2)
        print('wrote %s (baseline x %.2f)' % (a.write_copy, a.stricter)); return 0
    steps = bud['steps']
    arms = [s['off_arm'] for s in steps] + ['tf_on', 'tl_none']
    day, dn = parse_costsplit(a.day, arms)
    night, nn = parse_costsplit(a.night, arms) if a.night else (None, [])
    forest = parse_costsplit(a.forest, arms)[0] if a.forest else None   # secondary: its notes never make the check dirty
    cur, problems = build_current(day, night, steps, forest)
    rows = compare(cur, bud)
    print('BUDGET CHECK (task 230; PERF_BUDGET_GUARD.md sec. 3) -- %s' % datetime.datetime.now().strftime('%Y-%m-%d %H:%M'))
    print('budget file %s (status: %s; baseline %s)' % (a.budget, bud.get('status', '?')[:60], get(bud, 'baseline', 'date')))
    print('day log %s' % a.day); print('night log %s' % a.night); print('forest log %s' % a.forest)
    if a.settings: print('settings: %s' % a.settings)
    print('walk_near FULL runs range %s ms; FULL flood state %s (day: expect skip)' % (get(cur, 'walk_near', 'full_range_ms'), get(cur, 'walk_near', 'flood_full')))
    print()
    print('%-38s %9s %9s %8s %7s  %-6s  %s' % ('number', 'now', 'baseline', 'delta', 'tol', 'status', 'share / target / note'))
    for (n, v, b, d, t, ex, st, note) in rows:
        print('%-38s %9s %9s %8s %7s  %-6s  %s' % (n, fmt(v).strip(), fmt(b).strip(), '-' if d is None else '%+.1f' % d,
                                                  '-' if t is None else '%.1f' % t, st, '; '.join(x for x in (ex, note) if x)))
    for p in problems:
        print('PROBLEM: ' + p)
    dirty = a.dirty or ('; '.join(dn + nn) if (dn or nn) else None)
    statuses = [r[6] for r in rows]
    if dirty:
        verdict = 'NOT CLEAN'
    elif 'FAIL' in statuses or problems:
        verdict = 'FAIL'
    elif 'WARN' in statuses:
        verdict = 'WARN'
    else:
        verdict = 'PASS'
    print()
    if dirty: print('not clean: %s' % dirty)
    print('BUDGET %s' % verdict)
    if a.rebaseline:
        hard_fail = any(r[6] == 'FAIL' and '(hard)' in r[0] for r in rows)
        if dirty or problems or hard_fail:
            print('REBASELINE REFUSED: %s' % ('not clean' if dirty else 'log problems' if problems else 'a hard limit failed'))
            return 2
        nb = dict(cur)
        nb['date'] = datetime.date.today().isoformat()
        nb['sources'] = [a.day, a.night]
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
