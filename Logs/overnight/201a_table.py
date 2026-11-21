import re,sys
def num(s): return int(s.replace(',',''))
def parse(path):
    out=[];cur=None
    for ln in open(path):
        if '[mem] STEP 14' in ln:
            cur={}; m=re.search(r'process now ([\d.]+) MB, peak ([\d.]+)',ln); cur['peak']=m.group(2)
        if cur is None: continue
        m=re.search(r'\[gpupool\] in use ([\d,]+) of [\d,]+ \(free ([\d,]+)\).*refused total ([\d,]+).*refused now ([\d,]+).*box bodies mapped ([\d,]+).*not mapped in held chunks ([\d,]+).*refused chunks\' dense bricks ([\d,]+)',ln)
        if m: cur.update(inuse=num(m.group(1)),free=num(m.group(2)),reftot=num(m.group(3)),refnow=num(m.group(4)),mapped=num(m.group(5)),pend=num(m.group(6)),refd=num(m.group(7)))
        m=re.search(r'src (\w+)\s+([\d,]+) .*one-value ([\d,]+), surface ([\d,]+), fluid-mixed ([\d,]+), solid-mixed ([\d,]+) \| hidden inner ([\d,]+), hidden any ([\d,]+)',ln)
        if m: cur[m.group(1)]=num(m.group(2)); cur.setdefault('surf',0); cur['surf']+=num(m.group(4)); cur.setdefault('solid',0); cur['solid']+=num(m.group(6)); cur.setdefault('fmix',0); cur['fmix']+=num(m.group(5))
        m=re.search(r'hidden \(all-opaque.*inner ([\d,]+) .*chunk-border bricks ([\d,]+)',ln)
        if m: cur['hidI']=num(m.group(1)); cur['hidA']=num(m.group(2))
        m=re.search(r'duplicates: distinct bodies ([\d,]+) of ([\d,]+)',ln)
        if m: cur['dupes']=num(m.group(2))-num(m.group(1))
        m=re.search(r'sealed \(task 201a.*held chunks ([\d,]+)',ln)
        if m: cur['sealed']=num(m.group(1))
        m=re.search(r'WSPOSE fixed_(\w+)\|',ln)
        if m and cur is not None: cur['pose']=m.group(1); out.append(cur); cur=None
    return out
hdr="pose            arm      in use  free    ref now ref total demand  terrain  tree    ore     fluid   surface solid-mx fl-mix  hid-in  hid-any dupes   sealed  peak MB"
print(hdr)
for tag in sys.argv[1:]:
    for r in parse(f'Screenshots/201a/lookreview_{tag}.txt'):
        dem=r['mapped']+r['refd']+r['pend']+r.get('sealed',0)
        print(f"{r['pose']:<15} {tag:<8} {r['inuse']:>7,} {r['free']:>7,} {r['refnow']:>7} {r['reftot']:>9,} {dem:>7,} {r.get('terrain',0):>8,} {r.get('tree',0):>7,} {r.get('ore',0):>7,} {r.get('fluid',0):>7,} {r['surf']:>7,} {r['solid']:>8,} {r['fmix']:>7,} {r['hidI']:>7,} {r['hidA']:>7,} {r['dupes']:>7,} {r.get('sealed',0):>7,} {r['peak']}")
