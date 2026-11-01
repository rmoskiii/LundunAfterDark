"""Tests for validate_world.py: a clean file must pass, and each deliberately broken copy must fail the right rule.
Usage: python3 test_validator.py LundunWorld.json"""
import copy, json, sys
from validate_world import validate

def fails(W):
    rep = validate(W)
    return {(code, msg) for code, _, f, _ in rep.rows for msg in f}

def run(path, fixtures_dir=None):
    base = json.load(open(path))
    clean = fails(base)
    print(f'clean file → failing rules: {sorted({c for c, _ in clean}) or "none"}')
    ok = not clean
    J = lambda W, i: next(j for j in W['junctions'] if j['id'] == i)
    R = lambda W, i: next(r for r in W['roads'] if r['id'] == i)
    cases = [
        ('duplicate road id',            'V2',  lambda W: W['roads'].append(copy.deepcopy(R(W, 'R05')))),
        ('reference to unknown junction', 'V3',  lambda W: R(W, 'R19')['junctions'].append('J99')),
        ('one-way membership (J51 drops R19)', 'V4', lambda W: J(W, 'J51')['roads'].remove('R19')),
        ('junction moved 3 m off its road', 'V5',  lambda W: J(W, 'J10').update(x=J(W, 'J10')['x'] + 3)),
        ('wrong arm count on J01',        'V6',  lambda W: J(W, 'J01').update(arms=2)),
        ('junction list out of order',    'V7',  lambda W: R(W, 'R03')['junctions'].reverse()),
        ('road shortened → dead end',     'V7',  lambda W: R(W, 'R25')['points'][0].update(x=90)),
        ('typed length wrong',            'V8',  lambda W: R(W, 'R04').update(lengthM=R(W, 'R04')['lengthM'] + 5)),
        ('bridge water span wrong',       'V9',  lambda W: W['bridges'][1].update(waterSpanM=90)),
        ('bridge end in the river',       'V9',  lambda W: (R(W, 'B1')['points'][1].update(z=620))),
        ('ordinary road into the river',  'V10', lambda W: R(W, 'R04')['points'][1].update(z=600)),
        ('crossing without a junction',   'V10', lambda W: W['roads'].append(dict(id='R26', name='Test', roadClass='street', width=10, lengthM=100,
                                                    points=[dict(x=650, z=1000), dict(x=650, z=900)], junctions=[]))),
        ('network split in two',          'V11', lambda W: [J(W, x)['roads'].remove(rid) or R(W, rid)['junctions'].remove(x)
                                                    for rid, x in (('B1', 'J24'), ('B2', 'J27'), ('B3', 'J40'), ('B4', 'J26'), ('B5', 'J32'))]),
        ('park on a road',                'V12', lambda W: W['parks'].append(dict(name='Bad Park', polygon=[dict(x=540, z=900), dict(x=580, z=900), dict(x=580, z=940), dict(x=540, z=940)]))),
        ('police spawn on a junction',    'V13', lambda W: next(s for s in W['spawns'] if s['id'] == 'SP_POLICE_1').update(x=430, z=40)),
        ('road through existing street',  'V14', lambda W: R(W, 'R19')['points'][0].update(x=860)),
        ('typed summary count wrong',     'V15', lambda W: W['validation'].update(roadEntries=25)),
    ]
    for label, rule, mutate in cases:
        W = copy.deepcopy(base); mutate(W); got = {c for c, _ in fails(W) - clean}
        if fixtures_dir:
            import os, re as _re; os.makedirs(fixtures_dir, exist_ok=True)
            json.dump(W, open(os.path.join(fixtures_dir, f'{rule}_{_re.sub(r"[^a-z0-9]+", "_", label.lower()).strip("_")}.json'), 'w'), indent=1)
        hit = rule in got; ok &= hit
        print(f"{'ok  ' if hit else 'MISS'} {label:38s} expected {rule}, newly failing: {sorted(got) or 'none'}")
    print('\nALL TESTS PASS' if ok else '\nSOME TESTS FAILED'); return ok

if __name__ == '__main__':
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    fx = next((a.split('=', 1)[1] for a in sys.argv[1:] if a.startswith('--write-fixtures=')), None)
    sys.exit(0 if run(args[0] if args else 'LundunWorld.json', fx) else 1)
