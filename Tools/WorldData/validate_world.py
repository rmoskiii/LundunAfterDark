"""Independent validator for LundunWorld.json.
Reads ONLY the JSON file (never world.py or the exporter), so the data cannot quietly agree with itself.
Usage:  python3 validate_world.py LundunWorld.json
Exit code 0 = all rules pass, 1 = at least one FAIL. Rules are defined in WORLD_DATA_CONTRACT.md (V1–V16)."""
import json, math, sys, re

TOL_ON_ROAD = 0.10       # m: a junction must lie this close to every road it lists
TOL_LENGTH = 0.50        # m: declared lengths vs recomputed
TOL_CHAINAGE = 0.10      # m
WALL_CLEAR = 6.0         # m: land between an ordinary road's edge and the embankment wall
POLY_CLEAR = 0.0         # m: districts/parks may touch but not enter water+wall
MIN_JUNCTION_GAP = 25.0  # m along a road
POLICE_SPAWN_GAP = 40.0  # m from any junction

class Report:
    def __init__(self): self.rows = []
    def rule(self, code, title, fails, info=None):
        self.rows.append((code, title, fails, info))
    def print(self):
        for code, title, fails, info in self.rows:
            print(f"{'PASS' if not fails else 'FAIL'}  {code:4s} {title}" + (f"  ({info})" if info and not fails else ''))
            for f in fails[:12]: print(f'        ✗ {f}')
            if len(fails) > 12: print(f'        … and {len(fails) - 12} more')
        n = sum(1 for r in self.rows if r[2]); print(f'\n{len(self.rows) - n}/{len(self.rows)} rules pass' + ('' if not n else f' — {n} FAIL'))
        return n == 0

# ---------- geometry helpers (plan coordinates are (x, z)) ----------
def P(o): return (float(o['x']), float(o['z']))
def seg_closest(p, a, b):
    dx, dz = b[0] - a[0], b[1] - a[1]; L2 = dx * dx + dz * dz
    t = 0.0 if L2 == 0 else max(0.0, min(1.0, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dz) / L2))
    q = (a[0] + t * dx, a[1] + t * dz); return math.dist(p, q), t
def poly_len(pts): return sum(math.dist(a, b) for a, b in zip(pts, pts[1:]))
def chainage(p, pts):
    """Distance along the polyline to the closest point to p, and that distance from the line."""
    best = (1e18, 0.0); acc = 0.0
    for a, b in zip(pts, pts[1:]):
        d, t = seg_closest(p, a, b)
        if d < best[0] - 1e-9: best = (d, acc + t * math.dist(a, b))
        acc += math.dist(a, b)
    return best[1], best[0]
def dist_to_line(p, pts): return min(seg_closest(p, a, b)[0] for a, b in zip(pts, pts[1:]))
def seg_intersect(a, b, c, d):
    def cr(o, p, q): return (p[0] - o[0]) * (q[1] - o[1]) - (p[1] - o[1]) * (q[0] - o[0])
    d1, d2, d3, d4 = cr(c, d, a), cr(c, d, b), cr(a, b, c), cr(a, b, d)
    if d1 * d2 < 0 and d3 * d4 < 0:
        t = d1 / (d1 - d2); return (a[0] + t * (b[0] - a[0]), a[1] + t * (b[1] - a[1]))
    return None
def inside(p, poly):
    x, z = p; c = False
    for (x1, z1), (x2, z2) in zip(poly, poly[1:] + poly[:1]):
        if (z1 > z) != (z2 > z) and x < (x2 - x1) * (z - z1) / (z2 - z1) + x1: c = not c
    return c
def sample(pts, step):
    for a, b in zip(pts, pts[1:]):
        n = max(1, int(math.dist(a, b) / step))
        for k in range(n): t = k / n; yield (a[0] + t * (b[0] - a[0]), a[1] + t * (b[1] - a[1]))
    yield pts[-1]
def grid(poly, step):
    xs = [p[0] for p in poly]; zs = [p[1] for p in poly]
    x = min(xs) + step / 2
    while x < max(xs):
        z = min(zs) + step / 2
        while z < max(zs):
            if inside((x, z), poly): yield (x, z)
            z += step
        x += step

def validate(W):
    rep = Report()
    req = ['schemaVersion', 'units', 'axes', 'heights', 'roadClasses', 'river', 'roads', 'bridges', 'junctions', 'districts', 'parks', 'pois', 'spawns', 'validation']
    missing = [k for k in req if k not in W]
    rep.rule('V1', 'Required top-level fields present', [f'missing "{k}"' for k in missing])
    if missing: return rep

    roads = W['roads']; J = W['junctions']; B = W['bridges']
    road_by = {r['id']: r for r in roads}; j_by = {j['id']: j for j in J}
    pts = {r['id']: [P(p) for p in r['points']] for r in roads}
    classes = {c['name']: c for c in W['roadClasses']}
    river = [P(p) for p in W['river']['centreline']]; half = W['river']['width'] / 2; wall = W['river']['wallThickness']
    rdist = lambda p: dist_to_line(p, river)

    # V2 IDs unique + well formed
    f = []
    for kind, items, pat in [('road', roads, None), ('junction', J, r'^J\d{2}$'), ('district', W['districts'], r'^D_[A-Z_]+$'),
                             ('poi', W['pois'], None), ('spawn', W['spawns'], r'^SP_[A-Z0-9_]+$')]:
        ids = [i['id'] for i in items]
        f += [f'duplicate {kind} id {i}' for i in sorted({i for i in ids if ids.count(i) > 1})]
        if pat: f += [f'bad {kind} id "{i}"' for i in ids if not re.match(pat, i)]
    for r in roads:
        want = r'^B\d$' if r['roadClass'] == 'bridge' else r'^R\d{2}$'
        if not re.match(want, r['id']): f.append(f'road {r["id"]} ({r["roadClass"]}) should match {want}')
        if r['roadClass'] not in classes: f.append(f'road {r["id"]} has unknown class {r["roadClass"]}')
        if len(r['points']) < 2: f.append(f'road {r["id"]} has fewer than 2 points')
    rep.rule('V2', 'IDs unique, well formed; every road has a known class and ≥ 2 points', f)

    # V3 every reference exists
    f = []
    for r in roads: f += [f'{r["id"]} lists unknown junction {x}' for x in r['junctions'] if x not in j_by]
    for j in J:
        f += [f'{j["id"]} lists unknown road {x}' for x in j['roads'] if x not in road_by]
        f += [f'{j["id"]} leg uses unknown road {l["road"]}' for l in j.get('legs', []) if l['road'] not in road_by]
    for b in B:
        rid = b.get('roadId', b['id'])
        if rid not in road_by: f.append(f'bridge detail {b["id"]} references unknown road {rid}')
        elif road_by[rid]['roadClass'] != 'bridge': f.append(f'bridge detail {b["id"]} references non-bridge road {rid}')
        for k in ('northJunction', 'southJunction'):
            if b[k] not in j_by: f.append(f'bridge {b["id"]} {k} {b[k]} unknown')
    for x in W['validation'].get('referenceRoutes', []):
        pass
    rep.rule('V3', 'Every referenced ID exists (road↔junction↔bridge)', f)
    if f: return rep

    # V4 two-way membership
    f = []
    for r in roads:
        f += [f'{r["id"]} lists {x}, but {x} does not list {r["id"]}' for x in r['junctions'] if r['id'] not in j_by[x]['roads']]
    for j in J:
        f += [f'{j["id"]} lists {x}, but {x} does not list {j["id"]}' for x in j['roads'] if j['id'] not in road_by[x]['junctions']]
    rep.rule('V4', 'Junction membership agrees in both directions', f)

    # V5 junctions lie on every road they list; and no road passes a junction without listing it
    f = []
    for j in J:
        p = P(j)
        for rid in j['roads']:
            d = dist_to_line(p, pts[rid])
            if d > TOL_ON_ROAD: f.append(f'{j["id"]} ({p[0]}, {p[1]}) is {d:.3f} m off {rid}')
        for r in roads:
            if r['id'] not in j['roads'] and dist_to_line(p, pts[r['id']]) <= TOL_ON_ROAD:
                f.append(f'{j["id"]} lies on {r["id"]} but does not list it')
    worst = max(dist_to_line(P(j), pts[rid]) for j in J for rid in j['roads'])
    rep.rule('V5', f'Every junction lies on each road it lists (≤ {TOL_ON_ROAD} m), and lists every road it lies on', f, f'worst {worst:.3f} m')

    # V6 legs / arms: computed from geometry, compared with declared values
    f = []; hist = {}
    for j in J:
        p = P(j); legs = []
        for rid in j['roads']:
            c, _ = chainage(p, pts[rid]); L = poly_len(pts[rid])
            if c > TOL_CHAINAGE: legs.append((rid, 'start'))
            if c < L - TOL_CHAINAGE: legs.append((rid, 'end'))
        arms = len(legs); hist[arms] = hist.get(arms, 0) + 1
        if j['arms'] != arms: f.append(f'{j["id"]} declares {j["arms"]} arms; geometry gives {arms}')
        if 'legs' in j:
            decl = sorted((l['road'], l['toward']) for l in j['legs'])
            if decl != sorted(legs): f.append(f'{j["id"]} legs {decl} ≠ geometry {sorted(legs)}')
        if not 2 <= arms <= 4: f.append(f'{j["id"]} has {arms} arms (allowed 2–4)')
        if arms == 2 and len(j['roads']) != 2: f.append(f'{j["id"]} is a 2-arm junction but is not two roads meeting end to end')
    rep.rule('V6', 'Arms = legs counted from geometry (through road = 2, road ending here = 1); 2–4 arms', f,
             'arms: ' + ', '.join(f'{k}-arm × {v}' for k, v in sorted(hist.items())))

    # V7 junction order, chainage, ends, spacing (no dead ends)
    f = []
    for r in roads:
        ch = [chainage(P(j_by[x]), pts[r['id']])[0] for x in r['junctions']]
        if ch != sorted(ch): f.append(f'{r["id"]} junctions are not in order along the road')
        if 'junctionChainage' in r:
            for x, c_decl, c in zip(r['junctions'], r['junctionChainage'], ch):
                if abs(c_decl - c) > TOL_CHAINAGE: f.append(f'{r["id"]} chainage of {x}: declared {c_decl}, actual {c:.2f}')
        L = poly_len(pts[r['id']])
        # The road's actual end points must coincide with its first and last junctions (not merely project onto them)
        if not r['junctions'] or math.dist(pts[r['id']][0], P(j_by[r['junctions'][0]])) > TOL_ON_ROAD:
            f.append(f'{r["id"]} start {pts[r["id"]][0]} is not a junction (dead end)')
        if not r['junctions'] or math.dist(pts[r['id']][-1], P(j_by[r['junctions'][-1]])) > TOL_ON_ROAD:
            f.append(f'{r["id"]} end {pts[r["id"]][-1]} is not a junction (dead end)')
        gaps = [b - a for a, b in zip(ch, ch[1:])]
        f += [f'{r["id"]} has junctions only {g:.1f} m apart' for g in gaps if g < MIN_JUNCTION_GAP]
    rep.rule('V7', f'Junctions in order along each road; both road ends are junctions; ≥ {MIN_JUNCTION_GAP:g} m apart', f)

    # V8 lengths and widths
    f = []
    for r in roads:
        L = poly_len(pts[r['id']])
        if abs(L - r['lengthM']) > TOL_LENGTH: f.append(f'{r["id"]} lengthM {r["lengthM"]} vs polyline {L:.2f}')
        cw = classes[r['roadClass']]['width']
        if r['width'] != cw and r['id'] not in W.get('widthOverrides', {}) and r['roadClass'] != 'bridge':
            f.append(f'{r["id"]} width {r["width"]} ≠ class width {cw} and no override declared')
    rep.rule('V8', f'Declared lengths match polylines (≤ {TOL_LENGTH} m); widths match class or a declared override', f)

    # V9 bridges: endpoints, junctions, real water crossing, measurements
    f = []
    def crossing(bp):
        """Length of the deck centreline over water and over water+wall, plus entry/exit counts."""
        s = list(sample(bp, 0.05)); step = poly_len(bp) / (len(s) - 1)
        wet = [rdist(p) < half for p in s]; walled = [rdist(p) < half + wall for p in s]
        entries = sum(1 for a, b in zip(wet, wet[1:]) if not a and b)
        return sum(wet) * step, sum(walled) * step, entries, wet[0] or walled[0], wet[-1] or walled[-1]
    for b in B:
        rid = b.get('roadId', b['id']); bp = pts[rid]; r = road_by[rid]
        ends = {tuple(bp[0]), tuple(bp[-1])}
        n, s = P(b['northEnd']), P(b['southEnd'])
        if {n, s} != ends: f.append(f'{b["id"]} north/south ends {n}, {s} are not the road\'s end points {sorted(ends)}')
        if n[1] <= s[1]: f.append(f'{b["id"]} "north" end is not north of the "south" end')
        for k, e in (('northJunction', n), ('southJunction', s)):
            jj = j_by[b[k]]
            if math.dist(P(jj), e) > TOL_ON_ROAD: f.append(f'{b["id"]} {k} {b[k]} is {math.dist(P(jj), e):.2f} m from that end')
        water, walls, entries, s_wet, e_wet = crossing(bp)
        if entries != 1: f.append(f'{b["id"]} enters the water {entries} times (must be exactly 1)')
        if s_wet or e_wet: f.append(f'{b["id"]} ends in water or wall')
        if water < 20: f.append(f'{b["id"]} spans only {water:.1f} m of water')
        if abs(b['lengthM'] - r['lengthM']) > TOL_LENGTH: f.append(f'{b["id"]} lengthM {b["lengthM"]} ≠ road lengthM {r["lengthM"]}')
        if abs(b['waterSpanM'] - water) > TOL_LENGTH: f.append(f'{b["id"]} waterSpanM {b["waterSpanM"]} vs measured bank-to-bank {water:.1f}')
        if 'wallToWallM' in b and abs(b['wallToWallM'] - walls) > TOL_LENGTH: f.append(f'{b["id"]} wallToWallM {b["wallToWallM"]} vs measured {walls:.1f}')
        if 'northApproachM' in b and abs(b['northApproachM'] + b['southApproachM'] + b['wallToWallM'] - b['lengthM']) > TOL_LENGTH:
            f.append(f'{b["id"]} approaches + wallToWall ≠ length')
    rep.rule('V9', 'Bridges: ends = road ends = stated junctions; one clean water crossing; spans measured bank-to-bank', f)

    # V10 water: only bridges may enter the river corridor; ordinary roads keep clearance
    f = []; worst = 1e9
    for r in roads:
        if r['roadClass'] == 'bridge': continue
        clear = min(rdist(p) for p in sample(pts[r['id']], 1.0)) - half - wall - r['width'] / 2
        worst = min(worst, clear)
        if clear < WALL_CLEAR: f.append(f'{r["id"]} edge is {clear:.1f} m from the embankment wall (need ≥ {WALL_CLEAR:g})')
    for i, r in enumerate(roads):  # no road may cross another except at a listed junction
        for r2 in roads[i + 1:]:
            for a, b in zip(pts[r['id']], pts[r['id']][1:]):
                for c, d in zip(pts[r2['id']], pts[r2['id']][1:]):
                    x = seg_intersect(a, b, c, d)
                    if x and min(math.dist(x, P(j)) for j in J) > TOL_ON_ROAD * 5: f.append(f'{r["id"]} crosses {r2["id"]} at {tuple(round(v, 1) for v in x)} with no junction')
    rep.rule('V10', 'Only bridges enter the river; roads never cross without a junction', f, f'closest ordinary road edge to a wall: {worst:.1f} m')

    # V11 connectivity over legs
    parent = {j['id']: j['id'] for j in J}
    def find(a):
        while parent[a] != a: parent[a] = parent[parent[a]]; a = parent[a]
        return a
    for r in roads:
        for a, b in zip(r['junctions'], r['junctions'][1:]): parent[find(a)] = find(b)
    pieces = len({find(j['id']) for j in J})
    rep.rule('V11', 'Road network is one connected piece', [] if pieces == 1 else [f'{pieces} separate pieces'])

    # V12 polygons: districts vs water/each other; parks vs roads/water
    f = []
    dpolys = {d['id']: [P(p) for p in d['polygon']] for d in W['districts']}
    for k, poly in dpolys.items():
        wet = [p for p in grid(poly, 2.0) if rdist(p) < half + wall - POLY_CLEAR]
        if wet: f.append(f'district {k} overlaps water/wall by ≈ {len(wet) * 4} m² (e.g. near {tuple(round(v) for v in wet[0])})')
    ks = list(dpolys)
    for i in range(len(ks)):
        for k2 in ks[i + 1:]:
            ov = [p for p in grid(dpolys[ks[i]], 4.0) if inside(p, dpolys[k2])]
            if ov: f.append(f'districts {ks[i]} and {k2} overlap by ≈ {len(ov) * 16} m²')
    for pk in W['parks']:
        poly = [P(p) for p in pk['polygon']]
        for p in grid(poly, 2.0):
            hit = [r['id'] for r in roads if dist_to_line(p, pts[r['id']]) < r['width'] / 2 + classes[r['roadClass']]['pavement']]
            if hit: f.append(f'park {pk["name"]} overlaps road/pavement of {hit[0]} near {tuple(round(v) for v in p)}'); break
        if any(rdist(p) < half + wall for p in grid(poly, 2.0)): f.append(f'park {pk["name"]} overlaps water')
    rep.rule('V12', 'Districts: no water, no overlaps. Parks: clear of roads, pavements and water', f)

    # V13 POIs and spawns
    f = []
    def nearest_road(p): return min((dist_to_line(p, pts[r['id']]) - r['width'] / 2, r['id']) for r in roads)
    for p in W['pois']:
        d, rid = nearest_road(P(p))
        if d < 0 and p['kind'] != 'landmark': f.append(f'POI {p["id"]} sits on carriageway {rid}')
        if rdist(P(p)) < half + wall: f.append(f'POI {p["id"]} is in the river')
    for s in W['spawns']:
        d, rid = nearest_road(P(s))
        if s['id'].startswith('SP_POLICE'):
            dj = min(math.dist(P(s), P(j)) for j in J)
            if d > 0: f.append(f'{s["id"]} is not on a carriageway')
            if dj < POLICE_SPAWN_GAP: f.append(f'{s["id"]} is {dj:.0f} m from a junction (need ≥ {POLICE_SPAWN_GAP:g})')
        elif d > 12: f.append(f'{s["id"]} is {d:.0f} m from any road')
        if not 0 <= s['yaw'] < 360: f.append(f'{s["id"]} yaw {s["yaw"]} out of range')
    rep.rule('V13', 'POIs off the carriageway (landmarks excepted); spawns on/next to roads; police spawns clear of junctions', f)

    # V14 existing street footprint
    f = []
    es = W['existingStreet']; cx, cz, hx, hz = es['centreX'], es['centreZ'], es['sizeX'] / 2, es['sizeZ'] / 2
    rect = [(cx - hx, cz - hz), (cx + hx, cz - hz), (cx + hx, cz + hz), (cx - hx, cz + hz)]
    rep_road = next((r for r in roads if r['name'] == es['replacesRoad']), None)
    if not rep_road: f.append(f'replacesRoad "{es["replacesRoad"]}" not found')
    for r in roads:
        if rep_road and r['id'] == rep_road['id']: continue
        if any(inside(p, rect) for p in sample(pts[r['id']], 1.0)): f.append(f'{r["id"]} passes through the existing street footprint')
    rep.rule('V14', 'Only the replaced road passes through the existing street footprint', f)

    # V15 summary counts are computed, never trusted
    f = []; v = W['validation']
    n_bridge = sum(1 for r in roads if r['roadClass'] == 'bridge')
    computed = dict(roadEntries=len(roads), ordinaryRoads=len(roads) - n_bridge, bridgeRoads=n_bridge, bridgeDetails=len(B),
                    junctions=len(J), connectedPieces=pieces)
    for k, val in computed.items():
        if k in v and v[k] != val: f.append(f'validation.{k} says {v[k]}, computed {val}')
    if 'roads' in v and 'roadEntries' not in v: f.append(f'validation.roads = {v["roads"]} is ambiguous (25 ordinary + 5 bridge roads?) — use roadEntries / ordinaryRoads / bridgeRoads')
    if n_bridge != len(B): f.append(f'{n_bridge} bridge roads but {len(B)} bridge details')
    rep.rule('V15', 'Summary counts match the arrays and are unambiguous', f,
             ', '.join(f'{k}={val}' for k, val in computed.items()))

    # V16 documented semantics present (v1.1+)
    f = []
    if str(W['schemaVersion']) < '1.1': f.append(f'schemaVersion {W["schemaVersion"]}: legs, chainage and bridge measurement definitions are not declared in the file')
    else:
        for k in ('arms', 'legs', 'junctionChainage', 'waterSpanM', 'wallToWallM', 'roadEntries'):
            if k not in W.get('definitions', {}): f.append(f'definition of "{k}" missing')
    rep.rule('V16', 'Field meanings are declared in the file (schema ≥ 1.1)', f)
    return rep

if __name__ == '__main__':
    path = sys.argv[1] if len(sys.argv) > 1 else 'LundunWorld.json'
    W = json.load(open(path))
    print(f'Validating {path}  (schema {W.get("schemaVersion", W.get("version", "?"))})\n')
    sys.exit(0 if validate(W).print() else 1)
